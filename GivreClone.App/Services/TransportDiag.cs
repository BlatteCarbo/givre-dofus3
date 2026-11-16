using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia.Threading;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>
/// Redirige les lignes de diagnostic du transport (HandshakeRelay / mode Socket via TransportLog.EmittedWithPid) vers deux
/// sorties : un fichier TROUVABLE (%APPDATA%\GivreClone\logs\transport-&lt;date&gt;.log, UN PAR SESSION) ET des collections
/// observables pour la Console in-app (globale + par bot).
///
/// PERF (mode Socket, 15-30 comptes verbeux) : l'émetteur est souvent le fil de LECTURE d'une connexion de jeu — il ne doit
/// JAMAIS attendre le disque ni l'UI (sinon accusés de combat / réponses de séquence en retard). Write() ne fait donc que
/// formater et METTRE EN FILE : un écrivain de fond unique garde le fichier OUVERT et vide par lots toutes les 250 ms, et
/// l'UI est alimentée par lots par une minuterie (au lieu d'un Post + File.AppendAllText sous verrou global PAR LIGNE).
/// Verbosité inchangée.
/// </summary>
public static class TransportDiag
{
    private static readonly object _gate = new();
    private static bool _init;
    private const int MaxLines = 200;

    // Écriture disque (user : utile pour debug). UN FICHIER PAR SESSION horodaté dans logs\, purge auto des vieilles
    // sessions (garde les KeepSessions plus récentes), borne MaxFileBytes/fichier (au-delà -> nouveau segment).
    private static readonly string LogDir = System.IO.Path.Combine(MZP19LocalClone.DataRoot, "logs");
    private const long MaxFileBytes = 25L * 1024 * 1024;   // 25 Mo par fichier avant nouveau segment
    private const int KeepSessions = 20;                   // nb de journaux transport-*.log conservés
    private const int FlushIntervalMs = 250;               // vidage disque ET UI par lots
    private const int UiBatchMax = 500;                    // lignes max appliquées à l'UI par passe (le reste attend la suivante)
    private static string _path = "";                      // fichier de la session courante
    private static long _fileBytes;
    private static StreamWriter? _writer;                  // fichier OUVERT (écrivain de fond uniquement)
    private static Thread? _diskThread;
    private static string? _diskError;
    private static long _lastDiskFlushTicks;

    // Snapshot sans verrou disque : un écrivain bloqué ne doit pas bloquer le diagnostic MCP.
    public sealed record DiskHealth(bool Initialized, bool WorkerAlive, string File, int PendingLines,
        string? LastErrorType, string? LastFlushUtc, long BytesPerMinute = 0, bool GrowthAlert = false);
    // 25 Mo/min fills a segment every minute: 20 sessions of history would be rotated away in about 20 minutes.
    private static readonly LogGrowthMonitor _growth = new(TimeSpan.FromMinutes(1), MaxFileBytes);
    public static DiskHealth DiskStatus()
    {
        long now = DateTime.UtcNow.Ticks, rate; bool alert;
        lock (_growth) { rate = _growth.BytesPerMinute(now); alert = _growth.Alert(now); }
        return new(_init, _diskThread?.IsAlive ?? false,
            System.IO.Path.GetFileName(_path), _diskPending.Count, Volatile.Read(ref _diskError),
            Interlocked.Read(ref _lastDiskFlushTicks) is > 0 and var ticks
                ? new DateTime(ticks, DateTimeKind.Utc).ToString("O") : null, rate, alert);
    }

    private static readonly ConcurrentQueue<string> _diskPending = new();
    private static readonly ConcurrentQueue<(uint Pid, string Line)> _uiPending = new();
    private static readonly AutoResetEvent _diskSignal = new(false);
    private static DispatcherTimer? _uiTimer;

    /// <summary>Lignes de diagnostic live GLOBALES (tous bots), bornees, marshalees sur le thread UI.</summary>
    public static ObservableCollection<string> Lines { get; } = new();

