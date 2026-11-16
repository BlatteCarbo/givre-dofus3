using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using GivreClone.Protocol;

namespace GivreClone.App.Controls;

/// <summary>
/// Immutable provenance of a monster context menu. This is a UI authorization
/// boundary, not a server group lease: BotRowViewModel.RequestAttack still checks
/// the live world immediately before issuing its request.
/// </summary>
public sealed record MapAttackMenuOrigin(
    object Bot, int Pid, long MapId, int MapEpoch, long SelectionGeneration,
    long WorldMapGeneration, IReadOnlyList<MapActor> VisibleActors)
{
    /// <summary>Fail closed when the map binding has not caught up with SelectedBot.</summary>
    public static MapAttackMenuOrigin? Capture(
        object? bot, int pid, long mapId, int mapEpoch, long selectionGeneration,
        long worldMapGeneration, IReadOnlyList<MapActor>? visibleActors,
        IEnumerable? displayedActors, int displayedEpoch)
        => bot is not null && pid > 0 && mapId > 0 && mapEpoch > 0
            && selectionGeneration > 0 && worldMapGeneration > 0
            && visibleActors is not null && ReferenceEquals(visibleActors, displayedActors)
            && mapEpoch == displayedEpoch
            ? new MapAttackMenuOrigin(bot, pid, mapId, mapEpoch, selectionGeneration,
                worldMapGeneration, visibleActors)
            : null;

    /// <summary>
    /// Menu clicks cannot be retargeted to another bot, another incarnation
    /// of the same PID/map, or another monster with the same contextual ID.
    /// </summary>
    public bool Allows(MapAttackMenuOrigin? current, MapActor? target,
        IReadOnlyList<MapActor>? allActors)
        => current is not null && Pid > 0 && MapId > 0 && MapEpoch > 0
            && SelectionGeneration > 0 && WorldMapGeneration > 0
            && VisibleActors is not null
            && target is { Kind: MapActorKind.Monster, ActorId: < 0, CellId: >= 0 and < 560 }
            && ReferenceEquals(Bot, current.Bot)
            && Pid == current.Pid && MapId == current.MapId
            && MapEpoch == current.MapEpoch
            && SelectionGeneration == current.SelectionGeneration
            && WorldMapGeneration == current.WorldMapGeneration
            && ReferenceEquals(VisibleActors, current.VisibleActors)
            && HasUniqueReference(VisibleActors, target)
            && allActors is not null && HasUniqueReference(allActors, target);

    private static bool HasUniqueReference(IReadOnlyList<MapActor> actors, MapActor target)
    {
        try
        {
            return actors.Count(a => a.ActorId == target.ActorId) == 1
                && actors.Any(a => ReferenceEquals(a, target));
        }
        catch (InvalidOperationException) { return false; } // concurrent collection change
    }
}

/// <summary>Context-menu click with its original bot/map provenance.</summary>
public sealed record MapAttackMenuRequest(MapActor Target, MapAttackMenuOrigin Origin);

/// <summary>
/// Rendu isometrique EN RELIEF de la carte tactique Dofus (560 cellules) + acteurs. Les cellules viennent des
/// bundles du client (mov/los/floor) : sol marchable a plat (clair), obstacle en VOLUME (bloc 3D gris, sombre si
/// pas de ligne de vue), elevation via floor. Sans donnee de cellule -> grille plate neutre.
///
/// Un SEUL Control qui override Render (jamais 560 controls). Geometrie un bot tiers: row=id/14, col=id%14,
/// x=col*64+(row impair?64:32), y=row*18+18. La grille en relief est bakee en RenderTargetBitmap (rebuild au
/// changement de carte) ; acteurs + survol dessines par-dessus. Brosses ImmutableSolidColorBrush (regle hors-thread).
/// </summary>
public sealed class IsoMapControl : Control
{
    private const int Cols = 14, CellCount = 560;
    private const double CellW = 64, HalfW = 32, HalfH = 18, RowPitch = 18, PadTop = 56;
    private const double CanvasW = 930, CanvasH = 812;
    private const double WallH = 20, FloorScale = 8, MaxH = 64;

