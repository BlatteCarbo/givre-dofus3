using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using GivreClone.Protocol;

namespace GivreClone.App.Controls;

/// <summary>
/// Grille de combat ISOMÉTRIQUE (560 cellules Dofus) dessinée à la main. Rend le damier + les jetons (moi / ennemis
/// avec barre de PV, mort, cible courante) + les surcharges (chemin d'un déplacement, faisceau + chiffre de dégâts d'un
/// lancer). ANIME en interne : mon jeton GLISSE d'une case à l'autre quand <see cref="MyCell"/> change, et le chiffre
/// de dégât MONTE + s'estompe. Géométrie des cellules = <see cref="MapPathfinder.MapPointOf"/> projeté en iso
/// (<c>sx=(x-y)·HW</c>, <c>sy=(x+y)·HH</c>). Un clic renvoie la cellule la plus proche via <see cref="CellCommandProperty"/>.
/// </summary>
public sealed class IsoGrid : Control
{
    /// <summary>Un jeton ennemi à rendre : case + fraction de PV (0-1) + vivant + est-ce la cible courante.</summary>
    public readonly record struct EnemyToken(int Cell, double HpFrac, bool Alive, bool IsTarget);

    // px = mx-my, py = mx+my par cellule + bornes (précalculés : MapPointOf est déterministe).
    private static readonly int[] _px = new int[MapPathfinder.CellCount];
    private static readonly int[] _py = new int[MapPathfinder.CellCount];
    private static readonly int _minPx, _minPy, _rangePx, _rangePy;

    static IsoGrid()
    {
        int minPx = int.MaxValue, minPy = int.MaxValue, maxPx = int.MinValue, maxPy = int.MinValue;
        for (int c = 0; c < MapPathfinder.CellCount; c++)
        {
            var (mx, my) = MapPathfinder.MapPointOf(c);
            int px = mx - my, py = mx + my;
            _px[c] = px; _py[c] = py;
            if (px < minPx) minPx = px; if (px > maxPx) maxPx = px;
            if (py < minPy) minPy = py; if (py > maxPy) maxPy = py;
        }
        _minPx = minPx; _minPy = minPy; _rangePx = maxPx - minPx; _rangePy = maxPy - minPy;

        // MyCell/DamageSeq NE sont PAS dans AffectsRender : on les anime (glisse/monte) via OnPropertyChanged + timer.
        AffectsRender<IsoGrid>(EnemyTokensProperty, MoveCellsProperty, BeamTargetProperty,
            DamageCellProperty, DamageElementProperty, SelfIconProperty, EnemyIconProperty);
    }

    public static readonly StyledProperty<int> MyCellProperty = AvaloniaProperty.Register<IsoGrid, int>(nameof(MyCell), -1);
    public static readonly StyledProperty<IReadOnlyList<EnemyToken>?> EnemyTokensProperty = AvaloniaProperty.Register<IsoGrid, IReadOnlyList<EnemyToken>?>(nameof(EnemyTokens));
    public static readonly StyledProperty<IReadOnlyList<int>?> MoveCellsProperty = AvaloniaProperty.Register<IsoGrid, IReadOnlyList<int>?>(nameof(MoveCells));
    public static readonly StyledProperty<int> BeamTargetProperty = AvaloniaProperty.Register<IsoGrid, int>(nameof(BeamTarget), -1);
    public static readonly StyledProperty<int> DamageCellProperty = AvaloniaProperty.Register<IsoGrid, int>(nameof(DamageCell), -1);
    public static readonly StyledProperty<int> DamageAmountProperty = AvaloniaProperty.Register<IsoGrid, int>(nameof(DamageAmount), 0);
    public static readonly StyledProperty<int> DamageElementProperty = AvaloniaProperty.Register<IsoGrid, int>(nameof(DamageElement), -1);
    public static readonly StyledProperty<int> DamageSeqProperty = AvaloniaProperty.Register<IsoGrid, int>(nameof(DamageSeq), 0);
    public static readonly StyledProperty<Geometry?> SelfIconProperty = AvaloniaProperty.Register<IsoGrid, Geometry?>(nameof(SelfIcon));
    public static readonly StyledProperty<Geometry?> EnemyIconProperty = AvaloniaProperty.Register<IsoGrid, Geometry?>(nameof(EnemyIcon));
    public static readonly StyledProperty<ICommand?> CellCommandProperty = AvaloniaProperty.Register<IsoGrid, ICommand?>(nameof(CellCommand));

