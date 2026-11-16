using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace GivreClone.App.Services;

/// <summary>
/// Proposed configuration ONLY for a dedicated offline test host. No login, token,
/// process id, executable path, client handle, socket, proxy or account contents.
/// </summary>
public sealed record MZP4LauncherSelection(
    string? TestRunId,
    int TestRelayPort,
    string? TestAccountStorePath,
    string? JournalDirectory,
    string? ClientVersionCandidate,
    bool ExplicitOperatorApproval);

public enum MZP4LauncherRefusal
{
    None,
    SelectionMissing,
    OperatorApprovalMissing,
    UnsupportedPlatform,
    ClientVersionUnverified,
    InvalidTestRunId,
    ProtectedOrInvalidPort,
    IsolatedRootUnavailable,
    TestAccountStorePathMismatch,
    JournalDirectoryMismatch,
    UnsafeDirectoryChain,
    TestRunDirectoryMissing,
    JournalDirectoryMissing,
    PrivateDirectoryPermissionsUnverified,
    FileSystemInspectionFailed
}

/// <summary>
/// Structurally valid candidate for MZP2IsolatedTestHostConfiguration. Its mere
/// existence never grants MZP2 access to the host, socket, accounts or client.
/// The real MZP2 gate must rerun its checks at use time.
/// </summary>
public sealed record MZP4LauncherCandidate(
    int TestRelayPort, string TestAccountStorePath,
    string JournalDirectory, string ClientVersionCandidate,
    string TestRunId)
{
    public bool GrantsRuntimeAdmission => false;
    public bool PortAvailabilityVerified => false;
    public bool OwnedClientVerified => false;
    public bool ServerAckVerified => false;
}

/// <summary>
/// Diagnostics are safe for an App screen: no account names or paths, and no
/// statement about a socket listener, client ownership, authentication or ACK.
/// </summary>
public sealed record MZP4LauncherPreflightResult(
    MZP4LauncherRefusal Refusal, MZP4LauncherCandidate? Candidate)
{
    public bool IsConfigurationCandidate => Refusal == MZP4LauncherRefusal.None && Candidate is not null;
    public bool MayLaunchClient => false;
    public bool MayStartHost => false;
    public bool MayInject => false;
    public bool Authenticated => false;
    public bool PortAvailabilityVerified => false;
}

/// <summary>
/// Read-only, source-level preflight for the existing App's dedicated test path.
/// In particular, it does NOT call LaunchService, BotHostService, GivreRelay,
/// MZP3TestAccountService.Open, MZP2.StartOwnedTestHost, or any process/network API.
/// </summary>
public static class MZP4LauncherPreflight
{
    // Last locally observed release at 2026-10-16: the actual build should
    // still be rechecked through MZP11CytrusVersionProbe after any update.
    public const string TargetClientVersion = "3.7.11.11";
    public const string TestAccountFileName = "accounts.json";
    public const string TestJournalDirectoryName = "recovery";

