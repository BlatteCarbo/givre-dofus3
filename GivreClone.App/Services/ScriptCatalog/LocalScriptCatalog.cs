using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.ScriptCatalog;

/// <summary>
/// Read-only local registry. No script execution, imports, file writes or network calls.
/// A catalog root should be owned by Givre and not concurrently writable by untrusted actors.
/// </summary>
public sealed class LocalScriptCatalog
{
    public const int SchemaVersion = 1;
    public const int MaxLuaBytes = 512 * 1024;
    public const int MaxManifestBytes = 32 * 1024;
    private const int MaxPackageCount = 200;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex IdPattern = new("\\A[a-z0-9][a-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("\\A[0-9]+\\.[0-9]+\\.[0-9]+\\z", RegexOptions.CultureInvariant);
    private static readonly Regex PathSegment = new("\\A[A-Za-z0-9_-][A-Za-z0-9_.-]*\\z", RegexOptions.CultureInvariant);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    private readonly string _root;

    public LocalScriptCatalog(string catalogRoot)
    {
        if (string.IsNullOrWhiteSpace(catalogRoot)) throw new ArgumentException("Catalog root is required", nameof(catalogRoot));
        _root = Path.GetFullPath(catalogRoot);
    }

    public IReadOnlyList<ScriptReviewReport> Scan()
    {
        if (!Directory.Exists(_root)) return [];
        try
        {
            if (IsLink(_root)) return [Invalid("<catalog>", "Catalog root is a symbolic link or reparse point")];
            // Enumerate only immediate, bounded package folders; never recurse through untrusted paths.
            return Directory.EnumerateDirectories(_root)
                .Take(MaxPackageCount)
                .Select(Path.GetFileName)
                .Where(id => id is not null)
                .Select(id => Inspect(id!))
                .ToArray();
        }
        catch (IOException ex) { return [Invalid("<catalog>", ex.Message)]; }
        catch (UnauthorizedAccessException ex) { return [Invalid("<catalog>", ex.Message)]; }
    }

    public ScriptReviewReport Inspect(string? packageId)
    {
        string id = packageId ?? "";
        if (!IdPattern.IsMatch(id) || IsReservedDevice(id))
            return Invalid(id, "Invalid package id: expected lowercase ASCII non-device identifier");
        try
        {
            if (!Directory.Exists(_root)) return Invalid(id, "Catalog root does not exist");
            EnsurePlain(_root);
            string packageDir = ResolveContained(_root, id);
            EnsurePlain(packageDir);
            string manifestPath = ResolveContained(packageDir, "manifest.json");
            EnsurePlain(manifestPath);
            byte[] manifestBytes = ReadLimited(manifestPath, MaxManifestBytes);
            string json = StrictUtf8.GetString(manifestBytes);
            ValidateUniqueProperties(json);
            ScriptPackageManifest? manifest = JsonSerializer.Deserialize<ScriptPackageManifest>(json, JsonOptions);
            if (manifest is null) return Invalid(id, "Empty manifest");

            var findings = ValidateManifest(manifest, id);
            if (findings.Count > 0) return new(id, ScriptReviewStatus.Invalid, manifest, [], findings, null);

            string entry = ResolveContained(packageDir, manifest.EntryPoint);
            EnsurePlain(entry);
            byte[] lua = ReadLimited(entry, MaxLuaBytes);
            byte[] expected = Convert.FromHexString(manifest.Sha256);
            byte[] actual = SHA256.HashData(lua);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return new(id, ScriptReviewStatus.Invalid, manifest, [], ["Lua SHA-256 mismatch"], null);

            var capabilities = manifest.Capabilities.Select(ScriptCapabilityPolicy.Review).ToArray();
            var blocked = capabilities.Where(c => c.Risk == ScriptCapabilityRisk.Denied).ToArray();
            if (blocked.Length > 0)
                return new(id, ScriptReviewStatus.Blocked, manifest, capabilities,
                    ["Denied or unknown capabilities: " + string.Join(", ", blocked.Select(c => c.Name))], null);

            string preview = StrictUtf8.GetString(lua);
            if (preview.Length > 2000) preview = preview[..2000] + "\n… [preview truncated]";
            bool needsGrant = capabilities.Any(c => c.Risk == ScriptCapabilityRisk.RequiresGrant);
            return new(id,
                needsGrant ? ScriptReviewStatus.NeedsPermissionReview : ScriptReviewStatus.VerifiedForPreview,
                manifest, capabilities,
                needsGrant ? ["Declared capabilities require explicit grants in a future runtime adapter"] : [],
                preview);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
            or JsonException or DecoderFallbackException or NotSupportedException)
        {
            return Invalid(id, $"Package rejected: {ex.Message}");
        }
    }

    private static List<string> ValidateManifest(ScriptPackageManifest value, string id)
    {
        List<string> findings = [];
        if (value.SchemaVersion != SchemaVersion) findings.Add("Unsupported manifest schema version");
        if (!string.Equals(value.Id, id, StringComparison.Ordinal)) findings.Add("Package id differs from folder name");
        if (!VersionPattern.IsMatch(value.Version ?? "")) findings.Add("Version must be MAJOR.MINOR.PATCH");
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 120) findings.Add("Missing or excessive name");
        if ((value.Description?.Length ?? 0) > 1000) findings.Add("Description too long");
        if (!string.Equals(value.ApiVersion, GivreScriptApiV1.Version, StringComparison.Ordinal))
            findings.Add("Unsupported Givre script API version");
        if (value.EntryPoint is null || !IsSafeRelativeLua(value.EntryPoint)) findings.Add("Unsafe Lua entry point path");
        if (value.Sha256 is null || value.Sha256.Length != 64 || !value.Sha256.All(Uri.IsHexDigit))
            findings.Add("SHA-256 must have exactly 64 hexadecimal characters");
        if (value.Capabilities is null || value.Capabilities.Length > 32 ||
            value.Capabilities.Distinct(StringComparer.Ordinal).Count() != value.Capabilities.Length)
            findings.Add("Invalid, excessive or duplicate capability declarations");
        return findings;
    }

