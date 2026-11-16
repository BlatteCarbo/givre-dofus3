using System;
using System.Linq;
using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class CombatJournalTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 14, 30, 0);

    [Theory]
    [InlineData("text message=170 params=[2 | 3] text=PA insuffisants sequence_started=False")]
    [InlineData("text message=999 params=[] text=<texte non résolu> sequence_started=False")]
    public void Server_cast_messages_are_retained_in_the_fight_replay(string payload)
    {
        var recorder = new CombatJournalRecorder(7);
        var fight = Play(recorder,
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat-cast] " + payload),
            (2, "[combat] pid 7 : bilan -> issue=2 xp=0 butin=[]"));

        Assert.NotNull(fight);
        Assert.Contains(fight.Entries, entry => entry.Text.EndsWith(payload, StringComparison.Ordinal));
    }

    private static FightJournal? Play(CombatJournalRecorder r, params (double Sec, string Line)[] lines)
    {
        FightJournal? last = null;
        foreach (var (sec, line) in lines) if (r.Feed(T0.AddSeconds(sec), line) is { } f) last = f;
        return last;
    }

    [Fact]
    public void Decisions_and_enemy_moves_do_not_inflate_sent_actions()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat] sort 161 -> cible 120 (dist 3)"),
            (2, "[combat] pid 7 : lance le sort 161 sur la cellule 120 inject=OK"),
            (3, "[combat] ennemi -2 déplacé -> case 121 (déplacement suivi)"),
            (4, "[turbo-metric] move_request_to_server_confirmed_ms=20 from=100 target=104 final=104"),
            (5, "[combat] pid 7 : bilan -> issue=2 xp=0 butin=[]"))!;

        Assert.Equal(1, fight.Casts);
        Assert.Equal(1, fight.Moves);
    }

    [Fact]
    public void End_of_turn_approach_is_counted_until_the_finish_request()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat] fin de tour : rapprochement -> 311 (2 PM, dist cible 3)"),
            (2, "[turbo-metric] move_request_to_server_confirmed_ms=15 from=340 target=311 final=311"),
            (3, "[combat] pid 7 : fin du tour (FightTurnFinishRequest) inject=OK"),
            (4, "[combat] pid 7 : bilan -> issue=0 xp=0 butin=[]"))!;

        Assert.Equal(1, fight.TurnStats.Single().Moves);
        Assert.Equal(3, fight.TurnStats.Single().EndSec);
    }

    [Fact]
    public void Companion_turn_does_not_mask_an_empty_player_turn_or_replace_player_hp()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat] PV tour 1 : moi 200/1000 | alliés [123:3900/4000]"),
            (2, "[combat] pid 7 : fin du tour (FightTurnFinishRequest) inject=OK"),
            (3, "[combat] début de tour : COMPAGNON -1"),
            (4, "[combat] PV tour 1 : moi 3900/4000 | alliés [120:200/1000]"),
            (5, "[combat] pid 7 : lance le sort 4245 sur la cellule 150 inject=OK"),
            (6, "[combat] pid 7 : fin du tour (FightTurnFinishRequest) inject=OK"),
            (7, "[combat] pid 7 : bilan -> issue=0 xp=0 butin=[]"))!;

        Assert.Equal(0, fight.Casts);
        Assert.Equal(1, fight.CompanionCasts);
        Assert.Equal(1, fight.TurnsWithoutCast);
        Assert.Equal(0, fight.TurnStats.Single().Casts);
        Assert.Equal("200/1000", fight.TurnStats.Single().Hp);
        Assert.Equal(2, fight.TurnStats.Single().EndSec);
    }

    [Fact]
    public void Lines_that_are_not_combat_are_ignored_and_idle_lines_do_not_open_a_fight()
    {
        var r = new CombatJournalRecorder(7);
        Assert.Null(r.Feed(T0, "[socket] connexion"));
        Assert.Null(r.Feed(T0, "[combat] 3 sorts appris détectés : 1,2,3"));   // pas un début de combat
        Assert.False(r.Active);
        Assert.Null(r.Feed(T0, "[combat] pid 7 : attaque du groupe Bouftou (id 5, AttackMonsterRequest) inject=OK"));
        Assert.True(r.Active);
    }

    [Fact]
    public void A_victory_is_cut_out_with_its_turns_casts_and_result()
    {
        var f = Play(new CombatJournalRecorder(7),
            (0, "[combat] pid 7 : attaque du groupe Bouftou (id 5, AttackMonsterRequest) inject=OK"),
            (2, "[combat] 3 combattant(s) : cell 100=joueur, cell 120=monstre, cell 121=monstre"),
            (3, "[combat] pid 7 : placement -> case 101 inject=OK"),
            (5, "[combat] début de tour : MOI (round 1)"),
            (5.2, "[combat] TOUR style=Aggressive cible=Closest vitesse=Instant sorts=[161] maCellule=101 (PA6 PM3 PO+0)"),
            (5.4, "[combat] PV tour 1 : moi 900/1000 | ennemis [120:100%,121:100%]"),
            (5.6, "[combat] pid 7 : lance le sort 161 sur la cellule 120 inject=OK"),
            (6.0, "[combat] pid 7 : lance le sort 161 sur la cellule 120 inject=OK"),
            (6.5, "[combat] pid 7 : fin du tour (FightTurnFinishRequest) inject=OK"),
            (9, "[combat] début de tour : MOI (round 2)"),
            (9.5, "[combat] pid 7 : lance le sort 161 sur la cellule 121 inject=OK"),
            (10, "[combat] pid 7 : bilan -> issue=2 xp=1234 butin=[303x2, 311x1]"));
        Assert.NotNull(f);
        Assert.Equal("victoire", f!.Outcome);
        Assert.Equal(2, f.Turns);
        Assert.Equal(3, f.Casts);
        Assert.Equal(1234, f.Xp);
        Assert.Equal("303x2, 311x1", f.Loot);
        Assert.Equal(10, f.DurationSec, 3);
        Assert.Equal(0, f.TurnsWithoutCast);
        Assert.Contains("Rien d'anormal relevé.", f.Findings);
        Assert.Equal("900/1000", f.TurnStats[0].Hp);
        Assert.Equal(2, f.TurnStats[0].Casts);
        Assert.False(new CombatJournalRecorder(7).Active);
    }

    [Fact]
    public void A_defeat_without_any_attack_explains_why()
    {
        var f = Play(new CombatJournalRecorder(9),
            (0, "[combat] pid 9 : attaque du groupe Arakne (id 8, AttackMonsterRequest) inject=OK"),
            (3, "[combat] début de tour : MOI (round 1)"),
            (3.1, "[combat] FILET : aucun sort castable de 100 (ennemi le + proche à 6, PA6) sorts=[161]"),
            (3.5, "[combat] déplacement 100->104 refusé par le serveur -> bloqué ce tour. diag: communicates=True"),
            (4, "[combat] pid 9 : fin du tour (FightTurnFinishRequest) inject=OK"),
            (8, "[combat] début de tour : MOI (round 2)"),
            (8.1, "[combat] PV tour 2 : moi 300/1000 | ennemis [120:90%]"),
            (8.2, "[combat] FILET : aucun sort castable de 100 (ennemi le + proche à 5, PA6) sorts=[161]"),
            (8.6, "[combat] déplacement 100->103 refusé par le serveur -> bloqué ce tour. diag: communicates=True"),
            (9, "[combat] pid 9 : fin du tour (FightTurnFinishRequest) inject=ECHEC"),
            (20, "[combat] pid 9 : bilan -> issue=0 xp=0 butin=[]"));
        Assert.NotNull(f);
        Assert.Equal("défaite", f!.Outcome);
        Assert.True(f.Lost);
        Assert.Equal(0, f.Casts);
        Assert.Equal(2, f.RefusedMoves);
        Assert.Equal(1, f.FailedInjections);
        var all = string.Join("\n", f.Findings);
        Assert.Contains("DÉFAITE", all);
        Assert.Contains("Aucun sort lancé", all);
        Assert.Contains("aucun sort castable", all);
        Assert.Contains("déplacements refusés", all);
        Assert.Contains("ECHEC", all);
        Assert.Contains("PV du perso tombés à 30 %", all);
        Assert.True(f.LongestGapSec >= 11);                    // 9 s -> 20 s
        Assert.Contains("Plus longue pause", all);
    }

    [Fact]
    public void Long_turns_and_lost_turns_are_flagged_as_wasted_time()
    {
        var f = Play(new CombatJournalRecorder(1),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat] pid 1 : lance le sort 5 sur la cellule 3 inject=OK"),
            (30, "[combat] pid 1 : fin du tour (FightTurnFinishRequest) inject=OK"),
            (31, "[combat] TOUR IGNORÉ : aucun début de tour (3/3), 1200 ms"),
            (32, "[combat] pid 1 : bilan -> issue=2 xp=10 butin=[]"));
        var all = string.Join("\n", f!.Findings);
        Assert.Contains("plus de 20 s", all);
        Assert.Contains("perdu(s) ou ignoré(s)", all);
    }

    [Fact]
    public void An_inactive_fight_is_closed_when_the_next_one_starts_and_flush_closes_the_current_one()
    {
        var r = new CombatJournalRecorder(2);
        Play(r, (0, "[combat] début de tour : MOI (round 1)"), (1, "[combat] pid 2 : lance le sort 5 sur la cellule 3 inject=OK"));
        var closed = r.Feed(T0.AddMinutes(30), "[combat] pid 2 : attaque du groupe X (id 1, AttackMonsterRequest) inject=OK");
        Assert.NotNull(closed);
        Assert.Contains("inactivité", string.Join(" ", closed!.Findings));
        Assert.True(r.Active);                                     // le nouveau combat est ouvert
        var flushed = r.Flush("arrêt de l'application");
        Assert.NotNull(flushed);
        Assert.False(r.Active);
        Assert.Null(r.Flush("encore"));
    }

    [Fact]
    public void Text_has_summary_findings_turns_and_a_timeline_with_offsets()
    {
        var f = Play(new CombatJournalRecorder(3),
            (0, "[combat] début de tour : MOI (round 1)"),
            (0.5, "[combat] pid 3 : lance le sort 9 sur la cellule 44 inject=OK"),
            (1, "[combat] pid 3 : bilan -> issue=2 xp=5 butin=[]"))!;
        string text = CombatJournalFormat.ToText(f);
        Assert.Contains("=== Combat", text);
        Assert.Contains("VICTOIRE", text);
        Assert.Contains("Constats :", text);
        Assert.Contains("Tours :", text);
        Assert.Contains("Chronologie :", text);
        Assert.Contains("+  0.500 s", text);
        Assert.Contains("SORT", text);
        Assert.StartsWith("14:30:00 · victoire", f.OneLine);
    }

    private const string Fin = "[combat] pid 7 : fin du tour (FightTurnFinishRequest) inject=OK";

    [Theory]
    [InlineData("confirmed", true)]
    [InlineData("refused", true)]
    [InlineData("timeout", true)]
    [InlineData("confirmed", false)]
    [InlineData("refused", false)]
    [InlineData("timeout", false)]
    public void Move_attempts_are_attributed_to_the_acting_side_whatever_the_server_outcome(string outcome, bool companion)
    {
        string request = "[turbo-metric] request_tx kind=move:100->104 t=1 ack_to_request_ms=1";
        string result = $"[turbo-metric] move_request_to_server_{outcome}_ms=20 from=100 target=104 final=100";
        const string End = "[combat] pid 7 : bilan -> issue=2 xp=0 butin=[]";
        var fight = companion
            ? Play(new CombatJournalRecorder(7),
                (0, "[combat] début de tour : MOI (round 1)"), (1, Fin),
                (2, "[combat] début de tour : COMPAGNON -1"), (3, request), (4, result), (5, Fin), (6, End))!
            : Play(new CombatJournalRecorder(7),
                (0, "[combat] début de tour : MOI (round 1)"), (1, request), (2, result), (3, Fin), (4, End))!;

        Assert.Equal(companion ? 0 : 1, fight.Moves);
        Assert.Equal(companion ? 1 : 0, fight.CompanionMoves);
        Assert.Equal(companion ? 0 : 1, fight.TurnStats.Single().Moves);
    }

    [Fact]
    public void A_late_move_result_after_the_companion_finished_stays_with_the_companion_and_never_the_next_self_round()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, "[turbo-metric] request_tx kind=move:299->326 t=1 ack_to_request_ms=1"),
            (4, Fin),
            (5, "[turbo-metric] move_request_to_server_timeout_ms=900 from=299 target=326 final=299"),   // résultat tardif, après la fin du tour du compagnon
            (6, "[combat] début de tour : MOI (round 2)"),
            (7, "[turbo-metric] request_tx kind=move:100->104 t=2 ack_to_request_ms=1"),
            (8, "[turbo-metric] move_request_to_server_confirmed_ms=15 from=100 target=104 final=104"),
            (9, Fin),
            (10, "[combat] pid 7 : bilan -> issue=2 xp=0 butin=[]"))!;

        Assert.Equal(1, fight.CompanionMoves);
        Assert.Equal(1, fight.Moves);
        Assert.Equal(new[] { 0, 1 }, fight.TurnStats.Select(t => t.Moves).ToArray());
    }

    [Fact]
    public void A_move_result_with_no_evidence_of_its_owner_outside_any_turn_is_left_unassigned()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, Fin),
            (4, "[turbo-metric] move_request_to_server_refused_ms=30 from=299 target=326 final=299"),
            (5, "[combat] début de tour : MOI (round 2)"),
            (6, Fin),
            (7, "[combat] pid 7 : bilan -> issue=2 xp=0 butin=[]"))!;

        Assert.Equal(0, fight.Moves);
        Assert.Equal(0, fight.CompanionMoves);
        Assert.Equal(new[] { 0, 0 }, fight.TurnStats.Select(t => t.Moves).ToArray());
    }

    private const string BilanOk = "[combat] pid 7 : bilan -> issue=2 xp=0 butin=[]";

    [Fact]
    public void Late_companion_move_result_after_the_next_self_turn_started_stays_with_the_companion()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, "[turbo-metric] request_tx kind=move:299->326 t=1 ack_to_request_ms=1"),
            (4, Fin),
            (5, "[combat] début de tour : MOI (round 2)"),
            (6, "[turbo-metric] move_request_to_server_timeout_ms=900 from=299 target=326 final=299"),   // résultat tardif : le tour du perso a déjà commencé
            (7, "[turbo-metric] request_tx kind=move:100->104 t=2 ack_to_request_ms=1"),
            (8, "[turbo-metric] move_request_to_server_confirmed_ms=15 from=100 target=104 final=104"),
            (9, Fin),
            (10, BilanOk))!;

        Assert.Equal(1, fight.CompanionMoves);
        Assert.Equal(1, fight.Moves);
        Assert.Equal(new[] { 0, 1 }, fight.TurnStats.Select(t => t.Moves).ToArray());
    }

    [Fact]
    public void Late_self_move_result_after_the_companion_turn_started_stays_with_the_self_round_that_requested_it()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[turbo-metric] request_tx kind=move:100->104 t=1 ack_to_request_ms=1"),
            (2, Fin),
            (3, "[combat] début de tour : COMPAGNON -1"),
            (4, "[turbo-metric] move_request_to_server_timeout_ms=900 from=100 target=104 final=100"),   // résultat tardif du perso, pendant le tour du compagnon
            (5, "[turbo-metric] request_tx kind=move:299->326 t=2 ack_to_request_ms=1"),
            (6, "[turbo-metric] move_request_to_server_confirmed_ms=15 from=299 target=326 final=326"),
            (7, Fin),
            (8, BilanOk))!;

        Assert.Equal(1, fight.Moves);
        Assert.Equal(1, fight.CompanionMoves);
        Assert.Equal(new[] { 1 }, fight.TurnStats.Select(t => t.Moves).ToArray());
    }

    [Fact]
    public void Late_self_move_result_after_a_later_self_round_started_is_booked_on_the_round_of_its_request()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[turbo-metric] request_tx kind=move:100->104 t=1 ack_to_request_ms=1"),
            (2, Fin),
            (3, "[combat] début de tour : COMPAGNON -1"),
            (4, Fin),
            (5, "[combat] début de tour : MOI (round 2)"),
            (6, "[turbo-metric] move_request_to_server_timeout_ms=900 from=100 target=104 final=100"),
            (7, Fin),
            (8, BilanOk))!;

        Assert.Equal(1, fight.Moves);
        Assert.Equal(0, fight.CompanionMoves);
        Assert.Equal(new[] { 1, 0 }, fight.TurnStats.Select(t => t.Moves).ToArray());
    }

    [Fact]
    public void A_move_result_that_matches_no_pending_request_is_not_given_to_the_turn_in_progress()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, "[turbo-metric] request_tx kind=move:299->326 t=1 ack_to_request_ms=1"),
            (4, Fin),
            (5, "[combat] début de tour : MOI (round 2)"),
            (6, "[turbo-metric] move_request_to_server_refused_ms=30 from=300 target=327 final=300"),   // autre case : aucune requête connue ne correspond
            (7, Fin),
            (8, BilanOk))!;

        Assert.Equal(0, fight.Moves);
        Assert.Equal(0, fight.CompanionMoves);
        Assert.Equal(new[] { 0, 0 }, fight.TurnStats.Select(t => t.Moves).ToArray());
    }

    [Fact]
    public void An_abandoned_move_request_does_not_steal_the_result_of_the_same_move_asked_later_by_the_other_actor()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, "[turbo-metric] request_tx kind=move:299->326 t=1 ack_to_request_ms=1"),   // jamais de résultat
            (4, Fin),
            (5, "[combat] début de tour : MOI (round 2)"),
            (6, "[turbo-metric] request_tx kind=move:299->326 t=2 ack_to_request_ms=1"),
            (7, "[turbo-metric] move_request_to_server_confirmed_ms=15 from=299 target=326 final=326"),
            (8, Fin),
            (9, BilanOk))!;

        Assert.Equal(1, fight.Moves);
        Assert.Equal(0, fight.CompanionMoves);
        Assert.Equal(new[] { 0, 1 }, fight.TurnStats.Select(t => t.Moves).ToArray());
    }

    // Ligne canonique du producteur : le propriétaire et le round sont explicites, elle ne dépend ni de l'acteur courant ni des en-têtes de tour.
    private const string CanonCompanion = "[combat] move_result owner=companion round=1 from=299 target=326 final=299 outcome=timeout";
    private const string CanonSelf = "[combat] move_result owner=self round=1 from=100 target=104 final=104 outcome=confirmed";

    [Fact]
    public void Canonical_move_result_counts_once_with_its_explicit_owner_and_round_even_after_headers_changed()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, Fin),
            (4, "[combat] début de tour : MOI (round 2)"),
            (5, CanonCompanion),   // résultats arrivés après deux changements d'en-tête
            (6, CanonSelf),
            (7, Fin),
            (8, BilanOk))!;

        Assert.Equal(1, fight.Moves);
        Assert.Equal(1, fight.CompanionMoves);
        Assert.Equal(new[] { 1, 0 }, fight.TurnStats.Select(t => t.Moves).ToArray());
        Assert.Equal(new[] { JournalKind.Move, JournalKind.Move }, fight.Entries.Where(e => e.Text.StartsWith("move_result", StringComparison.Ordinal)).Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void Canonical_results_with_the_same_endpoints_but_different_owners_are_each_counted_for_their_owner()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, "[combat] move_result owner=companion round=1 from=299 target=326 final=326 outcome=confirmed"),
            (4, Fin),
            (5, "[combat] début de tour : MOI (round 2)"),
            (6, "[combat] move_result owner=self round=2 from=299 target=326 final=299 outcome=refused"),
            (7, Fin),
            (8, BilanOk))!;

        Assert.Equal(1, fight.CompanionMoves);
        Assert.Equal(1, fight.Moves);
        Assert.Equal(new[] { 0, 1 }, fight.TurnStats.Select(t => t.Moves).ToArray());
        Assert.DoesNotContain(fight.Entries, e => e.Kind == JournalKind.Problem);   // « outcome=refused » n'est pas un souci de tour
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("refused")]
    [InlineData("timeout")]
    public void A_canonical_result_and_its_marked_turbo_metric_are_one_move_not_two(string outcome)
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[turbo-metric] request_tx kind=move:100->104 t=1 ack_to_request_ms=1"),
            (2, $"[combat] move_result owner=self round=1 from=100 target=104 final=104 outcome={outcome}"),
            (3, $"[turbo-metric] move_request_to_server_{outcome}_ms=20 from=100 target=104 final=104 journal=canonical"),
            (4, Fin),
            (5, BilanOk))!;

        Assert.Equal(1, fight.Moves);
        Assert.Equal(0, fight.CompanionMoves);
        Assert.Equal(1, fight.TurnStats.Single().Moves);
        Assert.Equal(1, fight.Entries.Count(e => e.Kind == JournalKind.Move));
    }

    [Fact]
    public void A_canonical_result_with_an_unknown_owner_is_not_counted_for_anybody()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat] move_result owner=enemy round=1 from=100 target=104 final=104 outcome=confirmed"),
            (2, Fin),
            (3, BilanOk))!;

        Assert.Equal(0, fight.Moves);
        Assert.Equal(0, fight.CompanionMoves);
    }

    [Fact]
    public void A_late_self_legacy_result_after_the_same_self_finish_does_not_extend_the_round_end()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[turbo-metric] request_tx kind=move:100->104 t=1 ack_to_request_ms=1"),
            (2, Fin),
            (9, "[turbo-metric] move_request_to_server_timeout_ms=900 from=100 target=104 final=100"),   // après la fin du tour, avant tout nouvel en-tête
            (10, BilanOk))!;

        Assert.Equal(1, fight.Moves);
        var round = fight.TurnStats.Single();
        Assert.Equal(1, round.Moves);
        Assert.Equal(2, round.EndSec);
    }

    [Fact]
    public void A_late_self_canonical_result_does_not_extend_the_same_or_an_earlier_closed_round()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (3, Fin),
            (4, "[combat] move_result owner=self round=1 from=100 target=104 final=100 outcome=timeout"),   // même tour, après sa fin
            (5, "[combat] début de tour : COMPAGNON -1"),
            (6, Fin),
            (7, "[combat] début de tour : MOI (round 2)"),
            (8, Fin),
            (12, "[combat] move_result owner=self round=1 from=100 target=104 final=104 outcome=confirmed"),   // tour 1 déjà clos, tour 2 clos aussi
            (13, BilanOk))!;

        Assert.Equal(2, fight.Moves);
        Assert.Equal(new[] { 2, 0 }, fight.TurnStats.Select(t => t.Moves).ToArray());
        Assert.Equal(new double[] { 3, 8 }, fight.TurnStats.Select(t => t.EndSec).ToArray());
    }

    // Sorts : la ligne d'injection porte « owner=self|companion round=N » capturé avant l'envoi (producteur). Sans suffixe (anciens journaux) : acteur du tour en cours.
    private static string Cast(string suffix = "") => "[combat] pid 7 : lance le sort 161 sur la cellule 120 inject=OK" + suffix;

    [Fact]
    public void Explicit_cast_owner_self_after_the_companion_header_is_booked_for_the_self_round()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat] début de tour : COMPAGNON -1"),
            (2, Cast(" owner=self round=1")),   // injectée par le perso, journalisée après le changement d'en-tête
            (3, Fin),
            (4, BilanOk))!;

        Assert.Equal(1, fight.Casts);
        Assert.Equal(0, fight.CompanionCasts);
        Assert.Equal(new[] { 1 }, fight.TurnStats.Select(t => t.Casts).ToArray());
    }

    [Fact]
    public void Explicit_cast_owner_companion_after_the_self_header_is_not_booked_for_the_self_round()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Fin),
            (2, "[combat] début de tour : COMPAGNON -1"),
            (3, Fin),
            (4, "[combat] début de tour : MOI (round 2)"),
            (5, Cast(" owner=companion round=1")),   // sort du compagnon journalisé après le début du tour du perso
            (6, Cast(" owner=self round=2")),
            (7, Fin),
            (8, BilanOk))!;

        Assert.Equal(1, fight.Casts);
        Assert.Equal(1, fight.CompanionCasts);
        Assert.Equal(new[] { 0, 1 }, fight.TurnStats.Select(t => t.Casts).ToArray());
    }

    [Fact]
    public void A_late_explicit_self_cast_is_booked_on_its_round_without_extending_a_closed_round()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (3, Fin),
            (4, Cast(" owner=self round=1")),   // même tour, après sa fin
            (5, "[combat] début de tour : COMPAGNON -1"),
            (6, Fin),
            (7, "[combat] début de tour : MOI (round 2)"),
            (8, Fin),
            (12, Cast(" owner=self round=1")),   // tour 1 et tour 2 déjà clos
            (13, BilanOk))!;

        Assert.Equal(2, fight.Casts);
        Assert.Equal(new[] { 2, 0 }, fight.TurnStats.Select(t => t.Casts).ToArray());
        Assert.Equal(new double[] { 3, 8 }, fight.TurnStats.Select(t => t.EndSec).ToArray());
    }

    [Fact]
    public void An_explicit_self_cast_in_the_live_self_turn_counts_once_and_extends_the_round_end()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (2, Cast(" owner=self round=1")),
            (3, Fin),
            (4, BilanOk))!;

        Assert.Equal(1, fight.Casts);
        Assert.Equal(0, fight.CompanionCasts);
        Assert.Equal(3, fight.TurnStats.Single().EndSec);
    }

    [Fact]
    public void A_cast_with_an_unknown_explicit_owner_is_not_counted_for_anybody()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Cast(" owner=enemy round=1")),
            (2, Fin),
            (3, BilanOk))!;

        Assert.Equal(0, fight.Casts);
        Assert.Equal(0, fight.CompanionCasts);
        Assert.Equal(0, fight.TurnStats.Single().Casts);
    }

    [Fact]
    public void Legacy_cast_lines_without_an_owner_suffix_follow_the_acting_turn()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, Cast()),
            (2, Fin),
            (3, "[combat] début de tour : COMPAGNON -1"),
            (4, Cast()),
            (5, Cast()),
            (6, Fin),
            (7, BilanOk))!;

        Assert.Equal(1, fight.Casts);
        Assert.Equal(2, fight.CompanionCasts);
        Assert.Equal(new[] { 1 }, fight.TurnStats.Select(t => t.Casts).ToArray());
    }

    // Échantillons réels : lignes copiées telles quelles des journaux 2026-11-04 (232959 défaite, 232702 victoire), horodatage = décalage en s.
    private static FightJournal PlaySample(uint pid, string sample)
    {
        var r = new CombatJournalRecorder(pid);
        FightJournal? last = null;
        foreach (var row in sample.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = row.IndexOf('\t');
            double sec = double.Parse(row[..tab], System.Globalization.CultureInfo.InvariantCulture);
            if (r.Feed(T0.AddSeconds(sec), row[(tab + 1)..].TrimEnd('\r')) is { } f) last = f;
        }
        return last!;
    }

    [Fact]
    public void Real_defeat_log_keeps_companion_casts_and_moves_out_of_the_main_character_rounds()
    {
        var fight = PlaySample(1073741827, CombatJournalLogSamples.Defeat);

        Assert.Equal("défaite", fight.Outcome);
        Assert.Equal(new[] { 3, 3, 0, 0 }, fight.TurnStats.Select(t => t.Casts).ToArray());   // rounds 3-4 : Bastionbot ne lance rien
        Assert.Equal(new[] { 1, 2, 1, 0 }, fight.TurnStats.Select(t => t.Moves).ToArray());
        Assert.Equal(6, fight.Casts);
        Assert.Equal(4, fight.Moves);
        Assert.Equal(7, fight.CompanionCasts);
        Assert.Equal(3, fight.CompanionMoves);
        Assert.Equal(2, fight.TurnsWithoutCast);
    }

    [Fact]
    public void Real_defeat_log_takes_self_hp_only_from_self_turns()
    {
        var fight = PlaySample(1073741827, CombatJournalLogSamples.Defeat);

        Assert.Equal(new[] { "2762/3031", "2762/3031", "2053/2945", "2053/2945" }, fight.TurnStats.Select(t => t.Hp).ToArray());
    }

    [Fact]
    public void Real_victory_log_counts_one_cast_per_cast_request_not_per_target_line()
    {
        var fight = PlaySample(1073741825, CombatJournalLogSamples.Victory);

        Assert.Equal("victoire", fight.Outcome);
        Assert.Equal(30, fight.Casts);                                                         // 30 request_tx kind=cast, pas 56
        Assert.Equal(new[] { 4, 3, 3, 3, 4, 3, 3, 2, 4, 1 }, fight.TurnStats.Select(t => t.Casts).ToArray());
        Assert.Equal(9, fight.Moves);
        Assert.Equal(0, fight.CompanionCasts);
        Assert.DoesNotContain("56 sort", CombatJournalFormat.ToText(fight));
    }

    [Fact]
    public void Real_victory_log_does_not_report_a_living_character_at_zero_hp()
    {
        var fight = PlaySample(1073741825, CombatJournalLogSamples.Victory);

        Assert.Equal("2304/2414", fight.TurnStats[0].Hp);
        Assert.Equal("776/2231", fight.TurnStats[2].Hp);
        Assert.DoesNotContain(fight.TurnStats, t => t.Hp.StartsWith("0/", StringComparison.Ordinal));
        Assert.DoesNotContain("tombés à 0 %", string.Join("\n", fight.Findings));
    }

    [Fact]
    public void Text_reports_the_companion_actions_separately_and_stays_readable()
    {
        string text = CombatJournalFormat.ToText(PlaySample(1073741827, CombatJournalLogSamples.Defeat));

        Assert.Contains("6 sort(s) · 4 déplacement(s)", text);
        Assert.Contains("Compagnon : 7 sort(s) · 3 déplacement(s)", text);
        Assert.Contains("tour  3 :", text);
        Assert.Contains("0 sort(s) · 1 dépl.", text);
    }

    [Fact]
    public void Legacy_fights_without_companion_lines_keep_the_same_totals()
    {
        var fight = Play(new CombatJournalRecorder(7),
            (0, "[combat] début de tour : MOI (round 1)"),
            (1, "[combat] pid 7 : lance le sort 161 sur la cellule 120 inject=OK"),
            (2, "[combat] pid 7 : fin du tour (FightTurnFinishRequest) inject=OK"),
            (3, "[combat] pid 7 : bilan -> issue=2 xp=0 butin=[]"))!;

        Assert.Equal(1, fight.Casts);
        Assert.Equal(0, fight.CompanionCasts);
        Assert.DoesNotContain("Compagnon :", CombatJournalFormat.ToText(fight));
    }
}

