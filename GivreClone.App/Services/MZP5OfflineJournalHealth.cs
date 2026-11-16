using System.Security.AccessControl;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.Services;

public sealed partial class MZP3TestAccountService
{
    /// <summary>
    /// Explicit operator action to open/read a REAL MZP0 journal under this
    /// exclusive owner-only TEST run. May initialize an empty journal only
    /// after separate recovery-folder preparation. It never activates a
    /// generation, sends an action or purports to attest game connectivity.
    /// </summary>
    public MZP5RecoveryHealthSnapshot InitializeAndInspectTestJournal(bool explicitOperatorApproval)
    {
        if (!explicitOperatorApproval) return MZP5RecoveryHealthSnapshot.Unavailable;
        lock (_gate)
        {
            try
            {
                EnsureActive();
                if (_state != MZP3ServiceState.Ready || _dirty ||
                    _selectedProfileId is not { } id ||
                    !_profiles.Any(p => p.Id == id && p.IsOfflineCandidate))
                    return MZP5RecoveryHealthSnapshot.Unavailable;

                EnsurePathsAndPermissions();
                if (!Directory.Exists(_recoveryDirectory))
                    return MZP5RecoveryHealthSnapshot.Unavailable;
                CheckNoReparse(_recoveryDirectory);
                var security = FileSystemAclExtensions.GetAccessControl(
                    new DirectoryInfo(_recoveryDirectory));
                if (!security.AreAccessRulesProtected)
                    return MZP5RecoveryHealthSnapshot.Unavailable;
                ValidateAccessRules(security);

                // Only one fixed journal file in the operator-owned test run.
                // Its lock, sidecars and backup must also reject reparse points.
                var journal = Path.Combine(_recoveryDirectory, "mzp5-test-recovery.json");
                foreach (var candidate in new[]
                {
                    journal, journal + ".lck", journal + ".tmp", journal + ".bak"
                }) CheckNoReparse(candidate);

                using var reader = new MZP0RecoveryJournal(journal);
                return MZP5RecoveryHealthProjection.Read(reader);
            }
            catch
            {
                // The store may be locked by a separate authorized test owner,
                // corrupt, revoked or inaccessible. Never show a green status.
                return MZP5RecoveryHealthSnapshot.Unavailable;
            }
        }
    }
}
