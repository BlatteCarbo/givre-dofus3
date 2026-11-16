using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class TurboLogDiagnosticsTests
{
    [Fact]
    public void Separates_interleaved_bots_and_does_not_call_a_late_success_a_skipped_turn()
    {
        var lines = new[]
        {
            "10:00:00.000 <1> [turbo-metric] request_tx kind=cast t=10",
            "10:00:01.500 <1> [turbo-metric] BARRIERE NON FERMEE après 1500 ms",
            "10:00:01.501 <1> [turbo-metric] barrière lente -> seconde chance 3500 ms",
            "10:00:01.600 <1> [turbo-metric] barrier_closed action=42 request_to_ack_ms=1600 sequence_end_to_ack_ms=15",
            "10:00:02.000 <1> [combat] pid 1 : fin du tour (FightTurnFinishRequest) inject=OK",
            "10:00:02.100 <2> [turbo-metric] request_tx kind=cast t=20",
            "10:00:03.600 <2> [turbo-metric] BARRIERE NON FERMEE après 1500 ms",
            "10:00:03.700 <1> [combat] pid 1 : fin du tour (FightTurnFinishRequest) inject=OK",
            "10:00:03.800 <2> [combat] pid 2 : fin du tour (FightTurnFinishRequest) inject=OK"
        };
        var all = TurboLogDiagnostics.Analyze(lines);
        Assert.Equal(1, all.EndsAfterTimeout);
        var first = TurboLogDiagnostics.Analyze(lines, 1);
        Assert.Equal((1, 1, 1, 1, 0), (first.Requests, first.Closed, first.Timeouts, first.GraceRetries, first.EndsAfterTimeout));
        Assert.Equal((1600, 15), (first.MaxWaitMs, first.MaxSequenceToAckMs));
    }

    [Fact]
    public void Accepts_ui_lines_without_pid_and_ignores_unrelated_errors()
    {
        var report = TurboLogDiagnostics.Analyze(new[]
        {
            "12:00:00.000 [turbo-native] runtime Visual C++ absent -> Turbo natif impossible",
            "12:00:00.001 [socket-auth] échec",
            "12:00:00.002 [turbo-metric] barrier_closed action=1 request_to_ack_ms=9999999999999999 sequence_end_to_ack_ms=4"
        }, 5);
        Assert.Equal(1, report.NativeErrors);
        Assert.Equal(0, report.MaxWaitMs);
        Assert.Equal(4, report.MaxSequenceToAckMs);
    }
}
