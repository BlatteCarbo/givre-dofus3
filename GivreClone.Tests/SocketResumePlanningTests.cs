using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class SocketResumePlanningTests
{
    [Fact]
    public void Suspended_host_outside_slot_never_resolves_or_starts_a_script()
    {
        var action = PlanningEval.DecideRun("previous", null, false, null, false, true,
            () => throw new InvalidOperationException("No script may execute outside the slot"));
        Assert.Equal(PlanRunAction.None, action);
    }

    [Theory]
    [InlineData("same-slot")]
    [InlineData("previous-slot")]
    [InlineData(null)]
    public void Suspended_host_resolves_current_body_even_when_the_occurrence_has_not_changed(string? served)
    {
        string? started = null;
        var action = PlanningEval.DecideRun(served, "same-slot", false, null, false, true,
            () => started = "-- freshly edited script");
        Assert.Equal(PlanRunAction.Start, action);
        Assert.Equal("-- freshly edited script", started);
    }

    [Fact]
    public void Manual_pause_and_missing_body_do_not_restart_the_old_host()
    {
        Assert.Equal(PlanRunAction.None, PlanningEval.DecideRun("slot", "slot", false, null, true, true,
            () => throw new InvalidOperationException("Manual pause must win")));
        Assert.Equal(PlanRunAction.NoScript, PlanningEval.DecideRun("slot", "slot", false, null, false, true, () => null));
    }
}
