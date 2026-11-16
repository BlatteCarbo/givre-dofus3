using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GivreClone.App.Services.ItemCraftPlanner;

namespace GivreClone.App.Services.Production;

public enum ItemCraftAttemptDecision { Allowed, NeedsReconciliation, Unavailable }
public enum ItemCraftAttemptPhase { None, Started, Completed, Uncertain }

public readonly record struct ItemCraftAttemptLease(Guid RunId, uint Pid,
    string Profile, string RequestHash);

public readonly record struct ItemCraftAttemptSnapshot(ItemCraftAttemptPhase Phase,
    string Reason, string? RequestHash = null, Guid LocalRunId = default);

/// <summary>
/// Production actions are real mutations (bank withdrawal, HDV purchase,
/// craft, listing), not a reversible simulation. Store a fsynced Start before
/// entering the existing ItemCraftProductionRun and a fsynced terminal state
/// only after all expected receipts. A crash in between blocks every future
/// start for the same server+character until independently reconciled.
/// No observed stock count is misrepresented as a receipt for an old attempt.
/// </summary>
public sealed class ItemCraftProductionAttemptJournal
{
    private const string Header = "GIVRE-ITEMCRAFT-PRODUCTION-v1\n";
    private const string AnchorPrefix = "GIVRE-ITEMCRAFT-ANCHOR-v1|";
    private const string Genesis = "GENESIS";
    private readonly object _gate = new();
    private readonly HashSet<Guid> _localLeases = new();
    private readonly string _path;

