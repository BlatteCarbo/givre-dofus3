extern alias F06App;
using System.Reflection;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;
using Xunit;
using Xunit.Abstractions;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using World = F06App::GivreClone.App.Services.BotWorldStore;

namespace GivreClone.Tests;

public sealed class QaLuaApiRegistrationTests(ITestOutputHelper output)
{
    [Fact]
    public void All_host_bindings_register_without_a_game_session()
    {
        var launch = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var host = new Host(717, launch, new World(launch));
        var script = new Script(CoreModules.Preset_SoftSandbox);
        var standard = script.Globals.Pairs.Where(p => p.Key.Type == DataType.String)
            .ToDictionary(p => p.Key.String, p => p.Value);
        var standardMethods = standard.Where(p => p.Value.Type == DataType.Table)
            .ToDictionary(p => p.Key, p => p.Value.Table.Pairs.Where(m => m.Key.Type == DataType.String)
                .ToDictionary(m => m.Key.String, m => m.Value));
        typeof(Host).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [script, CancellationToken.None]);

        var names = new List<string>();
        foreach (var pair in script.Globals.Pairs)
        {
            if (pair.Key.Type != DataType.String) continue;
            string name = pair.Key.String;
            if (pair.Value.Type == DataType.ClrFunction
                && (!standard.TryGetValue(name, out var previous) || !ReferenceEquals(previous, pair.Value)))
                names.Add(name);
            if (pair.Value.Type != DataType.Table) continue;
            if (ReferenceEquals(pair.Value.Table, script.Globals)) continue;
            foreach (var method in pair.Value.Table.Pairs)
                if (method.Key.Type == DataType.String && method.Value.Type == DataType.ClrFunction)
                {
                    if (standardMethods.TryGetValue(name, out var methods)
                        && methods.TryGetValue(method.Key.String, out var previousMethod)
                        && ReferenceEquals(previousMethod, method.Value)) continue;
                    names.Add(name + ":" + method.Key.String);
                }
        }
        Assert.True(names.Count > 900, $"Only {names.Count} bindings registered");
        output.WriteLine($"API COUNT: {names.Count}\n" + string.Join("\n", names.Order()));
    }
}
