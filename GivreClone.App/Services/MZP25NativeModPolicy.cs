using System;

namespace GivreClone.App.Services;

/// <summary>Pure admission rule, testable without a process, disk, injector or network.</summary>
public static class MZP25NativeModPolicy
{
    public static bool MayControl(int pid, bool isolatedClone, Func<int, bool> ownsLiveClient)
    {
        if (pid <= 0) return false;
        if (!isolatedClone) return true;
        ArgumentNullException.ThrowIfNull(ownsLiveClient);
        return ownsLiveClient(pid);
    }
}
