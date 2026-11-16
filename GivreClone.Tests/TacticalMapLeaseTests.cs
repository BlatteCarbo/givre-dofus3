extern alias F06App;
using System.Globalization;
using System.Reflection;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class TacticalMapLeaseTests
{
    [Fact]
    public void Pending_construction_has_a_hard_deadline_and_supersession_is_irreversible()
    {
        var type = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.TacticalConstructionAdmission")!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object New() => Activator.CreateInstance(type, flags, null, new object[] { 100L, 3L, 1234L, 9L, 1000L }, null)!;
        string? Record(object state, long generation, long now) => (string?)type.GetMethod("Record", flags)!
            .Invoke(state, new object[] { generation, 42, 200L, now });
        var pending = New();
        Assert.Equal("1 100 42 200 3 1234 9 3250", Record(pending, 9, 1000));
        Assert.Equal("1 100 42 200 3 1234 9 16000", Record(pending, 9, 15999));
        Assert.Null(Record(pending, 9, 16000));
        Assert.Null(Record(pending, 9, 1000));
        var superseded = New();
        Assert.Null(Record(superseded, 10, 1001));
        Assert.Null(Record(superseded, 9, 1002));
    }

    [Fact]
    public void Producer_token_matches_native_contract_and_changes_on_revocation()
    {
        var type = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.NativeTurboRuntime")!;
        var format = type.GetMethod("FormatTacticalLeaseToken", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            string Token(long epoch, long generation, bool enabled) =>
                (string)format.Invoke(null, new object[] { 100L, 42, 200L, epoch, 1234L, generation, enabled })!;
            Assert.Equal("1 100 42 200 3 1234 9 1", Token(3, 9, true));
            Assert.Equal("1 100 42 200 3 1234 10 0", Token(3, 10, false));
            Assert.NotEqual(Token(3, 9, true), Token(4, 9, true));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
