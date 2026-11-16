using Avalonia.Controls;
using Avalonia.Input.Platform;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

/// <summary>Atelier éco : craft ou acheter, forgemagie, carnet. Lecture seule côté jeu.</summary>
public partial class EcoToolsPanel : UserControl
{
    public EcoToolsViewModel ViewModel { get; } = new();

    public EcoToolsPanel()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.CopyRequested += async text =>
        {
            try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(text); }
            catch { /* presse-papiers indisponible : sans effet */ }
        };
        AttachedToVisualTree += (_, _) => ViewModel.EnsureLoaded();
    }
}
