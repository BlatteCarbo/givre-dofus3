using System;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

/// <summary>« Vrai planning » (connexion/déconnexion auto) : aides pures de PlanningEval utilisées par le scheduler de
/// connexion — rester connecté si une plage reprend bientôt (StartsWithin) et prochain début affiché (NextStart).</summary>
public class PlanningEvalTests
{
    private static readonly DateTime Monday = new(2026, 9, 21);   // lundi

    private static CharPlanning Plan(int days, int startMin, int endMin) => new()
    {
        Enabled = true,
        Slots = { new PlanningSlot { Days = days, StartMin = startMin, EndMin = endMin } },
    };

    [Fact]
    public void Reference_date_is_a_monday() => Assert.Equal(DayOfWeek.Monday, Monday.DayOfWeek);

    [Fact]
    public void StartsWithin_only_inside_the_window()
    {
        var cp = Plan(1, 10 * 60, 12 * 60);   // lundi 10:00-12:00
        Assert.True(PlanningEval.StartsWithin(cp, Monday.AddHours(9).AddMinutes(55), "Bob", 10));
        Assert.False(PlanningEval.StartsWithin(cp, Monday.AddHours(9).AddMinutes(40), "Bob", 10));
        Assert.False(PlanningEval.StartsWithin(cp, Monday.AddHours(12).AddMinutes(5), "Bob", 10));   // plage finie, pas de reprise
    }

    [Fact]
    public void NextStart_skips_the_running_slot()
    {
        var cp = Plan(0b111_1111, 10 * 60, 12 * 60);   // tous les jours 10:00-12:00
        Assert.Equal(Monday.AddDays(1).AddHours(10), PlanningEval.NextStart(cp, Monday.AddHours(11), "Bob"));
        Assert.Equal(Monday.AddHours(10), PlanningEval.NextStart(cp, Monday.AddHours(8).AddMinutes(30), "Bob"));
    }

    [Fact]
    public void NextStart_handles_overnight_and_week_wrap()
    {
        var night = Plan(1, 22 * 60, 6 * 60);   // lundi 22:00 -> mardi 06:00
        Assert.Equal(Monday.AddHours(22), PlanningEval.NextStart(night, Monday.AddHours(21).AddMinutes(59), "Bob"));
        var sunday = Plan(64, 9 * 60, 10 * 60);   // dimanche 09:00-10:00, vu depuis le lundi
        Assert.Equal(Monday.AddDays(6).AddHours(9), PlanningEval.NextStart(sunday, Monday.AddHours(12), "Bob"));
    }

    [Fact]
    public void Jitter_near_midnight_never_moves_the_slot_to_another_day()
    {
        // Lundi 23:55 -> mardi 02:00, jitter 10 : jamais actif lundi matin (ancien bug du modulo 1440), actif mardi 01:00.
        var cp = Plan(1, 23 * 60 + 55, 2 * 60);
        cp.JitterMin = 10;
        foreach (var who in new[] { "Bob", "Zed", "Alice" })
        {
            for (int m = 0; m < 120; m++)
                Assert.False(PlanningEval.IsActiveNow(cp, Monday.AddMinutes(m), who));
            Assert.True(PlanningEval.IsActiveNow(cp, Monday.AddDays(1).AddHours(1), who));
            Assert.False(PlanningEval.IsActiveNow(cp, Monday.AddDays(1).AddHours(2), who));
        }
    }

    [Fact]
    public void Without_jitter_windows_are_exact()
    {
        var day = Plan(1, 10 * 60, 12 * 60);
        Assert.False(PlanningEval.IsActiveNow(day, Monday.AddHours(9).AddMinutes(59), "Bob"));
        Assert.True(PlanningEval.IsActiveNow(day, Monday.AddHours(10), "Bob"));
        Assert.False(PlanningEval.IsActiveNow(day, Monday.AddHours(12), "Bob"));
        var night = Plan(1, 22 * 60, 6 * 60);   // lundi 22:00 -> mardi 06:00
        Assert.True(PlanningEval.IsActiveNow(night, Monday.AddHours(23), "Bob"));
        Assert.True(PlanningEval.IsActiveNow(night, Monday.AddDays(1).AddHours(5).AddMinutes(59), "Bob"));
        Assert.False(PlanningEval.IsActiveNow(night, Monday.AddHours(5), "Bob"));   // lundi matin : la veille (dimanche) n'est pas cochée
    }