/// <summary>Lignes réelles des journaux de combat (« horodatage&lt;TAB&gt;ligne du transport »), sans donnée personnelle.</summary>
internal static class CombatJournalLogSamples
{
    public const string Defeat = """
1.690	[combat] pid 1073741827 : attaque du groupe Ino-Naru+Kurookin+Lichangoro (id -20002, AttackMonsterRequest) inject=OK
2.231	[combat] ennemi -2 déplacé -> case 400 (déplacement suivi)
4.962	[combat] début de tour : MOI (round 1)
4.963	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12762,14011,12743,12728,12757,12755,12727,12726,12745,12741,12734,12750,12733,12746,12738,12748,12732,12735,12739,12744,12759,22312] maCellule=284 (PA12 PM5 PO+3) [seq fin=28 début=0/0]
4.963	[combat] PV tour 1 : moi 2762/3031 | alliés [299:3920/3920] | ennemis [400:3100/3100, 360:2900/2900, 402:3100/3100, 417:3400/3400]
5.023	[turbo-metric] request_tx kind=move:284->313 t=487131197 ack_to_request_ms=2763
5.045	[turbo-metric] move_request_to_server_confirmed_ms=16 from=284 target=313 final=313
5.099	[turbo-metric] request_tx kind=cast:12755 t=487131273 ack_to_request_ms=54
5.099	[combat] pid 1073741827 : lance le sort 12755 sur la cellule 385 inject=OK
5.204	[turbo-metric] request_tx kind=cast:12755 t=487131378 ack_to_request_ms=59
5.204	[combat] pid 1073741827 : lance le sort 12755 sur la cellule 385 inject=OK
5.274	[turbo-metric] request_tx kind=cast:12727 t=487131448 ack_to_request_ms=46
5.274	[combat] pid 1073741827 : lance le sort 12727 sur la cellule 371 inject=OK
5.328	[combat] pid 1073741827 : fin du tour (FightTurnFinishRequest) inject=OK
5.429	[combat] ennemi -4 déplacé -> case 415 (déplacement suivi)
5.440	[combat] ennemi -4 déplacé -> case 431 (déplacement suivi)
5.503	[combat] début de tour : COMPAGNON -1
5.503	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[4245,4242,4244,4246] maCellule=299 (PA11 PM6 PO+0) [seq fin=64 début=1/0]
5.503	[combat] PV tour 1 : moi 3600/3889 | alliés [313:2762/3031] | ennemis [400:838/2875, 360:2900/2900, 431:3100/3100, 417:3400/3400]
5.519	[turbo-metric] request_tx kind=cast:4245 t=487131693 ack_to_request_ms=78
5.519	[combat] pid 1073741827 : lance le sort 4245 sur la cellule 400 inject=OK
5.559	[turbo-metric] request_tx kind=cast:4245 t=487131733 ack_to_request_ms=17
5.559	[combat] pid 1073741827 : lance le sort 4245 sur la cellule 400 inject=OK
5.597	[turbo-metric] request_tx kind=cast:4242 t=487131771 ack_to_request_ms=15
5.597	[combat] pid 1073741827 : lance le sort 4242 sur la cellule 285 inject=OK
5.625	[combat] pid 1073741827 : fin du tour (FightTurnFinishRequest) inject=OK
5.710	[combat] ennemi -5 déplacé -> case 315 (déplacement suivi)
5.816	[combat] ennemi -3 déplacé -> case 330 (déplacement suivi)
5.911	[combat] début de tour : MOI (round 2)
5.911	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12762,14011,12743,12728,12757,12755,12727,12726,12745,12741,12734,12750,12733,12746,12738,12748,12732,12735,12739,12744,12759,22312] maCellule=313 (PA12 PM7 PO+3) [seq fin=117 début=2/0]
5.911	[combat] PV tour 2 : moi 2762/3031 | alliés [299:3600/3889] | ennemis [330:2900/2900, 431:3100/3100, 315:3400/3400]
5.953	[turbo-metric] request_tx kind=cast:12727 t=487132127 ack_to_request_ms=42
5.953	[combat] pid 1073741827 : lance le sort 12727 sur la cellule 343 inject=OK
6.014	[turbo-metric] request_tx kind=cast:12727 t=487132188 ack_to_request_ms=37
6.014	[combat] pid 1073741827 : lance le sort 12727 sur la cellule 343 inject=OK
6.075	[turbo-metric] request_tx kind=cast:12757 t=487132249 ack_to_request_ms=38
6.075	[combat] pid 1073741827 : lance le sort 12757 sur la cellule 315 inject=OK
6.138	[turbo-metric] request_tx kind=move:313->314 t=487132312 ack_to_request_ms=41
6.181	[turbo-metric] move_request_to_server_confirmed_ms=47 from=313 target=314 final=327
6.225	[turbo-metric] request_tx kind=move:327->314 t=487132399 ack_to_request_ms=43
6.247	[turbo-metric] move_request_to_server_confirmed_ms=15 from=327 target=314 final=314
6.265	[combat] pid 1073741827 : fin du tour (FightTurnFinishRequest) inject=OK
6.317	[combat] ennemi -4 déplacé -> case 430 (déplacement suivi)
6.355	[combat] ennemi -4 déplacé -> case 415 (déplacement suivi)
6.356	[combat] ennemi -4 déplacé -> case 402 (déplacement suivi)
6.408	[combat] début de tour : COMPAGNON -1
6.408	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[4245,4242,4244,4246] maCellule=299 (PA11 PM7 PO+0) [seq fin=162 début=3/0]
6.408	[combat] PV tour 2 : moi 3347/3865 | alliés [314:2344/2981] | ennemis [330:2900/2900, 402:3100/3100, 315:1858/3249]
6.418	[turbo-metric] request_tx kind=move:299->326 t=487132592 ack_to_request_ms=1
6.439	[combat] ennemi -1 déplacé -> case 326 (déplacement suivi)
6.439	[turbo-metric] move_request_to_server_confirmed_ms=31 from=299 target=326 final=326
6.450	[turbo-metric] request_tx kind=cast:4245 t=487132624 ack_to_request_ms=10
6.450	[combat] pid 1073741827 : lance le sort 4245 sur la cellule 330 inject=OK
6.502	[turbo-metric] request_tx kind=move:326->327 t=487132676 ack_to_request_ms=30
6.526	[combat] ennemi -1 déplacé -> case 327 (déplacement suivi)
6.528	[turbo-metric] move_request_to_server_confirmed_ms=15 from=326 target=327 final=327
6.529	[turbo-metric] request_tx kind=cast:4245 t=487132703 ack_to_request_ms=2
6.529	[combat] pid 1073741827 : lance le sort 4245 sur la cellule 330 inject=OK
6.580	[turbo-metric] request_tx kind=move:327->328 t=487132754 ack_to_request_ms=29
6.601	[combat] ennemi -1 déplacé -> case 328 (déplacement suivi)
6.601	[turbo-metric] move_request_to_server_confirmed_ms=16 from=327 target=328 final=328
6.603	[combat] pid 1073741827 : fin du tour (FightTurnFinishRequest) inject=OK
6.849	[combat] ennemi -3 déplacé -> case 301 (déplacement suivi)
6.851	[combat] ennemi -3 déplacé -> case 328 (déplacement suivi)
6.915	[combat] début de tour : MOI (round 3)
6.915	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12762,14011,12743,12728,12757,12755,12727,12726,12745,12741,12734,12750,12733,12746,12738,12748,12732,12735,12739,12744,12759,22312] maCellule=314 (PA13 PM7 PO+3) [seq fin=232 début=4/0]
6.915	[combat] PV tour 3 : moi 2053/2945 | alliés [342:2659/3800] | ennemis [328:1884/2775, 402:3100/3100, 300:1858/3249]
6.952	[combat] défis : fin de tour -> 327 (1 PM) [case imposée]
6.952	[turbo-metric] request_tx kind=move:314->327 t=487133126 ack_to_request_ms=37
6.978	[turbo-metric] move_request_to_server_confirmed_ms=32 from=314 target=327 final=327
6.978	[combat] pid 1073741827 : fin du tour (FightTurnFinishRequest) inject=OK
7.042	[combat] ennemi -4 déplacé -> case 429 (déplacement suivi)
7.055	[combat] ennemi -4 déplacé -> case 402 (déplacement suivi)
7.076	[combat] début de tour : COMPAGNON -1
7.076	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[4245,4242,4244,4246] maCellule=342 (PA11 PM7 PO+0) [seq fin=263 début=5/0]
7.076	[combat] PV tour 3 : moi 2407/3776 | alliés [327:2053/2945] | ennemis [328:1884/2775, 402:3100/3100, 300:1858/3249]
7.077	[turbo-metric] request_tx kind=cast:4245 t=487133251 ack_to_request_ms=21
7.077	[combat] pid 1073741827 : lance le sort 4245 sur la cellule 328 inject=OK
7.106	[turbo-metric] request_tx kind=cast:4245 t=487133280 ack_to_request_ms=5
7.106	[combat] pid 1073741827 : lance le sort 4245 sur la cellule 328 inject=OK
7.129	[combat] pid 1073741827 : fin du tour (FightTurnFinishRequest) inject=OK
7.806	[combat] début de tour : MOI (round 4)
7.806	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12762,14011,12743,12728,12757,12755,12727,12726,12745,12741,12734,12750,12733,12746,12738,12748,12732,12735,12739,12744,12759,22312] maCellule=327 (PA13 PM5 PO+3) [seq fin=323 début=6/0]
7.806	[combat] PV tour 4 : moi 2053/2945 | alliés [] | ennemis [313:1174/2704, 402:3100/3100, 356:1225/3186]
7.840	[combat] défis : aucune case de fin de tour conforme atteignable avec la politique de déplacement (5 PM) -> je reste en 327
7.840	[combat] pid 1073741827 : fin du tour (FightTurnFinishRequest) inject=OK
7.862	[combat] pid 1073741827 : défi 37 échoué
8.308	[combat] ennemi -4 déplacé -> case 429 (déplacement suivi)
8.341	[combat] ennemi -4 déplacé -> case 400 (déplacement suivi)
8.409	[combat] ennemi -5 déplacé -> case 342 (déplacement suivi)
8.463	[combat] ennemi -5 déplacé -> case 300 (déplacement suivi)
8.507	[combat] pid 1073741827 : bilan -> issue=0 xp=0 butin=[]
""";

