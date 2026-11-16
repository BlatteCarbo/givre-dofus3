using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services;

/// <summary>Un combat lu dans le journal transport : attente (ms) entre la fin de MON tour et le début du suivant, et accusés envoyés pour les autres combattants.</summary>
public sealed record FightLogFight(int Rounds, IReadOnlyList<int> GapsMs, int ForeignAcks);

public sealed record FightLogReport(IReadOnlyList<FightLogFight> Fights, int MedianGapMs, int MaxGapMs, double ShareUnder400Ms);

/// <summary>Mesure la vitesse de combat à partir des lignes du journal transport (« [combat] début de tour : MOI (round N) »,
/// « [turbo-ready] … marqueur du serveur », « [turbo-ack] … (séquence de … »). Pur : testable sans jeu.</summary>
public static class FightLogMetrics
{
    private static readonly Regex Line = new(@"^(\d\d):(\d\d):(\d\d\.\d+)\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Round = new(@"d.but de tour : MOI \(round (\d+)\)", RegexOptions.Compiled);

    public static FightLogReport Analyze(IEnumerable<string> lines)
    {
        var fights = new List<FightLogFight>();
        var gaps = new List<int>();
        int rounds = 0, acks = 0;
        double? lastMarker = null;
        bool open = false;

        void Close()
        {
            if (open) fights.Add(new FightLogFight(rounds, gaps.ToList(), acks));
            rounds = 0; acks = 0; gaps.Clear(); open = false; lastMarker = null;
        }

        foreach (var raw in lines)
        {
            var m = Line.Match(raw);
            if (!m.Success) continue;
            double ts = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600.0 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60.0
                        + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            string x = m.Groups[4].Value;
            if (x.Contains("par le bot (s", StringComparison.Ordinal) && x.Contains("quence de", StringComparison.Ordinal)) acks++;
            else if (x.Contains("tour pr", StringComparison.Ordinal) && x.Contains("marqueur", StringComparison.Ordinal)) lastMarker = ts;
            var r = Round.Match(x);
            if (!r.Success) continue;
            int n = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
            if (n == 1) Close();
            open = true; rounds = n;
            if (n > 1 && lastMarker is double lm) gaps.Add((int)Math.Round((ts - lm) * 1000));
            lastMarker = null;
        }
        Close();

        var all = fights.SelectMany(f => f.GapsMs).OrderBy(v => v).ToList();
        int median = all.Count == 0 ? 0 : all[all.Count / 2];
        return new FightLogReport(fights, median, all.Count == 0 ? 0 : all[^1], all.Count == 0 ? 0 : all.Count(v => v < 400) / (double)all.Count);
    }
}
