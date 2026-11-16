using System;

namespace GivreClone.App.Services;

/// <summary>
/// Admission for per-PID native mod side effects.
/// An arbitrary PID, a module-name match, or a user interface selection cannot
/// establish ownership of a game process. Local clone mode requires the live
/// Process handle that its independent TEST launcher registered at creation.
/// </summary>
public static class MZP25NativeModOwnership
{
    public static bool MayControl(int pid)
        => MZP25NativeModPolicy.MayControl(pid, MZP19LocalClone.Enabled,
            GivreLauncher.Core.Launch.MZP9OwnedClientRegistry.IsOwnedAndLive);
}
