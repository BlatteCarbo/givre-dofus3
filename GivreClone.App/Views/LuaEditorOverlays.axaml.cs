using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class LuaEditorOverlays : UserControl
{
    private BotsViewModel? _vm;

    public LuaEditorOverlays()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as BotsViewModel;
            if (_vm is not null) { _vm.PropertyChanged += OnVmChanged; WatchBot(_vm.SelectedBot); }
        };
    }

    // Focus automatique du champ utile à l'ouverture (aller à la ligne, recherche dans l'API) ; éditeur plein écran construit
    // à l'ouverture et retiré à la fermeture (le contrôle lâche alors le document partagé).
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        if (e.PropertyName == nameof(BotsViewModel.LuaFullscreenOpen))
        {
            if (_vm.LuaFullscreenOpen && FsEditorHost.Content is null)
            {
                var ed = new GivreClone.App.Controls.LuaEditor.LuaCodeEditor { IsFullscreen = true };
                ed.PropertyChanged += (_, a) => { if (a.Property == GivreClone.App.Controls.LuaEditor.LuaCodeEditor.CaretTextProperty) FsCaret.Text = ed.CaretText; };
                FsEditorHost.Content = ed;
                UpdateFsReadOnly();
                // 1re ouverture : le modèle du conteneur n'existe pas encore -> focus après la mise en page.
                Dispatcher.UIThread.Post(() => ed.Editor.TextArea.Focus(), DispatcherPriority.Loaded);
            }
            else if (!_vm.LuaFullscreenOpen) FsEditorHost.Content = null;
            return;
        }
        if (e.PropertyName == nameof(BotsViewModel.SelectedBot)) { WatchBot(_vm.SelectedBot); UpdateFsReadOnly(); }
        if (e.PropertyName == nameof(BotsViewModel.LuaGoToOpen) && _vm.LuaGoToOpen)
            Dispatcher.UIThread.Post(() => GoToBox.Focus(), DispatcherPriority.Input);
        else if (e.PropertyName == nameof(BotsViewModel.LuaApiRefOpen) && _vm.LuaApiRefOpen)
            Dispatcher.UIThread.Post(() => { ApiSearch.Focus(); ApiSearch.SelectAll(); }, DispatcherPriority.Input);
    }

    // Lecture seule du plein écran = verrou « suiveur » du bot sélectionné, suivi en direct (comme la liaison de l'onglet).
    private BotRowViewModel? _watchedBot;
    private void WatchBot(BotRowViewModel? bot)
    {
        if (_watchedBot is not null) _watchedBot.PropertyChanged -= OnBotChanged;
        _watchedBot = bot;
        if (bot is not null) bot.PropertyChanged += OnBotChanged;
    }
    private void OnBotChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(BotRowViewModel.FollowerLocked)) UpdateFsReadOnly(); }
    private void UpdateFsReadOnly()
    {
        if (FsEditorHost.Content is GivreClone.App.Controls.LuaEditor.LuaCodeEditor ed) ed.IsReadOnly = _vm?.SelectedBot?.FollowerLocked == true;
    }

    private void OnFullscreenKeyDown(object? sender, KeyEventArgs e)
    {
        // Échap non consommé par l'éditeur (complétion, recherche, survol) : on revient à l'onglet.
        if (e.Key == Key.Escape && !e.Handled && _vm is { LuaFullscreenOpen: true }) { _vm.LuaCloseFullscreenCommand.Execute(null); e.Handled = true; }
    }

    private void OnGoToKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Enter) { _vm.LuaGoToConfirmCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { _vm.LuaGoToCancelCommand.Execute(null); e.Handled = true; }
    }

    private void OnApiSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Enter) { _vm.LuaInsertApiCommand.Execute(_vm.SelectedLuaApiRow); e.Handled = true; }
        else if (e.Key == Key.Escape) { _vm.LuaCloseApiRefCommand.Execute(null); e.Handled = true; }
    }

    private void OnApiDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm?.SelectedLuaApiRow is { } row) _vm.LuaInsertApiCommand.Execute(row);
    }

    private void OnTemplateDoubleTapped(object? sender, TappedEventArgs e) => _vm?.LuaInsertTemplateCommand.Execute(null);

    private void OnConsoleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        var row = (e.Source as Avalonia.StyledElement)?.DataContext as LuaLogRow;
        if (row is { HasLocation: true }) _vm.LuaJumpToLogCommand.Execute(row);
    }

    private async void OnCopyApiExample(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedLuaApiRow is not { } row) return;
        var ex = row.Overloads[0].Example;
        if (string.IsNullOrWhiteSpace(ex)) return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(ex));
        await clip.SetDataAsync(data);
        Toasts.Show(Loc.T("L.Common.Copied"), success: true, sound: false);
    }

    private void OnScrimTemplates(object? sender, PointerPressedEventArgs e) => _vm?.LuaCloseTemplatesCommand.Execute(null);
    private void OnScrimRecent(object? sender, PointerPressedEventArgs e) => _vm?.LuaCloseRecentCommand.Execute(null);
    private void OnScrimApi(object? sender, PointerPressedEventArgs e) => _vm?.LuaCloseApiRefCommand.Execute(null);
    private void OnScrimGoTo(object? sender, PointerPressedEventArgs e) => _vm?.LuaGoToCancelCommand.Execute(null);
}
