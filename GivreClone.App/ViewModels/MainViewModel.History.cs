using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.UiNavigation;

namespace GivreClone.App.ViewModels;

public sealed record ShortcutHelpRow(string Gesture, string Description);

/// <summary>Historique de navigation borné (Alt+← / Retour) et aide des raccourcis (F1).</summary>
public partial class MainViewModel
{
    private readonly NavigationHistory _history = new();
    private bool _navigatingBack;
    private int _currentBotTab = -1;   // onglet Bots affiché (clé « bots/<index> » du catalogue)
    private int _backTargetTab = -1;   // onglet visé par un retour, appliqué plus tard par la vue : ne pas l'empiler

    public bool CanNavigateBack => _history.Count > 0;

    /// <summary>Clé de l'écran courant : page, ou onglet précis sur la page Bots.</summary>
    private string CurrentHistoryKey => ActivePage == "bots" && _currentBotTab >= 0
        ? QuickNavigationCatalog.BotTabPrefix + _currentBotTab : ActivePage;

    /// <summary>Appelé par NavigateTo avant le changement de page (un retour n'empile rien).</summary>
    private void RecordNavigation(string toKey)
    {
        if (_navigatingBack) return;
        _history.Record(CurrentHistoryKey, toKey);
        RaiseBack();
    }

    /// <summary>Onglet de la page Bots changé (BotsView) : un changement d'onglet visible est une étape d'historique.</summary>
    public void RecordBotTab(int tab)
    {
        if (tab < 0) return;
        if (tab == _backTargetTab) _backTargetTab = -1;
        else if (!_navigatingBack && ActivePage == "bots" && _currentBotTab >= 0 && _currentBotTab != tab)
        {
            _history.Record(QuickNavigationCatalog.BotTabPrefix + _currentBotTab, QuickNavigationCatalog.BotTabPrefix + tab);
            RaiseBack();
        }
        _currentBotTab = tab;
    }

    /// <summary>Revient à la page / l'onglet affiché précédemment (Alt+← ou bouton Retour).</summary>
    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack()
    {
        if (_history.TryPop(CurrentHistoryKey, out var key))
        {
            _backTargetTab = QuickNavigationCatalog.BotTabIndex(key) ?? -1;
            _navigatingBack = true;
            try { ExecuteQuickNavigation(key); }
            finally { _navigatingBack = false; }
        }
        RaiseBack();
    }

    private void RaiseBack()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        NavigateBackCommand.NotifyCanExecuteChanged();
    }

    // ---- Aide des raccourcis (F1) ----
    [ObservableProperty] public partial bool ShortcutsHelpVisible { get; set; }
    /// <summary>Lignes de l'aide F1, textes résolus dans la langue courante à chaque ouverture.</summary>
    public IReadOnlyList<ShortcutHelpRow> Shortcuts => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
        KeyboardShortcutCatalog.All, s => new ShortcutHelpRow(s.Gesture, Services.Loc.T(s.DescriptionKey))));
    partial void OnShortcutsHelpVisibleChanged(bool value) { if (value) OnPropertyChanged(nameof(Shortcuts)); }

    [RelayCommand]
    private void ToggleShortcutsHelp()
    {
        QuickNavVisible = false;
        AdvancedToolsVisible = false;
        ShortcutsHelpVisible = !ShortcutsHelpVisible;
    }

    [RelayCommand] private void CloseShortcutsHelp() => ShortcutsHelpVisible = false;
}
