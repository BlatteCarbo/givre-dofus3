using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>Une destination de zaap ou de zaapi dans le menu de l'en-tête. Le clic suit la décision déjà calculée.</summary>
public sealed class QuickTeleportChoice
{
    public QuickTeleportChoice(QuickTeleportLine line, Func<QuickTeleportChoice, Task> go)
    {
        MapId = line.MapId;
        Title = line.Title;
        Detail = line.Detail;
        CostText = line.CostText;
        CanSend = line.CanSend;
        Reason = line.Reason;
        Travel = new AsyncRelayCommand(() => go(this));
    }

    public long MapId { get; }
    public string Title { get; }
    public string Detail { get; }
    public string CostText { get; }
    public bool CanSend { get; }
    public string Reason { get; }
    public ICommand Travel { get; }
}

// ===================== En-tête : quitter le donjon, zaap, zaapi =====================
// Rien n'est envoyé pendant le rafraîchissement. L'énergie du personnage n'est pas une condition.
public partial class BotsViewModel
{
    public ObservableCollection<QuickTeleportChoice> QuickZaaps { get; } = new();
    public ObservableCollection<QuickTeleportChoice> QuickZaapis { get; } = new();

    [ObservableProperty] public partial bool CanQuickExitDungeon { get; set; }
    [ObservableProperty] public partial bool CanQuickZaap { get; set; }
    [ObservableProperty] public partial bool CanQuickZaapi { get; set; }
    [ObservableProperty] public partial bool QuickActionBusy { get; set; }
    [ObservableProperty] public partial bool HasQuickZaaps { get; set; }
    [ObservableProperty] public partial bool HasQuickZaapis { get; set; }
    [ObservableProperty] public partial string QuickDungeonTip { get; set; } = QuickActions.NoCharacter;
    [ObservableProperty] public partial string QuickZaapTip { get; set; } = QuickActions.NoCharacter;
    [ObservableProperty] public partial string QuickZaapiTip { get; set; } = QuickActions.NoCharacter;
    [ObservableProperty] public partial string QuickActionStatus { get; set; } = "";
    [ObservableProperty] public partial string QuickZaapEmpty { get; set; } = WorldMapZaapTravel.UnknownListReason;
    [ObservableProperty] public partial string QuickZaapiEmpty { get; set; } = ZaapiTravel.ListMissingReason;
    [ObservableProperty] public partial string ZaapSearch { get; set; } = "";
    [ObservableProperty] public partial string ZaapiSearch { get; set; } = "";

    public string QuickZaapHint => QuickActions.ZaapHint;
    public string QuickZaapiHint => QuickActions.ZaapiHint;

    private bool _quickReady;
    private int _quickGate;
    private int _quickRefreshDepth;
    private int _quickPid;
    private bool _quickStatusLive = true;
    private string _quickHoldSig = "";
    private string _zaapListSig = "";
    private string _zaapiListSig = "";
    private long _zaapiPlanMap;
    private string _zaapiPlanSeen = "";
    private IReadOnlyList<int>? _zaapiPlanSkills;
    private ZaapiHost? _zaapiPlan;
    private CancellationTokenSource? _quickCts;
    private readonly Dictionary<int, HashSet<long>> _zaapiSeen = new();

    partial void OnZaapSearchChanged(string value)
    {
        if (_quickReady && _quickRefreshDepth == 0) RefreshQuickActions();
    }

    partial void OnZaapiSearchChanged(string value)
    {
        if (_quickReady && _quickRefreshDepth == 0) RefreshQuickActions();
    }

