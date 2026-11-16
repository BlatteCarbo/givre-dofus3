using GivreClone.App.Services;
using System.Diagnostics;

// Offline ONLY. Never call RunInteractiveAsync (no browser, login or network).
// Tests the real production verifier against copies under THIS harness output.
namespace GivreClone.App.Services
{
    internal static class WebAuthRunner
    {
        internal static string MZP18TestCompanionHandshake() => throw new NotSupportedException("Offline only");
    }
}

static class Harness
{
    private static int _passes;
    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
        _passes++;
        Console.WriteLine("PASS " + name);
    }

    public static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]))
        {
            Console.Error.WriteLine("Expected exact output path of reconstructed helper; no browser is launched.");
            return 2;
        }
        var source = Path.GetFullPath(args[0]);
        var own = Path.GetFullPath(AppContext.BaseDirectory);
        var dest = Path.Combine(own, "WebAuthTest");
        if (!string.Equals(Path.GetFullPath(Path.Combine(own, "WebAuthTest")), dest, StringComparison.Ordinal))
            return 3;
        if (Directory.Exists(dest) &&
            File.GetAttributes(dest).HasFlag(FileAttributes.ReparsePoint))
            return 3;
        Directory.CreateDirectory(dest);
        var names = new[]
        {
            "MZP18IsolatedWebAuth.exe", "MZP18IsolatedWebAuth.dll",
            "MZP18IsolatedWebAuth.deps.json", "MZP18IsolatedWebAuth.runtimeconfig.json",
            "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll",
            "Microsoft.Web.WebView2.Wpf.dll", "WebView2Loader.dll"
        };
        foreach (var name in names)
            File.Copy(Path.Combine(source, name), Path.Combine(dest, name), true);
        Check("pinned genuine TEST helper", MZP18IsolatedWebAuthRunner.IsHelperVerified);
        var dll = Path.Combine(dest, "MZP18IsolatedWebAuth.dll");
        var bytes = File.ReadAllBytes(dll);
        bytes[bytes.Length / 2] ^= 0x4;
        File.WriteAllBytes(dll, bytes);
        Check("same-length DLL mutation rejected", !MZP18IsolatedWebAuthRunner.IsHelperVerified);
        File.Copy(Path.Combine(source, "MZP18IsolatedWebAuth.dll"), dll, true);
        Check("repaired DLL accepted", MZP18IsolatedWebAuthRunner.IsHelperVerified);
        var loader = Path.Combine(dest, "WebView2Loader.dll");
        File.Move(loader, loader + ".absent", true);
        Check("missing native loader rejected", !MZP18IsolatedWebAuthRunner.IsHelperVerified);
        File.Move(loader + ".absent", loader, true);
        var exe = Path.Combine(dest, "MZP18IsolatedWebAuth.exe");
        using (var stream = new FileStream(exe, FileMode.Append, FileAccess.Write, FileShare.None))
            stream.WriteByte(0x01);
        Check("appended EXE bytes rejected", !MZP18IsolatedWebAuthRunner.IsHelperVerified);
        File.Copy(Path.Combine(source, "MZP18IsolatedWebAuth.exe"), exe, true);
        Check("restored exact helper accepted", MZP18IsolatedWebAuthRunner.IsHelperVerified);
        // Run only our rebuilt TEST executable with invalid/missing authorization,
        // which exits before WebView2, browser network or reading account storage.
        foreach (var invalid in new[] { (string?)null, Path.Combine(own, "unrelated") })
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment.Remove("MZ_WEBAUTH_HS");
            if (invalid is null) psi.Environment.Remove("MZ_TEST_PROFILE_ROOT");
            else psi.Environment["MZ_TEST_PROFILE_ROOT"] = invalid;
            using var started = Process.Start(psi) ?? throw new InvalidOperationException("TEST helper not created");
            bool exited = started.WaitForExit(5000);
            if (!exited) { started.Kill(entireProcessTree: false); throw new InvalidOperationException("Refusal did not exit"); }
            Check(invalid is null ? "missing profile root refused before browser"
                                  : "foreign profile root refused before browser", started.ExitCode == 3);
        }
        Console.WriteLine($"TOTAL {_passes}/8 PASS (offline, no account/client/network)");
        return 0;
    }
}
