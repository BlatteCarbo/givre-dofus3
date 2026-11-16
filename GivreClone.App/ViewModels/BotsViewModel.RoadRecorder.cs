using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// ===================== Enregistreur de trajet (« Road Creator ») =====================
public partial class BotsViewModel
{
    // ======================= ENREGISTREUR DE TRAJET (« Road Creator ») =======================
    private readonly RoadRecorder _road = new(Stores.Launch);
    [ObservableProperty] public partial bool Recording { get; set; }              // en cours d'enregistrement (pilote le libellé du bouton)
    [ObservableProperty] public partial bool MapPlanVisible { get; set; }         // modale « récolter/combattre » ouverte
    [ObservableProperty] public partial bool MapPlanGather { get; set; }          // case « Récolter » de la modale
    [ObservableProperty] public partial bool MapPlanFight { get; set; }           // case « Combattre » de la modale
    [ObservableProperty] public partial string MapPlanTitle { get; set; } = "";   // « Carte 5,-22 »
    private TaskCompletionSource<MapPlan?>? _mapPlanTcs;

    /// <summary>Démarre l'enregistrement du trajet du bot sélectionné (observation pure ; rien n'est envoyé au jeu).</summary>
    public bool StartRecording()
    {
        if (SelectedBot is not { Pid: > 0 } bot) return false;
        long map = Stores.Launch.CurrentMap((uint)bot.Pid) ?? (bot.MapId > 0 ? bot.MapId : 0);
        if (map <= 0) { AppendLuaLine("trajet", Loc.T("L.Bots.Road.NoMap"), LuaLogLevel.Warning, bot.Pid); return false; }
        _road.Start((uint)bot.Pid, map);
        Recording = true;
        AppendLuaLine("trajet", Loc.T("L.Bots.Road.Started"), LuaLogLevel.Success, bot.Pid);
        return true;
    }

    /// <summary>Arrête l'enregistrement et renvoie le script Lua généré (vide si rien d'exploitable).</summary>
    public string StopRecording()
    {
        int pid = (int)_road.RecordingPid;
        int count = _road.StepCount;
        // L'arrêt dans le panneau principal peut précéder le clic « Confirmer » :
        // ne pas perdre la sélection affichée pour la dernière carte.
        if (MapPlanVisible && _road.IsRecording)
            _road.SetCurrentPlan(MapPlanGather, MapPlanFight);
        string lua = _road.Stop();
        Recording = false;
        MapPlanVisible = false;
        _mapPlanTcs?.TrySetResult(null);
        if (string.IsNullOrEmpty(lua)) { AppendLuaLine("trajet", Loc.T("L.Bots.Road.Empty"), LuaLogLevel.Warning, pid); return ""; }
        AppendLuaLine("trajet", Loc.Fmt("L.Bots.Road.Stopped", count), LuaLogLevel.Success, pid);
        return lua;
    }

    /// <summary>Modale par-carte (appelée par le recorder à chaque nouvelle carte) : attend le choix récolter/combattre.
    /// Une nouvelle carte SUPERSÈDE la modale précédente en conservant ce qui a été coché.</summary>
    public async Task<MapPlan?> AskMapPlanAsync(long mapId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<MapPlan?>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _mapPlanTcs?.TrySetResult(new MapPlan(MapPlanGather, MapPlanFight));   // supersède : garde ce qui a été coché
            MapPlanGather = false; MapPlanFight = false;
            MapPlanTitle = Loc.Fmt("L.Bots.Road.MapLabel", MapCoordLabel(mapId));
            _mapPlanTcs = tcs; MapPlanVisible = true;
        });
        try
        {
            await using (ct.Register(() => tcs.TrySetResult(null)))
                return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_mapPlanTcs, tcs))
                {
                    _mapPlanTcs = null;
                    MapPlanVisible = false;
                }
            });
        }
    }

    private static string MapCoordLabel(long mapId) => Stores.Launch.MapCoordOf(mapId) is (int x, int y) ? $"{x},{y}" : mapId.ToString();

    [RelayCommand]
    private void ConfirmMapPlan() { MapPlanVisible = false; _mapPlanTcs?.TrySetResult(new MapPlan(MapPlanGather, MapPlanFight)); }

    [RelayCommand]
    private void SkipMapPlan() { MapPlanVisible = false; _mapPlanTcs?.TrySetResult(new MapPlan(false, false)); }
}
