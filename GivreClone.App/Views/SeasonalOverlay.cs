using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services;

namespace GivreClone.App.Views;

/// <summary>Décor de saison (Halloween : braises et chauves-souris ; Noël : neige) dessiné par-dessus l'interface. Ne prend jamais un clic
/// (IsHitTestVisible = false), se coupe dans Réglages > Effets, ne dessine rien hors saison et s'arrête quand la fenêtre est masquée.</summary>
public sealed class SeasonalOverlay : Control
{
    private struct P { public double X, Y, Vx, Vy, Size, Phase, Alpha; public int Kind; }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(66) };   // ~15 images/s : assez fluide pour de la neige, deux fois moins coûteux qu'à 45 ms

    /// <summary>Mode économe : fenêtre en arrière-plan -> plus de calcul ni de dessin (la chute reprend à la réactivation).</summary>
    public bool Paused
    {
        get => !_timer.IsEnabled;
        set { if (value) { _timer.Stop(); if (_ps.Length > 0) { _ps = Array.Empty<P>(); InvalidateVisual(); } } else if (VisualRoot is not null && !_timer.IsEnabled) _timer.Start(); }
    }
    private readonly Random _rnd = new();
    private P[] _ps = Array.Empty<P>();
    private Season _season;
    private DateTime _seasonCheckedAt = DateTime.MinValue;
    private double _t;

    public SeasonalOverlay()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Step();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private bool Active => AppConfig.Current.SeasonalDecor && _season != Season.None && Bounds.Width > 50 && Bounds.Height > 50 && IsEffectivelyVisible;

    private void Step()
    {
        if (DateTime.UtcNow - _seasonCheckedAt > TimeSpan.FromMinutes(5))
        {
            _seasonCheckedAt = DateTime.UtcNow;
            var s = SeasonalCalendar.Of(DateTime.Now);
            if (s != _season) { _season = s; _ps = Array.Empty<P>(); }
        }
        if (!Active) { if (_ps.Length > 0) { _ps = Array.Empty<P>(); InvalidateVisual(); } return; }
        double w = Bounds.Width, h = Bounds.Height;
        if (_ps.Length == 0) Spawn(w, h);
        _t += 0.045;
        for (int i = 0; i < _ps.Length; i++)
        {
            ref var p = ref _ps[i];
            p.Phase += 0.03 + p.Size * 0.002;
            if (p.Kind == 1)   // chauve-souris : traverse l'écran en ondulant
            {
                p.X += p.Vx; p.Y += Math.Sin(p.Phase * 0.8) * 0.9 + p.Vy;
                if (p.X > w + 60 || p.X < -60) { p.Vx = -p.Vx; p.X = p.Vx > 0 ? -50 : w + 50; p.Y = _rnd.NextDouble() * h * 0.5; }
            }
            else if (_season == Season.Christmas)   // neige : tombe en se balançant
            {
                p.Y += p.Vy; p.X += Math.Sin(p.Phase) * 0.5;
                if (p.Y > h + 10) { p.Y = -10; p.X = _rnd.NextDouble() * w; }
            }
            else   // braises : montent doucement en vacillant
            {
                p.Y -= p.Vy; p.X += Math.Sin(p.Phase) * 0.45;
                if (p.Y < -10) { p.Y = h + 10; p.X = _rnd.NextDouble() * w; }
            }
        }
        InvalidateVisual();
    }

    private void Spawn(double w, double h)
    {
        int embers = 26, bats = _season == Season.Halloween ? 4 : 0;
        _ps = new P[embers + bats];
        for (int i = 0; i < _ps.Length; i++)
        {
            bool bat = i >= embers;
            _ps[i] = new P
            {
                X = _rnd.NextDouble() * w, Y = bat ? _rnd.NextDouble() * h * 0.5 : _rnd.NextDouble() * h,
                Vx = bat ? (_rnd.Next(2) == 0 ? 1 : -1) * (1.2 + _rnd.NextDouble() * 1.4) : 0,
                Vy = bat ? 0 : 0.35 + _rnd.NextDouble() * 0.9, Size = bat ? 22 + _rnd.NextDouble() * 14 : 1.5 + _rnd.NextDouble() * 3.2,
                Phase = _rnd.NextDouble() * 6.28, Alpha = 0.25 + _rnd.NextDouble() * 0.5, Kind = bat ? 1 : 0,
            };
        }
    }

    public override void Render(DrawingContext ctx)
    {
        if (_ps.Length == 0) return;
        foreach (var p in _ps)
        {
            if (p.Kind == 1) { DrawBat(ctx, p); continue; }
            byte a = (byte)(255 * p.Alpha * (0.6 + 0.4 * Math.Sin(p.Phase * 2)));
            var color = _season == Season.Christmas ? Color.FromArgb(a, 235, 244, 255) : Color.FromArgb(a, 255, (byte)(140 + (p.Size * 18) % 90), 40);
            ctx.DrawEllipse(new SolidColorBrush(color), null, new Point(p.X, p.Y), p.Size, p.Size);
        }
    }

    private static void DrawBat(DrawingContext ctx, P p)
    {
        double flap = Math.Sin(p.Phase * 4) * 0.55 + 0.45;   // 0..1 : ailes en haut / en bas
        double s = p.Size, dir = p.Vx >= 0 ? 1 : -1;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double wy = -s * 0.5 * flap + s * 0.25 * (1 - flap);
            c.BeginFigure(new Point(0, 0), true);
            c.LineTo(new Point(dir * s * 0.45, wy));
            c.LineTo(new Point(dir * s * 0.9, wy + s * 0.35));
            c.LineTo(new Point(dir * s * 0.6, wy + s * 0.55));
            c.LineTo(new Point(dir * s * 0.35, wy + s * 0.42));
            c.LineTo(new Point(dir * s * 0.12, s * 0.28));
            c.LineTo(new Point(0, s * 0.35));
            c.LineTo(new Point(-dir * s * 0.12, s * 0.28));
            c.LineTo(new Point(-dir * s * 0.35, wy + s * 0.42));
            c.LineTo(new Point(-dir * s * 0.6, wy + s * 0.55));
            c.LineTo(new Point(-dir * s * 0.9, wy + s * 0.35));
            c.LineTo(new Point(-dir * s * 0.45, wy));
            c.EndFigure(true);
        }
        using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y)))
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(235, 40, 26, 66)), new Pen(new SolidColorBrush(Color.FromArgb(220, 176, 124, 240)), 1.1), g);
    }
}
