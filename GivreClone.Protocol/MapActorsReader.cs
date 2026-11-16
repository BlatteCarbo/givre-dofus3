using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>Nature d'un acteur pose sur la carte : notre perso, un autre joueur, un groupe de monstres, un PNJ, un indice
/// PNJ de chasse au trésor (« phorreur », ajouté en fin d'énumération : les valeurs existantes ne bougent pas).</summary>
public enum MapActorKind { Player, Monster, Npc, Unknown, TreasureHint }

/// <summary>Un type de monstre dans un groupe : son genericId (pour l'icone a venir), son nom, et son nombre.</summary>
public sealed record MonsterEntry(int GenericId, string Name, int Count);

/// <summary>Un monstre INDIVIDUEL d'un groupe : genericId + niveau (MonsterInGroupInformation.level ; 0 = inconnu/omis).</summary>
public sealed record MonsterMember(int GenericId, int Level);

/// <summary>
/// Un acteur pose sur la carte : cellule (0-559), orientation (0-7), id, libelle affichable, nature, et - pour un
/// groupe de monstres - le detail des monstres (par type) pour un affichage vertical + icones.
/// </summary>
public sealed record MapActor(
    int CellId, int Direction, long ActorId, string? Name, MapActorKind Kind, IReadOnlyList<MonsterEntry>? Monsters = null,
    IReadOnlyList<MonsterMember>? Members = null, int NpcId = 0)
{
    // NpcId : identifiant MODÈLE du PNJ (NpcStaticInformation.npc_id) ou de l'indice de chasse (treasure_hunt_npc_id) ; 0 sinon.
    // ActorId reste l'identifiant CONTEXTUEL (négatif) utilisé par les requêtes de dialogue.
    /// <summary>Somme des niveaux des monstres du groupe (0 si inconnu / pas un groupe).</summary>
    public int GroupLevel => Members?.Sum(m => m.Level) ?? 0;
}

/// <summary>
/// Extrait et CLASSE les acteurs d'un MapComplementaryInformationEvent decode. Structure observee (client actuel,
/// confirmee sur un dump reel Persodeux) :
///   actors = repeated (champ fourni par fields.json) ; par acteur (ljx) :
///     #1 = disposition { #1 cellId, #2 direction } ; #3 = int64 actorId ; #2 = ljv(ActorInformation)
///     ljv.#1 = lja(RolePlayActor) = ONEOF par NUMERO : #4 groupe de monstres, #5 joueur (NamedActor), #7 PNJ.
///   Groupe de monstres : lja.#4(lit).#2(lgx) ; leader = lgx.#2(led), sbires = lgx.#1 (repeated led) ;
///     led.#1 = genericId (=monsterId). Nom des monstres via le delegue monsterName (table client).
///   PNJ : lja.#7(liy).#4 = npcId (nom via le delegue npcName). Joueur : lja.#5(NamedActor).#1 = pseudo.
/// Numeros propres au patch (tags obfusques) ; imbrication navigee par position. cellId hors [0,559] -> ignore.
/// </summary>
public static class MapActorsReader
{
    private const int CellCount = 560;

