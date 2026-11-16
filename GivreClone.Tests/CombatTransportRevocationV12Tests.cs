extern alias F06App;

using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

/// <summary>
/// Exercises the ACTUAL Socket post-flush observer → exact-source EOF →
/// persisted opt-in combat advisor revocation. These local test frames do not
/// claim a confirmed spell, movement or transport ACK.
/// </summary>
public sealed class CombatTransportRevocationV12Tests
{
    private static readonly Assembly App = typeof(Launch).Assembly;
    private static readonly BindingFlags Hidden =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type SourceType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketLinkProvenance", true)!;
    private static readonly Type WrittenType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketTransportWriteObservation", true)!;
    private static readonly Type LedgerType = App.GetType(
        "GivreClone.App.Services.SocketSourceAuthorityLedger", true)!;
    private static readonly Type WindowType = typeof(Launch).GetNestedType(
        "TacticalAdvisorWindow", BindingFlags.NonPublic)!;

    private static object NewSource(uint pid, long epoch) =>
        Activator.CreateInstance(SourceType,
            [Guid.NewGuid(), epoch, Guid.NewGuid(), pid])!;

    private static object Written(object source, long ordinal) =>
        Activator.CreateInstance(WrittenType, Hidden, null,
            [source, ordinal, new byte[] { 1, 2, 3 },
                Environment.TickCount64], CultureInfo.InvariantCulture)!;

    private static Func<T, bool> Adapt<T>(Func<object, bool> predicate) =>
        value => predicate(value!);

    private static Delegate Validator(Func<object, bool> predicate) =>
        (Delegate)typeof(CombatTransportRevocationV12Tests)
            .GetMethod(nameof(Adapt), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(SourceType).Invoke(null, [predicate])!;

    private static bool SendLocalPostFlush(Launch launch, object source, long ordinal) =>
        (bool)typeof(Launch).GetMethod("FeedSocketWrittenObservation", Hidden)!
            .Invoke(launch, [Written(source, ordinal),
                Validator(s => Equals(s, source))])!;

    private static void ExactEof(Launch launch, object source) =>
        typeof(Launch).GetMethod("RevokeSocketObservedLink", Hidden)!
            .Invoke(launch, [source]);

    private static Launch NewInertService(uint pid)
    {
        var launch = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var ledger = Activator.CreateInstance(LedgerType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [true], CultureInfo.InvariantCulture)!;
        typeof(Launch).GetField("_socketSourceAuthority", Hidden)!
            .SetValue(launch, ledger);
        var windowsField = typeof(Launch).GetField("_tacticalAdvisorWindows", Hidden)!;
        object windows = Activator.CreateInstance(windowsField.FieldType)!;
        object window = Activator.CreateInstance(WindowType, nonPublic: true)!;
        WindowType.GetField("Enabled", BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(window, true);
        windowsField.FieldType.GetMethod("TryAdd")!.Invoke(windows, [pid, window]);
        windowsField.SetValue(launch, windows);
        return launch;
    }

    [Fact]
    public void Real_exact_current_socket_eof_revokes_opted_in_multiclass_advice_without_cast()
    {
        const uint pid = 89121;
        var launch = NewInertService(pid);
        object source = NewSource(pid, 22);
        Assert.True(SendLocalPostFlush(launch, source, 1));
        Assert.Equal(1, launch.SocketSourceAuthorityOf(pid)!.Value.EngineWritesCompleted);
        Assert.False(launch.SocketSourceAuthorityOf(pid)!.Value.ServerAckAttributed);
        ExactEof(launch, source);
        Assert.Null(launch.SocketSourceAuthorityOf(pid));
        var status = launch.ReadCombatTacticalAdvisoryStatus(pid);
        Assert.True(status.Enabled);
        Assert.Equal("Expired", status.Phase.ToString());
        Assert.Null(status.Candidate);
        Assert.False(status.TransportProvenanceEstablished);
        Assert.Contains("source revoked", status.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Old_eof_does_not_revoke_a_new_same_pid_link_or_expire_its_advisor()
    {
        const uint pid = 89122;
        var launch = NewInertService(pid);
        object oldLink = NewSource(pid, 3);
        object next = NewSource(pid, 4);
        Assert.True(SendLocalPostFlush(launch, oldLink, 1));
        Assert.True(SendLocalPostFlush(launch, next, 1));
        ExactEof(launch, oldLink);
        var live = launch.SocketSourceAuthorityOf(pid);
        Assert.NotNull(live);
        Assert.Equal(4, live.Value.RunEpoch);
        // The old link must not retroactively expire a successor's opt-in.
        Assert.NotNull(live);
        ExactEof(launch, next);
        Assert.Null(launch.SocketSourceAuthorityOf(pid));
        Assert.Equal("Expired",
            launch.ReadCombatTacticalAdvisoryStatus(pid).Phase.ToString());
    }

    [Fact]
    public void Duplicated_stale_source_eof_cannot_clear_the_next_source_counters()
    {
        const uint pid = 89123;
        var launch = NewInertService(pid);
        object first = NewSource(pid, 11);
        object replacement = NewSource(pid, 12);
        Assert.True(SendLocalPostFlush(launch, first, 1));
        Assert.True(SendLocalPostFlush(launch, replacement, 1));
        ExactEof(launch, first);
        ExactEof(launch, first);
        Assert.Equal(12, launch.SocketSourceAuthorityOf(pid)!.Value.RunEpoch);
        Assert.Equal(1, launch.SocketSourceAuthorityOf(pid)!.Value.EngineWritesCompleted);
        Assert.False(launch.SocketSourceAuthorityOf(pid)!.Value.ServerAckAttributed);
    }
}
