using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Combat;

/// <summary>Branche le replay de combat sur le journal de transport (MITM et Socket : même flux « [combat] »), garde les derniers combats de chaque bot en mémoire et écrit chacun dans
/// <c>combat/&lt;date&gt;/&lt;heure&gt;_pid&lt;pid&gt;_&lt;issue&gt;.log</c> (conservé 30 jours). N'influence jamais le moteur : une erreur ici est avalée.</summary>
public static class CombatJournalService
{
    private const int KeepPerBot = 40, KeepDays = 30;
    private static readonly ConcurrentDictionary<uint, CombatJournalRecorder> Recorders = new();
    private static readonly ConcurrentDictionary<uint, List<FightJournal>> Recent = new();
    private static readonly Regex PidRx = new(@"\bpid (\d+)\b", RegexOptions.Compiled);
    private static bool _init;

    /// <summary>Un combat vient de se terminer (thread du journal : ne rien faire de long).</summary>
    public static event Action<FightJournal>? FightFinished;

    public static string Folder => Path.Combine(MZP19LocalClone.DataRoot, "combat");

    public static void Init()
    {
        if (_init) return;
        _init = true;
        try { CleanOld(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* dossier illisible : tant pis */ }
        TransportLog.EmittedWithPid += OnLine;
    }

    private static void OnLine(uint pid, string message)
    {
        try
        {
            if (!message.StartsWith("[combat]", StringComparison.Ordinal)
                && !message.StartsWith("[combat-cast]", StringComparison.Ordinal)
                && !message.StartsWith("[combat-planner]", StringComparison.Ordinal)
                && !message.StartsWith("[turbo-metric]", StringComparison.Ordinal)) return;
            if (pid == 0 && PidRx.Match(message) is { Success: true } m && uint.TryParse(m.Groups[1].Value, out var p)) pid = p;
            var rec = Recorders.GetOrAdd(pid, id => new CombatJournalRecorder(id));
            FightJournal? done;
            lock (rec) done = rec.Feed(DateTime.Now, message);
            if (done is not null) Store(done);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { /* le replay ne doit jamais gêner le combat */ }
    }

    /// <summary>Ferme les combats encore ouverts (arrêt de l'application).</summary>
    public static void FlushAll()
    {
        foreach (var rec in Recorders.Values)
        {
            FightJournal? done;
            lock (rec) done = rec.Flush("arrêt de l'application");
            if (done is not null) Store(done);
        }
    }

    private static void Store(FightJournal j)
    {
        var list = Recent.GetOrAdd(j.Pid, _ => new List<FightJournal>());
        lock (list) { list.Add(j); if (list.Count > KeepPerBot) list.RemoveAt(0); }
        try
        {
            string dir = Path.Combine(Folder, j.Start.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{j.Start:HHmmss}_pid{j.Pid}_{j.Outcome}.log"), CombatJournalFormat.ToText(j));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* disque indisponible : mémoire seule */ }
        FightFinished?.Invoke(j);
    }

    /// <summary>Derniers combats (le plus récent d'abord), d'un bot ou de tous.</summary>
    public static IReadOnlyList<FightJournal> List(uint? pid = null, int limit = 20)
    {
        IEnumerable<FightJournal> all = Recent.Where(kv => pid is null || kv.Key == pid).SelectMany(kv => { lock (kv.Value) return kv.Value.ToList(); });
        return all.OrderByDescending(j => j.Start).Take(Math.Clamp(limit, 1, 200)).ToList();
    }

    public static FightJournal? Get(string id) => Recent.Values.SelectMany(l => { lock (l) return l.ToList(); }).FirstOrDefault(j => j.Id == id);

    private static void CleanOld()
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var d in Directory.GetDirectories(Folder))
            if (DateTime.TryParse(Path.GetFileName(d), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) && (DateTime.Now - day).TotalDays > KeepDays)
                Directory.Delete(d, true);
    }
}
