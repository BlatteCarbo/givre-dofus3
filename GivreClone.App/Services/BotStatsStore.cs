using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

/// <summary>Periode d'agregation des stats : 24h / 7j / 30j / 1 an (ou un jour precis via le date picker).</summary>
public enum StatsPeriod { Day, Week, Month, Year }

/// <summary>Une action recente (feed "Dernieres actions") : recolte (avec quantite + iconId) ou combat.</summary>
public sealed record StatsAction(long Ts, bool IsFight, string Label, int Qty, int IconId = 0);

/// <summary>Instantane agrege des stats d'un perso sur une periode donnee (calcule a la demande, jamais persiste).</summary>
public sealed class StatsSnapshot
{
    public int Harvests;
    public int Fights;
    public double AvgFightSec;
    public double BottingSec;
    public IReadOnlyList<StatsAction> Recent = Array.Empty<StatsAction>();
    public IReadOnlyList<double> BottingSeriesHours = Array.Empty<double>();   // temps de botting CUMULE (heures) a N points egaux
    public IReadOnlyList<ResourceTotal> ResourceTotals = Array.Empty<ResourceTotal>();   // total recolte par ressource (periode)
    // --- Métriques avancées (rentabilité / stabilité) ---
    public int ResourcesQty;         // quantité totale de ressources récoltées (somme des lots)
    public long KamasGained;         // kamas NET gagnés sur la période (dernier - premier échantillon)
    public long XpGained;            // XP NETTE gagnée sur la période (dernier - premier échantillon)
    public long EstimatedValue;      // valeur estimée des ressources récoltées (qté × prix HDV moyen connu)
    public double EstimatedValuePerHour;  // valeur estimée / heure de botting
    public int BankVisits;           // nombre de passages en banque
    public int FightsWon;            // combats gagnés
    public int FightsLost;           // combats perdus
    public double KamasPerHour;      // kamas nets / heure de botting
    public double XpPerHour;         // XP nette / heure de botting
    public double HarvestsPerHour;   // récoltes / heure de botting
    public double ResourcesPerHour;  // quantité récoltée / heure de botting
    public double FightsPerHour;     // combats / heure de botting
    public int Deaths;               // nombre de morts du perso sur la période
    public long PodsGenerated;       // pods récoltés sur la période (somme des hausses de pods entre échantillons)
    public double PodsPerHour;       // pods récoltés / heure de botting
    public int AnomalyFragments;     // fragments d'anomalie gagnés en combat sur la période
    public double AnomalyFragmentsPerHour;
    public IReadOnlyList<HeatmapRow> Heatmap = Array.Empty<HeatmapRow>();   // par carte : passages, récoltes, temps moyen
}

/// <summary>Ligne de heatmap de trajet : carte (coord "x,y" ou id), nombre de passages, récoltes, temps moyen par passage (s).</summary>
public sealed record HeatmapRow(string MapKey, int Passages, int Harvests, double AvgSec);

/// <summary>Total recolte pour une ressource sur la periode : nom, quantite cumulee, iconId (asset du jeu).</summary>
public sealed record ResourceTotal(string Name, int Qty, int IconId);

/// <summary>
/// Stats de botting PAR PERSONNAGE (clef = nom du perso, pas le PID qui change entre sessions), PERSISTEES sur disque
/// (%AppData%/GivreClone/stats/&lt;perso&gt;.json). Alimentee par les evenements de <see cref="LaunchService"/> (recoltes,
/// combats) + les sessions de script (botting). Agrege a la demande sur 24h/7j/30j/1an pour l'onglet Stats. Emonde a 1 an.
/// </summary>
public sealed partial class BotStatsStore
{
    // 1 an : permet la granularite "Annee" + le date picker (jour precis). Cout disque negligeable (arrays [ts, qty]).
    private const int RetentionDays = 365;
    // Delai depuis la PREMIERE modif non sauvee (pas depuis la derniere : sinon un farm continu ne declenche jamais la
    // sauvegarde -> data perdue a la fermeture). On sauve donc au plus toutes les 15 s pendant l'activite.
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(15);

