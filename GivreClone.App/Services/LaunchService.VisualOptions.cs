using System;
using System.IO;
namespace GivreClone.App.Services;
public sealed partial class LaunchService
{
    internal CombatVisualSelection NativeVisualOptions(uint pid)
    {
        if (IsSocketPid(pid) || !AppConfig.Current.ModsEnabled ||
            !LicenseService.HasPermission(LicenseService.Perm.NoAnim) ||
            !CombatVisualOptions.Available(Path.Combine(AppContext.BaseDirectory, "NativeTurboRuntime")))
            return CombatVisualSelection.Teardown;
        return _combat.TryGetValue(pid, out var profile) ? CombatVisualOptions.FromProfile(profile) : CombatVisualSelection.Teardown;
    }
}
