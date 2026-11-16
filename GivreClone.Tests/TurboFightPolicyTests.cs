using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class TurboFightPolicyTests
{
    [Fact]
    public void Foreign_sequences_are_acked_only_when_ack_all_is_on()
    {
        Assert.True(TurboFightPolicy.ShouldAckForeignSequence(true, author: 7, selfId: 1, actionId: 42));
        Assert.True(TurboFightPolicy.ShouldAckForeignSequence(true, author: -1, selfId: 1, actionId: 42)); // monstre/invocation
        Assert.False(TurboFightPolicy.ShouldAckForeignSequence(false, 7, 1, 42));   // case décochée
        Assert.False(TurboFightPolicy.ShouldAckForeignSequence(false, -1, 1, 42));  // monstre sans case cochée
        Assert.False(TurboFightPolicy.ShouldAckForeignSequence(true, 1, 1, 42));    // la nôtre : ScheduleOwnAck s'en charge
        Assert.False(TurboFightPolicy.ShouldAckForeignSequence(true, 0, 1, 42));    // auteur inconnu
        Assert.False(TurboFightPolicy.ShouldAckForeignSequence(true, 7, 1, 0));     // pas d'action_id
    }

    [Fact]
    public void Early_ready_fires_once_after_our_fight_start_sequence_before_first_turn()
    {
        Assert.True(TurboFightPolicy.ShouldSendEarlyReady(true, 1, 1, 8, noTurnYet: true, alreadySent: false));
        Assert.False(TurboFightPolicy.ShouldSendEarlyReady(false, 1, 1, 8, true, false));   // case décochée
        Assert.False(TurboFightPolicy.ShouldSendEarlyReady(true, 1, 1, 8, true, true));     // déjà envoyé
        Assert.False(TurboFightPolicy.ShouldSendEarlyReady(true, 1, 1, 8, false, false));   // un tour a déjà démarré
        Assert.False(TurboFightPolicy.ShouldSendEarlyReady(true, 1, 1, 0, true, false));    // pas une séquence de début de combat
        Assert.False(TurboFightPolicy.ShouldSendEarlyReady(true, 7, 1, 8, true, false));    // séquence d'un autre
        Assert.False(TurboFightPolicy.ShouldSendEarlyReady(true, 0, 0, 8, true, false));    // perso inconnu
    }

    [Fact]
    public void Eager_ready_delay_is_near_zero()
    {
        for (int i = 0; i < 50; i++) Assert.InRange(TurboFightPolicy.EagerReadyDelayMs(), 25, 55);
    }

    [Fact]
    public void Barrier_closes_on_our_own_acked_sequence_whatever_the_ack_order()
    {
        var seq = (ActionId: 42L, At: 200L, StartAt: 120L);
        // Socket : la fin de NOTRE séquence suffit (le bot est le client et accuse aussitôt)
        Assert.True(TurboFightPolicy.BarrierClosed(100, seq, 0, 'c', socket: true, botAcks: false, null, null));
        // MITM, accusés du bot : accusée (bot ou client, même AVANT la fin de séquence décodée) -> fermée
        Assert.True(TurboFightPolicy.BarrierClosed(100, seq, 0, 'c', false, botAcks: true, ackedAt: 150, null));
        Assert.True(TurboFightPolicy.BarrierClosed(100, seq, 0, 'c', false, true, ackedAt: 201, null));
        Assert.False(TurboFightPolicy.BarrierClosed(100, seq, 0, 'c', false, true, ackedAt: null, null));
        // MITM, accusés du client visible seulement : il doit suivre la fin de séquence, pour la même action
        Assert.True(TurboFightPolicy.BarrierClosed(100, seq, 0, 'c', false, false, null, (42, 230)));
        Assert.False(TurboFightPolicy.BarrierClosed(100, seq, 0, 'c', false, false, null, (42, 150)));
        Assert.False(TurboFightPolicy.BarrierClosed(100, seq, 0, 'c', false, false, null, (41, 230)));
    }

    [Fact]
    public void Barrier_ignores_older_or_unrelated_sequences()
    {
        var seq = (ActionId: 42L, At: 200L, StartAt: 120L);
        Assert.False(TurboFightPolicy.BarrierClosed(150, seq, 0, 'c', true, false, null, null));      // séquence ouverte AVANT la requête
        Assert.False(TurboFightPolicy.BarrierClosed(100, seq, 3, 'c', true, false, null, null));      // séquence déclenchée, pas notre sort
        Assert.False(TurboFightPolicy.BarrierClosed(100, seq, 0, 'm', true, false, null, null));      // déplacement attendu, sort fermé
        Assert.True(TurboFightPolicy.BarrierClosed(100, seq, 4, 'm', true, false, null, null));
        Assert.True(TurboFightPolicy.BarrierClosed(100, seq, -1, 'c', true, false, null, null));      // type inconnu : pas de filtre
        Assert.False(TurboFightPolicy.BarrierClosed(0, seq, 0, 'c', true, false, null, null));        // aucune requête suivie
        Assert.False(TurboFightPolicy.BarrierClosed(100, null, 0, 'c', true, false, null, null));
    }
    [Theory]
    [InlineData(99, false)]
    [InlineData(119, false)]
    [InlineData(120, true)]
    [InlineData(201, true)]
    public void Reused_action_id_requires_ack_from_current_request_and_sequence(long ack, bool closed)
        => Assert.Equal(closed, TurboFightPolicy.BarrierClosed(100,
            (31L, 200L, 120L), 0, 'c', false, true, ack, null));

    // Socket 23/10 23:32:48.829 : fin + accusé de l'action 9 et requête du sort 24015 dans la même milliseconde (t=6806099).
    [Fact]
    public void Request_in_the_same_millisecond_as_the_previous_sequence_is_not_closed_by_it()
    {
        long request = TurboFightPolicy.RequestStamp(6806099, 6806099);
        Assert.Equal(6806100, request);
        Assert.False(TurboFightPolicy.BarrierClosed(request, (9, 6806099, 6806099), 0, 'c', true, false, null, null));
        Assert.True(TurboFightPolicy.BarrierClosed(request, (8, 6806133, 6806132), 0, 'c', true, false, null, null));
        Assert.Equal(500, TurboFightPolicy.RequestStamp(500, 499));
    }

    [Theory]
    [InlineData(170, true)]
    [InlineData(171, true)]
    [InlineData(172, true)]
    [InlineData(173, true)]
    [InlineData(174, true)]
    [InlineData(175, true)]
    [InlineData(176, true)]
    [InlineData(177, true)]
    [InlineData(65, false)]
    [InlineData(188, false)]
    [InlineData(229, false)]
    [InlineData(0, false)]
    public void Rejection_codes_are_correctly_classified(int messageId, bool isRejection)
    {
        Assert.Equal(isRejection, TurboFightPolicy.IsRejectionCode(messageId));
    }

    [Fact]
    public void Adaptive_policy_starts_aggressive_when_healthy()
    {
        var policy = new AdaptiveReadyPolicy();
        Assert.Equal(0, policy.BackoffLevel);
        Assert.Equal(5, policy.GetBurstRetries());
        for (int i = 0; i < 50; i++)
        {
            Assert.InRange(policy.GetInitialDelayMs(), 25, 55);
            Assert.InRange(policy.GetOwnAckDelayMs(), 15, 36);
            Assert.InRange(policy.GetForeignAckDelayMs(), 10, 21);
            Assert.InRange(policy.GetBurstSpacingMs(1), 85, 126);
            Assert.InRange(policy.GetBurstSpacingMs(3), 125, 176);
            Assert.InRange(policy.GetBurstSpacingMs(5), 175, 236);
            Assert.InRange(policy.GetBurstSpacingMs(8), 235, 321);
        }
    }

    [Theory]
    [InlineData(0, 18)]
    [InlineData(1, 35)]
    [InlineData(2, 60)]
    public void Foreign_ack_burst_is_capped_to_the_sustained_rate(int backoffLevel, int spacingMs)
    {
        // 40 sequences etrangeres arrivent dans la MEME milliseconde (combat a plusieurs monstres). La gigue seule
        // laissait partir 85-87 accuses/s avant la fermeture serveur et le ban temporaire de Bastionbot (09:46:43).
        var policy = new AdaptiveReadyPolicy();
        if (backoffLevel > 0)
        {
            policy.NoteRejection(175, 0);
            for (int level = AdaptiveReadyPolicy.MaxBackoffLevel; level > backoffLevel; level--)
                for (int clean = 0; clean < AdaptiveReadyPolicy.CleanTurnsToRecover; clean++) policy.NoteTurnStart(0);
        }
        Assert.Equal(backoffLevel, policy.BackoffLevel);

        const long now = 10_000;
        // Rafale assez longue pour saturer la fenetre utile a toutes les cadences : nos propres accuses consomment
        // aussi un creneau mais ne sont jamais differes au-dela de MaxOwnAckDeferralMs, donc la fenetre de 900 ms met
        // plus d'accuses a se remplir qu'avec la seule cadence (900 / 18 = 50 creneaux au minimum).
        const int burst = 400;
        var delays = new List<int>();   // accuses ETRANGERS retenus, dans l'ordre de leur creneau
        int dropped = 0, ownSent = 0;
        for (int i = 0; i < burst; i++)
        {
            // Un tour reel accuse au plus une poignee de NOS sequences (4 sorts lances), contre une sequence par
            // action de chaque monstre : la rafale est donc massivement etrangere.
            if (i < 4) { policy.NextOwnAckDelayMs(now); ownSent++; continue; }
            int delay = policy.NextForeignAckDelayMs(now);
            if (delay < 0) dropped++; else delays.Add(delay);
        }
        Assert.Equal(burst, delays.Count + dropped + ownSent);
        Assert.Equal(4, ownSent);

        // Aucun accuse ne part au-dela de la fenetre utile, et le reste de la rafale est abandonne.
        Assert.All(delays, delay => Assert.InRange(delay, 0, TurboFightPolicy.MaxAckDeferralMs));
        Assert.True(dropped > 0, "aucun accuse abandonne : la rafale depasse pourtant la fenetre utile");

        // Les creneaux etrangers sont strictement croissants et espaces d'au moins la cadence configuree : c'est
        // l'invariant que la gigue seule ne donnait pas (elle laissait partir 85-87 accuses/s).
        for (int i = 1; i < delays.Count; i++)
            Assert.True(delays[i] - delays[i - 1] >= spacingMs,
                $"accuse etranger {i} envoye {delays[i] - delays[i - 1]} ms apres le precedent "
                + $"(cadence minimale {spacingMs} ms)");

        // DEBIT TOTAL sur la pire seconde, nos accuses compris : le critere exact du ban. Nos accuses propres gardent
        // leur gigue courte (la barriere Turbo les attend) et sont bornes a un par cycle de 4.
        int worstSecond = delays.Count == 0 ? 0
            : delays.Max(start => delays.Count(d => d >= start && d < start + 1000)) + ownSent;
        Assert.True(worstSecond < 85,
            $"{worstSecond} accuses dans la pire seconde : le serveur a banni a 85-87/s (cadence {spacingMs} ms)");
    }

    [Fact]
    public void Adaptive_policy_backs_off_on_rejection_codes()
    {
        var policy = new AdaptiveReadyPolicy();
        policy.NoteRejection(175, 1000);

        Assert.Equal(AdaptiveReadyPolicy.MaxBackoffLevel, policy.BackoffLevel);
        Assert.Equal(175, policy.LastRejectionCode);
        Assert.Equal(1000, policy.LastRejectionAt);
        Assert.Equal(2, policy.GetBurstRetries());

        for (int i = 0; i < 50; i++)
        {
            Assert.InRange(policy.GetInitialDelayMs(), 75, 126);
            Assert.InRange(policy.GetOwnAckDelayMs(), 40, 71);
            Assert.InRange(policy.GetForeignAckDelayMs(), 25, 46);
            Assert.InRange(policy.GetBurstSpacingMs(1), 250, 381);
            Assert.InRange(policy.GetBurstSpacingMs(2), 250, 381);
        }
    }

    [Fact]
    public void Adaptive_policy_recovers_stepwise_after_clean_turns()
    {
        var policy = new AdaptiveReadyPolicy();
        policy.NoteRejection(171, 1000);
        Assert.Equal(2, policy.BackoffLevel);

        // First clean turn
        policy.NoteReadySent(1050);
        policy.NoteTurnStart(1100);
        Assert.Equal(2, policy.BackoffLevel);
        Assert.Equal(1, policy.CleanTurns);

        // Second clean turn -> recovers to level 1
        policy.NoteReadySent(1200);
        policy.NoteTurnStart(1260);
        Assert.Equal(1, policy.BackoffLevel);
        Assert.Equal(3, policy.GetBurstRetries());
        for (int i = 0; i < 20; i++)
        {
            Assert.InRange(policy.GetInitialDelayMs(), 45, 81);
            Assert.InRange(policy.GetOwnAckDelayMs(), 25, 51);
            Assert.InRange(policy.GetBurstSpacingMs(1), 160, 241);
        }

        // Third clean turn
        policy.NoteReadySent(1400);
        policy.NoteTurnStart(1450);
        Assert.Equal(1, policy.BackoffLevel);
        Assert.Equal(1, policy.CleanTurns);

        // Fourth clean turn -> fully recovers to level 0
        policy.NoteReadySent(1500);
        policy.NoteTurnStart(1540);
        Assert.Equal(0, policy.BackoffLevel);
        Assert.Equal(5, policy.GetBurstRetries());
    }

    [Fact]
    public void Adaptive_policy_cautions_when_server_response_is_abnormally_slow()
    {
        var policy = new AdaptiveReadyPolicy();
        Assert.Equal(0, policy.BackoffLevel);

        // Normal fast response (100ms)
        policy.NoteReadySent(1000);
        policy.NoteTurnStart(1100);
        Assert.Equal(0, policy.BackoffLevel);

        // Very slow response (>1500ms) without explicit rejection
        policy.NoteReadySent(2000);
        policy.NoteTurnStart(3600);
        Assert.Equal(1, policy.BackoffLevel);
        Assert.Equal(3, policy.GetBurstRetries());
    }

    [Fact]
    public void Adaptive_policy_reset_restores_clean_state()
    {
        var policy = new AdaptiveReadyPolicy();
        policy.NoteRejection(174, 500);
        Assert.Equal(2, policy.BackoffLevel);

        policy.Reset();
        Assert.Equal(0, policy.BackoffLevel);
        Assert.Equal(0, policy.CleanTurns);
        Assert.Equal(0, policy.LastRejectionAt);
        Assert.Equal(0, policy.LastRejectionCode);
        Assert.Equal(5, policy.GetBurstRetries());
    }
}