    private sealed class CharData
    {
        public List<long[]> Harvests { get; set; } = new();   // [ts, qty, iconId]
        public List<string> HarvestNames { get; set; } = new(); // meme index que Harvests -> nom de la ressource
        public List<long[]> Fights { get; set; } = new();      // [ts, durationMs, outcome] (outcome : 1 gagné, -1 perdu, 0 nul/inconnu)
        public List<long[]> Sessions { get; set; } = new();    // [startTs, endTs] (botting)
        // Metriques avancees (enregistrees UNIQUEMENT pendant une session de botting active) :
        public List<long[]> KamasSamples { get; set; } = new();   // [ts, kamas] echantillonne au heartbeat (si change)
        public List<long[]> XpSamples { get; set; } = new();      // [ts, xp] experience TOTALE du perso echantillonnee (si change)
        public List<long[]> MapVisits { get; set; } = new();      // [ts, mapId, x, y] (x/y = long.MinValue si inconnu) -> heatmap
        public List<long> BankVisits { get; set; } = new();       // [ts] passage en banque (ouverture coffre, debounce)
        public List<long> Deaths { get; set; } = new();           // [ts] mort du perso (FighterKilled == mon id)
        public List<long[]> PodsSamples { get; set; } = new();    // [ts, podsCur] echantillonne (si change) -> pods generes/h (somme des deltas positifs)
        public List<long[]> Fragments { get; set; } = new();      // [ts, qty] fragments d'anomalie du butin d'un combat
    }

    private const long CoordUnknown = long.MinValue;
    private const long MapDwellCapMs = 5 * 60_000L;   // temps SUR une carte plafonne (évite de compter un AFK/logout comme "temps carte")

    // Table de prix HDV persistée (globale, pas par-perso) : gid (clé string pour le JSON) -> prix unitaire moyen.
    private sealed class PriceFile { public Dictionary<string, long> Prices { get; set; } = new(); }

    // Contexte STJ source-gen (NativeAOT) : compact (pas d'options -> WriteIndented=false), comme avant.
    [JsonSerializable(typeof(CharData))]
    [JsonSerializable(typeof(PriceFile))]
    private sealed partial class StatsJson : JsonSerializerContext { }

