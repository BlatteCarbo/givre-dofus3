using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

/// <summary>
/// Parseur/évaluateur du champ `statesCriterion` des sorts Dofus 3 (lu du bundle spelllevelsdataroot).
/// Format confirmé EN JEU (échantillons `HS!17`, `HS!5521`) : une EXPRESSION = un ou plusieurs ATOMES joints par
/// `&amp;` (ET) / `|` (OU). Un atome = clé (lettres) + opérateur (`!`/`=`/`&lt;`/`&gt;`) + valeur entière (id d'état).
/// Clé `HS` = « has state » ; `HS!17` = « ne doit PAS avoir l'état 17 » ; `HS=17` = « doit avoir l'état 17 ».
///
/// ⚠️ Grammaire volontairement SIMPLE (pas de parenthèses/précédence) : couvre les critères mono-atome et tout-ET,
/// qui sont l'écrasante majorité. Un opérateur/clé inconnu -&gt; atome IGNORÉ (fail-open) : on ne bloque jamais un
/// cast faute de comprendre le critère. L'évaluation contre les états ACTIFS du combattant est fournie par l'appelant.
/// </summary>
public static class StateCriterion
{
    public readonly record struct Atom(string Key, char Op, int Value)
    {
        /// <summary>Cet atome porte-t-il sur un état (clé HS) de façon interprétable ? (sinon on l'ignore, fail-open)</summary>
        public bool IsStateCheck => Key == "HS" && (Op == '!' || Op == '=');
        /// <summary>true = l'état Value doit être ABSENT ; false = il doit être PRÉSENT.</summary>
        public bool Forbidden => Op == '!';
    }

    /// <summary>Découpe une expression en atomes + l'opérateur logique qui les relie (le 1er a Logic='&amp;' neutre).
    /// Ex "HS!17&amp;HS=5521" -&gt; [(HS!17,'&amp;'),(HS=5521,'&amp;')].</summary>
    public static List<(Atom Atom, char Logic)> Parse(string? expr)
    {
        var list = new List<(Atom, char)>();
        if (string.IsNullOrWhiteSpace(expr)) return list;
        char logic = '&';
        int i = 0;
        var buf = new System.Text.StringBuilder();
        void Flush(char nextLogic)
        {
            var tok = buf.ToString().Trim();
            buf.Clear();
            if (tok.Length == 0) return;
            if (TryParseAtom(tok, out var a)) list.Add((a, logic));
            logic = nextLogic;
        }
        for (; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '&' || c == '|') Flush(c);
            else buf.Append(c);
        }
        Flush('&');
        return list;
    }

    private static bool TryParseAtom(string tok, out Atom atom)
    {
        atom = default;
        int op = -1;
        for (int j = 0; j < tok.Length; j++)
            if (tok[j] is '!' or '=' or '<' or '>' or '~') { op = j; break; }
        if (op <= 0 || op == tok.Length - 1) return false;
        string key = tok.Substring(0, op);
        char o = tok[op];
        if (!int.TryParse(tok.Substring(op + 1), out int val)) return false;
        atom = new Atom(key, o, val);
        return true;
    }

    /// <summary>true si le sort est LANÇABLE vis-à-vis de son critère d'états, `hasState(id)` renvoyant l'état actif du
    /// combattant concerné. Atome incompris -&gt; ignoré (fail-open). Logique : les `&amp;` doivent TOUS passer ; un groupe
    /// séparé par `|` suffit s'il passe. Implémentation simple sans parenthèses (adaptée au format observé).</summary>
    public static bool CanCast(string? expr, Func<int, bool> hasState)
    {
        var atoms = Parse(expr);
        if (atoms.Count == 0) return true;   // aucun critère -> toujours lançable
        // On évalue en OU de sous-expressions ET : coupe la liste sur les '|'.
        bool anyGroupOk = false, groupOk = true, groupHasCheck = false;
        foreach (var (a, logic) in atoms)
        {
            if (logic == '|')
            {
                anyGroupOk |= (groupOk && groupHasCheck) || !groupHasCheck && groupOk;   // groupe précédent
                groupOk = true; groupHasCheck = false;
            }
            if (!a.IsStateCheck) continue;                 // fail-open : atome ignoré
            groupHasCheck = true;
            bool present = hasState(a.Value);
            bool ok = a.Forbidden ? !present : present;
            groupOk &= ok;
        }
        anyGroupOk |= groupOk;
        return anyGroupOk;
    }

    /// <summary>Rendu LISIBLE du critère (pour logs/UI), `nameOf(id)` donnant le nom d'état (ou null). Ex `HS!17` ->
    /// « sans l'état Invisible ». Retourne "" si aucun critère interprétable.</summary>
    public static string Describe(string? expr, Func<int, string?> nameOf)
    {
        var atoms = Parse(expr);
        var parts = new List<string>();
        foreach (var (a, logic) in atoms)
        {
            if (!a.IsStateCheck) continue;
            string n = nameOf(a.Value) ?? $"état {a.Value}";
            parts.Add((a.Forbidden ? "sans " : "avec ") + n);
        }
        return parts.Count == 0 ? "" : string.Join(" et ", parts);
    }
}
