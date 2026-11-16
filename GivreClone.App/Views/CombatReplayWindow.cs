using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GivreClone.App.Services.Combat;

namespace GivreClone.App.Views;

/// <summary>Replay de combat : la liste des derniers combats de tous les bots (défaites en rouge) et, pour celui choisi, les constats, les tours et la chronologie des décisions de l'IA.</summary>
public sealed class CombatReplayWindow : Window
{
    private readonly ListBox _list = new();
    private readonly TextBox _text = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12 };
    private readonly CheckBox _onlyLost = new() { Content = "Défaites seulement" };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<FightJournal> _fights = Array.Empty<FightJournal>();

    public CombatReplayWindow()
    {
        Title = "Replay de combat";
        Width = 1180; Height = 760;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        var refresh = new Button { Content = "Actualiser" };
        var copy = new Button { Content = "Copier le texte" };
        var open = new Button { Content = "Ouvrir le dossier des journaux" };
        top.Children.Add(refresh); top.Children.Add(copy); top.Children.Add(open); top.Children.Add(_onlyLost);
        _list.Width = 380;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("380,*"), RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(14) };
        Grid.SetColumnSpan(top, 2);
        var left = _list; Grid.SetRow(left, 1);
        Grid.SetRow(_text, 1); Grid.SetColumn(_text, 1); _text.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetRow(_status, 2); Grid.SetColumnSpan(_status, 2); _status.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(top); grid.Children.Add(left); grid.Children.Add(_text); grid.Children.Add(_status);
        Content = grid;

        refresh.Click += (_, _) => Reload();
        _onlyLost.IsCheckedChanged += (_, _) => Reload();
        _list.SelectionChanged += (_, _) => ShowSelected();
        copy.Click += async (_, _) => { if (Clipboard is { } cb && _text.Text is { Length: > 0 } t) { await cb.SetTextAsync(t); _status.Text = "Texte copié."; } };
        open.Click += (_, _) =>
        {
            try { System.IO.Directory.CreateDirectory(CombatJournalService.Folder); Process.Start(new ProcessStartInfo(CombatJournalService.Folder) { UseShellExecute = true }); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or System.IO.IOException) { _status.Text = "Dossier introuvable : " + e.Message; }
        };
        Action<FightJournal> onFinished = _ => Dispatcher.UIThread.Post(Reload);
        CombatJournalService.FightFinished += onFinished;
        Closed += (_, _) => CombatJournalService.FightFinished -= onFinished;
        Reload();
    }

    private void Reload()
    {
        var keep = (_list.SelectedItem as ListBoxItem)?.Tag as string;
        _fights = CombatJournalService.List(null, 100).Where(f => _onlyLost.IsChecked != true || f.Lost).ToList();
        _list.Items.Clear();
        foreach (var f in _fights)
            _list.Items.Add(new ListBoxItem { Content = $"pid {f.Pid} · {f.OneLine}", Tag = f.Id, Foreground = f.Lost ? Brushes.IndianRed : f.Problems > 0 ? Brushes.Goldenrod : null });
        _status.Text = _fights.Count == 0
            ? "Aucun combat enregistré depuis le lancement : le replay se remplit pendant que les bots se battent (MITM ou Socket). Les journaux sont aussi écrits dans le dossier « combat »."
            : $"{_fights.Count} combat(s). Les journaux sont conservés 30 jours dans {CombatJournalService.Folder}.";
        var idx = keep is null ? 0 : Math.Max(0, _fights.ToList().FindIndex(f => f.Id == keep));
        if (_list.Items.Count > 0) _list.SelectedIndex = idx;
        else _text.Text = "";
    }

    private void ShowSelected()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: string id } && _fights.FirstOrDefault(f => f.Id == id) is { } f) _text.Text = CombatJournalFormat.ToText(f);
    }
}
