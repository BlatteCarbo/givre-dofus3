using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// ===================== Onglet Inventaire : « Envoyer à la mule » =====================
// Le perso sélectionné rejoint la carte de la mule (trajet auto), lui ouvre un échange, pose tout son sac sauf les objets gardés
// (cadenas) et l'équipement porté, plus les kamas si demandé ; la mule accepte et valide toute seule. Refusé si un script tourne
// sur l'un des deux (il garde la main sur ses déplacements et ses échanges).
public partial class BotsViewModel
{
    /// <summary>Autres bots en jeu (mules possibles) pour le perso sélectionné.</summary>
    public ObservableCollection<BotRowViewModel> MuleCandidates { get; } = new();
    [ObservableProperty] public partial BotRowViewModel? SelectedMule { get; set; }
    [ObservableProperty] public partial bool MuleSendKamas { get; set; }
    [ObservableProperty] public partial string MuleStatus { get; set; } = "";
    [ObservableProperty] public partial bool MuleBusy { get; set; }
    public bool HasMuleCandidates => MuleCandidates.Count > 0;
    private CancellationTokenSource? _muleCts;
    private bool _muleLoading;

    partial void OnSelectedMuleChanged(BotRowViewModel? value)
    {
        if (_muleLoading || SelectedBot is not { } giver || value is null || string.IsNullOrWhiteSpace(giver.Row.CharacterName)) return;
        AppConfig.Current.MuleOf[giver.Row.CharacterName] = value.Row.CharacterName;   // mule retenue par perso
        AppConfig.Save();
    }

    /// <summary>Recalcule la liste des mules possibles (tick lent) en gardant la mule choisie pour ce perso.</summary>
    private void RefreshMuleCandidates()
    {
        var giver = SelectedBot;
        var others = Bots.Where(b => b.Pid > 0 && !ReferenceEquals(b, giver) && !string.IsNullOrWhiteSpace(b.Row.CharacterName)).ToList();
        if (!others.SequenceEqual(MuleCandidates))
        {
            _muleLoading = true;
            MuleCandidates.Clear();
            foreach (var b in others) MuleCandidates.Add(b);
            _muleLoading = false;
            OnPropertyChanged(nameof(HasMuleCandidates));
        }
        string? wanted = giver is not null && AppConfig.Current.MuleOf.TryGetValue(giver.Row.CharacterName ?? "", out var n) ? n : null;
        var pick = others.FirstOrDefault(b => b.Row.CharacterName == wanted)
                   ?? (SelectedMule is { } cur && others.Contains(cur) ? cur : others.FirstOrDefault());
        if (!ReferenceEquals(pick, SelectedMule)) { _muleLoading = true; SelectedMule = pick; _muleLoading = false; }
    }

    private bool ScriptRunningOn(int pid) => pid > 0 && _luaHosts.TryGetValue(pid, out var h) && h.Running;

    [RelayCommand]
    private async Task SendToMule()
    {
        if (MuleBusy) return;
        if (SelectedBot is not { Pid: > 0 } giver || SelectedMule is not { Pid: > 0 } mule) { MuleStatus = Loc.T("L.Mule.PickMule"); return; }
        if (ScriptRunningOn(giver.Pid) || ScriptRunningOn(mule.Pid)) { MuleStatus = Loc.T("L.Mule.ScriptRunning"); return; }
        var l = Stores.Launch;
        uint g = (uint)giver.Pid, m = (uint)mule.Pid;
        var cts = new CancellationTokenSource();
        _muleCts = cts;
        MuleBusy = true;
        try
        {
            // Pas sur la même carte : le donneur rejoint la mule (même moteur de trajet que les scripts).
            long muleMap = l.CurrentMap(m) ?? 0;
            if (muleMap != 0 && l.CurrentMap(g) != muleMap)
            {
                MuleStatus = Loc.Fmt("L.Mule.Going", mule.Row.CharacterName);
                var host = NewFollowHost(giver.Pid);
                bool arrived;
                try { arrived = await host.TravelToMapAsync(muleMap, cts.Token); }
                finally { try { host.Dispose(); } catch { } }
                if (!arrived || l.CurrentMap(g) != l.CurrentMap(m)) { MuleStatus = Loc.T("L.Mule.NotReached"); return; }
                await Task.Delay(800, cts.Token);
            }
            MuleStatus = Loc.Fmt("L.Mule.Trading", mule.Row.CharacterName);
            long kamas = MuleSendKamas ? Math.Max(0, l.Kamas(g)) : 0;
            var r = await l.MuleTransferAsync(g, m, giver.MuleKeep, kamas, cts.Token);
            MuleStatus = r.Done ? Loc.Fmt("L.Mule.Done", r.Stacks, r.Kamas, mule.Row.CharacterName)
                                : Loc.Fmt("L.Mule.Failed", Loc.T("L.Mule.Reason." + r.Reason));
        }
        catch (OperationCanceledException) { MuleStatus = Loc.T("L.Mule.Cancelled"); }
        catch (Exception e) { MuleStatus = Loc.Fmt("L.Mule.Failed", e.Message); }
        finally
        {
            MuleBusy = false;
            if (ReferenceEquals(_muleCts, cts)) _muleCts = null;
            cts.Dispose();
        }
    }

    [RelayCommand] private void CancelMule() { try { _muleCts?.Cancel(); } catch { } }
}