    /// <summary>Recalcule l'état affiché. N'ouvre aucun dialogue et n'envoie aucune trame.</summary>
    public void RefreshQuickActions()
    {
        if (!_quickReady || _quickRefreshDepth > 0) return;
        _quickRefreshDepth++;
        try
        {
            var bot = SelectedBot;
            if (bot is not { Pid: > 0 })
            {
                CanQuickExitDungeon = false;
                CanQuickZaap = false;
                CanQuickZaapi = false;
                QuickDungeonTip = QuickActions.NoCharacter;
                QuickZaapTip = QuickActions.NoCharacter;
                QuickZaapiTip = QuickActions.NoCharacter;
                HasQuickZaaps = false;
                HasQuickZaapis = false;
                QuickZaapEmpty = WorldMapZaapTravel.UnknownListReason;
                QuickZaapiEmpty = ZaapiTravel.ListMissingReason;
                if (QuickZaaps.Count > 0) { QuickZaaps.Clear(); _zaapListSig = ""; }
                if (QuickZaapis.Count > 0) { QuickZaapis.Clear(); _zaapiListSig = ""; }
                HoldOrLive(QuickActions.NoCharacter);
                return;
            }

            if (bot.Pid != _quickPid)
            {
                _quickPid = bot.Pid;
                if (ZaapSearch.Length > 0) ZaapSearch = "";
                if (ZaapiSearch.Length > 0) ZaapiSearch = "";
            }

            int energy = Stores.Launch.SelfEnergy((uint)bot.Pid)?.Cur ?? -1;
            bool inFight = bot.FightActive || Stores.Launch.IsInFight((uint)bot.Pid);
            if (inFight && QuickActionBusy)
                try { _quickCts?.Cancel(); } catch { /* déjà annulé */ }
            bot.RefreshTravelShortcuts();
            bool onZaapi = ZaapiVisible(bot);
            if (onZaapi && bot.MapId > 0)
            {
                if (!_zaapiSeen.TryGetValue(bot.Pid, out var seen))
                    _zaapiSeen[bot.Pid] = seen = new HashSet<long>();
                seen.Add(bot.MapId);
            }
            var nearest = PlanZaapi(bot);
            long kamas = Stores.Launch.Kamas((uint)bot.Pid);

            var known = new List<(long MapId, long? CostKamas, string Label)>();
            foreach (var quote in Stores.Launch.WorldMapZaapQuotes((uint)bot.Pid))
                known.Add((quote.MapId, quote.CostKamas, PlaceLabel(quote.MapId)));
            var zaapRows = QuickActions.ZaapChoices(known, bot.MapId, kamas, ZaapSearch, energy);
            Publish(QuickZaaps, zaapRows, ZaapSearch, ref _zaapListSig, QuickZaapAsync);
            HasQuickZaaps = zaapRows.Count > 0;
            QuickZaapEmpty = known.Count == 0 ? WorldMapZaapTravel.UnknownListReason : QuickActions.ZaapNoMatch;

            IReadOnlyList<ZaapiStop> stops;
            try { stops = Stores.Launch.ZaapiStops((uint)bot.Pid); }
            catch { stops = Array.Empty<ZaapiStop>(); }
            var zaapiRows = QuickActions.ZaapiChoices(stops, bot.MapId, kamas, ZaapiSearch, energy);
            Publish(QuickZaapis, zaapiRows, ZaapiSearch, ref _zaapiListSig, QuickZaapiAsync);
            HasQuickZaapis = zaapiRows.Count > 0;
            QuickZaapiEmpty = stops.Count == 0 ? ZaapiTravel.ListMissingReason : QuickActions.ZaapiNoMatch;

            var dungeon = QuickActions.AdmitDungeon(true, inFight, bot.InDungeon, QuickActionBusy, energy);
            CanQuickExitDungeon = dungeon.Fire;
            QuickDungeonTip = QuickActions.DungeonTip(true, inFight, bot.InDungeon, QuickActionBusy, bot.DungeonHere, energy);
            CanQuickZaap = QuickActions.CanBrowseZaap(true, inFight, QuickActionBusy, known.Count, energy);
            QuickZaapTip = QuickActions.ZaapTip(true, inFight, QuickActionBusy, known.Count, energy);
            CanQuickZaapi = QuickActions.CanBrowseZaapi(true, inFight, QuickActionBusy, onZaapi, nearest, energy);
            QuickZaapiTip = QuickActions.ZaapiTip(true, inFight, QuickActionBusy, onZaapi, nearest, energy);
            HoldOrLive(QuickActions.LiveSummary(inFight, QuickDungeonTip, QuickZaapTip, QuickZaapiTip, energy));
        }
        catch
        {
            CanQuickExitDungeon = false;
            CanQuickZaap = false;
            CanQuickZaapi = false;
            QuickDungeonTip = QuickActions.Interrupted;
            QuickZaapTip = QuickActions.Interrupted;
            QuickZaapiTip = QuickActions.Interrupted;
        }
        finally { _quickRefreshDepth--; }
    }

