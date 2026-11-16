using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services.LotM;

/// <summary>
/// Heure de fermeture d'une anomalie. L'UNITÉ de <c>closing_time</c> n'est pas établie par le mapping : on la déduit de l'ordre de grandeur
/// (≥ 1e11 : epoch en millisecondes ; ≥ 1e9 : epoch en secondes ; sinon : secondes RELATIVES à la réception). Hypothèse à confirmer
/// sur une trame réelle ; les tests figent cette règle, pas la vérité du serveur.
/// </summary>
public static class AnomalyClock
{
    public static double RemainingSeconds(long closingRaw, DateTime receivedUtc, DateTime nowUtc)
    {
        if (closingRaw <= 0) return 0;
        double s;
        if (closingRaw >= 100_000_000_000L) s = closingRaw / 1000.0 - EpochSeconds(nowUtc);
        else if (closingRaw >= 1_000_000_000L) s = closingRaw - EpochSeconds(nowUtc);
        else s = closingRaw - (nowUtc - receivedUtc).TotalSeconds;
        return Math.Max(0, Math.Floor(s));
    }

    /// <summary>Instant de fermeture en secondes epoch (0 si inconnu), même hypothèse que <see cref="RemainingSeconds"/>.</summary>
    public static long ClosingEpochSeconds(long closingRaw, DateTime receivedUtc)
    {
        if (closingRaw <= 0) return 0;
        if (closingRaw >= 100_000_000_000L) return closingRaw / 1000;
        if (closingRaw >= 1_000_000_000L) return closingRaw;
        return (long)EpochSeconds(receivedUtc) + closingRaw;
    }

    private static double EpochSeconds(DateTime utc) => (utc - DateTime.UnixEpoch).TotalSeconds;
}

/// <summary>Résultat de l'évaluation d'un critère de départ de quête.</summary>
public sealed record CriterionResult(bool Satisfied, IReadOnlyList<string> BlockedBy, IReadOnlyList<string> NotChecked);

/// <summary>
/// Évalue un critère de départ de quête (« PL&gt;4&amp;Qf=230 »). Termes reconnus : <c>PL</c> (niveau), <c>Qf</c> (quête terminée), <c>Qa</c> (quête en
/// cours) avec = != &lt; &gt; &lt;= &gt;=. Tout autre terme, ou un critère avec « | » ou parenthèses, est rendu dans NotChecked (jamais deviné) ;
/// il ne bloque pas. Vide = satisfait.
/// </summary>
public static class CriterionEval
{
    public static CriterionResult Evaluate(string? criterion, int level, Func<int, bool> questFinished, Func<int, bool> questActive)
    {
        var blocked = new List<string>(); var unchecked_ = new List<string>();
        var c = (criterion ?? "").Trim();
        if (c.Length == 0) return new(true, blocked, unchecked_);
        if (c.Contains('|') || c.Contains('(')) { unchecked_.Add(c); return new(true, blocked, unchecked_); }
        foreach (var term in c.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int i = 0; while (i < term.Length && char.IsLetter(term[i])) i++;
            string key = term[..i];
            int j = i; while (j < term.Length && "=!<>".Contains(term[j])) j++;
            string op = term[i..j], val = term[j..];
            if (key.Length == 0 || op.Length == 0 || !long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { unchecked_.Add(term); continue; }
            bool? ok = key switch
            {
                "PL" => Compare(level, op, n),
                "Qf" when op is "=" or "!=" => (op == "=") == questFinished((int)n),
                "Qa" when op is "=" or "!=" => (op == "=") == questActive((int)n),
                _ => null,
            };
            if (ok is null) unchecked_.Add(term); else if (!ok.Value) blocked.Add(term);
        }
        return new(blocked.Count == 0, blocked, unchecked_);
    }

    private static bool? Compare(long a, string op, long b) => op switch
    {
        "=" => a == b, "!=" => a != b, ">" => a > b, "<" => a < b, ">=" => a >= b, "<=" => a <= b, _ => null,
    };
}

/// <summary>Une taille de lot à tenir en vente : combien, avec quel prix (de LOT) ou plancher éventuels.</summary>
public sealed record KeepSize(int Size, int Count, long? Price, long? MinPrice);

public sealed record KeepStep(int Size, long Price);
public sealed record KeepPlan(IReadOnlyList<KeepStep> Steps, string? Reason);

/// <summary>État du marché nécessaire au calcul des prix de <c>sale:keepOnSale</c>.</summary>
public sealed record KeepMarket(long AvgUnit, Func<int, long> MarketMinLot, Func<int, long> MyCheapestLot, Func<int, int> MyCount);

/// <summary>
/// Planification de <c>sale:keepOnSale</c> d'après le portail : complète les annonces jusqu'au nombre voulu pour CHAQUE taille, sans retirer ni
/// repricer. Prix d'un lot : <c>price</c> s'il est donné ; sinon aligné sur le lot du joueur s'il est déjà le moins cher de sa taille, sinon
/// marché − sous-cotation, jamais sous la moitié du prix moyen (× taille) ; sans concurrent : prix moyen × taille ; jamais sous <c>minPrice</c>.
/// Plus grands lots d'abord. Raisons : badsize, badargs, noprice.
/// </summary>
public static class KeepOnSalePlanner
{
    public static readonly int[] Sizes = { 1, 10, 100, 1000 };

