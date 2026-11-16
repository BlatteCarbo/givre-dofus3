using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class CombatSimOverlay : UserControl
{
    public CombatSimOverlay() => InitializeComponent();

    // Clic sur le fond (scrim) ou sur Fermer/✕ -> ferme le simulateur (commande sur BotsViewModel = DataContext racine).
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => Close();
    private void OnClosePressed(object? sender, RoutedEventArgs e) => Close();

    private void Close()
    {
        if (DataContext is BotsViewModel vm) vm.CloseCombatSimCommand.Execute(null);
    }
}