    /// <summary>Uses the actual Windows LocalAppData anchor. Does not create it.</summary>
    public static MZP4LauncherPreflightResult Evaluate(MZP4LauncherSelection? selection)
    {
        if (selection is null) return Deny(MZP4LauncherRefusal.SelectionMissing);
        if (!selection.ExplicitOperatorApproval)
            return Deny(MZP4LauncherRefusal.OperatorApprovalMissing);
        if (!OperatingSystem.IsWindows())
            return Deny(MZP4LauncherRefusal.UnsupportedPlatform);
        return EvaluateAtAnchor(selection,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    /// <summary>
    /// Same real implementation with a test-owned local-app-data anchor, so the
    /// harness can inspect its own disposable fixture instead of user configs.
    /// Internal: cannot be used by external product callers to select an anchor.
    /// </summary>
    internal static MZP4LauncherPreflightResult EvaluateAtAnchor(
        MZP4LauncherSelection selection, string localAppDataAnchor)
    {
        if (selection is null) return Deny(MZP4LauncherRefusal.SelectionMissing);
        if (!selection.ExplicitOperatorApproval)
            return Deny(MZP4LauncherRefusal.OperatorApprovalMissing);
        if (!OperatingSystem.IsWindows())
            return Deny(MZP4LauncherRefusal.UnsupportedPlatform);
        if (!GivreClone.Protocol.MZP0ProtocolAdmission.Supports37Family(selection.ClientVersionCandidate))
            return Deny(MZP4LauncherRefusal.ClientVersionUnverified);
        if (!ValidRunId(selection.TestRunId))
            return Deny(MZP4LauncherRefusal.InvalidTestRunId);

        // The existing MZP2/BotHostService gates explicitly reserve 5657/5658;
        // LaunchService has ZaapEmulator(26666). This preflight also protects it.
        if (selection.TestRelayPort is < 1024 or > 65535 or 5657 or 5658 or 26666)
            return Deny(MZP4LauncherRefusal.ProtectedOrInvalidPort);

        if (string.IsNullOrWhiteSpace(localAppDataAnchor) ||
            !Path.IsPathFullyQualified(localAppDataAnchor))
            return Deny(MZP4LauncherRefusal.IsolatedRootUnavailable);

        try
        {
            string anchor = Path.GetFullPath(localAppDataAnchor);
            string testRoot = Path.Combine(anchor, "GivreReplica", "TestRuns");
            string runDirectory = Path.Combine(testRoot, selection.TestRunId!);
            string accountStore = Path.Combine(runDirectory, TestAccountFileName);
            string journal = Path.Combine(runDirectory, TestJournalDirectoryName);

            // Exact selection is mandatory: never silently substitute a default
            // or canonicalize ../, device/UNC/alternate stream into an allowed path.
            if (!SameCanonicalSelection(selection.TestAccountStorePath, accountStore))
                return Deny(MZP4LauncherRefusal.TestAccountStorePathMismatch);
            if (!SameCanonicalSelection(selection.JournalDirectory, journal))
                return Deny(MZP4LauncherRefusal.JournalDirectoryMismatch);

            // Only inspect directories in this one expected test-run chain.
            // NEVER enumerate or open accounts.json, recovery journal, secrets,
            // GivreClone configs, Dofus installation or executable.
            foreach (string path in new[] { anchor, Path.Combine(anchor, "GivreReplica"),
                testRoot, runDirectory })
            {
                if (!Directory.Exists(path))
                    return Deny(path == runDirectory
                        ? MZP4LauncherRefusal.TestRunDirectoryMissing
                        : MZP4LauncherRefusal.IsolatedRootUnavailable);
                if (IsUnsafeDirectory(path))
                    return Deny(MZP4LauncherRefusal.UnsafeDirectoryChain);
            }

            if (!PrivateOwnerOnly(runDirectory, requireProtectedDacl: true))
                return Deny(MZP4LauncherRefusal.PrivateDirectoryPermissionsUnverified);
            if (!Directory.Exists(journal))
                return Deny(MZP4LauncherRefusal.JournalDirectoryMissing);
            if (IsUnsafeDirectory(journal))
                return Deny(MZP4LauncherRefusal.UnsafeDirectoryChain);
            if (!PrivateOwnerOnly(journal, requireProtectedDacl: false))
                return Deny(MZP4LauncherRefusal.PrivateDirectoryPermissionsUnverified);

            // No file creation and no account-store metadata/content reads.
            return new(MZP4LauncherRefusal.None,
                new MZP4LauncherCandidate(selection.TestRelayPort,
                    accountStore, journal, selection.ClientVersionCandidate!, selection.TestRunId!));
        }
        catch (Exception e) when (e is ArgumentException or IOException or
            UnauthorizedAccessException or System.Security.SecurityException or
            NotSupportedException)
        {
            return Deny(MZP4LauncherRefusal.FileSystemInspectionFailed);
        }
    }

    private static bool ValidRunId(string? value)
    {
        if (value is null || value.Length is < 4 or > 40 ||
            !char.IsAsciiLetterOrDigit(value[0])) return false;
        foreach (var ch in value)
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_')) return false;
        return true;
    }

    private static bool SameCanonicalSelection(string? supplied, string expected)
    {
        if (string.IsNullOrWhiteSpace(supplied) ||
            !Path.IsPathFullyQualified(supplied) ||
            supplied.StartsWith("\\\\", StringComparison.Ordinal) ||
            supplied.IndexOfAny(new[] { '\0', '"', '<', '>', '|', '*', '?' }) >= 0)
            return false;

        // Reject even a traversal that would normalize BACK into TestRuns.
        foreach (string part in supplied.Split(new[] { '\\', '/' }))
            if (part is "." or "..") return false;
        string full = Path.GetFullPath(supplied);
        return string.Equals(full, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeDirectory(string path)
    {
        FileAttributes attrs = File.GetAttributes(path);
        return !attrs.HasFlag(FileAttributes.Directory) ||
               attrs.HasFlag(FileAttributes.ReparsePoint);
    }

    [SupportedOSPlatform("windows")]
    private static bool PrivateOwnerOnly(string path, bool requireProtectedDacl)
    {
        var sid = WindowsIdentity.GetCurrent().User;
        if (sid is null) return false;
        var security = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(path));
        if (requireProtectedDacl && !security.AreAccessRulesProtected) return false;
        if (!sid.Equals(security.GetOwner(typeof(SecurityIdentifier)))) return false;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        bool ownerHasAllow = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (!sid.Equals(rule.IdentityReference)) return false;
            ownerHasAllow = true;
        }
        return ownerHasAllow;
    }

    private static MZP4LauncherPreflightResult Deny(MZP4LauncherRefusal reason)
        => new(reason, null);
}