    private static System.Func<string,string,int?>? _fieldNumber;
    public static void UseCatalog(System.Func<string,string,int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    public static IReadOnlyList<MapActor> Extract(
        IReadOnlyList<ProtoField> tree, int actorsField,
        Func<int, string?>? monsterName = null, Func<int, string?>? npcName = null)
    {
        var list = new List<MapActor>();
        foreach (var f in tree)
        {
            if (f.Field != actorsField || f.Message is not { } actor) continue;

            // 3.6.11.12 : { information #1, actorId #2, disposition #3 { direction #2, cellId #4 } }.
            // Avant : { disposition #1 { cell #1, dir #2 }, information #2, actorId #3 }. Les deux formes se
            // distinguent sans ambiguite : dans la NOUVELLE, actor.#1 est un message, jamais un varint de cellule.
            int cell, dir;
            long actorId;
            IReadOnlyList<ProtoField>? ljv;
            if (Msg(actor, F("ActorPositionInformation", "disposition")) is { } d3 && Var(d3, F("EntityDisposition", "cell_id")) is ulong c4)
            {
                cell = (int)c4;
                dir = (int)(Var(d3, F("EntityDisposition", "direction")) ?? 0);
                actorId = unchecked((long)(Var(actor, F("ActorPositionInformation", "actor_id")) ?? 0));
                ljv = Msg(actor, F("ActorPositionInformation", "actor_information"));
            }
            else continue;
            if (cell is < 0 or >= CellCount) continue;

            var lja = ljv is null ? null : Msg(ljv, F("ActorInformation", "role_play_actor"));
            var (name, kind, monsters, members) = Classify(lja, monsterName, npcName);
            list.Add(new MapActor(cell, dir, actorId, name, kind, monsters, members, NpcIdOf(lja, kind)));
        }
        return list;
    }

    private static (string?, MapActorKind, IReadOnlyList<MonsterEntry>?, IReadOnlyList<MonsterMember>?) Classify(
        IReadOnlyList<ProtoField>? lja, Func<int, string?>? monsterName, Func<int, string?>? npcName)
    {
        if (lja is null) return (null, MapActorKind.Unknown, null, null);

        // 3.6.11.12 : le ONEOF a tourne -> #2 joueur, #3 PNJ, #4 groupe de monstres (seul #4 est inchange).
        // #2 = joueur (NamedActor) : le pseudo est une chaine dans le sous-arbre. (Ancien repli #5 RETIRÉ : en v4
        // RolePlayActor.#5 = prism_actor -> lire #5 comme joueur classait un PRISME en faux joueur. Un prisme sans
        // #2 tombe désormais en Unknown, pas en Player.)
        if (Msg(lja, F("RolePlayActor", "named_actor")) is { } named2) return (FindName(named2), MapActorKind.Player, null, null);

        // #4 = groupe de monstres : lit.#2(lgx) ; MENEUR lgx.#1, sbires lgx.#3 (repete), genericId = entree.#2
        // (avant : meneur #2, sbires #1, genericId #1). Verifie : groupe 1+6 -> 7 combattants -1..-7 en combat.
        if (Msg(lja, F("RolePlayActor", "monster_group_actor")) is { } lit && Msg(lit, F("MonsterGroupActor", "identification")) is { } lgx)
        {
            var gids = new List<int>();
            var members = new List<MonsterMember>();   // 1 ligne par monstre : genericId + niveau (MonsterInGroupInformation.level, lu PAR NOM)
            int levelF = F("MonsterInGroupInformation", "level");
            if (Msg(lgx, F("MonsterGroupStaticInformation", "main_creature")) is { } lead2 && Var(lead2, F("MonsterInGroupInformation", "gid")) is ulong lg2)
            { gids.Add((int)lg2); members.Add(new MonsterMember((int)lg2, (int)(Var(lead2, levelF) ?? 0))); }
            foreach (var x in lgx)
                if (x.Field == F("MonsterGroupStaticInformation", "underlings") && x.Message is { } u2 && Var(u2, F("MonsterInGroupInformation", "gid")) is ulong ug2)
                { gids.Add((int)ug2); members.Add(new MonsterMember((int)ug2, (int)(Var(u2, levelF) ?? 0))); }
            var (title, entries) = MonsterBreakdown(gids, monsterName);
            return (title, MapActorKind.Monster, entries, members);
        }

        // #3 = PNJ (avant #7) : npcId = liy.#1 (avant #4/#5). Name = nom RÉSOLU seul (comparable tel quel par un script) ;
        // « PNJ <id> » seulement si le nom est inconnu. L'id de modèle vit dans MapActor.NpcId (plus dans le libellé).
        if (Msg(lja, F("RolePlayActor", "npc_actor")) is { } liy3 && Var(liy3, F("NpcStaticInformation", "npc_id")) is ulong np3)
        {
            var nm3 = npcName?.Invoke((int)np3);
            return (string.IsNullOrWhiteSpace(nm3) ? $"PNJ {np3}" : nm3, MapActorKind.Npc, null, null);
        }
        // (Ancien repli #7 RETIRÉ : en v4 RolePlayActor.#7 = ganv -> lire #7 comme PNJ classait un ganv en faux PNJ.
        // Un acteur ganv sans #3 tombe désormais en Unknown, pas en Npc.)
        // #6 = indice PNJ de chasse au trésor (« phorreur ») : id du modèle, entier direct ou 1er entier d'un sous-message.
        // Même règle que le PNJ : nom résolu seul, « Indice <id> » si inconnu (id de modèle dans MapActor.NpcId).
        if (TreasureNpcId(lja) is int tn && tn > 0)
        {
            var nmT = npcName?.Invoke(tn);
            return (string.IsNullOrWhiteSpace(nmT) ? $"Indice {tn}" : nmT, MapActorKind.TreasureHint, null, null);
        }
        return (FindName(lja), MapActorKind.Unknown, null, null);
    }

    private static int? TreasureNpcId(IReadOnlyList<ProtoField> lja)
    {
        int f = F("RolePlayActor", "treasure_hunt_npc_id");
        if (Var(lja, f) is ulong v) return (int)v;
        if (Msg(lja, f) is { } m) foreach (var x in m) if (x.Wire == WireType.Varint) return (int)x.Varint;
        return null;
    }

    // Identifiant MODÈLE d'un PNJ / indice de chasse (0 pour les autres acteurs).
    private static int NpcIdOf(IReadOnlyList<ProtoField>? lja, MapActorKind kind)
    {
        if (lja is null) return 0;
        if (kind == MapActorKind.Npc && Msg(lja, F("RolePlayActor", "npc_actor")) is { } liy && Var(liy, F("NpcStaticInformation", "npc_id")) is ulong np)
            return (int)np;
        if (kind == MapActorKind.TreasureHint) return TreasureNpcId(lja) ?? 0;
        return 0;
    }

    // Detail du groupe : titre "N monstres" + une entree par type (genericId + nom + nombre), ordre preserve.
    private static (string, List<MonsterEntry>) MonsterBreakdown(List<int> gids, Func<int, string?>? monsterName)
    {
        var order = new List<string>();
        var acc = new Dictionary<string, (int Gid, int Count)>();
        foreach (var g in gids)
        {
            var name = monsterName?.Invoke(g) ?? $"Monstre #{g}";
            if (!acc.ContainsKey(name)) { acc[name] = (g, 0); order.Add(name); }
            acc[name] = (acc[name].Gid, acc[name].Count + 1);
        }
        var entries = order.Select(n => new MonsterEntry(acc[n].Gid, n, acc[n].Count)).ToList();
        string title = gids.Count == 0 ? "Groupe de monstres" : $"{gids.Count} monstre{(gids.Count > 1 ? "s" : "")}";
        return (title, entries);
    }

    private static IReadOnlyList<ProtoField>? Msg(IReadOnlyList<ProtoField> node, int field)
        => node.FirstOrDefault(x => x.Field == field && x.Message is not null)?.Message;

    private static ulong? Var(IReadOnlyList<ProtoField> node, int field)
        => node.FirstOrDefault(x => x.Field == field && x.Wire == WireType.Varint)?.Varint;

    private static string? FindName(IReadOnlyList<ProtoField> node)
    {
        foreach (var f in node)
        {
            if (f.Wire == WireType.Len && f.String is { } s && LooksLikeName(s)) return s;
            if (f.Message is { } sub) { var r = FindName(sub); if (r is not null) return r; }
        }
        return null;
    }

    private static bool LooksLikeName(string s)
    {
        if (s.Length is < 2 or > 40) return false;
        bool hasLetter = false;
        foreach (var c in s)
        {
            if (char.IsLetter(c)) hasLetter = true;
            else if (!(char.IsDigit(c) || c is '-' or '_' or ' ' or '\'')) return false;
        }
        return hasLetter;
    }
}
