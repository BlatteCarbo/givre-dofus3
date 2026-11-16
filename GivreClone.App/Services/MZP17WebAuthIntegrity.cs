using System;
using System.IO;
using System.Security.Cryptography;

namespace GivreClone.App.Services;

/// <summary>
/// Bind the browser-login companion to the exact code-only distribution
/// extracted from the user's isolated, SHA-verified local Givre package.
/// Never opens the installed Givre app, profiles, passwords, or account data.
/// A lease keeps the executable open read-only through Process.Start.
/// This is code-integrity admission, NOT an authentication or license bypass.
/// </summary>
public static class MZP17WebAuthIntegrity
{
    private sealed record Witness(string RelativePath, long Bytes, string Sha256);

    private static readonly Witness[] Expected =
    {
        new("GivreLauncher.WebAuth.exe", 12666880, "19281A61CB097D15790DFEAF0AC332613A5DAF3DE0502F0AEC9C25864E003DC4"),
        new("GivreLauncher.WebAuth.dll", 76288, "31DDA6E03F6921A66D58BBE85110639D4BA367F94822B264A4E44B7E662BC1A6"),
        new("GivreLauncher.WebAuth.deps.json", 2081, "45CF29C47CA829E906B9BC4CA1981FD7AB80973BF88F79F2074A6AA6D3ECC271"),
        new("GivreLauncher.WebAuth.runtimeconfig.json", 519, "96BC2946DFF8790400D652EC7F8DE7BB043E954980D59470C112788B68A1FBD3"),
        new("Microsoft.Web.WebView2.Core.dll", 567896, "616157863B1CD1F1330B4914DD013ADEF10A4CE8CD819598FD6BD4037C2C1646"),
        new("Microsoft.Web.WebView2.WinForms.dll", 38360, "A2DDFDE024F6A248576A69CE9A54B5407AD915EE8FEC7DFF3A089A60948AC0AE"),
        new("Microsoft.Web.WebView2.Wpf.dll", 51160, "2FE5FBCE16F19324A41AE40EB81FEF4E48A853969185F24AB7902350FEABF536"),
        new("runtimes/win-arm64/native/WebView2Loader.dll", 140368, "D17168D459F16EAE2F944D31B2ECD6714B3447101F6BB531D162445D2BBB1C19"),
        new("runtimes/win-x64/native/WebView2Loader.dll", 165336, "1CBCA1698175936A93D0C4D4148ED355E1B91415C6A4DB6D762F017DE3B6EB9A"),
        new("runtimes/win-x86/native/WebView2Loader.dll", 116184, "DD4C6797FF04625934A14AA7B22D76B847C8B6BB1CCCD7C587EE6FEC6B636121")
    };

    public sealed class VerifiedCompanion : IDisposable
    {
        private readonly FileStream _executableLease;
        internal VerifiedCompanion(string path, FileStream stream)
        {
            ExecutablePath = path;
            _executableLease = stream;
        }
        public string ExecutablePath { get; }
        public void Dispose() => _executableLease.Dispose();
    }

    public static VerifiedCompanion? TryAcquire(string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
            return null;
        FileStream? lockedExecutable = null;
        try
        {
            string parent = Path.GetFullPath(appBaseDirectory);
            string root = Path.GetFullPath(Path.Combine(parent, "WebAuth"));
            if (!root.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(root) || IsReparse(root))
                return null;

            string? exePath = null;
            foreach (Witness witness in Expected)
            {
                string path = Path.GetFullPath(Path.Combine(root,
                    witness.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(path) || IsReparse(path))
                    return null;

                // Reject NTFS junctions in any nested architecture directory.
                string current = root;
                foreach (string component in witness.RelativePath.Split('/')[..^1])
                {
                    current = Path.Combine(current, component);
                    if (!Directory.Exists(current) || IsReparse(current))
                        return null;
                }

                var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.SequentialScan);
                bool retain = false;
                try
                {
                    if (stream.Length != witness.Bytes)
                        return null;
                    string currentSha = Convert.ToHexString(SHA256.HashData(stream));
                    if (!string.Equals(currentSha, witness.Sha256,
                            StringComparison.Ordinal))
                        return null;
                    if (witness.RelativePath == "GivreLauncher.WebAuth.exe")
                    {
                        lockedExecutable = stream;
                        exePath = path;
                        retain = true;
                    }
                }
                finally
                {
                    if (!retain) stream.Dispose();
                }
            }

            if (lockedExecutable is null || exePath is null)
                return null;
            var companion = new VerifiedCompanion(exePath, lockedExecutable);
            lockedExecutable = null;
            return companion;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
        finally
        {
            lockedExecutable?.Dispose();
        }
    }

    private static bool IsReparse(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
