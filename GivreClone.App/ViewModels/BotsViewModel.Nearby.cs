using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.ViewModels;

/// <summary>Un lieu utile proche (repère de la carte du monde : banque, hôtel des ventes, atelier, zaap…).</summary>
public sealed record NearbyPlaceRow(string Glyph, string Name, string Category, string DistanceText, long MapId, string Coords);

// ===================== Onglet Carte : « À proximité » =====================
// Repères officiels du jeu (HintData) les plus proches de la carte du bot affiché, UN par catégorie (la plus proche), triés par
// distance en cartes. « Y aller » lance le moteur de trajet (marche / zaap / havre-sac) sur un hôte transitoire, comme le suivi
// d'équipe ; refusé si un script tourne (il garde la main sur les déplacements). Annulable.
public partial class BotsViewModel
{
    public ObservableCollection<NearbyPlaceRow> NearbyPlaces { get; } = new();
    public bool HasNearby => NearbyPlaces.Count > 0;
    [ObservableProperty] public partial string NearbyStatus { get; set; } = "";
    public bool HasNearbyStatus => NearbyStatus.Length > 0;
    partial void OnNearbyStatusChanged(string value) => OnPropertyChanged(nameof(HasNearbyStatus));
    [ObservableProperty] public partial bool NearbyTraveling { get; set; }

    private long _nearbyMap = -1;
    private int _nearbyPid;
    private CancellationTokenSource? _nearbyCts;

    private void DriveNearbyTravel()
    {
        var bot = SelectedBot;
        long map = bot?.MapId ?? 0;
        int pid = bot?.Pid ?? 0;
        if (map == _nearbyMap && pid == _nearbyPid) return;
        _nearbyMap = map; _nearbyPid = pid;
        RebuildNearby(map);
    }

    private void RebuildNearby(long map)
    {
        NearbyPlaces.Clear();
        var launch = Stores.Launch;
        if (map > 0 && launch.GameData is { } db && launch.MapCoordOf(map) is (int x, int y))
        {
            var origin = new PlaceOrigin(x, y, launch.WorldMapOf(map) ?? 1);
            IReadOnlyList<WorldPlace>? all = null;
            try { all = db.Places("", origin, 50); } catch { /* base locale illisible : panneau vide */ }
            foreach (var p in (all ?? Array.Empty<WorldPlace>())
                         .Where(p => p.Distance is not null && p.MapId > 0)
                         .GroupBy(p => p.Category).Select(g => g.First())
                         .OrderBy(p => p.Distance).Take(6))
                NearbyPlaces.Add(new NearbyPlaceRow(GlyphOf(p.Category), p.Name, p.Category,
                    p.Distance == 0 ? Loc.T("L.Near.Here") : Loc.Fmt("L.Near.Maps", p.Distance), p.MapId, p.Coords));
        }
        OnPropertyChanged(nameof(HasNearby));
    }

    private static string GlyphOf(string category)
    {
        string c = PlaceText.Normalize(category);
        return c.Contains("banque") || c.Contains("bank") || c.Contains("banco") ? "🏦"
             : c.Contains("vente") || c.Contains("auction") || c.Contains("subasta") || c.Contains("commerce") ? "⚖"
             : c.Contains("atelier") || c.Contains("workshop") || c.Contains("taller") || c.Contains("artisan") ? "🔨"
             : c.Contains("zaap") ? "◈"
             : c.Contains("donjon") || c.Contains("dungeon") || c.Contains("mazmorra") ? "⚔"
             : "•";
    }

    [RelayCommand]
    private void NearbyGo(NearbyPlaceRow? place)
    {
        if (place is null || SelectedBot is not { Pid: > 0 } bot) return;
        if (_luaHosts.TryGetValue(bot.Pid, out var h) && h.Running) { NearbyStatus = Loc.T("L.Near.ScriptRunning"); return; }
        if (NearbyTraveling) { NearbyStatus = Loc.T("L.Near.Busy"); return; }
        if (bot.MapId == place.MapId) { NearbyStatus = Loc.Fmt("L.Near.Arrived", place.Name); return; }
        int pid = bot.Pid;
        var cts = new CancellationTokenSource();
        _nearbyCts = cts;
        NearbyTraveling = true;
        NearbyStatus = Loc.Fmt("L.Near.Going", place.Name, place.Coords);
        _ = Task.Run(async () =>
        {
            var host = NewFollowHost(pid);
            bool ok = false;
            try { ok = await host.TravelToMapAsync(place.MapId, cts.Token).ConfigureAwait(false); }
            catch { /* annulé / échec : rapporté ci-dessous */ }
            finally
            {
                try { host.Dispose(); } catch { }
                bool cancelled = cts.IsCancellationRequested;
                cts.Dispose();
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_nearbyCts, cts)) _nearbyCts = null;
                    NearbyTraveling = false;
                    NearbyStatus = cancelled ? Loc.T("L.Near.Cancelled")
                                 : ok ? Loc.Fmt("L.Near.Arrived", place.Name)
                                 : host.LastTravelResult?.Reason ?? Loc.Fmt("L.Near.Failed", place.Name);
                    if (!ok && !cancelled) Toasts.Show(NearbyStatus, success: false);
                });
            }
        });
    }

    [RelayCommand] private void NearbyStop() { try { _nearbyCts?.Cancel(); } catch { } }
}