    /// <summary>Taille réelle depuis une taille (1/10/100/1000) ou un index Givre (1..4) ; null si invalide.</summary>
    public static int? NormalizeSize(long raw) => raw switch
    {
        1 or 10 or 100 or 1000 => (int)raw,
        2 => 10, 3 => 100, 4 => 1000,
        _ => null,
    };

    public static KeepPlan Plan(IReadOnlyList<KeepSize> wanted, long undercut, KeepMarket m)
    {
        if (wanted.Count == 0) return new(Array.Empty<KeepStep>(), "badargs");
        var steps = new List<KeepStep>();
        string? reason = null;
        foreach (var w in wanted.OrderByDescending(x => x.Size))
        {
            if (w.Count < 0 || (w.Price is < 0) || (w.MinPrice is < 0)) return new(steps, "badargs");
            int missing = Math.Max(0, w.Count - m.MyCount(w.Size));
            if (missing == 0) continue;
            long? price = LotPrice(w, undercut, m);
            if (price is null or <= 0) { reason ??= "noprice"; continue; }
            for (int i = 0; i < missing; i++) steps.Add(new KeepStep(w.Size, price.Value));
        }
        return new KeepPlan(steps.OrderByDescending(s => s.Size).ToList(), reason);
    }

    private static long? LotPrice(KeepSize w, long undercut, KeepMarket m)
    {
        long? price = w.Price;
        if (price is null)
        {
            long market = m.MarketMinLot(w.Size), mine = m.MyCheapestLot(w.Size);
            if (mine > 0 && (market <= 0 || mine <= market)) price = mine;                       // aligné sur mon lot déjà le moins cher
            else if (market > 0)
            {
                price = market - Math.Max(0, undercut);
                if (m.AvgUnit > 0) price = Math.Max(price.Value, m.AvgUnit * w.Size / 2);        // jamais sous la moitié du prix moyen
            }
            else if (m.AvgUnit > 0) price = m.AvgUnit * w.Size;                                  // sans concurrent
            else return null;
        }
        if (w.MinPrice is long min) price = Math.Max(price.Value, min);
        return price;
    }
}

/// <summary>
/// Succès du perso : terminés (liste à la connexion puis incrémentale) et récompenses en attente. Les récompenses en attente sont celles des
/// succès terminés PENDANT la session. <c>AchievementsEvent</c> ne nomme que <c>achieved_achievements</c> ; un <c>AchievedAchievement</c> ne nomme
/// que l'id, le perso, le niveau de fin, le rang pionnier et le champ obfusqué <c>gfan</c>. Aucun de ces noms n'est une liste « terminé mais non
/// réclamé », donc <see cref="Seed"/> n'enfile pas de récompense. La capture de découverte enregistre l'événement entier pour relever le marqueur.
/// </summary>
public sealed class AchievementLedger
{
    private readonly object _gate = new();
    private readonly HashSet<int> _finished = new();
    private readonly Dictionary<int, int> _pending = new();   // id -> niveau à la fin du succès
    private readonly HashSet<int> _refused = new();

    public void Seed(IEnumerable<int> finished) { lock (_gate) foreach (var id in finished) _finished.Add(id); }

    /// <summary>Un succès vient d'être terminé : il devient « à récupérer » (niveau = niveau du perso à ce moment, 0 si inconnu).</summary>
    public bool OnFinished(int id, int level)
    {
        lock (_gate)
        {
            bool isNew = _finished.Add(id);
            if (isNew && !_pending.ContainsKey(id)) _pending[id] = level;
            return isNew;
        }
    }

