using GivreClone.App.Services;
using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public class LuaPrintTests
{
    [Theory]
    [InlineData("print")]
    [InlineData("printSuccess")]
    [InlineData("printWarning")]
    [InlineData("printError")]
    public void Preserves_all_arguments_and_trailing_nil(string name)
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);
        var lines = new List<string>();
        script.Globals[name] = LuaPrint.Callback(lines.Add);
        script.DoString($"{name}('carte',45,true,nil,false,nil)");
        Assert.Equal(new[] { "carte\t45\ttrue\tnil\tfalse\tnil" }, lines);
    }

    [Fact]
    public void Empty_call_emits_one_empty_line()
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);
        var lines = new List<string>();
        script.Globals["print"] = LuaPrint.Callback(lines.Add);
        Assert.True(script.DoString("return print()").IsNil());
        Assert.Equal(new[] { "" }, lines);
    }
}
