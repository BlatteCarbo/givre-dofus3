using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Positions des COMBATTANTS pendant un combat, decodees de EntitiesDispositionEvent (code kkr) :
/// { Dispositions #1 -&gt; EntityDisposition { EntityId #1 (int64 SIGNE : positif = joueur/allie, negatif = monstre) ;
/// Direction #2; CarryingCharacterId #3; CellId #4 } }. Tous les
/// numeros de champ viennent du MAPPING (fields.json / bundle serveur) via UseCatalog; l'autorite un bot tiers sert de
/// repli si le bundle ne resout pas encore le champ. Sert a placer les combattants sur la carte de combat.
/// </summary>
public static class MapFightReader
{
    // Branchement catalogue (fields.json en DEBUG, bundle serveur streame en prod) : les numeros de champ des
    // dispositions en sortent, plus aucun numero en dur.
    private static Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    /// <summary>Id du combattant TUE dans un GameActionFightEvent (code jwe) : la sous-action Death est au champ #23 sur
    /// le patch actuel (3.6.11.12 ; fields.json : death=23, #4 = slide desormais), forme { target_id #1 }. Retourne
    /// l'id, ou null si pas une mort. NB : ne PAS confondre avec #12 (points_variation PA/PM) ni #25 (life_points_lost).</summary>
    public static long? ReadKilledFighter(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : la mort est au sous-message #23 { cible #1 } (avant #4). Vérifié : chaque #14 = 103 avec
        // #23 = { -5 } est suivi de la disparition de -5 de l'ordre de jeu.
        // Porteur GameActionFightEvent.death, cible Death.target_id : noms du fields.json v5. ⚠ 04/10 : l'ancien nom Dofus 2
        // « GameActionFightDeathEvent » ne tenait que par _aliases de confirmed.json, absents du mapping streamé -> en prod
        // AUCUNE mort détectée (le bot visait des cadavres). PAS de repli sur le slide : chaque poussée serait lue comme une mort.
        var death = tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "death") && f.Message is not null)?.Message;
        var target = death?.FirstOrDefault(x => x.Field == F("Death", "target_id") && x.Wire == WireType.Varint);
        return target is null ? null : (long)target.Varint;
    }

    /// <summary>Deplacement FORCE (poussee/attirance) d'un GameActionFightEvent (jwe) : sous-action au champ #38, forme
    /// { cellA #1 ; target_id #2 (int64 signe) ; cellB #3 } (verifie : sort qui pousse -&gt; jwe #14=5 #38{#1 330 #2 -1 #3 290}).
    /// Renvoie (acteur, cellA, cellB) ; l'appelant deduit la DESTINATION = celle des deux qui n'est pas la case actuelle.
    /// Null si ce n'est pas un slide.</summary>
    public static (long Actor, int CellA, int CellB)? ReadSlide(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : poussée/attirance au sous-message #4 { depuis #1, vers #2, cible #4 } (avant #38 { #1, #2, #3 }).
        // Vérifié : #14 = 5 avec { 328, 270, -6 } puis les actions suivantes visent -6 en cellule 270.
        // Porteur GameActionFightEvent.slide, sous-message Slide { start_cell, end_cell, target_id } : noms du fields.json v5.
        // Vérifié capture 13:11 : { 284, 230, -1 } -> pousse -1 de 284 vers 230 ; et 04/10 01:22 : { 425, 396, -1 }.
        // ⚠ 04/10 : l'ancien nom « GameActionFightSlideEvent » ne tenait que par _aliases de confirmed.json, absents du mapping
        // streamé -> en prod AUCUNE poussée appliquée (mob resté à sa vieille case, coups dans le vide, bot bloqué).
        // Trame réelle (23/10 18:05:27, et Poutch 24/10 13:51) : { #1 target_id, #2 cell = départ, #3 end_cell } — PAS de
        // start_cell (#5). L'exiger faisait ignorer TOUTES les poussées/attirances : la cible restait à sa vieille case et le bot
        // tirait dans le vide (Crâ 32443 pousse d'une case : un seul coup touchait, tous les suivants visaient la case vide).
        if (tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "slide") && f.Message is not null)?.Message is { } s4
            && (s4.FirstOrDefault(x => x.Field == F("Slide", "start_cell") && x.Wire == WireType.Varint)
                ?? s4.FirstOrDefault(x => x.Field == F("Slide", "cell") && x.Wire == WireType.Varint)) is { } fromF
            && s4.FirstOrDefault(x => x.Field == F("Slide", "end_cell") && x.Wire == WireType.Varint) is { } toF
            && s4.FirstOrDefault(x => x.Field == F("Slide", "target_id") && x.Wire == WireType.Varint) is { } who4)
        {
            int fa = (int)fromF.Varint, fb = (int)toF.Varint;
            if (fa is >= 0 and < 560 && fb is >= 0 and < 560)
                return (who4.AsSignedVarint, fa, fb);
        }
        // (03/10 : l'ancienne lecture figée au champ #38 est retirée — sur le mapping actuel #38 = change_look, elle aurait
        // pris un changement d'apparence pour une poussée.)
        return null;
    }

    /// <summary>Autres déplacements FORCÉS d'un GameActionFightEvent, hors poussée : téléportation sur la carte (Bond, sorts
    /// qui téléportent une cible), lancer / poser d'un porté, prise en charge (le porté rejoint la case de son porteur), et
    /// invisible révélé. Chaque sous-message porte { target_id, cell } sous ses noms v5 (TeleportOnSameMap, ThrowCharacter,
    /// DropCharacter, CarryCharacter, InvisibleDetected). Renvoie (combattant, nouvelle case, sorte) ou null.</summary>
    public static (long Actor, int Cell, string Kind)? ReadRelocation(IReadOnlyList<ProtoField> tree)
    {
        foreach (var (carrier, sub) in new[]
                 {
                     ("teleport_on_same_map", "TeleportOnSameMap"), ("throw_character", "ThrowCharacter"),
                     ("drop_character", "DropCharacter"), ("carry_character", "CarryCharacter"),
                     ("invisible_detected", "InvisibleDetected"),
                 })
        {
            if (tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", carrier) && f.Message is not null)?.Message is not { } m)
                continue;
            var who = m.FirstOrDefault(x => x.Field == F(sub, "target_id") && x.Wire == WireType.Varint);
            var cell = m.FirstOrDefault(x => x.Field == F(sub, "cell") && x.Wire == WireType.Varint);
            ulong c = cell?.Varint ?? 0;   // proto3 : case 0 = champ OMIS, pas « inconnu »
            if (who is null || c >= 560) return null;
            return (who.AsSignedVarint, (int)c, sub);
        }
        return null;
    }

    /// <summary>Pose de marque au sol (glyphe, piège, rune...) : GameActionFightEvent.mark_cells { mark FightMark { mark_type,
    /// author_id, cells MarkedCell { gask = case } } }. Seule forme observée (01/11, Huppermage, sort 13686) : mark_type 4,
    /// une case. Identifiant confirmé dans le client 3.7.13.17 : lec champ 17 -> fyj.bijg -> FightMark.id.
    /// Lecture seule, aucune décision de combat n'en dépend.</summary>
    public static (long Author, int Type, IReadOnlyList<int> Cells, bool Active, int Id)? ReadMark(IReadOnlyList<ProtoField> tree)
    {
        if (tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "mark_cells") && f.Message is not null)?.Message is not { } mc
            || mc.FirstOrDefault(f => f.Field == F("MarkCells", "mark") && f.Message is not null)?.Message is not { } mark)
            return null;
        var author = mark.FirstOrDefault(x => x.Field == F("FightMark", "author_id") && x.Wire == WireType.Varint);
        var type = mark.FirstOrDefault(x => x.Field == F("FightMark", "mark_type") && x.Wire == WireType.Varint);
        var active = mark.FirstOrDefault(x => x.Field == F("FightMark", "active") && x.Wire == WireType.Varint);
        var id = mark.FirstOrDefault(x => x.Field == F("FightMark", "mark_id") && x.Wire == WireType.Varint);
        var cells = mark.Where(x => x.Field == F("FightMark", "cells") && x.Message is not null)
            .Select(x => x.Message!.FirstOrDefault(c => c.Field == F("MarkedCell", "gask") && c.Wire == WireType.Varint)?.Varint ?? 0)
            .Where(c => c < 560).Select(c => (int)c).ToList();
        if (author is null) return null;
        return (author.AsSignedVarint, (int)(type?.Varint ?? 0), cells, (active?.Varint ?? 0) != 0, unchecked((int)(id?.Varint ?? 0)));
    }

    /// <summary>Retrait de marque : GameActionFightEvent.unmark_cells { mark_id }.</summary>
    public static int? ReadUnmark(IReadOnlyList<ProtoField> tree)
        => tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "unmark_cells") && f.Message is not null)?.Message is { } u
            ? (int)(u.FirstOrDefault(x => x.Field == F("UnmarkCells", "mark_id") && x.Wire == WireType.Varint)?.Varint ?? 0) : null;

    /// <summary>Activation ou désactivation confirmée d'une marque ; false est omis en proto3.</summary>
    public static (int Id, bool Active)? ReadMarkActivation(IReadOnlyList<ProtoField> tree)
    {
        if (tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "activate_glyph_trap") && f.Message is not null)?.Message is not { } activation)
            return null;
        var id = activation.FirstOrDefault(f => f.Field == F("ActivateGlyphTrap", "mark_id") && f.Wire == WireType.Varint);
        var active = activation.FirstOrDefault(f => f.Field == F("ActivateGlyphTrap", "active") && f.Wire == WireType.Varint);
        return (unchecked((int)(id?.Varint ?? 0)), (active?.Varint ?? 0) != 0);
    }

    /// <summary>Échange de places (Transposition et apparentés) : sous-message ExchangePositions { caster_cell_id, target_id,
    /// target_cell_id } + GameActionFightEvent.source_id (le lanceur). Renvoie (lanceur, case lanceur, cible, case cible) telles
    /// que données par le serveur ; l'appelant décide si ce sont les nouvelles cases (cas attendu) ou les anciennes.</summary>
    public static (long Caster, int CasterCell, long Target, int TargetCell)? ReadExchangePositions(IReadOnlyList<ProtoField> tree)
    {
        if (tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "exchange_positions") && f.Message is not null)?.Message is not { } m)
            return null;
        var src = tree.FirstOrDefault(f => f.Field == F("GameActionFightEvent", "source_id") && f.Wire == WireType.Varint);
        var cc = m.FirstOrDefault(x => x.Field == F("ExchangePositions", "caster_cell_id") && x.Wire == WireType.Varint);
        var tid = m.FirstOrDefault(x => x.Field == F("ExchangePositions", "target_id") && x.Wire == WireType.Varint);
        var tc = m.FirstOrDefault(x => x.Field == F("ExchangePositions", "target_cell_id") && x.Wire == WireType.Varint);
        ulong ccv = cc?.Varint ?? 0, tcv = tc?.Varint ?? 0;   // proto3 : case 0 = champ OMIS
        if (src is null || tid is null || ccv >= 560 || tcv >= 560) return null;
        return (src.AsSignedVarint, (int)ccv, tid.AsSignedVarint, (int)tcv);
    }

    /// <summary>Décode EntitiesDispositionEvent (kkr) en (cellule, actor_id). Numéros de champ résolus par le mapping :
    /// EntitiesDispositionEvent.dispositions (le répété) + EntityDisposition.entity_id / .cell_id. Repli sur l'autorité
    /// Un bot tiers (dispositions=#1, entity_id=#1, cell_id=#4) si le catalogue ne résout pas. actor_id lu SIGNÉ.</summary>
    public static IReadOnlyList<(int Cell, long ActorId)> ReadDispositions(IReadOnlyList<ProtoField> tree)
    {
        int dispF = F("EntitiesDispositionEvent", "dispositions");
        int idNum = F("EntityDisposition", "entity_id");
        int cellNum = F("EntityDisposition", "cell_id");
        var list = new List<(int, long)>();
        foreach (var f in tree)
        {
            if (f.Field != dispF || f.Message is not { } m) continue;
            var idF = m.FirstOrDefault(x => x.Field == idNum && x.Wire == WireType.Varint);
            var cellF = m.FirstOrDefault(x => x.Field == cellNum && x.Wire == WireType.Varint);
            if (idF is null) continue;   // EntityId n'est JAMAIS 0 (joueur > 0, monstre < 0) -> requis
            // proto3 : cell_id == 0 (coin de la grille) est OMIS du fil. cellF null = case 0 VALIDE, PAS un combattant à
            // jeter (sinon le full-replace du handler le ferait disparaître du suivi -> tir/marche à travers lui).
            int cell = cellF is null ? 0 : (int)cellF.Varint;
            if (cell is < 0 or >= 560) continue;
            list.Add((cell, idF.AsSignedVarint));
        }
        return list;
    }
}
