using System.Collections.Generic;
using System.Globalization;
namespace GivreClone.App.Services;
public readonly record struct CombatVisualSelection(
    int Mask,
    int CombatMode,
    int RoleplayMode,
    string CombatMiniatures,
    string RoleplayMiniatures)
{
    public bool WantsModule => Mask != 0 || CombatMode != 0 || RoleplayMode != 0;
    public static CombatVisualSelection Teardown { get; } = new(0, 0, 0,
        CombatVisualOptions.DefaultCombatMiniatures, CombatVisualOptions.DefaultRoleplayMiniatures);
}
public static class CombatVisualOptions
{
    public const string DefaultCombatMiniatures = "111111";
    public const string DefaultRoleplayMiniatures = "11110";
    public const string ModuleName = "GivreCombatVisuals.dll";
    public const string ModId = "givre.dofus3.combat-visuals";
    public const string Sha256 = "05e919ee949a573127f31c47579bea63b86138d50897dd9ddd1f8902842676f4";
    public static readonly IReadOnlyList<CombatVisualModPolicy.Module> Modules =
    [
        // Verify the distributed source, before NativeTurboRuntime renames it per PID.
        new("DofusNativeBootstrap.dll", "9b9c5f96dbf762ea7fb9d5af395d4f22622781da0b2b53ffa25c20c6c50ea75e"),
        new("DofusNativeHost.dll", "22d2c6743c23d48f80bc0a9e114ba13bcfb07587470aedb188a9fdba8846ec1b"),
        new(ModuleName, Sha256)
    ];
    public static bool Available(string directory) => CombatVisualModPolicy.VerifyModules(directory, Modules);
    public static bool CompatibilityRefused(string status) => status.StartsWith(
        "ERROR Dofus 3.7.12.12 fingerprints differ;", System.StringComparison.Ordinal);
    public static int Mask(CombatProfile profile) =>
        (profile.NoSpellEffects ? 1 : 0) | (profile.ShortSpellCasts ? 2 : 0) |
        (profile.NoAnim ? 4 : 0) | (profile.NoDeathAnimations ? 8 : 0) |
        (profile.InstantMove ? 16 : 0) | (profile.Collapse ? 32 : 0);
    public static int NormalizeMode(int mode) => mode is >= 0 and <= 2 ? mode : 0;
    public static CombatVisualSelection FromProfile(CombatProfile profile)
    {
        int combat = NormalizeMode(profile.CombatAppearanceMode);
        int roleplay = NormalizeMode(profile.RoleplayAppearanceMode);
        return new CombatVisualSelection(Mask(profile), combat, roleplay,
            Digits(profile.MiniatureSelf, profile.MiniatureAllies, profile.MiniatureAllySummons,
                profile.MiniatureOwnSummons, profile.MiniatureEnemies, profile.MiniatureEnemySummons),
            Digits(profile.RoleplayMiniatureSelf, profile.RoleplayMiniatureParty, profile.RoleplayMiniaturePlayers,
                profile.RoleplayMiniatureMonsters, profile.RoleplayMiniatureNpcs));
    }
    public static bool WantsModule(CombatProfile profile) => FromProfile(profile).WantsModule;
    // A cleanup caller must not copy the profile: mode 0, not the Enhanced struct default of 2.
    public static CombatVisualSelection Resolve(bool profilePush, CombatProfile? profile) =>
        profilePush && profile is not null ? FromProfile(profile) : CombatVisualSelection.Teardown;
    public static string FormatIni(CombatVisualSelection selection)
    {
        int mask = selection.Mask & 63;
        int combat = NormalizeMode(selection.CombatMode);
        int roleplay = NormalizeMode(selection.RoleplayMode);
        string combatMiniatures = ValidDigits(selection.CombatMiniatures, DefaultCombatMiniatures.Length)
            ? selection.CombatMiniatures : DefaultCombatMiniatures;
        string roleplayMiniatures = ValidDigits(selection.RoleplayMiniatures, DefaultRoleplayMiniatures.Length)
            ? selection.RoleplayMiniatures : DefaultRoleplayMiniatures;
        return "mask=" + mask.ToString(CultureInfo.InvariantCulture) + "\n"
            + "combat-mode=" + combat.ToString(CultureInfo.InvariantCulture) + "\n"
            + "roleplay-mode=" + roleplay.ToString(CultureInfo.InvariantCulture) + "\n"
            + "combat-miniatures=" + combatMiniatures + "\n"
            + "roleplay-miniatures=" + roleplayMiniatures + "\n";
    }
    static string Digits(params bool[] values)
    {
        var chars = new char[values.Length];
        for (int i = 0; i < values.Length; i++) chars[i] = values[i] ? '1' : '0';
        return new string(chars);
    }
    static bool ValidDigits(string? value, int width)
    {
        if (value is null || value.Length != width) return false;
        foreach (char c in value) if (c != '0' && c != '1') return false;
        return true;
    }
    // Only the profile push supplies settings. Teardown must never reactivate
    // default-enabled options from a profile that is about to be removed.
    public static bool IsProfilePush(string caller) => caller == "SetCombatProfile";

    public static bool TacticalRequested(bool wanted, bool modsEnabled, bool permitted, bool socketOnly) =>
        wanted && modsEnabled && permitted && !socketOnly;
}
