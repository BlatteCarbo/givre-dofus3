using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using GivreClone.App.Services;

internal static class Program
{
    private static int _passed, _failed;

    private static void Check(bool condition, string label)
    {
        if (condition) { _passed++; Console.WriteLine("PASS " + label); }
        else { _failed++; Console.Error.WriteLine("FAIL " + label); }
    }

    private static void Denied(MZP4LauncherPreflightResult result,
        MZP4LauncherRefusal reason, string label)
        => Check(result.Refusal == reason && result.Candidate is null &&
            !result.IsConfigurationCandidate && !result.MayStartHost &&
            !result.MayLaunchClient && !result.MayInject && !result.Authenticated,
            label);

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity PrivateAcl()
    {
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("No Windows owner SID.");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.SetOwner(sid);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }

    private static void Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows-only ACL harness; target App is Windows.");
            Environment.ExitCode = 2;
            return;
        }

        // A dedicated TEMP fixture. It is not the user's actual GivreReplica store.
        string anchor = Path.Combine(Path.GetTempPath(), "MZP4LauncherHarness-" + Guid.NewGuid().ToString("N"));
        string runKey = "test-1234";
        string run = Path.Combine(anchor, "GivreReplica", "TestRuns", runKey);
        string recovery = Path.Combine(run, "recovery");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(run)!);
            FileSystemAclExtensions.CreateDirectory(PrivateAcl(), run);
            Directory.CreateDirectory(recovery); // Inherits owner-only DACL.

            var valid = new MZP4LauncherSelection(runKey, 15432,
                Path.Combine(run, "accounts.json"), recovery,
                MZP4LauncherPreflight.TargetClientVersion, true);
            MZP4LauncherPreflightResult Eval(MZP4LauncherSelection selection) =>
                MZP4LauncherPreflight.EvaluateAtAnchor(selection, anchor);

            var good = Eval(valid);
            Check(good.IsConfigurationCandidate && good.Refusal == MZP4LauncherRefusal.None &&
                good.Candidate is { TestRelayPort: 15432 } &&
                good.Candidate.ClientVersionCandidate == MZP4LauncherPreflight.TargetClientVersion &&
                good.Candidate.TestAccountStorePath == valid.TestAccountStorePath &&
                good.Candidate.JournalDirectory == valid.JournalDirectory,
                "real product preflight accepts private test-run directories");
            Check(!good.MayStartHost && !good.MayLaunchClient && !good.MayInject &&
                !good.Authenticated && !good.PortAvailabilityVerified &&
                good.Candidate is { GrantsRuntimeAdmission: false, PortAvailabilityVerified: false,
                    OwnedClientVerified: false, ServerAckVerified: false },
                "no accidental port/live/ACK authority");

            Denied(Eval(valid with { ExplicitOperatorApproval = false }),
                MZP4LauncherRefusal.OperatorApprovalMissing, "explicit approval required");
            Denied(MZP4LauncherPreflight.Evaluate(null),
                MZP4LauncherRefusal.SelectionMissing, "null selection denied before filesystem");
            Denied(Eval(valid with { ClientVersionCandidate = "3.6.12.16" }),
                MZP4LauncherRefusal.ClientVersionUnverified, "old build rejected");
            Check(Eval(valid with { ClientVersionCandidate = "3.7.10.10" }).IsConfigurationCandidate,
                "prior 3.7 patch accepted for isolated TEST preflight");
            Check(Eval(valid with { ClientVersionCandidate = "3.7.99.10" }).IsConfigurationCandidate,
                "same 3.7 family does not require exact patch version");
            Denied(Eval(valid with { ClientVersionCandidate = "3.8.0.0" }),
                MZP4LauncherRefusal.ClientVersionUnverified, "new protocol family rejected");
            Denied(Eval(valid with { ClientVersionCandidate = null }),
                MZP4LauncherRefusal.ClientVersionUnverified, "missing version rejected");
            Denied(Eval(valid with { TestRunId = ".." }),
                MZP4LauncherRefusal.InvalidTestRunId, "dotdot run refused");
            Denied(Eval(valid with { TestRunId = "abc" }),
                MZP4LauncherRefusal.InvalidTestRunId, "short run key refused");
            Denied(Eval(valid with { TestRunId = "test/escape" }),
                MZP4LauncherRefusal.InvalidTestRunId, "run key cannot navigate folders");
            Denied(Eval(valid with { TestRunId = new string('a', 41) }),
                MZP4LauncherRefusal.InvalidTestRunId, "oversized run key refused");

            foreach (int port in new[] { -1, 0, 80, 1023, 5657, 5658, 26666, 65536 })
                Denied(Eval(valid with { TestRelayPort = port }),
                    MZP4LauncherRefusal.ProtectedOrInvalidPort, $"protected/invalid port {port}");
            Check(Eval(valid with { TestRelayPort = 65535 }).IsConfigurationCandidate,
                "upper user port policy accepted without probing socket");
            Check(Eval(valid with { TestRelayPort = 1024 }).IsConfigurationCandidate,
                "lower unprivileged port policy accepted without probing socket");

            Denied(Eval(valid with { TestAccountStorePath = null }),
                MZP4LauncherRefusal.TestAccountStorePathMismatch, "no account-store path rejected");
            Denied(Eval(valid with { TestAccountStorePath = Path.Combine(anchor, "accounts.json") }),
                MZP4LauncherRefusal.TestAccountStorePathMismatch, "store outside TestRuns rejected");
            Denied(Eval(valid with { TestAccountStorePath = Path.Combine(run, "accounts2.json") }),
                MZP4LauncherRefusal.TestAccountStorePathMismatch, "unexpected account-store basename rejected");
            Denied(Eval(valid with { TestAccountStorePath = Path.Combine(run, "..", runKey, "accounts.json") }),
                MZP4LauncherRefusal.TestAccountStorePathMismatch, "traversal even if normalized inside root rejected");
            Denied(Eval(valid with { TestAccountStorePath = Path.Combine(run, "accounts.json:stream") }),
                MZP4LauncherRefusal.TestAccountStorePathMismatch, "alternate data stream rejected");
            Denied(Eval(valid with { JournalDirectory = Path.Combine(run, "..", runKey, "recovery") }),
                MZP4LauncherRefusal.JournalDirectoryMismatch, "journal path traversal rejected");
            Denied(Eval(valid with { JournalDirectory = Path.Combine(run, "accounts.json") }),
                MZP4LauncherRefusal.JournalDirectoryMismatch, "journal misdirected to account file rejected");
            Denied(Eval(valid with { JournalDirectory = null }),
                MZP4LauncherRefusal.JournalDirectoryMismatch, "no journal path rejected");
            Denied(Eval(valid with { TestAccountStorePath = @"C:\\Windows\\System32\\config\\SAM" }),
                MZP4LauncherRefusal.TestAccountStorePathMismatch, "system config rejected before inspection");
            Denied(Eval(valid with { JournalDirectory = @"\\\\server\\share\\recovery" }),
                MZP4LauncherRefusal.JournalDirectoryMismatch, "UNC path rejected before inspection");

            string absentKey = "other-0001";
            var absent = valid with { TestRunId = absentKey,
                TestAccountStorePath = Path.Combine(anchor, "GivreReplica", "TestRuns", absentKey, "accounts.json"),
                JournalDirectory = Path.Combine(anchor, "GivreReplica", "TestRuns", absentKey, "recovery") };
            Denied(Eval(absent), MZP4LauncherRefusal.TestRunDirectoryMissing,
                "missing actual run folder denies preflight");
            string looseRun = Path.Combine(anchor, "GivreReplica", "TestRuns", "loose-run1");
            Directory.CreateDirectory(looseRun); // inherited ACL: not private protected
            var loose = valid with { TestRunId = "loose-run1",
                TestAccountStorePath = Path.Combine(looseRun, "accounts.json"),
                JournalDirectory = Path.Combine(looseRun, "recovery") };
            Denied(Eval(loose), MZP4LauncherRefusal.PrivateDirectoryPermissionsUnverified,
                "inherited/public directory cannot pass private ACL gate");

            string noRecoveryKey = "no-journal";
            string noRecoveryRun = Path.Combine(anchor, "GivreReplica", "TestRuns", noRecoveryKey);
            FileSystemAclExtensions.CreateDirectory(PrivateAcl(), noRecoveryRun);
            var noRecovery = valid with { TestRunId = noRecoveryKey,
                TestAccountStorePath = Path.Combine(noRecoveryRun, "accounts.json"),
                JournalDirectory = Path.Combine(noRecoveryRun, "recovery") };
            Denied(Eval(noRecovery), MZP4LauncherRefusal.JournalDirectoryMissing,
                "journal folder must actually exist");
            Denied(MZP4LauncherPreflight.EvaluateAtAnchor(valid, Path.Combine(anchor, "does-not-exist")),
                MZP4LauncherRefusal.TestAccountStorePathMismatch,
                "cannot substitute another LocalAppData anchor");
            Denied(MZP4LauncherPreflight.EvaluateAtAnchor(valid, "relative-anchor"),
                MZP4LauncherRefusal.IsolatedRootUnavailable,
                "relative LocalAppData rejected before filesystem access");

            Console.WriteLine($"MZP4 Launcher: {_passed}/{_passed + _failed} PASS");
            if (_failed != 0) Environment.ExitCode = 1;
        }
        finally
        {
            // Delete ONLY the unique harness-owned fixture under TEMP.
            string full = Path.GetFullPath(anchor);
            string temp = Path.GetFullPath(Path.GetTempPath());
            if (Path.GetFileName(full).StartsWith("MZP4LauncherHarness-", StringComparison.Ordinal) &&
                full.StartsWith(temp.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
    }
}
