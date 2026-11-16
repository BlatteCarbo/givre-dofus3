using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using GivreClone.App.Views;
using GivreClone.App.Services;

// Real production GivreClone.App project/Panel, not a copy or surrogate logic.
// Never initializes LoginOverlay/Launcher/BotHost or any accounts/processes.
AppBuilder.Configure<OfflineHeadlessApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("MZP10 UI: " + label);
    checks++;
}

var panel = new MZP10MappingVerificationPanel();
Check(!panel.HasGameProtocolProof && !panel.CanLaunchDofus,
    "panel must never promote metadata to gameplay");
Check(panel.StatusText.Contains("non comparé", StringComparison.OrdinalIgnoreCase),
    "default panel must show no comparison");
var root = (Border)panel.Content!;
var stack = (StackPanel)root.Child!;
var button = stack.Children.OfType<Button>().Single();
Check(button.IsEnabled && button.Content is string s && s.Contains("Revérifier"),
    "real refresh control present");
button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Check(panel.StatusText.Contains("non comparé", StringComparison.OrdinalIgnoreCase),
    "refresh without signed mapping cannot fabricate result");
var status = stack.Children.OfType<TextBlock>().ToArray();
Check(status.Any(t => (t.Text ?? "").Contains(MZP4LauncherPreflight.TargetClientVersion)),
    "actual local candidate target visible after button verification");
Check(status.Any(t => (t.Text ?? "").Contains("métadonnées locales", StringComparison.OrdinalIgnoreCase) ||
                     (t.Text ?? "").Contains("non vérifié", StringComparison.OrdinalIgnoreCase)),
    "installed version must be local metadata or unverified, never game ACK");
var page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OriginalAccountsView.axaml"));
Check(page.Contains("<views:MZP10MappingVerificationPanel />", StringComparison.Ordinal),
    "original Accounts page must really mount mapping panel");
Check(!page.Contains("<views:FakeMappingPanel", StringComparison.Ordinal),
    "no fake substitute mounted in production page");

Console.WriteLine($"MZP10 real GivreClone.App Accounts UI: PASS ({checks} Headless checks). No login/client/network.");

internal sealed class OfflineHeadlessApp : Avalonia.Application { }
