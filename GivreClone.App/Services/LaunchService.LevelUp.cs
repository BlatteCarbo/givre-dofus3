using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Auto level-up des caractéristiques =====================
public sealed partial class LaunchService
{
    // Répartition complète en budgets investis ABSOLUS (captures officielles Intelligence+Sagesse du 24/10).
    private readonly HashSet<uint> _autoLevelActive = new();
    private readonly HashSet<uint> _statUpgradeActive = new();
    private readonly object _autoLevelLock = new();
    private sealed record StatSheet(long CharacterId, object Session, IReadOnlyDictionary<int, int> Bases, int Pool);
    private readonly ConcurrentDictionary<uint, StatSheet> _statSheets = new();
    private readonly Dictionary<uint, object> _statSessions = new();
    private readonly Dictionary<uint, CancellationTokenSource> _autoStatCancellation = new();
    private readonly HashSet<uint> _autoStatSuspended = new();
    private readonly Dictionary<uint, object> _autoStatRequests = new();

    private void NoteStatSheet(uint pid, IReadOnlyDictionary<int, int> bases, int pool)
    {
        lock (_autoLevelLock)
        {
            if (!_statSessions.TryGetValue(pid, out var session)) _statSessions[pid] = session = new object();
            _statSheets[pid] = new StatSheet(CharacterId(pid), session, bases, pool);
        }
        KickAutoLevel(pid);
    }

    public void CancelAutomaticStatUpgrade(uint pid)
    {
        lock (_autoLevelLock)
        {
            _autoStatSuspended.Add(pid);
            _autoStatRequests.Remove(pid);
            if (_autoStatCancellation.TryGetValue(pid, out var cts)) cts.Cancel();
        }
    }

    private void ResetStatSheet(uint pid)
    {
        lock (_autoLevelLock)
        {
            _statSheets.TryRemove(pid, out _);
            _autoStatRequests.Remove(pid);
            _statSessions[pid] = new object();
            if (_autoStatCancellation.TryGetValue(pid, out var cts)) cts.Cancel();
            _autoStatSuspended.Remove(pid);
        }
    }

    /// <summary>Nom de champ de forge (fields.json) pour la stat cible du profil d'auto level-up.</summary>
    private static string StatFieldName(LevelStatTarget t) => t switch
    {
        LevelStatTarget.Strength => "strength",
        LevelStatTarget.Intelligence => "intelligence",
        LevelStatTarget.Chance => "chance",
        LevelStatTarget.Agility => "agility",
        LevelStatTarget.Vitality => "vitality",
        LevelStatTarget.Wisdom => "wisdom",
        _ => "strength",
    };

