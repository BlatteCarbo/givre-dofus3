using System.IO;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // Unlike per-PID error display, this non-fiscal journal survives process
    // restart and is shared by direct, manual, Bulk, Lua and relist senders.
    // The separate HdvTaxReservations journal remains the only tax budget.
    private readonly HdvDirectMutationJournal _hdvDirectMutations = new(
        Path.Combine(MZP19LocalClone.DataRoot, "hdv", "direct-mutations-v1.journal"));

    private static string HdvMutationProfile(HdvMutationSession session)
        => HdvDirectMutationJournal.Profile(session.ServerId, session.CharacterId);

    private string HdvCurrentMutationProfile(uint pid)
        => HdvDirectMutationJournal.Profile(ServerIdOf(pid), CharacterId(pid));

    /// <summary>
    /// Explicit operator inspection of this character's actual durable HDV
    /// mutations. Never used as an ACK or permission to submit another order.
    /// A PID reuse during disk IO invalidates the snapshot before returning.
    /// </summary>
    public HdvDirectMutationSnapshot HdvReadMutationSnapshot(uint pid, int maxRows = 32)
    {
        int server = ServerIdOf(pid);
        long character = CharacterId(pid);
        string profile = HdvDirectMutationJournal.Profile(server, character);
        HdvDirectMutationSnapshot Denied(string why)
            => new(profile, HdvDirectMutationReadState.IdentityMissing,
                System.Array.Empty<HdvDirectMutationHistoryRow>(), why);
        if (pid == 0 || server <= 0 || character <= 0)
            return Denied("server/character identity unavailable");
        var snapshot = _hdvDirectMutations?.ReadSnapshot(profile, maxRows) ??
            new(profile, HdvDirectMutationReadState.StorageUnavailable,
                System.Array.Empty<HdvDirectMutationHistoryRow>(),
                "durable journal unavailable");
        if (ServerIdOf(pid) != server || CharacterId(pid) != character)
            return Denied("PID owner replaced during disk inspection");
        return snapshot;
    }

    /// <summary>
    /// Advisory session lease for a real HDV mutation. The observed server and
    /// character and the legacy decoder's LOCAL lifetime must all remain
    /// unchanged across the outgoing request and attributable listing events.
    /// None of these fields proves TCP ownership or an accepted server ACK.
    /// </summary>
    private readonly record struct HdvMutationSession(int ServerId, long CharacterId,
        long DecoderEpoch);

    // Mode transitions must pin provenance BEFORE the resulting seller/buyer
    // event sets the new mode. They are not stock mutations and do not enter
    // the mutation journal; the subsequent order still must pass that gate.
    private HdvMutationSession? CaptureHdvIdentitySession(uint pid)
    {
        if (pid == 0 || !HdvAnyOpen(pid) || IsLinkSuspended(pid)) return null;
        int server = ServerIdOf(pid);
        long character = CharacterId(pid);
        long epoch = CurrentLocalProductionDecoderEpoch(pid);
        return server > 0 && character > 0 && epoch > 0
            ? new HdvMutationSession(server, character, epoch)
            : null;
    }

    private bool IsCurrentHdvIdentitySession(uint pid, HdvMutationSession session)
        => HdvAnyOpen(pid) && !IsLinkSuspended(pid) &&
           session.ServerId > 0 && session.CharacterId > 0 && session.DecoderEpoch > 0 &&
           ServerIdOf(pid) == session.ServerId &&
           CharacterId(pid) == session.CharacterId &&
           CurrentLocalProductionDecoderEpoch(pid) == session.DecoderEpoch;

    private HdvMutationSession? CaptureHdvMutationSession(uint pid, bool buyer = false)
    {
        if (!(buyer ? HdvBuyerOpen(pid) : HdvSellerOpen(pid))) return null;
        return CaptureHdvIdentitySession(pid);
    }

    private bool IsCurrentHdvMutationSession(uint pid, HdvMutationSession session, bool buyer = false)
        => (buyer ? HdvBuyerOpen(pid) : HdvSellerOpen(pid)) &&
           IsCurrentHdvIdentitySession(pid, session);
}
