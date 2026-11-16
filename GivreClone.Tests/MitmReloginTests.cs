using GivreClone.Engine;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public sealed class MitmReloginTests
{
    [Theory]
    [InlineData(ConnectionMode.Mitm, ConnectionMode.Mitm, 261, 261)]
    [InlineData(ConnectionMode.Mitm, ConnectionMode.Mitm, 261, 262)]
    [InlineData(ConnectionMode.Socket, ConnectionMode.Socket, 261, 261)]
    [InlineData(ConnectionMode.Socket, ConnectionMode.Mitm, 261, 261)]
    [InlineData(ConnectionMode.Mitm, ConnectionMode.Socket, 261, 261)]
    [InlineData(ConnectionMode.Mitm, ConnectionMode.Mitm, 0, 0)]
    public void In_process_relogin_is_not_used(ConnectionMode source, ConnectionMode target, int sourceServer, int targetServer)
        => Assert.False(MitmRelogin.UseInProcess(source, target, sourceServer, targetServer));

    [Fact]
    public void Armed_relogin_keeps_the_process_and_expects_the_link_close()
    {
        var session = new MitmReloginSession();
        var armed = session.Arm(1_000);
        Assert.Equal(MitmReloginPhase.Armed, armed.Phase);
        Assert.True(armed.SameProcess);
        Assert.True(armed.ExpectsLinkClose);
        Assert.True(armed.SuppressDisconnect);
        Assert.True(armed.SuppressLoadingStall);
        Assert.True(armed.SuppressTicketRefusal);
        Assert.False(armed.FallbackRelaunch);
    }

    [Fact]
    public void Client_close_ticket_refusal_and_new_session_are_not_a_disconnect()
    {
        var session = new MitmReloginSession();
        session.Arm(0);
        var closed = session.OnLinkClosed(4_500);
        Assert.Equal(MitmReloginPhase.LinkClosed, closed.Phase);
        Assert.True(closed.SuppressDisconnect);
        Assert.True(closed.SameProcess);
        Assert.False(closed.FallbackRelaunch);

        var refused = session.OnTicketRefused(6_300);
        Assert.Equal(MitmReloginPhase.LinkClosed, refused.Phase);
        Assert.True(refused.SuppressTicketRefusal);
        Assert.True(refused.SuppressDisconnect);
        Assert.False(refused.FallbackRelaunch);

        var ready = session.OnSessionReady(9_800);
        Assert.Equal(MitmReloginPhase.SessionReady, ready.Phase);
        Assert.True(ready.SuppressLoadingStall);
        Assert.True(ready.SuppressDisconnect);
        Assert.False(ready.FallbackRelaunch);
    }

    [Fact]
    public void Session_can_become_ready_without_a_separately_observed_close()
    {
        var session = new MitmReloginSession();
        session.Arm(0);
        var ready = session.OnSessionReady(500);
        Assert.Equal(MitmReloginPhase.SessionReady, ready.Phase);
        Assert.True(ready.SuppressLoadingStall);
        Assert.False(ready.FallbackRelaunch);
    }

    [Fact]
    public void Confirm_ends_the_suppression_and_does_not_relaunch()
    {
        var session = new MitmReloginSession();
        session.Arm(0);
        session.OnLinkClosed(1);
        session.OnSessionReady(2);
        var done = session.Confirm(3);
        Assert.Equal(MitmReloginPhase.Confirmed, done.Phase);
        Assert.True(done.SameProcess);
        Assert.False(done.ExpectsLinkClose);
        Assert.False(done.SuppressDisconnect);
        Assert.False(done.SuppressLoadingStall);
        Assert.False(done.SuppressTicketRefusal);
        Assert.False(done.FallbackRelaunch);
        Assert.Equal(MitmReloginPhase.Confirmed, session.OnLinkClosed(4).Phase);
        Assert.Equal(MitmReloginPhase.Confirmed, session.Tick(MitmRelogin.TimeoutMs + 1).Phase);
        Assert.False(session.Tick(MitmRelogin.TimeoutMs + 1).FallbackRelaunch);
    }

    [Fact]
    public void Timeout_is_the_only_relaunch_and_follows_the_latest_progress()
    {
        var session = new MitmReloginSession();
        session.Arm(10_000);
        session.OnLinkClosed(10_100);
        var before = session.Tick(10_100 + MitmRelogin.TimeoutMs - 1);
        Assert.Equal(MitmReloginPhase.LinkClosed, before.Phase);
        Assert.True(before.SameProcess);
        Assert.True(before.SuppressDisconnect);
        Assert.True(before.SuppressLoadingStall);
        Assert.False(before.FallbackRelaunch);

        var timed = session.Tick(10_100 + MitmRelogin.TimeoutMs);
        Assert.Equal(MitmReloginPhase.TimedOut, timed.Phase);
        Assert.False(timed.SameProcess);
        Assert.False(timed.SuppressDisconnect);
        Assert.False(timed.SuppressLoadingStall);
        Assert.False(timed.SuppressTicketRefusal);
        Assert.True(timed.FallbackRelaunch);
        Assert.Equal(MitmReloginPhase.TimedOut, session.OnSessionReady(99_000).Phase);
        Assert.True(session.Confirm(99_001).FallbackRelaunch);
    }

    [Fact]
    public void Character_list_progress_restarts_the_relaunch_budget()
    {
        var session = new MitmReloginSession();
        session.Arm(0);
        session.OnLinkClosed(6_000);
        Assert.False(session.Tick(90_000).FallbackRelaunch);
        long listAt = 6_000 + MitmRelogin.TimeoutMs - 1_000;
        Assert.Equal(MitmReloginPhase.SessionReady, session.OnSessionReady(listAt).Phase);
        Assert.False(session.Tick(listAt + 1_000).FallbackRelaunch);
        Assert.True(session.Tick(listAt + MitmRelogin.TimeoutMs).FallbackRelaunch);
    }

    [Fact]
    public void A_second_arm_does_not_move_the_deadline()
    {
        var session = new MitmReloginSession();
        session.Arm(0);
        Assert.Equal(MitmReloginPhase.Armed, session.Arm(MitmRelogin.TimeoutMs - 1).Phase);
        Assert.True(session.Tick(MitmRelogin.TimeoutMs).FallbackRelaunch);
    }

    [Fact]
    public void Inject_failure_requests_relaunch_without_waiting()
    {
        var session = new MitmReloginSession();
        session.Arm(0);
        var failed = session.NoteInjectFailed(1);
        Assert.Equal(MitmReloginPhase.TimedOut, failed.Phase);
        Assert.True(failed.FallbackRelaunch);
        Assert.False(failed.SuppressDisconnect);
        Assert.False(session.NoteInjectFailed(2).SameProcess);
    }

    [Fact]
    public void Cancel_before_timeout_does_not_request_a_relaunch()
    {
        var session = new MitmReloginSession();
        session.Arm(0);
        session.OnTicketRefused(1);
        var cancelled = session.Cancel();
        Assert.Equal(MitmReloginPhase.Cancelled, cancelled.Phase);
        Assert.True(cancelled.SameProcess);
        Assert.False(cancelled.SuppressDisconnect);
        Assert.False(cancelled.SuppressLoadingStall);
        Assert.False(cancelled.FallbackRelaunch);
        Assert.Equal(MitmReloginPhase.Cancelled, session.Tick(MitmRelogin.TimeoutMs).Phase);
        Assert.False(session.Tick(MitmRelogin.TimeoutMs).FallbackRelaunch);
    }

    [Fact]
    public void Idle_session_ignores_link_events()
    {
        var session = new MitmReloginSession();
        Assert.Equal(MitmReloginPhase.Idle, session.OnLinkClosed(0).Phase);
        Assert.Equal(MitmReloginPhase.Idle, session.OnTicketRefused(0).Phase);
        Assert.Equal(MitmReloginPhase.Idle, session.Confirm(0).Phase);
        Assert.False(session.Tick(MitmRelogin.TimeoutMs).FallbackRelaunch);
        Assert.False(session.NoteInjectFailed(0).FallbackRelaunch);
    }

    [Fact]
    public void Relogin_request_carries_no_fields()
    {
        byte[] framed = MoveForge.BuildReloginFramed();
        Assert.NotEmpty(framed);
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        string code = catalog.CodeForName(MitmRelogin.RequestName) ?? "";
        Assert.Equal("knd", code);
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((int)len, framed.Length - pos);
        var message = RawProtobuf.Parse(framed[pos..]);
        int requestSlot = catalog.FieldNumber("GameMessage", "request") ?? -1;
        var request = message.Single(f => f.Field == requestSlot).Message;
        Assert.NotNull(request);
        int contentSlot = catalog.FieldNumber("Request", "content") ?? -1;
        var any = request!.Single(f => f.Field == contentSlot).Message;
        Assert.NotNull(any);
        Assert.Equal("type.ankama.com/" + code, any!.Single(f => f.Field == 1).String);
        Assert.DoesNotContain(any, f => f.Field == 2);
        // The transport log records the GameMessage (36 bytes). The forge adds the same length prefix as every other client request.
        Assert.Equal(36, (int)len);
        Assert.Equal("222208FFFFFFFFFFFFFFFFFF0112150A13747970652E616E6B616D612E636F6D2F6B6E64", Convert.ToHexString(framed[pos..]));
    }
}
