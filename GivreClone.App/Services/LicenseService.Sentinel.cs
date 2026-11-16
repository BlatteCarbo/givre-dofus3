using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.License;
using GivreClone.App.Services.Security;

namespace GivreClone.App.Services;

// ===================== Canal "sentinel" : Givre Sentinel (anti-RE + télémétrie RMM, panel admin > Security) =====================
// SIGNALEMENT SEUL côté client. Le serveur range les signaux dans une file d'alertes (panel + Discord « Givre Sentinel »),
// re-score, et c'est un HUMAIN qui décide d'une action (suspension, kill, commande distante). Collecte étendue autorisée
// par CGU au titre d'un outil RMM/UEM/MDM interne (support + anti-piratage). cf docs/web-sentinel-rmm.md.
public static partial class LicenseService
{
    // Sel local pour les EMPREINTES (hwid16/user_h/machine_h sont HASHÉS en plus du nom en clair -> corrélation stable
    // dans le panel sans dépendre du nom affiché, utile si deux comptes ont le même alias). Clé publique = pas un secret.
    private const string SentinelSalt = "givre-sentinel-fp-2026";

    private static string _lastSentinelSig = "";
    private static long _nextInventoryTick;                     // battement d'inventaire : force une trame même si rien ne change
    private static readonly TimeSpan InventoryEvery = TimeSpan.FromMinutes(1);

    // Cadence du scan — pilotable à chaud par le serveur (commande REMOTE_CONFIG { "sentinel.scan_interval_s": N }).
    // Défaut 3 s : produit premium avec support en direct (l'owner demandait plus rapide que 15 s), bornes 1-60 s.
    private const int MinScanIntervalS = 1, MaxScanIntervalS = 60;
    private static int _scanIntervalS = 3;

    /// <summary>Règle la cadence du scan Sentinelle (en secondes, bornes 1-60). Appelable depuis le dispatcher de
    /// commandes signées (REMOTE_CONFIG), ou localement au besoin. 0/négatif = ignoré. Prend effet au prochain scan.</summary>
    public static void SetSentinelScanInterval(int seconds)
    {
        if (seconds <= 0) return;
        _scanIntervalS = Math.Clamp(seconds, MinScanIntervalS, MaxScanIntervalS);
    }

    // Boucle de scan. Une trame SIGNÉE part quand : (a) l'ensemble des signaux change (apparition/retrait d'un outil,
    // débogueur), OU (b) le battement d'inventaire tombe (rafraîchit perf/logiciels côté serveur, même sans alerte).
    // Best-effort : WS down -> on retente au prochain tour. SentinelService n'est appelé QU'ICI, jamais depuis l'UI.
    private static void StartSentinel(LicenseClient client, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var (sk, pk) = ClientKey();
                var pkHex = Convert.ToHexString(pk).ToLowerInvariant();
                _nextInventoryTick = Environment.TickCount64;   // 1er envoi au démarrage de session
                while (!ct.IsCancellationRequested)
                {
                    IReadOnlyList<SentinelService.Signal> signals;
                    try { signals = SentinelService.Scan(); } catch { signals = Array.Empty<SentinelService.Signal>(); }
                    string sig = SignalSig(signals);
                    bool changed = sig != _lastSentinelSig;
                    bool heartbeat = Environment.TickCount64 >= _nextInventoryTick;
                    if ((changed || heartbeat) && client.CanUpload)
                    {
                        SentinelInventory.Snapshot? inv = null;
                        try { inv = SentinelInventory.Collect(); } catch { }
                        var bytes = BuildSentinelPayloadBytes(signals, inv, pkHex);
                        var signature = Cryptographic.Ed25519.Signature(bytes, sk, pk);
                        if (await client.PushUploadAsync("sentinel", bytes, signature).ConfigureAwait(false))
                        {
                            _lastSentinelSig = sig;
                            _nextInventoryTick = Environment.TickCount64 + (long)InventoryEvery.TotalMilliseconds;
                        }
                    }
                    try { await Task.Delay(TimeSpan.FromSeconds(_scanIntervalS), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception e) { Logger.Warn($"Sentinelle arrêtée : {e.Message}"); }
        });
    }

    // Signature de contenu : codes + path + pid d'un signal (un même outil qui change de chemin DOIT re-trigger une trame).
    private static string SignalSig(IReadOnlyList<SentinelService.Signal> signals) =>
        string.Join(";", signals.Select(s => $"{s.Code}|{s.Path}|{s.Pid}").OrderBy(x => x, StringComparer.Ordinal));

