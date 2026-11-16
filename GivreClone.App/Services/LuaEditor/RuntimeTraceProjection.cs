using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.LuaEditor;

/// <summary>One observed line in the on-disk GivreLuaHost.Logging.cs format.
/// This is local evidence, not evidence that a gameplay action was acknowledged.</summary>
public sealed record LuaRuntimeTraceLine(string Clock, string Level, string Category, string Message);

public sealed record LuaRuntimeTraceIssue(string Message, string? File, int? Line,
    string? SourceLine, string? PreviousLine, string? NextLine);

public sealed record LuaRuntimeTraceReport(
    string RunState, string? SessionHeader, int ParsedLines,
    LuaRuntimeTraceLine? LastActivity, LuaRuntimeTraceLine? LastError,
    LuaRuntimeTraceIssue? LastIssue, bool Truncated,
    string EvidenceDisclaimer);

/// <summary>
/// Pure projection of the persistent Lua log, optionally enriched with the exact
/// Lua script text supplied by the caller. Never opens a file, launches a bot,
/// sends a packet or infers a running session from an old log.
/// </summary>
public static class RuntimeTraceProjection
{
    private const int MaxInputCharacters = 1_000_000;
    private const int MaxLines = 12_000;
    private static readonly Regex LogLine = new(
        @"^(?<clock>\d\d:\d\d:\d\d) \[(?<level>OK  |WARN|ERR |DBG |INFO)\] (?<category>.{1,11}?)\s{1,11}(?<message>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
    private static readonly Regex Session = new(
        @"^===== session \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \(pid \d+\) =====$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
    private static readonly Regex SourceError = new(
        @"(?<file>[^\s:]+):(?:\((?<line>\d+)(?:,\d+(?:-\d+)?)?\)|(?<line2>\d+))\s*(?::|—|-)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    public static LuaRuntimeTraceReport Project(string? logText, bool? isRunning, string? source = null)
    {
        logText ??= "";
        bool truncated = logText.Length > MaxInputCharacters;
        if (truncated) logText = logText[^MaxInputCharacters..];
        string? header = null;
        LuaRuntimeTraceLine? lastActivity = null, lastError = null;
        int parsed = 0, visited = 0;
        // A file can contain many sessions for one character. Forget all earlier
        // signals as soon as a new session header is seen.
        foreach (var line in logText.Replace("\r", "").Split('\n').TakeLast(MaxLines))
        {
            if (++visited > MaxLines) break;
            if (line.Length > 16_384) continue;
            if (Session.IsMatch(line))
            {
                header = line;
                lastActivity = lastError = null;
                parsed = 0;
                continue;
            }
            var match = LogLine.Match(line);
            if (!match.Success) continue;
            string clock = match.Groups["clock"].Value;
            if (!TimeOnly.TryParseExact(clock, "HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _)) continue;
            var parsedLine = new LuaRuntimeTraceLine(clock, match.Groups["level"].Value.Trim(),
                match.Groups["category"].Value.Trim(), match.Groups["message"].Value);
            parsed++;
            if (parsedLine.Level == "ERR") lastError = parsedLine;
            else if (parsedLine.Level is "OK" or "INFO" or "WARN") lastActivity = parsedLine;
        }
        if (visited == MaxLines && logText.Split('\n').Length > MaxLines) truncated = true;
        return new LuaRuntimeTraceReport(isRunning switch
        {
            true => "running-observed-by-caller", false => "stopped-observed-by-caller", _ => "unknown"
        }, header, parsed, lastActivity, lastError,
            lastError is { } e ? Explain(e.Message, source) : null,
            truncated,
            "Journal local uniquement ; ni ACK serveur, ni état d'exécution actuel déduits des lignes persistées.");
    }

    private static LuaRuntimeTraceIssue Explain(string message, string? source)
    {
        var match = SourceError.Match(message);
        if (!match.Success) return new LuaRuntimeTraceIssue(message, null, null, null, null, null);
        int? line = int.TryParse(match.Groups["line"].Success ? match.Groups["line"].Value :
            match.Groups["line2"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            && n > 0 ? n : null;
        if (line is null || source is null || source.Length > 256_000)
            return new LuaRuntimeTraceIssue(message, match.Groups["file"].Value, line, null, null, null);
        var rows = source.Replace("\r", "").Split('\n');
        if (line > rows.Length)
            return new LuaRuntimeTraceIssue(message, match.Groups["file"].Value, line, null, null, null);
        string Limit(string s) => s.Length > 240 ? s[..240] + "…" : s;
        int index = line.Value - 1;
        return new LuaRuntimeTraceIssue(message, match.Groups["file"].Value, line,
            Limit(rows[index]), index > 0 ? Limit(rows[index - 1]) : null,
            index + 1 < rows.Length ? Limit(rows[index + 1]) : null);
    }
}
