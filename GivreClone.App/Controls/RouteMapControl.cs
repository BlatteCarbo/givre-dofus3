using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GivreClone.App.Services;
using GivreClone.App.Services.WorldMap;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GivreClone.App.Controls;

/// <summary>Une coordonnée de la carte du monde (clic sur la mini-carte).</summary>
public sealed record RouteCell(int X, int Y);

/// <summary>Un point du trajet sur la mini-carte : position, numéro d'ordre, sélection, étape vers le suivant en erreur ?</summary>
public sealed record RoutePoint(int X, int Y, int Number, bool Selected, bool Gather, bool Fight, bool Problem);

/// <summary>Segment entre deux points consécutifs, coloré selon l'état de l'étape.</summary>
public sealed record RouteLine(int X1, int Y1, int X2, int Y2, RoadLegState State);

/// <summary>Instantané immuable dessiné par <see cref="RouteMapControl"/> (construit par la VM à chaque changement).</summary>
public sealed record RouteMapModel(IReadOnlySet<(int X, int Y)> Coords, IReadOnlyList<RoutePoint> Points, IReadOnlyList<RouteLine> Lines,
                                   (int X, int Y)? Bot, IReadOnlySet<(int X, int Y)>? Marks = null);

/// <summary>
/// Mini-carte du MONDE de l'éditeur de trajet : une case par coordonnée de carte connue du calque, le trajet (points numérotés reliés
/// par des segments vert = à pied, orange = via zaap / havre-sac, rouge = injoignable) et la position du perso. Glisser = déplacer la
/// vue, molette = zoom, clic gauche = <see cref="CellCommand"/>, clic droit = <see cref="CellRemoveCommand"/>. Brosses immuables.
/// </summary>
public sealed class RouteMapControl : Control
{
    private static readonly ImmutableSolidColorBrush Bg = new(Color.Parse("#14181F"));
    private static readonly ImmutableSolidColorBrush MapCell = new(Color.Parse("#2B3442"));
    private static readonly ImmutableSolidColorBrush MapCellHover = new(Color.Parse("#46536A"));
    private static readonly ImmutableSolidColorBrush MarkCell = new(Color.Parse("#3B4E3A"));
    private static readonly ImmutableSolidColorBrush TileHover = new(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
    private static readonly ImmutableSolidColorBrush TileMark = new(Color.FromArgb(0x60, 0x40, 0xE0, 0x70));
    private static readonly ImmutableSolidColorBrush PointFill = new(Color.Parse("#4FA3FF"));
    private static readonly ImmutableSolidColorBrush PointProblem = new(Color.Parse("#E05A50"));
    private static readonly ImmutableSolidColorBrush BotFill = new(Color.Parse("#E8A63C"));
    private static readonly ImmutableSolidColorBrush Text = new(Colors.White);
    private static readonly ImmutableSolidColorBrush AxisText = new(Color.Parse("#8892A0"));
    private static readonly ImmutablePen PointBorder = new(new ImmutableSolidColorBrush(Colors.White), 1.2);
    private static readonly ImmutablePen SelectedPen = new(new ImmutableSolidColorBrush(Colors.White), 2.5);
    private static readonly ImmutablePen OkPen = new(new ImmutableSolidColorBrush(Color.Parse("#63D3A6")), 2.2);
    private static readonly ImmutablePen TeleportPen = new(new ImmutableSolidColorBrush(Color.Parse("#F2A541")), 2.2, new ImmutableDashStyle(new double[] { 3, 2 }, 0));
    private static readonly ImmutablePen BadPen = new(new ImmutableSolidColorBrush(Color.Parse("#E05A50")), 2.4, new ImmutableDashStyle(new double[] { 2, 2 }, 0));
    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    public static readonly StyledProperty<RouteMapModel?> ModelProperty = AvaloniaProperty.Register<RouteMapControl, RouteMapModel?>(nameof(Model));
    /// <summary>Coordonnée à centrer (changer la valeur recentre la vue ; le numéro de séquence permet de recentrer au même endroit).</summary>
    public static readonly StyledProperty<(int X, int Y, int Seq)?> CenterOnProperty = AvaloniaProperty.Register<RouteMapControl, (int X, int Y, int Seq)?>(nameof(CenterOn));
    public static readonly StyledProperty<ICommand?> CellCommandProperty = AvaloniaProperty.Register<RouteMapControl, ICommand?>(nameof(CellCommand));
    public static readonly StyledProperty<ICommand?> CellRemoveCommandProperty = AvaloniaProperty.Register<RouteMapControl, ICommand?>(nameof(CellRemoveCommand));
    /// <summary>Coordonnée survolée (« x,y »), pour la barre d'état de l'éditeur.</summary>
    public static readonly StyledProperty<RouteCell?> HoverCellProperty = AvaloniaProperty.Register<RouteMapControl, RouteCell?>(nameof(HoverCell), defaultBindingMode: Avalonia.Data.BindingMode.OneWayToSource);

    public RouteMapModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public (int X, int Y, int Seq)? CenterOn { get => GetValue(CenterOnProperty); set => SetValue(CenterOnProperty, value); }
    public ICommand? CellCommand { get => GetValue(CellCommandProperty); set => SetValue(CellCommandProperty, value); }
    public ICommand? CellRemoveCommand { get => GetValue(CellRemoveCommandProperty); set => SetValue(CellRemoveCommandProperty, value); }
    public RouteCell? HoverCell { get => GetValue(HoverCellProperty); set => SetValue(HoverCellProperty, value); }

    static RouteMapControl() => AffectsRender<RouteMapControl>(ModelProperty);

    private double _cx, _cy;          // coordonnée monde au centre de la vue
    private double _size = 20;        // côté d'une case en pixels
    private const double MinSize = 5, MaxSize = 64;
    private Point? _press;            // début d'un glisser
    private (double X, double Y) _pressCenter;
    private bool _dragged;
    private (int X, int Y)? _hover;

    // Vraies images de la carte du monde (DofusDB) sous les cases quand le zoom le permet ; sinon cases unies comme avant.
    private static DofusDbWorldMap? _tiles;
    private static bool _tilesTried;
    private readonly Dictionary<long, Bitmap?> _bitmaps = new();
    private readonly HashSet<long> _loading = new();
    private const double TileMinSize = double.MaxValue;   // tuiles DofusDB DÉSACTIVÉES (trop lourd pour la mini-carte) : repasser à 17 pour les réactiver

    private static DofusDbWorldMap? Tiles()
    {
        if (_tiles is not null || _tilesTried) return _tiles;
        _tilesTried = true;
        try
        {
            if (Stores.Launch.GameData is { } db)
            {
                DofusDbWorldMap.CacheFolder = System.IO.Path.Combine(MZP19LocalClone.DataRoot, "worldmap");
                _tiles = new DofusDbWorldMap(db.AllMapPositions());
            }
        }
        catch (Exception e) when (e is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { _tiles = null; }
        return _tiles;
    }

    private Bitmap? TileBitmap(DofusDbWorldMap t, int x, int y)
    {
        long? id = t.MapAt(1, x, y) ?? t.MapAt(2, x, y);   // Monde des Douze d'abord, puis Incarnam
        if (id is not long mapId) return null;
        if (_bitmaps.TryGetValue(mapId, out var b)) return b;
        if (_loading.Add(mapId))
            _ = DofusDbWorldMap.EnsureImageAsync(mapId).ContinueWith(task => Dispatcher.UIThread.Post(() =>
            {
                _loading.Remove(mapId);
                if (task.Result is not { } path) return;
                try { if (_bitmaps.Count > 600) _bitmaps.Clear(); _bitmaps[mapId] = new Bitmap(path); InvalidateVisual(); }
                catch (Exception e) when (e is System.IO.IOException or ArgumentException or InvalidOperationException) { _bitmaps[mapId] = null; }
            }), System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);
        return null;
    }

    public RouteMapControl()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.MediumQuality);
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CenterOnProperty && CenterOn is { } f) { _cx = f.X; _cy = f.Y; InvalidateVisual(); }
    }

    private Point ToScreen(double x, double y) => new(Bounds.Width / 2 + (x - _cx) * _size, Bounds.Height / 2 + (y - _cy) * _size);
    private (int X, int Y) ToWorld(Point p) => ((int)Math.Floor((p.X - Bounds.Width / 2) / _size + _cx + 0.5), (int)Math.Floor((p.Y - Bounds.Height / 2) / _size + _cy + 0.5));

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
        var m = Model;
        if (m is null) return;

        // Cases des cartes connues dans la fenêtre visible.
        int x0 = (int)Math.Floor(_cx - w / 2 / _size) - 1, x1 = (int)Math.Ceiling(_cx + w / 2 / _size) + 1;
        int y0 = (int)Math.Floor(_cy - h / 2 / _size) - 1, y1 = (int)Math.Ceiling(_cy + h / 2 / _size) + 1;
        double cell = Math.Max(1, _size - (_size >= 9 ? 1.5 : 0.5));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!m.Coords.Contains((x, y))) continue;
                var c = ToScreen(x, y);
                var brush = _hover is { } hv && hv.X == x && hv.Y == y ? MapCellHover
                          : m.Marks is { } mk && mk.Contains((x, y)) ? MarkCell : MapCell;
                var rect = new Rect(c.X - cell / 2, c.Y - cell / 2, cell, cell);
                var bmp = _size >= TileMinSize && Tiles() is { } tl ? TileBitmap(tl, x, y) : null;
                if (bmp is not null)
                {
                    ctx.DrawImage(bmp, new Rect(bmp.Size), rect);
                    if (!ReferenceEquals(brush, MapCell)) ctx.FillRectangle(brush == MapCellHover ? TileHover : TileMark, rect);   // survol / marque : voile par-dessus l'image
                }
                else ctx.FillRectangle(brush, rect);
            }

        // Repères de coordonnées (tous les 10) quand le zoom le permet.
        if (_size >= 8)
            for (int x = (int)Math.Ceiling(x0 / 10.0) * 10; x <= x1; x += 10)
            {
                var c = ToScreen(x, y0 + 1);
                ctx.DrawText(new FormattedText(x.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 10, AxisText), new Point(c.X - 6, 4));
            }

        // Trajet : segments puis points (numérotés).
        foreach (var l in m.Lines)
        {
            var pen = l.State switch { RoadLegState.Ok => OkPen, RoadLegState.Teleport => TeleportPen, _ => BadPen };
            ctx.DrawLine(pen, ToScreen(l.X1, l.Y1), ToScreen(l.X2, l.Y2));
        }
        double r = Math.Clamp(_size * 0.45, 5, 13);
        foreach (var p in m.Points)
        {
            var c = ToScreen(p.X, p.Y);
            ctx.DrawEllipse(p.Problem ? PointProblem : PointFill, p.Selected ? SelectedPen : PointBorder, c, r, r);
            if (r >= 7)
            {
                var ft = new FormattedText(p.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, Math.Min(11, r * 1.1), Text);
                ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
            }
        }
        if (m.Bot is { } b)
        {
            var c = ToScreen(b.X, b.Y);
            double s = Math.Clamp(_size * 0.4, 4, 10);
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(c.X, c.Y - s), true);
                gc.LineTo(new Point(c.X + s, c.Y)); gc.LineTo(new Point(c.X, c.Y + s)); gc.LineTo(new Point(c.X - s, c.Y));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(BotFill, PointBorder, g);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsRightButtonPressed)
        {
            var (x, y) = ToWorld(pt.Position);
            var cmd = CellRemoveCommand; var arg = new RouteCell(x, y);
            if (cmd?.CanExecute(arg) == true) cmd.Execute(arg);
            e.Handled = true;
        }
        else if (pt.Properties.IsLeftButtonPressed)
        {
            _press = pt.Position; _pressCenter = (_cx, _cy); _dragged = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_press is { } start)
        {
            var d = p - start;
            if (_dragged || Math.Abs(d.X) + Math.Abs(d.Y) > 4)
            {
                _dragged = true;
                _cx = _pressCenter.X - d.X / _size; _cy = _pressCenter.Y - d.Y / _size;
                InvalidateVisual();
            }
        }
        var hw = ToWorld(p);
        if (_hover != hw)
        {
            _hover = hw;
            HoverCell = Model?.Coords.Contains(hw) == true ? new RouteCell(hw.X, hw.Y) : null;
            InvalidateVisual();
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_press is not null && !_dragged && e.InitialPressMouseButton == MouseButton.Left)
        {
            var (x, y) = ToWorld(e.GetPosition(this));
            var cmd = CellCommand; var arg = new RouteCell(x, y);
            if (cmd?.CanExecute(arg) == true) cmd.Execute(arg);
        }
        _press = null; _dragged = false;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_hover is not null) { _hover = null; HoverCell = null; InvalidateVisual(); }
        base.OnPointerExited(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Zoom centré sous le curseur : la coordonnée pointée reste sous la souris.
        var p = e.GetPosition(this);
        double wx = (p.X - Bounds.Width / 2) / _size + _cx, wy = (p.Y - Bounds.Height / 2) / _size + _cy;
        _size = Math.Clamp(_size * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), MinSize, MaxSize);
        _cx = wx - (p.X - Bounds.Width / 2) / _size; _cy = wy - (p.Y - Bounds.Height / 2) / _size;
        InvalidateVisual();
        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }
}
