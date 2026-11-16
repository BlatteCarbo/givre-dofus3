using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

/// <summary>A display vocabulary only. Values are not map assets, routes or game observations.</summary>
public enum WorldAtlasLayer { Unknown, Surface, Mine, Cave, Zaap }
public enum WorldAtlasEvidenceState { Unverified, Verified }

/// <summary>
/// An explicit certificate supplied by a trusted local importer. The UI performs structural checks,
/// but only the importer can substantiate that the bytes genuinely came from the named source.
/// </summary>
public sealed record WorldAtlasEvidence(
    WorldAtlasEvidenceState State = WorldAtlasEvidenceState.Unverified,
    string Source = "",
    string GameVersion = "",
    string Sha256 = "",
    string EvidenceReference = "")
{
    public bool IsComplete =>
        State == WorldAtlasEvidenceState.Verified
        && !string.IsNullOrWhiteSpace(Source) && Source.Length <= 180
        && !string.IsNullOrWhiteSpace(GameVersion) && GameVersion.Length <= 64
        && !string.IsNullOrEmpty(Sha256) && Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit)
        && !string.IsNullOrWhiteSpace(EvidenceReference) && EvidenceReference.Length <= 260;
    public string StatusLabel => IsComplete ? "VERIFIED" : "UNVERIFIED";
    public string SourceLabel => string.IsNullOrWhiteSpace(Source) ? "SOURCE UNKNOWN" : Source;
    public string FingerprintLabel => !string.IsNullOrEmpty(Sha256) && Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit)
        ? Sha256[..12] + "…" : "SHA256 UNKNOWN";
    public string ReferenceLabel => string.IsNullOrWhiteSpace(EvidenceReference) ? "Preuve indisponible" : EvidenceReference;
}

public sealed record WorldAtlasMapRecord(
    long MapId, int? X, int? Y, string Zone, WorldAtlasLayer Layer, string Label,
    WorldAtlasEvidence Evidence, bool IsManual = false)
{
    public bool CanTrust => !IsManual && Evidence is { IsComplete: true };
    public string KindLabel => Layer switch
    {
        WorldAtlasLayer.Surface => "Surface",
        WorldAtlasLayer.Mine => "Mine",
        WorldAtlasLayer.Cave => "Grotte",
        WorldAtlasLayer.Zaap => "Zaap",
        _ => "Inconnue"
    };
    public string PositionLabel => X is int x && Y is int y
        ? $"[{x}, {y}]" : "Coordonnées UNKNOWN";
    public string Title => string.IsNullOrWhiteSpace(Label) ? $"Carte {MapId}" : Label;
    public string ZoneLabel => string.IsNullOrWhiteSpace(Zone) ? "Zone UNKNOWN" : Zone;
    public string EvidenceLabel => CanTrust ? "VERIFIED" : "UNVERIFIED";
    public string SourceLabel => IsManual ? "Saisie locale · non vérifiée" : Evidence?.SourceLabel ?? "SOURCE UNKNOWN";
    public string FingerprintLabel => IsManual ? "SHA256 UNKNOWN" : Evidence?.FingerprintLabel ?? "SHA256 UNKNOWN";
    public string EvidenceReference => IsManual ? "Preuve indisponible" : Evidence?.ReferenceLabel ?? "Preuve indisponible";
}

/// <summary>Directed edge by default. Reverse travel requires another proven edge or explicit bidirectionality.</summary>
public sealed record WorldAtlasTransitionRecord(
    long FromMapId, long ToMapId, string Label, WorldAtlasEvidence Evidence, bool Bidirectional = false);

/// <summary>Bounded, supplied read-only atlas. A missing root certificate disables all route computation.</summary>
public sealed record WorldAtlasSnapshot(
    string Title, WorldAtlasEvidence Evidence,
    IReadOnlyList<WorldAtlasMapRecord> Maps,
    IReadOnlyList<WorldAtlasTransitionRecord> Transitions);

