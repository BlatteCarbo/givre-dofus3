using Avalonia.Controls;
using Avalonia.Input;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class PlanningOverlays : UserControl
{
    public PlanningOverlays() => InitializeComponent();

    // Clic en dehors de la carte -> ferme la modale. Le fond n'est pas focusable : sans ceci, un champ d'heure en cours de
    // saisie (validé « à la perte du focus ») garderait le focus sous la modale invisible et ne serait jamais enregistré.
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
        if (DataContext is BotsViewModel vm) vm.CloseOfflinePlanningCommand.Execute(null);
    }
}
