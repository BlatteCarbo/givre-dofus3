using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>Bilan d'un combat pour MON perso, lu du FightEndEvent. Found=false : mon entrée absente de results (Outcome=-1).
/// Outcome : 2 = victoire, 1 = nul, 0 = défaite (proto3 omet le 0). Xp/Kamas null = non lus (champ absent / non résolu).</summary>
public sealed record FightEndSummary(bool Found, int Outcome, long ServerDuration, long? Xp, long? Kamas, IReadOnlyList<(int Gid, int Qty)> Loot);

/// <summary>
/// Lot C2 : décode le bilan d'un combat (FightEndEvent) pour le script (combat:lastFightResult / onFightEnd). TOUS les
/// numéros de champ, imbriqués compris, sont résolus PAR NOM via le mapping (un patch renumérote l'intérieur des messages).
/// Identité : mon entrée = celle dont FighterListEntry.fighter_id == selfId (même logique que DecodeFightOutcome).
/// Récompenses (schéma v5) : FightResultListEntry.rewards = UN FightReward { rewards = FightLoot*, kamas } ;
/// FightLoot { objects = Object* } ; Object { object_gid, quantity }. (Jusqu'au 04/10 le code cherchait les objets
/// directement dans FightReward et les kamas sur FightLoot, qui n'en a pas : butin toujours vide, kamas toujours nuls.)
/// </summary>
public static class FightResultReader
{
    private static Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string m, string f) => _fieldNumber?.Invoke(m, f) ?? FieldMiss.Of(m, f);   // mapping streamé seul

    private static readonly FightEndSummary NotFound = new(false, -1, 0, null, null, Array.Empty<(int, int)>());

    public static FightEndSummary Read(IReadOnlyList<ProtoField> payload, long selfId)
    {
        long serverDuration = VarintOf(payload, F("FightEndEvent", "duration")) ?? 0;
        if (selfId == 0) return NotFound with { ServerDuration = serverDuration };
        int resultsF = F("FightEndEvent", "results");
        int fighterF = F("FightResultListEntry", "fighter_list_entry");
        int rewardsF = F("FightResultListEntry", "rewards");
        int outcomeF = F("FightResultListEntry", "outcome");
        int fidF = F("FighterListEntry", "fighter_id");
        int playerF = F("FighterListEntry", "player_list_entry");
        int additionalF = F("PlayerListEntry", "additional");
        int expDataF = F("FightResultAdditionalData", "experience_data");
        int xpDeltaF = F("ExperienceData", "experience_fight_delta");
        int lootsF = F("FightReward", "rewards");
        int kamasF = F("FightReward", "kamas");
        int objectsF = F("FightLoot", "objects");
        int gidF = F("Object", "object_gid");
        int qtyF = F("Object", "quantity");

        foreach (var f in payload)
        {
            if (f.Field != resultsF || f.Message is not { } entry) continue;
            List<ProtoField>? fighter = null;
            foreach (var g in entry) if (g.Field == fighterF && g.Message is { } gm) { fighter = gm; break; }
            if (fighter is null || (VarintOf(fighter, fidF) ?? 0) != selfId) continue;

            int outcome = (int)(VarintOf(entry, outcomeF) ?? 0);

            // XP : fighter_list_entry -> player_list_entry -> additional* -> experience_data -> experience_fight_delta.
            long? xp = null;
            foreach (var pl in fighter)
            {
                if (xp is not null) break;
                if (pl.Field != playerF || pl.Message is not { } plm) continue;
                foreach (var ad in plm)
                {
                    if (ad.Field != additionalF || ad.Message is not { } adm) continue;
                    foreach (var ed in adm)
                        if (ed.Field == expDataF && ed.Message is { } edm) { xp = VarintOf(edm, xpDeltaF) ?? 0; break; }
                    if (xp is not null) break;
                }
            }

            // Butin : rewards (FightReward) -> rewards* (FightLoot) -> objects* {object_gid, quantity}. Quantité omise/0 = 1 ;
            // cumul par gid ; gid<=0 ignoré. Kamas = FightReward.kamas (proto3 omet 0 -> 0 si le champ est connu ; null s'il
            // est absent du mapping).
            var loot = new List<(int Gid, int Qty)>();
            var index = new Dictionary<int, int>();
            long? kamas = kamasF > 0 ? 0 : null;
            foreach (var rw in entry)
            {
                if (rw.Field != rewardsF || rw.Message is not { } rwm) continue;
                if (kamasF > 0 && VarintOf(rwm, kamasF) is long k) kamas += k;
                foreach (var lt in rwm)
                {
                    if (lt.Field != lootsF || lt.Message is not { } ltm) continue;
                    foreach (var ob in ltm)
                    {
                        if (ob.Field != objectsF || ob.Message is not { } obm) continue;
                        int gid = (int)(VarintOf(obm, gidF) ?? 0);
                        if (gid <= 0) continue;
                        int qty = (int)(VarintOf(obm, qtyF) ?? 0);
                        if (qty <= 0) qty = 1;
                        if (index.TryGetValue(gid, out int i)) loot[i] = (gid, loot[i].Qty + qty);
                        else { index[gid] = loot.Count; loot.Add((gid, qty)); }
                    }
                }
            }
            return new FightEndSummary(true, outcome, serverDuration, xp, kamas, loot);
        }
        return NotFound with { ServerDuration = serverDuration };
    }

    // 1er champ VARINT de ce numéro (complément à deux pour les int64 signés), null si absent.
    private static long? VarintOf(IReadOnlyList<ProtoField> msg, int field)
    {
        foreach (var x in msg) if (x.Field == field && x.Wire == WireType.Varint) return unchecked((long)x.Varint);
        return null;
    }
}
