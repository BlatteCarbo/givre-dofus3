using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GivreClone.App.Services;
using GivreClone.App.Services.ForgeAdvice;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class SmithMagicView : UserControl
{
    private const double CompactWorkshopWidth = 1040;
    private const double WideHistoryWidth = 176;
    private const double CompactHistoryWidth = 152;
    private const double InventoryWidth = 264;
    private bool? _compactLayout;
    private long _pickAnchor;
    private SmithMagicViewModel? _selectionModel;
    private bool _syncingRuneSelection;
    private bool _supportGidsValid = true;
    private bool _runeGidsValid = true;
    private ForgeOverlayController? _overlayController;

    public SmithMagicView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SyncAdaptiveFilters();
        AttachedToVisualTree += (_, _) => SyncAdaptiveFilters();
        AdaptiveSelectionPicker.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty && AdaptiveSelectionPicker.IsExpanded)
                UpdateAdaptiveSelectionNotes();
        };
    }

    private void SyncAdaptiveFilters()
    {
        _syncingRuneSelection = true;
        try
        {
            _selectionModel = DataContext as SmithMagicViewModel;
            ManualAdaptiveSupports.IsChecked = _selectionModel?.AdaptiveAllowedSupportGids is not null;
            ManualAdaptiveRunes.IsChecked = _selectionModel?.AdaptiveAllowedRuneGids is not null;
            AllowedSupportGidsBox.Text = FormatGids(_selectionModel?.AdaptiveAllowedSupportGids);
            AllowedRuneGidsBox.Text = FormatGids(_selectionModel?.AdaptiveAllowedRuneGids);
            _supportGidsValid = _runeGidsValid = true;
            UpdateAdaptiveSelectionNotes();
        }
        finally { _syncingRuneSelection = false; }
    }

    private static string FormatGids(IReadOnlySet<long>? gids)
        => gids is null ? "" : string.Join(", ", gids.Order().Select(gid => gid.ToString(CultureInfo.InvariantCulture)));

    private static (IReadOnlySet<long> Gids, bool Valid) ReadGids(string? text)
    {
        var gids = new HashSet<long>();
        foreach (var token in (text ?? "").Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var gid)
                || gid is <= 0 or > int.MaxValue)
                return (new HashSet<long>(), false);
            gids.Add(gid);
            if (gids.Count > 32) return (new HashSet<long>(), false);
        }
        return (gids, true);
    }

    private void OnForgeOverlayCalibrateClick(object? sender, RoutedEventArgs e)
    {
        if (_overlayController is null) return;
        _overlayController.Start();
        _overlayController.Calibrate();
    }

    private void OnForgeOverlayToggleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SmithMagicViewModel vm) return;
        if (ForgeOverlayToggle.IsChecked == true)
        {
            _overlayController ??= new ForgeOverlayController(vm);
            _overlayController.Start();
        }
        else _overlayController?.Stop();
    }

    private void OnAdaptiveFilterChanged(object? sender, RoutedEventArgs e)
    {
        if (_syncingRuneSelection || _selectionModel is null) return;
        var supports = ReadGids(AllowedSupportGidsBox.Text);
        var runes = ReadGids(AllowedRuneGidsBox.Text);
        _supportGidsValid = supports.Valid;
        _runeGidsValid = runes.Valid;
        // Invalid manual input fails closed, never falling back to unrestricted buying.
        _selectionModel.AdaptiveAllowedSupportGids = ManualAdaptiveSupports.IsChecked == true ? supports.Gids : null;
        _selectionModel.AdaptiveAllowedRuneGids = ManualAdaptiveRunes.IsChecked == true
            ? runes.Gids : null;
        UpdateAdaptiveSelectionNotes();
    }

    private void UpdateAdaptiveSelectionNotes()
    {
        AllowedSupportGidsBox.IsEnabled = ManualAdaptiveSupports.IsChecked == true;
        AllowedRuneGidsBox.IsEnabled = ManualAdaptiveRunes.IsChecked == true;
        AdaptiveSupportSelectionNote.Text = _selectionModel?.AdaptiveAllowedSupportGids is not { } supports
            ? "Achats de supports : choix automatique."
            : !_supportGidsValid ? "GID invalides : entiers positifs, 32 maximum. Aucun support ne sera acheté."
            : supports.Count == 0 ? "Aucun achat de support autorisé. Les supports du sac restent utilisables."
            : $"{supports.Count} type(s) de support autorisé(s) à l'achat.";
        AdaptiveRuneSelectionNote.Text = _selectionModel?.AdaptiveAllowedRuneGids is not { } allowed
            ? "Runes : choix automatique parmi les candidates compatibles."
            : !_runeGidsValid ? "GID invalides : entiers positifs, 32 maximum. Aucune rune autorisée."
            : allowed.Count == 0 ? "Aucune rune autorisée : saisis ses GID ou repasse en automatique."
            : $"{allowed.Count} rune(s) autorisée(s). Les runes non sélectionnées sont exclues, achats compris.";
        if (!AdaptiveSelectionPicker.IsExpanded) return;
        // Only explicit IDs are resolved, against already-loaded names; never enumerate or load a catalog.
        string Preview(IReadOnlySet<long>? gids) => gids is null ? "" : string.Join(Environment.NewLine,
            gids.Order().Select(gid => $"{gid} · " + (_selectionModel?.Picks.FirstOrDefault(row => row.Gid == gid)?.Name
                ?? Stores.Launch.GameNames.ItemName((int)gid) ?? "Nom local non chargé")));
        AdaptiveSupportPreview.Text = Preview(_selectionModel?.AdaptiveAllowedSupportGids);
        AdaptiveRunePreview.Text = Preview(_selectionModel?.AdaptiveAllowedRuneGids);
    }

    private void OnChooseAdaptiveSupports(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SmithMagicViewModel vm) return;
        vm.PickForgeCommand.Execute(null);
        WorkshopInventoryTabs.SelectedIndex = 0;
        if (CompactInventoryTab.IsVisible) CompactInventoryTab.IsSelected = true;
    }

    private void OnWorkshopSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactWorkshopWidth;
        if (_compactLayout == compact) return;

        var inventory = WideInventoryHost.Content ?? CompactInventoryHost.Content;
        WideInventoryHost.Content = null;
        CompactInventoryHost.Content = null;
        if (compact) CompactInventoryHost.Content = inventory;
        else WideInventoryHost.Content = inventory;

        if (!compact && CompactInventoryTab.IsSelected) WorkshopTabs.SelectedIndex = 0;
        CompactInventoryTab.IsVisible = compact;
        WideInventoryHost.IsVisible = !compact;
        WorkshopColumns.ColumnDefinitions[0].Width = new GridLength(compact ? CompactHistoryWidth : WideHistoryWidth);
        WorkshopColumns.ColumnDefinitions[2].Width = new GridLength(compact ? 0 : InventoryWidth);
        _compactLayout = compact;
    }

    private void OnBagSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not SmithMagicViewModel vm) return;
        vm.AddSearchHitCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPickPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not BagPickRow row || DataContext is not SmithMagicViewModel vm) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _pickAnchor > 0)
            vm.SelectPickRange(_pickAnchor, row.ObjectUid);
        else
        {
            _pickAnchor = row.ObjectUid;
            vm.TogglePick(row.ObjectUid);
        }
        e.Handled = true;
    }
}
