using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GivreClone.App.Services;

namespace GivreClone.App.Controls;

/// <summary>Prix moyen unitaire confirmé par le serveur du bot, y compris après ouverture de la fiche.</summary>
public sealed class ItemAveragePrice : TextBlock
{
    public static readonly StyledProperty<long> GidProperty = AvaloniaProperty.Register<ItemAveragePrice, long>(nameof(Gid));
    public static readonly StyledProperty<int> PidProperty = AvaloniaProperty.Register<ItemAveragePrice, int>(nameof(Pid));
    public long Gid { get => GetValue(GidProperty); set => SetValue(GidProperty, value); }
    public int Pid { get => GetValue(PidProperty); set => SetValue(PidProperty, value); }
    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Stores.Launch.AveragePricesChanged += PricesChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Stores.Launch.AveragePricesChanged -= PricesChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && (change.Property == GidProperty || change.Property == PidProperty)) Refresh();
    }

    private void PricesChanged(uint _) => Dispatcher.UIThread.Post(() => { if (_attached) Refresh(); });

    private void Refresh()
    {
        long price = Pid > 0 && Gid is > 0 and <= int.MaxValue ? Stores.Launch.AveragePrice((uint)Pid, (int)Gid) : 0;
        Text = Loc.Fmt("L.ItemTip.Average", price > 0 ? price.ToString("N0") : "—");
    }
}
