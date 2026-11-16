using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using GivreClone.App.Services;

namespace GivreClone.App.Views;

public partial class SettingsView
{
    private void InitializeItemOverlaySettings()
    {
        if (NativeModHealth.Parent is not StackPanel panel) return;
        var enabled = new ToggleSwitch
        {
            Content = "Infobulles enrichies : identifiants et données locales",
            IsChecked = AppConfig.Current.ItemIdOverlay,
        };
        var formats = new ComboBox
        {
            ItemsSource = new[] { "#id", "[id]" },
            SelectedIndex = ItemOverlayPreferences.ReadFormat() == "brackets" ? 1 : 0,
            Width = 180,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        Avalonia.Automation.AutomationProperties.SetName(formats, "Format du suffixe");
        var status = new TextBlock
        {
            Text = "Appliqué au prochain chargement du module. Les données inconnues restent masquées.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        status.Classes.Add("caption");
        void Save()
        {
            bool requested = enabled.IsChecked == true;
            try
            {
                ItemOverlayPreferences.Save(requested, formats.SelectedIndex == 1 ? "brackets" : "hash");
                AppConfig.Current.ItemIdOverlay = requested;
                AppConfig.Save();
                status.Text = "Enregistré. Appliqué au prochain chargement du module.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status.Text = "Échec de l'enregistrement des préférences locales.";
            }
        }
        enabled.IsCheckedChanged += (_, _) => Save();
        formats.SelectionChanged += (_, _) => Save();
        panel.Children.Add(new Separator { Margin = new Thickness(0, 8) });
        panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock { Text = "Format du suffixe" });
        panel.Children.Add(formats);
        panel.Children.Add(status);
    }
}
