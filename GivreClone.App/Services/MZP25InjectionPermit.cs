using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using GivreCore.Injection;
using GivreLauncher.Core.Launch;

namespace GivreClone.App.Services;

/// <summary>
/// Permis d'injection à usage unique pour les DLL natives du clone. DllInjector.Inject refuse tout appel sans permis : sans
/// ce pont, GivreKama.dll / le bootstrap natif ne sont jamais chargés. Le permis n'est délivré que pour un Dofus.exe lancé
/// par CE clone (registre MZP9, même instance/même naissance), pour une DLL située sous le dossier de l'app ou sous les
/// données du clone, et dont l'empreinte SHA-256 est épinglée (hook de capture, mods sources, runtime natif Turbo).
/// </summary>
internal static class MZP25InjectionPermit
{
    private static long _generation;

    private static readonly HashSet<string> PinnedHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        MZP25NativeModCatalog.OwnedCaptureHook.Sha256,
        NativeTurboRuntime.BootstrapSha256,
        NativeTurboRuntime.HostSha256,
        NativeTurboRuntime.SkipperSha256,
        NativeTurboRuntime.SpeedHackSha256,
        NativeTurboRuntime.OverlaySha256,
    };

    private static bool IsPinned(string sha256) =>
        PinnedHashes.Contains(sha256) || MZP25NativeModCatalog.SourceOwned.Any(m => string.Equals(sha256, m.Sha256, StringComparison.OrdinalIgnoreCase));

    private static bool UnderRoot(string path, string root)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Authority : ITestInjectionAuthority
    {
        public bool VerifyIndependentTestOwnership(AuthorizedTestInjection p) => Check(p);
        public bool IsStillAuthorized(AuthorizedTestInjection p) => Check(p);

        private static bool Check(AuthorizedTestInjection p)
        {
            if (p.ExpiresUtc <= DateTimeOffset.UtcNow || !MZP9OwnedClientRegistry.IsOwnedAndLive(p.ProcessId)) return false;
            if (!UnderRoot(p.DllPath, AppContext.BaseDirectory) && !UnderRoot(p.DllPath, MZP19LocalClone.DataRoot)) return false;
            return IsPinned(p.DllSha256);
        }
    }

    /// <summary>Délivre un permis (60 s) pour injecter <paramref name="dllPath"/> dans <paramref name="pid"/>. False = refus.</summary>
    public static bool TryRegister(int pid, string dllPath)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !MZP9OwnedClientRegistry.IsOwnedAndLive(pid)) return false;
            using var proc = Process.GetProcessById(pid);
            var exe = proc.MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return false;
            var permit = new AuthorizedTestInjection(pid, proc.StartTime.ToUniversalTime().Ticks, exe, Sha256Of(exe),
                dllPath, Sha256Of(dllPath), Interlocked.Increment(ref _generation), DateTimeOffset.UtcNow.AddSeconds(60));
            bool ok = DllInjector.RegisterOwnedTestPermit(permit, new Authority());
            if (!ok) GivreLauncher.Core.Transport.TransportLog.Line($"[mod] permis refusé pour {Path.GetFileName(dllPath)} (pinned={IsPinned(permit.DllSha256)}, chemin ok={UnderRoot(dllPath, AppContext.BaseDirectory) || UnderRoot(dllPath, MZP19LocalClone.DataRoot)}, client vivant={MZP9OwnedClientRegistry.IsOwnedAndLive(pid)})");
            return ok;
        }
        catch (Exception ex) { GivreLauncher.Core.Transport.TransportLog.Line($"[mod] permis : erreur {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    private static string Sha256Of(string file)
    {
        using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(s));
    }
}
