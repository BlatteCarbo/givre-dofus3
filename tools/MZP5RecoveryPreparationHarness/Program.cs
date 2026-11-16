using System.Security.AccessControl;
using GivreClone.App.Services;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Owner-only ACL regression requires Windows.");

var checks = 0;
void Check(bool pass, string reason)
{
    if (!pass) throw new InvalidOperationException(reason);
    checks++;
}

string runId = "mzp5-" + Guid.NewGuid().ToString("N");
string run = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "GivreReplica", "TestRuns", runId);
string recovery = Path.Combine(run, "recovery");
string accounts = Path.Combine(run, "accounts.json");

Check(!Directory.Exists(run), "Disposable run ID already exists");
try
{
    using (var service = MZP3TestAccountService.Open(runId, explicitOperatorApproval: true))
    {
        Check(!Directory.Exists(recovery), "Store opened the recovery directory without approval");
        var refusal = service.PrepareIsolatedRecovery(false);
        Check(refusal.State == MZP5RecoveryPreparationState.ApprovalMissing &&
              !Directory.Exists(recovery) && !refusal.CanStartHost && !refusal.MayConnect,
            "Non-consenting recovery preparation wrote state");
        Check(service.PrepareIsolatedRecovery(true).State == MZP5RecoveryPreparationState.CandidateMissing,
            "Missing selected offline profile must refuse");

        var profile = service.CreateProfile("offline-test");
        service.SelectProfile(profile.Id);
        Check(service.PrepareIsolatedRecovery(true).State == MZP5RecoveryPreparationState.StoreNotReady,
            "Unsaved dirty profile should not create recovery directory");
        service.Save();
        Check(service.PrepareIsolatedRecovery(true).State == MZP5RecoveryPreparationState.CandidateMissing,
            "Saved profile is not equivalent to selected server/character candidate");

        service.SetOfflineCandidate(profile.Id, "offline-server", "offline-character", true);
        service.Save();
        var selected = new MZP4LauncherSelection(runId, 15432, accounts,
            recovery, "3.7.10.10", true);
        Check(MZP4LauncherPreflight.Evaluate(selected).Refusal ==
              MZP4LauncherRefusal.JournalDirectoryMissing,
            "Launcher was not blocked before the directory existed");

        // Refuse an existing unexpected FILE at the journal path and do not
        // overwrite it; this is our new isolated run, not production state.
        var poison = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(recovery, poison);
        Check(service.PrepareIsolatedRecovery(true).State == MZP5RecoveryPreparationState.UnsafeDirectory,
            "Non-directory journal path must fail closed");
        Check(File.ReadAllBytes(recovery).SequenceEqual(poison),
            "Unexpected journal path was overwritten");
        File.Delete(recovery);

        var prepared = service.PrepareIsolatedRecovery(true);
        Check(prepared.LocalDirectoryReady && prepared.DirectoryCreated &&
              Directory.Exists(recovery) && !prepared.CanStartHost &&
              !prepared.ServerAckVerified,
            "Explicitly prepared directory was not created or granted a fake capability");
        var acl = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(recovery));
        Check(acl.AreAccessRulesProtected, "Prepared recovery directory has inherited DACL");
        var again = service.PrepareIsolatedRecovery(true);
        Check(again.LocalDirectoryReady && !again.DirectoryCreated,
            "Repeated preparation is not idempotent");
        var admitted = MZP4LauncherPreflight.Evaluate(selected);
        Check(admitted.IsConfigurationCandidate && !admitted.MayLaunchClient &&
              !admitted.MayInject && !admitted.Authenticated,
            "Isolated directory candidate incorrectly became runtime admission");
        Check(!File.Exists(accounts), "Neither service nor preflight may create credential store");
    }

    using (var reopened = MZP3TestAccountService.Open(runId, explicitOperatorApproval: true))
    {
        Check(reopened.PrepareIsolatedRecovery(true).LocalDirectoryReady,
            "Recovery not durable after exclusive lease release/reopen");
    }
}
finally
{
    var full = Path.GetFullPath(run);
    var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "GivreReplica", "TestRuns"));
    if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(full).Equals(runId, StringComparison.Ordinal) ||
        !runId.StartsWith("mzp5-", StringComparison.Ordinal) ||
        (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
        throw new InvalidOperationException("Unsafe disposable-run cleanup");
    if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
}
Check(!Directory.Exists(run), "Disposable local test run was not cleaned");
Console.WriteLine($"MZP5 isolated recovery directory: PASS ({checks} real filesystem checks). No client, account credentials or network accessed.");
