namespace GivreClone.Protocol;

/// <summary>Budget de points dépensables vers une base absolue, paliers visibles dans le client 3.7.</summary>
public static class CharacteristicUpgradePolicy
{
    public static readonly (string Name, int Id)[] Stats =
    { ("strength",10), ("vitality",11), ("wisdom",12), ("chance",13), ("agility",14), ("intelligence",15) };

    /// <summary>Le wire porte les budgets investis pour TOUTES les stats, pas les bases résultantes.</summary>
    public static int Invested(string stat, int basis)
    {
        if (basis < 0) throw new System.ArgumentOutOfRangeException(nameof(basis));
        if (stat == "vitality") return basis;
        if (stat == "wisdom") return checked(basis * 3);
        if (stat is not ("strength" or "intelligence" or "chance" or "agility")) throw new System.ArgumentException("Unknown stat",nameof(stat));
        return checked(System.Math.Min(basis,100) + System.Math.Min(System.Math.Max(basis-100,0),100)*2
            + System.Math.Min(System.Math.Max(basis-200,0),100)*3 + System.Math.Max(basis-300,0)*4);
    }

    public static System.Collections.Generic.IReadOnlyDictionary<string,int>? Allocation(System.Collections.Generic.IReadOnlyDictionary<int,int> bases, string stat, int target)
    {
        var result = new System.Collections.Generic.Dictionary<string,int>();
        if (!System.Array.Exists(Stats, s => s.Name == stat)) return null;
        foreach (var (name,id) in Stats)
        {
            if (!bases.TryGetValue(id,out int basis) || basis < 0) return null; // fiche incomplète : ne jamais réinitialiser une autre stat
            result[name] = Invested(name,name == stat ? target : basis);
        }
        return result;
    }

    public static int Target(string stat, int currentBase, int budget)
    {
        if (currentBase < 0 || budget <= 0) return currentBase;
        if (stat is not ("vitality" or "wisdom" or "strength" or "intelligence" or "chance" or "agility")) return currentBase;
        int target = currentBase;
        while (budget > 0 && target < int.MaxValue)
        {
            int cost = stat == "vitality" ? 1 : stat == "wisdom" ? 3 : target < 100 ? 1 : target < 200 ? 2 : target < 300 ? 3 : 4;
            if (budget < cost) break;
            budget -= cost;
            target++;
        }
        return target;
    }
}
