extern alias F06App;
using GivreLauncher.Core.Transport;
using Xunit;
using Journal = F06App::GivreClone.App.Services.Combat.CombatJournalService;

namespace GivreClone.Tests;

[CollectionDefinition("Transport journal and timing", DisableParallelization = true)]
public sealed class TransportJournalAndTimingCollection;

[Collection("Transport journal and timing")]
public sealed class CombatJournalTransportTests
{
    [Fact]
    public void Server_cast_refusal_reaches_the_completed_journal_from_transport()
    {
        const uint pid = 4294967001;
        bool enabled = TransportLog.Enabled;
        Journal.Init();
        TransportLog.Enabled = true;
        try
        {
            TransportLog.Line(pid, "[combat] début de tour : MOI (round 1)");
            TransportLog.Line(pid, "[combat-cast] text message=170 params=[2 | 3] sequence_started=False");
            TransportLog.Line(pid, "[combat-planner] execute spell=12733 from=327 cell=342 target=-1");
            TransportLog.Line(pid, $"[combat] pid {pid} : bilan -> issue=0 xp=0 butin=[]");

            var fight = Assert.Single(Journal.List(pid));
            Assert.Contains(fight.Entries, entry => entry.Text.Contains("message=170 params=[2 | 3]"));
            Assert.Contains(fight.Entries, entry => entry.Text.Contains("execute spell=12733 from=327 cell=342 target=-1"));
            Assert.Equal(0, fight.Casts);
            Assert.Equal(0, fight.Moves);
        }
        finally
        {
            TransportLog.Enabled = enabled;
        }
    }
}
