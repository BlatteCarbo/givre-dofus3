extern alias F06App;
using Xunit;
using McpActionJournal = F06App::GivreClone.App.Services.Mcp.McpActionJournal;
using McpActionState = F06App::GivreClone.App.Services.Mcp.McpActionState;

namespace GivreClone.Tests;

public sealed class McpActionJournalTests
{
    [Fact]
    public void Accepted_call_is_not_reported_as_ended_until_an_execution_signal()
    {
        var journal = new McpActionJournal();
        long id = journal.Begin("travel_to", "Bastionbot");
        Assert.Equal(McpActionState.Running, Assert.Single(journal.Read()).State);
        journal.SetState(id, McpActionState.Accepted);
        Assert.Equal(McpActionState.Accepted, Assert.Single(journal.Read()).State);
        journal.SetState(id, McpActionState.ExecutionEnded);
        Assert.Equal(McpActionState.ExecutionEnded, Assert.Single(journal.Read()).State);
    }

    [Theory]
    [InlineData(McpActionState.Failed)]
    [InlineData(McpActionState.Refused)]
    [InlineData(McpActionState.Cancelled)]
    [InlineData(McpActionState.ExecutionCompleted)]
    [InlineData(McpActionState.ExecutionEnded)]
    [InlineData(McpActionState.ExecutionFailed)]
    public void Errors_and_cancellation_remain_distinguishable(McpActionState state)
    {
        var journal = new McpActionJournal();
        long id = journal.Begin("get_bot_state", "Cible non résolue");
        journal.SetState(id, state);
        Assert.Equal(state, Assert.Single(journal.Read()).State);
    }

    [Fact]
    public void Retention_is_bounded_and_a_late_completion_cannot_restore_an_evicted_call()
    {
        var journal = new McpActionJournal(2);
        long first = journal.Begin("get_inventory", "A");
        long second = journal.Begin("get_map_state", "B");
        long third = journal.Begin("get_bot_state", "C");
        journal.SetState(first, McpActionState.Returned);
        Assert.Equal(new[] { third, second }, journal.Read().Select(e => e.Id));
    }

    [Fact]
    public void Invalid_tool_payload_is_not_retained_as_a_name()
    {
        var journal = new McpActionJournal();
        journal.Begin("invalid\npassword=SECRET", "Application");
        Assert.DoesNotContain("SECRET", Assert.Single(journal.Read()).Tool);
    }
}