    /// <summary>Menu Zaapi ouvert sans destinations connues : ouvre le zaapi de la carte, seule source de la liste.</summary>
    public void RequestZaapiListIfMissing()
    {
        if (SelectedBot is not { Pid: > 0 } bot || QuickActionBusy || !ZaapiVisible(bot)) return;
        if (bot.FightActive || Stores.Launch.IsInFight((uint)bot.Pid)) return;
        if (Stores.Launch.ZaapiStops((uint)bot.Pid).Count == 0) bot.OpenZaapiHereCommand.Execute(null);
    }

    [RelayCommand]
    private async Task QuickExitDungeon()
    {
        if (Interlocked.CompareExchange(ref _quickGate, 1, 0) != 0) return;
        CancellationTokenSource? cts = null;
        int pid = 0;
        try
        {
            var bot = SelectedBot;
            pid = bot?.Pid ?? 0;
            int energy = pid > 0 ? Stores.Launch.SelfEnergy((uint)pid)?.Cur ?? -1 : -1;
            bool inFight = bot is not null && (bot.FightActive || Stores.Launch.IsInFight((uint)pid));
            var admit = QuickActions.AdmitDungeon(bot is { Pid: > 0 }, inFight, bot?.InDungeon == true, false, energy);
            if (inFight) admit = new QuickAdmission(false, QuickActions.Fight);
            if (!admit.Fire || bot is null)
            {
                ShowQuick(admit.Status, false);
                return;
            }
            cts = new CancellationTokenSource();
            _quickCts = cts;
            QuickActionBusy = true;
            CanQuickExitDungeon = false;
            CanQuickZaap = false;
            CanQuickZaapi = false;
            _quickStatusLive = false;
            string name = string.IsNullOrWhiteSpace(bot.DungeonHere) ? "donjon" : bot.DungeonHere!;
            QuickActionStatus = "Sortie du donjon « " + name + " »…";
            var (ok, reason, mapId) = await Stores.Launch.DungeonExitAsync((uint)pid, cts.Token);
            string text = QuickActions.DungeonResult(ok, reason, mapId);
            await OnUi(() => { if (SelectedBot?.Pid == pid) ShowQuick(text, ok); });
        }
        catch (OperationCanceledException)
        {
            await OnUi(() => { if (SelectedBot?.Pid == pid) ShowQuick(QuickActions.Cancelled, false); });
        }
        catch
        {
            await OnUi(() => { if (SelectedBot?.Pid == pid) ShowQuick(QuickActions.Interrupted, false); });
        }
        finally
        {
            await ReleaseQuick(cts, null);
        }
    }

    [RelayCommand]
    private void QuickCancel()
    {
        try { _quickCts?.Cancel(); } catch { /* déjà terminé */ }
    }

    private Task QuickZaapAsync(QuickTeleportChoice choice) => TravelQuick(choice, false);

    private Task QuickZaapiAsync(QuickTeleportChoice choice) => TravelQuick(choice, true);

