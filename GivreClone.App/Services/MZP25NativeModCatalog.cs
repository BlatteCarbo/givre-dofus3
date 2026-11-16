using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace GivreClone.App.Services;

/// <summary>
/// Read-only inventory of two independently compiled native mods and their
/// matching host/bootstrap. Source: dofus3-native-host commit 010eaac.
/// A SHA match proves identity of shipped bytes, NEVER successful in-game load.
/// </summary>
public static class MZP25NativeModCatalog
{
    public sealed record ModFile(string RelativePath, string Sha256);
    public sealed record ModCheck(string Name, bool Exists, bool Pinned, string Status);

    public static readonly IReadOnlyList<ModFile> SourceOwned = new[]
    {
        new ModFile("DofusNativeBootstrap.dll", "cfdf212ab4503a09cd4feb3753ba46a4427273e28f2f3541b73398b9f49346bf"),
        new ModFile("DofusNativeHost.dll", "90ea11609bc03df3c36b820d65c0ee18285ee7e96a1f1c0d9922b8cc75fb3c1a"),
        new ModFile("NativeMods/DofusNativeTactical.dll", "9e9a7983360e51b1992e0971ef58a7ca442e5ed38a0a725302bc2a72ba618532"),
        new ModFile("NativeMods/DofusCombatAnimationSkipper.dll", "c8f2a344bdf88d933269298cbf6cc9cc39fb3eadcb9e732128c4d82e47ee1eab"),
        new ModFile("NativeMods/DofusInterfaceAnalyzer.dll", "4681d0ad761edc4c996e206297890ee122a39aa3ab11024bed15570de655b7be"),
        new ModFile("NativeMods/DofusRuntimeInspector.dll", "b4ca9d87435839e94dc0ea66f6c87bb3329573732f8ff25b3eb46061c3b99a12"),
        new ModFile("NativeMods/DofusNetworkDumper.dll", "d1d147779302fd89b10a5a5fcc6836244735de441dc734ac8a9700523430d1ee"),
        new ModFile("NativeMods/DofusNativeExample.dll", "7271d9c40204265982be48e793d86b9a65fb03fcf1ba35c4b67b4f21e1f6d0de"),
    };

    /// <summary>
    /// Locally compiled Detours hook (not the packed original GivreKama.dll).
    /// The fingerprint must be updated only after rebuilding/reviewing source.
    /// 2026-10-16 : source revue = GivreKama.cpp avec le port du relais lu dans GIVRE_RELAY_PORT (1024..65535, 5658 par défaut) ; banc d'essai tools/MZP23HookHarness PASS.
    /// 2026-11-01 : source revue = redirection AF_INET :5555 ajoutee (reconnect Zaap apres ReloginRequest), handshake factorise.
    /// </summary>
    public static readonly ModFile OwnedCaptureHook = new(
        "GivreKama.dll",
        "d2688a210d2d41a00c656aeeda7e04abdebb0bfd079c2ccaa87bd810cd4b372b");

    /// <summary>
    /// Refuses symlinks/junctions at all path components, unknown paths and wrong
    /// hashes. No native loading, launcher interaction or process inspection.
    /// </summary>
    public static ModCheck CheckFile(string root, ModFile spec)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(spec.RelativePath) ||
            spec.RelativePath.StartsWith('/') || spec.RelativePath.StartsWith('\\') ||
            Path.IsPathRooted(spec.RelativePath) ||
            spec.RelativePath.Split('/', '\\').Any(p => p is "." or ".." or ""))
            return new ModCheck(spec.RelativePath, false, false, "chemin non admis");
        try
        {
            var baseDir = Path.GetFullPath(root);
            var relative = spec.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(baseDir, relative));
            if (!path.StartsWith(baseDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return new ModCheck(spec.RelativePath, false, false, "chemin hors du paquet");

            for (var d = new DirectoryInfo(Path.GetDirectoryName(path)!);
                 d is not null && d.FullName.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
                 d = d.Parent)
            {
                if (!d.Exists) break;
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new ModCheck(spec.RelativePath, false, false, "répertoire redirigé refusé");
                if (string.Equals(d.FullName, baseDir, StringComparison.OrdinalIgnoreCase)) break;
            }

            var file = new FileInfo(path);
            if (!file.Exists) return new ModCheck(spec.RelativePath, false, false, "absent");
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                return new ModCheck(spec.RelativePath, true, false, "lien symbolique refusé");
            using var input = file.OpenRead();
            var digest = Convert.ToHexString(SHA256.HashData(input));
            bool pinned = digest.Equals(spec.Sha256, StringComparison.OrdinalIgnoreCase);
            return new ModCheck(spec.RelativePath, true, pinned,
                pinned ? "SHA-256 conforme au build source" : "SHA-256 inattendu : ne pas charger");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                   System.Security.SecurityException)
        { return new ModCheck(spec.RelativePath, false, false, "lecture impossible"); }
    }

    public static IReadOnlyList<ModCheck> Inspect(string root)
        => SourceOwned.Select(spec => CheckFile(root, spec)).ToArray();

    public static string Summary(string root)
    {
        var checks = Inspect(root);
        var verified = checks.Count(x => x.Pinned);
        return $"Mods reconstruits depuis les sources : {verified}/{checks.Count} fichiers vérifiés." +
            Environment.NewLine +
            string.Join(Environment.NewLine, checks.Select(c =>
                (c.Pinned ? "✓ " : "× ") + c.Name + " : " + c.Status)) +
            Environment.NewLine +
            "Empreintes des DLL uniquement ; aucun chargement ni effet en jeu certifié. " +
            "Le dumper réseau est désactivé par défaut : ses captures peuvent contenir des données sensibles.";
    }
}
