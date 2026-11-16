using System.Buffers.Binary;
using System.Text;
using GivreClone.App.Services.Diagnostics;
using GivreClone.App.Services.Mcp;
using GivreControl;
using Xunit;

namespace GivreClone.Tests;

// Pure offline contracts only: the test project deliberately does not reference the Avalonia ViewModel assembly.
public sealed class McpControlSecurityTests
{
    [Theory]
    [InlineData("PROTECTED@example.test", "protected", true)]
    [InlineData("protected@other.invalid", "protected@example.test", true)]
    [InlineData("unrelated@example.test", "protected", false)]
    [InlineData("", "protected", false)]
    public void Protected_account_check_uses_the_local_login_case_insensitively(string login, string blocked, bool expected)
        => Assert.Equal(expected, McpGuards.IsBlocked(login, [blocked]));

    [Fact]
    public void Console_rejects_negative_cursors_and_unbounded_regex_patterns()
    {
        var journal = new ConsoleJournal();
        journal.Add("error", "script", "lua.lua:5: invalid field");
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.Read(-1, 10));
        Assert.Throws<ArgumentException>(() => journal.Read(0, 10, new string('x', 501)));
        Assert.Single(journal.Read(0, 100).Lines);
    }

    [Fact]
    public async Task Waiting_on_console_honors_caller_cancellation()
    {
        var journal = new ConsoleJournal();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await journal.WaitAsync(0, "never", 30, canceled.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ControlFrame.MaxBytes + 1)]
    public async Task Malformed_pipe_frames_are_rejected_without_allocating_payload(int invalidLength)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, invalidLength);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ControlFrame.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void Readiness_never_claims_server_confirmation_from_unknown_proofs()
    {
        var report = BotReadiness.Evaluate(new BotReadiness.Input(
            "online", 1234, 1000, false, true, false, true,
            ProtocolVerified: null, ServerAcknowledgementVerified: null,
            HdvSellReview: false, HdvRemoveReview: false, HdvRepriceReview: false,
            DisconnectReason: null));
        Assert.False(report.MayAssumeGameplayConfirmed);
        Assert.Equal("not-verified", report.Verdict);
        Assert.Contains(report.Checks, c => c.Id == "protocol" && c.State == "unknown");
        Assert.Contains(report.Checks, c => c.Id == "server-ack" && c.State == "unknown");
    }
}
