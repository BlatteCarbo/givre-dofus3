using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Journal de diagnostic LOCAL <c>%APPDATA%\GivreClone\diag.log</c>, écrit AUSSI en production (contrairement à
/// <see cref="Logger"/>, Debug seulement). Sert à reconstituer ce qui s'est passé avec le serveur de licence quand un
/// utilisateur signale un souci (« déconnecté », « clé invalide », crash au login…) : il nous envoie ce fichier.
///
/// OPSEC : une ligne = horodatage UTC + CODE NUMÉRIQUE opaque + quelques NOMBRES (durée, tentative, compteur, code
/// HTTP, code d'erreur numéroté). JAMAIS d'URL, de chemin d'API, de jeton, de clé, de HWID, d'IP, de login, de nom de
/// personnage ni de message d'exception : la signature n'accepte que des entiers. Le dictionnaire des codes vit dans
/// le dépôt (<c>docs/diag-codes.md</c>), jamais livré. Best-effort : n'échoue jamais, ne bloque jamais.
/// </summary>
public static class Diag
{
    private static readonly object _lock = new();
    private static string _path = "";
    private static readonly Dictionary<long, long> _throttle = new();   // (code, clé) -> dernier tick écrit
    private const long MaxBytes = 1_000_000;                           // au-delà : diag.log -> diag.1.log (une seule archive)

    /// <summary>Au démarrage de l'app. Signale si la session PRÉCÉDENTE s'est terminée sans sortie propre (crash natif,
    /// fail-fast, kill) : dernière ligne ≠ 101.</summary>
    public static void Init(string version, bool aot, string? dirOverride = null)   // dirOverride : bancs de test uniquement
    {
        try
        {
            var dir = dirOverride ?? MZP19LocalClone.DataRoot;
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "diag.log");
            if (LastCode() is { } last && last != 101) Log(103, last);
            // Seule ligne avec du texte : la version de l'app (non sensible), pour savoir quel build a produit le journal.
            Write($"100 v{version} {(aot ? 1 : 0)}");
        }
        catch { _path = ""; }
    }

    /// <summary>Écrit « code args… ». Uniquement des entiers (voir OPSEC).</summary>
    public static void Log(int code, params long[] args)
    {
        if (_path.Length == 0) return;
        var sb = new StringBuilder(24);
        sb.Append(code.ToString(CultureInfo.InvariantCulture));
        foreach (var a in args) sb.Append(' ').Append(a.ToString(CultureInfo.InvariantCulture));
        Write(sb.ToString());
    }

    /// <summary>Comme <see cref="Log"/>, au plus une ligne par (code, clé) et par fenêtre (évènements répétitifs).</summary>
    public static void LogThrottled(int code, long key, long windowMs, params long[] args)
    {
        long now = Environment.TickCount64, k = ((long)code << 32) ^ key;
        lock (_throttle)
        {
            if (_throttle.TryGetValue(k, out var last) && now - last < windowMs) return;
            _throttle[k] = now;
        }
        Log(code, args);
    }

    private static void Write(string line)
    {
        try
        {
            var ts = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            lock (_lock)
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > MaxBytes) File.Move(_path, Path.Combine(fi.DirectoryName!, "diag.1.log"), overwrite: true);
                File.AppendAllText(_path, ts + " " + line + Environment.NewLine);
            }
        }
        catch { /* le journal ne doit jamais rien casser */ }
    }

    // Code de la dernière ligne du journal (pour détecter une fin anormale), null si vide/illisible.
    private static int? LastCode()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return null;
            int n = (int)Math.Min(fs.Length, 512);
            fs.Seek(-n, SeekOrigin.End);
            var buf = new byte[n];
            fs.ReadExactly(buf);
            var lines = Encoding.UTF8.GetString(buf).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) return null;
            var parts = lines[^1].Split(' ');
            return parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : null;
        }
        catch { return null; }
    }

    // ------------------------------------------------ numérotation opaque (voir docs/diag-codes.md) --

    /// <summary>Code d'erreur métier du serveur -> numéro.</summary>
    public static long Err(string? serverCode) => serverCode switch
    {
        null or "" => 0,
        "VALIDATION_ERROR" => 1, "RATE_LIMITED" => 2, "SESSION_EXPIRED" => 3, "HWID_MISMATCH" => 4, "INVALID_KEY" => 5,
        "KEY_EXPIRED" => 6, "KEY_REVOKED" => 7, "KEY_PAUSED" => 8, "KEY_LOCKED" => 9, "USER_BANNED" => 10,
        "UPLOAD_REJECTED" => 11, "INTERNAL" => 12, "SIGNATURE_REJECTED" => 13,
        _ => 99,
    };

    /// <summary>Raison d'un UPLOAD_REJECTED -> numéro.</summary>
    public static long Reason(string? reason) => reason switch
    {
        null or "" => 0,
        "not_ready" => 1, "channel" => 2, "malformed" => 3, "pubkey" => 4, "signature" => 5, "key_mismatch" => 6,
        "drift" => 7, "replay" => 8, "screenshot" => 9, "internal" => 10, "unsigned" => 11,
        _ => 99,
    };

    /// <summary>Exception -> numéro (type seulement, jamais le message).</summary>
    public static long Ex(Exception e) => e switch
    {
        TaskCanceledException or TimeoutException or OperationCanceledException => 2,
        HttpRequestException => 1,
        WebSocketException => 3,
        IOException => 4,
        License.EnvelopeException => 5,
        _ => 9,
    };

    /// <summary>Requête HTTP signée -> numéro d'endpoint (le chemin n'est jamais écrit).</summary>
    public static long Ep(string path) => path switch
    {
        "auth" => 1, "heartbeat" => 2, "logout" => 3, "mapping" => 4, "scripts" => 5, _ => 9,
    };

    /// <summary>Canal d'envoi WS -> numéro.</summary>
    public static long Chan(string channel) => channel switch
    {
        "bots" => 1, "arch" => 2, "pm" => 3, "command_ack" => 4, _ => 9,
    };

    /// <summary>Commande serveur -> numéro.</summary>
    public static long Cmd(string? command) => command switch
    {
        "DISCONNECT_BOT" => 1, "START_SCRIPT" => 2, "STOP_SCRIPT" => 3, "PAUSE_SCRIPT" => 4, "RESUME_SCRIPT" => 5,
        "SHOW_NOTIFICATION" => 6, "SEND_PM" => 7, "PM_ACTION" => 8, _ => 9,
    };

    /// <summary>Motif de rejet d'une enveloppe signée -> numéro (1 signature, 2 horloge, 3 nonce, 4 format/page non JSON).</summary>
    public static long Env(string? reason) =>
        reason is null ? 4
        : reason.Contains("signature", StringComparison.OrdinalIgnoreCase) ? 1
        : reason.Contains("server_time", StringComparison.OrdinalIgnoreCase) ? 2
        : reason.Contains("nonce", StringComparison.OrdinalIgnoreCase) ? 3
        : 4;
}
