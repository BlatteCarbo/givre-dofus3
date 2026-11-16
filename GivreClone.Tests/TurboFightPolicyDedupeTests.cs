using System.IO;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

/// <summary>MITM Turbo : chaque confirmation (tour prêt, accusé) part UNE seule fois vers le serveur, comme en Socket.</summary>
public class TurboFightPolicyDedupeTests
{
    private const uint Pid = 14296;

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(MappingScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Ready_bot_first_then_client_copy_for_same_marker_is_dropped()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteReadyMarker(Pid);
        Assert.True(d.TryBeginBotReady(Pid, 100));            // le bot répond tout de suite : latence inchangée
        Assert.True(d.ShouldDropClientReady(Pid, 900));       // copie du client visible : retirée
        Assert.True(d.ShouldDropClientReady(Pid, 901));       // et toute autre copie du client pour ce marqueur aussi
    }

    [Fact]
    public void Ready_second_client_ready_for_same_marker_is_dropped_after_first_forwarded()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteReadyMarker(Pid);
        Assert.False(d.ShouldDropClientReady(Pid, 100));      // le client est le premier : transmis
        Assert.True(d.ShouldDropClientReady(Pid, 101));       // un 2e du client pour ce marqueur : retiré
        Assert.False(d.TryBeginBotReady(Pid, 102));           // et le bot ne double pas
    }

    [Fact]
    public void Ready_bot_sends_once_per_marker_and_each_marker_starts_fresh()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteReadyMarker(Pid);
        Assert.True(d.TryBeginBotReady(Pid, 100));
        Assert.True(d.ShouldDropClientReady(Pid, 200));
        d.NoteReadyMarker(Pid);                               // marqueur suivant
        Assert.True(d.TryBeginBotReady(Pid, 300));
        Assert.True(d.ShouldDropClientReady(Pid, 400));
    }

    [Fact]
    public void Ready_early_bot_ready_absorbs_the_clients_early_ready()
    {
        var d = new TurboUpstreamDedupe();
        Assert.True(d.TryBeginBotReady(Pid, 10, early: true)); // « tour prêt » anticipé (début de combat), avant tout marqueur
        Assert.True(d.ShouldDropClientReady(Pid, 20));        // le client visible envoie le sien : retiré
    }

    [Fact]
    public void Ready_early_bot_ready_does_not_consume_the_first_markers_answer()
    {
        var d = new TurboUpstreamDedupe();
        Assert.True(d.TryBeginBotReady(Pid, 10, early: true));
        d.NoteReadyMarker(Pid);
        Assert.True(d.TryBeginBotReady(Pid, 500));            // le marqueur reçoit bien SA réponse du bot
        Assert.False(d.TryBeginBotReady(Pid, 501));           // mais une seule
    }

    [Fact]
    public void Ready_failed_injection_lets_the_client_answer()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteReadyMarker(Pid);
        Assert.True(d.TryBeginBotReady(Pid, 10));
        d.UndoBotReady(Pid);                                  // l'injection a échoué
        Assert.False(d.ShouldDropClientReady(Pid, 20));       // le client reste la seule source
    }

    [Fact]
    public void Ack_client_copy_after_bot_ack_for_same_action_is_dropped_once()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteBotAck(Pid, 42, now: 100);
        Assert.True(d.ShouldDropClientAck(Pid, 42, 150));
        Assert.False(d.ShouldDropClientAck(Pid, 42, 151));    // un seul accusé par séquence : pas de double suppression
        Assert.False(d.ShouldDropClientAck(Pid, 43, 152));    // autre action : transmis
    }

    [Fact]
    public void Ack_foreign_sequence_ack_is_deduped_like_our_own()
    {
        var d = new TurboUpstreamDedupe();
        // InjectTurboAckFor (séquence d'un autre combattant) enregistre l'accusé du bot comme pour nos séquences.
        d.NoteBotAck(Pid, 7, now: 100);
        Assert.True(d.HasPendingAcks);
        Assert.True(d.ShouldDropClientAck(Pid, 7, 200));
        Assert.False(d.HasPendingAcks);
    }

    [Fact]
    public void Ack_reused_action_id_each_bot_ack_absorbs_exactly_one_client_ack()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteBotAck(Pid, 3, 100);
        d.NoteBotAck(Pid, 3, 110);                            // même action_id réutilisé par une 2e séquence
        Assert.True(d.ShouldDropClientAck(Pid, 3, 120));
        Assert.True(d.ShouldDropClientAck(Pid, 3, 130));
        Assert.False(d.ShouldDropClientAck(Pid, 3, 140));
    }

    [Fact]
    public void Ack_stale_bot_ack_expires_and_never_eats_a_later_client_ack()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteBotAck(Pid, 5, 100);
        Assert.False(d.ShouldDropClientAck(Pid, 5, 100 + TurboUpstreamDedupe.ClientCopyGraceMs + 1));
        Assert.False(d.HasPendingAcks);
    }

    [Fact]
    public void Ack_foreign_copy_expires_quickly_because_the_real_client_never_acks_foreign_sequences()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteBotAck(Pid, 11, 100, foreign: true);
        Assert.False(d.ShouldDropClientAck(Pid, 11, 100 + TurboUpstreamDedupe.ForeignCopyGraceMs + 1));
        d.NoteBotAck(Pid, 11, 100, foreign: true);
        Assert.True(d.ShouldDropClientAck(Pid, 11, 100 + TurboUpstreamDedupe.ForeignCopyGraceMs));
    }

    [Fact]
    public void Ack_failed_injection_is_undone_and_reset_clears_the_pid()
    {
        var d = new TurboUpstreamDedupe();
        d.NoteBotAck(Pid, 9, 100);
        d.UndoBotAck(Pid, 9);
        Assert.False(d.ShouldDropClientAck(Pid, 9, 101));
        d.NoteBotAck(Pid, 9, 100);
        d.NoteBotAck(Pid + 1, 9, 100);
        d.Reset(Pid);
        Assert.False(d.ShouldDropClientAck(Pid, 9, 101));
        Assert.True(d.ShouldDropClientAck(Pid + 1, 9, 101));
    }

    [Fact]
    public void Mitm_has_no_ready_relance_burst()
    {
        var combat = Source("GivreClone.App/Services/LaunchService.Combat.cs");
        var packets = Source("GivreClone.App/Services/LaunchService.Packets.cs");
        Assert.DoesNotContain("TurboReadyBurst", combat + packets);
        Assert.DoesNotContain("_turboReadyGen", combat + packets);
        Assert.DoesNotContain("relance ", combat + packets);
    }

    [Fact]
    public void Mitm_upstream_filter_covers_ready_and_foreign_acks()
    {
        var combat = Source("GivreClone.App/Services/LaunchService.Combat.cs");
        int from = combat.IndexOf("private void InjectTurboAckFor", StringComparison.Ordinal);
        int to = combat.IndexOf("private void InjectTurboReady", StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from);
        Assert.Contains("_turboDedupe.NoteBotAck", combat.Substring(from, to - from));   // acks étrangers inclus
        Assert.Contains("\"FightTurnReadyRequest\"", combat);
        Assert.Contains("_turboDedupe.ShouldDropClientReady", combat);
        Assert.Contains("_turboDedupe.TryBeginBotReady", combat);
    }
}
