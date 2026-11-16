using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class TeamsView : UserControl
{
    public TeamsView() => InitializeComponent();

    // Sélection dans le ComboBox « Ajouter un membre » d'une carte : DataContext du ComboBox = l'équipe, item = le bot.
    // ⚠️ On NE mute PAS la source (UnassignedBots) en plein SelectionChanged : affecter le bot vide/reconstruit
    // UnassignedBots (l'ItemsSource même du ComboBox) et Avalonia plante si l'item sélectionné en disparaît (surtout
    // s'il était le dernier -> liste vide). On relâche la sélection PUIS on diffère l'affectation au tick UI suivant.
    private void OnTeamAddMember(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: Team team } cb || cb.SelectedItem is not BotRowViewModel bot) return;
        cb.SelectedItem = null;   // rend le placeholder + termine proprement l'opération de sélection courante
        if (DataContext is TeamsViewModel vm)
            Dispatcher.UIThread.Post(() => vm.Bots.AssignBot(team, bot));
    }

    // Copie la console Équipe dans le presse-papiers (accès via le TopLevel de la vue).
    private async void OnCopyTeamConsole(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null || DataContext is not TeamsViewModel vm) return;
        var text = vm.Bots.TeamConsoleText();
        if (text.Length == 0) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        try { await clip.SetDataAsync(data); } catch { }
    }
}
