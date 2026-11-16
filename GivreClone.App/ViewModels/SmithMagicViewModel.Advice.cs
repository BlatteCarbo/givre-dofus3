using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.ForgeAdvice;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class SmithMagicViewModel
{
    private readonly ForgeAdviceController _advice = new();
    private ForgeAdviceIdentity? _adviceIdentity;
    private ForgeMode _forgeMode = ForgeMode.Automatic;
    private long _adviceObjectiveRevision;
    private bool _adviceConfigured;
    private bool _adviceActive;
    private readonly HashSet<SmithLineRow> _adviceLines = new();
    private bool _adviceFeedHooked;
    private bool _adviceConfiguring;
    private bool _advicePublishing;
    private int _adviceSessionEpoch;
    private long _adviceProcessTicks;
    private int _adviceProcessTicksPid;
    private long _adviceObservationRevision;
    private bool _adviceEffectsConfirmed;
    private Dictionary<int, int>? _adviceEffects;
    private int? _advicePool;
    public ForgeMode Mode => _forgeMode;
    public bool IsAdviceMode => Mode == ForgeMode.Advice;
    public bool IsAutomaticMode => Mode == ForgeMode.Automatic;
    public bool AdviceActive => _adviceActive;
    public ForgeAdviceRecord Advice => _advice.Current;
    /// <summary>Texte d'écran. L'état inconnu n'affiche jamais une rune.</summary>
    public string AdviceText
    {
        get
        {
            if (!IsAdviceMode || !AdviceActive) return "";
            return Advice.Status switch
            {
                ForgeAdviceStatus.Ready when Advice.RuneGid is > 0 && Advice.RuneName.Length > 0 => "Conseil : " + Advice.RuneName,
                ForgeAdviceStatus.Done => "Objectif atteint.",
                ForgeAdviceStatus.MissingRune => "Rune absente du stock utilisable.",
                _ => "État inconnu.",
            };
        }
    }
    public event Action<ForgeAdviceRecord>? AdviceChanged;
    /// <summary>
    /// Seam for the later in-game bridge. This screen never applies a rune.
    /// Only records already published through <see cref="AdviceChanged"/> are forwarded.
    /// </summary>
    public Action<ForgeAdviceRecord>? InGameAdviceBridge { get; set; }

    private void InitializeAdvice()
    {
        Lines.CollectionChanged += (_, _) =>
        {
            foreach (var line in _adviceLines.Where(l => !Lines.Contains(l)).ToArray())
            {
                line.PropertyChanged -= AdviceLineChanged;
                _adviceLines.Remove(line);
            }
            foreach (var line in Lines)
                if (_adviceLines.Add(line)) line.PropertyChanged += AdviceLineChanged;
            if (_adviceConfiguring) return;
            ClearAdviceObjective();
            if (_adviceActive) PublishAdviceObservation();
        };
        _advice.Changed += record =>
        {
            OnPropertyChanged(nameof(Advice));
            OnPropertyChanged(nameof(AdviceText));
            AdviceChanged?.Invoke(record);
            var bridge = InGameAdviceBridge;
            if (bridge is null) return;
            try { bridge(record); }
            catch { /* Le pont externe ne doit jamais bloquer le conseil. */ }
        };
        HookAdviceFeed();
    }

    private void AdviceLineChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_adviceConfiguring) return;
        if (args.PropertyName is nameof(SmithLineRow.Min) or nameof(SmithLineRow.PaFrom)
            or nameof(SmithLineRow.RaFrom) or nameof(SmithLineRow.NaturalMaximum))
        {
            ClearAdviceObjective();
            if (_adviceActive) PublishAdviceObservation();
        }
    }

    private void ClearAdviceObjective()
    {
        _adviceConfigured = false;
        _advice.ClearObjective();
    }

    /// <summary>Both mode commands are inert while the existing start/run guard owns the session.</summary>
    public bool SetForgeMode(ForgeMode mode)
    {
        if (Busy || !Enum.IsDefined(mode)) return false;
        if (_forgeMode == mode) return true;
        _forgeMode = mode;
        StopAdvice();
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsAdviceMode));
        OnPropertyChanged(nameof(IsAutomaticMode));
        OnPropertyChanged(nameof(AdviceText));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanStop));
        return true;
    }

    [RelayCommand] private void SelectAdviceMode() => SetForgeMode(ForgeMode.Advice);
    [RelayCommand] private void SelectAutomaticMode() => SetForgeMode(ForgeMode.Automatic);

    /// <summary>Parent supplies actual process creation/session/character/server identity, never a synthetic PID identity.</summary>
    public void AttachAdviceIdentity(ForgeAdviceIdentity? identity)
    {
        if (identity is not null && identity.Pid != _pid)
            throw new ArgumentException("Advice identity does not match the attached bot.");
        _adviceIdentity = identity;
        ClearAdviceSnapshot();
        StopAdvice();
        SyncAdviceItem();
    }

    private void SyncAdviceItem()
    {
        var item = Queue.FirstOrDefault(q => q.ObjectUid == _loadedUid);
        _advice.Attach(_adviceIdentity, item?.ObjectUid ?? 0, item?.Gid ?? 0);
        if (_advice.Current.ObjectiveRevision == 0) _adviceConfigured = false;
    }

    /// <summary>Ordered repairs are explicit priorities. EXO PA adds one PA above the supplied natural baseline.</summary>
    public void ConfigureAdviceFromLines(int? naturalPa = null)
    {
        SyncAdviceItem();
        var catalog = RuneItems()?.Select(p => new ForgeAdviceRune(p.Key, p.Value.EffectId, p.Value.Tier, p.Value.Name)).ToArray()
            ?? Array.Empty<ForgeAdviceRune>();
        var repairs = Lines.Where(l => !naturalPa.HasValue || l.EffectId != 111).Select((line, index) =>
        {
            var family = RuneCatalog.Of(line.EffectId) ?? throw new InvalidOperationException("Unknown repair effect.");
            var tier = RuneCatalog.TierFor(family, line.Current, line.Min, line.PaFrom, line.RaFrom);
            if (line.EffectId is 111 or 128) tier = "Ba";
            var name = RuneCatalog.ItemName(family, tier);
            var matches = catalog.Where(r => r.EffectId == line.EffectId && r.Tier == tier
                && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Exact repair rune not found in the game catalog.");
            return new ForgeAdviceRepair(line.EffectId, line.Min, index + 1, matches[0].Gid);
        }).ToArray();
        if (naturalPa.HasValue)
        {
            var pa = Lines.FirstOrDefault(l => l.EffectId == 111);
            if (pa is null)
            {
                pa = new SmithLineRow { EffectId = 111, Name = "PA", NaturalMaximum = naturalPa.Value };
                Lines.Add(pa);
            }
            pa.Min = naturalPa.Value + 1;
            pa.Max = Math.Max(pa.Max, pa.Min);
            SaveLines();
        }
        ConfigureAdviceObjective(new ForgeAdviceObjective(++_adviceObjectiveRevision, repairs, naturalPa));
    }

    /// <summary>Advanced settings seam: explicit thresholds, priorities and exact catalog GIDs.</summary>
    public void ConfigureAdviceObjective(ForgeAdviceObjective objective)
    {
        SyncAdviceItem();
        _advice.Configure(objective, RuneItems()?.Select(p =>
            new ForgeAdviceRune(p.Key, p.Value.EffectId, p.Value.Tier, p.Value.Name)) ?? Enumerable.Empty<ForgeAdviceRune>());
        _adviceObjectiveRevision = Math.Max(_adviceObjectiveRevision, objective.Revision);
        _adviceConfigured = true;
    }

    /// <summary>Call on the UI thread for received item/stock revisions, not from a timer. No gameplay calls.</summary>
    public bool ObserveAdvice(ForgeAdviceObservation observation) =>
        IsAdviceMode && AdviceActive && !Busy && _advice.Observe(observation);

    public void InvalidateAdvice() => _advice.Invalidate();

    private void StopAdvice()
    {
        _adviceActive = false;
        OnPropertyChanged(nameof(AdviceActive));
        OnPropertyChanged(nameof(AdviceText));
        OnPropertyChanged(nameof(CanStop));
        _advice.Invalidate();
    }

    private void StartAdvice()
    {
        try
        {
            if (!_adviceConfigured) ConfigureAdviceFromCurrentLines();
            _advice.Invalidate("Waiting for a fresh item and stock observation.");
            _adviceActive = true;
            OnPropertyChanged(nameof(AdviceActive));
            OnPropertyChanged(nameof(AdviceText));
            OnPropertyChanged(nameof(CanStop));
            Status = "Conseil uniquement : aucune rune ne sera appliquée.";
            PublishAdviceObservation();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            _advice.Invalidate();
            Status = "Conseil impossible : objectif incomplet.";
        }
    }

    /// <summary>EXO PA is explicit only when the PA line already asks for one point above its natural maximum.</summary>
    private int? ExplicitExoNaturalPa()
    {
        var pa = Lines.FirstOrDefault(line => line.EffectId == 111 && line.Min == line.NaturalMaximum + 1);
        return pa is null ? null : pa.NaturalMaximum;
    }

    private void ConfigureAdviceFromCurrentLines()
    {
        _adviceConfiguring = true;
        try
        {
            var naturalPa = ExplicitExoNaturalPa();
            if (naturalPa.HasValue) ConfigureAdviceFromLines(naturalPa);
            else ConfigureAdviceFromLines();
        }
        finally { _adviceConfiguring = false; }
    }

    private void ResetAdviceProcessIdentity()
    {
        _adviceSessionEpoch = 0;
        _adviceProcessTicks = 0;
        _adviceProcessTicksPid = 0;
    }

    private void ClearAdviceSnapshot()
    {
        _adviceEffectsConfirmed = false;
        _adviceEffects = null;
        _advicePool = null;
    }

    /// <summary>Identity is attached when every observed part exists, and cleared on disconnect.</summary>
    private void BindAdviceIdentity(bool itemChanged)
    {
        if (AdviceLinkDown())
        {
            if (_adviceIdentity is not null || _adviceActive) AttachAdviceIdentity(null);
            else ClearAdviceSnapshot();
            return;
        }
        var identity = ResolveAdviceIdentity();
        if (identity is null)
        {
            if (itemChanged) AttachAdviceIdentity(null);
            return;
        }
        if (!itemChanged && identity == _adviceIdentity)
        {
            SyncAdviceItem();
            if (!_adviceEffectsConfirmed) NoteCachedItemEffects();
            return;
        }
        bool restart = !itemChanged && _adviceActive;
        AttachAdviceIdentity(identity);
        NoteCachedItemEffects();
        if (restart) Status = "Identité du personnage reçue. Relance le conseil.";
    }

    private bool AdviceLinkDown()
    {
        if (_pid <= 0) return true;
        var launch = Stores.Launch;
        uint pid = (uint)_pid;
        return launch.IsLinkDown(pid) || launch.ActivityOf(pid) == BotActivity.Disconnected;
    }

    private ForgeAdviceIdentity? ResolveAdviceIdentity()
    {
        if (_pid <= 0) return null;
        var launch = Stores.Launch;
        uint pid = (uint)_pid;
        long ticks = AdviceProcessCreationTicks(_pid);
        long character = launch.CharacterId(pid);
        int server = launch.ServerIdOf(pid);
        string? accountId = launch.ScriptAccounts.FirstOrDefault(a => a.Pid == pid)?.Id;
        if (ticks <= 0 || character <= 0 || server <= 0 || string.IsNullOrWhiteSpace(accountId)) return null;
        string session = accountId + ":" + _adviceSessionEpoch.ToString(CultureInfo.InvariantCulture);
        try
        {
            return new ForgeAdviceIdentity(_pid, ticks, session,
                character.ToString(CultureInfo.InvariantCulture), server.ToString(CultureInfo.InvariantCulture));
        }
        catch (ArgumentException) { return null; }
    }

    private long AdviceProcessCreationTicks(int pid)
    {
        if (pid <= 0 || LaunchService.IsSocketPid((uint)pid)) return 0;
        if (_adviceProcessTicksPid == pid && _adviceProcessTicks > 0) return _adviceProcessTicks;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || !string.Equals(process.ProcessName, "Dofus", StringComparison.OrdinalIgnoreCase))
                return 0;
            long ticks = process.StartTime.ToUniversalTime().Ticks;
            if (ticks <= 0) return 0;
            _adviceProcessTicksPid = pid;
            _adviceProcessTicks = ticks;
            return ticks;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return 0;
        }
    }

    private void NoteCachedItemEffects()
    {
        if (_adviceEffectsConfirmed || _pid <= 0 || _loadedUid is <= 0 or > int.MaxValue) return;
        var effects = Stores.Launch.ItemEffects((uint)_pid, (int)_loadedUid);
        if (effects.Count == 0) return;
        _adviceEffectsConfirmed = true;
        _adviceEffects = ForgeState.ById(effects);
    }

    /// <summary>Bag position 63 only: runes that can be used now. Bank stock is never included. Null until the bag snapshot exists.</summary>
    private IReadOnlyDictionary<long, int>? AdviceRuneStock()
    {
        if (_pid <= 0) return null;
        var launch = Stores.Launch;
        if (!launch.Inventory.IsSeeded((uint)_pid)) return null;
        var catalog = RuneItems();
        if (catalog is not { Count: > 0 }) return null;
        var stock = new Dictionary<long, int>();
        foreach (var item in launch.Inventory.Items((uint)_pid))
        {
            if (item.Position != 63 || item.Qty <= 0 || !catalog.ContainsKey(item.Gid)) continue;
            stock[item.Gid] = stock.GetValueOrDefault(item.Gid) + item.Qty;
        }
        return stock;
    }

    private void PublishAdviceObservation()
    {
        if (_advicePublishing || !_adviceActive || !IsAdviceMode || Busy || _adviceIdentity is null) return;
        var item = Queue.FirstOrDefault(q => q.ObjectUid == _loadedUid);
        if (item is null || item.ObjectUid <= 0 || item.Gid <= 0) return;
        _advicePublishing = true;
        try
        {
            if (!_adviceEffectsConfirmed) NoteCachedItemEffects();
            if (!_adviceConfigured)
            {
                try { ConfigureAdviceFromCurrentLines(); }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    Status = "Conseil impossible : objectif incomplet.";
                    return;
                }
            }
            IReadOnlyDictionary<int, int>? effects = _adviceEffectsConfirmed
                ? _adviceEffects ?? new Dictionary<int, int>() : null;
            ObserveAdvice(new ForgeAdviceObservation(_adviceIdentity, item.ObjectUid, item.Gid,
                ++_adviceObservationRevision, effects, AdviceRuneStock(), _advicePool));
        }
        catch (ArgumentOutOfRangeException)
        {
            // Revision or item rejected: the screen stays on the unknown state.
        }
        finally { _advicePublishing = false; }
    }

    private void HookAdviceFeed()
    {
        if (_adviceFeedHooked) return;
        _adviceFeedHooked = true;
        var launch = Stores.Launch;
        launch.PacketDecoded += OnAdvicePacket;
        launch.GameLinkLost += OnAdviceLinkLost;
        launch.GameLinkRestored += OnAdviceLinkRestored;
        launch.CharacterIdentified += OnAdviceCharacter;
        launch.ServerIdentified += OnAdviceServer;
        launch.ActivityChanged += OnAdviceActivity;
    }

    private void OnAdviceLinkLost(uint pid, string reason, bool imposed) =>
        PostAdvice(() => { if (pid == (uint)_pid) DisconnectAdvice(); });

    private void OnAdviceLinkRestored(uint pid) => PostAdvice(() =>
    {
        if (pid != (uint)_pid) return;
        _adviceSessionEpoch++;
        ClearAdviceSnapshot();
        BindAdviceIdentity(itemChanged: true);
    });

    private void OnAdviceCharacter(uint pid, CharacterInfo info) =>
        PostAdvice(() => { if (pid == (uint)_pid) BindAdviceIdentity(itemChanged: false); });

    private void OnAdviceServer(uint pid, string name) =>
        PostAdvice(() => { if (pid == (uint)_pid) BindAdviceIdentity(itemChanged: false); });

    private void OnAdviceActivity(uint pid) => PostAdvice(() =>
    {
        if (pid != (uint)_pid) return;
        if (AdviceLinkDown()) { DisconnectAdvice(); return; }
        if (!IsAdviceMode && _adviceIdentity is not null) return;
        BindAdviceIdentity(itemChanged: false);
    });

    private void DisconnectAdvice()
    {
        bool wasActive = _adviceActive;
        ClearAdviceSnapshot();
        _adviceProcessTicks = 0;
        _adviceProcessTicksPid = 0;
        if (_adviceIdentity is null && !wasActive) return;
        AttachAdviceIdentity(null);
        if (wasActive) Status = "Conseil arrêté : personnage déconnecté.";
    }

    private void OnAdvicePacket(DecodedPacket packet)
    {
        try
        {
            if (_pid <= 0 || packet.Pid != (uint)_pid) return;
            if (packet.ClientToServer)
            {
                if (packet.Name is not ("CharacterSelectionRequest" or "CharacterFirstSelectionRequest")) return;
                var payload = packet.Payload;
                var name = packet.Name;
                var pid = packet.Pid;
                PostAdvice(() => ApplyAdviceFeed(pid, name, payload));
                return;
            }
            if (!IsAdviceFeedPacket(packet.Name)) return;
            if (!IsAdviceSessionPacket(packet.Name) && !IsAdviceMode) return;
            var received = packet.Payload;
            var eventName = packet.Name ?? "";
            var eventPid = packet.Pid;
            PostAdvice(() => ApplyAdviceFeed(eventPid, eventName, received));
        }
        catch
        {
            // A live observation must not interrupt packet decoding.
        }
    }

    private static bool IsAdviceSessionPacket(string? name) => name is
        "AuthenticationTicketAcceptedEvent" or "CharacterSelectionEvent";

    private static bool IsAdviceFeedPacket(string? name) => IsAdviceSessionPacket(name) || name is
        "InventoryContentEvent" or "ObjectAddedEvent" or "ObjectsAddedEvent" or "ObjectModifiedEvent"
        or "ObjectQuantityEvent" or "ObjectsQuantityEvent" or "ObjectDeletedEvent" or "ObjectsDeletedEvent"
        or "ObjectMovementEvent" or "ExchangeCraftResultEvent";

    private void ApplyAdviceFeed(uint pid, string name, IReadOnlyList<GivreCore.Protobuf.ProtoField> payload)
    {
        if (pid != (uint)_pid || _pid <= 0) return;
        if (name == "AuthenticationTicketAcceptedEvent")
        {
            _adviceSessionEpoch++;
            ClearAdviceSnapshot();
            BindAdviceIdentity(itemChanged: true);
            return;
        }
        if (name is "CharacterSelectionEvent" or "CharacterSelectionRequest" or "CharacterFirstSelectionRequest")
        {
            string? before = _adviceIdentity?.CharacterId;
            BindAdviceIdentity(itemChanged: false);
            string now = Stores.Launch.CharacterId(pid).ToString(CultureInfo.InvariantCulture);
            if (before is not null && before != now)
            {
                ClearAdviceSnapshot();
                BindAdviceIdentity(itemChanged: true);
            }
            return;
        }
        try { NoteAdviceReceipt(name, payload); }
        catch { /* Un effet illisible laisse l'état inconnu. */ }
        if (_adviceActive) PublishAdviceObservation();
    }

    private void NoteAdviceReceipt(string name, IReadOnlyList<GivreCore.Protobuf.ProtoField> payload)
    {
        switch (name)
        {
            case "InventoryContentEvent":
                NoteEffects(InventoryReader.ReadContentEffects(payload));
                break;
            case "ObjectAddedEvent":
                if (InventoryReader.ReadAddedEffects(payload) is { } added) NoteEffects([added]);
                break;
            case "ObjectsAddedEvent":
                NoteEffects(InventoryReader.ReadAddedListEffects(payload));
                break;
            case "ObjectModifiedEvent":
                if (InventoryReader.ReadModifiedEffects(payload) is { } modified) NoteEffects([modified]);
                break;
            case "ObjectDeletedEvent":
                if (InventoryReader.ReadDeletedUid(payload) is int deleted && deleted == _loadedUid) ClearAdviceSnapshot();
                break;
            case "ObjectsDeletedEvent":
                if (InventoryReader.ReadDeletedUids(payload).Any(uid => uid == _loadedUid)) ClearAdviceSnapshot();
                break;
            case "ObjectQuantityEvent":
                if (InventoryReader.ReadQuantity(payload) is { } one && one.Uid == _loadedUid && one.Qty <= 0) ClearAdviceSnapshot();
                break;
            case "ObjectsQuantityEvent":
                if (InventoryReader.ReadQuantities(payload).Any(q => q.Uid == _loadedUid && q.Qty <= 0)) ClearAdviceSnapshot();
                break;
            case "ExchangeCraftResultEvent":
                var magic = InventoryReader.ReadMagicResult(payload);
                if (!ForgeState.HasUsableResult(magic)) return;
                var item = Queue.FirstOrDefault(q => q.ObjectUid == _loadedUid);
                if (item is null || item.Gid != magic.Gid) return;
                _adviceEffectsConfirmed = true;
                _adviceEffects = ForgeState.ById(magic.Effects);
                _advicePool = magic.Pool;
                break;
        }
    }

    private void NoteEffects(IEnumerable<(int Uid, IReadOnlyList<InventoryReader.ItemEffect> Effects)> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Uid != _loadedUid) continue;
            _adviceEffectsConfirmed = true;
            _adviceEffects = ForgeState.ById(entry.Effects);
            return;
        }
    }

    private static void PostAdvice(Action action)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        }
        catch (InvalidOperationException)
        {
            // No interface thread in this host.
        }
    }
}
