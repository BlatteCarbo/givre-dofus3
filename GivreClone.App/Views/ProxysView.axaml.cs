using Avalonia.Controls;

namespace GivreClone.App.Views;

public partial class ProxysView : UserControl
{
    public ProxysView()
    {
        InitializeComponent();
        // Onglet affiché : liste des agents relue tout de suite (créés, renommés ou supprimés sur le portail entre-temps).
        AttachedToVisualTree += (_, _) => _ = Services.Relay.GivreRelay.Client.RequestExitsAsync();
    }
}
