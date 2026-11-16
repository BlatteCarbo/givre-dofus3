using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services.LotM;

public sealed partial class GameDb
{
    /// <summary>Actions que le jeu déclare pour un PNJ (NpcData.actions -> NpcActionData : id d'action + libellé). Liste vide ou null = aucune
    /// action connue : l'interface n'en propose alors aucune (jamais de « Parler » par défaut).</summary>
    public IReadOnlyList<(int Id, string Label)>? NpcActions(int npcId)
    {
        try
        {
            if (npcId <= 0 || !IsKnownTable("NpcData") || !IsKnownTable("NpcActionData")) return null;
            if (Scalar("select actions from NpcData where id = $0", npcId) is not string json) return null;
            using var doc = JsonDocument.Parse(json);
            var list = new List<(int, string)>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetInt32(out int id)) continue;
                var row = Query("select nameId from NpcActionData where id = $0", id);
                string label = row is { } r && r.FirstOrDefault() is { } first ? (Text(L(first[0])) ?? "") : "";
                list.Add((id, label));
            }
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }

    /// <summary>Texte d'une QUESTION de PNJ (NpcMessageData.messageId traduit), « #1 »… remplacés par les paramètres reçus ; null si inconnu.</summary>
    public string? NpcQuestionText(int messageId, IReadOnlyList<string>? args = null)
    {
        try
        {
            if (messageId <= 0 || !IsKnownTable("NpcMessageData")) return null;
            if (Text(L(Scalar("select messageId from NpcMessageData where id = $0", messageId))) is not { } t) return null;
            if (args is not null) for (int i = args.Count; i >= 1; i--) t = t.Replace("#" + i, args[i - 1]);
            return t;
        }
        catch { return null; }
    }

    /// <summary>Nom d'un TYPE d'élément interactif (InteractiveData.nameId, ex. 97 -> « Plan de Travail ») ; null si inconnu.</summary>
    public string? InteractiveTypeName(int typeId) => typeId <= 0 || !IsKnownTable("InteractiveData") ? null : Text(L(Scalar("select nameId from InteractiveData where id = $0", typeId)));

    private readonly Dictionary<int, IReadOnlyList<RecipeInfo>> _skillRecipes = new();
    private Dictionary<int, int>? _replyTextAcrossNpcs;

    /// <summary>Nom du PNJ (NpcData.nameId traduit) ; null si absent.</summary>
    public string? NpcName(int npcId) => npcId <= 0 || !IsKnownTable("NpcData") ? null : Text(L(Scalar("select nameId from NpcData where id = $0", npcId)));

    /// <summary>Texte d'une RÉPONSE de dialogue : paires [replyId, textId] de NpcData.dialogReplies du PNJ ; à défaut, correspondance valable pour
    /// TOUS les PNJ (un replyId qui pointerait vers deux textes différents reste non résolu). null = texte inconnu (jamais inventé).</summary>
    public string? NpcReplyText(int npcId, int replyId)
    {
        try
        {
            if (replyId <= 0 || !IsKnownTable("NpcData")) return null;
            int textId = 0;
            if (npcId > 0 && Scalar("select dialogReplies from NpcData where id = $0", npcId) is string json)
                foreach (var (r, t) in ReplyPairs(json)) if (r == replyId) { textId = t; break; }
            if (textId <= 0)
            {
                if (_replyTextAcrossNpcs is null)
                {
                    var index = new Dictionary<int, int>();
                    foreach (var row in Query("select dialogReplies from NpcData where dialogReplies is not null and dialogReplies != '[]'"))
                        if (row[0] is string j)
                            foreach (var (r, t) in ReplyPairs(j))
                                index[r] = index.TryGetValue(r, out int old) && old != t ? -1 : t;
                    _replyTextAcrossNpcs = index;
                }
                if (_replyTextAcrossNpcs.TryGetValue(replyId, out int found) && found > 0) textId = found;
            }
            return textId > 0 ? Text(textId) : null;
        }
        catch { return null; }
    }

    private static IEnumerable<(int Reply, int Text)> ReplyPairs(string json)
    {
        var list = new List<(int, int)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("values", out var v) && v.TryGetProperty("Array", out var a) && a.GetArrayLength() == 2
                    && a[0].TryGetInt32(out int r) && a[1].TryGetInt32(out int t) && r > 0 && t > 0) list.Add((r, t));
        }
        catch { }
        return list;
    }

    /// <summary>Recettes que l'on peut réaliser avec une compétence d'atelier (RecipeData.skillId) ; vide si la compétence n'est pas un atelier de craft.</summary>
    public IReadOnlyList<RecipeInfo> RecipesOfSkill(int skillId)
    {
        try
        {
            if (skillId <= 0 || !IsKnownTable("RecipeData")) return Array.Empty<RecipeInfo>();
            if (_skillRecipes.TryGetValue(skillId, out var hit)) return hit;
            var list = Query("select id, jobId, quantities, resultId, resultLevel from RecipeData where skillId = $0 order by resultLevel desc, resultId", skillId).Select(ToRecipe).ToList();
            return _skillRecipes[skillId] = list;   // données du jeu statiques : un atelier n'est lu qu'une fois
        }
        catch { return Array.Empty<RecipeInfo>(); }
    }
}