    /// <summary>Envoie tous les budgets investis pour conserver les caractéristiques non ciblées.</summary>
    private bool SendCharacteristicUpgrade(uint pid, IReadOnlyDictionary<string,int> allocation)
    {
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildCharacteristicUpgradeFramed(allocation);
        if (pkt.Length == 0) { TransportLog.Line($"[carac] forge refusée : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[carac] pid {pid} : répartition complète {string.Join(", ", allocation.Select(x => x.Key + "=" + x.Value))} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Amorcée par la fiche reçue ; attend la fin du combat sans réutiliser une autre identité.</summary>
    private void KickAutoLevel(uint pid)
    {
        if (!AutomaticCharacteristicsEnabled(pid) || !LicenseService.HasPermission(LicenseService.Perm.Fight)) return;
        CancellationTokenSource cts;
        StatSheet sheet;
        object request;
        lock (_autoLevelLock)
        {
            if (!_autoStatRequests.TryGetValue(pid, out request!) || _autoStatSuspended.Contains(pid) || _statUpgradeActive.Contains(pid) || _autoLevelActive.Contains(pid)
                || !_statSheets.TryGetValue(pid, out sheet!) || sheet.Pool <= 0 || sheet.CharacterId == 0 || sheet.CharacterId != CharacterId(pid)) return;
            _autoLevelActive.Add(pid);
            _autoStatCancellation[pid] = cts = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                while (IsInFight(pid)) await Task.Delay(100, cts.Token).ConfigureAwait(false);
                while (!cts.IsCancellationRequested && AutomaticCharacteristicsEnabled(pid)
                    && _statSheets.TryGetValue(pid, out var latest) && ReferenceEquals(sheet.Session, latest.Session) && latest.CharacterId == CharacterId(pid) && latest.Pool > 0)
                {
                    bool ordered = OrderedCharacteristicSettings(pid)?.OrderedCharacteristicsEnabled == true;
                    string stat = _combat.TryGetValue(pid, out var current) ? StatFieldName(current.LevelStatTarget) : "";
                    if (await UpgradeCharacteristicCoreAsync(pid, stat, latest.Pool, cts.Token, ordered, automatic: true).ConfigureAwait(false) is null) break;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            catch (Exception e) { TransportLog.Line($"[carac] pid {pid} : auto-dépense arrêtée ({e.GetType().Name})."); }
            finally
            {
                bool followNewLevel;
                lock (_autoLevelLock)
                {
                    _autoLevelActive.Remove(pid); _autoStatCancellation.Remove(pid);
                    followNewLevel = _autoStatRequests.TryGetValue(pid, out var pending) && !ReferenceEquals(request, pending);
                    if (!followNewLevel) _autoStatRequests.Remove(pid);
                }
                if (followNewLevel) KickAutoLevel(pid);
                cts.Dispose();
            }
        });
    }

    private BotSettings? OrderedCharacteristicSettings(uint pid)
    {
        string? name = SelfCharacterInfo(pid)?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : BotSettingsStore.Get(name);
    }

    private bool AutomaticCharacteristicsEnabled(uint pid) =>
        OrderedCharacteristicSettings(pid)?.OrderedCharacteristicsEnabled == true
        || (_combat.TryGetValue(pid, out var profile) && profile.AutoLevelStats);

    private CharacteristicSpend? PlanOrderedCharacteristics(uint pid, StatSheet sheet, int budget)
    {
        var settings = OrderedCharacteristicSettings(pid);
        if (settings?.OrderedCharacteristicsEnabled != true || settings.OrderedCharacteristicTargets is null
            || SelfCharacterInfo(pid)?.BreedIndex is not int breed
            || GameData?.Row("BreedData", ClassIconProvider.BreedId(breed).ToString(System.Globalization.CultureInfo.InvariantCulture)) is not { } row) return null;
        var costs = new Dictionary<string, string>();
        foreach (var (stat, _) in CharacteristicUpgradePolicy.Stats)
        {
            string field = "statsPointsFor" + char.ToUpperInvariant(stat[0]) + stat[1..];
            if (!row.TryGetValue(field, out var raw) || raw is not string json) return null;
            costs[stat] = json;
        }
        return OrderedCharacteristicPolicy.Next(sheet.Bases, budget, settings.OrderedCharacteristicTargets, costs);
    }

    /// <summary>Pool provenant du même instantané serveur que les bases ; -1 si fiche inconnue/incomplète.</summary>
    public int StatPointsLeft(uint pid) => _statSheets.TryGetValue(pid, out var sheet) && sheet.CharacterId == CharacterId(pid) ? sheet.Pool : -1;

    /// <summary>Nom de stat Lua (FR/EN/élément, insensible à la casse) -> nom de champ de forge (wisdom|chance|agility|
    /// intelligence|strength|vitality), ou null si inconnu.</summary>
    public static string? NormalizeStatName(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "vitality" or "vitalite" or "vitalité" or "vita" => "vitality",
        "wisdom" or "sagesse" => "wisdom",
        "strength" or "strenght" or "force" or "terre" => "strength",
        "intelligence" or "intel" or "feu" => "intelligence",
        "chance" or "eau" => "chance",
        "agility" or "agilite" or "agilité" or "air" => "agility",
        _ => null,
    };

    /// <summary>Dépense au plus amount points de budget dans la base choisie ; confirme la base ET la baisse du pool sur la fiche serveur.</summary>
    public async Task<int?> UpgradeCharacteristicAsync(uint pid, string statName, int amount, CancellationToken ct)
    {
        lock (_autoLevelLock)
        {
            if (_autoLevelActive.Contains(pid) || !_statUpgradeActive.Add(pid)) return null;
            _autoStatRequests.Remove(pid); // la répartition explicite prend la main jusqu’au prochain niveau
        }
        try { return await UpgradeCharacteristicCoreAsync(pid, statName, amount, ct).ConfigureAwait(false); }
        finally { lock (_autoLevelLock) _statUpgradeActive.Remove(pid); }
    }

    private async Task<int?> UpgradeCharacteristicCoreAsync(uint pid, string statName, int amount, CancellationToken ct, bool ordered = false, bool automatic = false)
    {
        ct.ThrowIfCancellationRequested();
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || IsInFight(pid)) return null;
        if (!_statSheets.TryGetValue(pid, out var before) || before.CharacterId == 0 || before.CharacterId != CharacterId(pid)) return null;
        int pool = before.Pool;
        amount = Math.Clamp(amount, 1, 1000);
        if (pool <= 0) return null;
        var planned = ordered ? PlanOrderedCharacteristics(pid, before, Math.Min(pool, amount)) : null;
        if (ordered && planned is null) return null;
        string? stat = planned?.Stat ?? NormalizeStatName(statName);
        if (stat is null) return null;
        int id = CharacteristicUpgradePolicy.Stats.First(s => s.Name == stat).Id;
        if (!before.Bases.TryGetValue(id, out int baseValue)) return null;
        int target = planned?.Target ?? CharacteristicUpgradePolicy.Target(stat, baseValue, Math.Min(pool, amount));
        if (target <= baseValue) return null;
        var allocation = planned?.Allocation ?? CharacteristicUpgradePolicy.Allocation(before.Bases,stat,target);
        if (allocation is null) return null;
        int expectedPool = pool - (planned?.Cost ?? (CharacteristicUpgradePolicy.Invested(stat,target) - CharacteristicUpgradePolicy.Invested(stat,baseValue)));
        lock (_autoLevelLock)
        {
            ct.ThrowIfCancellationRequested();
            if (!_statSheets.TryGetValue(pid, out var now) || !ReferenceEquals(before, now) || now.CharacterId != CharacterId(pid)) return null;
            if (ordered && OrderedCharacteristicSettings(pid)?.OrderedCharacteristicsEnabled != true) return null;
            if (automatic && !ordered && (!_combat.TryGetValue(pid, out var profile) || !profile.AutoLevelStats)) return null;
            if (!SendCharacteristicUpgrade(pid, allocation)) return null;
            if (ordered) TransportLog.Line(pid, $"[carac] plan ordonné : {stat} {baseValue} -> {target}, budget {pool - expectedPool}, confirmation en attente.");
        }
        for (int i = 0; i < 80; i++)
        {
            await Task.Delay(50, ct).ConfigureAwait(false);
            if (!_statSheets.TryGetValue(pid, out var after) || !ReferenceEquals(before.Session, after.Session) || after.CharacterId != before.CharacterId || after.CharacterId != CharacterId(pid)) return null;
            int remaining = after.Pool;
            if (!ReferenceEquals(before, after) && after.Bases.TryGetValue(id, out int actual) && actual == target && remaining == expectedPool
                && CharacteristicUpgradePolicy.Stats.All(s => s.Id == id || (before.Bases.TryGetValue(s.Id,out int prior) && after.Bases.TryGetValue(s.Id,out int now) && prior == now)))
            {
                if (ordered) TransportLog.Line(pid, $"[carac] plan ordonné confirmé : {stat}={target}, points restants={remaining}.");
                return remaining;
            }
        }
        return null;
    }

    /// <summary>Vrai si une boucle d'auto level-up est en cours pour ce pid (upgradeStat est alors ignoré).</summary>
    public bool AutoLevelActive(uint pid) { lock (_autoLevelLock) return _autoLevelActive.Contains(pid); }
}
