using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.VisualMods;

namespace GivreClone.App.ViewModels;

/// <summary>Self-contained, fully offline notes and manual coordinate planner.</summary>
public partial class VisualModsViewModel : ViewModelBase
{
    private readonly VisualCarnetStore _store;
    public ObservableCollection<VisualPosition> Positions { get; } = new();
    public string StoragePath => _store.FilePath;
    public string SessionState => "UNKNOWN";
    public string AuthorityText => "Positions et indices saisis manuellement · aucune localisation observée";
    public bool HasPositions => Positions.Count != 0;

    [ObservableProperty] public partial bool StorageReadOnly { get; private set; }
    public bool CanEdit => !StorageReadOnly;
    [ObservableProperty] public partial string StatusMessage { get; private set; } = "";
    [ObservableProperty] public partial string NewLabel { get; set; } = "";
    [ObservableProperty] public partial string NewX { get; set; } = "";
    [ObservableProperty] public partial string NewY { get; set; } = "";
    [ObservableProperty] public partial string NewNote { get; set; } = "";
    [ObservableProperty] public partial bool NewBookmark { get; set; }

    [ObservableProperty] public partial string StartX { get; set; } = "";
    [ObservableProperty] public partial string StartY { get; set; } = "";
    [ObservableProperty] public partial string HintX { get; set; } = "";
    [ObservableProperty] public partial string HintY { get; set; } = "";
    [ObservableProperty] public partial string HintText { get; set; } = "";
    [ObservableProperty] public partial bool HintVerified { get; set; }
    [ObservableProperty] public partial string RouteResult { get; private set; } = "UNKNOWN · aucun trajet calculé";

    public VisualModsViewModel(string? storagePath = null)
    {
        _store = new VisualCarnetStore(storagePath);
        var loaded = _store.Load();
        foreach (var position in loaded.Document.Positions) Positions.Add(position);
        StorageReadOnly = loaded.ReadOnly;
        StatusMessage = loaded.Message;
    }

    partial void OnStorageReadOnlyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));
    partial void OnHintXChanged(string value) => ResetHint();
    partial void OnHintYChanged(string value) => ResetHint();
    partial void OnHintTextChanged(string value) => ResetHint();
    partial void OnStartXChanged(string value) => RouteResult = "UNKNOWN · recalcul requis";
    partial void OnStartYChanged(string value) => RouteResult = "UNKNOWN · recalcul requis";
    partial void OnHintVerifiedChanged(bool value)
    {
        if (!value) RouteResult = "UNKNOWN · vérification manuelle requise";
    }

    private void ResetHint()
    {
        HintVerified = false;
        RouteResult = "UNKNOWN · vérification manuelle requise";
    }

    [RelayCommand]
    private void AddPosition()
    {
        if (!CanEdit) return;
        if (Positions.Count >= VisualCarnetStore.MaxPositions)
        {
            StatusMessage = $"Limite de {VisualCarnetStore.MaxPositions} positions atteinte.";
            return;
        }
        if (!VisualCarnetStore.TryCreatePosition(NewLabel, NewX, NewY, NewNote,
                NewBookmark, out var position, out var error))
        {
            StatusMessage = error;
            return;
        }
        var next = Positions.Append(position!).ToArray();
        if (!Save(next)) return;
        Positions.Add(position!);
        OnPropertyChanged(nameof(HasPositions));
        NewLabel = "";
        NewNote = "";
        NewBookmark = false;
        StatusMessage = "Position ajoutée et sauvegardée localement.";
    }

    [RelayCommand]
    private void DeletePosition(VisualPosition? position)
    {
        if (!CanEdit || position is null || !Positions.Any(p => p.Id == position.Id)) return;
        if (!Save(Positions.Where(p => p.Id != position.Id).ToArray())) return;
        var existing = Positions.First(p => p.Id == position.Id);
        Positions.Remove(existing);
        OnPropertyChanged(nameof(HasPositions));
        StatusMessage = "Position supprimée du carnet local.";
    }

    [RelayCommand]
    private void ToggleBookmark(VisualPosition? position)
    {
        if (!CanEdit || position is null) return;
        int index = Positions.ToList().FindIndex(p => p.Id == position.Id);
        if (index < 0) return;
        var changed = Positions[index] with { IsBookmark = !Positions[index].IsBookmark };
        var next = Positions.ToArray();
        next[index] = changed;
        if (!Save(next)) return;
        Positions[index] = changed;
        StatusMessage = changed.IsBookmark ? "Favori enregistré." : "Favori retiré.";
    }

    [RelayCommand]
    private void UseAsStart(VisualPosition? position)
    {
        if (position is null || !Positions.Any(p => p.Id == position.Id)) return;
        StartX = position.X.ToString(CultureInfo.InvariantCulture);
        StartY = position.Y.ToString(CultureInfo.InvariantCulture);
        RouteResult = "UNKNOWN · saisis et vérifie l'indice avant le calcul";
    }

    [RelayCommand]
    private void PlanHint()
    {
        if (!ManualTreasurePlanner.TryPlan(StartX, StartY, HintX, HintY, HintText, HintVerified,
                out var route, out var error))
        {
            RouteResult = error;
            return;
        }
        RouteResult = $"{route!.Direction} · ΔX={route.DeltaX:+0;-0;0} / ΔY={route.DeltaY:+0;-0;0} " +
                      $"· {route.ManhattanDistance} pas de grille (distance théorique, aucun chemin vérifié)";
    }

    private bool Save(VisualPosition[] next)
    {
        try
        {
            _store.Save(new VisualCarnetDocument { Positions = next.ToList() });
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            StatusMessage = "Sauvegarde impossible : " + e.Message;
            return false;
        }
    }
}
