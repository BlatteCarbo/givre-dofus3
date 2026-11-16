using System;

namespace GivreClone.Protocol;

/// <summary>
/// Évalue une CHAÎNE DE CRITÈRE du world-graph Dofus (m_criterion), ex "Qf=230", "(Qf=230&amp;Qf=231)|Qf=12".
/// Combinateurs : &amp; (ET), | (OU), parenthèses. Un ATOME = 2 lettres d'opérateur + comparateur + opérande.
///
/// Opérateurs interprétés : <c>Qf</c> (quête TERMINÉE, via QuestsEvent) et <c>MI</c> (état d'un INTERACTIF,
/// <c>MI=&lt;élément&gt;,&lt;état&gt;</c> : l'élément de la carte de départ est dans cet état — porte dans son état normal (0),
/// levier activé (1)…). MI se lit en DIRECT sur la carte courante ; ailleurs l'état est inconnu et seul l'état par défaut (0)
/// est présumé (une porte au repos), jamais un levier activé. Tout autre opérateur (Qa/PO/Ad/IS/SC/PG/Qo/…) est traité comme
/// FAUX — choix CONSERVATEUR :
/// on n'emprunte une porte/PNJ conditionnel QUE si l'on peut PROUVER que sa condition est remplie ; sinon on ne
/// l'inclut pas dans l'itinéraire (comportement identique à avant, où toute arête à critère était ignorée). Un
/// franchissement tenté à tort échoue de toute façon proprement (le PNJ ne coopère pas -&gt; reroute/cap).
/// Sur parse invalide -&gt; faux.
/// </summary>
public static class QuestCriteria
{
    /// <summary><paramref name="questFinished"/> : id de quête -&gt; terminée ?. true si le critère est (prouvé) satisfait.
    /// Une chaîne vide = pas de condition = toujours franchissable.</summary>
    public static bool Eval(string? criterion, Func<int, bool> questFinished) => Eval(criterion, questFinished, null);

    /// <summary>Idem, avec l'état LIVE des interactifs de la carte courante (<paramref name="interactiveState"/> : id
    /// d'élément -&gt; état, null si l'élément n'est pas sur la carte courante) pour les atomes MI.</summary>
    public static bool Eval(string? criterion, Func<int, bool> questFinished, Func<int, int?>? interactiveState)
    {
        if (string.IsNullOrWhiteSpace(criterion)) return true;
        try { int i = 0; bool v = ParseOr(criterion, ref i, new Ctx(questFinished, interactiveState)); return v && i == criterion.Length; }
        catch { return false; }
    }

    /// <summary>Le critère ne porte QUE sur des états d'interactifs (tous ses atomes sont MI) : ex « MI=484493,0&amp;MI=484493,0 »
    /// (porte au repos). Un tel passage n'est pas une progression du perso : satisfait, il vaut un passage libre.</summary>
    public static bool IsInteractiveStateOnly(string? criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion)) return false;
        // Appelé pour chaque arête à critère à chaque passe du BFS : mémoïsé (≈ 1 000 chaînes distinctes dans tout le jeu).
        return _stateOnly.GetOrAdd(criterion, ComputeStateOnly);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _stateOnly = new(StringComparer.Ordinal);

    private static bool ComputeStateOnly(string criterion)
    {
        try
        {
            int i = 0;
            bool valid = ParseOr(criterion, ref i, new Ctx(_ => false, null, AllInteractiveSyntax: true));
            return valid && i == criterion.Length;
        }
        catch { return false; }
    }

    private sealed record Ctx(Func<int, bool> Qf, Func<int, int?>? InteractiveState, bool AllInteractiveSyntax = false);

    // OU (précédence la plus faible) : term ('|' term)*
    private static bool ParseOr(string s, ref int i, Ctx c)
    {
        bool acc = ParseAnd(s, ref i, c);
        while (i < s.Length && s[i] == '|') { i++; bool r = ParseAnd(s, ref i, c); acc = acc || r; }
        return acc;
    }

    // ET : factor ('&' factor)*
    private static bool ParseAnd(string s, ref int i, Ctx c)
    {
        bool acc = ParseFactor(s, ref i, c);
        while (i < s.Length && s[i] == '&') { i++; bool r = ParseFactor(s, ref i, c); acc = acc && r; }
        return acc;
    }

    // '(' expr ')' | atome
    private static bool ParseFactor(string s, ref int i, Ctx c)
    {
        if (i < s.Length && s[i] == '(')
        {
            i++;
            bool v = ParseOr(s, ref i, c);
            if (i >= s.Length || s[i] != ')') throw new FormatException("Parenthèse non fermée.");
            i++;
            return v;
        }
        return ParseAtom(s, ref i, c);
    }

    // Atome = tout jusqu'au prochain combinateur/parenthèse. Format : <2 lettres><comparateur><opérande>.
    private static bool ParseAtom(string s, ref int i, Ctx c)
    {
        int start = i;
        while (i < s.Length && s[i] != '&' && s[i] != '|' && s[i] != '(' && s[i] != ')') i++;
        if (start == i) throw new FormatException("Opérande absent.");
        string atom = s.Substring(start, i - start).Trim();
        if (atom.Length < 3) throw new FormatException("Atome invalide.");
        string op = atom.Substring(0, 2);
        char cmp = atom[2];
        string operand = atom.Substring(3);
        if (c.AllInteractiveSyntax && op != "MI") throw new FormatException("Ce critère n'est pas purement interactif.");
        if (op == "Qf" && int.TryParse(operand, out int questId))
            return cmp switch { '=' => c.Qf(questId), '!' => !c.Qf(questId), _ => false };
        if (op == "MI")
        {
            // MI=<élément>,<état> : état LIVE si l'élément est sur la carte courante ; sinon inconnu -> seul l'état par
            // défaut (0, porte au repos) est présumé vrai. « ! » n'est affirmé que sur un état connu.
            var parts = operand.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int elem) || !int.TryParse(parts[1], out int want))
            {
                if (c.AllInteractiveSyntax) throw new FormatException("Atome interactif invalide.");
                return false;
            }
            if (c.AllInteractiveSyntax)
            {
                if (cmp is not ('=' or '!')) throw new FormatException("Opérateur interactif invalide.");
                return true;
            }
            int? have = c.InteractiveState?.Invoke(elem);
            return cmp switch
            {
                '=' => have is int h ? h == want : want == 0,
                '!' => have is int h2 && h2 != want,
                _ => false,
            };
        }
        return false;   // opérateur non interprété -> conservateur (non franchissable)
    }
}
