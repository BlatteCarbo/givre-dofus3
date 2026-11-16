using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Executes production MCP validators from the compiled application in an isolated load context.
/// No Avalonia window, account, game connection or persistent settings write is made.</summary>
public sealed class McpHarnessProductionTests
{
    private sealed class AppBoundary
    {
        private sealed class AppContext(string path) : AssemblyLoadContext("OfflineMcpHarness", isCollectible: false)
        {
            private readonly AssemblyDependencyResolver _resolver = new(path);
            protected override Assembly? Load(AssemblyName assemblyName)
            {
                var path = _resolver.ResolveAssemblyToPath(assemblyName);
                return path is null ? null : LoadFromAssemblyPath(path);
            }
        }

        internal readonly Type TargetType;
        internal readonly Type SettingsType;
        internal readonly MethodInfo Resolve;
        internal readonly MethodInfo DemandWritable;
        internal readonly MethodInfo ApplySettings;

        internal AppBoundary()
        {
            string config = new DirectoryInfo(System.AppContext.BaseDirectory).Parent?.Name ?? "Debug";
            // Project references copy the application beside the tests, including
            // when validation uses a separate output directory beside a live app.
            string? path = Path.Combine(System.AppContext.BaseDirectory, "GivreClone.dll");
            if (!File.Exists(path)) path = null;
            if (path is null)
            {
                var folder = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "GivreClone.App", "bin", config, "net10.0", "GivreClone.dll")));
                if (folder is not null)
                    path = Path.Combine(folder.FullName, "GivreClone.App", "bin", config, "net10.0", "GivreClone.dll");
            }
            if (path is null) throw new InvalidOperationException(
                "Compile GivreClone.App before running the MCP production harness; the live application is not needed.");
            var assembly = new AppContext(path).LoadFromAssemblyPath(path);
            TargetType = assembly.GetType("GivreClone.App.ViewModels.McpControlTarget", throwOnError: true)!;
            SettingsType = assembly.GetType("GivreClone.App.Services.BotSettings", throwOnError: true)!;
            var access = assembly.GetType("GivreClone.App.ViewModels.McpControlAccess", throwOnError: true)!;
            var botVm = assembly.GetType("GivreClone.App.ViewModels.BotsViewModel", throwOnError: true)!;
            Resolve = access.GetMethod("Resolve", BindingFlags.Static | BindingFlags.Public)!;
            DemandWritable = access.GetMethod("DemandWritable", BindingFlags.Static | BindingFlags.Public)!;
            ApplySettings = botVm.GetMethod("ApplySettings", BindingFlags.Static | BindingFlags.NonPublic)!;
        }

        internal object Target(string id, string alias, string name, int pid, string? login)
            => Activator.CreateInstance(TargetType, id, alias, name, pid, login)!;
        internal object Select(string selector, params object[] targets)
        {
            Array array = Array.CreateInstance(TargetType, targets.Length);
            for (int i = 0; i < targets.Length; i++) array.SetValue(targets[i], i);
            return Resolve.Invoke(null, [selector, array])!;
        }
        internal string AccountId(object target) => (string)TargetType.GetProperty("AccountId")!.GetValue(target)!;
        internal void WriteCheck(object target, string[]? blocked) => DemandWritable.Invoke(null, [target, blocked]);
        internal object Settings() => Activator.CreateInstance(SettingsType)!;
        internal object? Property(object settings, string name) => SettingsType.GetProperty(name)!.GetValue(settings);
        internal object Apply(object settings, string json, Action save)
        {
            using var input = JsonDocument.Parse(json);
            return ApplySettings.Invoke(null, [settings, input.RootElement, save])!;
        }
    }

    private static readonly Lazy<AppBoundary> App = new(() => new());
    private static Exception Rejected(Action action)
    {
        var invocation = Assert.Throws<TargetInvocationException>(action);
        return Assert.IsAssignableFrom<Exception>(invocation.InnerException);
    }
    private static object[] Targets(AppBoundary app) =>
    [
        app.Target("account-a", "shared", "Hero", 101, "locked@example.test"),
        app.Target("account-b", "shared", "Hero", 202, "clear@example.test"),
        app.Target("account-c", "unique", "Else", 0, "offline@example.test")
    ];

    [Theory]
    [InlineData("account-a", "account-a")]
    [InlineData("ACCOUNT-B", "account-b")]
    [InlineData("101", "account-a")]
    [InlineData("202", "account-b")]
    [InlineData("unique", "account-c")]
    public void Actual_selector_uses_exact_account_id_pid_and_unambiguous_alias(string query, string expected)
    {
        var app = App.Value;
        Assert.Equal(expected, app.AccountId(app.Select(query, Targets(app))));
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("Hero")]
    [InlineData("missing")]
    [InlineData("")]
    [InlineData(" ")]
    public void Actual_selector_rejects_ambiguous_and_unknown_names(string query)
    {
        var app = App.Value;
        Assert.IsType<ArgumentException>(Rejected(() => app.Select(query, Targets(app))));
    }

    [Fact]
    public void Actual_selector_rejects_duplicate_ids_and_pids()
    {
        var app = App.Value;
        var first = app.Target("duplicate", "one", "one", 12, "clear@example.test");
        var second = app.Target("duplicate", "two", "two", 12, "clear2@example.test");
        Assert.IsType<ArgumentException>(Rejected(() => app.Select("duplicate", first, second)));
        Assert.IsType<ArgumentException>(Rejected(() => app.Select("12", first, second)));
    }

    [Fact]
    public void Actual_write_guard_rejects_protected_and_missing_identity()
    {
        var app = App.Value;
        var targets = Targets(app);
        Assert.IsType<InvalidOperationException>(Rejected(() => app.WriteCheck(targets[0], ["locked"])));
        Assert.IsType<InvalidOperationException>(Rejected(() => app.WriteCheck(app.Target("", "x", "x", 1, "clear@example.test"), [])));
        Assert.IsType<InvalidOperationException>(Rejected(() => app.WriteCheck(app.Target("ok", "x", "x", 1, null), [])));
        app.WriteCheck(targets[1], ["locked"]);
    }

    [Theory]
    [InlineData("{\"PreferWalk\":\"true\"}")]
    [InlineData("{\"AntiStuckEnabled\":0}")]
    [InlineData("{\"NotifyAnomalyClosed\":null}")]
    [InlineData("{\"Speed\":\"Undefined\"}")]
    [InlineData("{\"Speed\":\"1\"}")]
    [InlineData("{\"Speed\":1}")]
    [InlineData("{\"Speed\":true}")]
    [InlineData("{\"DiscordWebhookUrl\":\"https://invalid.test\"}")]
    [InlineData("{\"LlmApiKeyProtected\":\"secret\"}")]
    [InlineData("{\"WalkMaxHops\":-1}")]
    [InlineData("{\"LegitSettleMinMs\":9000,\"LegitSettleMaxMs\":8000}")]
    [InlineData("{\"PreferWalk\":true,\"AntiStuckEnabled\":null}")]
    [InlineData("{\"Speed\":\"Legit\",\"WalkMaxHops\":-5}")]
    [InlineData("{\"PreferWalk\":true,\"PreferWalk\":false}")]
    [InlineData("{\"RegenerationEnabled\":true,\"RegenerationStartPercent\":96,\"RegenerationStopPercent\":95}")]
    [InlineData("{\"RegenerationStartPercent\":0}")]
    [InlineData("{\"RegenerationStopPercent\":101}")]
    [InlineData("{\"RegenerationStopPercent\":50}")]
    [InlineData("{\"RegenerationEnabled\":\"true\"}")]
    [InlineData("{\"RegenerationWaitForTeam\":1}")]
    [InlineData("{\"RegenerationFoodGid\":-1}")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Actual_settings_validator_rejects_before_mutation_and_never_saves(string json)
    {
        var app = App.Value;
        var settings = app.Settings();
        object?[] before = new[] { "Speed", "PreferWalk", "AntiStuckEnabled", "LegitSettleMinMs", "LegitSettleMaxMs",
            "RegenerationEnabled", "RegenerationStartPercent", "RegenerationStopPercent", "RegenerationFoodGid", "RegenerationWaitForTeam" }
            .Select(p => app.Property(settings, p)).ToArray();
        int saved = 0;
        Assert.IsType<ArgumentException>(Rejected(() => app.Apply(settings, json, () => saved++)));
        Assert.Equal(0, saved);
        object?[] after = new[] { "Speed", "PreferWalk", "AntiStuckEnabled", "LegitSettleMinMs", "LegitSettleMaxMs",
            "RegenerationEnabled", "RegenerationStartPercent", "RegenerationStopPercent", "RegenerationFoodGid", "RegenerationWaitForTeam" }
            .Select(p => app.Property(settings, p)).ToArray();
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(90, 95, 0, false)]
    [InlineData(1, 100, 42, true)]
    public void Regeneration_settings_apply_and_return_the_same_bounded_policy(int start, int stop, int food, bool team)
    {
        var app = App.Value;
        var settings = app.Settings();
        string changes = JsonSerializer.Serialize(new
        {
            RegenerationEnabled = true, RegenerationStartPercent = start, RegenerationStopPercent = stop,
            RegenerationFoodGid = food, RegenerationWaitForTeam = team
        });
        int saved = 0;

        var result = app.Apply(settings, changes, () => saved++);

        Assert.Equal(1, saved);
        using var response = JsonDocument.Parse(result.ToString()!);
        using var request = JsonDocument.Parse(changes);
        foreach (var property in request.RootElement.EnumerateObject())
        {
            var returned = response.RootElement.GetProperty("settings").GetProperty(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                Assert.Equal(property.Value.GetInt32(), app.Property(settings, property.Name));
                Assert.Equal(property.Value.GetInt32(), returned.GetInt32());
            }
            else
            {
                Assert.Equal(property.Value.GetBoolean(), app.Property(settings, property.Name));
                Assert.Equal(property.Value.GetBoolean(), returned.GetBoolean());
            }
        }
        Assert.False(response.RootElement.GetProperty("persistenceVerified").GetBoolean());
    }

    [Fact]
    public void Actual_settings_validator_applies_allowlist_atomically_with_noop_save()
    {
        var app = App.Value;
        var settings = app.Settings();
        int saved = 0;
        var result = app.Apply(settings,
            "{\"Speed\":\"Legit\",\"PreferWalk\":true,\"LegitSettleMinMs\":2000,\"LegitSettleMaxMs\":2500,\"NotifyAnomalyOpened\":true}",
            () => saved++);
        Assert.Equal(1, saved);
        Assert.Equal("Legit", app.Property(settings, "Speed")?.ToString());
        Assert.Equal(true, app.Property(settings, "PreferWalk"));
        Assert.Equal(2000, app.Property(settings, "LegitSettleMinMs"));
        Assert.Equal(2500, app.Property(settings, "LegitSettleMaxMs"));
        Assert.Contains("applied_in_memory", result.ToString());
        Assert.DoesNotContain("DiscordWebhookUrl", result.ToString());
        Assert.DoesNotContain("LlmApiKeyProtected", result.ToString());
    }
}
