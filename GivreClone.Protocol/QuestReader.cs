using System;
using System.Collections.Generic;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Lot I — décodage des événements de QUÊTES (lecture pure, testable). CHAQUE niveau (QuestsEvent, QuestFinished, QuestActive,
/// Details, QuestObjective, Completion) est lu PAR NOM via <see cref="UseCatalog"/> : un patch renumérote aussi l'INTÉRIEUR
/// des messages. Le repli est le numéro de fields.json au moment de l'écriture (⚠ différent du .proto de référence).
///
/// proto3 : un champ à 0 / false est OMIS -> objective_reached absent = false, completion absente = 0/0.
/// </summary>
public static class QuestReader
{
    public readonly record struct Objective(int Id, bool Reached, int Current, int Max);
    public sealed record Quest(int Id, int StepId, IReadOnlyList<Objective> Objectives, bool HasDetails);
    public sealed record Snapshot(long PlayerId, IReadOnlyList<int> Finished, IReadOnlyList<int> LegacyExtra, IReadOnlyList<Quest> Active);

    private static Func<string, string, int?>? _fieldNumber;
    public static void UseCatalog(Func<string, string, int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    /// <summary>QuestsEvent (ief) : quêtes terminées + quêtes en cours (avec étape/objectifs quand le serveur les détaille).</summary>
    public static Snapshot ReadQuests(IReadOnlyList<ProtoField> tree)
    {
        long player = PayloadReader.Int(tree, F("QuestsEvent", "player_id"), 0);
        int fFinished = F("QuestsEvent", "finished_quests");
        int fActive = F("QuestsEvent", "active_quests");
        int fQid = F("QuestFinished", "quest_id");
        // parité legacy CriterionMet uniquement ; #2 = fpci non résolu (probablement PAS un id de quête — cf. DEFERRED D6).
        int fLegacy = F("QuestFinished", "fpci");
        var finished = new List<int>();
        var seen = new HashSet<int>();
        var legacyRaw = new List<int>();
        var active = new List<Quest>();
        foreach (var f in tree)
        {
            if (f.Field == fFinished && Sub(f) is { } qf)
            {
                long a = PayloadReader.Int(qf, fQid, 0);
                if (a > 0 && seen.Add((int)a)) finished.Add((int)a);
                long b = fLegacy != fQid ? PayloadReader.Int(qf, fLegacy, 0) : 0;
                if (b > 0) legacyRaw.Add((int)b);
            }
            else if (f.Field == fActive && Sub(f) is { } qa && ReadActive(qa) is { } q)
                active.Add(q);
        }
        var legacy = new List<int>();
        foreach (var b in legacyRaw) if (!seen.Contains(b) && !legacy.Contains(b)) legacy.Add(b);
        return new Snapshot(player, finished, legacy, active);
    }

    /// <summary>Un QuestActive (sous-message) -> quête (étape + objectifs si « details » présent). null si quest_id absent.</summary>
    public static Quest? ReadActive(IReadOnlyList<ProtoField> questActive)
    {
        int id = (int)PayloadReader.Int(questActive, F("QuestActive", "quest_id"), 0);
        if (id <= 0) return null;
        var df = PayloadReader.Field(questActive, F("QuestActive", "details"));
        if (df is null || Sub(df) is not { } details) return new Quest(id, 0, Array.Empty<Objective>(), false);
        int step = (int)PayloadReader.Int(details, F("Details", "step_id"), 0);
        int fObj = F("Details", "objectives");
        int fObjId = F("QuestObjective", "objective_id");
        int fReached = F("QuestObjective", "objective_reached");
        int fCompletion = F("QuestObjective", "completion");
        int fCur = F("Completion", "current_completion");
        int fMax = F("Completion", "max_completion");
        var objs = new List<Objective>();
        foreach (var f in details)
        {
            if (f.Field != fObj || Sub(f) is not { } o) continue;
            int oid = (int)PayloadReader.Int(o, fObjId, 0);
            if (oid <= 0) continue;
            bool reached = PayloadReader.Bool(o, fReached);
            int cur = 0, max = 0;
            var cf = PayloadReader.Field(o, fCompletion);
            if (cf is not null && Sub(cf) is { } c)
            {
                cur = (int)PayloadReader.Int(c, fCur, 0);
                max = (int)PayloadReader.Int(c, fMax, 0);
            }
            objs.Add(new Objective(oid, reached, cur, max));
        }
        return new Quest(id, step, objs, true);
    }

    /// <summary>QuestStepInformationEvent (icv) : (player_id, information = QuestActive).</summary>
    public static (long PlayerId, Quest? Info) ReadStepInformation(IReadOnlyList<ProtoField> tree)
    {
        long player = PayloadReader.Int(tree, F("QuestStepInformationEvent", "player_id"), 0);
        var inf = PayloadReader.Field(tree, F("QuestStepInformationEvent", "information"));
        return (player, inf is not null && Sub(inf) is { } qa ? ReadActive(qa) : null);
    }

    /// <summary>QuestsFollowedEvent (ieg) : quêtes suivies (QuestActive répétés).</summary>
    public static IReadOnlyList<Quest> ReadFollowed(IReadOnlyList<ProtoField> tree)
    {
        int fq = F("QuestsFollowedEvent", "quests");
        var list = new List<Quest>();
        foreach (var f in tree)
            if (f.Field == fq && Sub(f) is { } qa && ReadActive(qa) is { } q) list.Add(q);
        return list;
    }

    /// <summary>QuestStepStartedEvent / QuestStepValidatedEvent (ieh / idk) : (quest_id, step_id).</summary>
    public static (int QuestId, int StepId) ReadStepEvent(IReadOnlyList<ProtoField> tree, string msg)
        => ((int)PayloadReader.Int(tree, F(msg, "quest_id"), 0), (int)PayloadReader.Int(tree, F(msg, "step_id"), 0));

    /// <summary>QuestObjectiveValidatedEvent (idz) : (quest_id, objective_id).</summary>
    public static (int QuestId, int ObjectiveId) ReadObjectiveValidated(IReadOnlyList<ProtoField> tree)
        => ((int)PayloadReader.Int(tree, F("QuestObjectiveValidatedEvent", "quest_id"), 0),
            (int)PayloadReader.Int(tree, F("QuestObjectiveValidatedEvent", "objective_id"), 0));

    /// <summary>quest_id de QuestStartedEvent (idi, repli #2) ou QuestValidatedEvent (iea, repli #1). 0 si absent.</summary>
    public static int ReadQuestId(IReadOnlyList<ProtoField> tree, string msg)
        => (int)PayloadReader.Int(tree, F(msg, "quest_id"), 0);

    // Sous-message : interprétation du décodeur brut, sinon re-parse des octets (un Len peut avoir été classé en texte).
    private static IReadOnlyList<ProtoField>? Sub(ProtoField f)
    {
        if (f.Wire != WireType.Len) return null;
        if (f.Message is not null) return f.Message;
        if (f.Bytes is { Length: > 0 } b) { try { return RawProtobuf.Parse(b); } catch { return null; } }
        return null;
    }
}
