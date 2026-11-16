using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Scheduler du planning (<see cref="PlanningEval.DecideRun"/>) : bascule de script entre plages CONTIGUËS (bug « le
/// script de la 1re plage tourne jusqu'à la fin de la dernière » : 06:30 Otomai, 09:00 Fishing, 11:00 Souvenir, 15:00 fin).
/// Rejeu minute par minute, même enchaînement que BotsViewModel.EvaluatePlanning (plage servie, script en cours).</summary>
public class PlanningSchedulerTests
{
    private const string Prefix = "-- corps ";

    // Planning du rapport utilisateur (Persoun, JitterMin 0, samedi) : trois plages dos à dos, chacune avec SON script.
    private static CharPlanning Persoun(int jitter = 0)
    {
        var cp = new CharPlanning { Enabled = true, AutoConnect = true, JitterMin = jitter, Script = "-- repli figé" };
        foreach (var n in new[] { "Potion-Souvenir", "Otomai", "100% PDM 100 GIVRE" })
            cp.Scripts.Add(new PlanningScript { Name = n, Body = Prefix + n });
        cp.Slots.Add(new PlanningSlot { Name = "Otomai", Script = "Otomai", Days = 32, StartMin = 390, EndMin = 540 });
        cp.Slots.Add(new PlanningSlot { Name = "Fishing", Script = "100% PDM 100 GIVRE", Days = 32, StartMin = 540, EndMin = 660 });
        cp.Slots.Add(new PlanningSlot { Name = "Souvenir", Script = "Potion-Souvenir", Days = 32, StartMin = 660, EndMin = 900 });
        return cp;
    }

    private static readonly DateTime Saturday = new(2026, 9, 26);

    /// <summary>État d'un bot vu par le scheduler + journal des actions (« HH:mm start X » / « HH:mm stop »).</summary>
    private sealed class Sim
    {
        public string? Served;        // occurrence de plage servie (_planRun.Occ)
        public string? Running;       // corps du script en cours (null = arrêté)
        public bool Paused;           // arrêt manuel pendant la plage servie
        public bool Resumed;          // script repris après une reconnexion
        public bool Started;          // un script déjà démarré pour la plage servie
        public readonly List<string> Events = new();

        public PlanRunAction Step(CharPlanning cp, DateTime at, string who = "Persoun")
        {
            string? occ = PlanningEval.ActiveOccurrence(cp, at, who);
            int slot = PlanningEval.ActiveSlotIndex(cp, at, who);
            if (PlanningEval.IsNewOccurrence(Served, occ)) Paused = false;   // la pause ne vaut que pour la plage servie
            bool resumed = Resumed; Resumed = false;
            string? body = null;
            var act = PlanningEval.DecideRun(Served, occ, Running is not null, Running, Paused, resumed,
                () => body = PlanningEval.ResolveSlotScript(cp, slot, null).Body, started: Started);
            if (Served != occ) Started = false;
            string hm = at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            switch (act)
            {
                case PlanRunAction.Start:
                case PlanRunAction.Switch:
                    Started = true; Running = body; Events.Add($"{hm} {(act == PlanRunAction.Switch ? "switch" : "start")} {Name(body)}"); break;
                case PlanRunAction.Keep: Started = true; Events.Add($"{hm} keep {Name(Running)}"); break;
                case PlanRunAction.StopSlotEnd:
                case PlanRunAction.StopNoScript:
                    Started = false; Running = null; Events.Add($"{hm} stop"); break;
                case PlanRunAction.NoScript: Events.Add($"{hm} noscript"); break;
            }
            Served = occ;
            return act;
        }

        public void Replay(CharPlanning cp, int fromMin, int toMin)
        {
            for (int m = fromMin; m <= toMin; m++) Step(cp, Saturday.AddMinutes(m));
        }

        private static string Name(string? body) => body is null ? "?" : body.StartsWith(Prefix) ? body[Prefix.Length..] : body;
    }

    [Fact]
    public void Back_to_back_slots_switch_scripts_minute_by_minute()
    {
        // 06:00 -> 16:00 : Otomai à 06:30, bascule sur le script de pêche à 09:00, sur Souvenir à 11:00, arrêt à 15:00.
        var cp = Persoun();
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 16 * 60);
        Assert.Equal(new[]
        {
            "06:30 start Otomai",
            "09:00 switch 100% PDM 100 GIVRE",
            "11:00 switch Potion-Souvenir",
            "15:00 stop",
        }, sim.Events);
    }

    [Fact]
    public void Completed_script_is_not_restarted_at_every_tick_of_the_slot()
    {
        var cp = Persoun();
        var sim = new Sim();
        Assert.Equal(PlanRunAction.Start, sim.Step(cp, Saturday.AddMinutes(400)));
        sim.Running = null; // le trajet se termine normalement
        sim.Replay(cp, 401, 539);
        Assert.Single(sim.Events);
        Assert.Equal(PlanRunAction.Start, sim.Step(cp, Saturday.AddMinutes(540)));
        Assert.Equal(2, sim.Events.Count);
    }

    [Fact]
    public void Occurrence_changes_exactly_at_each_contiguous_boundary()
    {
        var cp = Persoun();
        string? a = PlanningEval.ActiveOccurrence(cp, Saturday.AddMinutes(539), "Persoun");
        string? b = PlanningEval.ActiveOccurrence(cp, Saturday.AddMinutes(540), "Persoun");
        string? c = PlanningEval.ActiveOccurrence(cp, Saturday.AddMinutes(659), "Persoun");
        string? d = PlanningEval.ActiveOccurrence(cp, Saturday.AddMinutes(660), "Persoun");
        Assert.NotNull(a); Assert.NotNull(b); Assert.NotNull(d);
        Assert.NotEqual(a, b);
        Assert.Equal(b, c);
        Assert.NotEqual(c, d);
        Assert.True(PlanningEval.IsNewOccurrence(a, b));
        Assert.False(PlanningEval.IsNewOccurrence(b, c));
        Assert.False(PlanningEval.IsNewOccurrence(null, b));   // 1re vue : pas une bascule
    }

    [Fact]
    public void Same_script_in_consecutive_slots_keeps_running()
    {
        var cp = Persoun();
        cp.Slots[1].Script = "Otomai";   // Fishing lance aussi Otomai
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 16 * 60);
        Assert.Equal(new[] { "06:30 start Otomai", "09:00 keep Otomai", "11:00 switch Potion-Souvenir", "15:00 stop" }, sim.Events);
    }

    [Fact]
    public void Manual_script_is_left_alone_until_the_next_slot_then_the_planning_takes_over()
    {
        var cp = Persoun();
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 7 * 60);                      // Otomai lancé à 06:30
        sim.Running = "-- script manuel";                     // 07:01 : l'utilisateur lance un autre script à la main
        sim.Replay(cp, 7 * 60 + 1, 9 * 60);
        Assert.Equal(new[] { "06:30 start Otomai", "09:00 switch 100% PDM 100 GIVRE" }, sim.Events);
    }

    [Fact]
    public void Manual_script_seen_first_during_a_slot_is_not_replaced_before_the_next_slot()
    {
        // App démarrée / planning activé à 10:00 pendant « Fishing » alors qu'un script lancé à la main tourne.
        var cp = Persoun();
        var sim = new Sim { Running = "-- script manuel" };
        Assert.Equal(PlanRunAction.None, sim.Step(cp, Saturday.AddMinutes(600)));
        sim.Replay(cp, 601, 11 * 60);
        Assert.Equal(new[] { "11:00 switch Potion-Souvenir" }, sim.Events);
    }

    [Fact]
    public void Manual_stop_holds_until_the_end_of_the_slot_only()
    {
        var cp = Persoun();
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 7 * 60);
        sim.Running = null; sim.Paused = true;                // 07:01 « Arrêter » pendant Otomai
        sim.Replay(cp, 7 * 60 + 1, 9 * 60 + 5);
        Assert.Equal(new[] { "06:30 start Otomai", "09:00 start 100% PDM 100 GIVRE" }, sim.Events);
    }

    [Fact]
    public void Script_resumed_after_a_reconnection_across_a_boundary_is_switched()
    {
        // Coupure à 08:58 (Otomai), retour à 09:04 : le script REPRIS est celui d'Otomai -> bascule sur la plage en cours.
        var cp = Persoun();
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 8 * 60 + 58);
        // 08:59 -> 09:03 : bot bloqué (reconnexion) -> le scheduler ne sert pas la plage (Served reste celle d'Otomai).
        sim.Resumed = true;                                   // 09:04 : reprise du script relevé avant la coupure
        Assert.Equal(PlanRunAction.Switch, sim.Step(cp, Saturday.AddMinutes(9 * 60 + 4)));
        Assert.Equal(new[] { "06:30 start Otomai", "09:04 switch 100% PDM 100 GIVRE" }, sim.Events);
    }

    [Fact]
    public void Script_resumed_within_the_same_slot_is_kept()
    {
        var cp = Persoun();
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 7 * 60);
        sim.Resumed = true;
        Assert.Equal(PlanRunAction.None, sim.Step(cp, Saturday.AddMinutes(7 * 60 + 5)));
        Assert.Equal("-- corps Otomai", sim.Running);
    }

    [Fact]
    public void Resumed_script_without_known_slot_is_checked_against_the_active_slot()
    {
        // Bot relancé hors ligne (plage servie inconnue) : le script repris est confronté à la plage active.
        var cp = Persoun();
        var stale = new Sim { Running = "-- corps Otomai", Resumed = true };
        Assert.Equal(PlanRunAction.Switch, stale.Step(cp, Saturday.AddMinutes(10 * 60)));
        Assert.Equal("-- corps 100% PDM 100 GIVRE", stale.Running);
        var same = new Sim { Running = "-- corps 100% PDM 100 GIVRE", Resumed = true };
        Assert.Equal(PlanRunAction.Keep, same.Step(cp, Saturday.AddMinutes(10 * 60)));
    }

    [Fact]
    public void Missing_script_in_the_next_slot_stops_the_previous_one()
    {
        var cp = Persoun();
        cp.Scripts.RemoveAll(s => s.Name == "100% PDM 100 GIVRE");   // script de pêche supprimé : plage « introuvable »
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 12 * 60);
        // 09:00 : le script d'Otomai est arrêté (rien à lancer pour Fishing, jamais un autre trajet à la place), puis « rien à
        // lancer » à chaque passage (journalisé UNE fois par l'appelant) jusqu'à Souvenir.
        Assert.Equal(new[] { "06:30 start Otomai", "09:00 stop", "11:00 start Potion-Souvenir" },
                     sim.Events.Where(e => !e.EndsWith("noscript")).ToArray());
        Assert.Equal("09:01 noscript", sim.Events.First(e => e.EndsWith("noscript")));
        Assert.Equal("10:59 noscript", sim.Events.Last(e => e.EndsWith("noscript")));
    }

    [Fact]
    public void Nothing_to_run_is_reported_each_pass_until_a_script_is_assigned()
    {
        var cp = Persoun();
        cp.Slots[0].Script = "Supprimé";
        var sim = new Sim();
        Assert.Equal(PlanRunAction.NoScript, sim.Step(cp, Saturday.AddMinutes(400)));
        Assert.Equal(PlanRunAction.NoScript, sim.Step(cp, Saturday.AddMinutes(401)));   // l'appelant ne journalise qu'une fois
        cp.Slots[0].Script = "Otomai";                                                  // l'utilisateur choisit un script
        Assert.Equal(PlanRunAction.Start, sim.Step(cp, Saturday.AddMinutes(402)));
    }

    [Fact]
    public void With_jitter_the_three_scripts_still_run_in_order()
    {
        var cp = Persoun(jitter: 5);
        var sim = new Sim();
        sim.Replay(cp, 6 * 60, 16 * 60);
        var started = sim.Events.Where(e => e.Contains(" start ") || e.Contains(" switch ")).Select(e => e[(e.IndexOf(' ', 6) + 1)..]).ToArray();
        Assert.Equal(new[] { "Otomai", "100% PDM 100 GIVRE", "Potion-Souvenir" }, started);
        Assert.EndsWith("stop", sim.Events[^1]);
        Assert.Null(sim.Running);
    }

    [Fact]
    public void Out_of_slot_stops_whatever_runs()
    {
        Assert.Equal(PlanRunAction.StopSlotEnd, PlanningEval.DecideRun("x@1", null, true, "-- a", false, false, () => throw new InvalidOperationException()));
        Assert.Equal(PlanRunAction.None, PlanningEval.DecideRun(null, null, false, null, false, false, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void Slot_body_is_resolved_only_when_needed()
    {
        int calls = 0;
        string? Body() { calls++; return "-- b"; }
        // Même plage, script en cours : aucune résolution (pas de rechargement disque à chaque passage).
        Assert.Equal(PlanRunAction.None, PlanningEval.DecideRun("o@1", "o@1", true, "-- a", false, false, Body));
        // Pause manuelle pendant la plage servie : idem.
        Assert.Equal(PlanRunAction.None, PlanningEval.DecideRun("o@1", "o@1", false, null, true, false, Body));
        Assert.Equal(0, calls);
    }
}
