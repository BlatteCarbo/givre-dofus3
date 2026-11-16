using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Layout;
using GivreClone.App.Services;

namespace GivreClone.App.Views;

public sealed class LocalNotificationSettingsWindow : Window
{
    public LocalNotificationSettingsWindow(LocalNotificationPreferences store, string accountId, Func<string> deliveryStatus)
    {
        Title = "Notifications locales — compte sélectionné";
        Width = 480; Height = 620;
        var preference = store.Get(accountId);
        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 10 };
        var enabled = new CheckBox { Content = "Activer les notifications Windows", IsChecked = preference.Enabled };
        panel.Children.Add(enabled);
        var choices = new Dictionary<LocalBotEvent, CheckBox>();
        foreach (var item in new[] { (LocalBotEvent.PodsFull, "Pods pleins (100 %)"), (LocalBotEvent.Death, "Mort du personnage"),
            (LocalBotEvent.Disconnected, "Déconnexion"), (LocalBotEvent.ScriptEnded, "Script arrêté ou terminé"),
            (LocalBotEvent.FightWon, "Combat gagné"), (LocalBotEvent.FightLost, "Combat perdu"), (LocalBotEvent.KamasGained, "Kamas gagnés") })
        {
            var check = new CheckBox { Content = item.Item2, IsChecked = (preference.Events & item.Item1) != 0 };
            choices.Add(item.Item1, check); panel.Children.Add(check);
        }
        panel.Children.Add(new TextBlock { Text = "Seuil de gains cumulés de kamas" });
        var threshold = new NumericUpDown { Minimum = 1, Maximum = long.MaxValue, Value = preference.KamasThreshold };
        panel.Children.Add(threshold);
        panel.Children.Add(new TextBlock { Text = "Délai minimum par événement (secondes)" });
        var cooldown = new NumericUpDown { Minimum = 1, Maximum = 86400, Value = preference.CooldownSeconds };
        panel.Children.Add(cooldown);
        var status = new TextBlock { Text = deliveryStatus(), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Content = "Enregistrer pour ce compte", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            try
            {
                LocalBotEvent events = LocalBotEvent.None;
                foreach (var choice in choices) if (choice.Value.IsChecked == true) events |= choice.Key;
                store.Set(accountId, new(enabled.IsChecked == true, events, (long)(threshold.Value ?? 1000), (int)(cooldown.Value ?? 30)));
                status.Text = "Préférences enregistrées. " + deliveryStatus();
            }
            catch { status.Text = "Échec d'enregistrement des préférences."; }
        };
        panel.Children.Add(save); panel.Children.Add(status);
        var checkWindows = new Button { Content = "Vérifier Windows sans envoyer de toast" };
        checkWindows.Click += async (_, _) =>
        {
            checkWindows.IsEnabled = false;
            status.Text = "Vérification Windows en cours…";
            try
            {
                var result = await new WindowsLocalToastDelivery().CheckAvailabilityAsync(CancellationToken.None);
                status.Text = result.Status == LocalToastStatus.Available
                    ? "API Windows disponible (Setting=0). Aucun affichage testé."
                    : "Vérification Windows : " + result.Status + (result.PlatformSetting is { } setting ? " (Setting=" + setting + ")" : "");
            }
            catch { status.Text = "Échec de la vérification Windows."; }
            finally { checkWindows.IsEnabled = true; }
        };
        panel.Children.Add(checkWindows);
        panel.Children.Add(new TextBlock { Text = "Texte Windows sans nom de compte. Aucun service externe. L'affichage dépend des réglages de Windows.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Content = new ScrollViewer { Content = panel };
    }
}
