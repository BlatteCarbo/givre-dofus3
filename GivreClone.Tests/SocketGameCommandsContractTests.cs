using System;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline contract checks against production source. The tests intentionally
/// do not load account configuration, TCP sockets, token-bearing logs or
/// invoke any gameplay action. They cannot certify a real server ACK.
/// </summary>
public sealed class SocketGameCommandsContractTests
{
    private static string Source(string file = "SocketGameClient.cs")
    {
        DirectoryInfo? root = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName,
            "GivreClone.App", "Services", "Transport", "Socket", file)));
        if (root is null) throw new DirectoryNotFoundException("Repo root missing.");
        return File.ReadAllText(Path.Combine(root.FullName,
            "GivreClone.App", "Services", "Transport", "Socket", file));
    }

    [Fact]
    public void Review_pilot_is_an_opt_in_not_a_login_side_effect()
    {
        var source = Source();
        Assert.Contains("public bool ReviewedCrossPatchPilot { get; init; }", source);
        Assert.Contains("bool reviewedMoveOnly = !exactGameplayMapping &&", source);
        Assert.Contains("p.ReviewedCrossPatchPilot &&", source);
        Assert.Contains("OperatorReviewedTarget", source);
        Assert.Contains("IdenticalProtoSha256", source);
    }

    [Fact]
    public void Injector_is_published_only_after_character_success_and_server_context()
    {
        var source = Source();
        int selection = source.IndexOf("case \"CharacterSelectionEvent\":", StringComparison.Ordinal);
        int context = source.IndexOf("case \"ContextCreationEvent\":", StringComparison.Ordinal);
        int forced = source.IndexOf("case \"FightJoinRunningEvent\":", StringComparison.Ordinal);
        int publish = source.IndexOf("if (p.RegisterInjectorWithAdmission is { } register)", StringComparison.Ordinal);
        int ready = source.IndexOf("Volatile.Write(ref _injectionReady, 1);", StringComparison.Ordinal);
        Assert.True(selection > 0 && context > selection && forced > selection);
        Assert.True(publish > context && ready > publish);
        // The old unconditional registration was replaced by an actual
        // session-lease admission: a stale/superseded Socket client must not
        // be able to publish an injector under the reused synthetic PID.
        Assert.Contains("if (!register(_logPid, Inject)) return;", source);
        Assert.Contains("else p.RegisterInjector!(_logPid, Inject);", source);
        Assert.Contains("Volatile.Write(ref _injectorPublished, 1);", source[publish..ready]);
        Assert.Contains("if (!_selSuccess || !gameContextSeen", source);
        Assert.Contains("PublishGameplayInjectorIfReady(name);", source);
    }

    [Fact]
    public void Never_register_dispatch_at_tcp_connect_before_selection()
    {
        var source = Source();
        int tcp = source.IndexOf("connexion TCP établie", StringComparison.Ordinal);
        int selection = source.IndexOf("case \"CharacterSelectionEvent\":", StringComparison.Ordinal);
        Assert.True(tcp > 0 && selection > tcp);
        Assert.DoesNotContain("p.RegisterInjector(_logPid, Inject)",
            source[tcp..selection], StringComparison.Ordinal);
    }

    [Fact]
    public void Reconnection_into_existing_fight_still_requires_selected_character()
    {
        var source = Source();
        int start = source.IndexOf("case \"FightJoinRunningEvent\":", StringComparison.Ordinal);
        int end = source.IndexOf("case \"FightSynchronizeEvent\":", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        Assert.Contains("gameContextSeen = true;", source[start..end]);
        Assert.Contains("PublishGameplayInjectorIfReady(name)", source[start..end]);
        Assert.Contains("!_selSuccess || !gameContextSeen", source);
    }

    [Fact]
    public void Review_rejects_every_unlisted_gameplay_command()
    {
        var source = Source();
        int classifier = source.IndexOf("_reviewClassifier = framed =>", StringComparison.Ordinal);
        int end = source.IndexOf("Volatile.Write(ref _reviewDiagnosticMode, 1);",
            classifier, StringComparison.Ordinal);
        Assert.True(classifier > 0 && end > classifier);
        var region = source[classifier..end];
        Assert.Contains("hit.Code == confirmCode", region);
        Assert.Contains("hit.Code != moveCode", region);
        Assert.DoesNotContain("ReviewCommand.Attack", region);
        Assert.DoesNotContain("FightSpellCastRequest", region);
        Assert.DoesNotContain("ChallengeSelectionRequest", region);
    }

    [Fact]
    public void Review_movement_is_exactly_one_in_flight_until_observed_response()
    {
        var source = Source();
        Assert.Contains("Interlocked.CompareExchange(ref _reviewMoveState, 1, 0)", source);
        Assert.Contains("Interlocked.CompareExchange(ref _reviewMoveState, 1, 4)", source);
        Assert.Contains("Interlocked.CompareExchange(ref _reviewMoveState, 3, 2)", source);
        Assert.Contains("Interlocked.CompareExchange(ref _reviewMoveState, 4, 3)", source);
        Assert.Contains("if (_inFight) return false", source);
    }

    [Fact]
    public void Review_move_needs_structurally_mapped_request_content()
    {
        var source = Source();
        Assert.Contains("cat.CodeForName(\"MapMovementRequest\")", source);
        Assert.Contains("cat.CodeForName(\"MapMovementConfirmRequest\")", source);
        Assert.Contains("cat.FieldNumber(\"GameMessage\", \"request\")", source);
        Assert.Contains("cat.FieldNumber(\"MapMovementRequest\", \"key_cells\")", source);
        Assert.Contains("cat.FieldNumber(\"MapMovementRequest\", \"map_id\")", source);
        Assert.Contains("declared != (ulong)(framed.Length - prefixLen)", source);
        Assert.Contains("tree.Count != 1 || tree[0].Field != requestSlot", source);
    }

    [Fact]
    public void Only_self_movement_echo_can_enable_confirmation()
    {
        var source = Source();
        Assert.Contains("case \"MapMovementEvent\":", source);
        Assert.Contains("ownMove.ActorId == _selfId", source);
        Assert.Contains("CompareExchange(ref _reviewMoveState, 2, 1)", source);
        Assert.Contains("écho de NOTRE acteur", source);
    }

    [Fact]
    public void A_server_movement_response_is_not_renamed_gameplay_certification()
    {
        var source = Source();
        Assert.Contains("case \"MapMovementConfirmResponse\":", source);
        Assert.Contains("corrélation UID non prouvée", source);
        Assert.Contains("case \"MapMovementRefusedEvent\":", source);
        Assert.DoesNotContain("GameplayCertified = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Timed_out_commands_fail_closed_for_that_session()
    {
        var source = Source();
        Assert.Contains("Later(20_000, \"diagnostic mouvement sans réponse\"", source);
        Assert.Contains("Volatile.Read(ref _reviewMoveGeneration) == generation", source);
        Assert.Contains("CompareExchange(ref _reviewMoveState, -1, 3)", source);
        Assert.Contains("Volatile.Write(ref _injectionReady, 0)", source);
    }

    [Fact]
    public void Buffer_is_cloned_before_queuing_not_reused_by_delayed_writer()
    {
        var source = Source();
        Assert.Contains("var ownedFrame = (byte[])framedToServer.Clone();", source);
        // V9 moved the owned buffer into a source-locked admission that
        // mints a queue ordinal. It must still enqueue this SAME cloned
        // buffer, never the mutable frame supplied by the caller.
        Assert.Contains("queued = _outbound.Writer.TryWrite(new Outgoing(", source);
        Assert.Contains("ownedFrame,", source);
        Assert.Contains("var ownedFrame = (byte[])framedToServer.Clone();", source);
        // Pure aliasing regression illustrating the actual bug fixed above.
        var q = Channel.CreateBounded<byte[]>(1);
        var input = new byte[] { 3, 4, 5 };
        Assert.True(q.Writer.TryWrite((byte[])input.Clone()));
        input[1] = 99;
        Assert.True(q.Reader.TryRead(out var committed));
        Assert.Equal(new byte[] { 3, 4, 5 }, committed);
    }

    [Fact]
    public void Request_arrival_stays_distinct_from_transport_enqueue()
    {
        var source = Source();
        Assert.Contains("PAS accusé serveur", source);
        Assert.Contains("WriteQueuedEngineActionAsync(stream!, o, p, tk)", source);
        Assert.Contains("await target.WriteAsync(item.Frame, ct)", source);
        Assert.Contains("await target.FlushAsync(ct)", source);
        Assert.Contains("public bool ServerAcknowledged => false", source);
        Assert.Contains("ReadAllAsync(tk)", source);
        Assert.Contains("Channel.CreateBounded<Outgoing>", source);
    }

    [Fact]
    public void On_session_end_readiness_is_revoked_before_dispatch_unregistration()
    {
        var source = Source();
        int disabled = source.LastIndexOf("Volatile.Write(ref _injectionReady, 0);",
            StringComparison.Ordinal);
        int published = source.LastIndexOf("Volatile.Write(ref _injectorPublished, 0);",
            StringComparison.Ordinal);
        int remove = source.LastIndexOf("p.UnregisterInjector?.Invoke(_logPid)",
            StringComparison.Ordinal);
        Assert.True(disabled > 0 && published > disabled && remove > published);
    }

    [Fact]
    public void No_live_network_or_secrets_are_needed_for_contract_validation()
    {
        var source = Source();
        Assert.Contains("SocketLog.Tx(_logPid, $\"[jeu] ⇒ injection pilotage {o.Frame.Length}o\"", source);
        Assert.Contains("SocketLog.Tx(_logPid, $\"[jeu] → IdentificationRequest {loginFramed.Length}o\")", source);
    }
}
