using GivreClone.Engine;
using System;
using System.Collections.Generic;
using System.Threading;
using MoonSharp.Interpreter;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

public sealed partial class GivreLuaHost
{
    private static DynValue UnsupportedLua(string api, string reason = "API non implémentée dans GivreClone")
        => LuaSnowCompatRuntime.Unsupported(api, reason);

    private DynValue CallLua(Script script, DynValue fn, CancellationToken ct, params DynValue[] args)
        => LuaSnowCompatRuntime.Call(script, fn, ct, ThrowIfHookTurnGone, args);
    private void BindFinalLuaContract(Script s)
    {
        // Lot M réinstalle ses anciennes valeurs neutres : le contrat réel passe en dernier.
        var ch = GetOrCreateTable(s, "character");
        foreach (var (name, id, field) in new[] {
            ("Vitality", 11, "statsPointsForVitality"), ("Wisdom", 12, "statsPointsForWisdom"),
            ("Strenght", 10, "statsPointsForStrength"), ("Strength", 10, "statsPointsForStrength"),
            ("Intelligence", 15, "statsPointsForIntelligence"), ("Chance", 13, "statsPointsForChance"),
            ("Agility", 14, "statsPointsForAgility") })
        {
            ch["getCost" + name] = DynValue.NewCallback((_, a) =>
            {
                int current = _launch.CharacteristicBase(_pid, id);
                if (current < 0 || _launch.SelfCharacterInfo(_pid)?.BreedIndex is not int breed
                    || _launch.GameData?.Row("BreedData", ClassIconProvider.BreedId(breed).ToString(System.Globalization.CultureInfo.InvariantCulture)) is not { } row
                    || !row.TryGetValue(field, out var raw)) return DynValue.Nil;
                var cost = ReadCharacteristicCost(raw as string, current);
                return cost is { } value ? DynValue.NewNumber(value) : DynValue.Nil;
            });
        }
        ch["switch"] = UnsupportedLua("character:switch");
        var quest = GetOrCreateTable(s, "quest");
        quest["plan"] = UnsupportedLua("quest:plan", "Données d’objectif par carte absentes (QuestObjectiveData)");
        var npc = GetOrCreateTable(s, "npc");
        var sell = npc.Get("sell");
        if (sell.Type == DataType.ClrFunction)
            npc["sell"] = DynValue.NewCallback((ctx, a) =>
            {
                if (At(a, npc, 0).CastToNumber() is double gid && gid > 0 && gid <= int.MaxValue
                    && !CanSellNpcGid((int)gid))
                    return DynValue.NewTuple(DynValue.False, DynValue.NewNumber(0), DynValue.NewString("bound"));
                return sell.Callback.Invoke(ctx, a.GetArray());
            });
    }
    internal static CombatSpeed ParseLuaFightSpeed(DynValue value)
    {
        var number = value.CastToNumber();
        if (number is not { } speed || !double.IsFinite(speed) || speed != System.Math.Truncate(speed) || speed < 0 || speed > 3)
            throw new ScriptRuntimeException("fightBasic:setFightSpeed : vitesse attendue 0=Instant, 1=Fast, 2=Slow, 3=Turbo");
        return (CombatSpeed)(int)speed;
    }

    // BreedData : [seuil de base, points dépensés, points gagnés (optionnel)].
    // Aucune formule de classe de repli : une donnée absente/malformée reste inconnue.
    internal static double? ReadCharacteristicCost(string? json, int current)
    {
        if (current < 0 || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            int previous = -1;
            double? result = null;
            foreach (var tier in doc.RootElement.EnumerateArray())
            {
                if (tier.ValueKind != System.Text.Json.JsonValueKind.Array || tier.GetArrayLength() is < 2 or > 3
                    || !tier[0].TryGetInt32(out int threshold) || threshold < 0 || threshold <= previous
                    || !tier[1].TryGetInt32(out int cost) || cost <= 0) return null;
                int gain = 1;
                if (tier.GetArrayLength() == 3 && (!tier[2].TryGetInt32(out gain) || gain <= 0)) return null;
                previous = threshold;
                if (current >= threshold) result = (double)cost / gain;
            }
            return result;
        }
        catch (System.Text.Json.JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // Seam pour tests unitaires (LuaHostBindingTests) : initialise un Script et y lie tous les verbes
    internal Script TestInitScript(CancellationToken ct = default)
    {
        var script = new Script(CoreModules.Preset_SoftSandbox);
        _script = script;
        Bind(script, ct);
        return script;
    }

    // Seam pour tests unitaires : invoque SelectMonster directement
    internal MapActor? TestSelectMonster(BotWorldState st, int src, int preferredCell, HashSet<long>? exclude = null)
        => SelectMonster(st, src, preferredCell, exclude);
}
