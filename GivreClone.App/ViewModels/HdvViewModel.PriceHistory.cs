using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne brute issue de ObjectAveragePricesHistoryEvent, formatée pour lecture.</summary>
public sealed record HdvPriceHistoryLine(string DateText, string PriceText, string SoldText);

/// <summary>
/// Historique HDV consultatif : AUCUN appel n'est initié par un changement de sélection.
/// Une lecture réseau ne peut démarrer qu'avec le bouton dédié. La réponse du protocole
/// ne contient pas de GID attesté : le contexte de demande est indiqué, sans corrélation garantie.
/// </summary>
public partial class HdvViewModel
{
    private const int PriceHistoryMaxRows = 30;
    private const int PriceHistoryMaxCachedItems = 12;
    private readonly Dictionary<(uint Pid, string CharacterScope, long Gid), PriceHistoryCacheEntry> _priceHistoryCache = new();
    private CancellationTokenSource? _priceHistoryRequest;
    private uint _priceHistoryCachePid;
    private string _priceHistoryCacheCharacterScope = "";

    private sealed record PriceHistoryCacheEntry(
        DateTimeOffset ReceivedAt, int ReceivedCount, IReadOnlyList<LotMReader.PricePoint> Points);

    public ObservableCollection<HdvPriceHistoryLine> PriceHistoryRows { get; } = new();
    /// <summary>Consultative projection of received server points; NEVER a fabricated price series.</summary>
    public ObservableCollection<HdvPriceHistoryLine> PriceHistoryWindowRows { get; } = new();
    [ObservableProperty] public partial int PriceHistoryWindowDays { get; private set; } = 30;
    [ObservableProperty] public partial bool PriceHistoryHasChart { get; private set; }
    [ObservableProperty] public partial List<Point> PriceHistoryLinePoints { get; private set; } = new();
    [ObservableProperty] public partial List<Point> PriceHistoryAreaPoints { get; private set; } = new();
    [ObservableProperty] public partial string PriceHistoryWindowStatus { get; private set; } =
        "Aucune série de prix horodatée reçue du serveur.";

    public bool PriceHistoryIs24h => PriceHistoryWindowDays == 1;
    public bool PriceHistoryIs7d => PriceHistoryWindowDays == 7;
    public bool PriceHistoryIs30d => PriceHistoryWindowDays == 30;

