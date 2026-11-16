using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

public static class HdvProfileKey
{
    // Le nom seul n'est PAS une identité globale : il peut exister sur plusieurs serveurs.
    public static string For(string? character, int serverId) =>
        string.IsNullOrWhiteSpace(character) ? "?" :
        serverId > 0 ? $"server:{serverId}:{character}" : character!;
}

// Uniquement les intentions utilisateur. Jamais les prix observés, stock temporaire,
// icônes ou accusés de réception de transactions.
public sealed class HdvBuyRule
{
    public long Gid { get; set; }
    public int Needed { get; set; }
    public List<HdvBuyLotRule> Lots { get; set; } = new();
    public HdvBuyRule Clone() => new()
    {
        Gid = Gid, Needed = Needed,
        Lots = (Lots ?? new()).Select(l => l.Clone()).ToList()
    };
}

public sealed class HdvBuyLotRule
{
    public int Qty { get; set; }
    public bool Allowed { get; set; }
    public long Max { get; set; }
    public HdvBuyLotRule Clone() => new() { Qty = Qty, Allowed = Allowed, Max = Max };
}

public sealed class HdvRelistRule
{
    public long Gid { get; set; }
    public int Qty { get; set; }
    public long MinPrice { get; set; }
    public bool Keep { get; set; }
    /// <summary>Lots de cette taille à garder en vente (la remise en vente complète jusqu'à ce nombre). 1 = ancien comportement.</summary>
    public int Target { get; set; } = 1;
    public HdvRelistRule Clone() => new() { Gid = Gid, Qty = Qty, MinPrice = MinPrice, Keep = Keep, Target = Target };
}