    public const string Victory = """
0.813	[combat] pid 1073741825 : attaque du groupe Betto+Sarkapwane (id -20002, AttackMonsterRequest) inject=OK
1.492	[combat] début de tour : MOI (round 1)
1.492	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=344 (PA11 PM5 PO+3) [seq fin=30 début=0/0]
1.492	[combat] PV tour 1 : moi 2304/2414 | alliés [] | ennemis [257:3600/3600, 230:3500/3500, 189:2300/2300, 300:2300/2300]
1.492	[combat] sort 12913 sur MOI (344) [buff]
1.492	[turbo-metric] request_tx kind=cast:12913 t=486950168 ack_to_request_ms=30
1.492	[combat] pid 1073741825 : lance le sort 12913 sur la cellule 344 inject=OK
1.521	[combat] sort 12938 -> cible 300 (dist 3)
1.521	[turbo-metric] request_tx kind=cast:12938 t=486950198 ack_to_request_ms=1
1.521	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 300 inject=OK
1.548	[combat] sort 12938 -> cible 300 (dist 3)
1.548	[turbo-metric] request_tx kind=cast:12938 t=486950225 ack_to_request_ms=28
1.548	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 300 inject=OK
1.594	[turbo-metric] request_tx kind=move:344->272 t=486950271 ack_to_request_ms=23
1.616	[turbo-metric] move_request_to_server_confirmed_ms=15 from=344 target=272 final=315
1.617	[combat] sort 12902 -> cible 300 (dist 1)
1.617	[turbo-metric] request_tx kind=cast:12902 t=486950294 ack_to_request_ms=1
1.617	[combat] pid 1073741825 : lance le sort 12902 sur la cellule 300 inject=OK
1.638	[combat] fin de tour : ennemi au contact (case 315) -> je reste au CAC (pas de déplacement taclé) [kite 0]
1.638	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
6.182	[combat] début de tour : MOI (round 2)
6.183	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=257 (PA11 PM7 PO+3) [seq fin=119 début=1/0]
6.183	[combat] PV tour 2 : moi 1346/2304 | alliés [] | ennemis [242:3600/3600, 258:3500/3500, 135:2300/2300, 261:1174/2189]
6.184	[combat] sort 12938 -> cible 242 (dist 1)
6.184	[turbo-metric] request_tx kind=cast:12938 t=486954860 ack_to_request_ms=12
6.184	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 242 inject=OK
6.207	[combat] sort 12938 -> cible 242 (dist 1)
6.207	[turbo-metric] request_tx kind=cast:12938 t=486954885 ack_to_request_ms=1
6.207	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 242 inject=OK
6.232	[combat] sort 12938 -> cible 258 (dist 2)
6.232	[turbo-metric] request_tx kind=cast:12938 t=486954909 ack_to_request_ms=1
6.232	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 258 inject=OK
6.257	[combat] fin de tour : ennemi au contact (case 257) -> je reste au CAC (pas de déplacement taclé) [kite 0]
6.257	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
7.299	[combat] début de tour : MOI (round 3)
7.299	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=229 (PA11 PM7 PO+3) [seq fin=209 début=2/0]
7.299	[combat] PV tour 3 : moi 776/2231 | alliés [] | ennemis [214:2688/3509, 230:3029/3453, 193:2300/2300, 259:1174/2189]
7.300	[combat] sort 12938 -> cible 214 (dist 1)
7.300	[turbo-metric] request_tx kind=cast:12938 t=486955976 ack_to_request_ms=298
7.300	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 214 inject=OK
7.370	[combat] sort 12938 -> cible 214 (dist 1)
7.370	[turbo-metric] request_tx kind=cast:12938 t=486956048 ack_to_request_ms=1
7.371	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 214 inject=OK
7.393	[combat] sort 12938 -> cible 230 (dist 2)
7.393	[turbo-metric] request_tx kind=cast:12938 t=486956071 ack_to_request_ms=1
7.393	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 230 inject=OK
7.416	[combat] fin de tour : ennemi au contact (case 229) -> je reste au CAC (pas de déplacement taclé) [kite 0]
7.416	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
8.264	[combat] début de tour : MOI (round 4)
8.264	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=287 (PA11 PM5 PO+3) [seq fin=329 début=3/0]
8.264	[combat] PV tour 4 : moi 0/2103 | alliés [] | ennemis [258:1623/3404, 272:2460/3397, 249:2300/2300, 213:1174/2189]
8.264	[combat] sort 12938 -> cible 272 (dist 1)
8.264	[turbo-metric] request_tx kind=cast:12938 t=486956941 ack_to_request_ms=33
8.264	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 272 inject=OK
8.288	[combat] sort 12938 -> cible 272 (dist 1)
8.288	[turbo-metric] request_tx kind=cast:12938 t=486956965 ack_to_request_ms=1
8.288	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 272 inject=OK
8.313	[combat] sort 12917 -> cible 272 (dist 1)
8.313	[turbo-metric] request_tx kind=cast:12917 t=486956990 ack_to_request_ms=1
8.313	[combat] pid 1073741825 : lance le sort 12917 sur la cellule 272 inject=OK
8.335	[combat] fin de tour : ennemi au contact (case 287) -> je reste au CAC (pas de déplacement taclé) [kite 0]
8.335	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
11.099	[combat] début de tour : MOI (round 5)
11.099	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=259 (PA11 PM5 PO+3) [seq fin=450 début=4/0]
11.099	[combat] PV tour 5 : moi 0/2001 | alliés [] | ennemis [244:1623/3404, 260:650/3217, 193:2300/2300, 158:1174/2189]
11.099	[combat] sort 12913 sur MOI (259) [buff]
11.099	[turbo-metric] request_tx kind=cast:12913 t=486959776 ack_to_request_ms=1
11.099	[combat] pid 1073741825 : lance le sort 12913 sur la cellule 259 inject=OK
11.124	[combat] sort 12938 -> cible 260 (dist 2)
11.124	[turbo-metric] request_tx kind=cast:12938 t=486959801 ack_to_request_ms=1
11.124	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 260 inject=OK
11.147	[combat] sort 12938 -> cible 260 (dist 2)
11.147	[turbo-metric] request_tx kind=cast:12938 t=486959824 ack_to_request_ms=1
11.147	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 260 inject=OK
11.181	[combat] sort 12938 -> cible 244 (dist 1)
11.181	[turbo-metric] request_tx kind=cast:12938 t=486959858 ack_to_request_ms=1
11.181	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 244 inject=OK
11.204	[combat] fin de tour : ennemi au contact (case 259) -> je reste au CAC (pas de déplacement taclé) [kite 0]
11.204	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
11.564	[combat] début de tour : MOI (round 6)
11.564	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=244 (PA11 PM7 PO+3) [seq fin=549 début=5/0]
11.564	[combat] PV tour 6 : moi 0/1938 | alliés [] | ennemis [230:1170/3359, 247:2300/2300, 100:1174/2189]
11.564	[combat] sort 12938 -> cible 230 (dist 1)
11.564	[turbo-metric] request_tx kind=cast:12938 t=486960241 ack_to_request_ms=79
11.564	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 230 inject=OK
11.588	[combat] sort 12938 -> cible 230 (dist 1)
11.588	[turbo-metric] request_tx kind=cast:12938 t=486960265 ack_to_request_ms=1
11.588	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 230 inject=OK
11.618	[combat] sort 12917 -> cible 230 (dist 1)
11.618	[turbo-metric] request_tx kind=cast:12917 t=486960295 ack_to_request_ms=6
11.618	[combat] pid 1073741825 : lance le sort 12917 sur la cellule 230 inject=OK
11.647	[combat] fin de tour : rapprochement -> 261 (5 PM, dist cible 1)
11.672	[turbo-metric] request_tx kind=move:244->261 t=486960348 ack_to_request_ms=25
11.700	[turbo-metric] move_request_to_server_confirmed_ms=15 from=244 target=261 final=273
11.705	[combat] fin de tour : rapprochement -> 288 (2 PM, dist cible 13) [essai 2]
11.735	[turbo-metric] request_tx kind=move:261->288 t=486960412 ack_to_request_ms=31
11.766	[turbo-metric] move_request_to_server_confirmed_ms=31 from=261 target=288 final=274
11.767	[combat] fin de tour : AUCUN rapprochement (cible + proche 260 dist 3, 7 PM, 0 cases atteignables, 0 bloquees, floor[moi]=0, 218 cases marchables sur la carte) -> je passe
11.767	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
12.636	[combat] début de tour : MOI (round 7)
12.636	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=220 (PA11 PM7 PO+3) [seq fin=630 début=6/0]
12.636	[combat] PV tour 7 : moi 0/1852 | alliés [] | ennemis [328:2300/2300, 130:1174/2189]
12.639	[turbo-metric] request_tx kind=move:220->288 t=486961315 ack_to_request_ms=1
12.664	[turbo-metric] move_request_to_server_confirmed_ms=31 from=220 target=288 final=274
12.667	[combat] sort 12902 -> cible 328 (dist 3)
12.668	[turbo-metric] request_tx kind=cast:12902 t=486961344 ack_to_request_ms=1
12.668	[combat] pid 1073741825 : lance le sort 12902 sur la cellule 328 inject=OK
12.691	[combat] sort 12902 -> cible 328 (dist 3)
12.691	[turbo-metric] request_tx kind=cast:12902 t=486961368 ack_to_request_ms=1
12.691	[combat] pid 1073741825 : lance le sort 12902 sur la cellule 328 inject=OK
12.713	[combat] FILET attaque obligatoire : sort 12938 -> cible 328 (dist 3, PA5) [castsThisTurn=2]
12.713	[turbo-metric] request_tx kind=cast:12938 t=486961391 ack_to_request_ms=1
12.713	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 328 inject=OK
12.737	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
13.125	[combat] début de tour : MOI (round 8)
13.125	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=355 (PA11 PM5 PO+3) [seq fin=703 début=7/0]
13.125	[combat] PV tour 8 : moi 0/1792 | alliés [] | ennemis [346:964/2168, 186:1174/2189]
13.129	[turbo-metric] request_tx kind=move:355->315 t=486961807 ack_to_request_ms=1
13.154	[turbo-metric] move_request_to_server_confirmed_ms=15 from=355 target=315 final=315
13.154	[combat] sort 12902 -> cible 346 (dist 6)
13.154	[turbo-metric] request_tx kind=cast:12902 t=486961832 ack_to_request_ms=1
13.155	[combat] pid 1073741825 : lance le sort 12902 sur la cellule 346 inject=OK
13.180	[combat] sort 12902 -> cible 346 (dist 6)
13.180	[turbo-metric] request_tx kind=cast:12902 t=486961858 ack_to_request_ms=1
13.180	[combat] pid 1073741825 : lance le sort 12902 sur la cellule 346 inject=OK
13.204	[combat] fin de tour : rapprochement -> 288 (2 PM, dist cible 4)
13.234	[turbo-metric] request_tx kind=move:315->288 t=486961911 ack_to_request_ms=31
13.256	[turbo-metric] move_request_to_server_confirmed_ms=16 from=315 target=288 final=288
13.257	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
13.895	[combat] début de tour : MOI (round 9)
13.895	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=346 (PA11 PM5 PO+3) [seq fin=774 début=8/0]
13.895	[combat] PV tour 9 : moi 0/1769 | alliés [] | ennemis [328:152/2088, 243:1174/2189]
13.895	[combat] sort 12913 sur MOI (346) [buff]
13.895	[turbo-metric] request_tx kind=cast:12913 t=486962572 ack_to_request_ms=1
13.895	[combat] pid 1073741825 : lance le sort 12913 sur la cellule 346 inject=OK
13.953	[turbo-metric] request_tx kind=move:346->288 t=486962629 ack_to_request_ms=26
13.976	[turbo-metric] move_request_to_server_confirmed_ms=16 from=346 target=288 final=288
13.977	[combat] sort 12938 -> cible 328 (dist 3)
13.977	[turbo-metric] request_tx kind=cast:12938 t=486962654 ack_to_request_ms=1
13.977	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 328 inject=OK
14.024	[combat] sort 12902 -> cible 243 (dist 5)
14.024	[turbo-metric] request_tx kind=cast:12902 t=486962701 ack_to_request_ms=2
14.025	[combat] pid 1073741825 : lance le sort 12902 sur la cellule 243 inject=OK
14.050	[combat] sort 12902 -> cible 243 (dist 5)
14.050	[turbo-metric] request_tx kind=cast:12902 t=486962728 ack_to_request_ms=1
14.050	[combat] pid 1073741825 : lance le sort 12902 sur la cellule 243 inject=OK
14.089	[combat] fin de tour : rapprochement -> 301 (1 PM, dist cible 4)
14.094	[turbo-metric] request_tx kind=move:288->301 t=486962771 ack_to_request_ms=20
14.117	[turbo-metric] move_request_to_server_confirmed_ms=15 from=288 target=301 final=301
14.117	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
14.461	[combat] début de tour : MOI (round 10)
14.461	[combat] TOUR style=Aggressive cible=Closest vitesse=Turbo sorts=[12913,12938,12917,12902] maCellule=229 (PA11 PM7 PO+3) [seq fin=853 début=9/0]
14.461	[combat] PV tour 10 : moi 0/1748 | alliés [] | ennemis [261:260/2098]
14.463	[turbo-metric] request_tx kind=move:229->245 t=486963139 ack_to_request_ms=1
14.485	[turbo-metric] move_request_to_server_confirmed_ms=31 from=229 target=245 final=245
14.486	[combat] sort 12938 -> cible 261 (dist 3)
14.486	[turbo-metric] request_tx kind=cast:12938 t=486963163 ack_to_request_ms=1
14.486	[combat] pid 1073741825 : lance le sort 12938 sur la cellule 261 inject=OK
14.522	[combat] pid 1073741825 : fin du tour (FightTurnFinishRequest) inject=OK
14.865	[combat] pid 1073741825 : défi 461 échoué
14.869	[combat] pid 1073741825 : bilan -> issue=2 xp=287694 butin=[13523x1, 7308x1, 2654x1]
""";
}