    public int MyCell { get => GetValue(MyCellProperty); set => SetValue(MyCellProperty, value); }
    public IReadOnlyList<EnemyToken>? EnemyTokens { get => GetValue(EnemyTokensProperty); set => SetValue(EnemyTokensProperty, value); }
    public IReadOnlyList<int>? MoveCells { get => GetValue(MoveCellsProperty); set => SetValue(MoveCellsProperty, value); }
    public int BeamTarget { get => GetValue(BeamTargetProperty); set => SetValue(BeamTargetProperty, value); }
    public int DamageCell { get => GetValue(DamageCellProperty); set => SetValue(DamageCellProperty, value); }
    public int DamageAmount { get => GetValue(DamageAmountProperty); set => SetValue(DamageAmountProperty, value); }
    public int DamageElement { get => GetValue(DamageElementProperty); set => SetValue(DamageElementProperty, value); }
    public int DamageSeq { get => GetValue(DamageSeqProperty); set => SetValue(DamageSeqProperty, value); }
    public Geometry? SelfIcon { get => GetValue(SelfIconProperty); set => SetValue(SelfIconProperty, value); }
    public Geometry? EnemyIcon { get => GetValue(EnemyIconProperty); set => SetValue(EnemyIconProperty, value); }
    public ICommand? CellCommand { get => GetValue(CellCommandProperty); set => SetValue(CellCommandProperty, value); }

    // Couleurs (contrôle dessiné à la main -> constantes calées sur la DA sombre de l'app).
    private static readonly Color CellA = Color.Parse("#171B24");
    private static readonly Color CellB = Color.Parse("#1E232E");
    private static readonly Color Grid = Color.Parse("#2A3040");
    private static readonly Color MovePath = Color.Parse("#5C3BD17E");
    private static readonly Color Accent = Color.Parse("#4C8DFF");
    private static readonly Color Enemy = Color.Parse("#F0533B");
    private static readonly Color Dead = Color.Parse("#474C57");
    private static readonly Color Beam = Color.Parse("#FFC24B");

    private double _hw, _hh, _ox, _oy;   // demi-largeur / demi-hauteur d'une cellule + offset de centrage (dernier rendu)