    private readonly string _dir;
    private readonly ConcurrentDictionary<string, CharData> _byChar = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<uint, string> _pidToChar = new();
    private readonly ConcurrentDictionary<uint, long> _fightStart = new();   // pid -> instant de debut du combat en cours
    private readonly ConcurrentDictionary<string, long> _openSession = new(StringComparer.OrdinalIgnoreCase); // char -> debut de session botting en cours
    private readonly ConcurrentDictionary<string, DateTime> _dirtySince = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastMap = new(StringComparer.OrdinalIgnoreCase);   // char -> dernier mapId visité (dédup consécutif)
    private readonly ConcurrentDictionary<string, long> _lastBank = new(StringComparer.OrdinalIgnoreCase);  // char -> ts dernier passage banque (debounce)
    private readonly ConcurrentDictionary<string, long> _lastKamas = new(StringComparer.OrdinalIgnoreCase); // char -> dernier kamas échantillonné (skip si inchangé)
    private readonly ConcurrentDictionary<string, long> _lastXp = new(StringComparer.OrdinalIgnoreCase);    // char -> dernière XP échantillonnée (skip si inchangée)
    private readonly ConcurrentDictionary<string, int> _lastPods = new(StringComparer.OrdinalIgnoreCase);  // char -> derniers pods échantillonnés (skip si inchangés)
    private readonly ConcurrentDictionary<uint, int> _lastFightOutcome = new();   // pid -> issue du dernier combat (1/-1/0), consommée par FightEnded
    private readonly ConcurrentDictionary<long, long> _gidPrice = new();          // gid -> prix unitaire moyen HDV (persisté, global)
    private volatile bool _pricesDirty;
    private string PricesPath => Path.Combine(_dir, "_prices.json");
    private const long BankDebounceMs = 30_000L;   // deux ouvertures de coffre à < 30 s = un seul passage
    private readonly object _ioLock = new();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public BotStatsStore(LaunchService launch)
    {
        _dir = Path.Combine(MZP19LocalClone.DataRoot, "stats");
        try { Directory.CreateDirectory(_dir); } catch { /* best-effort */ }

        launch.CharacterIdentified += (pid, ci) => { if (!string.IsNullOrWhiteSpace(ci.Name)) _pidToChar[pid] = ci.Name!; };
        LoadPrices();
        launch.ResourceHarvested += (pid, gid, qty, icon, name) =>
        {
            if (Resolve(pid) is not { } d) return;
            lock (d) { d.Harvests.Add(new[] { Now, (long)qty, (long)icon, (long)gid }); d.HarvestNames.Add(name ?? ""); }   // gid -> estimation valeur ; nom neutre persisté
            MarkDirty(pid);
        };
        launch.MarketPriceUpdated += (gid, price) => SetPrice(gid, price);   // le bot consulte l'HDV -> on mémorise le prix du gid
        launch.FightStarted += pid => _fightStart[pid] = Now;
        launch.FighterKilled += (pid, deadId) =>
        {
            if (deadId == 0 || deadId != launch.CharacterId(pid)) return;   // on ne compte QUE ma propre mort
            if (Resolve(pid) is not { } d) return;
            lock (d) d.Deaths.Add(Now);
            MarkDirty(pid);
        };
        launch.FightOutcome += (pid, won, lost) => _lastFightOutcome[pid] = won ? 1 : (lost ? -1 : 0);   // capté juste AVANT FightEnded
        launch.FightEnded += pid =>
        {
            long start = _fightStart.TryRemove(pid, out var s) ? s : Now;
            long outcome = _lastFightOutcome.TryRemove(pid, out var o) ? o : 0;   // 1 gagné, -1 perdu, 0 nul/inconnu
            if (Resolve(pid) is not { } d) return;
            // Fragments d'anomalie du butin (résultat publié juste avant FightEnded).
            int frags = launch.LastFightResult(pid)?.Loot.Where(l => l.Gid == UchronicElixirs.FragmentGid).Sum(l => l.Qty) ?? 0;
            lock (d)
            {
                d.Fights.Add(new[] { Now, Math.Max(0, Now - start), outcome });
                if (frags > 0) d.Fragments.Add(new[] { Now, (long)frags });
            }
            MarkDirty(pid);
        };
        // Métriques avancées : enregistrées UNIQUEMENT pendant une session de botting active (heatmap ciblée + volume borné).
        launch.MapChanged += (pid, map, x, y) =>
        {
            if (!BottingChar(pid, out var name) || map == 0) return;
            if (launch.IsMeaninglessPosition(pid, map, x, y)) return;   // combat / havre-sac annoncé en (0,0) : pas une vraie visite
            if (_lastMap.TryGetValue(name, out var last) && last == map) return;   // dédup ré-entrée immédiate
            _lastMap[name] = map;
            var d = Get(name);
            lock (d) d.MapVisits.Add(new[] { Now, map, x ?? CoordUnknown, y ?? CoordUnknown });
            MarkDirty(name);
        };
        launch.BankUpdated += pid =>
        {
            if (!BottingChar(pid, out var name)) return;
            long now = Now;
            if (_lastBank.TryGetValue(name, out var last) && now - last < BankDebounceMs) return;   // debounce (re-fires du coffre)
            _lastBank[name] = now;
            var d = Get(name);
            lock (d) d.BankVisits.Add(now);
            MarkDirty(name);
        };
    }

    // Le pid est-il en session de botting active, et si oui son nom de perso ?
    private bool BottingChar(uint pid, out string name)
    {
        name = "";
        if (!_pidToChar.TryGetValue(pid, out var n) || !_openSession.ContainsKey(n)) return false;
        name = n; return true;
    }

    /// <summary>Échantillonne les kamas d'un perso (appelé au heartbeat pour les bots en botting). N'enregistre QUE si la
    /// valeur a changé et qu'une session est ouverte -> série éparse pour le calcul kamas/heure.</summary>
    public void SampleKamas(string? charName, long kamas)
    {
        if (string.IsNullOrWhiteSpace(charName) || kamas < 0) return;   // échantillonné même hors botting (série éparse : que si changé)
        if (_lastKamas.TryGetValue(charName!, out var last) && last == kamas) return;
        _lastKamas[charName!] = kamas;
        var d = Get(charName!);
        lock (d) d.KamasSamples.Add(new[] { Now, kamas });
        MarkDirty(charName!);
    }

    // -------- Prix HDV persistés (globaux) : pour estimer la valeur des ressources récoltées --------
    private void SetPrice(long gid, long price)
    {
        if (gid <= 0 || price <= 0) return;
        _gidPrice[gid] = price;
        _pricesDirty = true;
    }

    private void LoadPrices()
    {
        try
        {
            if (File.Exists(PricesPath) && JsonSerializer.Deserialize(File.ReadAllText(PricesPath), StatsJson.Default.PriceFile) is { } pf)
                foreach (var kv in pf.Prices)
                    if (long.TryParse(kv.Key, out var g) && kv.Value > 0) _gidPrice[g] = kv.Value;
        }
        catch { /* best-effort : repart sans prix */ }
    }

