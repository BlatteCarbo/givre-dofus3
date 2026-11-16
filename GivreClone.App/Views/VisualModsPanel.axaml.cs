using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using GivreClone.App.Services.VisualMods;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

/// <summary>Standalone panel. Prime can mount new VisualModsPanel() without a MainViewModel binding.</summary>
public partial class VisualModsPanel : UserControl
{
    public VisualModsViewModel ViewModel { get; }
    public string StoragePath => ViewModel.StoragePath;

    public VisualModsPanel() : this(new VisualModsViewModel()) { }

    public VisualModsPanel(VisualModsViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.Positions.CollectionChanged += OnPositionsChanged;
        DetachedFromVisualTree += (_, _) => ViewModel.Positions.CollectionChanged -= OnPositionsChanged;
        AttachedToVisualTree += (_, _) =>
        {
            ViewModel.Positions.CollectionChanged -= OnPositionsChanged;
            ViewModel.Positions.CollectionChanged += OnPositionsChanged;
            RebuildMap();
        };
        RebuildMap();
    }

    private void OnPositionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildMap();

    private void RebuildMap()
    {
        PinsCanvas.Children.Clear();
        foreach (var pin in VisualMapProjection.Project(ViewModel.Positions))
        {
            var accent = Color.Parse(pin.IsBookmark ? "#F2C66D" : "#70D7BE");
            var point = new Ellipse
            {
                Width = 12, Height = 12, Fill = new SolidColorBrush(accent),
                Stroke = new SolidColorBrush(Color.Parse("#131A2A")), StrokeThickness = 1.5
            };
            var name = new TextBlock
            {
                Text = pin.Label.Length > 18 ? pin.Label[..17] + "…" : pin.Label,
                FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#F4F7FD")),
                MaxWidth = 100, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var marker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            marker.Children.Add(point);
            marker.Children.Add(name);
            Canvas.SetLeft(marker, pin.Left - 6);
            Canvas.SetTop(marker, pin.Top - 6);
            ToolTip.SetTip(marker, $"{pin.Label} ({pin.X}, {pin.Y}) · position manuelle");
            PinsCanvas.Children.Add(marker);
        }
    }
}
