using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public sealed class MitmReloginAdmissionTests
{
    [Fact]
    public void Armed_relogin_reclassifies_the_next_socket_and_keeps_the_old_game_socket()
    {
        var admission = new MitmRelayAdmission();
        Assert.True(admission.Commit(7, known: true, owned: true).ConnectionServer);
        Assert.False(admission.Commit(7, known: true, owned: true).ConnectionServer);

        admission.Arm(7);
        Assert.False(admission.GameSocketReplaced(7));
        var login = admission.Commit(7, known: true, owned: true);
        Assert.True(login.Admit);
        Assert.True(login.ConnectionServer);
        Assert.Equal(7u, login.OwnerPid);
        Assert.False(admission.GameSocketReplaced(7));

        var game = admission.Commit(7, known: true, owned: true);
        Assert.False(game.ConnectionServer);
        Assert.True(admission.GameSocketReplaced(7));
        Assert.False(MitmReloginWatch.ShouldForceClose(expected: true, gameSocketReplaced: true));
    }

    [Fact]
    public void Link_close_after_the_fresh_hop_does_not_steal_the_game_socket()
    {
        var admission = new MitmRelayAdmission();
        Assert.True(admission.Commit(7, known: true, owned: true).ConnectionServer);
        Assert.False(admission.Commit(7, known: true, owned: true).ConnectionServer);
        admission.Arm(7);
        Assert.True(admission.Commit(7, known: true, owned: true).ConnectionServer);
        admission.NoteLinkClosed(7);
        Assert.False(admission.Commit(7, known: true, owned: true).ConnectionServer);
        Assert.True(admission.GameSocketReplaced(7));
    }

    [Fact]
    public void Link_close_before_any_fresh_socket_still_yields_one_connection_server()
    {
        var admission = new MitmRelayAdmission();
        Assert.True(admission.Commit(7, known: true, owned: true).ConnectionServer);
        Assert.False(admission.Commit(7, known: true, owned: true).ConnectionServer);
        admission.Arm(7);
        admission.NoteLinkClosed(7);
        Assert.True(admission.Commit(7, known: true, owned: true).ConnectionServer);
        Assert.False(admission.Commit(7, known: true, owned: true).ConnectionServer);
    }

    [Fact]
    public void One_armed_account_adopts_an_unknown_pid_and_zero_or_two_do_not()
    {
        var idle = new MitmRelayAdmission();
        Assert.False(idle.WouldAdmit(99, known: false, owned: false));
        Assert.False(idle.Commit(99, known: false, owned: false).Admit);

        var one = new MitmRelayAdmission();
        one.Arm(7);
        var adopted = one.Commit(99, known: false, owned: false);
        Assert.True(adopted.Admit);
        Assert.True(adopted.Adopted);
        Assert.Equal(7u, adopted.OwnerPid);
        Assert.True(adopted.ConnectionServer);
        var game = one.Commit(99, known: false, owned: false);
        Assert.Equal(7u, game.OwnerPid);
        Assert.False(game.ConnectionServer);
        Assert.True(one.GameSocketReplaced(7));

        var two = new MitmRelayAdmission();
        two.Arm(7);
        two.Arm(8);
        Assert.False(two.WouldAdmit(99, known: false, owned: false));
        Assert.False(two.Commit(99, known: false, owned: false).Admit);
        Assert.True(two.Commit(7, known: true, owned: true).ConnectionServer);
    }

    [Fact]
    public void Known_unowned_pid_is_refused_without_consuming_the_first_hop()
    {
        var admission = new MitmRelayAdmission();
        Assert.False(admission.WouldAdmit(7, known: true, owned: false));
        Assert.False(admission.Commit(7, known: true, owned: false).Admit);
        Assert.True(admission.Commit(7, known: true, owned: true).ConnectionServer);
    }

    [Fact]
    public void Disarm_keeps_later_sockets_as_game_and_drops_the_adoption()
    {
        var admission = new MitmRelayAdmission();
        Assert.True(admission.Commit(7, known: true, owned: true).ConnectionServer);
        Assert.False(admission.Commit(7, known: true, owned: true).ConnectionServer);
        admission.Arm(7);
        Assert.Equal(7u, admission.Commit(99, known: false, owned: false).OwnerPid);
        admission.Disarm(7);
        Assert.False(admission.WouldAdmit(99, known: false, owned: false));
        Assert.False(admission.Commit(7, known: true, owned: true).ConnectionServer);
        Assert.False(admission.GameSocketReplaced(7));
    }

    [Fact]
    public void Force_close_stops_once_the_replacement_game_socket_is_in()
    {
        Assert.True(MitmReloginWatch.ShouldForceClose(expected: true, gameSocketReplaced: false));
        Assert.False(MitmReloginWatch.ShouldForceClose(expected: true, gameSocketReplaced: true));
        Assert.False(MitmReloginWatch.ShouldForceClose(expected: false, gameSocketReplaced: false));
        Assert.Equal(90_000, MitmRelogin.TimeoutMs);
        Assert.Equal(2_000, MitmRelogin.TokenSilenceMs);
        Assert.Equal(6_000, MitmRelogin.ClientCloseAfterTokenMs);
        Assert.True(MitmRelogin.ClientCloseAfterTokenMs < MitmRelogin.TimeoutMs);
    }

    [Fact]
    public void Relogin_binding_is_wired_and_the_token_is_not_stored()
    {
        string link = Source("GivreClone.App/Services/LaunchService.GameLink.cs");
        string snow = Source("GivreClone.App/Services/LaunchService.Snow.cs");
        string launch = Source("GivreClone.App/Services/LaunchService.cs");
        Assert.Contains("_localRelay?.ArmRelogin(pid)", link);
        Assert.Contains("_localRelay?.DisarmRelogin(pid)", link);
        Assert.Contains("CloseGameClient(pid)", link);
        int note = snow.IndexOf("case \"ReloginTokenEvent\": NoteReloginToken(p.Pid);", StringComparison.Ordinal);
        int guard = note < 0 ? -1 : snow.IndexOf("if (LuaSnowCompat.IsSensitiveMessage(name)) return;", note, StringComparison.Ordinal);
        int store = snow.IndexOf("_lastByName[(p.Pid, name)] = p;", StringComparison.Ordinal);
        Assert.True(note >= 0 && guard > note && store > guard);
        Assert.Contains("OnReloginConnectionOpened(pid)", launch);
        Assert.Contains("_localRelay = local;", launch);
    }

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(MappingScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
}