    public bool IsDone(int id) { lock (_gate) return _finished.Contains(id); }
    public IReadOnlyList<int> Finished() { lock (_gate) return _finished.OrderBy(x => x).ToList(); }
    public IReadOnlyList<(int Id, int Level)> Rewardable() { lock (_gate) return _pending.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToList(); }
    public bool HasReward(int? id = null) { lock (_gate) return id is null ? _pending.Count > 0 : _pending.ContainsKey(id.Value); }
    public int RewardCount() { lock (_gate) return _pending.Count; }

    /// <summary>Résultat d'une demande de récompense : succès = retirée de l'attente ; refus = gardée mais non redemandée automatiquement.</summary>
    public void OnRewardResult(int id, bool success)
    {
        lock (_gate)
        {
            if (success) _pending.Remove(id); else if (_pending.ContainsKey(id)) _refused.Add(id);
        }
    }

    /// <summary>Récompenses à demander automatiquement : en attente et pas déjà refusées.</summary>
    public IReadOnlyList<int> AutoClaimable() { lock (_gate) return _pending.Keys.Where(k => !_refused.Contains(k)).OrderBy(k => k).ToList(); }

    public void Reset() { lock (_gate) { _finished.Clear(); _pending.Clear(); _refused.Clear(); } }

    /// <summary>Délai avant la récupération automatique d'un succès terminé : 3 à 6 s (portail).</summary>
    public static TimeSpan AutoClaimDelay(Random rng) => TimeSpan.FromMilliseconds(rng.Next(3000, 6001));
}

/// <summary>Une carte où un PNJ a été vu.</summary>
public sealed record NpcSighting(long MapId, int Cell, int X, int Y, DateTime SeenUtc, string GameVersion);

/// <summary>
/// Mémoire des PNJ croisés par les bots (les fichiers du jeu ne donnent pas la position des PNJ) : carte, cellule, date, version du jeu.
/// Le plus récent d'abord ; une même carte n'est gardée qu'une fois par PNJ (mise à jour). Sérialisable en JSON pour persister.
/// </summary>
public sealed class NpcSightingStore
{
    public const int MaxPerNpc = 40;
    private readonly object _gate = new();
    private readonly Dictionary<int, List<NpcSighting>> _byNpc = new();

    public void Record(int npcId, long mapId, int cell, int x, int y, DateTime nowUtc, string gameVersion)
    {
        if (npcId <= 0 || mapId <= 0) return;
        lock (_gate)
        {
            if (!_byNpc.TryGetValue(npcId, out var l)) _byNpc[npcId] = l = new List<NpcSighting>();
            l.RemoveAll(s => s.MapId == mapId);
            l.Insert(0, new NpcSighting(mapId, cell, x, y, nowUtc, gameVersion));
            if (l.Count > MaxPerNpc) l.RemoveRange(MaxPerNpc, l.Count - MaxPerNpc);
        }
    }

    /// <summary>Cartes où ce PNJ a été vu, la plus récente d'abord (vide s'il n'a jamais été croisé).</summary>
    public IReadOnlyList<NpcSighting> Locate(int npcId)
    {
        lock (_gate) return _byNpc.TryGetValue(npcId, out var l) ? l.OrderByDescending(s => s.SeenUtc).ToList() : new List<NpcSighting>();
    }

    private sealed class Dto { public Dictionary<string, List<NpcSighting>> Npcs { get; set; } = new(); }

    public string ToJson()
    {
        lock (_gate) return JsonSerializer.Serialize(new Dto { Npcs = _byNpc.ToDictionary(k => k.Key.ToString(CultureInfo.InvariantCulture), v => v.Value) });
    }

    public static NpcSightingStore FromJson(string? json)
    {
        var s = new NpcSightingStore();
        if (string.IsNullOrWhiteSpace(json)) return s;
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(json);
            if (dto?.Npcs is null) return s;
            foreach (var (k, v) in dto.Npcs)
                if (int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                    s._byNpc[id] = v.Where(x => x.MapId > 0).Take(MaxPerNpc).ToList();
        }
        catch (JsonException) { /* fichier corrompu : mémoire vide, jamais d'exception au démarrage */ }
        return s;
    }
}
