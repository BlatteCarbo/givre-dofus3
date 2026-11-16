using System;
using System.Globalization;

namespace GivreClone.App.Services.LotM;

public sealed record ItemCardInfo(string Name, int Level, string Type, string Set, int Weight);

public sealed partial class GameDb
{
    /// <summary>Métadonnées locales uniquement ; aucun jet théorique ne remplace les effets d'un UID.</summary>
    public ItemCardInfo? ItemCard(long gid)
    {
        lock (ExpeditionGate)
        {
            var row = Row("ItemData", gid.ToString(CultureInfo.InvariantCulture));
            if (row is null) return null;
            long N(string key, long fallback = 0) => row.TryGetValue(key, out var value) && value is not null ? L(value) : fallback;
            string type = N("typeId") > 0 ? NameOf("ItemTypeData", N("typeId")) : "";
            string set = N("itemSetId") > 0 ? NameOf("ItemSetData", N("itemSetId")) : "";
            return new ItemCardInfo(Text(N("nameId")) ?? "", (int)N("level", -1), type, set, (int)N("realWeight", N("weight", -1)));
        }
    }
}
