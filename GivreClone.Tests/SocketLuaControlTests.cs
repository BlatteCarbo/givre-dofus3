using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class SocketLuaControlTests
{
    private static SocketLuaControlSnapshot Ready() => new(
        IsSocketSession: true, AccountRunning: true,
        SessionParked: false, SessionBackoff: false,
        Reconnecting: false, WaitingForNewSession: false,
        HasServerMap: true, InjectorRegistered: true,
        ControlState: "channel-registered");

    [Fact]
    public void Ready_socket_channel_may_attempt_Lua_but_does_not_certify_server_ACK()
    {
        Assert.Null(SocketLuaControlAdmission.StartBlockReason(Ready()));
        // In the App this only permits host.Start; server ACKs are per-action.
    }

    [Theory]
    [InlineData("observation-only", true)]
    [InlineData("awaiting-map", true)]
    [InlineData("mapping-unavailable", true)]
    [InlineData("license-blocked", true)]
    [InlineData("botting-paused", true)]
    [InlineData("offline", false)]
    [InlineData("suspended", false)]
    [InlineData("channel-registered", false)]
    public void No_injector_means_no_script_even_with_running_account_and_server_map(
        string observedState, bool hasServerMap)
    {
        var sample = Ready() with
        {
            ControlState = observedState,
            HasServerMap = hasServerMap,
            InjectorRegistered = false
        };
        Assert.NotNull(SocketLuaControlAdmission.StartBlockReason(sample));
    }

    [Theory]
    [InlineData("observation-only")]
    [InlineData("awaiting-map")]
    [InlineData("mapping-unavailable")]
    [InlineData("license-blocked")]
    [InlineData("botting-paused")]
    [InlineData("suspended")]
    [InlineData("offline")]
    [InlineData("")]
    public void Registered_injector_alone_does_not_override_bad_control_state(string state)
    {
        Assert.NotNull(SocketLuaControlAdmission.StartBlockReason(
            Ready() with { ControlState = state }));
    }

    [Fact]
    public void Map_observation_is_required_even_with_registered_injector()
    {
        Assert.NotNull(SocketLuaControlAdmission.StartBlockReason(
            Ready() with { HasServerMap = false }));
    }

    [Fact]
    public void Running_account_is_not_proof_of_socket_game_session()
    {
        Assert.NotNull(SocketLuaControlAdmission.StartBlockReason(
            Ready() with { AccountRunning = false }));
    }

    [Theory]
    [InlineData("parked")]
    [InlineData("backoff")]
    [InlineData("reconnecting")]
    [InlineData("waiting")]
    public void A_suspended_or_reconnecting_session_cannot_start_or_auto_resume(string key)
    {
        var ready = Ready();
        var candidate = key switch
        {
            "parked" => ready with { SessionParked = true },
            "backoff" => ready with { SessionBackoff = true },
            "reconnecting" => ready with { Reconnecting = true },
            _ => ready with { WaitingForNewSession = true }
        };
        Assert.NotNull(SocketLuaControlAdmission.StartBlockReason(candidate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mitm_sessions_keep_legacy_admission_unchanged(bool isAccountRunning)
    {
        Assert.Null(SocketLuaControlAdmission.StartBlockReason(
            Ready() with
            {
                IsSocketSession = false,
                AccountRunning = isAccountRunning,
                HasServerMap = false,
                InjectorRegistered = false,
                ControlState = "offline",
                Reconnecting = true
            }));
    }

    [Fact]
    public void Unknown_or_missing_state_is_fail_closed()
    {
        Assert.NotNull(SocketLuaControlAdmission.StartBlockReason(
            Ready() with { ControlState = "Connected" }));
        Assert.NotNull(SocketLuaControlAdmission.StartBlockReason(
            Ready() with { ControlState = null }));
    }
}
