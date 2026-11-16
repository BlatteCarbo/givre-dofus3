using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services;

public sealed record TurboLogReport(int Requests, int Closed, int Timeouts, int GraceRetries,
    int EndsAfterTimeout, int MaxWaitMs, int MaxSequenceToAckMs, int NativeErrors);

/// <summary>Analyse des traces existantes, sans interpréter une injection comme une réussite serveur.</summary>
public static class TurboLogDiagnostics
{
    private static readonly Regex Pid = new(@"<([0-9]+)>", RegexOptions.Compiled);
    private static readonly Regex Wait = new(@"(?<![a-z_])request_to_ack_ms=([0-9]+)", RegexOptions.Compiled);
    private static readonly Regex AckWait = new(@"sequence_end_to_ack_ms=([0-9]+)", RegexOptions.Compiled);
    private static int Value(Regex rx, string line) => int.TryParse(rx.Match(line).Groups[1].Value, out int n) ? n : 0;

    public static TurboLogReport Analyze(IEnumerable<string> lines, uint? onlyPid = null)
    {
        int requests = 0, closed = 0, timeouts = 0, grace = 0, ends = 0, maxWait = 0, maxAck = 0, native = 0;
        var pendingTimeout = new Dictionary<uint, bool>();
        foreach (string line in lines)
        {
            bool tagged = uint.TryParse(Pid.Match(line).Groups[1].Value, out uint pid);
            if (tagged && onlyPid is uint filter && pid != filter) continue;
            // Les lignes UI sont déjà filtrées par bot ; les lignes disque portent chacune leur PID.
            if (!tagged) pid = onlyPid ?? 0;
            if (line.Contains("[turbo-metric] request_tx", StringComparison.Ordinal)) { requests++; pendingTimeout[pid] = false; }
            if (line.Contains("[turbo-metric] barrier_closed", StringComparison.Ordinal))
            {
                closed++; pendingTimeout[pid] = false;
                maxWait = Math.Max(maxWait, Value(Wait, line));
                maxAck = Math.Max(maxAck, Value(AckWait, line));
            }
            if (line.Contains("BARRIERE NON FERMEE", StringComparison.Ordinal)) { timeouts++; pendingTimeout[pid] = true; }
            if (line.Contains("[turbo-metric]", StringComparison.Ordinal) && line.Contains("seconde chance", StringComparison.Ordinal)) grace++;
            if (line.Contains("[combat]", StringComparison.Ordinal) && line.Contains("fin du tour (FightTurnFinishRequest)", StringComparison.Ordinal)
                && line.Contains("inject=OK", StringComparison.Ordinal) && pendingTimeout.GetValueOrDefault(pid))
            { ends++; pendingTimeout[pid] = false; }
            if (line.Contains("[turbo-native]", StringComparison.Ordinal) && new[] { "impossible", "échec", "refus", "absent" }.Any(w => line.Contains(w, StringComparison.OrdinalIgnoreCase))) native++;
        }
        return new(requests, closed, timeouts, grace, ends, maxWait, maxAck, native);
    }
}
