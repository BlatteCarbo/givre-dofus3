using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.Services.ForgeAdvice;

public enum ForgeMode { Advice, Automatic }
public enum ForgeAdviceStatus { UnknownState, Ready, Done, MissingRune }

/// <summary>Creation ticks and session identity prevent PID reuse and character changes from reusing advice.</summary>
public sealed record ForgeAdviceIdentity
{
    public int Pid { get; }
    public long ProcessCreationUtcTicks { get; }
    public string SessionId { get; }
    public string CharacterId { get; }
    public string ServerId { get; }

    public ForgeAdviceIdentity(int pid, long processCreationUtcTicks, string sessionId, string characterId, string serverId)
    {
        if (pid <= 0 || processCreationUtcTicks <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        Pid = pid;
        ProcessCreationUtcTicks = processCreationUtcTicks;
        SessionId = Bounded(sessionId);
        CharacterId = Bounded(characterId);
        ServerId = Bounded(serverId);
    }

    private static string Bounded(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
        ? value : throw new ArgumentException("Identity must contain 1 to 128 characters.");
}

public sealed record ForgeAdviceRepair(int EffectId, int Minimum, int Priority, long RuneGid);
public sealed record ForgeAdviceRune(long Gid, int EffectId, string Tier, string Name);

/// <summary>User thresholds and priority, not a probability or an optimal EXO solver.</summary>
public sealed class ForgeAdviceObjective
{
    public long Revision { get; }
    public int? ExoPaTarget { get; }
    public IReadOnlyList<ForgeAdviceRepair> Repairs { get; }

    public ForgeAdviceObjective(long revision, IEnumerable<ForgeAdviceRepair> repairs, int? naturalPa = null)
    {
        var copy = repairs.Take(65).ToArray();
        if (revision <= 0 || copy.Length > 64 || copy.Any(r => r.Minimum < 0 || r.RuneGid <= 0 || RuneCatalog.Of(r.EffectId) is null)
            || copy.Select(r => r.EffectId).Distinct().Count() != copy.Length
            || copy.Select(r => r.RuneGid).Distinct().Count() != copy.Length
            || naturalPa is < 0 or > 99 || (naturalPa.HasValue && copy.Any(r => r.EffectId == 111)))
            throw new ArgumentException("Invalid explicit forge objective.");
        Revision = revision;
        ExoPaTarget = naturalPa.HasValue ? naturalPa.Value + 1 : null;
        Repairs = Array.AsReadOnly(copy);
    }
}

/// <summary>Null effects/stock mean unknown; empty dictionaries mean received and empty. Pool remains nullable.</summary>
public sealed class ForgeAdviceObservation
{
    public ForgeAdviceIdentity Identity { get; }
    public long ItemUid { get; }
    public long ItemGid { get; }
    public long Revision { get; }
    public IReadOnlyDictionary<int, int>? Effects { get; }
    public IReadOnlyDictionary<long, int>? Stock { get; }
    public int? Pool { get; }

    public ForgeAdviceObservation(ForgeAdviceIdentity identity, long itemUid, long itemGid, long revision,
        IReadOnlyDictionary<int, int>? effects, IReadOnlyDictionary<long, int>? stock, int? pool)
    {
        if (itemUid <= 0 || itemGid <= 0 || revision <= 0) throw new ArgumentOutOfRangeException(nameof(itemUid));
        Identity = identity;
        ItemUid = itemUid;
        ItemGid = itemGid;
        Revision = revision;
        Effects = effects is null ? null : new ReadOnlyDictionary<int, int>(effects.ToDictionary(p => p.Key, p => p.Value));
        Stock = stock is null ? null : new ReadOnlyDictionary<long, int>(stock.ToDictionary(p => p.Key, p => p.Value));
        Pool = pool;
    }
}

/// <summary>Bounded immutable bridge payload. RuneGid is null unless ready; no transport or gameplay capability.</summary>
public sealed record ForgeAdviceRecord(int Version, ForgeAdviceIdentity? Identity, long ItemUid, long ItemGid,
    long ItemRevision, long ObjectiveRevision, long? RuneGid, string RuneName, string Reason,
    ForgeAdviceStatus Status, int? Pool, long PublicationRevision = 0);

/// <summary>Single-owner, event-driven controller. Call on the UI thread; it has no action/launch dependency.</summary>
public sealed class ForgeAdviceController
{
    private ForgeAdviceIdentity? _identity;
    private long _uid, _gid, _revision;
    private long _publicationRevision;
    private ForgeAdviceObjective? _objective;
    private IReadOnlyList<ForgeAdviceRune> _catalog = Array.Empty<ForgeAdviceRune>();
    public ForgeAdviceRecord Current { get; private set; } = new(1, null, 0, 0, 0, 0, null, "", "No observation.", ForgeAdviceStatus.UnknownState, null);
    public event Action<ForgeAdviceRecord>? Changed;

    public void Attach(ForgeAdviceIdentity? identity, long itemUid = 0, long itemGid = 0)
    {
        if (_identity == identity && _uid == itemUid && _gid == itemGid) return;
        _identity = identity;
        _uid = itemUid;
        _gid = itemGid;
        _revision = 0;
        _objective = null;
        Publish(Unknown("Identity or item changed."));
    }

    public void Configure(ForgeAdviceObjective objective, IEnumerable<ForgeAdviceRune> catalog)
    {
        if (_objective is not null && objective.Revision <= _objective.Revision)
            throw new ArgumentException("Objective revision must increase.");
        // Catalog text never goes onto the bridge without bounds.
        var copy = catalog.Take(513).ToArray();
        if (copy.Length > 512 || copy.Any(r => r.Gid <= 0 || r.Name.Length is 0 or > 96 || r.Tier.Length > 8)
            || copy.Select(r => r.Gid).Distinct().Count() != copy.Length)
            throw new ArgumentException("Invalid rune catalog.");
        _objective = objective;
        _catalog = Array.AsReadOnly(copy);
        Publish(Unknown("Objective changed; waiting for a fresh observation."));
    }

    public void Invalidate(string reason = "Observation invalidated.")
    {
        Publish(Unknown(reason.Length <= 192 ? reason : reason[..192]));
    }

    public void ClearObjective()
    {
        _objective = null;
        Invalidate("Objective changed; configure advice again.");
    }

    public bool Observe(ForgeAdviceObservation observation)
    {
        if (observation.Identity != _identity || observation.ItemUid != _uid || observation.ItemGid != _gid
            || observation.Revision <= _revision) return false;
        _revision = observation.Revision;
        Publish(Project(observation));
        return true;
    }

    private ForgeAdviceRecord Project(ForgeAdviceObservation observation)
    {
        var objective = _objective;
        if (objective is null || observation.Effects is null)
            return Unknown("Item effects or objective unknown.", observation.Pool);
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in observation.Effects)
            if (RuneCatalog.Of(pair.Key) is { } family) stats[family.Code] = pair.Value;
        if (objective.ExoPaTarget is int pa && stats.GetValueOrDefault("Ga Pa") >= pa)
            return Result(ForgeAdviceStatus.Done, "EXO PA target reached.", observation.Pool);

        var targets = objective.Repairs.Select(r => new ForgeTarget(RuneCatalog.Of(r.EffectId)!.Code,
            r.Minimum, r.Priority, r.RuneGid.ToString(CultureInfo.InvariantCulture))).ToArray();
        var action = ForgeEngine.DecideTargets(stats, targets);
        ForgeAdviceRune? rune;
        string reason;
        if (action.Kind == ForgeActionKind.Finish)
        {
            if (!objective.ExoPaTarget.HasValue)
                return Result(ForgeAdviceStatus.Done, "Repair targets reached.", observation.Pool);
            var matches = _catalog.Where(r => r.EffectId == 111 && r.Tier == "Ba"
                && string.Equals(r.Name, "Rune Ga Pa", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) return Unknown("Exact Ga Pa catalog identity unknown.", observation.Pool);
            rune = matches[0];
            reason = "Repair thresholds reached; explicit EXO PA attempt.";
        }
        else
        {
            long gid = long.Parse(action.Rune, CultureInfo.InvariantCulture);
            rune = _catalog.FirstOrDefault(r => r.Gid == gid);
            var repair = objective.Repairs.First(r => r.RuneGid == gid);
            if (rune is null || rune.EffectId != repair.EffectId
                || (rune.EffectId is 111 or 128 && rune.Tier != "Ba"))
                return Unknown("Configured repair rune catalog identity unknown.", observation.Pool);
            reason = "Repair below explicit threshold; configured priority.";
        }
        if (observation.Stock is null) return Unknown("Rune stock unknown.", observation.Pool);
        if (observation.Stock.GetValueOrDefault(rune.Gid) <= 0)
            return Result(ForgeAdviceStatus.MissingRune, "Selected rune absent from observed stock.", observation.Pool);
        return Result(ForgeAdviceStatus.Ready, reason, observation.Pool) with { RuneGid = rune.Gid, RuneName = rune.Name };
    }

    private ForgeAdviceRecord Unknown(string reason, int? pool = null) => Result(ForgeAdviceStatus.UnknownState, reason, pool);
    private ForgeAdviceRecord Result(ForgeAdviceStatus status, string reason, int? pool) =>
        new(1, _identity, _uid, _gid, _revision, _objective?.Revision ?? 0, null, "", reason, status, pool);
    private void Publish(ForgeAdviceRecord result)
    {
        Current = result with { PublicationRevision = ++_publicationRevision };
        Changed?.Invoke(Current);
    }
}