    private async Task TravelQuick(QuickTeleportChoice choice, bool zaapi)
    {
        if (Interlocked.CompareExchange(ref _quickGate, 1, 0) != 0) return;
        CancellationTokenSource? cts = null;
        GivreLuaHost? host = null;
        int pid = 0;
        try
        {
            var bot = SelectedBot;
            pid = bot?.Pid ?? 0;
            int energy = pid > 0 ? Stores.Launch.SelfEnergy((uint)pid)?.Cur ?? -1 : -1;
            bool inFight = bot is not null && (bot.FightActive || (pid > 0 && Stores.Launch.IsInFight((uint)pid)));
            bool script = bot is not null && _luaHosts.TryGetValue(bot.Pid, out var running) && running.Running;
            bool onMap = zaapi && ZaapiVisible(bot);
            var nearest = zaapi && bot is not null ? PlanZaapi(bot) : null;
            var line = new QuickTeleportLine(choice.MapId, choice.Title, choice.Detail, choice.CostText, choice.CanSend, choice.Reason);
            var admit = zaapi
                ? QuickActions.AdmitZaapi(bot is { Pid: > 0 }, inFight, script, false, onMap, nearest, bot?.MapId ?? 0, line, energy)
                : QuickActions.AdmitZaap(bot is { Pid: > 0 }, inFight, script, false, line, energy);
            if (inFight)
                admit = new QuickAdmission(false, zaapi ? ZaapiTravel.CombatReason : WorldMapZaapTravel.CombatReason);
            if (!admit.Fire || bot is null)
            {
                ShowQuick(admit.Status, false);
                return;
            }
            if (Stores.Launch.IsInFight((uint)pid))
            {
                ShowQuick(zaapi ? ZaapiTravel.CombatReason : WorldMapZaapTravel.CombatReason, false);
                return;
            }
            cts = new CancellationTokenSource();
            _quickCts = cts;
            QuickActionBusy = true;
            CanQuickExitDungeon = false;
            CanQuickZaap = false;
            CanQuickZaapi = false;
            _quickStatusLive = false;
            QuickActionStatus = zaapi && admit.HostMap > 0
                ? "Rejoint le zaapi, puis téléporte vers " + choice.Title + "…"
                : (zaapi ? "Zaapi vers " : "Zaap vers ") + choice.Title + "…";
            long dest = choice.MapId;
            long hostMap = admit.HostMap;
            host = NewFollowHost(pid);
            (bool Ok, string Reason) result;
            try
            {
                result = zaapi
                    ? await host.QuickZaapiAsync(dest, hostMap, cts.Token)
                    : await host.QuickWorldZaapAsync(dest, cts.Token);
            }
            catch (OperationCanceledException) { result = (false, QuickActions.Cancelled); }
            catch { result = (false, QuickActions.Interrupted); }
            string text = string.IsNullOrWhiteSpace(result.Reason) ? QuickActions.Interrupted : result.Reason;
            bool ok = result.Ok;
            await OnUi(() =>
            {
                if (SelectedBot?.Pid == pid) ShowQuick(text, ok);
                else if (!ok) Toasts.Show(text, success: false);
            });
        }
        catch (OperationCanceledException)
        {
            await OnUi(() => { if (SelectedBot?.Pid == pid) ShowQuick(QuickActions.Cancelled, false); });
        }
        catch
        {
            await OnUi(() => { if (SelectedBot?.Pid == pid) ShowQuick(QuickActions.Interrupted, false); });
        }
        finally
        {
            await ReleaseQuick(cts, host);
        }
    }

    private void ShowQuick(string text, bool ok, bool toast = true)
    {
        QuickActionStatus = text;
        _quickStatusLive = false;
        _quickHoldSig = SituationSig();
        if (toast) Toasts.Show(text, ok);
    }

    private async Task ReleaseQuick(CancellationTokenSource? cts, GivreLuaHost? host)
    {
        await OnUi(() =>
        {
            QuickActionBusy = false;
            if (cts is not null && ReferenceEquals(_quickCts, cts)) _quickCts = null;
            Interlocked.Exchange(ref _quickGate, 0);
            RefreshQuickActions();
        });
        try { host?.Dispose(); } catch { /* hôte déjà fermé */ }
        try { cts?.Dispose(); } catch { /* déjà libéré */ }
    }

