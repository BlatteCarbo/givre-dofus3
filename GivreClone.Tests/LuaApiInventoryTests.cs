using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace GivreClone.Tests;

public sealed class LuaApiInventoryTests
{
    private static string Root
    {
        get
        {
            var dir = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "docs", "portal", "lua-f07-inventory.json")));
            return dir?.FullName ?? throw new InvalidOperationException("Inventaire Lua F07 introuvable");
        }
    }

    private static JsonDocument Inventory() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "portal", "lua-f07-inventory.json")));
    private static string Normalize(string key) => key.Contains(':') ? key.Replace("_", "").ToLowerInvariant() : key;

    [Fact]
    public void Inventory_covers_the_current_binding_sources()
    {
        using var inventory = Inventory();
        foreach (var source in inventory.RootElement.GetProperty("sourceHashes").EnumerateObject())
        {
            string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, source.Name))));
            Assert.True(hash == source.Value.GetString(), $"Régénérer python docs/portal/lua-f07-inventory.py : {source.Name}");
        }
        var apis = inventory.RootElement.GetProperty("apis").EnumerateArray().ToArray();
        Assert.True(apis.Length > 900);
        foreach (string module in new[] { "map", "combat", "character", "inventory", "bank", "developer", "fightSlave", "merchant", "mount", "quest", "fm", "eco", "soul" })
            Assert.Contains(apis, a => a.GetProperty("api").GetString()!.StartsWith(module + ":", StringComparison.Ordinal));
        Assert.Empty(inventory.RootElement.GetProperty("documentedButAbsent").EnumerateArray());
    }

    [Fact]
    public void Every_documented_unavailable_signature_declares_its_lua_error()
    {
        using var inventory = Inventory();
        var unavailable = inventory.RootElement.GetProperty("apis").EnumerateArray()
            .Where(a => a.GetProperty("unavailable").GetBoolean()).Select(a => Normalize(a.GetProperty("api").GetString()!)).ToHashSet();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "portal", "lua-api-doc.json")));
        int count = 0;
        foreach (var cat in doc.RootElement.GetProperty("categories").EnumerateArray())
        {
            if (!cat.TryGetProperty("methods", out var methods)) continue;
            foreach (var method in methods.EnumerateArray())
            foreach (var sig in method.GetProperty("sig").EnumerateArray())
            {
                string key = (sig.TryGetProperty("ns", out var ns) ? ns.GetString() : "") + sig.GetProperty("name").GetString();
                if (!unavailable.Contains(Normalize(key))) continue;
                Assert.Equal("unavailable", sig.GetProperty("implementationStatus").GetString());
                Assert.Contains("aucune action", sig.GetProperty("throws").GetString());
                count++;
            }
        }
        Assert.True(count > 50);
    }

    [Fact]
    public void Late_modules_cannot_restore_neutral_cost_or_switch_stubs()
    {
        string bind = File.ReadAllText(Path.Combine(Root, "GivreClone.App", "Services", "GivreLuaHost.Bindings.cs"));
        Assert.True(bind.IndexOf("BindFinalLuaContract(s)", StringComparison.Ordinal) > bind.IndexOf("BindLotM(s, ct)", StringComparison.Ordinal));
        string final = File.ReadAllText(Path.Combine(Root, "GivreClone.App", "Services", "GivreLuaHost.ApiContract.cs"));
        Assert.Contains("ch[\"getCost\" + name] = DynValue.NewCallback", final);
        Assert.Contains("ReadCharacteristicCost(raw as string, current)", final);
        Assert.Contains("ch[\"switch\"] = UnsupportedLua", final);
        Assert.Contains("\"switch\"", final);
    }

    [Fact]
    public void Blocking_exchange_and_npc_bindings_require_evidence_instead_of_just_sending()
    {
        string economy = File.ReadAllText(Path.Combine(Root, "GivreClone.App", "Services", "GivreLuaHost.SnowEconomy.cs"));
        Assert.Contains("sent && ack is not null && ack.Name != \"ExchangeErrorEvent\"", economy);
        Assert.Contains("return DynValue.NewBoolean(confirmed)", economy);
        string bindings = File.ReadAllText(Path.Combine(Root, "GivreClone.App", "Services", "GivreLuaHost.Bindings.cs"));
        Assert.Contains("return WaitUntil(() => _launch.NpcDialogSeq(_pid) != seq0", bindings);
        string host = File.ReadAllText(Path.Combine(Root, "GivreClone.App", "Services", "GivreLuaHost.cs"));
        Assert.Contains("c.Token == ct", host); // un stopped() périmé n'annule pas le prochain run
    }
}