    private static bool IsSafeRelativeLua(string path)
    {
        if (path.Length is < 5 or > 240 || !path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Contains('\0')) return false;
        string[] segments = path.Split('/');
        return segments.Length <= 5 && segments.All(s => s is not ("." or "..")
            && !s.Contains("..") && !s.EndsWith('.') && !IsReservedDevice(s) && PathSegment.IsMatch(s));
    }

    private static bool IsReservedDevice(string segment)
    {
        string basename = segment.Split('.')[0];
        if (basename.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            basename.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            basename.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            basename.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
        return basename.Length == 4 && basename[3] is >= '1' and <= '9'
            && (basename.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                basename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveContained(string parent, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(parent, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, PathComparison))
            throw new IOException("Path escapes its package directory");
        return path;
    }

    private void EnsurePlain(string path)
    {
        // Only inspect components inside the trusted catalog tree (including the root itself).
        if (IsLink(_root)) throw new IOException("Catalog root is a link");
        if (string.Equals(path, _root, PathComparison)) return;
        string relative = Path.GetRelativePath(_root, path);
        string cursor = _root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0 || part == "." || part == "..") throw new IOException("Invalid path segment");
            cursor = Path.Combine(cursor, part);
            if (IsLink(cursor)) throw new IOException("Symbolic links and reparse points are prohibited");
        }
    }

    private static bool IsLink(string path)
    {
        // Attributes also catch directory junctions; File/Directory.Exists may return false for broken links.
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static byte[] ReadLimited(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes) throw new IOException("File exceeds catalog size limit");
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk)) != 0)
        {
            if (buffer.Length + read > maxBytes) throw new IOException("File exceeds catalog size limit");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static void ValidateUniqueProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Manifest must be a JSON object");
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var prop in document.RootElement.EnumerateObject())
            if (!seen.Add(prop.Name)) throw new JsonException("Duplicate manifest field: " + prop.Name);
    }

    private static ScriptReviewReport Invalid(string id, string message) =>
        new(id, ScriptReviewStatus.Invalid, null, [], [message], null);
}
