using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public sealed class MeasuredSessionEconomics20261101Tests
{
    private static Script Load(string setup = "")
    {
        var directory = MappingScan.WalkUp(d => File.Exists(Path.Combine(
            d.FullName, "scripts", "lib", "Measured-Session-Economics-20261101.lua")));
        var lua = new Script(CoreModules.Preset_SoftSandbox);
        lua.DoString("""
            wallet, hours, character, server = 100, 0, "Test", "TestServer"
            reads, lines, bytes = 0, 0, 0
            function getKamas() reads = reads + 1 return wallet end
            function getCharacterName() return character end
            function getServerName() return server end
            global = {}
            function global:elapsedTime() return hours end
            function print(text) lines = lines + 1 bytes = bytes + #text end
            """);
        lua.DoString(setup);
        lua.Globals["economics"] = lua.DoString(File.ReadAllText(Path.Combine(
            directory!.FullName, "scripts", "lib", "Measured-Session-Economics-20261101.lua")));
        return lua;
    }

    private static Table Finish(Script lua, string between = "")
    {
        lua.DoString("economics.start()");
        lua.DoString(between);
        return lua.DoString("return economics.finish()").Table;
    }

    [Theory]
    [InlineData(150, 50)]
    [InlineData(50, -50)]
    [InlineData(100, 0)]
    public void Cashflow_is_signed_and_never_becomes_net_income(int wallet, int delta)
    {
        var lua = Load();
        var result = Finish(lua, $"wallet = {wallet}");
        Assert.Equal(delta, result.Get("walletDelta").Number);
        Assert.Equal(2, result.Get("sampleCount").Number);
        Assert.Equal(2, result.Get("knownWalletSamples").Number);
        Assert.Equal("unknown", result.Get("netProfitabilityStatus").String);
        Assert.True(result.Get("netIncome").IsNil());
        Assert.True(result.Get("knownFees").IsNil());
        Assert.True(result.Get("durationSeconds").IsNil());
        Assert.True(result.Get("cashflowPerHour").IsNil());
    }

    [Theory]
    [InlineData("wallet = -1", "wallet = 150", 1)]
    [InlineData("", "wallet = -1", 1)]
    [InlineData("wallet = -1", "", 0)]
    [InlineData("", "wallet = 0/0", 1)]
    [InlineData("", "wallet = math.huge", 1)]
    public void Unknown_wallet_is_not_zero(string setup, string between, int known)
    {
        var result = Finish(Load(setup), between);
        Assert.Equal(known, result.Get("knownWalletSamples").Number);
        Assert.True(result.Get("walletDelta").IsNil());
        Assert.Equal("unknown", result.Get("cashflowStatus").String);
    }

    [Theory]
    [InlineData("character = 'Other'")]
    [InlineData("server = 'OtherServer'")]
    [InlineData("character = ''")]
    public void Identity_discontinuity_does_not_create_cashflow(string between)
    {
        var result = Finish(Load(), between);
        Assert.False(result.Get("sameIdentity").Boolean);
        Assert.True(result.Get("walletDelta").IsNil());
    }

    [Fact]
    public void Whole_hour_clock_produces_bounds_not_an_exact_rate()
    {
        var result = Finish(Load("hours = 4"), "hours = 6");
        Assert.Equal(3600, result.Get("durationLowerBoundSeconds").Number);
        Assert.Equal(10800, result.Get("durationUpperBoundSecondsExclusive").Number);
        Assert.True(result.Get("durationSeconds").IsNil());
    }

    [Theory]
    [InlineData("hours = nil")]
    [InlineData("hours = -1")]
    [InlineData("hours = 0")]
    public void Missing_or_reset_clock_is_not_elapsed_time(string between)
    {
        var result = Finish(Load("hours = 4"), between);
        Assert.True(result.Get("durationLowerBoundSeconds").IsNil());
        Assert.True(result.Get("durationUpperBoundSecondsExclusive").IsNil());
    }

    [Fact]
    public void Observed_fee_subtotals_are_not_subtracted_twice_or_assumed_complete()
    {
        var lua = Load();
        lua.DoString("economics.start(); wallet = 90");
        var result = lua.DoString("""
            return economics.finish({knownFees = 10, knownOtherCosts = 0, source = "receipt:test"})
            """).Table;
        Assert.Equal(-10, result.Get("walletDelta").Number);
        Assert.Equal(10, result.Get("knownFees").Number);
        Assert.Equal(0, result.Get("knownOtherCosts").Number);
        Assert.Equal("unknown", result.Get("feesStatus").String);
        Assert.Equal("unknown", result.Get("costBasisStatus").String);
        Assert.Equal("unknown", result.Get("netProfitabilityStatus").String);
    }

    [Theory]
    [InlineData("{knownFees = -1, source = 'test'}")]
    [InlineData("{knownFees = 1}")]
    [InlineData("{knownOtherCosts = math.huge, source = 'test'}")]
    public void Invalid_evidence_does_not_cache_a_partial_result(string evidence)
    {
        var lua = Load();
        lua.DoString("economics.start()");
        Assert.Throws<ScriptRuntimeException>(() => lua.DoString($"economics.finish({evidence})"));
        Assert.True(lua.Globals.Get("READONLY_SESSION_ECONOMICS_RESULT").IsNil());
        var result = lua.DoString("return economics.finish()").Table;
        Assert.Equal(2, result.Get("sampleCount").Number);
        Assert.Equal(2, lua.Globals.Get("reads").Number);
    }

    [Fact]
    public void Finish_is_bounded_and_idempotent()
    {
        var lua = Load();
        Finish(lua);
        lua.DoString("for i = 1, 100 do economics.finish() end");
        Assert.Equal(2, lua.Globals.Get("reads").Number);
        Assert.Equal(1, lua.Globals.Get("lines").Number);
        Assert.InRange(lua.Globals.Get("bytes").Number, 1, 400);
        Assert.Throws<ScriptRuntimeException>(() => lua.DoString("economics.start()"));
    }

    [Fact]
    public void Preset_and_stop_callback_share_one_report()
    {
        var lua = Load();
        lua.DoString("function require(name) return economics end");
        var directory = MappingScan.WalkUp(d => File.Exists(Path.Combine(
            d.FullName, "scripts", "Readonly-Session-Economics-20261101.lua")));
        lua.DoString(File.ReadAllText(Path.Combine(
            directory!.FullName, "scripts", "Readonly-Session-Economics-20261101.lua")));
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        lua.DoString("stopped('completed'); move()");
        Assert.Equal(2, lua.Globals.Get("reads").Number);
        Assert.Equal(1, lua.Globals.Get("lines").Number);
    }
}