    private static readonly ImmutableSolidColorBrush Bg = new(Color.Parse("#14181F"));
    // sol marchable
    private static readonly ImmutableSolidColorBrush WalkTop = new(Color.Parse("#DED2A8"));
    private static readonly ImmutableSolidColorBrush WalkRight = new(Color.Parse("#B4A87F"));
    private static readonly ImmutableSolidColorBrush WalkLeft = new(Color.Parse("#8E8362"));
    // obstacle avec ligne de vue
    private static readonly ImmutableSolidColorBrush LosTop = new(Color.Parse("#A9AFB8"));
    private static readonly ImmutableSolidColorBrush LosRight = new(Color.Parse("#878C94"));
    private static readonly ImmutableSolidColorBrush LosLeft = new(Color.Parse("#6B6F76"));
    // obstacle sans ligne de vue
    private static readonly ImmutableSolidColorBrush BlkTop = new(Color.Parse("#787E86"));
    private static readonly ImmutableSolidColorBrush BlkRight = new(Color.Parse("#60646B"));
    private static readonly ImmutableSolidColorBrush BlkLeft = new(Color.Parse("#4B4F54"));
    // grille neutre (aucune donnee de cellule)
    private static readonly ImmutableSolidColorBrush NeutA = new(Color.Parse("#232C3A"));
    private static readonly ImmutableSolidColorBrush NeutB = new(Color.Parse("#28323F"));
    private static readonly ImmutablePen TopBorder = new(new ImmutableSolidColorBrush(Color.Parse("#33101418")), 0.7);
    // Cellules de TRANSITION (mapChangeData != 0: on peut quitter la carte) - teinte teal + liseré.
    private static readonly ImmutableSolidColorBrush TransitionFill = new(Color.FromArgb(0x7A, 0x2E, 0xE6, 0xC6));
    private static readonly ImmutablePen TransitionPen = new(new ImmutableSolidColorBrush(Color.FromArgb(0xE0, 0x62, 0xF2, 0xCE)), 1.3);
    // Elements interactifs (ressources/zaaps) : petit losange vert = disponible, gris = epuise.
    private static readonly ImmutableSolidColorBrush ResourceOk = new(Color.FromArgb(0xF0, 0x5C, 0xD6, 0x76));
    private static readonly ImmutableSolidColorBrush ResourceGone = new(Color.FromArgb(0xB0, 0x8A, 0x93, 0xA3));
    private static readonly ImmutablePen ResourcePen = new(new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x0E, 0x12, 0x16)), 1.0);

    private static readonly ImmutableSolidColorBrush SelfFill = new(Color.Parse("#E8A63C"));
    private static readonly ImmutableSolidColorBrush PlayerFill = new(Color.Parse("#8076D0"));
    private static readonly ImmutableSolidColorBrush MonsterFill = new(Color.Parse("#C85450"));
    private static readonly ImmutableSolidColorBrush NpcFill = new(Color.Parse("#5FC8C8"));
    // archimonstre : magenta VIF (se démarque du rouge monstre et de l'ambre du perso) + halo.
    private static readonly ImmutableSolidColorBrush ArchFill = new(Color.Parse("#E24BC8"));
    private static readonly ImmutableSolidColorBrush ArchGlow = new(Color.FromArgb(0x55, 0xE2, 0x4B, 0xC8));
    private static readonly ImmutablePen SelfTrail = new(SelfFill, 2.5);
    private static readonly ImmutablePen PlayerTrail = new(PlayerFill, 2.5);
    private static readonly ImmutablePen MonsterTrail = new(MonsterFill, 2.5);
    private static readonly ImmutablePen ArchTrail = new(ArchFill, 2.5);
    private static readonly ImmutablePen NpcTrail = new(NpcFill, 2.5);
    private static readonly ImmutablePen ActorBorder = new(new ImmutableSolidColorBrush(Colors.White), 1.5);
    private static readonly ImmutableSolidColorBrush MatchFill = new(Color.FromArgb(0x60, 0xF4, 0xD0, 0x3F));
    private static readonly ImmutablePen MatchPen = new(new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0xF4, 0xD0, 0x3F)), 1.6);
    private static readonly ImmutablePen SelectPen = new(new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), 2.4);
    private static readonly ImmutableSolidColorBrush PathFill = new(Color.FromArgb(0x70, 0x4F, 0xD1, 0xC5));
    private static readonly ImmutablePen PathPen = new(new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0x4F, 0xD1, 0xC5)), 2);
    private static readonly ImmutablePen PathPendingPen = new(new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0xF4, 0xD0, 0x3F)), 3);
    private static readonly ImmutableSolidColorBrush HoverFill = new(Color.Parse("#77FFFFFF"));
    private static readonly ImmutableSolidColorBrush CountBrush = new(Colors.White);
    private static readonly Typeface CountTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
    private static readonly ImmutableSolidColorBrush CellIdBrush = new(Color.Parse("#FFF4D03F"));   // jaune lisible pour les n° de cellule
    private static readonly ImmutableSolidColorBrush CellIdShadow = new(Color.FromArgb(210, 0, 0, 0));

    public static readonly StyledProperty<IEnumerable?> ActorsProperty =
        AvaloniaProperty.Register<IsoMapControl, IEnumerable?>(nameof(Actors));
    public static readonly StyledProperty<IEnumerable?> CellsProperty =
        AvaloniaProperty.Register<IsoMapControl, IEnumerable?>(nameof(Cells));
    public static readonly StyledProperty<string?> SelfNameProperty =
        AvaloniaProperty.Register<IsoMapControl, string?>(nameof(SelfName));
    /// <summary>Id d'acteur de NOTRE perso (= characterId) : reconnaissance prioritaire, le nom n'est qu'un repli (nom pas encore
    /// résolu, casse différente…). Sans lui, le marqueur jaune disparaissait dès que le nom ne correspondait pas exactement.</summary>
    public static readonly StyledProperty<long?> SelfActorIdProperty =
        AvaloniaProperty.Register<IsoMapControl, long?>(nameof(SelfActorId));
    /// <summary>Cellule SOURCE autoritaire du perso (SelfCellId du VM), recalee serveur. Prioritaire sur la recherche
    /// par nom -> le pathfinding part toujours de la bonne cellule, meme si le marqueur (acteur) n'a pas encore bouge.</summary>
    public static readonly StyledProperty<int?> SourceCellProperty =
        AvaloniaProperty.Register<IsoMapControl, int?>(nameof(SourceCell));
    public static readonly StyledProperty<int?> HoveredCellProperty =
        AvaloniaProperty.Register<IsoMapControl, int?>(nameof(HoveredCell));
    public static readonly StyledProperty<IReadOnlyList<MapActor>?> HoveredActorsProperty =
        AvaloniaProperty.Register<IsoMapControl, IReadOnlyList<MapActor>?>(nameof(HoveredActors));
    /// <summary>Element interactif (ressource/zaap...) sur la cellule survolee, ou null.</summary>
    public static readonly StyledProperty<MapInteractive?> HoveredInteractiveProperty =
        AvaloniaProperty.Register<IsoMapControl, MapInteractive?>(nameof(HoveredInteractive));
    /// <summary>Signal d'un deplacement (acteur + chemin) : declenche l'animation le long des cellules.</summary>
    public static readonly StyledProperty<MoveInfo?> MoveSignalProperty =
        AvaloniaProperty.Register<IsoMapControl, MoveInfo?>(nameof(MoveSignal));
    /// <summary>Incremente quand la CARTE est rechargee (nouveau map event) : les acteurs se REPOSITIONNENT sans glissement.</summary>
    public static readonly StyledProperty<int> MapEpochProperty =
        AvaloniaProperty.Register<IsoMapControl, int>(nameof(MapEpoch));
    /// <summary>Surligne les cellules de TRANSITION (mapChangeData != 0: ou l'on peut quitter la carte).</summary>
    public static readonly StyledProperty<bool> ShowTransitionsProperty =
        AvaloniaProperty.Register<IsoMapControl, bool>(nameof(ShowTransitions));
    /// <summary>Affiche le NUMERO de chaque cellule (0..559) sur la carte : evite de survoler case par case pour trouver un cellId.</summary>
    public static readonly StyledProperty<bool> ShowCellIdsProperty =
        AvaloniaProperty.Register<IsoMapControl, bool>(nameof(ShowCellIds));
    /// <summary>Elements interactifs (MapInteractive) a marquer sur la carte.</summary>
    public static readonly StyledProperty<IEnumerable?> InteractivesProperty =
        AvaloniaProperty.Register<IsoMapControl, IEnumerable?>(nameof(Interactives));

    /// <summary>Cellule de l'entité INSPECTÉE (anneau de sélection), ou null.</summary>
    public static readonly StyledProperty<int?> SelectedCellProperty =
        AvaloniaProperty.Register<IsoMapControl, int?>(nameof(SelectedCell));
    /// <summary>Cellules qui correspondent à la recherche (surbrillance).</summary>
    public static readonly StyledProperty<IReadOnlySet<int>?> MatchCellsProperty =
        AvaloniaProperty.Register<IsoMapControl, IReadOnlySet<int>?>(nameof(MatchCells));
    /// <summary>INSPECTION : un clic ouvre une fiche, ne déplace jamais. ACTION : un clic prévisualise le trajet, un 2e clic sur la même cellule confirme.</summary>
    public static readonly StyledProperty<MapInteractionMode> InteractionModeProperty =
        AvaloniaProperty.Register<IsoMapControl, MapInteractionMode>(nameof(InteractionMode));
    public MapInteractionMode InteractionMode { get => GetValue(InteractionModeProperty); set => SetValue(InteractionModeProperty, value); }
    /// <summary>Nombre de pas du trajet prévisualisé (0 = aucun / inatteignable).</summary>
    public static readonly StyledProperty<int> PreviewStepsProperty = AvaloniaProperty.Register<IsoMapControl, int>(nameof(PreviewSteps));
    public int PreviewSteps { get => GetValue(PreviewStepsProperty); private set => SetValue(PreviewStepsProperty, value); }
    private IReadOnlyList<int> _preview = Array.Empty<int>();
    private int? _pending;      // cellule de destination dont le trajet est verrouillé en attente de confirmation
    private int _previewSrc = -1, _previewDst = -1;
    public int? SelectedCell { get => GetValue(SelectedCellProperty); set => SetValue(SelectedCellProperty, value); }
    public IReadOnlySet<int>? MatchCells { get => GetValue(MatchCellsProperty); set => SetValue(MatchCellsProperty, value); }
    /// <summary>Appelé au clic gauche sur une cellule : renvoie true si une entité y a été inspectée (alors AUCUN déplacement).</summary>
    public Func<int, bool>? CellInspector { get; set; }
    /// <summary>Navigation clavier de la carte : "escape", "next", "prev".</summary>
    public event Action<string>? KeyAction;

    public IEnumerable? Actors { get => GetValue(ActorsProperty); set => SetValue(ActorsProperty, value); }
    public IEnumerable? Cells { get => GetValue(CellsProperty); set => SetValue(CellsProperty, value); }
    public string? SelfName { get => GetValue(SelfNameProperty); set => SetValue(SelfNameProperty, value); }
    public long? SelfActorId { get => GetValue(SelfActorIdProperty); set => SetValue(SelfActorIdProperty, value); }

    /// <summary>Cet acteur est-il NOTRE perso ? Par id d'abord, puis par nom (insensible à la casse).</summary>
    private bool IsSelfActor(MapActor a)
    {
        if (a.Kind != MapActorKind.Player) return false;
        if (SelfActorId is long id && id != 0 && a.ActorId == id) return true;
        var self = SelfName;
        return a.Name is { Length: > 0 } n && !string.IsNullOrEmpty(self) && string.Equals(n, self, StringComparison.OrdinalIgnoreCase);
    }
    public int? SourceCell { get => GetValue(SourceCellProperty); set => SetValue(SourceCellProperty, value); }
    public int? HoveredCell { get => GetValue(HoveredCellProperty); set => SetValue(HoveredCellProperty, value); }
    public IReadOnlyList<MapActor>? HoveredActors { get => GetValue(HoveredActorsProperty); set => SetValue(HoveredActorsProperty, value); }
    public MapInteractive? HoveredInteractive { get => GetValue(HoveredInteractiveProperty); set => SetValue(HoveredInteractiveProperty, value); }
    public MoveInfo? MoveSignal { get => GetValue(MoveSignalProperty); set => SetValue(MoveSignalProperty, value); }
    public int MapEpoch { get => GetValue(MapEpochProperty); set => SetValue(MapEpochProperty, value); }
    public bool ShowTransitions { get => GetValue(ShowTransitionsProperty); set => SetValue(ShowTransitionsProperty, value); }
    public bool ShowCellIds { get => GetValue(ShowCellIdsProperty); set => SetValue(ShowCellIdsProperty, value); }
    public IEnumerable? Interactives { get => GetValue(InteractivesProperty); set => SetValue(InteractivesProperty, value); }

    // ── Combat joué à la main : cases colorées (placement / déplacement / portée), combattants, clic = action de combat ──
    public static readonly StyledProperty<IReadOnlyList<FightMark>?> FightMarksProperty = AvaloniaProperty.Register<IsoMapControl, IReadOnlyList<FightMark>?>(nameof(FightMarks));
    public static readonly StyledProperty<IReadOnlyList<GivreClone.App.Services.LaunchService.ScriptFighter>?> FightersProperty = AvaloniaProperty.Register<IsoMapControl, IReadOnlyList<GivreClone.App.Services.LaunchService.ScriptFighter>?>(nameof(Fighters));
    public static readonly StyledProperty<bool> FightModeProperty = AvaloniaProperty.Register<IsoMapControl, bool>(nameof(FightMode));
    public IReadOnlyList<FightMark>? FightMarks { get => GetValue(FightMarksProperty); set => SetValue(FightMarksProperty, value); }
    public IReadOnlyList<GivreClone.App.Services.LaunchService.ScriptFighter>? Fighters { get => GetValue(FightersProperty); set => SetValue(FightersProperty, value); }
    public bool FightMode { get => GetValue(FightModeProperty); set => SetValue(FightModeProperty, value); }
    /// <summary>Clic sur une case en mode combat : (cellule, clic droit ?).</summary>
    public event Action<int, bool>? FightCellClicked;
    private static readonly IBrush FmPlace = new SolidColorBrush(Color.FromArgb(150, 255, 140, 20)), FmReach = new SolidColorBrush(Color.FromArgb(95, 70, 150, 255)),
        FmRange = new SolidColorBrush(Color.FromArgb(105, 80, 220, 110)), FmTarget = new SolidColorBrush(Color.FromArgb(150, 235, 70, 70));
    private static readonly IBrush FtSelf = new SolidColorBrush(Color.FromRgb(255, 196, 60)), FtEnemy = new SolidColorBrush(Color.FromRgb(214, 74, 74)),
        FtAlly = new SolidColorBrush(Color.FromRgb(96, 150, 255)), HpBack = new SolidColorBrush(Color.FromArgb(200, 20, 20, 24)), HpFill = new SolidColorBrush(Color.FromRgb(90, 210, 110));

    private RenderTargetBitmap? _relief;
    private MapCell?[] _byId = new MapCell?[CellCount];
    private int? _hover;
    private const double StepPx = 6.5;                            // vitesse de marche (px / tick de 16 ms)
    private readonly Dictionary<long, Point> _actorPos = new();   // position pixel animee par acteur
    private readonly Dictionary<long, (List<Point> Pts, int Idx)> _actorPath = new();   // chemin en cours
    private readonly DispatcherTimer _anim;

    // Image du jeu (DofusDB, taille native ~1910×970) posée sous les cases : la grille d'origine mesure 14,5 × 86 px ; le calage (décalage de la grille dans l'image, zoom) se règle à la main.
    public static readonly StyledProperty<Bitmap?> BackgroundImageProperty = AvaloniaProperty.Register<IsoMapControl, Bitmap?>(nameof(BackgroundImage));
    public static readonly StyledProperty<double> BgOffsetXProperty = AvaloniaProperty.Register<IsoMapControl, double>(nameof(BgOffsetX), 240);
    public static readonly StyledProperty<double> BgOffsetYProperty = AvaloniaProperty.Register<IsoMapControl, double>(nameof(BgOffsetY), 20);
    public static readonly StyledProperty<double> BgScaleProperty = AvaloniaProperty.Register<IsoMapControl, double>(nameof(BgScale), 1);
    public static readonly StyledProperty<double> BgCellOpacityProperty = AvaloniaProperty.Register<IsoMapControl, double>(nameof(BgCellOpacity), 0.3);
    public Bitmap? BackgroundImage { get => GetValue(BackgroundImageProperty); set => SetValue(BackgroundImageProperty, value); }
    public double BgOffsetX { get => GetValue(BgOffsetXProperty); set => SetValue(BgOffsetXProperty, value); }
    public double BgOffsetY { get => GetValue(BgOffsetYProperty); set => SetValue(BgOffsetYProperty, value); }
    public double BgScale { get => GetValue(BgScaleProperty); set => SetValue(BgScaleProperty, value); }
    public double BgCellOpacity { get => GetValue(BgCellOpacityProperty); set => SetValue(BgCellOpacityProperty, value); }

    static IsoMapControl() => AffectsRender<IsoMapControl>(BackgroundImageProperty, BgOffsetXProperty, BgOffsetYProperty, BgScaleProperty, BgCellOpacityProperty, SelfNameProperty, SelfActorIdProperty, SourceCellProperty, ShowTransitionsProperty, ShowCellIdsProperty, SelectedCellProperty, MatchCellsProperty, InteractionModeProperty, FightMarksProperty, FightersProperty, FightModeProperty);

    public IsoMapControl()
    {
        _anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _anim.Tick += OnAnimTick;
        Focusable = true;   // clavier : Échap ferme la fiche, Tab / flèches parcourent les entités
    }

    protected override Size MeasureOverride(Size availableSize) => new(CanvasW, CanvasH);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CellsProperty) { RebuildCells(); _relief = null; InvalidateVisual(); }
        else if (e.Property == ActorsProperty) OnActorsChanged();
        else if (e.Property == MoveSignalProperty && MoveSignal is { } mv) StartMove(mv);
        else if (e.Property == MapEpochProperty) SnapAllToCells();
        else if (e.Property == InteractivesProperty) InvalidateVisual();
        else if (e.Property == InteractionModeProperty) { _pending = null; SetPreview(-1); }
        else if (e.Property == FightersProperty) OnFightersChanged();
        else if (e.Property == SelfActorIdProperty) { _lastLife.Clear(); _floats.Clear(); }   // autre bot affiché : ids de monstres réutilisés
    }

    // Dégâts / soins flottants en combat : écart de PV d'un combattant entre deux relevés -> « -120 » qui monte et s'efface.
    private readonly Dictionary<long, int> _lastLife = new();
    private readonly List<(int Cell, int Delta, long Start)> _floats = new();
    private const long FloatMs = 1300;
    private static readonly IBrush FloatDmg = new SolidColorBrush(Color.FromRgb(255, 107, 122)), FloatHeal = new SolidColorBrush(Color.FromRgb(99, 211, 166)),
        FloatShadow = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
    private static readonly Typeface FloatFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private void OnFightersChanged()
    {
        if (!FightMode || Fighters is null) { _lastLife.Clear(); _floats.Clear(); return; }
        long now = Environment.TickCount64;
        foreach (var f in Fighters)
        {
            if (f.MaxLife <= 0) continue;
            if (_lastLife.TryGetValue(f.ActorId, out int before) && before != f.Life && (uint)f.Cell < CellCount)
                _floats.Add((f.Cell, f.Life - before, now));
            _lastLife[f.ActorId] = f.Life;
        }
        if (_floats.Count > 0 && !_anim.IsEnabled) _anim.Start();
    }

    private bool DrawFloats(DrawingContext ctx)
    {
        long now = Environment.TickCount64;
        _floats.RemoveAll(x => now - x.Start > FloatMs);
        foreach (var (cell, delta, start) in _floats)
        {
            double t = (now - start) / (double)FloatMs;
            var top = CellCenterTop(cell);
            string s = delta > 0 ? "+" + delta : delta.ToString(CultureInfo.InvariantCulture);
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FloatFace, 15, delta > 0 ? FloatHeal : FloatDmg);
            var sh = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FloatFace, 15, FloatShadow);
            var p = new Point(top.X - ft.Width / 2, top.Y - 34 - t * 26);
            using (ctx.PushOpacity(1 - t * t))
            {
                ctx.DrawText(sh, p + new Point(1, 1));
                ctx.DrawText(ft, p);
            }
        }
        return _floats.Count > 0;
    }

    // Changement de carte : les acteurs se posent DIRECTEMENT sur leur cellule (pas de glissement inter-cartes).
    private void SnapAllToCells()
    {
        _actorPath.Clear();
        _pending = null; _previewSrc = _previewDst = -1; _preview = Array.Empty<int>(); PreviewSteps = 0;   // nouvelle carte : plus aucun trajet en attente
        if (Actors is not null)
            foreach (var o in Actors)
                if (o is MapActor a) _actorPos[a.ActorId] = TargetOf(a);
        InvalidateVisual();
    }

    // Prepare l'animation : construit le chemin en pixels et lance le timer. Le perso GLISSE de cellule en cellule.
    private void StartMove(MoveInfo mv)
    {
        var pts = new List<Point>(mv.Cells.Count);
        foreach (var c in mv.Cells) if (c is >= 0 and < CellCount) pts.Add(CellCenterTop(c));
        if (pts.Count == 0) return;

        int idx = 0;
        if (_actorPos.TryGetValue(mv.ActorId, out var cur))
        {
            if (Near(cur, pts[0])) { if (pts.Count > 1) idx = 1; }   // deja au depart -> viser le waypoint suivant
            else _actorPos[mv.ActorId] = pts[0];                     // position PERIMEE -> on se replace au depart REEL (sinon trainee hors carte)
        }
        else _actorPos[mv.ActorId] = pts[0];                         // acteur inconnu -> apparait au depart
        _actorPath[mv.ActorId] = (pts, idx);
        if (!_anim.IsEnabled) _anim.Start();
        InvalidateVisual();
    }

    // Un acteur nouveau apparait a sa cellule (pas de glissement) ; un acteur qui a change de cellule glisse vers
    // sa nouvelle position via le timer. Retire les acteurs disparus.
    private void OnActorsChanged()
    {
        var present = new HashSet<long>();
        if (Actors is not null)
            foreach (var o in Actors)
                if (o is MapActor a)
                {
                    present.Add(a.ActorId);
                    if (!_actorPos.ContainsKey(a.ActorId)) _actorPos[a.ActorId] = TargetOf(a);
                }
        foreach (var id in _actorPos.Keys.Where(k => !present.Contains(k)).ToList()) _actorPos.Remove(id);
        foreach (var id in _actorPath.Keys.Where(k => !present.Contains(k)).ToList()) _actorPath.Remove(id);
        if (!_anim.IsEnabled) _anim.Start();
        InvalidateVisual();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        bool moving = false;
        if (Actors is not null)
            foreach (var o in Actors)
                if (o is MapActor a)
                {
                    if (!_actorPos.TryGetValue(a.ActorId, out var cur)) { _actorPos[a.ActorId] = TargetOf(a); continue; }

                    // Cible : le prochain waypoint du chemin en cours, sinon la cellule (destination).
                    Point target;
                    if (_actorPath.TryGetValue(a.ActorId, out var path))
                    {
                        target = path.Pts[path.Idx];
                        if (Near(cur, target))
                        {
                            int next = path.Idx + 1;
                            if (next >= path.Pts.Count) { _actorPath.Remove(a.ActorId); _actorPos[a.ActorId] = target; continue; }
                            _actorPath[a.ActorId] = (path.Pts, next);
                            target = path.Pts[next];
                        }
                    }
                    else target = TargetOf(a);

                    var np = StepToward(cur, target);
                    _actorPos[a.ActorId] = np;
                    if (_actorPath.ContainsKey(a.ActorId) || !Near(np, TargetOf(a))) moving = true;
                }
        InvalidateVisual();
        if (!moving && _floats.Count == 0) _anim.Stop();
    }

    private Point TargetOf(MapActor a) => CellCenterTop(a.CellId);

    private Point CellCenterTop(int cell)
    {
        var (cx, cy) = CellCenter(cell);
        return new Point(cx, cy - CellHeight(cell));
    }

    private static bool Near(Point a, Point b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy < 1.5; }

    private static Point StepToward(Point cur, Point target)
    {
        double dx = target.X - cur.X, dy = target.Y - cur.Y, d = Math.Sqrt(dx * dx + dy * dy);
        return d <= StepPx ? target : new Point(cur.X + dx / d * StepPx, cur.Y + dy / d * StepPx);
    }

    private void RebuildCells()
    {
        var arr = new MapCell?[CellCount];
        if (Cells is not null)
            foreach (var o in Cells)
                if (o is MapCell c && c.CellId is >= 0 and < CellCount) arr[c.CellId] = c;
        _byId = arr;
    }

    private static (double X, double Y) CellCenter(int id)
    {
        int row = id / Cols, col = id % Cols;
        double x = col * CellW + ((row & 1) == 1 ? CellW : HalfW);
        double y = row * RowPitch + HalfH + PadTop;
        return (x, y);
    }

    private double CellHeight(int id)
    {
        var c = _byId[id];
        if (c is null) return 0;
        int f = Math.Clamp(c.Floor, 0, 6);
        double h = (c.Mov ? 0 : WallH) + f * FloorScale;
        return Math.Min(h, MaxH);
    }

    private (IBrush Top, IBrush Right, IBrush Left) Palette(int id)
    {
        var c = _byId[id];
        if (c is null) return ((id & 1) == 0 ? NeutA : NeutB, NeutA, NeutA);
        if (c.Mov) return (WalkTop, WalkRight, WalkLeft);
        return c.Los ? (LosTop, LosRight, LosLeft) : (BlkTop, BlkRight, BlkLeft);
    }

    private static Geometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], true);
        for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(true);
        return g;
    }

    private static Geometry TopDiamond(double cx, double cyTop) => Poly(
        new Point(cx, cyTop - HalfH), new Point(cx + HalfW, cyTop),
        new Point(cx, cyTop + HalfH), new Point(cx - HalfW, cyTop));

    // Petit losange centre (marqueur d'interactif), rayon r.
    private static Geometry Diamond(double cx, double cy, double r) => Poly(
        new Point(cx, cy - r), new Point(cx + r, cy), new Point(cx, cy + r), new Point(cx - r, cy));

    // Grille en relief bakee une fois par carte.
    private void BuildRelief()
    {
        _relief = new RenderTargetBitmap(new PixelSize((int)CanvasW, (int)CanvasH), new Vector(96, 96));
        using var ctx = _relief.CreateDrawingContext();
        for (int id = 0; id < CellCount; id++)   // ordre = arriere vers avant (occlusion correcte)
        {
            var (cx, cy) = CellCenter(id);
            double h = CellHeight(id);
            var (top, right, left) = Palette(id);
            if (h >= 1)
            {
                ctx.DrawGeometry(left, null, Poly(
                    new Point(cx - HalfW, cy - h), new Point(cx, cy + HalfH - h),
                    new Point(cx, cy + HalfH), new Point(cx - HalfW, cy)));
                ctx.DrawGeometry(right, null, Poly(
                    new Point(cx, cy + HalfH - h), new Point(cx + HalfW, cy - h),
                    new Point(cx + HalfW, cy), new Point(cx, cy + HalfH)));
            }
            ctx.DrawGeometry(top, TopBorder, TopDiamond(cx, cy - h));
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Bg, new Rect(0, 0, CanvasW, CanvasH));
        if (_relief is null) BuildRelief();
        if (BackgroundImage is { } bg)
        {
            // pixel (offX, offY) de l'image = coin haut-gauche de la grille ; une case du jeu fait 86 px de large, ici 64
            double k = CellW / 86.0 * BgScale;
            var dest = new Rect(-BgOffsetX * k, PadTop - BgOffsetY * k, bg.Size.Width * k, bg.Size.Height * k);
            using (context.PushClip(new Rect(0, 0, CanvasW, CanvasH))) context.DrawImage(bg, new Rect(bg.Size), dest);
            using (context.PushOpacity(Math.Clamp(BgCellOpacity, 0, 1))) context.DrawImage(_relief!, new Rect(0, 0, CanvasW, CanvasH));
        }
        else context.DrawImage(_relief!, new Rect(0, 0, CanvasW, CanvasH));

        // Cellules de transition (mapChangeData != 0) : teinte teal par-dessus le relief, sous les acteurs.
        if (ShowTransitions)
            for (int id = 0; id < CellCount; id++)
                if (_byId[id] is { MapChangeData: not 0 })
                {
                    var (tx, ty) = CellCenter(id);
                    context.DrawGeometry(TransitionFill, TransitionPen, TopDiamond(tx, ty - CellHeight(id)));
                }

        // Numeros de cellule (repere manuel) : id sur la face du dessus de chaque case, 2 passes (ombre + corps) pour la lisibilite.
        if (ShowCellIds)
            for (int id = 0; id < CellCount; id++)
            {
                var top = CellCenterTop(id);
                string s = id.ToString(CultureInfo.InvariantCulture);
                var body = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CountTypeface, 9, CellIdBrush);
                double ox = top.X - body.Width / 2, oy = top.Y - body.Height / 2;
                context.DrawText(new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CountTypeface, 9, CellIdShadow), new Point(ox + 1, oy + 1));
                context.DrawText(body, new Point(ox, oy));
            }

        if (_hover is int hc)
        {
            var (hx, hy) = CellCenter(hc);
            context.DrawGeometry(HoverFill, null, TopDiamond(hx, hy - CellHeight(hc)));
        }

        // Elements interactifs (ressources/zaaps) : icone de la ressource si resolue (skill -> item), sinon losange
        // (vert = dispo, gris = epuise). Sous les acteurs. Icone grisee (opacite) si epuise.
        // En COMBAT on ne les dessine pas : ni les ressources ni les PNJ ne sont des cibles, et ils surchargeaient la
        // grille tactique (le jeu les masque aussi pendant un combat).
        if (Interactives is not null && !FightMode)
            foreach (var o in Interactives)
                if (o is MapInteractive it && it.CellId is >= 0 and < CellCount)
                {
                    var ip = CellCenterTop(it.CellId);
                    // Icone de la ressource si un skill (active OU desactive) resout vers un objet ; sinon losange.
                    Bitmap? icon = null;
                    foreach (var s in it.Skills) { icon = Stores.InteractiveIcons.Get(s.SkillId); if (icon is not null) break; }
                    if (icon is not null)
                    {
                        const double sz = 36;
                        var rect = new Rect(ip.X - sz / 2, ip.Y - sz + 8, sz, sz);
                        if (it.Harvestable) context.DrawImage(icon, rect);          // dispo = pleine opacite
                        else using (context.PushOpacity(0.4)) context.DrawImage(icon, rect);   // epuise = grisee
                    }
                    else
                        context.DrawGeometry(it.Harvestable ? ResourceOk : ResourceGone, ResourcePen, Diamond(ip.X, ip.Y, 7));
                }

        if (FightMarks is { } fm)
            foreach (var m in fm)
                if ((uint)m.Cell < CellCount)
                    context.DrawGeometry(m.Kind switch { 0 => FmPlace, 1 => FmReach, 2 => FmRange, _ => FmTarget }, null, TopDiamond(CellCenter(m.Cell).X, CellCenter(m.Cell).Y - CellHeight(m.Cell)));

        if (_preview.Count > 1)
        {
            foreach (int c in _preview.Skip(1)) context.DrawGeometry(PathFill, null, TopDiamond(CellCenter(c).X, CellCenter(c).Y - CellHeight(c)));
            int last = _preview[^1];
            context.DrawGeometry(null, _pending is null ? PathPen : PathPendingPen, TopDiamond(CellCenter(last).X, CellCenter(last).Y - CellHeight(last)));
        }

        // Recherche : losange doré sur chaque cellule qui correspond. Sélection : anneau net par-dessus (sous les acteurs).
        if (MatchCells is { Count: > 0 } mc)
            foreach (int c in mc)
                if ((uint)c < CellCount) context.DrawGeometry(MatchFill, MatchPen, TopDiamond(CellCenter(c).X, CellCenter(c).Y - CellHeight(c)));
        if (SelectedCell is int sc0 && (uint)sc0 < CellCount)
            context.DrawGeometry(null, SelectPen, TopDiamond(CellCenter(sc0).X, CellCenter(sc0).Y - CellHeight(sc0)));

        bool selfDrawn = false;
        if (Actors is not null)
        {
            // Une pastille PAR CELLULE : les acteurs empiles (meme cellId) donnent un seul point + un compteur.
            var byCell = new Dictionary<int, List<MapActor>>();
            foreach (var o in Actors)
                if (o is MapActor a && a.CellId is >= 0 and < CellCount)
                {
                    if (!byCell.TryGetValue(a.CellId, out var l)) byCell[a.CellId] = l = new List<MapActor>();
                    l.Add(a);
                }

            foreach (var cell in byCell.Keys.OrderBy(k => k))   // arriere -> avant
            {
                var group = byCell[cell];
                bool solo = group.Count == 1;
                bool isSelf = group.Any(IsSelfActor);
                selfDrawn |= isSelf;
                bool isArch = group.Any(IsArchActor);   // groupe contenant un archimonstre -> surbrillance dédiée
                var (fill, trail) = isSelf ? (SelfFill, SelfTrail)
                    : isArch ? (ArchFill, ArchTrail)
                    : group.Any(a => a.Kind == MapActorKind.Player) ? (PlayerFill, PlayerTrail)
                    : group.Any(a => a.Kind == MapActorKind.Monster) ? (MonsterFill, MonsterTrail)
                    : group.Any(a => a.Kind == MapActorKind.Npc) ? (NpcFill, NpcTrail)
                    : (PlayerFill, PlayerTrail);

                // position : anim fluide pour un acteur SEUL (en mouvement) ; figee au centre pour un empilement.
                var pos = solo && _actorPos.TryGetValue(group[0].ActorId, out var p) ? p : CellCenterTop(cell);
                if (solo && _actorPath.TryGetValue(group[0].ActorId, out var ap))
                {
                    // trainee = le VRAI chemin restant (cellule par cellule) : position courante -> waypoints restants.
                    var prev = pos;
                    for (int k = ap.Idx; k < ap.Pts.Count; k++)
                    {
                        context.DrawLine(trail, prev, ap.Pts[k]);
                        prev = ap.Pts[k];
                    }
                }
                double r = isSelf ? 13 : isArch ? 13 : (solo ? 11 : 12);
                if (isArch) context.DrawEllipse(ArchGlow, null, pos, r + 6, r + 6);   // halo magenta autour de l'archimonstre
                context.DrawEllipse(fill, ActorBorder, pos, r, r);
                // Portrait local du jeu (1er type de monstre du groupe) à la place de la simple pastille, si l'icône est disponible.
                if (!isSelf && group.FirstOrDefault(a => a.Kind == MapActorKind.Monster) is { Monsters: { Count: > 0 } pm }
                    && Stores.MonsterIcons.Get(pm[0].GenericId) is { } portrait)
                {
                    double pr = r + 4;
                    using (context.PushGeometryClip(new EllipseGeometry(new Rect(pos.X - pr, pos.Y - pr, pr * 2, pr * 2))))
                        context.DrawImage(portrait, new Rect(pos.X - pr, pos.Y - pr, pr * 2, pr * 2));
                    context.DrawEllipse(null, new ImmutablePen(fill, 2.2), pos, pr, pr);
                }
                if (!solo)
                {
                    var ft = new FormattedText(group.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight, CountTypeface, 11, CountBrush);
                    context.DrawText(ft, new Point(pos.X - ft.Width / 2, pos.Y - ft.Height / 2));
                }
            }
        }
        if (FightMode && Fighters is { } fts)
            foreach (var f in fts.Where(x => x.Alive && (uint)x.Cell < CellCount).OrderBy(x => x.Cell))
            {
                var pos = CellCenterTop(f.Cell);
                var col = f.IsSelf ? FtSelf : f.IsEnemy ? FtEnemy : FtAlly;
                if (f.IsSelf) selfDrawn = true;
                double fr = f.IsSelf ? 13 : 11;
                context.DrawEllipse(col, ActorBorder, pos, fr, fr);
                // Icone de CLASSE sur la pastille quand la classe du combattant est connue (deja decodee des paquets) :
                // on reconnait les personnages d'un coup d'oeil au lieu de ronds de couleur. Meme fournisseur d'icones et
                // meme decoupe circulaire que la carte hors combat, donc aucun chargement supplementaire.
                if (f.Breed > 0 && Stores.ClassIcons.GetById(f.Breed) is { } classIcon)
                {
                    double pr = fr + 3;
                    using (context.PushGeometryClip(new EllipseGeometry(new Rect(pos.X - pr, pos.Y - pr, pr * 2, pr * 2))))
                        context.DrawImage(classIcon, new Rect(pos.X - pr, pos.Y - pr, pr * 2, pr * 2));
                    context.DrawEllipse(null, new Pen(col, 2.2), pos, pr, pr);
                }
                var bar = new Rect(pos.X - 13, pos.Y - 24, 26, 4);
                context.FillRectangle(HpBack, bar);
                context.FillRectangle(f.IsEnemy ? FtEnemy : HpFill, new Rect(bar.X, bar.Y, bar.Width * Math.Clamp(f.HpPercent, 0, 100) / 100.0, bar.Height));
            }
        // Notre perso n'est pas (encore) dans la liste d'acteurs (carte reçue avant son nom, acteur retiré pendant une
        // transition…) mais sa cellule est connue : le marqueur jaune reste affiché au lieu de disparaître.
        if (!selfDrawn && SourceCell is int own && (uint)own < CellCount)
            context.DrawEllipse(SelfFill, ActorBorder, CellCenterTop(own), 13, 13);

        DrawGroupTooltip(context);
        DrawFloats(context);
    }

    // Infobulle d'un groupe de monstres survolé (comme en jeu) : niveau total puis chaque monstre avec son niveau.
    private static readonly IBrush TipBack = new SolidColorBrush(Color.FromArgb(235, 22, 26, 38)), TipHead = new SolidColorBrush(Color.FromRgb(240, 240, 250)), TipLine = new SolidColorBrush(Color.FromRgb(190, 200, 235));
    private void DrawGroupTooltip(DrawingContext context)
    {
        if (FightMode || _hover is not int hc || MonsterAt(hc) is not { Members: { Count: > 0 } members } g) return;
        var names = (g.Monsters ?? Array.Empty<MonsterEntry>()).GroupBy(m => m.GenericId).ToDictionary(x => x.Key, x => x.First().Name);
        var lines = new List<(string Text, bool Head)> { ($"Niveau {g.GroupLevel}", true) };
        foreach (var m in members.OrderByDescending(x => x.Level))
            lines.Add(((names.TryGetValue(m.GenericId, out var nm) ? nm : "Monstre " + m.GenericId) + (m.Level > 0 ? $" ({m.Level})" : ""), false));
        var fts = lines.Select(l => new FormattedText(l.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, CountTypeface, l.Head ? 13 : 11.5, l.Head ? TipHead : TipLine)).ToList();
        double w = fts.Max(f => f.Width) + 20, h = fts.Sum(f => f.Height + 2) + 14;
        var top = CellCenterTop(hc);
        double x = Math.Clamp(top.X - w / 2, 2, CanvasW - w - 2), y = Math.Clamp(top.Y - 30 - h, 2, CanvasH - h - 2);
        context.DrawRectangle(TipBack, null, new Rect(x, y, w, h), 8, 8);
        double cy = y + 7;
        foreach (var f in fts) { context.DrawText(f, new Point(x + (w - f.Width) / 2, cy)); cy += f.Height + 2; }
    }

    // Trajet (pathfinder existant) de notre perso vers dst ; mis en cache tant que (source, cible) ne changent pas.
    private void SetPreview(int dst)
    {
        int src = SelfCell() ?? -1;
        if (dst < 0 || src < 0) { if (_preview.Count > 0 || PreviewSteps != 0) { _preview = Array.Empty<int>(); PreviewSteps = 0; _previewSrc = _previewDst = -1; InvalidateVisual(); } return; }
        if (src == _previewSrc && dst == _previewDst) return;
        _previewSrc = src; _previewDst = dst;
        _preview = MapPathPreview.Compute(src, dst, c => (uint)c < CellCount && _byId[c] is { Mov: true });
        PreviewSteps = _preview.Count > 1 ? _preview.Count - 1 : 0;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        int? h = SpriteCellAt(p.X, p.Y) ?? HitTest(p.X, p.Y);   // sprite d'abord : le survol décrit ce qu'on VOIT sous le curseur
        if (h != _hover)
        {
            _hover = h;
            SetValue(HoveredCellProperty, h);
            SetValue(HoveredActorsProperty, ActorsAt(h));
            SetValue(HoveredInteractiveProperty, InteractiveAt(h));
            if (_pending is null && !FightMode) SetPreview(h ?? -1);   // aperçu du trajet au survol
            InvalidateVisual();
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_hover is not null)
        {
            _hover = null;
            SetValue(HoveredCellProperty, null);
            SetValue(HoveredActorsProperty, null);
            SetValue(HoveredInteractiveProperty, null);
            InvalidateVisual();
        }
        base.OnPointerExited(e);
    }

    /// <summary>Leve quand on clique-GAUCHE une cellule : (cellule du perso, cellule cible). Le VM calcule le chemin.</summary>
    public event Action<int, int>? MoveRequested;

    /// <summary>Leve quand on choisit "Recolter" dans le menu contextuel (clic droit sur un interactif).</summary>
    public event Action<MapInteractive>? HarvestRequested;

    /// <summary>Captures the selected bot/map state when the context menu is CREATED, not when clicked.</summary>
    public Func<MapAttackMenuOrigin?>? AttackOriginProvider { get; set; }

    /// <summary>Attaquer carries immutable menu provenance; no implicit SelectedBot fallback.</summary>
    public event Action<MapAttackMenuRequest>? AttackRequested;

    /// <summary>Clic droit sur un autre joueur : (joueur, action "trade" | "party").</summary>
    public event Action<MapActor, string>? PlayerActionRequested;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        int? cell = HitTest(p.X, p.Y);

        if (FightMode && cell is int fc && (props.IsLeftButtonPressed || props.IsRightButtonPressed))
        {
            Focus();
            FightCellClicked?.Invoke(fc, props.IsRightButtonPressed);
            e.Handled = true;
            return;
        }
        if (props.IsRightButtonPressed)
        {
            cell = SpriteCellAt(p.X, p.Y) ?? cell;   // menu de ce qu'on voit sous le curseur
        }
        if (props.IsRightButtonPressed)
        {
            // Clic DROIT : menu contextuel = recolte/utilisation d'un interactif + attaque d'un groupe de monstres.
            // La fiche de ce qui est sous le curseur s'ouvre DIRECTEMENT dans le panneau de droite ; le menu ne propose que le verbe.
            if (cell is int rc && (ActorsAt(rc) is { Count: > 0 } || InteractiveAt(rc) is not null)) CellInspector?.Invoke(rc);
            if (BuildContextMenu(cell) is { } menu) { menu.ShowAt(this, showAtPointer: true); e.Handled = true; }
        }
        else if (props.IsLeftButtonPressed)
        {
            Focus();
            // Clic gauche = déplacement (le trajet a été prévisualisé au survol). La fiche d'une entité s'ouvre par le clic DROIT.
            if (cell is int target && SelfCell() is int source && source != target && _byId[target] is { Mov: true })
            {
                MoveRequested?.Invoke(source, target);
                SetPreview(-1);
                e.Handled = true;
            }
        }
        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        string? act = e.Key switch
        {
            Key.Escape => "escape",
            Key.Right or Key.Down => "next",
            Key.Left or Key.Up => "prev",
            _ => null,
        };
        if (act == "escape" && _pending is not null) { _pending = null; SetPreview(-1); e.Handled = true; return; }   // annule d'abord l'aperçu verrouillé
        if (act is not null) { KeyAction?.Invoke(act); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // Menu contextuel d'une cellule : entree "action interactif" (verbe du skill : "Couper — Frene", "Pecher — Goujon",
    // "Utiliser", "Entrer"...) grisee si indisponible, + entree "Attaquer <groupe>" si un groupe de monstres est la.
    // Retourne null si rien a proposer sur cette cellule.
    private MenuFlyout? BuildContextMenu(int? cell)
    {
        var menu = new MenuFlyout();

        if (InteractiveAt(cell) is MapInteractive it)
        {
            string name = it.Name ?? Loc.Fmt("L.Bots.Map.InteractiveType", it.ElementTypeId);
            string label = !string.IsNullOrWhiteSpace(it.ActionName) ? it.ActionName! : name;   // le verbe seul : Faucher, Cueillir, Miner…
            if (!it.Harvestable) label += Loc.T("L.Bots.Map.Unavailable");
            var action = new MenuItem { Header = label, IsEnabled = it.Harvestable };   // grise si aucun skill actif -> evite le refus serveur (iwq)
            action.Click += (_, _) => HarvestRequested?.Invoke(it);
            menu.Items.Add(action);
        }

        if (MonsterAt(cell) is MapActor mob
            && AttackOriginProvider?.Invoke() is { } origin
            && origin.VisibleActors.Any(a => ReferenceEquals(a, mob)))
        {
            var atk = new MenuItem { Header = Loc.T("L.MapInsp.AttackVerb") };   // une seule action contre des monstres
            atk.Click += (_, _) =>
            {
                // Between menu opening and choosing the verb the operator may
                // switch bots, the map may be replaced, or the group may move.
                // Never redirect this click to the newly selected bot.
                var current = AttackOriginProvider?.Invoke();
                if (origin.Allows(current, mob, current?.VisibleActors))
                    AttackRequested?.Invoke(new MapAttackMenuRequest(mob, origin));
            };
            menu.Items.Add(atk);
        }

        // Autre joueur sous le curseur (comme le menu du jeu : échange, invitation de groupe, copier le pseudo).
        if (cell is not null && Actors is not null)
            foreach (var o in Actors)
                if (o is MapActor pl && pl.CellId == cell && pl.Kind == MapActorKind.Player && !IsSelfActor(pl) && pl.Name is { Length: > 0 } pname)
                {
                    var trade = new MenuItem { Header = "Proposer un échange à " + pname };
                    trade.Click += (_, _) => PlayerActionRequested?.Invoke(pl, "trade");
                    var party = new MenuItem { Header = "Inviter dans mon groupe" };
                    party.Click += (_, _) => PlayerActionRequested?.Invoke(pl, "party");
                    var copy = new MenuItem { Header = "Copier le pseudo" };
                    copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(pname); };
                    menu.Items.Add(trade); menu.Items.Add(party); menu.Items.Add(copy);
                    break;
                }

        return menu.Items.Count > 0 ? menu : null;
    }

    // Un acteur est-il un ARCHIMONSTRE ? (groupe de monstres dont un membre a un genericId d'archi.)
    private static bool IsArchActor(MapActor a) =>
        a.Kind == MapActorKind.Monster && a.Monsters is { } ms && ms.Any(e => ArchMonsters.IsArch(e.GenericId) || GivreClone.App.Services.Combat.CapturableMonsters.Is(e.GenericId));

    private MapActor? MonsterAt(int? cell)
    {
        if (cell is null || Actors is null) return null;
        foreach (var o in Actors)
            if (o is MapActor a && a.CellId == cell && a.Kind == MapActorKind.Monster) return a;
        return null;
    }

    // Cellule SOURCE de NOTRE perso : d'abord SourceCell (SelfCellId autoritaire du VM, recalee serveur) ; a defaut,
    // l'acteur Joueur dont le nom == SelfName. -> le pathfinding part toujours de la cellule reelle.
    private int? SelfCell()
    {
        if (SourceCell is int sc && (uint)sc < CellCount) return sc;
        if (Actors is null) return null;
        foreach (var o in Actors)
            if (o is MapActor a && IsSelfActor(a)) return a.CellId;
        return null;
    }

    private MapInteractive? InteractiveAt(int? cell)
    {
        if (cell is null || Interactives is null) return null;
        foreach (var o in Interactives) if (o is MapInteractive it && it.CellId == cell) return it;
        return null;
    }

    private IReadOnlyList<MapActor>? ActorsAt(int? cell)
    {
        if (cell is null || Actors is null) return null;
        var list = new List<MapActor>();
        foreach (var o in Actors)
            if (o is MapActor a && a.CellId == cell) list.Add(a);
        return list.Count > 0 ? list : null;
    }

    // Cellule d'une entité dont le SPRITE (pastille / icône de ressource, dessinés AU-DESSUS du sol) est sous le curseur ; null si aucun.
    // Sans cela, cliquer l'icône d'une ressource visait la case située derrière elle, sans entité -> pas de menu / pas de fiche.
    private int? SpriteCellAt(double px, double py)
    {
        int? best = null;
        if (Interactives is not null)
            foreach (var o in Interactives)
                if (o is MapInteractive it && it.CellId is >= 0 and < CellCount)
                {
                    var ip = CellCenterTop(it.CellId);
                    bool hasIcon = false;
                    foreach (var sk in it.Skills) if (Stores.InteractiveIcons.Get(sk.SkillId) is not null) { hasIcon = true; break; }
                    var r = hasIcon ? new Rect(ip.X - 18, ip.Y - 28, 36, 36) : new Rect(ip.X - 9, ip.Y - 9, 18, 18);
                    if (r.Contains(new Point(px, py)) && (best is null || it.CellId > best)) best = it.CellId;
                }
        if (Actors is not null)
            foreach (var o in Actors)
                if (o is MapActor a && a.CellId is >= 0 and < CellCount)
                {
                    var pos = _actorPos.TryGetValue(a.ActorId, out var p) ? p : CellCenterTop(a.CellId);
                    double dx = px - pos.X, dy = py - pos.Y;
                    if (dx * dx + dy * dy <= 18 * 18 && (best is null || a.CellId >= best)) best = a.CellId;
                }
        return best;
    }

    // Hit-test sur l'empreinte au sol (base plate) : losange Manhattan.
    private static int? HitTest(double px, double py)
    {
        for (int id = 0; id < CellCount; id++)
        {
            var (x, y) = CellCenter(id);
            if (Math.Abs(px - x) / HalfW + Math.Abs(py - y) / HalfH <= 1.0) return id;
        }
        return null;
    }
}

/// <summary>Case colorée de la carte de combat : 0 placement, 1 déplacement possible, 2 portée du sort, 3 cible.</summary>
public sealed record FightMark(int Cell, int Kind);
