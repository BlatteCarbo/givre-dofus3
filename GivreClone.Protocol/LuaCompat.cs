using System;
using System.Globalization;

namespace GivreClone.Protocol;

/// <summary>Mot-clé de téléport d'une étape de trajet: zaap(id) / zaapi(id) / havenbag / zaapcarte(id).</summary>
public enum TeleportKeyword { None, Zaap, Zaapi, HavenBag, WorldMap }

/// <summary>
/// Helpers PURS de compatibilité de l'API Lua: conversions d'arguments (lots HDV, index
/// 1-based), tolérance d'ordre d'arguments, et parsing des jetons du DSL de trajet (« a|b », « top(123) », « zaap(id) »).
/// Placés dans Protocol (et non App) pour être testables : le projet de tests ne référence que Protocol.
/// Parsers écrits à la main (pas de Regex) : simple et AOT-friendly.
/// </summary>
public static class LuaCompat
{
    /// <summary>sale: argument « lot » -> TAILLE. 0/absent/1 -> 1; 2 -> 10; 3 -> 100; 4 -> 1000;
    /// 10/100/1000 -> inchangé (taille) ; toute autre valeur -> inchangée (quantité brute, rangée ensuite par
    /// BidHouseStore.SlotOf comme avant).</summary>
    public static int LotSizeFromArg(int x) => x switch { <= 1 => 1, 2 => 10, 3 => 100, 4 => 1000, _ => x };

    /// <summary>sale:GetLots: quantité -> INDEX 1..4 (1->1, 10->2, 100->3, 1000->4), sinon 0.</summary>
    public static int LotIndexOfQuantity(int qty) => qty switch { 1 => 1, 10 => 2, 100 => 3, 1000 => 4, _ => 0 };

    /// <summary>sale:GetItem* : index Lua 1-based -> 0-based. 0 = alias de 1 (compat anciens scripts GivreClone 0-based).
    /// Négatif -> -1 (hors bornes).</summary>
    public static int ZeroBasedFromLuaIndex(int i) => i < 0 ? -1 : i == 0 ? 0 : i - 1;

    /// <summary>sendPrivateMessage: un pseudo Dofus n'a PAS d'espace. L'ordre tiers (texte, pseudo) est détecté quand
    /// arg1 contient un espace et que arg2 (non vide) n'en contient aucun.</summary>
    public static bool ShouldSwapPmArgs(string a1, string a2)
        => (a1 ?? "").Trim().Contains(' ') && (a2 ?? "").Trim() is { Length: > 0 } b && !b.Contains(' ');

    /// <summary>« a|b|c » -> UNE alternative au hasard (parties non vides, rognées). Sans '|' -> chaîne inchangée.
    /// Aucune partie non vide (ex « | ») -> "".</summary>
    public static string PickAlternative(string raw, Random rng)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('|') < 0) return raw ?? "";
        var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "";
        return parts.Length == 1 ? parts[0] : parts[rng.Next(parts.Length)];
    }

    /// <summary>Direction avec cellule de sortie FORCÉE optionnelle : « top », « haut », « north », « nord »… (mêmes
    /// synonymes que le pas-à-pas directionnel) OU « top(123) » (espaces tolérés, insensible à la casse). cell = -1 sans
    /// parenthèses. Cellule hors 0..559 ou non entière -> false.</summary>
    public static bool TryParseDirToken(string s, out MapDir dir, out int cell)
    {
        dir = MapDir.Top; cell = -1;
        string t = (s ?? "").Trim();
        if (t.Length == 0) return false;
        string name = t;
        int open = t.IndexOf('(');
        if (open >= 0)
        {
            if (t[t.Length - 1] != ')' || t.IndexOf('(', open + 1) >= 0 || t.IndexOf(')') != t.Length - 1) return false;
            name = t.Substring(0, open);
            string inner = t.Substring(open + 1, t.Length - open - 2).Trim();
            if (!int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out int c) || c < 0 || c >= MapPathfinder.CellCount) return false;
            cell = c;
        }
        switch (name.Trim().ToLowerInvariant())
        {
            case "top": case "haut": case "north": case "nord": dir = MapDir.Top; return true;
            case "bottom": case "bas": case "south": case "sud": dir = MapDir.Bottom; return true;
            case "left": case "gauche": case "west": case "ouest": dir = MapDir.Left; return true;
            case "right": case "droite": case "east": case "est": dir = MapDir.Right; return true;
            default: dir = MapDir.Top; cell = -1; return false;
        }
    }

    /// <summary>« zaap(&lt;id&gt;) », « zaapi(&lt;id&gt;) », « zaapcarte(&lt;id&gt;) » (id entier &gt; 0), « havenbag » /
    /// « havenbag() » (insensible à la casse, espaces tolérés). zaapcarte = téléport depuis la carte du monde, sans zaap
    /// au sol ni havre-sac. La forme « zaap:123 » n'est PAS reconnue ici (elle reste au StripTravelPrefix).</summary>
    public static bool TryParseTeleportKeyword(string s, out TeleportKeyword kind, out long mapId)
    {
        kind = TeleportKeyword.None; mapId = 0;
        string t = (s ?? "").Trim();
        if (t.Length == 0) return false;
        string name = t, inner = "";
        bool parens = false;
        int open = t.IndexOf('(');
        if (open >= 0)
        {
            if (t[t.Length - 1] != ')' || t.IndexOf('(', open + 1) >= 0 || t.IndexOf(')') != t.Length - 1) return false;
            name = t.Substring(0, open);
            inner = t.Substring(open + 1, t.Length - open - 2).Trim();
            parens = true;
        }
        switch (name.Trim().ToLowerInvariant())
        {
            case "havenbag":
                if (inner.Length != 0) return false;
                kind = TeleportKeyword.HavenBag; return true;
            case "zaap":
            case "zaapi":
                if (!parens || !long.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0) return false;
                kind = name.Trim().Equals("zaap", StringComparison.OrdinalIgnoreCase) ? TeleportKeyword.Zaap : TeleportKeyword.Zaapi;
                mapId = id;
                return true;
            case "zaapcarte":
                if (!parens || !long.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out long worldMap) || worldMap <= 0) return false;
                kind = TeleportKeyword.WorldMap;
                mapId = worldMap;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Lot I (inventory:stuff) : un type d'objet est-il éligible au remplissage AUTOMATIQUE ? Toutes ses positions
    /// possibles dans 0..15 ET (une seule position, OU les anneaux {2,4}, OU un sous-ensemble des dofus/trophées 9..14). Exclut
    /// les types multi-emplacements (super types 22 et 69) et les emplacements &gt;= 16 (monture, compagnon, costume).</summary>
    public static bool AutoStuffEligible(System.Collections.Generic.IReadOnlyList<int> positions)
    {
        if (positions is null || positions.Count == 0) return false;
        bool only9to14 = true, has2 = false, has4 = false, other = false;
        foreach (int p in positions)
        {
            if (p < 0 || p > 15) return false;
            if (p < 9 || p > 14) only9to14 = false;
            if (p == 2) has2 = true; else if (p == 4) has4 = true; else other = true;
        }
        if (positions.Count == 1) return true;
        if (has2 && has4 && !other) return true;   // anneaux
        return only9to14;
    }
}
