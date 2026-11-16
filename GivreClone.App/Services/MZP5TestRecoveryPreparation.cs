using System.Security.AccessControl;

namespace GivreClone.App.Services;

/// <summary>Only the prepared LOCAL directory, never a transport/session/ACK capability.</summary>
public enum MZP5RecoveryPreparationState
{
    Ready,
    ApprovalMissing,
    StoreNotReady,
    CandidateMissing,
    UnsafeDirectory,
    IoDenied
}

public sealed record MZP5RecoveryPreparationResult(
    MZP5RecoveryPreparationState State, bool DirectoryCreated)
{
    public bool LocalDirectoryReady => State == MZP5RecoveryPreparationState.Ready;
    public bool MayConnect => false;
    public bool CanStartHost => false;
    public bool ServerAckVerified => false;
}

public sealed partial class MZP3TestAccountService
{
    /// <summary>
    /// Prepare only this already opened and exclusively leased MZP3 run's
    /// owner-only recovery DIRECTORY. No journal entry, account or process is
    /// created/read. Explicit approval is required on each invocation.
    /// Cannot be used to select an arbitrary filesystem path.
    /// </summary>
    public MZP5RecoveryPreparationResult PrepareIsolatedRecovery(bool explicitOperatorApproval)
    {
        if (!explicitOperatorApproval)
            return new(MZP5RecoveryPreparationState.ApprovalMissing, false);

        lock (_gate)
        {
            try
            {
                EnsureActive();
                if (_state != MZP3ServiceState.Ready || _dirty || _lease is null)
                    return new(MZP5RecoveryPreparationState.StoreNotReady, false);
                if (_selectedProfileId is not { } id ||
                    !_profiles.Any(p => p.Id == id && p.IsOfflineCandidate))
                    return new(MZP5RecoveryPreparationState.CandidateMissing, false);

                // Original test service already checks every parent, ACL of the
                // exclusive owner, store digest and symlinks. Recheck immediately
                // before creating anything to avoid trusting a stale preflight.
                EnsurePathsAndPermissions();
                CheckNoReparse(_recoveryDirectory);
                if (File.Exists(_recoveryDirectory) && !Directory.Exists(_recoveryDirectory))
                    return new(MZP5RecoveryPreparationState.UnsafeDirectory, false);

                var created = false;
                if (!Directory.Exists(_recoveryDirectory))
                {
                    // Create with an explicit protected owner-only DACL, not
                    // inherited permissions from an unrelated directory.
                    FileSystemAclExtensions.CreateDirectory(PrivateDirectoryAcl(), _recoveryDirectory);
                    created = true;
                }

                CheckNoReparse(_recoveryDirectory);
                var permissions = FileSystemAclExtensions.GetAccessControl(
                    new DirectoryInfo(_recoveryDirectory));
                if (!permissions.AreAccessRulesProtected)
                    return new(MZP5RecoveryPreparationState.UnsafeDirectory, created);
                ValidateAccessRules(permissions);
                return new(MZP5RecoveryPreparationState.Ready, created);
            }
            catch (MZP3StoreException)
            {
                return new(MZP5RecoveryPreparationState.UnsafeDirectory, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                return new(MZP5RecoveryPreparationState.IoDenied, false);
            }
        }
    }
}
