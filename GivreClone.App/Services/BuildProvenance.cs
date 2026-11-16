using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GivreClone.App.Services;

// Directory.Build.props forbids embedding the git SHA, so a build is identified by the SHA-256 of the
// running binary, computed at export time: nothing developer-specific is stamped into the executable.
public sealed record BuildProvenance(string Version, string BinarySha256, string Runtime, string OperatingSystem)
{
    public static BuildProvenance Current => From(typeof(BuildProvenance).Assembly);

    public static BuildProvenance From(Assembly assembly) => new(
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "?",
        FileSha256(assembly.Location),
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription);

    public static string FileSha256(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "indisponible";
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }
        catch (IOException) { return "indisponible"; }
        catch (UnauthorizedAccessException) { return "indisponible"; }
    }
}
