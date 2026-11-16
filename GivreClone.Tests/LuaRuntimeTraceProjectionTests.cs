using GivreClone.App.Services.LuaEditor;
using Xunit;

namespace GivreClone.Tests;

public sealed class LuaRuntimeTraceProjectionTests
{
    [Fact]
    public void Uses_latest_session_and_observed_status_not_a_guessed_running_state()
    {
        string log = "===== session 2026-10-19 11:11:00 (pid 77) =====\n" +
            "11:11:05 [ERR ] script      old.lua:7 — old error\n" +
            "===== session 2026-10-20 11:00:00 (pid 77) =====\n" +
            "11:00:01 [INFO] deplacement  Déplacement demandé\n";
        var report = RuntimeTraceProjection.Project(log, isRunning: null);
        Assert.Equal("unknown", report.RunState);
        Assert.Null(report.LastError);
        Assert.NotNull(report.LastActivity);
        Assert.Equal(1, report.ParsedLines);
        Assert.Contains("2026-10-20", report.SessionHeader);
    }

    [Fact]
    public void Reports_actual_moonsharp_line_and_bounded_neighbouring_source_lines()
    {
        var report = RuntimeTraceProjection.Project(
            "===== session 2026-10-20 11:00:00 (pid 77) =====\n11:00:04 [ERR ] script      tour.lua:2 — attempt to call a nil value\n",
            isRunning: false, source: "local a = 1\ngetJobLevel()\nreturn a\n");
        Assert.Equal("stopped-observed-by-caller", report.RunState);
        Assert.Equal("ERR", report.LastError!.Level);
        Assert.Equal(2, report.LastIssue!.Line);
        Assert.Equal("getJobLevel()", report.LastIssue.SourceLine);
        Assert.Equal("local a = 1", report.LastIssue.PreviousLine);
        Assert.Equal("return a", report.LastIssue.NextLine);
    }

    [Fact]
    public void Handles_no_log_malformed_lines_and_not_found_source_without_false_success()
    {
        Assert.Equal(0, RuntimeTraceProjection.Project(null, true).ParsedLines);
        var report = RuntimeTraceProjection.Project("12:00:00 [ERR ] Lua         chunk:(999,2): invalid operation", null, "return 0");
        Assert.Equal(999, report.LastIssue?.Line);
        Assert.Null(report.LastIssue?.SourceLine);
        Assert.Equal("unknown", report.RunState);
    }

    [Fact]
    public void Input_is_bounded_even_for_very_large_logs()
    {
        var report = RuntimeTraceProjection.Project(new string('X', 2_000_000), null);
        Assert.True(report.Truncated);
        Assert.Null(report.LastError);
    }
}
