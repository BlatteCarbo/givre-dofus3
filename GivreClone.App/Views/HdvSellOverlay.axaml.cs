using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class HdvSellOverlay : UserControl
{
    public HdvSellOverlay() => InitializeComponent();

    // Clic sur le fond (scrim) ou sur Fermer / ✕ / Réduire : ferme la fenêtre (une mise en vente en cours CONTINUE : la carte de
    // l'onglet HDV montre la progression). Le focus est d'abord retiré pour qu'un champ en cours de saisie valide sa valeur.
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => Close();
    private void OnClosePressed(object? sender, RoutedEventArgs e) => Close();

    private void Close()
    {
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
        if (DataContext is BotsViewModel vm) vm.Hdv.CloseSellModalCommand.Execute(null);
    }
}