    [RelayCommand]
    private void SetPriceHistoryWindow(string? days)
    {
        if (!int.TryParse(days, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value is not (1 or 7 or 30)) return;
        PriceHistoryWindowDays = value;
        OnPropertyChanged(nameof(PriceHistoryIs24h));
        OnPropertyChanged(nameof(PriceHistoryIs7d));
        OnPropertyChanged(nameof(PriceHistoryIs30d));
        RefreshPriceHistoryChart();
    }

    [ObservableProperty] public partial HdvListingRow? PriceHistorySelection { get; set; }
    [ObservableProperty] public partial bool PriceHistoryBusy { get; set; }
    [ObservableProperty] public partial bool PriceHistoryHasRows { get; set; }
    [ObservableProperty] public partial string PriceHistoryStatus { get; set; } =
        "Non reçu · sélectionnez un lot, puis demandez l'historique.";
    [ObservableProperty] public partial string PriceHistorySource { get; set; } = "";
    [ObservableProperty] public partial string PriceHistorySelectionLabel { get; set; } = "";

    /// <summary>Ce bool sert seulement au bouton de LECTURE. Le garde est revérifié dans la commande.</summary>
    public bool CanReadPriceHistory => !PriceHistoryBusy && CanHdv && _pid != 0
        && PriceHistorySelection is { Gid: > 0 and <= int.MaxValue } row
        && ListingsView.Contains(row) && _launch.HdvAnyOpen(_pid);

    partial void OnPriceHistorySelectionChanged(HdvListingRow? row)
    {
        _priceHistoryRequest?.Cancel();
        PriceHistoryBusy = false;
        PriceHistoryRows.Clear();
        PriceHistoryHasRows = false;
        PriceHistorySource = "";
        ClearPriceHistoryChart();
        PriceHistorySelectionLabel = row is null ? "" : $"{row.Name} · GID {row.Gid} · {row.LotText}";
        PriceHistoryStatus = row is null
            ? "Non reçu · sélectionnez un lot, puis demandez l'historique."
            : "Non reçu pour ce lot · cliquez sur Lire l'historique.";
        EnsurePriceHistoryScope();

        if (row is not null && _priceHistoryCache.TryGetValue((_pid, PriceHistoryCharacterScope, row.Gid), out var cached))
            DisplayPriceHistory(cached, fromCache: true);
        OnPropertyChanged(nameof(CanReadPriceHistory));
    }

    private string PriceHistoryCharacterScope => _hdvProfileKey ?? _charName ?? "";

    private void ResetPriceHistorySession()
    {
        _priceHistoryRequest?.Cancel();
        _priceHistoryRequest = null;
        _priceHistoryCache.Clear();
        _priceHistoryCachePid = _pid;
        _priceHistoryCacheCharacterScope = PriceHistoryCharacterScope;
        PriceHistorySelection = null;
        PriceHistoryRows.Clear();
        PriceHistoryHasRows = false;
        PriceHistoryBusy = false;
        PriceHistorySource = "";
        ClearPriceHistoryChart();
        PriceHistoryStatus = "Non reçu · sélectionnez un lot, puis demandez l'historique.";
        OnPropertyChanged(nameof(CanReadPriceHistory));
    }

    private void EnsurePriceHistoryScope()
    {
        string scope = PriceHistoryCharacterScope;
        if (_priceHistoryCachePid == _pid &&
            string.Equals(_priceHistoryCacheCharacterScope, scope, StringComparison.Ordinal)) return;
        _priceHistoryRequest?.Cancel();
        _priceHistoryCachePid = _pid;
        _priceHistoryCacheCharacterScope = scope;
        _priceHistoryCache.Clear();
    }

    private void DisplayPriceHistory(PriceHistoryCacheEntry data, bool fromCache)
    {
        PriceHistoryRows.Clear();
        foreach (var point in data.Points.Reverse())
        {
            PriceHistoryRows.Add(new HdvPriceHistoryLine(
                string.IsNullOrWhiteSpace(point.Date) ? "Date absente" : point.Date,
                point.Price.ToString("N0", CultureInfo.CurrentUICulture),
                point.Sold.ToString("N0", CultureInfo.CurrentUICulture)));
        }
        PriceHistoryHasRows = PriceHistoryRows.Count > 0;
        PriceHistorySource = (fromCache ? "Cache mémoire · " : "Réception locale · ")
            + data.ReceivedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        PriceHistoryStatus = data.ReceivedCount == 0
            ? "Réponse reçue, aucun point d'historique transmis."
            : data.ReceivedCount > PriceHistoryMaxRows
                ? $"{data.ReceivedCount} points reçus · {PriceHistoryMaxRows} dernières lignes affichées."
                : $"{data.ReceivedCount} point(s) reçu(s) pour la demande sélectionnée.";
        RefreshPriceHistoryChart();
    }

    private void ClearPriceHistoryChart()
    {
        PriceHistoryWindowRows.Clear();
        PriceHistoryLinePoints = new();
        PriceHistoryAreaPoints = new();
        PriceHistoryHasChart = false;
        PriceHistoryWindowStatus = "Aucune série de prix horodatée reçue du serveur.";
    }

    private void RefreshPriceHistoryChart()
    {
        ClearPriceHistoryChart();
        if (PriceHistorySelection is not { } item ||
            !_priceHistoryCache.TryGetValue((_pid, PriceHistoryCharacterScope, item.Gid), out var source))
            return;

        // The source contains daily average-price points, not a median nor an
        // executable HDV quote. Dates must parse before 24 h / 7 d / 30 d filtering.
        var dated = new List<(DateTimeOffset Day, LotMReader.PricePoint Point)>();
        foreach (var point in source.Points)
        {
            if (point.Price < 0 || point.Sold < 0 ||
                !DateTimeOffset.TryParse(point.Date, CultureInfo.GetCultureInfo("fr-FR"),
                    DateTimeStyles.AssumeLocal, out var day) &&
                !DateTimeOffset.TryParse(point.Date, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out day))
            {
                PriceHistoryWindowStatus = "Historique reçu, mais dates/prix non interprétables : voir les lignes brutes dans Bots → HDV.";
                return;
            }
            dated.Add((day, point));
        }

        var cutoff = DateTimeOffset.Now.AddDays(-PriceHistoryWindowDays);
        var window = dated.Where(p => p.Day >= cutoff && p.Day <= DateTimeOffset.Now.AddHours(2))
            .OrderBy(p => p.Day).TakeLast(30).ToArray();
        foreach (var entry in window.Reverse())
            PriceHistoryWindowRows.Add(new HdvPriceHistoryLine(
                entry.Day.LocalDateTime.ToString("dd/MM HH:mm", CultureInfo.CurrentUICulture),
                entry.Point.Price.ToString("N0", CultureInfo.CurrentUICulture),
                entry.Point.Sold.ToString("N0", CultureInfo.CurrentUICulture)));
        if (window.Length < 2)
        {
            PriceHistoryWindowStatus = window.Length == 0
                ? $"Aucun point reçu sur les {PriceHistoryWindowDays} derniers jours."
                : "Un seul point reçu : courbe non affichée pour éviter un graphique trompeur.";
            return;
        }

        long min = window.Min(p => p.Point.Price), max = window.Max(p => p.Point.Price);
        double range = Math.Max(1.0, (double)max - min);
        var curve = window.Select((entry, i) => new Point(
            14 + (960.0 * i / (window.Length - 1)),
            224 - 190 * (entry.Point.Price - (double)min) / range)).ToList();
        PriceHistoryLinePoints = curve;
        PriceHistoryAreaPoints = new List<Point> { new(curve[0].X, 235) }
            .Concat(curve).Append(new Point(curve[^1].X, 235)).ToList();
        PriceHistoryHasChart = true;
        PriceHistoryWindowStatus = $"{window.Length} point(s) daté(s) transmis par le serveur · prix moyens observés, pas prix HDV temps réel.";
    }

    [RelayCommand]
    private async Task ReadPriceHistoryAsync()
    {
        EnsurePriceHistoryScope();
        if (!CanReadPriceHistory || PriceHistorySelection is not { } selected)
        {
            PriceHistoryStatus = "Lecture indisponible · sélectionnez un lot et ouvrez son HDV.";
            return;
        }

        uint pid = _pid;
        long gid = selected.Gid;
        _priceHistoryRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _priceHistoryRequest = request;
        PriceHistoryBusy = true;
        OnPropertyChanged(nameof(CanReadPriceHistory));
        PriceHistoryStatus = "Lecture demandée · en attente de la réponse serveur…";
        try
        {
            // Un clic utilisateur = au plus une requête. Aucun rafraîchissement périodique.
            var points = await _launch.PriceHistoryAsync(pid, gid, request.Token);
            if (request.IsCancellationRequested || _pid != pid ||
                !ReferenceEquals(selected, PriceHistorySelection)) return;

            if (points is null)
            {
                PriceHistoryStatus = "Non reçu · pas de réponse exploitable du serveur (ou relais indisponible).";
                PriceHistorySource = "";
                PriceHistoryRows.Clear();
                PriceHistoryHasRows = false;
                return;
            }

            // Ne jamais garder en mémoire la réponse entière : 30 points par GID au maximum.
            var cacheEntry = new PriceHistoryCacheEntry(
                DateTimeOffset.UtcNow, points.Count, points.TakeLast(PriceHistoryMaxRows).ToArray());
            _priceHistoryCache[(pid, PriceHistoryCharacterScope, gid)] = cacheEntry;
            // Un petit cache de consultations manuelles uniquement, isolé par session PID et objet.
            while (_priceHistoryCache.Count > PriceHistoryMaxCachedItems)
            {
                var oldest = _priceHistoryCache.OrderBy(e => e.Value.ReceivedAt).First().Key;
                _priceHistoryCache.Remove(oldest);
            }
            DisplayPriceHistory(cacheEntry, fromCache: false);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Changement de lot : la nouvelle sélection affiche son propre état.
        }
        catch (Exception)
        {
            if (!request.IsCancellationRequested && _pid == pid &&
                ReferenceEquals(selected, PriceHistorySelection))
            {
                PriceHistoryRows.Clear();
                PriceHistoryHasRows = false;
                PriceHistorySource = "";
                PriceHistoryStatus = "Non reçu · échec de la lecture du serveur.";
            }
        }
        finally
        {
            if (ReferenceEquals(_priceHistoryRequest, request))
            {
                _priceHistoryRequest = null;
                PriceHistoryBusy = false;
                OnPropertyChanged(nameof(CanReadPriceHistory));
            }
        }
    }
}
