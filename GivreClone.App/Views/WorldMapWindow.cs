using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.Services.Resources;
using GivreClone.App.Services.WorldMap;

namespace GivreClone.App.Views;

/// <summary>Un bot affiché sur la carte du monde.</summary>
public sealed record WorldMapBot(string Name, long MapId, int Pid = 0);

/// <summary>Vraie carte du monde (images DofusDB) : glisser pour se déplacer, molette pour zoomer, clic pour lire la coordonnée / l'id de carte. Surbrillance des endroits où récolter une ressource, position des bots.</summary>
public sealed class WorldMapWindow : Window
{
    private sealed class MapView : Control
    {
        public DofusDbWorldMap? Map;
        public int World = 1;
        public double Zoom = 0.4, OffX, OffY;
        public IReadOnlyList<ResourceNode> Nodes = Array.Empty<ResourceNode>();
        public IReadOnlyList<(string Name, int X, int Y)> Bots = Array.Empty<(string, int, int)>();
        public (int X, int Y)? Selected;
        public Action<int, int>? Hover, Picked;

        private readonly Dictionary<long, Bitmap?> _bitmaps = new();
        private readonly HashSet<long> _loading = new();
        private bool _drag; private Point _last; private bool _moved;
        private static readonly Typeface Face = new("Segoe UI");

