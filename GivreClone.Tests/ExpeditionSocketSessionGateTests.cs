using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class ExpeditionSocketSessionGateTests
{
    private static ExpeditionSocketSessionSnapshot Ready => new(
        IsSocketSession: true,
        AccountRunning: true,
        SessionParked: false,
        SessionBackoff: false,
        Reconnecting: false,
        WaitingForNewSession: false,
        GameLinkDown: false,
        HasServerMap: true,
        HasFightPermission: true);

    [Fact]
    public void Mitm_is_never_gated_by_socket_preflight()
    {
        var noState = new ExpeditionSocketSessionSnapshot(
            IsSocketSession: false, AccountRunning: false,
            SessionParked: true, SessionBackoff: true, Reconnecting: true,
            WaitingForNewSession: true, GameLinkDown: true,
            HasServerMap: false, HasFightPermission: false,
            BottingAuthorized: false, LevelAllowed: false, IsFollowerLocked: true);
        Assert.Null(ExpeditionSocketSessionGate.StartBlockReason(noState));
    }

    [Fact]
    public void Account_row_running_without_confirmed_map_is_not_ready()
    {
        var reason = ExpeditionSocketSessionGate.StartBlockReason(Ready with { HasServerMap = false });
        Assert.Contains("carte", reason!);
        Assert.Contains("serveur", reason!);
    }

    [Theory]
    [InlineData("parked")]
    [InlineData("backoff")]
    [InlineData("reconnect")]
    [InlineData("waiting")]
    public void Reconnecting_or_parked_socket_does_not_start_expedition(string state)
    {
        var s = state switch
        {
            "parked" => Ready with { SessionParked = true },
            "backoff" => Ready with { SessionBackoff = true },
            "reconnect" => Ready with { Reconnecting = true },
            _ => Ready with { WaitingForNewSession = true }
        };
        Assert.Contains("reconnexion", ExpeditionSocketSessionGate.StartBlockReason(s)!);
    }

    [Fact]
    public void Socket_link_down_refuses_even_if_stale_map_exists()
        => Assert.Contains("coupé", ExpeditionSocketSessionGate.StartBlockReason(Ready with { GameLinkDown = true })!);

    [Fact]
    public void Map_known_without_socket_injector_does_not_enable_game_actions()
        => Assert.Contains("injecteur", ExpeditionSocketSessionGate.StartBlockReason(Ready with { InjectorRegistered = false })!);

    [Fact]
    public void No_fight_permission_does_not_claim_expedition_capability()
        => Assert.Contains("Combat", ExpeditionSocketSessionGate.StartBlockReason(Ready with { HasFightPermission = false })!);

    [Fact]
    public void Account_not_running_is_not_a_live_socket_session()
        => Assert.Contains("hors ligne", ExpeditionSocketSessionGate.StartBlockReason(Ready with { AccountRunning = false })!);

    [Fact]
    public void License_or_level_cap_blocks_action_even_if_map_is_known()
    {
        Assert.Contains("licence", ExpeditionSocketSessionGate.StartBlockReason(Ready with { BottingAuthorized = false })!);
        Assert.Contains("niveau", ExpeditionSocketSessionGate.StartBlockReason(Ready with { LevelAllowed = false })!);
        Assert.Contains("suiveur", ExpeditionSocketSessionGate.StartBlockReason(Ready with { IsFollowerLocked = true })!);
    }

    [Fact]
    public void All_known_preconditions_clear_is_not_an_ack()
    {
        // The gate deliberately has no Send/ACK inputs or callbacks: this merely
        // authorizes attempting a local Lua start, not asserting game success.
        Assert.Null(ExpeditionSocketSessionGate.StartBlockReason(Ready));
    }
}
