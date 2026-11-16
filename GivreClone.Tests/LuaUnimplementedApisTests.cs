using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Contrat public de compatibilité Lua : une API avec valeur de secours doit le signaler
/// explicitement, et les opérations PNJ ne doivent pas confondre envoi et confirmation complète.
/// Tests de SOURCE/DOCUMENTATION (l'hôte Avalonia n'est pas référencé par le projet Tests).
/// </summary>
public sealed class LuaUnimplementedApisTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "docs", "portal", "lua-api-doc.json")));
        return dir?.FullName ?? throw new InvalidOperationException("Dépôt source et docs/portal introuvables pour le test de parité Lua");
    }

    private static string Host(string part) => File.ReadAllText(Path.Combine(Root, "GivreClone.App", "Services", "GivreLuaHost." + part + ".cs"));

    private static string Doc() => File.ReadAllText(Path.Combine(Root, "docs", "portal", "lua-api-portal-v2.0.1.txt"));

    private static IReadOnlyDictionary<string, string> Signatures(string module)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "portal", "lua-api-doc.json")));
        var cat = json.RootElement.GetProperty("categories").EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == module);
        string scope = module switch
        {
            "mod-fightcharacter" => "fightSlave:",
            "mod-mount" => "mount:",
            "mod-merchant" => "merchant:",
            "mod-quest" => "quest:",
            _ => throw new ArgumentOutOfRangeException(nameof(module)),
        };
        return cat.GetProperty("methods").EnumerateArray()
            .SelectMany(m => m.GetProperty("sig").EnumerateArray())
            .Where(s => s.TryGetProperty("ns", out var ns) && ns.GetString() == scope)
            .GroupBy(s => s.GetProperty("name").GetString()!)
            .ToDictionary(g => g.Key, g => g.Last().TryGetProperty("returns", out var ret) ? ret.GetString() ?? "" : "");
    }

    [Theory]
    [InlineData("mod-mount", "SnowNpc", "mount", "mount_telemetry_and_actions_unavailable")]
    [InlineData("mod-merchant", "SnowEconomy", "m", "player_merchant_mode_unavailable")]
    [InlineData("mod-quest", "SnowNpc", "q", "quest_snapshot_missing_no_list_request")]
    [InlineData("mod-fightcharacter", "SnowMap", "fsl", "controlled_summon_lua_context_unavailable")]
    public void An_unavailable_legacy_api_exposes_a_diagnosable_status(
        string module, string sourcePart, string bindingTable, string reason)
    {
        var signatures = Signatures(module);
        Assert.Equal("table", signatures["status"]);
        string source = Host(sourcePart);
        Assert.Contains("Fill(" + bindingTable + ", \"status\"", source);
        Assert.Contains(reason, source);
        Assert.Contains(reason, Doc());
        Assert.Contains("SnowStatusTable(s,", source);
    }

    [Theory]
    [InlineData("mod-merchant", "SnowEconomy", "open", "buyItem", "priceItem", "quantityItem", "existItem")]
    [InlineData("mod-mount", "SnowNpc", "hasMount", "isRiding", "toggleRiding", "feedMount", "setXpRatio")]
    [InlineData("mod-fightcharacter", "SnowMap", "entity", "name", "cellId", "canCastSpellOnCell", "moveTowardCell")]
    public void Legacy_names_and_return_types_are_preserved_in_the_documented_contract(
        string module, string part, params string[] names)
    {
        var signatures = Signatures(module);
        string source = Host(part);
        foreach (string method in names)
        {
            Assert.True(signatures.ContainsKey(method), $"API Lua non documentée : {module}.{method}");
            Assert.Contains("\"" + method + "\"", source);
        }
    }

    [Fact]
    public void Quest_live_reads_are_available_but_loading_does_not_invent_a_list_request()
    {
        string live = Host("LotI"), snow = Host("SnowNpc");
        Assert.Contains("_launch.QuestsLoaded(_pid)", live);
        Assert.Contains("_launch.ActiveQuests(_pid)", live);
        Assert.Contains("ValidateQuestObjective(", live);
        Assert.Contains("q[\"initialize\"]", snow);
        Assert.Contains("canRequestList", snow);
        Assert.Contains("DynValue.False", snow);
        Assert.Contains("QuestListRequest n'est pas disponible", Doc());
        Assert.Equal("bool", Signatures("mod-quest")["initialize"]);
    }

    [Fact]
    public void Npc_shop_and_legacy_sale_require_server_evidence_and_reject_invalid_quantities()
    {
        string npc = Host("SnowNpc"), economy = Host("SnowEconomy");
        string engine = File.ReadAllText(Path.Combine(Root, "GivreClone.App", "Services", "LaunchService.LotM.cs"));
        Assert.Contains("_launch.ShopOpenAsync(_pid, actor, ct)", npc);
        Assert.Contains("ShopOpenAsync(uint pid, long actorId", engine);
        Assert.Contains("&& LotMOf(pid).ShopOpen", engine);
        Assert.Contains("ExchangeNpcShopStartedEvent", engine);
        Assert.Contains("_launch.NpcBuyAsync(_pid, gid, qty, ct)", economy);
        Assert.Contains("_launch.NpcSellAsync(_pid, gid, qty, ct)", economy);
        Assert.Contains("if (gid <= 0 || qty <= 0)", economy);
        Assert.Contains("r.Done >= qty", economy);
        Assert.Contains("ExchangeBoughtEvent", engine);
        Assert.Contains("ExchangeSoldEvent", engine);
        Assert.Contains("sale:buyToPnj", Doc());
    }
}