    public ItemCraftProductionAttemptJournal(string path)
        => _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));

    public static string RequestFingerprint(ItemCraftProductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var r = request.Recipe ?? throw new ArgumentException("Missing recipe", nameof(request));
        var canonical = new StringBuilder()
            .Append(r.ResultGid.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(r.ResultPerCraft?.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(r.SkillId?.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(r.KamasPerCraft?.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(request.Quantity.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(request.MaxKamas.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(request.BankAllowed ? '1' : '0').Append('|')
            .Append(request.SaleFloor?.ToString(CultureInfo.InvariantCulture));
        foreach (var item in r.Inputs.OrderBy(x => x.Gid).ThenBy(x => x.PerCraft))
            canonical.Append('|').Append(item.Gid.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(item.PerCraft.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(item.BuyAllowed ? '1' : '0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public ItemCraftAttemptSnapshot ReadStatus(string? profile)
    {
        if (!ValidProfile(profile)) return new(ItemCraftAttemptPhase.Uncertain, "Profil serveur/personnage non établi");
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                    return !File.Exists(_path + ".anchor")
                        ? new(ItemCraftAttemptPhase.None, "Aucune production en attente")
                        : new(ItemCraftAttemptPhase.Uncertain, "Ancre présente sans journal : vérification manuelle nécessaire");
                using var stream = OpenExclusive(FileMode.Open, FileAccess.Read);
                var state = ReadState(stream);
                if (state is null || !ValidAnchor(state))
                    return new(ItemCraftAttemptPhase.Uncertain, "Journal/ancre altérés : arrêt conservateur");
                return Snapshot(state, profile!);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                return new(ItemCraftAttemptPhase.Uncertain, "Journal inaccessible : arrêt conservateur");
            }
        }
    }

    public ItemCraftAttemptDecision TryBegin(string? profile, uint pid,
        ItemCraftProductionRequest request, out ItemCraftAttemptLease lease)
    {
        lease = default;
        if (pid == 0 || !ValidProfile(profile) || request is null ||
            request.Quantity < 1 || request.Recipe is null) return ItemCraftAttemptDecision.Unavailable;
        string hash;
        try { hash = RequestFingerprint(request); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NullReferenceException)
        { return ItemCraftAttemptDecision.Unavailable; }
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var stream = OpenExclusive(FileMode.OpenOrCreate, FileAccess.ReadWrite);
                var state = ReadState(stream);
                if (state is null) return ItemCraftAttemptDecision.Unavailable;
                bool anchored = File.Exists(_path + ".anchor");
                if (anchored != (stream.Length > 0) || anchored && !ValidAnchor(state))
                    return ItemCraftAttemptDecision.Unavailable;
                if (Snapshot(state, profile!).Phase is ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
                    return ItemCraftAttemptDecision.NeedsReconciliation;
                if (stream.Length == 0)
                {
                    byte[] header = Encoding.UTF8.GetBytes(Header);
                    stream.Write(header);
                    stream.Flush(flushToDisk: true);
                    using var firstAnchor = new FileStream(_path + ".anchor", FileMode.CreateNew,
                        FileAccess.Write, FileShare.None);
                    byte[] baseline = Encoding.UTF8.GetBytes(AnchorFor(state));
                    firstAnchor.Write(baseline);
                    firstAnchor.Flush(flushToDisk: true);
                }
                var id = Guid.NewGuid();
                Append(stream, state, profile!, pid, id, hash, ItemCraftAttemptPhase.Started);
                lease = new(id, pid, profile!, hash);
                _localLeases.Add(id);
                return ItemCraftAttemptDecision.Allowed;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
            { return ItemCraftAttemptDecision.Unavailable; }
        }
    }

    /// <summary>Finish only a lease opened by this live journal instance.</summary>
    public bool TryFinish(ItemCraftAttemptLease lease, bool fullyConfirmed)
    {
        if (lease.RunId == Guid.Empty || !ValidProfile(lease.Profile)) return false;
        lock (_gate)
        {
            if (!_localLeases.Contains(lease.RunId)) return false;
            try
            {
                using var stream = OpenExclusive(FileMode.Open, FileAccess.ReadWrite);
                var state = ReadState(stream);
                if (state is null || !ValidAnchor(state) ||
                    !state.Latest.TryGetValue(lease.Profile, out var last) ||
                    last.Phase != ItemCraftAttemptPhase.Started || last.RunId != lease.RunId ||
                    last.Pid != lease.Pid || last.RequestHash != lease.RequestHash) return false;
                Append(stream, state, lease.Profile, lease.Pid, lease.RunId,
                    lease.RequestHash, fullyConfirmed ? ItemCraftAttemptPhase.Completed : ItemCraftAttemptPhase.Uncertain);
                _localLeases.Remove(lease.RunId);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
            { return false; }
        }
    }

    private static ItemCraftAttemptSnapshot Snapshot(State state, string profile)
    {
        if (!state.Latest.TryGetValue(profile, out var row))
            return new(ItemCraftAttemptPhase.None, "Aucune production en attente");
        return row.Phase switch
        {
            ItemCraftAttemptPhase.Completed => new(row.Phase, "Dernière production entièrement confirmée", row.RequestHash, row.RunId),
            ItemCraftAttemptPhase.Started => new(row.Phase, "Production interrompue ou en cours : rapprochement requis", row.RequestHash, row.RunId),
            ItemCraftAttemptPhase.Uncertain => new(row.Phase, "Résultat de production incomplet : rapprochement requis", row.RequestHash, row.RunId),
            _ => new(ItemCraftAttemptPhase.Uncertain, "État inconnu : rapprochement requis")
        };
    }

    private sealed record Record(Guid RunId, uint Pid, string RequestHash, ItemCraftAttemptPhase Phase);
    private sealed class State
    {
        public int Count;
        public string LastHash = Genesis;
        public Dictionary<string, Record> Latest { get; } = new(StringComparer.Ordinal);
    }

    private static bool ValidProfile(string? profile)
        => !string.IsNullOrWhiteSpace(profile) && profile.Length <= 256 &&
           profile.StartsWith("server:", StringComparison.Ordinal) && profile.Split(':').Length >= 3;

    private FileStream OpenExclusive(FileMode mode, FileAccess access)
    {
        for (int i = 0; ; i++)
        {
            try { return new FileStream(_path, mode, access, FileShare.None); }
            catch (IOException ex) when (i < 30 && (ex.HResult & 0xFFFF) is 32 or 33)
            { System.Threading.Thread.Sleep(10); }
        }
    }

    private static string Sha(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string AnchorFor(State state)
        => AnchorPrefix + state.Count.ToString(CultureInfo.InvariantCulture) + "|" + state.LastHash + "\n";

    private bool ValidAnchor(State state)
    {
        try { return File.ReadAllText(_path + ".anchor", new UTF8Encoding(false, true)) == AnchorFor(state); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { return false; }
    }

    private static State? ReadState(FileStream stream)
    {
        if (stream.Length == 0) return new State();
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string text = reader.ReadToEnd();
        if (!text.StartsWith(Header, StringComparison.Ordinal) || !text.EndsWith('\n')) return null;
        var state = new State();
        var lines = text[Header.Length..].Split('\n', StringSplitOptions.None);
        for (int i = 0; i < lines.Length - 1; i++)
        {
            string[] parts = lines[i].Split('|');
            if (parts.Length != 7 || !int.TryParse(parts[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int next) || next != state.Count + 1 ||
                !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint pid) ||
                !Guid.TryParseExact(parts[3], "N", out var run) || run == Guid.Empty ||
                !Enum.TryParse(parts[5], false, out ItemCraftAttemptPhase phase) ||
                phase is ItemCraftAttemptPhase.None || !Enum.IsDefined(phase) || parts[4].Length != 64 ||
                !parts[4].All(Uri.IsHexDigit) || parts[6].Length != 64) return null;
            string profile;
            try { profile = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(parts[1])); }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { return null; }
            if (!ValidProfile(profile)) return null;
            string payload = string.Join('|', parts.Take(6));
            string expected = Sha(state.LastHash + "|" + payload);
            if (!string.Equals(parts[6], expected, StringComparison.Ordinal)) return null;
            if (state.Latest.TryGetValue(profile, out var old))
            {
                if (phase == ItemCraftAttemptPhase.Started &&
                    old.Phase != ItemCraftAttemptPhase.Completed) return null;
                if (phase != ItemCraftAttemptPhase.Started &&
                    (old.Phase != ItemCraftAttemptPhase.Started || old.RunId != run ||
                     old.Pid != pid || old.RequestHash != parts[4])) return null;
            }
            else if (phase != ItemCraftAttemptPhase.Started) return null;
            state.Latest[profile] = new Record(run, pid, parts[4], phase);
            state.LastHash = expected;
            state.Count++;
        }
        return state;
    }

    private void Append(FileStream stream, State state, string profile, uint pid,
        Guid run, string requestHash, ItemCraftAttemptPhase phase)
    {
        string payload = (state.Count + 1).ToString(CultureInfo.InvariantCulture) + "|" +
                         Convert.ToBase64String(Encoding.UTF8.GetBytes(profile)) + "|" +
                         pid.ToString(CultureInfo.InvariantCulture) + "|" + run.ToString("N") + "|" +
                         requestHash + "|" + phase;
        string hash = Sha(state.LastHash + "|" + payload);
        byte[] bytes = Encoding.UTF8.GetBytes(payload + "|" + hash + "\n");
        stream.Position = stream.Length;
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        state.Count++;
        state.LastHash = hash;
        // Anchor bytes are fsynced before replacing the previous anchor. Any
        // interruption between journal and anchor commits fails closed.
        string staging = _path + ".anchor.pending-" + Guid.NewGuid().ToString("N");
        using (var anchor = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] content = Encoding.UTF8.GetBytes(AnchorFor(state));
            anchor.Write(content);
            anchor.Flush(flushToDisk: true);
        }
        File.Move(staging, _path + ".anchor", overwrite: true);
    }
}
