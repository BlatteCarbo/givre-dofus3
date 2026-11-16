using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class CombatPolicySummaryTests
{
    [Fact]
    public void Summary_states_every_decision_setting_of_the_active_profile()
    {
        var p = new CombatProfile
        {
            AutoFight = true, Style = CombatStyle.Fleeing, PlacementMode = PlacementMode.Far, MovementPolicy = CombatMovementPolicy.NeverApproach,
            Target = TargetMode.LowestHp, FinishKill = false, Speed = CombatSpeed.Turbo, AutoSpells = true,
            KiteMinDistance = 3, KiteMaxDistance = 6, FleeBelowHpPercent = 25, MaxCastsPerTurn = 4,
            Spells = { new SpellConfig { SpellId = 1 }, new SpellConfig { SpellId = 2, Enabled = false } },
        };
        Assert.Equal("auto=oui style=Fleeing placement=Far déplacement=NeverApproach cible=LowestHp achever=non vitesse=Turbo"
            + " sorts=auto+liste (1/2 actifs) kite=3-6 fuite<25% lancers/tour≤4", p.PolicySummary());
    }

    [Fact]
    public void Unbounded_kite_and_no_flee_threshold_are_spelled_out()
    {
        string s = new CombatProfile().PolicySummary();
        Assert.Contains("kite=4-∞", s);
        Assert.Contains("fuite<jamais", s);
    }
}
