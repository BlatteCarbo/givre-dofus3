using System;
using System.Collections.Concurrent;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.Controls;

/// <summary>Fiche commune, reconstruite à chaque ouverture pour lire le dernier état de l'exemplaire.</summary>
public sealed class ItemTooltip : ContentControl
{
    private static readonly ConcurrentDictionary<string, Bitmap> Assets = new();
    public static readonly StyledProperty<long> GidProperty = AvaloniaProperty.Register<ItemTooltip, long>(nameof(Gid));
    public static readonly StyledProperty<int> UidProperty = AvaloniaProperty.Register<ItemTooltip, int>(nameof(Uid));
    public static readonly StyledProperty<int> PidProperty = AvaloniaProperty.Register<ItemTooltip, int>(nameof(Pid));
    public long Gid { get => GetValue(GidProperty); set => SetValue(GidProperty, value); }
    public int Uid { get => GetValue(UidProperty); set => SetValue(UidProperty, value); }
    public int Pid { get => GetValue(PidProperty); set => SetValue(PidProperty, value); }
    private bool _attached;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && (change.Property == GidProperty || change.Property == UidProperty || change.Property == PidProperty)) BuildCard();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (this.GetVisualAncestors().OfType<ToolTip>().FirstOrDefault() is { } tooltip)
        {
            tooltip.MaxWidth = 360;
            tooltip.Padding = new Thickness(0);
            tooltip.BorderThickness = new Thickness(0);
        }
        BuildCard();
    }

    private void BuildCard()
    {
        var launch = Stores.Launch;
        ItemCardInfo? info = null;
        try { info = launch.GameData?.ItemCard(Gid); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or FormatException or InvalidOperationException)
        { /* catalogue indisponible : les noms et effets vus dans la session restent utilisables */ }
        string name = !string.IsNullOrEmpty(info?.Name) ? info.Name : launch.ItemDisplayName(Gid);
        int level = info?.Level ?? launch.ItemLevel((int)Gid);
        string type = !string.IsNullOrEmpty(info?.Type) ? info.Type : launch.ItemCategory(Gid) ?? "";
        var body = new StackPanel { Spacing = 7, Width = 310 };
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), ColumnSpacing = 9 };
        title.Children.Add(new Image { Source = Stores.InteractiveIcons.GetByIcon(launch.ItemIconId(Gid)), Width = 38, Height = 38 });
        var heading = new StackPanel { Spacing = 3 };
        heading.Children.Add(Text(name, 15, FontWeight.Bold));
        heading.Children.Add(Text(string.Join(" · ", new[] { level >= 0 ? Loc.Fmt("L.Info.LevelN", level) : "", type }.Where(s => s.Length > 0)), 11));
        Grid.SetColumn(heading, 1); title.Children.Add(heading); body.Children.Add(title);
        if (!string.IsNullOrEmpty(info?.Set)) body.Children.Add(Text(info.Set, 12, FontWeight.SemiBold, "#C4A8EC"));
        body.Children.Add(Rule());

        uint pid = (uint)Math.Max(0, Pid);
        var effects = pid > 0 && Uid > 0 ? launch.ItemEffects(pid, Uid) : Array.Empty<GivreClone.Protocol.InventoryReader.ItemEffect>();
        int shown = 0;
        foreach (var fx in effects)
        {
            var def = launch.GameNames.Effect(fx.ActionId);
            if (def is { ShowOnItem: false } && !GivreClone.Engine.Recovery.ItemBindingRules.IsShownLink(fx.ActionId)) continue;
            string? obj = fx.ObjectGid > 0 ? launch.ItemDisplayName(fx.ObjectGid) : null;
            bool zero = fx.Value == 0 && fx.Min == 0 && fx.Max == 0 && fx.Const == 0 && fx.ObjectGid == 0 && string.IsNullOrEmpty(fx.Text);
            var parameters = ItemEffectText.Params(fx.Value, fx.Min, fx.Max, fx.Const, fx.Text, obj);
            if (zero) parameters[0] = "0";
            string line = ItemEffectText.Format(fx.ActionId, launch.GameNames.EffectTemplate(fx.ActionId), parameters);
            bool negative = ItemEffectText.IsNegative(def?.BonusType ?? 0, fx.Value, line);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*"), ColumnSpacing = 6 };
            row.Children.Add(EffectIcon(fx.ActionId));
            var text = Text(line, 12, FontWeight.Normal, zero ? "#8B929E" : negative ? "#F07881" : "#85D6A0");
            Grid.SetColumn(text, 1); row.Children.Add(text); body.Children.Add(row); shown++;
        }
        if (shown == 0) body.Children.Add(Text(Loc.T("L.ItemTip.UnknownEffects"), 11));
        body.Children.Add(Rule());
        int weight = info?.Weight ?? launch.GameNames.ItemWeight((int)Gid);
        body.Children.Add(Fact("pod", Loc.Fmt("L.ItemTip.Weight", weight >= 0 ? weight.ToString("N0") : "—")));
        var priceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        priceRow.Children.Add(Asset("kamas"));
        var average = new ItemAveragePrice { Pid = Pid, Gid = Gid, FontSize = 11 };
        average.Bind(TextBlock.ForegroundProperty, average.GetResourceObservable("TextSecondaryBrush"));
        priceRow.Children.Add(average);
        body.Children.Add(priceRow);
        var card = new Border { Child = body, Padding = new Thickness(12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Surface1Brush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("StrokeSoftBrush"));
        Content = card;
    }

    private static TextBlock Text(string text, double size, FontWeight? weight = null, string? color = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
        if (color is not null) block.Foreground = new SolidColorBrush(Color.Parse(color));
        else block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("TextSecondaryBrush"));
        return block;
    }

    private static Border Rule()
    {
        var rule = new Border { Height = 1, Margin = new Thickness(0, 2) };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("StrokeSoftBrush"));
        return rule;
    }

    private static Image Asset(string name) => new() { Source = Assets.GetOrAdd(name, key =>
    {
        using var stream = AssetLoader.Open(new Uri($"avares://GivreClone/Assets/stats/{key}.png"));
        return new Bitmap(stream);
    }), Width = 16, Height = 16 };
    private static Control EffectIcon(int action)
    {
        string? asset = action switch { 111 or 101 => "pa", 128 or 127 => "pm", 117 or 116 => "po", 125 or 153 => "pv", 174 or 175 => "initiative", 176 or 177 => "pp", 158 or 159 => "pod", _ => null };
        if (asset is not null) return Asset(asset);
        string name = action switch { 118 or 157 => "strength", 119 or 154 => "agility", 123 or 152 => "chance", 126 or 155 => "intelligence", 124 or 156 => "wisdom", _ => "characteristic" };
        if (Stores.UiIcons.Get("tx_" + name) is { } icon) return new Image { Source = icon, Width = 16, Height = 16 };
        string color = name switch { "strength" => "#B89062", "agility" => "#85CB93", "chance" => "#78B6ED", "intelligence" => "#F07881", "wisdom" => "#B19BE0", _ => "#A8B0BD" };
        return new PathIcon { Data = Geometry.Parse("M8,0L16,8L8,16L0,8Z"), Width = 12, Height = 12, Foreground = new SolidColorBrush(Color.Parse(color)) };
    }

    private static Control Fact(string icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(Asset(icon)); row.Children.Add(Text(text, 11)); return row;
    }
}