    private static async Task OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private void HoldOrLive(string live)
    {
        if (QuickActionBusy) return;
        string sig = SituationSig();
        if (!_quickStatusLive && sig == _quickHoldSig) return;
        _quickStatusLive = true;
        QuickActionStatus = live;
    }

    private string SituationSig()
    {
        var bot = SelectedBot;
        if (bot is not { Pid: > 0 }) return "0";
        bool fight = bot.FightActive || Stores.Launch.IsInFight((uint)bot.Pid);
        return bot.Pid.ToString(CultureInfo.InvariantCulture) + "|"
            + bot.MapId.ToString(CultureInfo.InvariantCulture) + "|"
            + (fight ? "1" : "0") + "|" + (bot.DungeonHere ?? "");
    }

    private bool ZaapiVisible(BotRowViewModel? bot)
    {
        if (bot is not { Pid: > 0 }) return false;
        try
        {
            foreach (var interactive in Stores.World.Get((uint)bot.Pid).Interactives)
                if (UsableZaapi(interactive)) return true;
            if (bot.MapId <= 0) return false;
            foreach (var interactive in Stores.Launch.PlacelessInteractives((uint)bot.Pid, bot.MapId))
                if (UsableZaapi(interactive)) return true;
        }
        catch { return false; }
        return false;
    }

    private static bool UsableZaapi(MapInteractive interactive)
        => ZaapiTravel.IsUsable(interactive.ElementTypeId, interactive.Skills.Select(s => (s.SkillId, s.Enabled)));

    private ZaapiHost? PlanZaapi(BotRowViewModel bot)
    {
        if (bot.MapId <= 0) return null;
        _zaapiSeen.TryGetValue(bot.Pid, out var seen);
        string seenKey = seen is null ? "" : string.Join(',', seen.OrderBy(id => id));
        IReadOnlyList<int> skills;
        try { skills = Stores.Launch.ZaapiSkillMaps(); }
        catch { skills = Array.Empty<int>(); }
        if (bot.MapId == _zaapiPlanMap && seenKey == _zaapiPlanSeen && ReferenceEquals(skills, _zaapiPlanSkills))
            return _zaapiPlan;
        ZaapiHost? found = null;
        try
        {
            if (Stores.Launch.NearestZaapiHost(bot.MapId, seen) is { } hit)
                found = new ZaapiHost(hit.MapId, hit.Hops);
        }
        catch { found = null; }
        _zaapiPlanMap = bot.MapId;
        _zaapiPlanSeen = seenKey;
        _zaapiPlanSkills = skills;
        _zaapiPlan = found;
        return found;
    }

    private string PlaceLabel(long mapId)
    {
        try
        {
            string coords = "";
            if (Stores.Launch.MapCoordOf(mapId) is (int x, int y))
                coords = x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
            string? name = null;
            int sub = Stores.Launch.SubAreaOf(mapId);
            if (sub > 0) name = Stores.Launch.GameData?.SubArea(sub)?.Name;
            if (!string.IsNullOrWhiteSpace(name) && coords.Length > 0) return name.Trim() + " (" + coords + ")";
            if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();
            if (coords.Length > 0) return coords;
        }
        catch { /* libellé de repli : l'id de carte */ }
        return mapId.ToString(CultureInfo.InvariantCulture);
    }

    private void Publish(ObservableCollection<QuickTeleportChoice> target, IReadOnlyList<QuickTeleportLine> rows,
        string query, ref string signature, Func<QuickTeleportChoice, Task> go)
    {
        var next = new System.Text.StringBuilder(query);
        foreach (var row in rows)
            next.Append('\n').Append(row.MapId).Append('|').Append(row.CanSend ? '1' : '0').Append('|')
                .Append(row.CostText).Append('|').Append(row.Title).Append('|').Append(row.Detail).Append('|').Append(row.Reason);
        string sig = next.ToString();
        if (sig == signature) return;
        signature = sig;
        target.Clear();
        foreach (var row in rows)
            target.Add(new QuickTeleportChoice(row, go));
    }
}