    // JSON signé : { signals:[{code,sev,path?,pid?,window?,signed?}], score, device:{...}, inventory:{...}, session,
    // scan_interval_s, pubkey, nonce, server_time }. Les champs device étendus (user_name clair + perf) sont autorisés
    // par le cadre RMM/UEM -> ils aident le support à qualifier le poste sans deviner.
    private static byte[] BuildSentinelPayloadBytes(IReadOnlyList<SentinelService.Signal> signals, SentinelInventory.Snapshot? inv, string pubKeyHex)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            // -------- signals
            w.WriteStartArray("signals");
            foreach (var s in signals)
            {
                w.WriteStartObject();
                w.WriteString("code", s.Code);
                w.WriteNumber("sev", s.Severity);
                if (!string.IsNullOrEmpty(s.Path)) w.WriteString("path", Clip(s.Path, 260));
                if (s.Pid is int pid && pid > 0) w.WriteNumber("pid", pid);
                if (!string.IsNullOrEmpty(s.Window)) w.WriteString("window", Clip(s.Window, 120));
                if (s.Signed is bool si) w.WriteBoolean("signed", si);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // -------- score indicatif (max des sev)
            w.WriteNumber("score", signals.Count == 0 ? 0 : signals.Max(s => s.Severity));
            // -------- device : identité EN CLAIR (autorisée par CGU dans le cadre RMM) + EMPREINTES (corrélation stable)
            //                  + matériel + locale. Tout est optionnel côté serveur.
            w.WriteStartObject("device");
            w.WriteString("os", Clip(RuntimeInformation.OSDescription, 64));
            try { w.WriteString("os_version", Environment.OSVersion.Version.ToString()); } catch { }
            w.WriteString("arch", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
            w.WriteString("user_name", Clip(Safe(() => Environment.UserName), 64));           // CLAIR : support direct
            w.WriteString("machine_name", Clip(Safe(() => Environment.MachineName), 64));
            w.WriteString("user_h", HashShort(Safe(() => Environment.UserName)));              // + empreintes stables
            w.WriteString("machine_h", HashShort(Safe(() => Environment.MachineName)));
            try { w.WriteString("hwid16", Hwid.Compute()[..16]); } catch { }
            try { w.WriteNumber("cpu_cores", Environment.ProcessorCount); } catch { }
            try { w.WriteNumber("ram_mb", (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024)); } catch { }
            try { w.WriteString("locale", CultureInfo.CurrentUICulture.Name); } catch { }
            try { w.WriteString("tz", TimeZoneInfo.Local.Id); } catch { }
            w.WriteBoolean("elevated", SafeBool(IsElevated));
            try { w.WriteNumber("uptime_s", (long)(Environment.TickCount64 / 1000)); } catch { }
            w.WriteEndObject();
            // -------- inventaire RMM étendu (optionnel, borné : §1.2 de la spec)
            if (inv is not null)
            {
                w.WriteStartObject("inventory");
                if (!string.IsNullOrEmpty(inv.Av)) w.WriteString("av", inv.Av);
                w.WriteBoolean("vm", inv.Vm);
                if (!string.IsNullOrEmpty(inv.Domain)) w.WriteString("domain", Clip(inv.Domain, 64));
                if (inv.SecureBoot is bool sb) w.WriteBoolean("secure_boot", sb);
                if (!string.IsNullOrEmpty(inv.Dotnet)) w.WriteString("dotnet", inv.Dotnet);
                if (inv.Disks.Count > 0)
                {
                    w.WriteStartArray("disks");
                    foreach (var d in inv.Disks)
                    {
                        w.WriteStartObject();
                        w.WriteString("drive", d.Drive);
                        w.WriteNumber("total_gb", d.TotalGb);
                        w.WriteNumber("free_gb", d.FreeGb);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteStartObject("perf");
                w.WriteNumber("cpu_pct", inv.CpuPct);
                w.WriteNumber("ram_pct", inv.RamPct);
                w.WriteEndObject();
                if (inv.Software.Count > 0)
                {
                    w.WriteStartArray("software");
                    foreach (var s in inv.Software.Take(40)) w.WriteStringValue(Clip(s, 64));
                    w.WriteEndArray();
                }
                if (inv.Services.Count > 0)
                {
                    w.WriteStartArray("services");
                    foreach (var s in inv.Services.Take(24))
                    {
                        w.WriteStartObject();
                        w.WriteString("name", s.Name);
                        if (!string.IsNullOrEmpty(s.Display)) w.WriteString("display", Clip(s.Display, 80));
                        if (!string.IsNullOrEmpty(s.Status)) w.WriteString("status", s.Status);
                        w.WriteBoolean("auto", s.AutoStart);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                if (inv.Events.Count > 0)
                {
                    w.WriteStartArray("events");
                    foreach (var e in inv.Events.Take(10))
                    {
                        w.WriteStartObject();
                        w.WriteString("log", e.Log);
                        w.WriteString("source", e.Source);
                        w.WriteNumber("event_id", e.EventId);
                        w.WriteString("level", e.Level);
                        if (e.At > 0) w.WriteNumber("at", e.At);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteString("session", Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant());
            w.WriteNumber("scan_interval_s", _scanIntervalS);   // le serveur voit la cadence effective (et peut la régler)
            w.WriteString("pubkey", pubKeyHex);
            w.WriteString("nonce", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            w.WriteNumber("server_time", ServerClock.NowMs());
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
    private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }

    // SHA256 salé, tronqué à 16 hexa : stable par poste, non réversible vers le nom en clair.
    private static string HashShort(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(SentinelSalt + "|" + s.ToLowerInvariant()));
        return Convert.ToHexString(h, 0, 8).ToLowerInvariant();
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