    // Lignes PAR BOT (pid) : la Console de l'onglet est filtree au bot selectionne. Cree a la demande.
    private static readonly ConcurrentDictionary<uint, ObservableCollection<string>> _perPid = new();
    // Bots retirés (Forget) : leurs lignes tardives (fin de session socket loguée après le Stop) ne recréent pas de collection.
    private static readonly ConcurrentDictionary<uint, byte> _forgotten = new();

    /// <summary>Collection observable des lignes du bot `pid` (creee a la demande). Pour la Console par-bot.</summary>
    public static ObservableCollection<string> LinesFor(uint pid)
    {
        _forgotten.TryRemove(pid, out _);   // redemandée explicitement (bot relancé sur ce pid) -> de nouveau alimentée
        return _perPid.GetOrAdd(pid, static _ => new ObservableCollection<string>());
    }

    /// <summary>Fichier de journal de la SESSION courante (%APPDATA%\GivreClone\logs\transport-&lt;date&gt;.log).</summary>
    public static string Path_ => _path;

    public static void Init()
    {
        lock (_gate)
        {
            if (_init) return;
            _init = true;
            NewSessionFile();   // ouvre logs\transport-<date>.log pour CETTE session + purge les vieilles
            CleanupLegacy();    // supprime l'ancien transport.log (racine) qui accumulait toutes les sessions
            var t = new Thread(DiskLoop) { IsBackground = true, Name = "transport-log", Priority = ThreadPriority.BelowNormal };
            _diskThread = t;
            t.Start();
            try
            {
                _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(FlushIntervalMs), DispatcherPriority.Background, (_, _) => DrainUi());
                _uiTimer.Start();
            }
            catch { /* pas d'UI (headless) : fichier seulement */ }
            TransportLog.Enabled = true;
            TransportLog.EmittedWithPid += Write;   // porte le pid -> Console globale ET par-bot
            LicenseService.PermissionsChanged += PurgeLegacyCodeLogsOnce;
        }
        Write(0, "=== GivreClone : diagnostic transport demarre ===");
    }

    /// <summary>Vide immédiatement la file disque (fermeture de l'app) : aucune ligne de fin de session perdue.</summary>
    public static void Flush()
    {
        try { lock (_gate) FlushDiskLocked(); } catch { /* le diagnostic ne doit jamais casser l'arrêt */ }
    }

    /// <summary>Émet une ligne APPLICATIVE dans la Console d'un bot (disque + onglet Console) — ex. l'écho des
    /// commandes console `/xxx` tapées dans la barre de l'onglet Console. Même chemin que le firehose transport.</summary>
    public static void Emit(uint pid, string message) => Write(pid, message);

    /// <summary>Vide la Console globale. (La Console par-bot se vide via ClearFor.)</summary>
    public static void Clear() => Dispatcher.UIThread.Post(Lines.Clear);

    /// <summary>Vide la Console d'un bot precis.</summary>
    public static void ClearFor(uint pid) { if (_perPid.TryGetValue(pid, out var c)) Dispatcher.UIThread.Post(c.Clear); }

    /// <summary>Libère la collection de lignes d'un bot retiré (évite l'accumulation sans fin de _perPid).</summary>
    public static void Forget(uint pid)
    {
        _forgotten[pid] = 0;
        _perPid.TryRemove(pid, out _);
    }

    /// <summary>« Reconnecter » MITM en place : la Console du bot suit son NOUVEAU pid, historique conservé (lignes déjà
    /// reçues pour le nouveau pid ajoutées à la suite). L'ancien pid est marqué oublié (ses lignes tardives ne recréent rien).
    /// Thread UI, AVANT que la ligne du compte ne publie le nouveau pid.</summary>
    public static void Rekey(uint oldPid, uint newPid)
    {
        if (oldPid == newPid) return;
        DrainUi();   // dernières lignes de l'ancien pid livrées dans SA collection avant qu'il ne soit marqué oublié
        _forgotten[oldPid] = 0;
        if (!_perPid.TryRemove(oldPid, out var c)) return;
        _forgotten.TryRemove(newPid, out _);
        if (_perPid.TryRemove(newPid, out var early)) foreach (var l in early) c.Add(l);
        for (int excess = c.Count - MaxLines; excess > 0; excess--) c.RemoveAt(0);
        _perPid[newPid] = c;
    }

    private static void Write(uint pid, string message)
    {
        // Filet de sécurité (licence non développeur) : aucun dump de reverse ni URL de type « type.ankama.com/<code> » ne doit
        // atteindre la Console ou transport-*.log, même si un appelant oublie sa garde.
        if (!LicenseService.IsDeveloper)
        {
            if (message.StartsWith("[pkt-dbg]", StringComparison.Ordinal) || message.StartsWith("[combat-dbg]", StringComparison.Ordinal)) return;
            if (message.Contains("type.ankama.com/", StringComparison.Ordinal)) message = MaskTypeUrls(message);
        }
        var now = DateTime.Now;
        var line = $"{now:HH:mm:ss.fff}  {message}";
        // Fichier : le pid (ou « - » pour l'infra) sur CHAQUE ligne -> un journal à 30 comptes entrelacés reste exploitable.
        // NB : contenu en clair (corps de MP, corps d'auth) -> reste local à la machine du user.
        _diskPending.Enqueue(pid != 0 ? $"{now:HH:mm:ss.fff}  <{pid}> {message}" : line);
        if (_diskPending.Count > 2048) _diskSignal.Set();   // rafale : vider sans attendre le prochain tick
        _uiPending.Enqueue((pid, line));
    }

    // Journaux écrits par les versions ≤ 1.1.7 : codes du protocole en clair pour TOUTES les licences. Supprimés une seule fois,
    // dès qu'une licence NON développeur est authentifiée (un développeur garde son historique). Marqueur dans logs\.
    // Garde-fous « développeur » : une licence développeur vue UNE fois sur ce poste (marqueur .dev) protège définitivement ses
    // journaux ; la purge n'a lieu que sur une licence active (droits normaux présents, pas un statut suspendu/expiré où TOUS
    // les droits tombent) et seulement si ce n'est toujours pas un développeur 60 s plus tard (pas d'état transitoire).
    private static int _legacyPurge;
    private static bool ActiveNonDev() => LicenseService.IsAuthorized && !LicenseService.IsDeveloper
        && (LicenseService.HasPermission(LicenseService.Perm.Gather) || LicenseService.HasPermission(LicenseService.Perm.Fight));
    private static void PurgeLegacyCodeLogsOnce()
    {
        string devMarker = System.IO.Path.Combine(LogDir, ".dev");
        if (LicenseService.IsDeveloper)
        {
            try { if (!File.Exists(devMarker)) { Directory.CreateDirectory(LogDir); File.WriteAllText(devMarker, ""); } } catch { }
            return;
        }
        if (!ActiveNonDev()) return;
        if (Interlocked.CompareExchange(ref _legacyPurge, 1, 0) != 0) return;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(60_000);
                if (!ActiveNonDev()) { Interlocked.Exchange(ref _legacyPurge, 0); return; }
                string marker = System.IO.Path.Combine(LogDir, ".purge-1.1.8");
                if (File.Exists(marker) || File.Exists(devMarker)) return;
                string current = _path;
                foreach (var f in Directory.EnumerateFiles(LogDir, "transport-*.log"))
                    if (!string.Equals(f, current, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(f); } catch { /* ouvert ailleurs : ignoré */ } }
                File.WriteAllText(marker, "");
            }
            catch { /* best-effort */ }
        });
    }

    private static string MaskTypeUrls(string s)
    {
        const string P = "type.ankama.com/";
        var sb = new StringBuilder(s.Length);
        int i = 0;
        for (int at; (at = s.IndexOf(P, i, StringComparison.Ordinal)) >= 0; )
        {
            sb.Append(s, i, at - i).Append(P).Append('•');
            i = at + P.Length;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.')) i++;
        }
        return sb.Append(s, i, s.Length - i).ToString();
    }

    // Écrivain de fond UNIQUE : garde le fichier ouvert, vide la file par lots (toutes les 250 ms ou sur rafale).
    private static void DiskLoop()
    {
        while (true)
        {
            try
            {
                _diskSignal.WaitOne(FlushIntervalMs);
                lock (_gate) FlushDiskLocked();
            }
            catch (Exception e) { Volatile.Write(ref _diskError, e.GetType().Name); }
        }
    }

    // Sous _gate.
    private static void FlushDiskLocked()
    {
        if (_diskPending.IsEmpty) return;
        if (_path.Length == 0) NewSessionFile();
        long flushed = 0;
        while (_diskPending.TryDequeue(out var l))
        {
            long add = Encoding.UTF8.GetByteCount(l) + Environment.NewLine.Length;
            if (_fileBytes + add > MaxFileBytes) NewSessionFile();   // segment plein -> nouveau fichier
            if (_writer is null) OpenWriter();
            if (_writer is null) continue;                           // dossier inaccessible : la Console reste alimentée
            _writer.WriteLine(l);
            _fileBytes += add;
            flushed += add;
        }
        _writer?.Flush();
        if (_writer is not null)
        {
            lock (_growth) _growth.Record(DateTime.UtcNow.Ticks, flushed);
            Interlocked.Exchange(ref _lastDiskFlushTicks, DateTime.UtcNow.Ticks);
            Volatile.Write(ref _diskError, null);
        }
    }

    // Applique les lignes en attente aux collections observables (thread UI, par lots). Borne chaque collection d'un coup.
    private static void DrainUi()
    {
        if (_uiPending.IsEmpty) return;
        var touched = new HashSet<ObservableCollection<string>>();
        for (int i = 0; i < UiBatchMax && _uiPending.TryDequeue(out var e); i++)
        {
            Lines.Add(e.Line);
            touched.Add(Lines);
            if (e.Pid != 0 && !_forgotten.ContainsKey(e.Pid))   // pid 0 = infra (auth/mapping) : global seulement
            {
                var c = _perPid.GetOrAdd(e.Pid, static _ => new ObservableCollection<string>());
                c.Add(e.Line);
                touched.Add(c);
            }
        }
        foreach (var c in touched)
            for (int excess = c.Count - MaxLines; excess > 0; excess--) c.RemoveAt(0);
    }

    // Ouvre un nouveau fichier de session horodaté (logs\transport-yyyy-MM-dd_HH-mm-ss[.N].log) + purge les vieux. Sous _gate.
    private static void NewSessionFile()
    {
        try { _writer?.Flush(); _writer?.Dispose(); } catch { }
        _writer = null;
        try
        {
            Directory.CreateDirectory(LogDir);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var p = System.IO.Path.Combine(LogDir, $"transport-{stamp}.log");
            for (int n = 2; File.Exists(p); n++) p = System.IO.Path.Combine(LogDir, $"transport-{stamp}.{n}.log");   // collision segment (<1s)
            _path = p;
            Prune();
        }
        catch { /* best-effort : dossier inaccessible -> la Console in-app reste alimentée */ }
        _fileBytes = 0;
    }

    // Fichier partagé en lecture (on peut l'ouvrir/le copier pendant que l'app tourne). Sous _gate.
    private static void OpenWriter()
    {
        try
        {
            var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            _writer = new StreamWriter(fs, new UTF8Encoding(false), 64 * 1024) { AutoFlush = false };
        }
        catch (Exception e) { _writer = null; Volatile.Write(ref _diskError, e.GetType().Name); }
    }

    // Ne garde que les KeepSessions journaux transport-*.log les plus récents (le nom horodaté trie chronologiquement).
    private static void Prune()
    {
        try
        {
            var files = Directory.GetFiles(LogDir, "transport-*.log");
            System.Array.Sort(files, System.StringComparer.Ordinal);   // croissant = du plus vieux au plus récent
            for (int i = 0; i < files.Length - KeepSessions; i++) { try { File.Delete(files[i]); } catch { } }
        }
        catch { /* best-effort */ }
    }

    // Supprime l'ancien journal UNIQUE (racine %APPDATA%\GivreClone\transport.log[.1]) qui mélangeait toutes les sessions.
    private static void CleanupLegacy()
    {
        try
        {
            var root = System.IO.Path.Combine(MZP19LocalClone.DataRoot, "transport.log");
            foreach (var old in new[] { root, root + ".1" }) if (File.Exists(old)) { try { File.Delete(old); } catch { } }
        }
        catch { /* best-effort */ }
    }
}
