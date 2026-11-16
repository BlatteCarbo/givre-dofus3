using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services.Metamob;

/// <summary>Bornes du contrat public Metamob. La doc du site limite une quantité à 0-30 et un lot à 200.
/// Le validateur du SDK ne borne pas la quantité : on suit la doc, sans tronquer.</summary>
public static class MetamobLimits
{
    public const int MaxQuantity = 30;
    public const int MaxBatch = 200;
    public const int MaxPage = 200;
    public const string BaseUrl = "https://www.metamob.fr/api/v1/";
}

public enum SoulScope { Bag, Chest, Both }

public enum SoulSyncOutcome { Failed = 0, Updated = 1, Skipped = 2 }

public sealed class SoulSyncStop
{
    private int _requested;
    public bool Requested => System.Threading.Volatile.Read(ref _requested) != 0;
    public void Request() => System.Threading.Volatile.Write(ref _requested, 1);
}

public sealed class MetamobServer
{
    public MetamobServer(int id, string name, string gameVersion)
    {
        Id = id;
        Name = name;
        GameVersion = gameVersion ?? "";
    }

    public int Id { get; }
    public string Name { get; }
    public string GameVersion { get; }
    public string Label => GameVersion.Length == 0 ? Name : Name + " · " + GameVersion;
}

public sealed class MetamobQuest
{
    public MetamobQuest(string slug, string characterName, int serverId, string serverName, string typeName)
    {
        Slug = slug;
        CharacterName = characterName ?? "";
        ServerId = serverId;
        ServerName = serverName ?? "";
        TypeName = typeName ?? "";
    }

    public string Slug { get; }
    public string CharacterName { get; }
    public int ServerId { get; }
    public string ServerName { get; }
    public string TypeName { get; }

    public string Label
    {
        get
        {
            var who = CharacterName.Length == 0 ? Slug : CharacterName;
            return TypeName.Length == 0 ? who + " · " + ServerName : who + " · " + TypeName + " · " + ServerName;
        }
    }
}

public sealed record RemoteMonster(int Id, string Name, int Quantity);

public sealed record MonsterPage(System.Collections.Generic.IReadOnlyList<RemoteMonster> Monsters, int? Total);

public readonly record struct MonsterAck(int MonsterId, int Quantity);

public readonly record struct QuantityChange(int MonsterId, string Name, int RemoteQuantity, int LocalQuantity);

public sealed record MetamobSyncRow(SoulSyncOutcome Outcome, int MonsterId, string Name, int RemoteQuantity, int LocalQuantity, string Detail)
{
    public string KindLabel => Outcome switch
    {
        SoulSyncOutcome.Updated => "Mis à jour",
        SoulSyncOutcome.Skipped => "Ignoré",
        _ => "Échec",
    };

    public string Line => KindLabel + " · " + MonsterId.ToString(CultureInfo.InvariantCulture) + " · " + Name;
}

public class MetamobCallException : Exception
{
    public MetamobCallException(int status, string message, bool requestSent) : base(message)
    {
        Status = status;
        RequestSent = requestSent;
    }

    public int Status { get; }
    public bool RequestSent { get; }
}

public sealed class MetamobContractException : MetamobCallException
{
    public MetamobContractException(string message, bool requestSent = false) : base(0, message, requestSent) { }
}

public static class MetamobText
{
    public static bool IsSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length > 128) return false;
        foreach (var c in slug)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-') return false;
        return true;
    }

    public static string Scrub(string? text, string? secret)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (string.IsNullOrEmpty(secret) || secret.Length < 8) return text;
        return text.Replace(secret, "[clé]", StringComparison.Ordinal);
    }

    public static string PublicError(int status, string? body, string secret)
    {
        if (status is 401 or 403) return "Clé refusée.";
        var text = Extract(body);
        if (string.IsNullOrWhiteSpace(text))
            text = status <= 0 ? "Réseau indisponible." : "HTTP " + status.ToString(CultureInfo.InvariantCulture);
        text = Scrub(text, secret);
        return text.Length <= 300 ? text : text.Substring(0, 300);
    }

    private static string? Extract(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String && message.GetString() is { Length: > 0 } written)
                return written;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Object)
            {
                string? errorField = null;
                var parts = new System.Collections.Generic.List<string>();
                foreach (var prop in messages.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String || prop.Value.GetString() is not { Length: > 0 } value) continue;
                    if (prop.NameEquals("error")) errorField = value;
                    else parts.Add(prop.Name + ": " + value);
                }
                if (!string.IsNullOrEmpty(errorField)) return errorField;
                if (parts.Count > 0) return string.Join("; ", parts);
            }
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Lit la clé au moment de l'appel. Ne la journalise pas et ne l'écrit nulle part ailleurs.</summary>
public static class MetamobKeyFile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GivreReplica", "secrets", "metamob.key");

    public static string? TryRead(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return null;
            var text = File.ReadAllText(file).Trim();
            if (text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                text = text.Substring("Bearer ".Length).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
