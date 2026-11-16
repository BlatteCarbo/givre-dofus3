using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class AccountsView : UserControl
{
    public AccountsView()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is not Control field) return;
            foreach (var scroll in field.GetVisualAncestors().OfType<ScrollViewer>().Where(scroll =>
                scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled
                && scroll.Extent.Width > scroll.Viewport.Width))
            {
                if (field.TranslatePoint(default, scroll) is not { } point) continue;
                double delta = point.X < 0 ? point.X
                    : Math.Max(0, point.X + field.Bounds.Width - scroll.Viewport.Width);
                scroll.Offset = new Vector(scroll.Offset.X + delta, scroll.Offset.Y);
                scroll.UpdateLayout();
            }
        }, RoutingStrategies.Bubble);
    }

    // Le toast ne répète pas l'identifiant : il resterait lisible en mode Stream.
    private async void OnCopyLogin(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string login } || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(login);
        Toasts.Show("Identifiant copié.", success: true, sound: false);
    }

    // Les lignes de compte n'affichent pas de sélection : le bouton propose directement les comptes.
    private void OnCreateCharacterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountsViewModel vm || sender is not Control anchor || vm.Accounts.Count == 0) return;
        var menu = new MenuFlyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
        foreach (var row in vm.Accounts)
        {
            var item = new MenuItem { Header = row.Running ? row.Login + " (en cours)" : row.Login, IsEnabled = !row.Running };
            var target = row;
            item.Click += async (_, _) => await vm.StartCharacterCreationAsync(target);
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }
}
