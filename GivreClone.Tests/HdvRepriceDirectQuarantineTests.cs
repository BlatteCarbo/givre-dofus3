extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

/// <summary>
/// Exercises the real direct LaunchService reprice command entry, rather than
/// merely the correlation helper. The pending state is installed through the
/// same private helper used after an ambiguous attempted emission; no network
/// or game process is required to verify its next-action admission boundary.
/// </summary>
public sealed class HdvRepriceDirectQuarantineTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void SeedIdentity(LaunchService launch, uint pid, int serverId, long characterId)
    {
        var server = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(LaunchService).GetField("_serverInfo", Hidden)!.GetValue(launch)!;
        var characters = (ConcurrentDictionary<uint, long>)
            typeof(LaunchService).GetField("_charId", Hidden)!.GetValue(launch)!;
        server[pid] = (serverId, "fixture-offline");
        characters[pid] = characterId;
    }

    private static void FlagOriginalProfile(LaunchService launch, uint pid,
        int serverId, long characterId, int oldUid, int quantity, long price)
    {
        var nested = typeof(LaunchService).GetNestedType("HdvMutationSession",
            BindingFlags.NonPublic)!;
        Assert.NotNull(nested);
        var original = Activator.CreateInstance(nested,
            [serverId, characterId, 21L])!;
        typeof(LaunchService).GetMethod("FlagHdvRepriceReviewForSession", Hidden)!
            .Invoke(launch, [pid, original, oldUid, quantity, price]);
    }

    private static void SimulatePreviouslyAmbiguousAttempt(
        LaunchService launch, uint pid, int oldUid, int quantity, long price)
    {
        MethodInfo method = typeof(LaunchService).GetMethod(
            "FlagHdvRepriceReview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(method);
        method.Invoke(launch, new object[] { pid, oldUid, quantity, price });
    }

    [Fact]
    public async Task Direct_reprice_blocks_replays_and_different_uid_after_ambiguous_attempt()
    {
        var launch = new LaunchService();
        SimulatePreviouslyAmbiguousAttempt(launch, pid: 711, oldUid: 300, quantity: 10, price: 3000);
        Assert.True(launch.HdvRepriceReviewPending(711));

        // Admission is evaluated before license, market, network and PID-only
        // session state. Both exact replay and changed UID must be denied.
        Assert.False(await launch.HdvRepriceAsync(711, 300, 10, 3000));
        Assert.False(await launch.HdvRepriceAsync(711, 301, 10, 3200));
        Assert.True(launch.HdvRepriceReviewPending(711));
        Assert.False(launch.HdvRepriceReviewPending(712));
    }

    [Fact]
    public async Task Unlimited_tax_setting_does_not_remove_unresolved_order_or_affect_other_pid()
    {
        var launch = new LaunchService();
        SimulatePreviouslyAmbiguousAttempt(launch, pid: 110, oldUid: 55, quantity: 100, price: 8500);
        launch.SetHdvSaleLimits(110, ceilingMultiplier: 0m, maxTaxPerOperation: 0);

        Assert.False(await launch.HdvRepriceAsync(110, 55, 100, 8500));
        Assert.True(launch.HdvRepriceReviewPending(110));
        Assert.False(launch.HdvRepriceReviewPending(111));
        Assert.False(launch.HdvAcknowledgeRepriceReview(111));
        Assert.True(launch.HdvRepriceReviewPending(110));
    }

    [Fact]
    public void Only_explicit_operator_review_arms_new_action_without_faking_server_ack()
    {
        var launch = new LaunchService();
        SimulatePreviouslyAmbiguousAttempt(launch, pid: 44, oldUid: 22, quantity: 1, price: 300);
        Assert.True(launch.HdvRepriceReviewPending(44));
        Assert.True(launch.HdvAcknowledgeRepriceReview(44));
        Assert.False(launch.HdvRepriceReviewPending(44));
        Assert.False(launch.HdvAcknowledgeRepriceReview(44));
    }

    [Fact]
    public async Task Original_character_quarantine_survives_forget_and_pid_reuse_without_blocking_another_character()
    {
        var launch = new LaunchService();
        const uint oldPid = 811, newPid = 812, anotherPid = 813;
        SeedIdentity(launch, oldPid, 292, 99100);
        FlagOriginalProfile(launch, oldPid, 292, 99100,
            oldUid: 199, quantity: 10, price: 4500);
        Assert.True(launch.HdvRepriceReviewPending(oldPid));

        // The real ForgetHdv clears the transient per-PID UI state, not the
        // unreviewed original character's mutation. There is no new network
        // send and no inference from a same-PID decoder epoch.
        typeof(LaunchService).GetMethod("ForgetHdv", Hidden)!.Invoke(launch, [oldPid]);
        Assert.True(launch.HdvRepriceReviewPending(oldPid));

        SeedIdentity(launch, oldPid, 292, 99200); // PID re-used by another character
        Assert.False(launch.HdvRepriceReviewPending(oldPid));
        SeedIdentity(launch, newPid, 292, 99100); // original character new PID
        SeedIdentity(launch, anotherPid, 353, 99100); // same actor ID on another server
        Assert.True(launch.HdvRepriceReviewPending(newPid));
        Assert.False(launch.HdvRepriceReviewPending(anotherPid));
        Assert.False(await launch.HdvRepriceAsync(newPid, 200, 10, 4700));

        Assert.True(launch.HdvAcknowledgeRepriceReview(newPid));
        Assert.False(launch.HdvRepriceReviewPending(newPid));
        Assert.False(launch.HdvAcknowledgeRepriceReview(anotherPid));
    }
}
