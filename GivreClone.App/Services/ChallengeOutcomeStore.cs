using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GivreClone.App.Services;

/// <summary>
/// Issues réelles des défis (réussi / échoué) PAR PERSONNAGE et par règle, persistées dans challenge-stats.tsv. Le choix du
/// défi corrige ainsi ses chances a priori (<see cref="ChallengeCatalog.Blend"/>) : les estimations fixes faisaient
/// choisir Statue (p80) et Nomade, ratés 13/14 et 12/14 la nuit du 24/10. Format texte : perso, règle, réussites, échecs.
/// </summary>
public static class ChallengeOutcomeStore
{
    private static readonly string FilePath = Path.Combine(MZP19LocalClone.DataRoot, "challenge-stats.tsv");
    private static readonly object Gate = new();
    private static Dictionary<(long Character, ChallengeRule Rule), (int Ok, int Ko)>? _data;

    private static Dictionary<(long Character, ChallengeRule Rule), (int Ok, int Ko)> Data()
    {
        if (_data is not null) return _data;
        var d = new Dictionary<(long Character, ChallengeRule Rule), (int Ok, int Ko)>();
        try
        {
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var c = line.Split('\t');
                    if (c.Length == 4 && long.TryParse(c[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ch)
                        && Enum.TryParse(c[1], out ChallengeRule r)
                        && int.TryParse(c[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ok)
                        && int.TryParse(c[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ko))
                        d[(ch, r)] = (ok, ko);
                }
        }
        catch (Exception e) { Logger.Warn($"challenge-stats.tsv illisible, statistiques repartent de zéro : {e.Message}"); }
        return _data = d;
    }

    /// <summary>Chance de réussite de <paramref name="rule"/> pour ce personnage, a priori corrigé par ses issues observées.</summary>
    public static int Adjusted(long character, ChallengeRule rule, int prior)
    {
        lock (Gate)
            return Data().TryGetValue((character, rule), out var v) ? ChallengeCatalog.Blend(prior, v.Ok, v.Ko) : prior;
    }

    public static (int Ok, int Ko) Of(long character, ChallengeRule rule)
    {
        lock (Gate) return Data().TryGetValue((character, rule), out var v) ? v : (0, 0);
    }

    public static void Record(long character, ChallengeRule rule, bool success)
    {
        if (character == 0 || rule == ChallengeRule.Unknown) return;
        lock (Gate)
        {
            var d = Data();
            var v = d.TryGetValue((character, rule), out var cur) ? cur : (Ok: 0, Ko: 0);
            d[(character, rule)] = success ? (v.Ok + 1, v.Ko) : (v.Ok, v.Ko + 1);
            var sb = new StringBuilder();
            foreach (var kv in d.OrderBy(k => k.Key.Character).ThenBy(k => k.Key.Rule))
                sb.Append(kv.Key.Character.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(kv.Key.Rule).Append('\t')
                  .Append(kv.Value.Ok.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(kv.Value.Ko.ToString(CultureInfo.InvariantCulture)).Append('\n');
            try { AtomicJson.Write(FilePath, sb.ToString()); }
            catch (Exception e) { Logger.Warn($"challenge-stats.tsv non écrit : {e.Message}"); }
        }
    }
}