    private void SavePrices()
    {
        if (!_pricesDirty) return;
        _pricesDirty = false;
        try
        {
            var pf = new PriceFile();
            foreach (var kv in _gidPrice) pf.Prices[kv.Key.ToString()] = kv.Value;
            lock (_ioLock) AtomicJson.Write(PricesPath, JsonSerializer.Serialize(pf, StatsJson.Default.PriceFile));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Échantillonne l'XP totale d'un perso (au heartbeat, pendant le botting). Enregistre si changée -> XP/heure.</summary>
    public void SampleXp(string? charName, long xp)
    {
        if (string.IsNullOrWhiteSpace(charName) || xp <= 0) return;   // échantillonné même hors botting (série éparse : que si changé)
        if (_lastXp.TryGetValue(charName!, out var last) && last == xp) return;
        _lastXp[charName!] = xp;
        var d = Get(charName!);
        lock (d) d.XpSamples.Add(new[] { Now, xp });
        MarkDirty(charName!);
    }

    /// <summary>Échantillonne les pods COURANTS d'un perso (au heartbeat). Enregistre si changé -> pods récoltés/heure
    /// (somme des HAUSSES entre échantillons ; une baisse = passage banque, ignorée).</summary>
    public void SamplePods(string? charName, int pods)
    {
        if (string.IsNullOrWhiteSpace(charName) || pods < 0) return;
        if (_lastPods.TryGetValue(charName!, out var last) && last == pods) return;
        _lastPods[charName!] = pods;
        var d = Get(charName!);
        lock (d) d.PodsSamples.Add(new[] { Now, (long)pods });
        MarkDirty(charName!);
    }

    private CharData? Resolve(uint pid) => _pidToChar.TryGetValue(pid, out var name) ? Get(name) : null;

    private CharData Get(string name) => _byChar.GetOrAdd(name, Load);

    // -------- Botting (session de script) : appele par le VM au Lancer / Arreter + heartbeat periodique --------

    /// <summary>Debut d'une session de botting (script lance) pour ce perso.</summary>
    public void StartBotting(string? charName)
    {
        if (string.IsNullOrWhiteSpace(charName)) return;
        _openSession[charName!] = Now;
    }

    /// <summary>Heartbeat : prolonge la session ouverte (persiste l'intervalle courant pour survivre a un crash).</summary>
    public void BottingHeartbeat(string? charName)
    {
        if (string.IsNullOrWhiteSpace(charName) || !_openSession.TryGetValue(charName!, out var start)) return;
        CommitSession(charName!, start, Now, keepOpen: true);
    }

    /// <summary>Fin de session de botting (script arrete / termine).</summary>
    public void StopBotting(string? charName)
    {
        if (string.IsNullOrWhiteSpace(charName) || !_openSession.TryRemove(charName!, out var start)) return;
        CommitSession(charName!, start, Now, keepOpen: false);
    }

    // Ecrit/prolonge la DERNIERE session si elle correspond au meme debut, sinon en cree une. keepOpen -> on garde
    // _openSession pour continuer a la prolonger (heartbeat). Fusionne pour ne pas exploser le nombre d'intervalles.
    private void CommitSession(string charName, long start, long end, bool keepOpen)
    {
        var d = Get(charName);
        lock (d)
        {
            if (d.Sessions.Count > 0 && d.Sessions[^1][0] == start) d.Sessions[^1][1] = end;
            else d.Sessions.Add(new[] { start, end });
        }
        MarkDirty(charName);
    }

    private void MarkDirty(uint pid) { if (_pidToChar.TryGetValue(pid, out var name)) MarkDirty(name); }
    // TryAdd -> conserve l'instant de la PREMIERE modif non sauvee (ne le repousse pas a chaque event).
    private void MarkDirty(string name)
    {
        _dirtySince.TryAdd(name, DateTime.UtcNow);
        _revision.AddOrUpdate(name, 1L, static (_, v) => v + 1);   // invalide le snapshot memoise de ce perso
    }

    // Revision des donnees d'un perso : incrementee par CHAQUE mutation (MarkDirty est leur point de passage unique).
    // Sert de cle au memo de Compute ci-dessous.
    private readonly ConcurrentDictionary<string, long> _revision = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (long Rev, StatsPeriod Period, DateTime? Day, int Points, long To, StatsSnapshot Snap)> _snapCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Agregation memoisee. Compute balaie les 11 collections du perso : 138 229 elements et 136 ms mesures sur
    /// Bastionbot (68 525 visites de carte), et la boucle du minuteur de l'UI l'appelle a CHAQUE tick -> l'onglet Stats
    /// saccadait en continu et l'ouverture d'une fiche payait ce balayage. Le resultat ne depend que de (revision des
    /// donnees, periode, jour, nb de points) : on le rend tel quel tant que rien n'a change. La borne To de la fenetre
    /// glissante avance avec l'horloge, donc on recalcule aussi des qu'elle a bouge d'une seconde.</summary>
    public StatsSnapshot Compute(string? charName, StatsPeriod period, DateTime? day = null, int seriesPoints = 48)
    {
        if (string.IsNullOrWhiteSpace(charName)) return ComputeCore(charName, period, day, seriesPoints);
        long rev = _revision.GetValueOrDefault(charName!);
        long to = Window(period, day).To;
        if (_snapCache.TryGetValue(charName!, out var hit)
            && hit.Rev == rev && hit.Period == period && hit.Day == day && hit.Points == seriesPoints
            && Math.Abs(to - hit.To) < 1_000)
            return hit.Snap;
        var fresh = ComputeCore(charName, period, day, seriesPoints);
        _snapCache[charName!] = (rev, period, day, seriesPoints, to, fresh);
        return fresh;
    }

    /// <summary>A appeler periodiquement (VM) : persiste les persos modifies depuis &gt;= SaveDebounce (sauvegarde reguliere
    /// meme en farm continu).</summary>
    public void FlushDue()
    {
        foreach (var kv in _dirtySince.ToArray())
            if (DateTime.UtcNow - kv.Value >= SaveDebounce && _dirtySince.TryRemove(kv.Key, out _))
                Save(kv.Key);
        SavePrices();
    }

    /// <summary>Persiste TOUT ce qui est en attente (a appeler a la fermeture de l'app pour ne rien perdre).</summary>
    public void FlushAll()
    {
        // Ferme les sessions de botting ouvertes puis sauve tous les persos connus.
        foreach (var kv in _openSession.ToArray()) CommitSession(kv.Key, kv.Value, Now, keepOpen: true);
        foreach (var name in _byChar.Keys.ToArray()) { _dirtySince.TryRemove(name, out _); Save(name); }
        SavePrices();
    }

    // -------- Agregation a la demande --------

    /// <summary>Fenetre [from, to] (ms epoch) : soit un jour LOCAL precis (<paramref name="day"/>), soit la periode
    /// glissante finissant maintenant. Base commune au calcul des stats ET a l'export CSV.</summary>
    public static (long From, long To) Window(StatsPeriod period, DateTime? day)
    {
        if (day is { } dd)
        {
            var start = dd.Date;
            return (new DateTimeOffset(start, TimeZoneInfo.Local.GetUtcOffset(start)).ToUnixTimeMilliseconds(),
                    new DateTimeOffset(start.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(start.AddDays(1))).ToUnixTimeMilliseconds());
        }
        long now = Now;
        long span = period switch
        {
            StatsPeriod.Day => 86_400_000L,
            StatsPeriod.Week => 7 * 86_400_000L,
            StatsPeriod.Month => 30 * 86_400_000L,
            _ => 365 * 86_400_000L,
        };
        return (now - span, now);
    }

    private StatsSnapshot ComputeCore(string? charName, StatsPeriod period, DateTime? day, int seriesPoints)
    {
        var snap = new StatsSnapshot();
        if (string.IsNullOrWhiteSpace(charName)) return snap;
        var d = Get(charName!);   // charge le fichier persiste au 1er acces -> les stats des sessions precedentes apparaissent

        var (from, to) = Window(period, day);
        long span = Math.Max(1, to - from);

        long[][] harvests; string[] names; long[][] fights; long[][] sessions; long[][] kamas; long[][] xp; long[][] mapVisits; long[] bankVisits; long[] deaths; long[][] pods; long[][] frags;
        lock (d)
        {
            harvests = d.Harvests.ToArray(); names = d.HarvestNames.ToArray();
            fights = d.Fights.ToArray(); sessions = d.Sessions.ToArray();
            kamas = d.KamasSamples.ToArray(); xp = d.XpSamples.ToArray(); mapVisits = d.MapVisits.ToArray(); bankVisits = d.BankVisits.ToArray();
            deaths = d.Deaths.ToArray(); pods = d.PodsSamples.ToArray(); frags = d.Fragments.ToArray();
        }
        // La session en cours (non encore committee) doit apparaitre live dans le total (clippee a [from, to]).
        if (_openSession.TryGetValue(charName!, out var openStart))
            sessions = sessions.Append(new[] { openStart, to }).ToArray();

        var recent = new List<StatsAction>();
        var totals = new Dictionary<string, (int Qty, int Icon)>();
        for (int i = 0; i < harvests.Length; i++)
        {
            if (harvests[i][0] < from || harvests[i][0] >= to) continue;
            snap.Harvests++;
            int q = (int)harvests[i][1];
            snap.ResourcesQty += q;
            if (harvests[i].Length > 3 && _gidPrice.TryGetValue(harvests[i][3], out var unit) && unit > 0)
                snap.EstimatedValue += (long)q * unit;   // valeur estimée = qté × prix HDV moyen connu du gid
            string n = i < names.Length && !string.IsNullOrEmpty(names[i]) ? names[i] : Loc.T("L.Stats.Resource");
            int icon = harvests[i].Length > 2 ? (int)harvests[i][2] : 0;
            recent.Add(new StatsAction(harvests[i][0], false, Loc.Fmt("L.Stats.Harvest", n), q, icon));
            var cur = totals.GetValueOrDefault(n);
            totals[n] = (cur.Qty + q, icon != 0 ? icon : cur.Icon);
        }
        snap.ResourceTotals = totals.OrderByDescending(kv => kv.Value.Qty)
            .Select(kv => new ResourceTotal(kv.Key, kv.Value.Qty, kv.Value.Icon)).ToList();
        long fightTotal = 0;
        foreach (var f in fights)
        {
            if (f[0] < from || f[0] >= to) continue;
            snap.Fights++;
            fightTotal += f[1];
            if (f.Length > 2) { if (f[2] == 1) snap.FightsWon++; else if (f[2] == -1) snap.FightsLost++; }
            recent.Add(new StatsAction(f[0], true, Loc.T("L.Stats.Fight"), 0));
        }
        snap.AvgFightSec = snap.Fights > 0 ? fightTotal / 1000.0 / snap.Fights : 0;

        // Temps de botting = union des intervalles de session clippes a [from, to].
        double bottingMs = 0;
        foreach (var (s0, e0) in MergeIntervals(sessions))
        {
            long s = Math.Max(s0, from), e = Math.Min(e0, to);
            if (e > s) bottingMs += e - s;
        }
        snap.BottingSec = bottingMs / 1000.0;

        // Serie cumulee (heures) a N points egaux sur la fenetre -> pour la courbe.
        var merged = MergeIntervals(sessions);
        var series = new double[seriesPoints];
        for (int p = 0; p < seriesPoints; p++)
        {
            long t = from + (long)((double)(p + 1) / seriesPoints * span);
            double cumMs = 0;
            foreach (var (s0, e0) in merged) { long s = Math.Max(s0, from), e = Math.Min(e0, t); if (e > s) cumMs += e - s; }
            series[p] = cumMs / 3_600_000.0;
        }
        snap.BottingSeriesHours = series;

        foreach (var t in deaths) if (t >= from && t < to) snap.Deaths++;
        FillAdvanced(snap, kamas, xp, mapVisits, bankVisits, harvests, pods, from, to);
        foreach (var f in frags) if (f.Length >= 2 && f[0] >= from && f[0] < to) snap.AnomalyFragments += (int)f[1];
        if (snap.BottingSec > 0) snap.AnomalyFragmentsPerHour = snap.AnomalyFragments / (snap.BottingSec / 3600.0);

        snap.Recent = recent.OrderByDescending(a => a.Ts).Take(40).ToList();
        return snap;
    }

    // Net (dernier - premier échantillon) d'une série [ts, valeur] clippée à [from, to].
    private static long NetOverWindow(long[][] samples, long from, long to)
    {
        long? first = null, last = null;
        foreach (var s in samples.Where(s => s.Length >= 2 && s[0] >= from && s[0] < to).OrderBy(s => s[0]))
        { first ??= s[1]; last = s[1]; }
        return first is long f && last is long l ? l - f : 0;
    }

    // Métriques avancées : kamas/XP nets + taux/heure + passages banque + heatmap par carte, toutes clippées à [from, to].
    private static void FillAdvanced(StatsSnapshot snap, long[][] kamas, long[][] xp, long[][] mapVisits, long[] bankVisits, long[][] harvests, long[][] pods, long from, long to)
    {
        snap.KamasGained = NetOverWindow(kamas, from, to);
        snap.XpGained = NetOverWindow(xp, from, to);

        foreach (var t in bankVisits) if (t >= from && t < to) snap.BankVisits++;

        // Pods RÉCOLTÉS = somme des HAUSSES de pods entre échantillons consécutifs de la fenêtre (une baisse = passage banque, ignorée).
        long? prevPods = null;
        foreach (var p in pods.Where(p => p.Length >= 2 && p[0] >= from && p[0] < to).OrderBy(p => p[0]))
        {
            if (prevPods is long pp && p[1] > pp) snap.PodsGenerated += p[1] - pp;
            prevPods = p[1];
        }

        double hours = snap.BottingSec / 3600.0;
        if (hours > 0)
        {
            snap.HarvestsPerHour = snap.Harvests / hours;
            snap.ResourcesPerHour = snap.ResourcesQty / hours;
            snap.FightsPerHour = snap.Fights / hours;
            snap.KamasPerHour = snap.KamasGained / hours;
            snap.XpPerHour = snap.XpGained / hours;
            snap.EstimatedValuePerHour = snap.EstimatedValue / hours;
            snap.PodsPerHour = snap.PodsGenerated / hours;
        }

        // Heatmap : par carte -> passages (entrées dans la fenêtre), temps (jusqu'au prochain changement, plafonné) et
        // récoltes attribuées à l'intervalle. Deux-pointeurs sur les récoltes triées -> O(visites + récoltes).
        var visits = mapVisits.Where(v => v.Length >= 4).OrderBy(v => v[0]).ToArray();
        if (visits.Length == 0) return;
        var ht = harvests.Where(h => h.Length >= 1 && h[0] >= from && h[0] < to).Select(h => h[0]).OrderBy(t => t).ToArray();
        int hp = 0;
        var agg = new Dictionary<string, (int Passages, long TimeMs, int Harvests)>();
        for (int i = 0; i < visits.Length; i++)
        {
            long ts = visits[i][0];
            if (ts >= to) break;
            long nextTs = i + 1 < visits.Length ? visits[i + 1][0] : to;
            long hi = Math.Min(nextTs, to);
            while (hp < ht.Length && ht[hp] < ts) hp++;
            int hv = 0;
            while (hp < ht.Length && ht[hp] < hi) { hv++; hp++; }
            if (ts < from) continue;   // visite commencée avant la fenêtre -> pas un passage de la période
            long dwell = Math.Min(Math.Max(0, hi - ts), MapDwellCapMs);
            string key = visits[i][2] != CoordUnknown && visits[i][3] != CoordUnknown ? $"{visits[i][2]},{visits[i][3]}" : visits[i][1].ToString();
            var cur = agg.GetValueOrDefault(key);
            agg[key] = (cur.Passages + 1, cur.TimeMs + dwell, cur.Harvests + hv);
        }
        snap.Heatmap = agg.OrderByDescending(kv => kv.Value.TimeMs).ThenByDescending(kv => kv.Value.Passages)
            .Select(kv => new HeatmapRow(kv.Key, kv.Value.Passages, kv.Value.Harvests, kv.Value.Passages > 0 ? kv.Value.TimeMs / 1000.0 / kv.Value.Passages : 0))
            .Take(60).ToList();
    }

    /// <summary>TOUTES les actions (recoltes + combats, non plafonnees) de la fenetre, pour l'export CSV. Label = nom
    /// BRUT de la ressource (non prefixe) ; pour un combat, Qty = duree en secondes.</summary>
    public IReadOnlyList<StatsAction> ActionsInWindow(string? charName, long from, long to)
    {
        var list = new List<StatsAction>();
        if (string.IsNullOrWhiteSpace(charName)) return list;
        var d = Get(charName!);
        long[][] harvests; string[] names; long[][] fights;
        lock (d) { harvests = d.Harvests.ToArray(); names = d.HarvestNames.ToArray(); fights = d.Fights.ToArray(); }
        for (int i = 0; i < harvests.Length; i++)
        {
            if (harvests[i][0] < from || harvests[i][0] >= to) continue;
            string n = i < names.Length && !string.IsNullOrEmpty(names[i]) ? names[i] : Loc.T("L.Stats.Resource");
            int icon = harvests[i].Length > 2 ? (int)harvests[i][2] : 0;
            list.Add(new StatsAction(harvests[i][0], false, n, (int)harvests[i][1], icon));
        }
        foreach (var f in fights)
        {
            if (f[0] < from || f[0] >= to) continue;
            list.Add(new StatsAction(f[0], true, Loc.T("L.Stats.Fight"), (int)(f[1] / 1000)));
        }
        return list.OrderByDescending(a => a.Ts).ToList();
    }

    /// <summary>Jours LOCAUX (minuit) ayant au moins une donnée (récolte, combat ou session de botting), du + récent au +
    /// ancien. Pour le sélecteur « jour précis » de l'onglet Stats : on ne propose QUE des jours avec des données.</summary>
    public IReadOnlyList<DateTime> DaysWithData(string? charName)
    {
        if (string.IsNullOrWhiteSpace(charName)) return Array.Empty<DateTime>();
        var d = Get(charName!);
        long[][] harvests, fights, sessions;
        lock (d) { harvests = d.Harvests.ToArray(); fights = d.Fights.ToArray(); sessions = d.Sessions.ToArray(); }
        var days = new SortedSet<DateTime>();
        static DateTime Day(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.Date;
        foreach (var h in harvests) if (h.Length >= 1) days.Add(Day(h[0]));
        foreach (var f in fights) if (f.Length >= 1) days.Add(Day(f[0]));
        foreach (var s in sessions)
            if (s.Length >= 2)
                for (var day = Day(s[0]); day <= Day(s[1]); day = day.AddDays(1)) days.Add(day);   // couvre chaque jour couvert par la session
        return days.Reverse().ToList();   // le + récent d'abord
    }

    private static List<(long Start, long End)> MergeIntervals(long[][] raw)
    {
        var list = raw.Where(x => x.Length >= 2 && x[1] > x[0]).Select(x => (x[0], x[1])).OrderBy(x => x.Item1).ToList();
        var merged = new List<(long Start, long End)>();
        foreach (var (s, e) in list)
        {
            if (merged.Count > 0 && s <= merged[^1].End) { if (e > merged[^1].End) merged[^1] = (merged[^1].Start, e); }
            else merged.Add((s, e));
        }
        return merged;
    }

    // -------- Persistance --------

    private CharData Load(string name)
    {
        try
        {
            var path = PathFor(name);
            if (File.Exists(path))
            {
                var d = JsonSerializer.Deserialize(File.ReadAllText(path), StatsJson.Default.CharData);
                if (d is not null) { Prune(d); return d; }
            }
        }
        catch { /* fichier corrompu -> on repart propre */ }
        return new CharData();
    }

    private void Save(string name)
    {
        if (!_byChar.TryGetValue(name, out var d)) return;
        CharData copy;
        lock (d)
        {
            Prune(d);
            copy = new CharData
            {
                Harvests = d.Harvests.ToList(), HarvestNames = d.HarvestNames.ToList(),
                Fights = d.Fights.ToList(), Sessions = d.Sessions.ToList(),
                KamasSamples = d.KamasSamples.ToList(), XpSamples = d.XpSamples.ToList(),
                MapVisits = d.MapVisits.ToList(), BankVisits = d.BankVisits.ToList(),
                Deaths = d.Deaths.ToList(), PodsSamples = d.PodsSamples.ToList(), Fragments = d.Fragments.ToList(),
            };
        }
        try { lock (_ioLock) AtomicJson.Write(PathFor(name), JsonSerializer.Serialize(copy, StatsJson.Default.CharData)); }
        catch { /* best-effort */ }
    }

    private static void Prune(CharData d)
    {
        long cutoff = Now - (long)RetentionDays * 86_400_000L;
        // Emonde recoltes + noms de facon SYNCHRONE (memes index).
        for (int i = d.Harvests.Count - 1; i >= 0; i--)
            if (d.Harvests[i][0] < cutoff) { d.Harvests.RemoveAt(i); if (i < d.HarvestNames.Count) d.HarvestNames.RemoveAt(i); }
        d.Fights.RemoveAll(f => f[0] < cutoff);
        d.Sessions.RemoveAll(s => s[1] < cutoff);
        d.KamasSamples.RemoveAll(k => k.Length < 1 || k[0] < cutoff);
        d.XpSamples.RemoveAll(x => x.Length < 1 || x[0] < cutoff);
        d.MapVisits.RemoveAll(v => v.Length < 1 || v[0] < cutoff);
        d.BankVisits.RemoveAll(t => t < cutoff);
        d.Deaths.RemoveAll(t => t < cutoff);
        d.PodsSamples.RemoveAll(p => p.Length < 1 || p[0] < cutoff);
        d.Fragments.RemoveAll(f => f.Length < 1 || f[0] < cutoff);
    }

    private string PathFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_dir, safe + ".json");
    }
}