public sealed record WorldAtlasTransitionRow(
    long FromMapId, long ToMapId, string DirectionText, string KindLabel,
    string EvidenceLabel, string SourceLabel, string FingerprintLabel, string EvidenceReference,
    bool RouteEligible)
{
    public string TargetLabel => ToMapId.ToString(CultureInfo.InvariantCulture);
    // A visible row is a lease on one atlas load and one selected map. Matching
    // mapIds are not enough: the same IDs may be reused by a replaced snapshot.
    public long SnapshotGeneration { get; init; }
    public long SelectionGeneration { get; init; }
    public string SnapshotVersion { get; init; } = "";
}

/// <summary>
/// Pure observed-actor evidence for a future map-context action adapter.
/// Not populated by the offline WorldAtlas: no monster action is dispatched here.
/// </summary>
public sealed record WorldAtlasMonsterGroup(long ContextualId, long InstanceGeneration, int CellId);

/// <summary>Authoritative session/map/actor generations must be supplied by the observer.</summary>
public sealed record WorldAtlasMonsterSnapshot(
    string SessionId, string ClientVersion,
    long SessionGeneration, long MapGeneration, long ActorGeneration,
    string MapSnapshotId, string ActorSnapshotId, long MapId,
    IReadOnlyList<WorldAtlasMonsterGroup> Groups);

/// <summary>
/// Captured context-menu target. It is valid only against a freshly acquired
/// authoritative snapshot of exactly the same session, map and actor generation.
/// </summary>
public sealed record WorldAtlasMonsterSelection(
    string SessionId, string ClientVersion,
    long SessionGeneration, long MapGeneration, long ActorGeneration,
    string MapSnapshotId, string ActorSnapshotId, long MapId,
    long ContextualId, long InstanceGeneration, int CellId);

/// <summary>
/// Offline preflight only: never infers the target from cell proximity, a matching
/// visual label or an ID reused by another monster group. The future caller must
/// synchronize the final authoritative read with its dispatch.
/// </summary>
public static class WorldAtlasMonsterActionGuard
{
    private static bool Valid(WorldAtlasMonsterSnapshot? snapshot)
        => snapshot is { Groups: not null, SessionGeneration: > 0,
                MapGeneration: > 0, ActorGeneration: > 0, MapId: > 0 }
            && !string.IsNullOrWhiteSpace(snapshot.SessionId)
            && !string.IsNullOrWhiteSpace(snapshot.ClientVersion)
            // These are opaque caller-issued object/epoch tokens, not mapIds.
            // They catch a replaced map or actor snapshot even with a recycled ID.
            && !string.IsNullOrWhiteSpace(snapshot.MapSnapshotId)
            && !string.IsNullOrWhiteSpace(snapshot.ActorSnapshotId)
            && snapshot.Groups.Count <= 4096;

    private static bool Valid(WorldAtlasMonsterGroup? group)
        => group is { ContextualId: > 0, InstanceGeneration: > 0, CellId: >= 0 };

    public static bool TryCapture(
        WorldAtlasMonsterSnapshot? observed, long contextualId,
        out WorldAtlasMonsterSelection? selection)
    {
        selection = null;
        if (!Valid(observed) || contextualId <= 0) return false;
        // A duplicate contextual ID is ambiguous, not a reason to pick the first.
        var matches = observed!.Groups.Where(g => g is not null && g.ContextualId == contextualId)
            .Take(2).ToArray();
        if (matches.Length != 1 || !Valid(matches[0])) return false;
        var group = matches[0];
        selection = new WorldAtlasMonsterSelection(observed.SessionId, observed.ClientVersion,
            observed.SessionGeneration, observed.MapGeneration, observed.ActorGeneration,
            observed.MapSnapshotId, observed.ActorSnapshotId,
            observed.MapId, group.ContextualId, group.InstanceGeneration, group.CellId);
        return true;
    }

