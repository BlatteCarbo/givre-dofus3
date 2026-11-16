using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace MZP1GuardedWiringHarness;

/// <summary>
/// Integration through the REAL MZP1GuardedEngineWiring dispatch, not a direct
/// call to the new map reader. Test authorities are OFFLINE ONLY, not a real
/// owner/response verifier, and cannot establish Dofus runtime certification.
/// </summary>
public sealed class MZP13GuardedStateTests
{
    private const int Connection = 107;
    private const int Process = 4107;
    private const string Version = "3.7.11.11";
    private static readonly MZP0SessionIdentity Identity =
        new(Process, 638960000000000000, Connection, Version);

    private sealed class Source : IMZP0AuthenticatedSessionSource
    {
        public bool Enabled = true;
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        {
            identity = Identity;
            return Enabled;
        }
    }
    private sealed class Verifier : IMZP0PositiveResponseVerifier
    {
        public bool IsVerifiedPositiveResponse(BotMessage inbound,
            MZP0SessionGeneration expectedGeneration, int uid) => false;
    }
    private sealed class Sink : IFrameSink
    {
        public bool Send(Direction direction, byte[] bytes)
            => throw new Exception("State test must never send network data");
    }
    private static ProtocolCatalog Catalog()
        => ProtocolCatalog.LoadFromStrings(
            "message MapCurrentEvent { int64 map_id = 1; }",
            """{"type.ankama.com/mapcandidate":"MessageType.MapCurrentEvent"}""",
            "{}",
            """{"MapCurrentEvent":{"map_id":7}}""");

    private static CapturedMessage Capture(long map, uint process = Process,
        Direction direction = Direction.ServerToClient, string code = "mapcandidate")
        => new(1, direction, Connection, process, code,
            [new ProtoField { Field = 7, Wire = WireType.Varint, Varint = (ulong)map }],
            []);

    [Fact]
    public void Only_guarded_identity_matching_inbound_can_fold_map_and_revocation_clears_it()
    {
        var temp = Path.Combine(Path.GetTempPath(), "mzp13-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var journal = new MZP0RecoveryJournal(Path.Combine(temp, "journal.json"));
            var source = new Source();
            var wiring = MZP1GuardedEngineWiringFactory.Create(
                Connection, Version, Catalog(), new Sink(), new NoPacer(),
                journal, source, new Verifier());
            Assert.Equal(MZP1Admission.Ready, wiring.Admission);
            Assert.Null(wiring.State.MapId);
            Assert.Equal(MZP1CaptureResult.MissingProcessProof,
                wiring.TryDispatchCaptured(Capture(41, process: 0)));
            Assert.Null(wiring.State.MapId);
            Assert.Equal(MZP1CaptureResult.MismatchedSession,
                wiring.TryDispatchCaptured(Capture(42, process: 1000)));
            Assert.Null(wiring.State.MapId);
            Assert.Equal(MZP1CaptureResult.Accepted,
                wiring.TryDispatchCaptured(Capture(191105026)));
            Assert.Equal(191105026L, wiring.State.MapId);
            Assert.True(wiring.State.MapKnown);
            Assert.False(wiring.State.FightKnown);
            Assert.Equal(MZP1CaptureResult.Accepted,
                wiring.TryDispatchCaptured(Capture(101, direction: Direction.ClientToServer)));
            Assert.Equal(191105026L, wiring.State.MapId);
            source.Enabled = false;
            Assert.Equal(MZP1Admission.Revoked, wiring.Admission);
            Assert.Null(wiring.State.MapId);
            Assert.False(wiring.State.MapKnown);
            Assert.Equal(MZP1CaptureResult.Blocked,
                wiring.TryDispatchCaptured(Capture(192000111)));
            Assert.Null(wiring.State.MapId);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
