using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services;

/// <summary>Rapport d'incident LOCAL : un zip avec les journaux récents (secrets masqués), un résumé et un manifeste d'empreintes. Rien n'est envoyé nulle part.</summary>
public static class IncidentReport
{
    // Lignes bornées à 2048 caractères : une machine chargée ne doit pas masquer une ligne saine.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private const int MaxLogReadBytes = 4 * 1024 * 1024;

    // clé = valeur : mots-clés sensibles (portés de l'ancien SessionDiagnosticBundle : + pwd, token, cookie, session, ticket,
    // username, email). Les clés composées (WEBHOOK_URL, SESSION_ID, access_token…) sont reconnues par leur mot-clé.
    private static readonly Regex Secret = new(
        @"(?i)(?<key>\b[\w.-]*(?:access[_-]?token|refresh[_-]?token|authorization|api[_-]?key|password|passwd|pwd|secret|webhook|token|cookie|session(?:[_-]?id)?|ticket|username|e-?mail)[\w.-]*)\b(?<sep>[\""']?\s*[:=]\s*)(?:bearer\s+)?(?:\""[^\""\r\n]*\""|'[^'\r\n]*'|[^\s,;}\)]+)",
        Opts, Timeout);
    private static readonly Regex Bearer = new(@"(?i)\bBearer\s+[A-Za-z0-9\-\._~\+/]+=*", Opts, Timeout);
    private static readonly Regex UriCredential = new(@"(?i)(https?://)([^/\s:@]+):([^@\s/]+)@", Opts, Timeout);
    private static readonly Regex DiscordWebhook = new(@"(?i)https://(?:ptb\.|canary\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w-]+", Opts, Timeout);
    // Jeton JWT (3 segments base64url) égaré dans une ligne sans clé explicite.
    private static readonly Regex Jwt = new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", Opts, Timeout);

    /// <summary>Masque les identifiants évidents d'une ligne de journal (au mieux, pas une garantie de confidentialité).</summary>
    public static string Redact(string line) => Redact(line, out _);

    /// <summary>Comme <see cref="Redact(string)"/>, en indiquant si la ligne a été modifiée.</summary>
    public static string Redact(string line, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(line)) return line ?? string.Empty;
        try
        {
            string s = line.Length > 2048 ? line[..2048] : line;
            string r = DiscordWebhook.Replace(s, "https://discord.com/api/webhooks/[REDACTED]");
            r = UriCredential.Replace(r, "$1[REDACTED]:[REDACTED]@");
            r = Bearer.Replace(r, "Bearer [REDACTED]");
            r = Jwt.Replace(r, "[REDACTED-JWT]");
            r = Secret.Replace(r, "${key}${sep}[REDACTED]");
            changed = !string.Equals(r, line, StringComparison.Ordinal);
            return r;
        }
        catch (RegexMatchTimeoutException) { changed = true; return "[LOG REDACTED: unsafe to inspect]"; }
    }

    /// <summary>Écrit le zip : les <paramref name="maxLogs"/> journaux *.log les plus récents de <paramref name="logsDir"/>, chacun limité aux lignes complètes de ses derniers 4 Mio, plus <c>resume.txt</c> masqué et <c>manifeste.json</c> (taille, lignes, lignes masquées, SHA-256 de chaque fichier écrit).</summary>
    public static int Write(string logsDir, string zipPath, string summary, int maxLogs = 12, int maxLinesPerLog = 5000)
    {
        var logs = Directory.Exists(logsDir)
            ? new DirectoryInfo(logsDir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(maxLogs).ToArray()
            : Array.Empty<FileInfo>();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        var manifest = new System.Collections.Generic.List<(string Name, long Bytes, int Lines, int Redacted, string Sha256, long? SourceBytes, long? ReadStart)>();
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var summaryLines = summary.Split('\n');
        int summaryRedacted = 0;
        for (int i = 0; i < summaryLines.Length; i++)
        {
            summaryLines[i] = Redact(summaryLines[i], out bool changed);
            if (changed) summaryRedacted++;
        }
        var summaryBytes = new UTF8Encoding(false).GetBytes(string.Join('\n', summaryLines));
        using (var s = zip.CreateEntry("resume.txt").Open()) s.Write(summaryBytes);
        manifest.Add(("resume.txt", summaryBytes.Length, summaryLines.Length, summaryRedacted, Hex(SHA256.HashData(summaryBytes)), null, null));
        int written = 0;
        foreach (var f in logs)
        {
            string[] lines;
            long sourceBytes, readStart;
            try
            {
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                sourceBytes = fs.Length;
                Encoding encoding;
                using (var probe = new StreamReader(fs, Encoding.UTF8, true, 128, leaveOpen: true))
                {
                    probe.Peek();
                    encoding = probe.CurrentEncoding;
                }
                // Align UTF-16/32 as well as UTF-8; the first partial line is never exported.
                readStart = Math.Max(0, sourceBytes - MaxLogReadBytes);
                readStart = (readStart + 3) / 4 * 4;
                fs.Position = readStart;
                var buffer = new byte[(int)Math.Max(0, sourceBytes - readStart)];
                int read = 0, n;
                while (read < buffer.Length && (n = fs.Read(buffer, read, buffer.Length - read)) > 0)
                    read += n;
                string tail = encoding.GetString(buffer, 0, read);
                if (readStart > 0)
                {
                    int newline = tail.IndexOf('\n');
                    tail = newline < 0 ? string.Empty : tail[(newline + 1)..];
                }
                else if (tail.StartsWith('\uFEFF')) tail = tail[1..];
                var all = readStart > 0 && tail.Length == 0 ? Array.Empty<string>() : tail.Split('\n');
                lines = all.Skip(Math.Max(0, all.Length - maxLinesPerLog)).ToArray();
            }
            catch (IOException) { continue; }
            var sb = new StringBuilder();
            int redacted = 0;
            foreach (var l in lines)
            {
                sb.Append(Redact(l.TrimEnd('\r'), out bool ch)).Append('\n');
                if (ch) redacted++;
            }
            var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            using (var s = zip.CreateEntry("logs/" + f.Name).Open()) s.Write(bytes);
            manifest.Add(("logs/" + f.Name, bytes.Length, lines.Length, redacted, Hex(SHA256.HashData(bytes)), sourceBytes, readStart));
            written++;
        }
        using (var ms = new MemoryStream())
        {
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("genereLe", DateTime.UtcNow.ToString("O"));
                w.WriteString("application", typeof(IncidentReport).Assembly.GetName().Version?.ToString() ?? "?");
                var build = BuildProvenance.Current;
                w.WriteString("versionInformative", build.Version);
                w.WriteString("empreinteBinaire", build.BinarySha256);
                w.WriteString("runtime", build.Runtime);
                w.WriteString("systeme", build.OperatingSystem);
                w.WriteString("note", "Empreintes calculées sur le contenu écrit dans ce zip (après masquage). Rien n'a été envoyé nulle part.");
                w.WriteStartArray("fichiers");
                foreach (var m in manifest)
                {
                    w.WriteStartObject();
                    w.WriteString("nom", m.Name); w.WriteNumber("octets", m.Bytes); w.WriteNumber("lignes", m.Lines);
                    w.WriteNumber("lignesMasquees", m.Redacted); w.WriteString("sha256", m.Sha256);
                    if (m.SourceBytes is long sourceBytes)
                    {
                        w.WriteNumber("octetsSource", sourceBytes);
                        w.WriteNumber("debutLecture", m.ReadStart!.Value);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            using var s = zip.CreateEntry("manifeste.json").Open(); ms.Position = 0; ms.CopyTo(s);
        }
        return written;
    }

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
