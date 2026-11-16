using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.EquipmentSetData;

/// <summary>
/// Sealed, source-owned local JSON only. No network or SQLite scraping is performed.
/// Each set's members must agree with every item's explicitly supplied SetId.
/// </summary>
public static class EquipmentSetCatalogReader
{
    public const int MaxManifestBytes = 65_536;
    public const int MaxJsonBytes = 24 * 1024 * 1024;
    public const int MaxSets = 10_000;
    public const int MaxItems = 100_000;
    public const int MaxMembersPerSet = 64;
    public const int MaxTotalMembers = 100_000;
    private static readonly Regex GameVersionPattern =
        new(@"^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Requires a caller-selected directory containing manifest.json and equipment-sets.json.
    /// Refuses public DofusDB API data, unsupported formats/versions, symlinks,
    /// tampering, missing/duplicate set members and contradictory itemSetId.
    /// </summary>
    public static EquipmentSetCatalog LoadDirectory(string? folder, string? expectedGameVersion = null)
    {
        if (string.IsNullOrWhiteSpace(folder)) return EquipmentSetCatalog.Rejected("Missing catalog directory.");
        if (expectedGameVersion is not null && !ValidGameVersion(expectedGameVersion))
            return EquipmentSetCatalog.Rejected("Invalid expected game version.");
        try
        {
            string root = Path.GetFullPath(folder);
            CheckDirectoryChain(root);
            byte[] metadata = ReadBounded(Path.Combine(root, "manifest.json"), MaxManifestBytes);
            using var manifest = ParseJson(metadata, maxDepth: 8);
            var manifestNode = manifest.RootElement;
            RequireFields(manifestNode, ["schemaVersion", "gameVersion", "source", "permission", "format", "asset"]);
            if (ReadInt(manifestNode, "schemaVersion", 1, 1) != 1)
                throw new InvalidDataException("Unsupported manifest schemaVersion.");
            string version = ReadText(manifestNode, "gameVersion", 32);
            if (!ValidGameVersion(version) ||
                expectedGameVersion is not null && expectedGameVersion != version)
                throw new InvalidDataException("Invalid or mismatched game version.");
            string source = ReadText(manifestNode, "source", 64);
            if (source is not ("local-extraction" or "privately-licensed-offline"))
                throw new InvalidDataException("Public/unknown source not admitted.");
            string permission = ReadText(manifestNode, "permission", 64);
            if (permission != "owner-authorized")
                throw new InvalidDataException("Owner authorization claim required.");
            if (ReadText(manifestNode, "format", 16) != "json")
                throw new InvalidDataException("Unsupported data format.");
            var asset = manifestNode.GetProperty("asset");
            RequireFields(asset, ["file", "bytes", "sha256"]);
            if (ReadText(asset, "file", 64) != "equipment-sets.json")
                throw new InvalidDataException("Unexpected asset name or path.");
            long expectedBytes = ReadInt(asset, "bytes", 2, MaxJsonBytes);
            string expectedSha = ReadText(asset, "sha256", 64);
            if (expectedSha.Length != 64 || expectedSha.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Invalid SHA-256.");
            string assetPath = Path.Combine(root, "equipment-sets.json");
            var file = new FileInfo(assetPath);
            if (!file.Exists || file.Length != expectedBytes ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Missing, linked or size-mismatched asset.");
            byte[] raw = ReadBounded(assetPath, MaxJsonBytes);
            if (raw.LongLength != expectedBytes ||
                !Convert.ToHexString(SHA256.HashData(raw)).Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256/size mismatch.");
            var items = new List<EquipmentSetItem>();
            var sets = new List<EquipmentSet>();
            using (var data = ParseJson(raw, maxDepth: 12))
                ReadCatalog(data.RootElement, sets, items);
            if (file.Length != expectedBytes ||
                !HashFile(assetPath).Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Asset changed while importing.");
            Validate(sets, items);
            return new EquipmentSetCatalog(
                new EquipmentSetSource(version, source, permission, expectedSha.ToLowerInvariant(), true),
                sets, items, true, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   JsonException or InvalidDataException or ArgumentException or
                                   NotSupportedException or OverflowException or FormatException or
                                   InvalidOperationException or System.Security.SecurityException)
        {
            return EquipmentSetCatalog.Rejected("Equipment catalog rejected: " +
                (ex is InvalidDataException ? ex.Message : ex.GetType().Name));
        }
    }

    private static void CheckDirectoryChain(string folder)
    {
        var current = new DirectoryInfo(folder);
        if (!current.Exists) throw new InvalidDataException("Directory does not exist.");
        // Reject any resolved parent-directory links as well as links in the
        // dataset folder itself. No user-controlled path can escape through a link.
        for (DirectoryInfo? dir = current; dir is not null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked directory is not accepted.");
    }

    private static byte[] ReadBounded(string path, int bound)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 2 ||
            file.Length > bound || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Missing, linked or oversized file.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.SequentialScan);
        if (stream.Length != file.Length) throw new InvalidDataException("File length changed.");
        byte[] result = new byte[checked((int)stream.Length)];
        stream.ReadExactly(result);
        return result;
    }

    private static JsonDocument ParseJson(byte[] bytes, int maxDepth)
    {
        int bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        return JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, bom, bytes.Length - bom),
            new JsonDocumentOptions
            {
                MaxDepth = maxDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
    }

    private static string HashFile(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65_536, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void RequireFields(JsonElement node, string[] fields)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
            if (!seen.Add(property.Name) || !fields.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException("Duplicate or unsupported JSON field: " + property.Name);
        if (seen.Count != fields.Length) throw new InvalidDataException("Missing JSON field.");
    }

    private static long ReadInt(JsonElement node, string field, long min, long max)
    {
        if (!node.TryGetProperty(field, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out long result) || result < min || result > max)
            throw new InvalidDataException("Invalid integer: " + field);
        return result;
    }

    private static string ReadText(JsonElement node, string field, int max)
    {
        if (!node.TryGetProperty(field, out var property) ||
            property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Expected text: " + field);
        string text = property.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(text) || text.Length > max)
            throw new InvalidDataException("Invalid text length: " + field);
        return text;
    }

    private static string? ReadOptionalName(JsonElement node)
    {
        var name = node.GetProperty("name");
        if (name.ValueKind == JsonValueKind.Null) return null;
        if (name.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Invalid name field.");
        var text = name.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256)
            throw new InvalidDataException("Name cannot be blank or oversized.");
        return text;
    }

    private static bool ValidGameVersion(string version)
        => GameVersionPattern.IsMatch(version) && version.Split('.').All(part =>
            ushort.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));

    private static void ReadCatalog(JsonElement json,
        List<EquipmentSet> sets, List<EquipmentSetItem> items)
    {
        RequireFields(json, ["schemaVersion", "sets", "items"]);
        if (ReadInt(json, "schemaVersion", 1, 1) != 1)
            throw new InvalidDataException("Unsupported dataset schemaVersion.");
        var arraySets = json.GetProperty("sets");
        var arrayItems = json.GetProperty("items");
        if (arraySets.ValueKind != JsonValueKind.Array ||
            arraySets.GetArrayLength() > MaxSets ||
            arrayItems.ValueKind != JsonValueKind.Array ||
            arrayItems.GetArrayLength() > MaxItems)
            throw new InvalidDataException("Invalid/oversized sets or items.");

        int totalMembers = 0;
        foreach (var set in arraySets.EnumerateArray())
        {
            RequireFields(set, ["id", "name", "memberItemIds"]);
            long setId = ReadInt(set, "id", 1, long.MaxValue);
            var members = set.GetProperty("memberItemIds");
            if (members.ValueKind != JsonValueKind.Array ||
                members.GetArrayLength() is < 1 or > MaxMembersPerSet)
                throw new InvalidDataException("Set has missing or oversized member list.");
            totalMembers = checked(totalMembers + members.GetArrayLength());
            if (totalMembers > MaxTotalMembers)
                throw new InvalidDataException("Too many set memberships.");
            var ids = new List<long>(members.GetArrayLength());
            foreach (var member in members.EnumerateArray())
            {
                if (member.ValueKind != JsonValueKind.Number ||
                    !member.TryGetInt64(out long id) || id <= 0)
                    throw new InvalidDataException("Invalid member item ID.");
                ids.Add(id);
            }
            if (ids.Distinct().Count() != ids.Count)
                throw new InvalidDataException("Duplicate member in a set.");
            sets.Add(new EquipmentSet(setId, ReadOptionalName(set), Array.AsReadOnly(ids.ToArray())));
        }

        foreach (var item in arrayItems.EnumerateArray())
        {
            RequireFields(item, ["id", "name", "setId"]);
            long id = ReadInt(item, "id", 1, long.MaxValue);
            var setNode = item.GetProperty("setId");
            long? setId = null;
            if (setNode.ValueKind != JsonValueKind.Null)
            {
                if (setNode.ValueKind != JsonValueKind.Number ||
                    !setNode.TryGetInt64(out long number) || number <= 0)
                    throw new InvalidDataException("Invalid itemSetId.");
                setId = number;
            }
            items.Add(new EquipmentSetItem(id, ReadOptionalName(item), setId));
        }
    }

    private static void Validate(List<EquipmentSet> sets, List<EquipmentSetItem> items)
    {
        if (sets.GroupBy(x => x.Id).Any(g => g.Count() != 1) ||
            items.GroupBy(x => x.Id).Any(g => g.Count() != 1))
            throw new InvalidDataException("Duplicate set or item ID.");
        var byItem = items.ToDictionary(i => i.Id);
        var setIds = sets.Select(s => s.Id).ToHashSet();
        var observedMembership = new Dictionary<long, long>();
        foreach (var set in sets)
            foreach (long itemId in set.MemberItemIds)
            {
                if (!byItem.TryGetValue(itemId, out var item))
                    throw new InvalidDataException("Set references unknown item.");
                if (!observedMembership.TryAdd(itemId, set.Id))
                    throw new InvalidDataException("Item belongs to multiple sets.");
                if (item.SetId != set.Id)
                    throw new InvalidDataException("Item.itemSetId contradicts set membership.");
            }
        foreach (var item in items)
        {
            if (item.SetId is not { } id) continue;
            if (!setIds.Contains(id) || !observedMembership.TryGetValue(item.Id, out long actual) ||
                actual != id)
                throw new InvalidDataException("Item has unresolved/inconsistent itemSetId.");
        }
    }
}