    public static bool TryResolveAction(
        WorldAtlasMonsterSelection? selection, WorldAtlasMonsterSnapshot? current,
        out WorldAtlasMonsterGroup? group)
    {
        group = null;
        if (selection is null || !Valid(current)
            || selection.SessionGeneration <= 0 || selection.MapGeneration <= 0
            || selection.ActorGeneration <= 0 || selection.MapId <= 0
            || selection.ContextualId <= 0 || selection.InstanceGeneration <= 0
            || selection.CellId < 0
            || !string.Equals(selection.SessionId, current!.SessionId, StringComparison.Ordinal)
            || !string.Equals(selection.ClientVersion, current.ClientVersion, StringComparison.Ordinal)
            || !string.Equals(selection.MapSnapshotId, current.MapSnapshotId, StringComparison.Ordinal)
            || !string.Equals(selection.ActorSnapshotId, current.ActorSnapshotId, StringComparison.Ordinal)
            || selection.SessionGeneration != current.SessionGeneration
            || selection.MapGeneration != current.MapGeneration
            || selection.ActorGeneration != current.ActorGeneration
            || selection.MapId != current.MapId)
            return false;
        var matches = current.Groups.Where(g => g is not null && g.ContextualId == selection.ContextualId)
            .Take(2).ToArray();
        if (matches.Length != 1 || !Valid(matches[0])
            || matches[0].InstanceGeneration != selection.InstanceGeneration
            || matches[0].CellId != selection.CellId)
            return false;
        group = matches[0];
        return true;
    }
}

public sealed partial class WorldAtlasViewModel : ViewModelBase
{
    public const int MaxMaps = 30000;
    public const int MaxTransitions = 100000;
    public const int MaxDisplayed = 140;
    private WorldAtlasSnapshot? _snapshot;
    private readonly List<WorldAtlasMapRecord> _manual = new();
    private readonly List<WorldAtlasMapRecord> _dataMaps = new();
    private readonly List<WorldAtlasTransitionRecord> _dataTransitions = new();
    private readonly Dictionary<long, WorldAtlasMapRecord> _mapLookup = new();
    private long _snapshotGeneration;
    private long _selectionGeneration;

    /// <summary>Optional read-only snapshot factory. Explicit RefreshCommand invokes it, never the game.</summary>
    public Func<WorldAtlasSnapshot?>? SnapshotProvider { get; set; }
    public ObservableCollection<WorldAtlasMapRecord> Results { get; } = new();
    public ObservableCollection<WorldAtlasTransitionRow> SelectedTransitions { get; } = new();
    public ObservableCollection<string> RouteSteps { get; } = new();

