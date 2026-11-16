using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using GivreClone.App.Views;

var checks = 0;
void Check(bool condition, string explanation)
{
    if (!condition) throw new InvalidOperationException(explanation);
    checks++;
}

AppBuilder.Configure<OfflineApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var panel = new MZP4OfflinePacketPanel();
Check(!panel.HasInput && panel.ResultText.Contains("INCONNUS"), "Initial status must not imply a capture");
var layout = (StackPanel)((Border)panel.Content!).Child!;
var controls = layout.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
Check(controls.Length == 2, "Local inspect and clear controls missing");
Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "BotsView.axaml"))
    .Contains("<views:MZP4OfflinePacketPanel />"), "Real Bots / Paquets view is not wired");
panel.SetInputForTest("08 2A");
controls[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(panel.ResultText.Contains("#1 Varint (1 o)") &&
      panel.ResultText.Contains("Valeurs masquées"), "Real codec was not used to inspect neutral bytes");
Check(!panel.ResultText.Contains("42"), "Decoded scalar value leaked");
panel.SetInputForTest("08");
controls[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(panel.ResultText.Contains("rejetée") && !panel.ResultText.Contains("#1 Varint"),
    "Truncated protobuf incorrectly accepted");
panel.SetInputForTest("ZZ");
controls[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(panel.ResultText.Contains("invalide"), "Invalid hex accepted");
panel.SetInputForTest("08 2A");
controls[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(!panel.HasInput && panel.ResultText.Contains("effacée"), "Clear input button ineffective");

Console.WriteLine($"MZP4 real Bots/Paquets panel: PASS ({checks} Headless checks). No network or bot state accessed.");
sealed class OfflineApp : Application { }
