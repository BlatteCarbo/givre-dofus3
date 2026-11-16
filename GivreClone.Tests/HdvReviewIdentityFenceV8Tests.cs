extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using HdvVm = F06App::GivreClone.App.ViewModels.HdvViewModel;

namespace GivreClone.Tests;

/// <summary>
/// The actual confirmation-dialog identity fence used by Sell, Remove,
/// Reprice and Buy. No Dialogs UI, network or account identity is faked.
/// </summary>
public sealed class HdvReviewIdentityFenceV8Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static (Launch Launch, HdvVm View) Create(uint pid)
    {
        var service = new Launch();
        var vm = (HdvVm)RuntimeHelpers.GetUninitializedObject(typeof(HdvVm));
        typeof(HdvVm).GetField("_launch", Hidden)!.SetValue(vm, service);
        typeof(HdvVm).GetField("_pid", Hidden)!.SetValue(vm, pid);
        return (service, vm);
    }

    private static void SetIdentity(Launch service, uint pid, int server, long actor)
    {
        var servers = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", Hidden)!.GetValue(service)!;
        var actors = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", Hidden)!.GetValue(service)!;
        servers[pid] = (server, "fixture");
        actors[pid] = actor;
    }

    private static bool StillSame(HdvVm vm, uint pid, int server, long actor)
        => (bool)typeof(HdvVm).GetMethod("SameHdvReviewIdentity", Hidden)!
            .Invoke(vm, [pid, server, actor])!;

    [Fact]
    public void Exact_server_actor_and_pid_allow_manual_review_dialog_to_continue()
    {
        var (service, vm) = Create(901);
        SetIdentity(service, 901, 292, 112233);
        Assert.True(StillSame(vm, 901, 292, 112233));
    }

    [Fact]
    public void Pid_reuse_character_swap_and_server_swap_invalidate_stale_confirmation()
    {
        var (service, vm) = Create(902);
        SetIdentity(service, 902, 292, 112233);
        Assert.True(StillSame(vm, 902, 292, 112233));
        SetIdentity(service, 902, 292, 998877);
        Assert.False(StillSame(vm, 902, 292, 112233));
        SetIdentity(service, 902, 353, 112233);
        Assert.False(StillSame(vm, 902, 292, 112233));
        Assert.False(StillSame(vm, 999, 353, 112233));
    }

    [Theory]
    [InlineData((uint)0, 292, 112233L)]
    [InlineData((uint)902, 0, 112233L)]
    [InlineData((uint)902, 292, 0L)]
    public void Missing_identity_refuses_review_even_when_pid_is_unchanged(
        uint pid, int server, long actor)
    {
        var (service, vm) = Create(902);
        SetIdentity(service, 902, 292, 112233);
        Assert.False(StillSame(vm, pid, server, actor));
    }
}