    [ObservableProperty] public partial string SearchMapId { get; set; } = "";
    [ObservableProperty] public partial string SearchX { get; set; } = "";
    [ObservableProperty] public partial string SearchY { get; set; } = "";
    [ObservableProperty] public partial string SearchZone { get; set; } = "";
    [ObservableProperty] public partial bool ShowSurface { get; set; } = true;
    [ObservableProperty] public partial bool ShowMines { get; set; } = true;
    [ObservableProperty] public partial bool ShowCaves { get; set; } = true;
    [ObservableProperty] public partial bool ShowZaaps { get; set; } = true;
    [ObservableProperty] public partial bool ShowUnknown { get; set; } = true;
    [ObservableProperty] public partial WorldAtlasMapRecord? SelectedMap { get; set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = "UNVERIFIED · aucun snapshot injecté";
    [ObservableProperty] public partial string SearchStatus { get; private set; } = "Saisis un mapId, une coordonnée ou une zone pour filtrer.";
    [ObservableProperty] public partial string RouteStatus { get; private set; } = "UNVERIFIED · aucun parcours calculé";
    [ObservableProperty] public partial string RouteStartMapId { get; set; } = "";
    [ObservableProperty] public partial string RouteEndMapId { get; set; } = "";
    [ObservableProperty] public partial string ManualMapId { get; set; } = "";
    [ObservableProperty] public partial string ManualX { get; set; } = "";
    [ObservableProperty] public partial string ManualY { get; set; } = "";
    [ObservableProperty] public partial string ManualZone { get; set; } = "";
    [ObservableProperty] public partial string ManualLabel { get; set; } = "";
    [ObservableProperty] public partial int ManualLayerIndex { get; set; } = 0;
    public IReadOnlyList<string> ManualLayers { get; } = new[] { "Inconnue", "Surface", "Mine", "Grotte", "Zaap" };

    public string SnapshotTitle => string.IsNullOrWhiteSpace(_snapshot?.Title) ? "Atlas hors ligne" : _snapshot!.Title;
    public string SnapshotProof => _snapshot?.Evidence?.StatusLabel ?? "UNVERIFIED";
    public string SnapshotSource => _snapshot?.Evidence?.SourceLabel ?? "SOURCE UNKNOWN";
    public string SnapshotVersion => string.IsNullOrWhiteSpace(_snapshot?.Evidence?.GameVersion) ? "Version UNKNOWN" : _snapshot!.Evidence.GameVersion;
    public string SnapshotFingerprint => _snapshot?.Evidence?.FingerprintLabel ?? "SHA256 UNKNOWN";
    public string SnapshotReference => _snapshot?.Evidence?.ReferenceLabel ?? "Preuve indisponible";
    public string DatasetCounts => $"{_dataMaps.Count} cartes importées · {_dataTransitions.Count} transitions · {_manual.Count} fiches manuelles";
    public bool HasSelectedMap => SelectedMap is not null;
    public bool HasSelectedTransitions => SelectedTransitions.Count > 0;
    public bool HasResults => Results.Count > 0;
    public bool HasRoute => RouteSteps.Count > 0;
    public long SnapshotGeneration => _snapshotGeneration;
    public string SelectedExplanation => SelectedMap is null ? "Sélectionne une carte pour examiner ses transitions."
        : SelectedMap.CanTrust ? "Métadonnées certifiées par la provenance fournie. Les chemins restent soumis aux preuves de chaque transition."
        : "UNVERIFIED · données consultatives uniquement. Aucune transition ou route déduite automatiquement.";
    public string SelectedMapId => SelectedMap is null ? "UNKNOWN" : SelectedMap.MapId.ToString(CultureInfo.InvariantCulture);

    public WorldAtlasViewModel() => ApplyFilters();

    /// <summary>Injection côté prime/worker-1 après vérification de source. Aucune lecture réseau / client.</summary>
    public bool LoadSnapshot(WorldAtlasSnapshot? supplied)
    {
        if (supplied is null)
        {
            StatusText = "UNVERIFIED · snapshot absent (aucune donnée importée)";
            return false;
        }
        if (supplied.Maps is null || supplied.Transitions is null
            || supplied.Maps.Count > MaxMaps || supplied.Transitions.Count > MaxTransitions)
        {
            StatusText = "UNVERIFIED · snapshot invalide, chargement refusé ; atlas précédent conservé";
            return false;
        }
        WorldAtlasMapRecord[] maps;
        WorldAtlasTransitionRecord[] transitions;
        try
        {
            // Freeze the caller's lists *before* validating and publishing.
            // Otherwise a mutable imported list can change between validation
            // and AddRange and leave the atlas only partially replaced.
            maps = supplied.Maps.ToArray();
            transitions = supplied.Transitions.ToArray();
        }
        catch (InvalidOperationException)
        {
            StatusText = "UNVERIFIED · snapshot modifié pendant sa lecture, atlas précédent conservé";
            return false;
        }
        if (maps.Length > MaxMaps || transitions.Length > MaxTransitions
            || maps.Any(m => m is null || m.MapId <= 0 || m.Evidence is null || m.Zone is null || m.Label is null
                || m.X is < -10000 or > 10000 || m.Y is < -10000 or > 10000 || (m.X is null) != (m.Y is null)
                || m.Zone.Length > 120 || m.Label.Length > 120 || !Enum.IsDefined(m.Layer))
            || maps.GroupBy(m => m.MapId).Any(g => g.Count() != 1)
            || transitions.Any(t => t is null || t.FromMapId <= 0 || t.ToMapId <= 0
                || t.FromMapId == t.ToMapId || t.Evidence is null || t.Label is null || t.Label.Length > 120)
            || supplied.Evidence is null)
        {
            StatusText = "UNVERIFIED · snapshot invalide, chargement refusé ; atlas précédent conservé";
            return false;
        }
        // Rebind even if records have equal values: record equality can otherwise
        // suppress OnSelectedMapChanged and retain transition rows from a prior load.
        var previouslySelectedId = SelectedMap?.MapId;
        ++_snapshotGeneration;
        SelectedMap = null;
        _snapshot = supplied with { Maps = maps, Transitions = transitions };
        _dataMaps.Clear();
        _dataMaps.AddRange(maps);
        _mapLookup.Clear();
        foreach (var map in _dataMaps) _mapLookup.Add(map.MapId, map);
        // Imported records replace identically identified ephemeral notes in the visible
        // index. Their metadata never inherits trust from a manually typed entry.
        _manual.RemoveAll(m => _mapLookup.ContainsKey(m.MapId));
        _dataTransitions.Clear();
        _dataTransitions.AddRange(transitions);
        StatusText = _snapshot.Evidence.IsComplete
            ? "SOURCE VERIFIED · les cartes et transitions conservent leur propre statut de preuve"
            : "UNVERIFIED · certificat global manquant ou incomplet ; routes indisponibles";
        RaiseSnapshot();
        ApplyFilters();
        if (previouslySelectedId is long selectedId)
            SelectedMap = Results.FirstOrDefault(m => m.MapId == selectedId);
        OnPropertyChanged(nameof(SnapshotGeneration));
        return true;
    }

    [RelayCommand]
    private void RefreshSnapshot()
    {
        if (SnapshotProvider is null)
        {
            StatusText = "UNVERIFIED · aucune source locale injectable configurée";
            return;
        }
        try { LoadSnapshot(SnapshotProvider()); }
        catch (Exception) { StatusText = "UNVERIFIED · lecture de source échouée, données précédentes conservées"; }
    }

    partial void OnSearchMapIdChanged(string value) => ApplyFilters();
    partial void OnSearchXChanged(string value) => ApplyFilters();
    partial void OnSearchYChanged(string value) => ApplyFilters();
    partial void OnSearchZoneChanged(string value) => ApplyFilters();
    partial void OnShowSurfaceChanged(bool value) => ApplyFilters();
    partial void OnShowMinesChanged(bool value) => ApplyFilters();
    partial void OnShowCavesChanged(bool value) => ApplyFilters();
    partial void OnShowZaapsChanged(bool value) => ApplyFilters();
    partial void OnShowUnknownChanged(bool value) => ApplyFilters();

    [RelayCommand] public void ClearSearch()
    {
        SearchMapId = SearchX = SearchY = SearchZone = "";
        ApplyFilters();
    }

    private static bool ParseOptionalInt(string? value, out int? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.Length > 7 || !int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var number) || number is < -10000 or > 10000) return false;
        parsed = number;
        return true;
    }

    private static bool ParseMapId(string? value, out long id)
    {
        id = 0;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 19
            && long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    private bool IsShown(WorldAtlasLayer layer) => layer switch
    {
        WorldAtlasLayer.Surface => ShowSurface,
        WorldAtlasLayer.Mine => ShowMines,
        WorldAtlasLayer.Cave => ShowCaves,
        WorldAtlasLayer.Zaap => ShowZaaps,
        _ => ShowUnknown
    };

    private void ApplyFilters()
    {
        if (SearchZone.Length > 120 || (SearchMapId.Length > 0 && !ParseMapId(SearchMapId, out _))
            || !ParseOptionalInt(SearchX, out var sx) || !ParseOptionalInt(SearchY, out var sy))
        {
            Results.Clear();
            SearchStatus = "Recherche invalide : mapId positif ; X/Y entiers entre -10000 et 10000 ; zone ≤ 120 caractères.";
            SelectedMap = null;
            OnPropertyChanged(nameof(HasResults));
            return;
        }
        long id = ParseMapId(SearchMapId, out var matchId) ? matchId : 0;
        var matched = _dataMaps.Concat(_manual)
            .Where(m => IsShown(m.Layer) && (id == 0 || m.MapId == id)
                && (!sx.HasValue || m.X == sx) && (!sy.HasValue || m.Y == sy)
                && (string.IsNullOrWhiteSpace(SearchZone)
                    || m.Zone.Contains(SearchZone.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.Zone, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Layer).ThenBy(m => m.MapId)
            .ToList();
        var selectedId = SelectedMap?.MapId;
        Results.Clear();
        foreach (var m in matched.Take(MaxDisplayed)) Results.Add(m);
        SelectedMap = selectedId is long keep ? Results.FirstOrDefault(m => m.MapId == keep) : null;
        SearchStatus = matched.Count > MaxDisplayed
            ? $"{matched.Count} résultats · {MaxDisplayed} affichés ; affine la recherche"
            : $"{matched.Count} résultat(s) · {(_snapshot?.Evidence?.IsComplete == true ? "source identifiée" : "SOURCE UNVERIFIED")}";
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private void AddManual()
    {
        if (!ParseMapId(ManualMapId, out long id) || _manual.Any(m => m.MapId == id)
            || _dataMaps.Any(m => m.MapId == id))
        {
            StatusText = "Saisie refusée : mapId positif et unique obligatoire.";
            return;
        }
        if (!ParseOptionalInt(ManualX, out int? x) || !ParseOptionalInt(ManualY, out int? y)
            || (x.HasValue != y.HasValue)
            || ManualZone.Length > 120 || ManualLabel.Length > 120
            || ManualZone.Any(char.IsControl) || ManualLabel.Any(char.IsControl)
            || ManualLayerIndex is < 0 or > 4 || _manual.Count >= 200)
        {
            StatusText = "Saisie invalide : X/Y ensemble ou absents, zone/libellé ≤ 120, 200 fiches max.";
            return;
        }
        var layer = ManualLayerIndex switch
        {
            1 => WorldAtlasLayer.Surface, 2 => WorldAtlasLayer.Mine,
            3 => WorldAtlasLayer.Cave, 4 => WorldAtlasLayer.Zaap,
            _ => WorldAtlasLayer.Unknown
        };
        _manual.Add(new WorldAtlasMapRecord(id, x, y, ManualZone.Trim(), layer, ManualLabel.Trim(),
            new WorldAtlasEvidence(), true));
        StatusText = "UNVERIFIED · fiche ajoutée en mémoire uniquement, aucune sortie ou transition certifiée.";
        OnPropertyChanged(nameof(DatasetCounts));
        ApplyFilters();
        SelectedMap = Results.FirstOrDefault(m => m.MapId == id);
    }

    [RelayCommand]
    private void RemoveManual(WorldAtlasMapRecord? item)
    {
        if (item is not { IsManual: true }) return;
        // A stale UI row with a recycled mapId must never remove its successor.
        if (_manual.RemoveAll(m => ReferenceEquals(m, item)) != 1) return;
        StatusText = "Fiche manuelle retirée de la session.";
        OnPropertyChanged(nameof(DatasetCounts));
        ApplyFilters();
    }

    partial void OnSelectedMapChanged(WorldAtlasMapRecord? value)
    {
        ++_selectionGeneration;
        SelectedTransitions.Clear();
        if (value is not null && !Results.Any(m => ReferenceEquals(m, value)))
        {
            // Binding may deliver a selected item belonging to the old snapshot.
            SelectedMap = null;
            return;
        }
        if (value is not null)
        {
            foreach (var edge in _dataTransitions.Where(t => t.FromMapId == value.MapId
                || (t.Bidirectional && t.ToMapId == value.MapId)).Take(300))
            {
                bool reversed = edge.FromMapId != value.MapId;
                long target = reversed ? edge.FromMapId : edge.ToMapId;
                bool eligible = CanTraverse(value.MapId, target, edge);
                SelectedTransitions.Add(new WorldAtlasTransitionRow(value.MapId, target,
                    reversed ? "Retour explicitement bidirectionnel" : "Sens déclaré", edge.Label,
                    eligible ? "VERIFIED" : "UNVERIFIED", edge.Evidence?.SourceLabel ?? "SOURCE UNKNOWN",
                    edge.Evidence?.FingerprintLabel ?? "SHA256 UNKNOWN",
                    edge.Evidence?.ReferenceLabel ?? "Preuve indisponible", eligible)
                {
                    SnapshotGeneration = _snapshotGeneration,
                    SelectionGeneration = _selectionGeneration,
                    SnapshotVersion = _snapshot?.Evidence?.GameVersion ?? ""
                });
            }
        }
        OnPropertyChanged(nameof(HasSelectedMap));
        OnPropertyChanged(nameof(HasSelectedTransitions));
        OnPropertyChanged(nameof(SelectedExplanation));
        OnPropertyChanged(nameof(SelectedMapId));
    }

    [RelayCommand]
    private void InspectDestination(WorldAtlasTransitionRow? transition)
    {
        if (transition is null) return;
        // Do not reuse a context action after a map/session replacement or a
        // different selection: identical mapIds and labels are not enough.
        if (transition.SnapshotGeneration != _snapshotGeneration
            || transition.SelectionGeneration != _selectionGeneration
            || !string.Equals(transition.SnapshotVersion,
                _snapshot?.Evidence?.GameVersion ?? "", StringComparison.Ordinal)
            || SelectedMap?.MapId != transition.FromMapId
            || !Results.Any(m => ReferenceEquals(m, SelectedMap))
            || !SelectedTransitions.Any(row => ReferenceEquals(row, transition)))
        {
            StatusText = "UNVERIFIED · action refusée : sélection ou snapshot de carte périmé.";
            return;
        }
        // Search by mapId so the list can select a target that was hidden by previous filters.
        SearchMapId = transition.ToMapId.ToString(CultureInfo.InvariantCulture);
        SearchX = SearchY = SearchZone = "";
        ShowSurface = ShowMines = ShowCaves = ShowZaaps = ShowUnknown = true;
        ApplyFilters();
        SelectedMap = Results.FirstOrDefault(m => m.MapId == transition.ToMapId);
        if (SelectedMap is null) StatusText = "UNVERIFIED · carte de destination absente de la source.";
    }

    [RelayCommand]
    private void SetRouteStart()
    {
        if (SelectedMap is not null && Results.Any(m => ReferenceEquals(m, SelectedMap)))
            RouteStartMapId = SelectedMap.MapId.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void SetRouteEnd()
    {
        if (SelectedMap is not null && Results.Any(m => ReferenceEquals(m, SelectedMap)))
            RouteEndMapId = SelectedMap.MapId.ToString(CultureInfo.InvariantCulture);
    }

    partial void OnRouteStartMapIdChanged(string value) => ClearRoute();
    partial void OnRouteEndMapIdChanged(string value) => ClearRoute();
    private void ClearRoute()
    {
        RouteSteps.Clear();
        RouteStatus = "UNVERIFIED · recalcul requis";
        OnPropertyChanged(nameof(HasRoute));
    }

    private bool CanTrust(WorldAtlasMapRecord? entry)
        => entry is { CanTrust: true }
            && _snapshot?.Evidence?.IsComplete == true
            && entry.Evidence.GameVersion == _snapshot.Evidence.GameVersion;

    private bool CanTraverse(long from, long to, WorldAtlasTransitionRecord edge)
    {
        _mapLookup.TryGetValue(from, out var origin);
        _mapLookup.TryGetValue(to, out var destination);
        return CanTrust(origin) && CanTrust(destination)
            && edge.Evidence is { IsComplete: true }
            && edge.Evidence.GameVersion == _snapshot!.Evidence.GameVersion
            && ((edge.FromMapId == from && edge.ToMapId == to)
                || (edge.Bidirectional && edge.FromMapId == to && edge.ToMapId == from));
    }

    [RelayCommand]
    private void BuildRoute()
    {
        ClearRoute();
        if (!ParseMapId(RouteStartMapId, out var start) || !ParseMapId(RouteEndMapId, out var end))
        {
            RouteStatus = "UNVERIFIED · indique deux mapId positifs.";
            return;
        }
        _mapLookup.TryGetValue(start, out var startMap);
        _mapLookup.TryGetValue(end, out var endMap);
        if (!CanTrust(startMap) || !CanTrust(endMap))
        {
            RouteStatus = "UNVERIFIED · les deux cartes et la source globale doivent être certifiées.";
            return;
        }
        if (start == end)
        {
            RouteStatus = "Même carte prouvée ; aucun déplacement à calculer.";
            RouteSteps.Add($"{start} · {startMap!.ZoneLabel}");
            OnPropertyChanged(nameof(HasRoute));
            return;
        }
        // Bounded BFS of certified directed transitions. No coordinate adjacency inference.
        var adjacency = new Dictionary<long, List<long>>();
        foreach (var edge in _dataTransitions)
        {
            if (CanTraverse(edge.FromMapId, edge.ToMapId, edge))
            {
                if (!adjacency.TryGetValue(edge.FromMapId, out var neighbours))
                    adjacency[edge.FromMapId] = neighbours = new List<long>();
                neighbours.Add(edge.ToMapId);
            }
            if (edge.Bidirectional && CanTraverse(edge.ToMapId, edge.FromMapId, edge))
            {
                if (!adjacency.TryGetValue(edge.ToMapId, out var neighbours))
                    adjacency[edge.ToMapId] = neighbours = new List<long>();
                neighbours.Add(edge.FromMapId);
            }
        }
        var q = new Queue<long>();
        var prev = new Dictionary<long, long> { [start] = start };
        q.Enqueue(start);
        while (q.Count > 0 && prev.Count <= MaxMaps)
        {
            var current = q.Dequeue();
            if (current == end) break;
            if (!adjacency.TryGetValue(current, out var next)) continue;
            foreach (var neighbour in next)
            {
                if (!prev.TryAdd(neighbour, current)) continue;
                q.Enqueue(neighbour);
            }
        }
        if (!prev.ContainsKey(end))
        {
            RouteStatus = "UNVERIFIED · aucun enchaînement intégralement prouvé (transitions manquantes ou sens inconnu).";
            return;
        }
        var path = new List<long> { end };
        while (path[^1] != start && path.Count <= MaxMaps) path.Add(prev[path[^1]]);
        path.Reverse();
        foreach (var id in path)
        {
            var item = _mapLookup[id];
            RouteSteps.Add($"{id} · {item.KindLabel} · {item.ZoneLabel} · {item.PositionLabel}");
        }
        RouteStatus = $"PARCOURS CERTIFIÉ · {path.Count - 1} transition(s) dirigée(s) sourcée(s). Consultation uniquement.";
        OnPropertyChanged(nameof(HasRoute));
    }

    private void RaiseSnapshot()
    {
        OnPropertyChanged(nameof(SnapshotTitle));
        OnPropertyChanged(nameof(SnapshotProof));
        OnPropertyChanged(nameof(SnapshotSource));
        OnPropertyChanged(nameof(SnapshotVersion));
        OnPropertyChanged(nameof(SnapshotFingerprint));
        OnPropertyChanged(nameof(SnapshotReference));
        OnPropertyChanged(nameof(DatasetCounts));
        ClearRoute();
    }
}
