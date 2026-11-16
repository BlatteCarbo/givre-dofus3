using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace GivreClone.App.Services;

/// <summary>
/// Conservative visual-mod capability policy. The standalone Animator2D skipper
/// implements no-animation only. An instantaneous-looking movement has to be
/// supported by an independently verified renderer; a profile flag or the
/// existence of an unrelated DLL does not constitute that proof.
/// </summary>
public static class CombatVisualModPolicy
{
    public sealed record Module(string Name, string Sha256);
    public static readonly IReadOnlyList<Module> StandaloneNoAnimModules =
    [
        new("DofusNativeBootstrap.dll", "9b9c5f96dbf762ea7fb9d5af395d4f22622781da0b2b53ffa25c20c6c50ea75e"),
        new("DofusNativeHost.dll", "22d2c6743c23d48f80bc0a9e114ba13bcfb07587470aedb188a9fdba8846ec1b"),
        new("DofusCombatAnimationSkipper.dll", "7138d7dc21cd394fcf737b0db26bfad148341f147f5664f6290cec563666a79b"),
        new("DofusCombatSpeedHack.dll", "895a195a6551efb4a1085e500b745676ce8bf44a0967426de3e14abd5d1a0a24")
    ];

    /// <summary>
    /// Offline preflight for a source-owned, SHA-pinned local module bundle.
    /// File identity does not certify injection, client version or gameplay.
    /// </summary>
    public static bool HasPinnedStandaloneSkipper(string directory) =>
        VerifyModules(directory, StandaloneNoAnimModules);

    public static bool VerifyModules(string directory, IReadOnlyList<Module> modules)
    {
        if (string.IsNullOrWhiteSpace(directory) || modules is null || modules.Count is < 1 or > 12)
            return false;
        try
        {
            string root = Path.GetFullPath(directory);
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return false;
            foreach (var module in modules)
            {
                if (module.Name != Path.GetFileName(module.Name) || module.Name is "." or ".." ||
                    module.Sha256.Length != 64 || !module.Sha256.All(Uri.IsHexDigit))
                    return false;
                string file = Path.Combine(root, module.Name);
                var info = new FileInfo(file);
                if (!info.Exists || info.Length is <= 0 or > 16 * 1024 * 1024 ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)),
                    module.Sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        { return false; }
    }
    public sealed record State(bool CanRequestNoAnim, bool CanRequestCollapse,
        bool CanRequestInstantMove, string Description)
    {
        public bool ServerTeleportSupported => false;
    }

    public static bool CanUseHistoricalMods(bool isolatedClone, bool originalDllExists) =>
        !isolatedClone && originalDllExists;

    public static State Evaluate(bool isolatedClone, bool modsEnabled, bool noAnimPermission,
        bool socketOnly, bool pinnedSkipperInstalled, bool independentlyVerifiedLegacyRenderer)
    {
        if (!modsEnabled)
            return new(false, false, false, "Mods natifs désactivés dans les réglages. Aucun effet client demandé.");
        if (!noAnimPermission)
            return new(false, false, false, "Droit no-anim indisponible.");
        if (socketOnly)
            return new(false, false, false, "Mode Socket sans client graphique : aucune animation à accélérer.");

        bool noAnim = pinnedSkipperInstalled ||
            CanUseHistoricalMods(isolatedClone, independentlyVerifiedLegacyRenderer);
        // Neither the standalone skipper nor the tactical/overlay plugins
        // implement movement snapping or shortening every sequence delay.
        bool nativeVisualExtras = CanUseHistoricalMods(isolatedClone, independentlyVerifiedLegacyRenderer);
        return new(noAnim, nativeVisualExtras, nativeVisualExtras, noAnim
            ? nativeVisualExtras
                ? "Animations : module candidat. Réduction des délais et déplacement visuel : module historique uniquement, effet en jeu non certifié."
                : "No Anim : module autonome disponible hors jeu, chargement/effet réel non certifiés. Déplacements instantanés et délais réduits : aucun fournisseur natif autorisé pour ce clone."
            : "No Anim : module épinglé manquant ou non vérifié. Aucun chargement natif ni accélération certifiés.");
    }

    public static (bool NoAnim, bool Collapse, bool InstantMove) Effective(
        State capability, bool wantedNoAnim, bool wantedCollapse, bool wantedInstantMove) =>
        (wantedNoAnim && capability.CanRequestNoAnim,
            wantedCollapse && capability.CanRequestCollapse,
            wantedInstantMove && capability.CanRequestInstantMove);
}