    [Fact]
    public void Occurrence_changes_per_slot_and_per_day()
    {
        var always = Plan(0b111_1111, 0, 0);   // 24 h / 24, 7 j / 7
        string? a = PlanningEval.ActiveOccurrence(always, Monday.AddHours(23).AddMinutes(59), "Bob");
        string? b = PlanningEval.ActiveOccurrence(always, Monday.AddDays(1).AddMinutes(1), "Bob");
        Assert.NotNull(a); Assert.NotNull(b); Assert.NotEqual(a, b);

        var twoSlots = Plan(1, 8 * 60, 12 * 60);
        twoSlots.Slots.Add(new PlanningSlot { Days = 1, StartMin = 12 * 60, EndMin = 16 * 60 });   // contiguë
        Assert.NotEqual(PlanningEval.ActiveOccurrence(twoSlots, Monday.AddHours(11), "Bob"),
                        PlanningEval.ActiveOccurrence(twoSlots, Monday.AddHours(13), "Bob"));
        Assert.Null(PlanningEval.ActiveOccurrence(twoSlots, Monday.AddHours(17), "Bob"));

        var night = Plan(1, 22 * 60, 6 * 60);   // même occurrence de part et d'autre de minuit
        Assert.Equal(PlanningEval.ActiveOccurrence(night, Monday.AddHours(23), "Bob"),
                     PlanningEval.ActiveOccurrence(night, Monday.AddDays(1).AddHours(3), "Bob"));

        // Supprimer une plage PRÉCÉDENTE (index décalé) ou changer l'heure de fin ne crée pas une « nouvelle » occurrence.
        string? before = PlanningEval.ActiveOccurrence(twoSlots, Monday.AddHours(13), "Bob");
        twoSlots.Slots.RemoveAt(0);
        Assert.Equal(before, PlanningEval.ActiveOccurrence(twoSlots, Monday.AddHours(13), "Bob"));
        twoSlots.Slots[0].EndMin = 17 * 60;
        Assert.Equal(before, PlanningEval.ActiveOccurrence(twoSlots, Monday.AddHours(13), "Bob"));
    }

    [Fact]
    public void Occurrence_survives_editing_the_start_time_during_the_slot()
    {
        // Modifier l'heure de DÉBUT pendant la plage ne doit pas lever un arrêt manuel ni ré-armer le budget.
        var cp = Plan(1, 12 * 60, 16 * 60);
        string? before = PlanningEval.ActiveOccurrence(cp, Monday.AddHours(13), "Bob");
        cp.Slots[0].StartMin = 12 * 60 + 30;
        Assert.NotNull(before);
        Assert.Equal(before, PlanningEval.ActiveOccurrence(cp, Monday.AddHours(13), "Bob"));
    }

    [Fact]
    public void Planning_without_usable_slot_never_locks_the_user_out()
    {
        Assert.False(PlanningEval.HasUsableSlot(new CharPlanning { Enabled = true, AutoConnect = true }));
        Assert.False(PlanningEval.HasUsableSlot(Plan(0, 60, 120)));   // plage sans aucun jour coché
        Assert.True(PlanningEval.HasUsableSlot(Plan(1, 60, 120)));
    }

    [Fact]
    public void NextStart_null_without_days()
    {
        Assert.Null(PlanningEval.NextStart(Plan(0, 60, 120), Monday, "Bob"));
        Assert.False(PlanningEval.StartsWithin(Plan(0, 60, 120), Monday, "Bob", 10));
    }
}
