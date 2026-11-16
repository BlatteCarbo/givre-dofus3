using System;
using System.IO;
using System.Globalization;

namespace GivreClone.App.Services;

/// <summary>
/// Pure directory policy for local native mod control. A TEST clone never
/// writes to the original GivreClone/GivreLauncher per-process directories.
/// </summary>
public static class MZP25NativeModPaths
{
    public static string Controls(bool localClone, string cloneDataRoot, string localAppData)
        => Path.Combine(localClone ? RequireRoot(cloneDataRoot) : RequireRoot(localAppData),
            localClone ? "ModsControl" : "GivreLauncher");

    public static string Turbo(int pid, bool localClone, string cloneDataRoot, string localAppData)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        return localClone
            ? Path.Combine(RequireRoot(cloneDataRoot), "NativeTurbo", pid.ToString(CultureInfo.InvariantCulture))
            : Path.Combine(RequireRoot(localAppData), "GivreClone", "NativeTurbo", pid.ToString(CultureInfo.InvariantCulture));
    }

    private static string RequireRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Un chemin privé absolu est requis.", nameof(path));
        return Path.GetFullPath(path);
    }
}
