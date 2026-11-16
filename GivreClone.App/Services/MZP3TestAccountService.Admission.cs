using GivreClone.Engine;

namespace GivreClone.App.Services;

/// <summary>
/// Separate adapter; the pure offline store compiles without MZP2/GivreCore.
/// This file is included in GivreClone.App, but the offline Avalonia workbench can
/// link only MZP3TestAccountService.cs without introducing runtime dependencies.
/// </summary>
public sealed partial class MZP3TestAccountService
{
    /// <summary>
    /// Prepares the real MZP2 request from independently supplied permit and
    /// Engine authorities. It neither creates/validates an owner permit nor
    /// authenticates a server response, reads accounts or starts BotHostService.
    /// </summary>
    public MZP2TestClientAdmissionRequest PrepareMZP2Request(
        MZP1OwnedClientPermit externallyVerifiedPermit,
        MZP2TrustedEnginePorts independentlyVerifiedPorts,
        int testRelayPort, bool explicitOperatorApproval)
    {
        lock (_gate)
        {
            EnsureActive();
            if (!explicitOperatorApproval) throw new MZP3StoreException(MZP3Failure.ApprovalRequired);
            if (_dirty) throw new MZP3StoreException(MZP3Failure.UnsavedChanges);
            if (externallyVerifiedPermit is null || independentlyVerifiedPorts is null)
                throw new MZP3StoreException(MZP3Failure.CandidateNotExplicit);
            if (testRelayPort is < 1024 or > 65535 or 5657 or 5658)
                throw new MZP3StoreException(MZP3Failure.InvalidPort);
            if (_selectedProfileId is not { } id || !_profiles.Any(p => p.Id == id && p.IsOfflineCandidate))
                throw new MZP3StoreException(MZP3Failure.NoSelection);
            EnsurePathsAndPermissions();
            return new MZP2TestClientAdmissionRequest(true,
                externallyVerifiedPermit, independentlyVerifiedPorts, _recoveryDirectory,
                new MZP2IsolatedTestHostConfiguration(testRelayPort,
                    _externalAccountStorePath, true));
        }
    }
}
