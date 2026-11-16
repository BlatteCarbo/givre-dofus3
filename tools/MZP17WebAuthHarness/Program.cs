using GivreClone.App.Services;

int checks = 0;
void Check(bool value, string reason)
{
    if (!value) throw new Exception("MZP17 WebAuth: " + reason);
    checks++;
}

DirectoryInfo? workspace = new(AppContext.BaseDirectory);
while (workspace is not null && !File.Exists(
    Path.Combine(workspace.FullName, "GivreClone.App", "GivreClone.App.csproj")))
    workspace = workspace.Parent;
if (workspace is null) throw new Exception("Historical project root missing");
string build = Path.Combine(workspace.FullName, "GivreClone.App", "bin",
    "Release", "net10.0", "win-x64");
string vendor = Path.Combine(workspace.FullName, "vendor", "GivreWebAuth131");
Check(Directory.Exists(build) && Directory.Exists(vendor), "staged build and provenance paths");
Check(MZP17WebAuthIntegrity.TryAcquire("") is null, "empty location rejected");
Check(MZP17WebAuthIntegrity.TryAcquire(Path.Combine(build, "unknown")) is null,
    "absent helper rejected");
using (var companion = MZP17WebAuthIntegrity.TryAcquire(build))
{
    Check(companion is not null, "real App build contains exact verified companion");
    Check(companion!.ExecutablePath.EndsWith(
        Path.Combine("WebAuth", "GivreLauncher.WebAuth.exe"), StringComparison.OrdinalIgnoreCase),
        "verified helper is inside the build WebAuth directory");
}

// Isolate the mutability tests to a disposable copy of product code files.
string scratch = Path.Combine(Path.GetTempPath(),
    "MZP17-WebAuth-" + Guid.NewGuid().ToString("N"));
string target = Path.Combine(scratch, "WebAuth");
Directory.CreateDirectory(target);
try
{
    foreach (string file in Directory.EnumerateFiles(vendor, "*", SearchOption.AllDirectories))
    {
        string rel = Path.GetRelativePath(vendor, file);
        string output = Path.Combine(target, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.Copy(file, output);
    }
    using (var companion = MZP17WebAuthIntegrity.TryAcquire(scratch))
        Check(companion is not null, "untampered source clone admitted");

    string config = Path.Combine(target, "GivreLauncher.WebAuth.runtimeconfig.json");
    string saved = File.ReadAllText(config);
    File.AppendAllText(config, " ");
    Check(MZP17WebAuthIntegrity.TryAcquire(scratch) is null,
        "one changed byte in .NET runtimeconfig fails closed");
    File.WriteAllText(config, saved);
    using (var restored = MZP17WebAuthIntegrity.TryAcquire(scratch))
        Check(restored is not null, "byte-exact restoration re-admitted");

    string dll = Path.Combine(target, "Microsoft.Web.WebView2.WinForms.dll");
    byte[] before = File.ReadAllBytes(dll);
    byte[] tampered = (byte[])before.Clone();
    tampered[tampered.Length / 2] ^= 0x20;
    File.WriteAllBytes(dll, tampered);
    Check(MZP17WebAuthIntegrity.TryAcquire(scratch) is null,
        "modified managed dependency rejected");
    File.WriteAllBytes(dll, before);

    string exe = Path.Combine(target, "GivreLauncher.WebAuth.exe");
    File.Delete(exe);
    Check(MZP17WebAuthIntegrity.TryAcquire(scratch) is null,
        "missing helper is unavailable, not a fake login");
}
finally
{
    string root = Path.GetFullPath(Path.GetTempPath());
    string full = Path.GetFullPath(scratch);
    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(full).StartsWith("MZP17-WebAuth-", StringComparison.Ordinal) ||
        (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
        throw new IOException("Unsafe disposable TEST cleanup");
    Directory.Delete(full, recursive: true);
}
Console.WriteLine($"MZP17 authentic WebAuth sidecar integrity: PASS ({checks} offline checks). " +
    "No original client, auth, browser, account, or network touched.");
