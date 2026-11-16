using System.Diagnostics;
using GivreLauncher.Core.Launch;

// This test deliberately creates ONLY its own disposable child executable.
// No original GivreClone process, game account, launcher installation, or client.
if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Windows-only owned-child regression.");
var childDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "MZP9OwnedChild", "bin", "Release", "net10.0"));
var allowed = new[] { "MZP9OwnedChild.exe", "MZP9OwnedChild.dll",
    "MZP9OwnedChild.runtimeconfig.json" };
foreach (var name in allowed)
    if (!File.Exists(Path.Combine(childDir, name)))
        throw new FileNotFoundException("Compile MZP9OwnedChild Release first.", name);
var ownedRoot = Path.Combine(Path.GetTempPath(), "MZP9Owned_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(ownedRoot);
var dummyExe = Path.Combine(ownedRoot, "Dofus.exe");
try
{
    File.Copy(Path.Combine(childDir, allowed[0]), dummyExe);
    foreach (var name in allowed.Skip(1)) File.Copy(Path.Combine(childDir, name), Path.Combine(ownedRoot, name));
    var authorized = new HarnessTestAuthority();
    var launcher = new GameLauncher(authorized);
    if (!launcher.HasIndependentTestAuthority) throw new Exception("test authority absent");
    using var started = launcher.Launch(Guid.NewGuid().ToString(), 2, dummyExe);
    if (!authorized.CheckedRecipe || !authorized.CheckedSecondAdmission)
        throw new Exception("both authority checks mandatory");
    if (!MZP9OwnedClientRegistry.IsOwnedAndLive(started.Id))
        throw new Exception("newly created owned child was not retained by handle");
    if (MZP9OwnedClientRegistry.IsOwnedAndLive(Environment.ProcessId) ||
        MZP9OwnedClientRegistry.TryStopOwned(Environment.ProcessId))
        throw new Exception("unowned harness process was wrongly eligible for termination");
    if (!MZP9OwnedClientRegistry.TryStopOwned(started.Id))
        throw new Exception("cannot terminate exact owned test child by retained handle");
    started.WaitForExit(3000);
    if (!started.HasExited || MZP9OwnedClientRegistry.TryStopOwned(started.Id))
        throw new Exception("terminated child still admitted / repeated stop allowed");
    Console.WriteLine("MZP9 owned Windows child: PASS (6 checks). Only disposable TEST helper started/stopped.");
}
finally
{
    var path = Path.GetFullPath(ownedRoot);
    var parent = Path.GetFullPath(Path.GetTempPath());
    if (!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(path).StartsWith("MZP9Owned_", StringComparison.Ordinal))
        throw new IOException("Unsafe TEST helper cleanup path.");
    Directory.Delete(path, recursive: true);
}

internal sealed class HarnessTestAuthority : IIndependentTestGameLaunchAuthority
{
    public bool CheckedRecipe { get; private set; }
    public bool CheckedSecondAdmission { get; private set; }
    public ProcessStartInfo? CreateAuthorizedStartInfo(string path, string exeSha256, string sessionHash,
        int connectionType, string? seed)
    {
        CheckedRecipe = path.EndsWith("Dofus.exe", StringComparison.OrdinalIgnoreCase)
            && exeSha256.Length == 64 && Guid.TryParse(sessionHash, out _)
            && connectionType == 2 && seed is null;
        return !CheckedRecipe ? null : new ProcessStartInfo(path)
        { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
    }
    public bool ConfirmStillAuthorized(string path, string sha, string hash)
    {
        CheckedSecondAdmission = CheckedRecipe && sha.Length == 64 && Guid.TryParse(hash, out _);
        return CheckedSecondAdmission;
    }
}