    // Animation : glisse de mon jeton (_slideT 0->1 de _slideFrom vers _slideTo) + montée/fondu du chiffre de dégât (_dmgT).
    private int _slideFrom = -1, _slideTo = -1;
    private double _slideT = 1, _dmgT = 1;
    private DispatcherTimer? _anim;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MyCellProperty)
        {
            int oldC = change.GetOldValue<int>(), newC = change.GetNewValue<int>();
            if (oldC >= 0 && newC >= 0 && oldC != newC) { _slideFrom = oldC; _slideTo = newC; _slideT = 0; EnsureAnim(); }
            else { _slideT = 1; InvalidateVisual(); }
        }
        else if (change.Property == DamageSeqProperty)
        {
            if (change.GetNewValue<int>() > 0 && DamageAmount > 0) { _dmgT = 0; EnsureAnim(); }
        }
    }

    private void EnsureAnim()
    {
        _anim ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnAnimTick);
        if (!_anim.IsEnabled) _anim.Start();
        InvalidateVisual();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        bool active = false;
        if (_slideT < 1) { _slideT = Math.Min(1, _slideT + 0.065); active = true; }
        if (_dmgT < 1) { _dmgT = Math.Min(1, _dmgT + 0.04); active = true; }
        InvalidateVisual();
        if (!active) _anim?.Stop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _anim?.Stop();
    }

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    private (double x, double y) Center(int cell)
        => (_ox + (_px[cell] - _minPx) * _hw, _oy + (_py[cell] - _minPy) * _hh);
    private double TokenRadius() => Math.Max(Math.Min(_hw * 0.62, _hh * 1.15), 8);

    // Position ANIMÉE de mon jeton (interpolée pendant une glisse).
    private (double x, double y) MyPos()
    {
        if (_slideT >= 1 || _slideFrom < 0 || _slideTo < 0) return Center((uint)MyCell < MapPathfinder.CellCount ? MyCell : _slideTo);
        var a = Center(_slideFrom); var b = Center(_slideTo); double t = EaseOut(_slideT);
        return (a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 8 || h < 8) return;

        // Ajuste la taille des cellules pour tenir dans les bornes (ratio iso 2:1), puis centre.
        const double pad = 6;
        double hw = (w - 2 * pad) / (_rangePx + 2);
        double hh = Math.Min(hw / 2, (h - 2 * pad) / (_rangePy + 2));
        hw = Math.Min(hw, hh * 2);
        _hw = hw; _hh = hh;
        double gridW = (_rangePx + 2) * hw, gridH = (_rangePy + 2) * hh;
        _ox = (w - gridW) / 2 + hw; _oy = (h - gridH) / 2 + hh;

        var gridPen = new Pen(new SolidColorBrush(Grid), 1);
        var moveBrush = new SolidColorBrush(MovePath);
        var moveSet = MoveCells is { Count: > 0 } ? new HashSet<int>(MoveCells) : null;

        // 1) damier
        for (int c = 0; c < MapPathfinder.CellCount; c++)
        {
            var (cx, cy) = Center(c);
            var geo = Diamond(cx, cy, hw, hh);
            bool onPath = moveSet != null && moveSet.Contains(c);
            var fill = onPath ? moveBrush : new SolidColorBrush(((_px[c] + _py[c]) & 1) == 0 ? CellA : CellB);
            ctx.DrawGeometry(fill, gridPen, geo);
        }

        // 2) faisceau du lancer (depuis ma position -> cible)
        if (BeamTarget >= 0 && BeamTarget < MapPathfinder.CellCount && (uint)MyCell < MapPathfinder.CellCount)
        {
            var (ax, ay) = MyPos();
            var (bx, by) = Center(BeamTarget);
            ctx.DrawLine(new Pen(new SolidColorBrush(Beam), 3), new Point(ax, ay), new Point(bx, by));
        }

        // 3) jetons ennemis (morts dessinés d'abord, donc derrière les vivants).
        if (EnemyTokens is { } toks)
            foreach (var t in toks.OrderBy(t => t.Alive))
            { if ((uint)t.Cell < MapPathfinder.CellCount) { var (cx, cy) = Center(t.Cell); DrawActor(ctx, cx, cy, isEnemy: true, t.HpFrac, t.Alive, t.IsTarget); } }

        // 4) mon jeton (position animée)
        if ((uint)MyCell < MapPathfinder.CellCount) { var (cx, cy) = MyPos(); DrawActor(ctx, cx, cy, isEnemy: false, 1.0, true, false); }

        // 5) chiffre de dégâts (monte + s'estompe)
        DrawDamage(ctx);
    }

    private void DrawActor(DrawingContext ctx, double cx, double cy, bool isEnemy, double hpFrac, bool alive, bool isTarget)
    {
        double r = TokenRadius();
        // ombre portée
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, new Point(cx, cy + r * 0.35), r * 0.95, r * 0.5);
        // halo cible courante
        if (isTarget && alive)
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Beam), 2.5), new Point(cx, cy), r + 4, r + 4);
        // disque
        var fill = !alive ? Dead : (isEnemy ? Enemy : Accent);
        ctx.DrawEllipse(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Colors.White, 0.85), 1.5), new Point(cx, cy), r, r);

        if (!alive)
        {
            double k = r * 0.5;
            var xpen = new Pen(new SolidColorBrush(Enemy), 2.5);
            ctx.DrawLine(xpen, new Point(cx - k, cy - k), new Point(cx + k, cy + k));
            ctx.DrawLine(xpen, new Point(cx + k, cy - k), new Point(cx - k, cy + k));
            return;
        }

        // icône vectorielle (viewBox 24 des Icons.axaml) scalée + centrée ; repli lettre si absente.
        var icon = isEnemy ? EnemyIcon : SelfIcon;
        if (icon != null)
        {
            double s = r * 1.15 / 24.0;
            using (ctx.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(cx, cy)))
                ctx.DrawGeometry(Brushes.White, null, icon);
        }
        else
        {
            var ft = new FormattedText(isEnemy ? "E" : "M", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, r * 1.1, new SolidColorBrush(Colors.White));
            ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
        }

        // barre de PV (ennemis vivants uniquement)
        if (isEnemy)
        {
            double barW = r * 1.9, barH = 4.5, bx = cx - barW / 2, by = cy - r - 8;
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), null, new Rect(bx, by, barW, barH), 2, 2);
            double f = Math.Clamp(hpFrac, 0, 1);
            if (f > 0.001)
                ctx.DrawRectangle(new SolidColorBrush(HpColor(f)), null, new Rect(bx, by, barW * f, barH), 2, 2);
        }
    }

    private void DrawDamage(DrawingContext ctx)
    {
        if (DamageCell < 0 || DamageCell >= MapPathfinder.CellCount || DamageAmount <= 0 || _dmgT >= 1) return;
        var (cx, cy) = Center(DamageCell);
        double r = TokenRadius();
        double rise = 20 + 20 * EaseOut(_dmgT);                        // monte
        double op = _dmgT < 0.55 ? 1 : Math.Clamp(1 - (_dmgT - 0.55) / 0.45, 0, 1);   // s'estompe sur la fin
        double size = Math.Max(_hh * 1.6, 15);
        var face = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
        string txt = "-" + DamageAmount;
        var outline = new FormattedText(txt, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(Colors.Black));
        var body = new FormattedText(txt, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(ElementColor(DamageElement)));
        double tx = cx - body.Width / 2, ty = cy - r - rise;
        using (ctx.PushOpacity(op))
        {
            ctx.DrawText(outline, new Point(tx + 1.3, ty + 1.3));
            ctx.DrawText(body, new Point(tx, ty));
        }
    }

    private static Color HpColor(double f)
        => f > 0.5 ? Color.Parse("#54C878") : f > 0.25 ? Color.Parse("#E6B24C") : Color.Parse("#E5533B");

    private static Color ElementColor(int e) => e switch
    {
        2 => Color.Parse("#FF8A4C"), 3 => Color.Parse("#49B6FF"), 4 => Color.Parse("#8CD152"),
        5 => Color.Parse("#D0954A"), _ => Color.Parse("#E3E7EF"),
    };

    private static StreamGeometry Diamond(double cx, double cy, double hw, double hh)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(cx, cy - hh), true);
        ctx.LineTo(new Point(cx + hw, cy));
        ctx.LineTo(new Point(cx, cy + hh));
        ctx.LineTo(new Point(cx - hw, cy));
        ctx.EndFigure(true);
        return g;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (CellCommand is null || _hw <= 0) return;
        var p = e.GetPosition(this);
        int best = -1; double bestD = double.MaxValue;
        for (int c = 0; c < MapPathfinder.CellCount; c++)
        {
            var (cx, cy) = Center(c);
            double d = (cx - p.X) * (cx - p.X) + (cy - p.Y) * (cy - p.Y);
            if (d < bestD) { bestD = d; best = c; }
        }
        if (best >= 0 && CellCommand.CanExecute(best)) CellCommand.Execute(best);
    }
}
