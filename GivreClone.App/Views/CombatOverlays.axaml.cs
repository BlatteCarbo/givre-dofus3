using Avalonia.Controls;
using Avalonia.Input;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class CombatOverlays : UserControl
{
    public CombatOverlays() => InitializeComponent();

    // Clic sur le fond (scrim) -> ferme le modal de config de sort.
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BotsViewModel vm) vm.CloseSpellConfigCommand.Execute(null);
    }

    // Clic sur le fond -> ferme « Défis acceptés ».
    private void OnChallengeScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BotsViewModel vm) vm.CloseChallengeListCommand.Execute(null);
    }

    // Clic sur le fond -> ferme « Importer d'un autre perso ».
    private void OnImportScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BotsViewModel vm) vm.CloseCombatImportCommand.Execute(null);
    }
}
