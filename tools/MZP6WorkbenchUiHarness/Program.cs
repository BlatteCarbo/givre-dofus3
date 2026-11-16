using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using MZP3ReplicaWorkbench;
using GivreClone.App.Views;

var checks = 0;
void Check(bool result, string reason)
{
    if (!result) throw new InvalidOperationException(reason);
    checks++;
}

AppBuilder.Configure<ReplicaApplication>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
Check(Application.Current!.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark,
    "Real Workbench must have dark theme");

var window = new ReplicaWorkbenchWindow();
Check(window.Title!.Contains("TEST hors ligne"), "Workbench falsely presents gameplay readiness");
Check(window.MinWidth >= 800 && window.MinHeight >= 550, "Workspace minimum dimensions collapsed");
var root = (Grid)window.Content!;
Check(root.Children.Count == 2, "Unexpected root layout");
var rail = (DockPanel)root.Children[0];
var nav = (StackPanel)rail.Children.OfType<ScrollViewer>().Single().Content!;
var buttons = nav.Children.OfType<Button>().ToArray();
Check(buttons.Length >= 15, "Original application navigation was removed");
Check(buttons.Any(b => b.Content as string == "Sécurité TEST"),
    "New production test-security route not reachable");

var main = (Border)root.Children[1];
Check(main.Child is ScrollViewer, "Default Accounts page missing");
var initial = (StackPanel)((ScrollViewer)main.Child!).Content!;
Check(initial.Children.OfType<TextBlock>().Any(t => t.Text == "Comptes"),
    "Original Accounts route inaccessible");
Check(initial.Children.OfType<Button>().Any(b =>
      (b.Content as string ?? "").Contains("Ouvrir les profils locaux")),
    "Workbench must not open TEST storage without explicit operator click");

buttons.Single(b => b.Content as string == "Sécurité TEST")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(main.Child is ScrollViewer, "Security route missing scroll container");
var route = (StackPanel)((ScrollViewer)main.Child!).Content!;
var offline = route.Children.OfType<MZP4TestAccountsPanel>().SingleOrDefault();
Check(offline is not null, "Security route is not using the genuine MZP4 App panel");
Check(!offline!.IsTestStoreOpen && offline.TestProfileCount == 0,
    "Navigation opened a test profile store without explicit user consent");
Check(offline.OperatorStatus.Contains("non ouvert", StringComparison.OrdinalIgnoreCase),
    "Security panel initial status misleads about connection");
Check(route.Children.OfType<Border>().Any(),
    "No explicit network/runtime limitation notice");

buttons.Single(b => b.Content as string == "Bots")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var bots = (StackPanel)((ScrollViewer)main.Child!).Content!;
Check(bots.Children.OfType<TextBlock>().Any(t => t.Text == "Bots"),
    "Original Bots route inaccessible after security page");
Check(!offline.IsTestStoreOpen, "Navigating from security leaked a TEST lease");

buttons.Single(b => b.Content as string == "Sécurité TEST")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var reopened = (StackPanel)((ScrollViewer)main.Child!).Content!;
Check(reopened.Children.OfType<MZP4TestAccountsPanel>().Count() == 1,
    "Navigating back failed to compose panel");
buttons.Single(b => b.Content as string == "Paquets")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var packets = (StackPanel)((ScrollViewer)main.Child!).Content!;
var inspector = packets.Children.OfType<MZP6ProtocolInspectorPanel>().SingleOrDefault();
Check(inspector is not null, "Original Paquets route doesn't embed MZP6 product source");
Check(!inspector!.IsOfflineCandidate && !inspector.HasAuthenticSource &&
      !inspector.ServerAckCertified && !inspector.CanConnect,
    "Paquets default state fabricated a catalogue, connection or ACK");
Check(inspector.ReadinessText.Contains("Aucun catalogue", StringComparison.OrdinalIgnoreCase),
    "Without external versioned catalogue the inspector must be disabled");
Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "_MZP6_OriginalBotsView.axaml"))
    .Contains("<views:MZP6ProtocolInspectorPanel />", StringComparison.Ordinal),
    "Original GivreClone.App BotsView did not mount MZP6 product control");
window.Close();
Console.WriteLine($"MZP6 real WinExe Avalonia Headless navigation: PASS ({checks} checks); no game/credential/session or TEST storage opened.");
