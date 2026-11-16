using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GivreClone.App.Services;

/// <summary>
/// Independent local clone distribution. It never shares GivreClone's license
/// session, stored accounts or application settings. This does NOT attest
/// ownership of, or authorize traffic to, any Dofus client or server.
/// </summary>
public static class MZP19LocalClone
{
    public const bool Enabled = true;

    private static string RunRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GivreReplica", "TestRuns", ValidateRunName(Environment.GetEnvironmentVariable("GIVRE_CLONE_RUN_NAME")));

    /// <summary>Nom d'une instance de validation isolée, toujours sous TestRuns. Aucun chemin libre accepté.</summary>
    public static string ValidateRunName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "givre-app-offline";
        if (name.Length > 64 || name[0] is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new ArgumentException("Invalid local validation run name.", nameof(name));
        foreach (char c in name)
            if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
                throw new ArgumentException("Invalid local validation run name.", nameof(name));
        return name;
    }

    public static string DataRoot => Path.Combine(
        RunRoot, "userdata");

    /// <summary>
    /// The previous standalone clone created this exact TEST run with inherited
    /// Windows ACLs. Repair only this owned run, never another run or installed
    /// GivreClone state. All data below this run belongs to the reconstructed clone.
    /// Refuse junctions and foreign ownership before changing permissions.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void PreparePrivateRun()
    {
        if (!Enabled) return;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows TEST run required.");
        string appData = Path.GetFullPath(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData));
        string replica = Path.Combine(appData, "GivreReplica");
        string testRuns = Path.Combine(replica, "TestRuns");
        string run = RunRoot;
        foreach (var path in new[] { appData, replica, testRuns, run, DataRoot })
        {
            if (!Directory.Exists(path)) continue;
            if ((new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("TEST storage contains a junction or symlink.");
        }
        Directory.CreateDirectory(testRuns);
        if (!Directory.Exists(run))
            System.IO.FileSystemAclExtensions.CreateDirectory(PrivateAcl(), run);
        else
            SecureOwnedDirectory(run);
        if (!Directory.Exists(DataRoot))
            System.IO.FileSystemAclExtensions.CreateDirectory(PrivateAcl(), DataRoot);
        else
            SecureOwnedDirectory(DataRoot);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SecureOwnedDirectory(string path)
    {
        var target = new DirectoryInfo(path);
        if ((target.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Unsafe TEST directory.");
        var acl = System.IO.FileSystemAclExtensions.GetAccessControl(target);
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot establish current Windows owner.");
        if (!user.Equals(acl.GetOwner(typeof(SecurityIdentifier))))
            throw new InvalidOperationException("TEST directory owner differs from current user.");
        acl.SetAccessRuleProtection(true, false);
        acl.SetAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        System.IO.FileSystemAclExtensions.SetAccessControl(target, acl);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static DirectorySecurity PrivateAcl()
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot establish current Windows user.");
        var acl = new DirectorySecurity();
        acl.SetOwner(user);
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }
}