        public MapView()
        {
            ClipToBounds = true;
            Focusable = true;
            RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
            KeyDown += (_, e) =>
            {
                double step = 80;
                switch (e.Key)
                {
                    case Key.Left: OffX += step; break;
                    case Key.Right: OffX -= step; break;
                    case Key.Up: OffY += step; break;
                    case Key.Down: OffY -= step; break;
                    case Key.OemPlus or Key.Add: ZoomAt(Bounds.Width / 2, Bounds.Height / 2, 1.2); break;
                    case Key.OemMinus or Key.Subtract: ZoomAt(Bounds.Width / 2, Bounds.Height / 2, 1 / 1.2); break;
                    default: return;
                }
                InvalidateVisual(); e.Handled = true;
            };
            PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _drag = true; _moved = false; _last = e.GetPosition(this); e.Pointer.Capture(this); } };
            PointerReleased += (_, e) =>
            {
                if (_drag && !_moved) { var (x, y) = DofusDbWorldMap.FromScreen(_last.X, _last.Y, Zoom, OffX, OffY); Selected = (x, y); Picked?.Invoke(x, y); InvalidateVisual(); }
                _drag = false; e.Pointer.Capture(null);
            };
            PointerMoved += (_, e) =>
            {
                var p = e.GetPosition(this);
                if (_drag)
                {
                    if (Math.Abs(p.X - _last.X) + Math.Abs(p.Y - _last.Y) > 3) _moved = true;
                    if (_moved) { OffX += p.X - _last.X; OffY += p.Y - _last.Y; _last = p; InvalidateVisual(); }
                }
                var (x, y) = DofusDbWorldMap.FromScreen(p.X, p.Y, Zoom, OffX, OffY);
                Hover?.Invoke(x, y);
            };
            PointerWheelChanged += (_, e) =>
            {
                var p = e.GetPosition(this);
                ZoomAt(p.X, p.Y, e.Delta.Y > 0 ? 1.2 : 1 / 1.2); InvalidateVisual(); e.Handled = true;
            };
        }

        public bool ShowGrid = true;
        public (int X, int Y)? Link;
        public IReadOnlyList<(int X, int Y, int World)> Zaaps = Array.Empty<(int, int, int)>();
        public bool ShowZaaps = true;   // bot suivi : ligne vers la sélection

        private void ZoomAt(double px, double py, double factor)
        {
            double nz = Math.Clamp(Zoom * factor, 0.03, 3);
            OffX = px - (px - OffX) * nz / Zoom; OffY = py - (py - OffY) * nz / Zoom;   // le point sous la souris reste fixe
            Zoom = nz;
        }

        /// <summary>Cadre toute la zone connue du calque dans la fenêtre.</summary>
        public void Fit(int minX, int minY, int maxX, int maxY)
        {
            double zx = Bounds.Width / ((maxX - minX + 1) * DofusDbWorldMap.TileWidth), zy = Bounds.Height / ((maxY - minY + 1) * DofusDbWorldMap.TileHeight);
            Zoom = Math.Clamp(Math.Min(zx, zy), 0.03, 3);
            CenterOn((minX + maxX) / 2, (minY + maxY) / 2);
        }

        public void CenterOn(int x, int y)
        {
            OffX = Bounds.Width / 2 - (x + 0.5) * DofusDbWorldMap.TileWidth * Zoom;
            OffY = Bounds.Height / 2 - (y + 0.5) * DofusDbWorldMap.TileHeight * Zoom;
            InvalidateVisual();
        }

        private Bitmap? BitmapOf(long mapId)
        {
            if (_bitmaps.TryGetValue(mapId, out var b)) return b;
            if (_loading.Add(mapId))
                _ = DofusDbWorldMap.EnsureImageAsync(mapId).ContinueWith(t => Dispatcher.UIThread.Post(() =>
                {
                    _loading.Remove(mapId);
                    if (t.Result is not { } path) return;
                    try
                    {
                        if (_bitmaps.Count > 700) _bitmaps.Clear();   // plafond mémoire : on recharge ce qui est visible depuis le disque
                        _bitmaps[mapId] = new Bitmap(path);
                        InvalidateVisual();
                    }
                    catch (Exception e) when (e is System.IO.IOException or ArgumentException or InvalidOperationException) { _bitmaps[mapId] = null; }
                }), System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);
            return null;
        }

        private static FormattedText Text(string s, double size, IBrush brush) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, brush);

        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(18, 20, 26)), new Rect(Bounds.Size));
            if (Map is null) return;
            double tw = DofusDbWorldMap.TileWidth * Zoom, th = DofusDbWorldMap.TileHeight * Zoom;
            var (x0, y0) = DofusDbWorldMap.FromScreen(0, 0, Zoom, OffX, OffY);
            var (x1, y1) = DofusDbWorldMap.FromScreen(Bounds.Width, Bounds.Height, Zoom, OffX, OffY);
            bool images = Zoom >= 0.12;
            var fill = new SolidColorBrush(Color.FromRgb(52, 62, 84));
            var edge = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
            foreach (var t in Map.TilesIn(World, x0, y0, x1, y1))
            {
                var (sx, sy) = DofusDbWorldMap.ToScreen(t.X, t.Y, Zoom, OffX, OffY);
                var r = new Rect(sx, sy, tw, th);
                var bmp = images ? BitmapOf(t.MapId) : null;
                if (bmp is not null) dc.DrawImage(bmp, new Rect(bmp.Size), r); else dc.DrawRectangle(fill, edge, r);
                if (bmp is null && images && tw > 60) dc.DrawText(Text("…", 14, Brushes.Gray), new Point(sx + tw / 2 - 4, sy + th / 2 - 9));
                if (images && tw > 90) dc.DrawText(Text($"{t.X},{t.Y}", 10, Brushes.White), new Point(sx + 3, sy + 2));
            }
            if (ShowGrid && tw >= 14)
            {
                // Règle : graduations de coordonnées en haut (x) et à gauche (y), espacées selon le zoom
                int every = tw >= 120 ? 1 : tw >= 60 ? 2 : tw >= 28 ? 5 : 10;
                var ink = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
                var bg = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
                dc.FillRectangle(bg, new Rect(0, 0, Bounds.Width, 16));
                dc.FillRectangle(bg, new Rect(0, 0, 34, Bounds.Height));
                for (int x = x0; x <= x1; x++)
                    if (x % every == 0) dc.DrawText(Text(x.ToString(CultureInfo.InvariantCulture), 10, ink), new Point(DofusDbWorldMap.ToScreen(x, 0, Zoom, OffX, OffY).X + 3, 1));
                for (int y = y0; y <= y1; y++)
                    if (y % every == 0) dc.DrawText(Text(y.ToString(CultureInfo.InvariantCulture), 10, ink), new Point(3, DofusDbWorldMap.ToScreen(0, y, Zoom, OffX, OffY).Y + 3));
            }
            // Ressources
            var node = new SolidColorBrush(Color.FromArgb(215, 255, 170, 0));
            foreach (var n in Nodes)
            {
                if (n.World != World || n.X < x0 - 1 || n.X > x1 + 1 || n.Y < y0 - 1 || n.Y > y1 + 1) continue;
                var (sx, sy) = DofusDbWorldMap.ToScreen(n.X, n.Y, Zoom, OffX, OffY);
                double rad = Math.Clamp(Math.Min(tw, th) * 0.22, 4, 22);
                dc.DrawEllipse(node, new Pen(Brushes.Black, 1.5), new Point(sx + tw / 2, sy + th / 2), rad, rad);
                if (rad >= 9) dc.DrawText(Text(n.Count.ToString(CultureInfo.InvariantCulture), Math.Min(rad * 1.1, 16), Brushes.Black), new Point(sx + tw / 2 - 4, sy + th / 2 - rad * 0.6));
            }
            if (ShowZaaps)
                foreach (var z in Zaaps)
                {
                    if (z.World != World || z.X < x0 - 1 || z.X > x1 + 1 || z.Y < y0 - 1 || z.Y > y1 + 1) continue;
                    var (sx, sy) = DofusDbWorldMap.ToScreen(z.X, z.Y, Zoom, OffX, OffY);
                    double rz = Math.Clamp(Math.Min(tw, th) * 0.18, 5, 16); var c = new Point(sx + tw - rz - 3, sy + rz + 3);
                    var diamond = new StreamGeometry();
                    using (var gc = diamond.Open()) { gc.BeginFigure(new Point(c.X, c.Y - rz), true); gc.LineTo(new Point(c.X + rz, c.Y)); gc.LineTo(new Point(c.X, c.Y + rz)); gc.LineTo(new Point(c.X - rz, c.Y)); gc.EndFigure(true); }
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(70, 160, 255)), new Pen(Brushes.White, 1.5), diamond);
                }
            // Bots
            foreach (var b in Bots)
            {
                if (b.X < x0 - 1 || b.X > x1 + 1 || b.Y < y0 - 1 || b.Y > y1 + 1) continue;
                var (sx, sy) = DofusDbWorldMap.ToScreen(b.X, b.Y, Zoom, OffX, OffY);
                var c = new Point(sx + tw / 2, sy + th / 2);
                dc.DrawEllipse(Brushes.Red, new Pen(Brushes.White, 2), c, 7, 7);
                dc.DrawText(Text(b.Name, 12, Brushes.White), new Point(c.X + 10, c.Y - 8));
            }
            if (Link is { } lk && Selected is { } sl)
            {
                var a = DofusDbWorldMap.ToScreen(lk.X, lk.Y, Zoom, OffX, OffY); var b = DofusDbWorldMap.ToScreen(sl.X, sl.Y, Zoom, OffX, OffY);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 255, 255)), 2.5, dashStyle: DashStyle.Dash), new Point(a.X + tw / 2, a.Y + th / 2), new Point(b.X + tw / 2, b.Y + th / 2));
            }
            if (Selected is { } s)
            {
                var (sx, sy) = DofusDbWorldMap.ToScreen(s.X, s.Y, Zoom, OffX, OffY);
                dc.DrawRectangle(null, new Pen(Brushes.Cyan, 3), new Rect(sx, sy, tw, th));
            }
        }
    }

    private readonly MapView _view = new();
    private readonly DofusDbWorldMap _map;
    private readonly Func<IEnumerable<WorldMapBot>> _bots;
    private readonly ComboBox _worldBox = new() { MinWidth = 110 };
    private readonly AutoCompleteBox _resource = new() { Watermark = "Ressource (ex : Frêne, Fer)…", MinWidth = 240, FilterMode = AutoCompleteFilterMode.Contains };
    private readonly TextBlock _info = new() { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
    private long _selectedMap;
    private readonly Func<int, long, string>? _send;

    public WorldMapWindow(DofusDbWorldMap map, Func<IEnumerable<WorldMapBot>> bots, Func<int, long, string>? send = null, IReadOnlyList<long>? zaapMaps = null)
    {
        _map = map; _bots = bots; _send = send;
        Title = "Carte du monde · DofusDB";
        Width = 1250; Height = 820;
        _view.Map = map;

        var worlds = map.Worlds.Where(w => map.TileCount(w) > 0).ToList();
        foreach (var w in worlds) _worldBox.Items.Add(WorldLabel(w));
        _worldBox.SelectedIndex = Math.Max(0, worlds.IndexOf(1));
        _worldBox.SelectionChanged += (_, _) =>
        {
            if (_worldBox.SelectedIndex < 0) return;
            _view.World = worlds[_worldBox.SelectedIndex];
            ApplyResource(); Refresh(); CenterWorld();
        };

        var names = ResourceMap.Default.Resources.Select(r => r.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        _resource.ItemsSource = names;
        _resource.TextChanged += (_, _) => ApplyResource();
        var clear = new Button { Content = "Effacer" };
        clear.Click += (_, _) => { _resource.Text = ""; };
        var botsBtn = new Button { Content = "Centrer sur un bot" };
        botsBtn.Click += (_, _) => CenterOnBot();
        var copy = new Button { Content = "Copier travelTo(carte)" };
        copy.Click += async (_, _) =>
        {
            if (_selectedMap == 0 || Clipboard is not { } cb) return;
            await cb.SetTextAsync($"travelTo({_selectedMap.ToString(CultureInfo.InvariantCulture)})");
            _info.Text += "   · copié";
        };
        var fit = new Button { Content = "Ajuster la vue" };
        fit.Click += (_, _) => { if (_map.Bounds(_view.World) is { } b) _view.Fit(b.MinX, b.MinY, b.MaxX, b.MaxY); };
        var ruler = new CheckBox { Content = "Règle", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        ruler.IsCheckedChanged += (_, _) => { _view.ShowGrid = ruler.IsChecked == true; _view.InvalidateVisual(); };
        var zaapBox = new CheckBox { Content = "Zaaps", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        zaapBox.IsCheckedChanged += (_, _) => { _view.ShowZaaps = zaapBox.IsChecked == true; _view.InvalidateVisual(); };
        _view.Zaaps = (zaapMaps ?? Array.Empty<long>()).Select(id => map.PositionOf(id)).Where(p => p is not null).Select(p => (p!.Value.X, p!.Value.Y, p.Value.World)).Distinct().ToList();
        var gox = new TextBox { Watermark = "x,y", Width = 80 };
        var go = new Button { Content = "Aller à" };
        go.Click += (_, _) =>
        {
            var parts = (gox.Text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int gx) && int.TryParse(parts[1], out int gy))
            { _view.Zoom = Math.Max(_view.Zoom, 0.5); _view.Selected = (gx, gy); _selectedMap = _map.MapAt(_view.World, gx, gy) ?? 0; _view.CenterOn(gx, gy); UpdateInfo(); }
        };
        var sendBtn = new Button { Content = "Envoyer le bot ici" };
        sendBtn.Click += (_, _) =>
        {
            var bot = _bots().FirstOrDefault();
            if (_send is null || bot is null || bot.Pid <= 0) { _info.Text = "Aucun bot connecté."; return; }
            if (_selectedMap == 0) { _info.Text = "Clique d'abord sur une carte."; return; }
            _info.Text = $"{bot.Name} : " + _send(bot.Pid, _selectedMap);
        };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        foreach (Control c in new Control[] { _worldBox, _resource, clear, botsBtn, fit, gox, go, ruler, zaapBox, sendBtn, copy }) top.Children.Add(c);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12) };
        Grid.SetRow(_view, 1); Grid.SetRow(_info, 2); _info.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(top); grid.Children.Add(_view); grid.Children.Add(_info);
        Content = grid;

        _view.Hover = (x, y) => { _hover = (x, y); UpdateInfo(); };
        _view.Picked = (x, y) => { _selectedMap = _map.MapAt(_view.World, x, y) ?? 0; UpdateInfo(); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); CenterOnBot(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
        UpdateInfo();
    }

    private (int X, int Y)? _hover;

    private static string WorldLabel(int w) => w switch { 1 => "Monde des Douze", 2 => "Incarnam", -1 => "Donjons / intérieurs", _ => "Calque " + w };

    private void ApplyResource()
    {
        var r = ResourceMap.Default.Resolve(_resource.Text);
        _view.Nodes = r is null ? Array.Empty<ResourceNode>() : ResourceMap.Default.NodesOf(r.ItemId);
        _view.InvalidateVisual();
        UpdateInfo();
    }

    private void Refresh()
    {
        _view.Bots = _bots().Select(b => (b.Name, p: _map.PositionOf(b.MapId))).Where(b => b.p is { } p && p.World == _view.World).Select(b => (b.Name, b.p!.Value.X, b.p!.Value.Y)).ToList();
        _view.Link = _view.Bots.Count > 0 ? (_view.Bots[0].X, _view.Bots[0].Y) : null;
        _view.InvalidateVisual();
    }

    private void CenterWorld()
    {
        if (_map.Bounds(_view.World) is { } b) _view.CenterOn((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
    }

    private void CenterOnBot()
    {
        if (_view.Bots.Count > 0) { var b = _view.Bots[0]; _view.Zoom = 0.6; _view.CenterOn(b.X, b.Y); }
        else CenterWorld();
    }

    private void UpdateInfo()
    {
        string hover = _hover is { } h ? $"[{h.X},{h.Y}]" + (_map.MapAt(_view.World, h.X, h.Y) is { } id ? $" carte {id}" : " (rien)") : "";
        string sel = _selectedMap != 0 ? $"   ·   sélection : carte {_selectedMap}" + (_map.PositionOf(_selectedMap) is { } p ? $" [{p.X},{p.Y}]" : "") : "";
        string dist = _view.Link is { } l && _view.Selected is { } t ? $"   ·   distance du bot : {Math.Abs(l.X - t.X) + Math.Abs(l.Y - t.Y)} carte(s)" : "";
        string res = ResourceMap.Default.Resolve(_resource.Text) is { } r ? $"   ·   {r.Name} : {_view.Nodes.Count(n => n.World == _view.World)} endroit(s) sur ce calque (pastilles orange = nombre de points)" : "";
        _info.Text = "Glisser / flèches = déplacer · molette / + − = zoom · clic = choisir.   " + hover + sel + dist + res;
    }
}
