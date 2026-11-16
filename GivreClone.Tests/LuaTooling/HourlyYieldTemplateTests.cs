using System.Globalization;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public sealed class HourlyYieldTemplateTests
{
    [Theory]
    [InlineData(1800d, 120, 240d)]
    [InlineData(7200d, 120, 60d)]
    [InlineData(3600d, 0, 0d)]
    [InlineData(0d, 120, null)]
    [InlineData(null, 120, null)]
    public void Report_scales_by_measured_seconds_and_refuses_missing_duration(double? seconds, int resources, double? expected)
    {
        var script = new Script(CoreModules.None);
        var summary = new Table(script);
        summary["resources"] = resources;
        if (seconds is double value) summary["bottingSec"] = value;
        var stats = new Table(script);
        stats["summary"] = DynValue.NewCallback((_, _) => DynValue.NewTable(summary));
        script.Globals["stats"] = stats;
        string? report = null;
        int warnings = 0;
        script.Globals["printMessage"] = (Action<string, string>)((message, _) => report = message);
        script.Globals["printWarning"] = (Action<string>)(_ => warnings++);
        script.DoString(LuaTemplates.Scripts.Single(t => t.Id == "rendement-horaire").Body);

        Assert.False(script.Call(script.Globals.Get("move")).Boolean);

        if (expected is double rate)
        {
            Assert.NotNull(report);
            string numericRate = Regex.Match(report, @"[-+]?\d+(?:\.\d+)?$").Value;
            Assert.Equal(rate, double.Parse(numericRate, CultureInfo.InvariantCulture));
            Assert.Equal(0, warnings);
        }
        else
        {
            Assert.Null(report);
            Assert.Equal(1, warnings);
        }
    }
}
