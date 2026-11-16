using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using GivreClone.App.Views;

var checks = 0;
void Check(bool condition, string why)
{
    if (!condition) throw new InvalidOperationException(why);
    checks++;
}

AppBuilder.Configure<OfflineApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();

var runId = "ui-" + Guid.NewGuid().ToString("N");
var panel = new MZP4TestAccountsPanel(runId);
Check(!panel.IsTestStoreOpen, "Construction opened storage without approval");
Check(panel.TestProfileCount == 0, "New panel claims game accounts");
Check(panel.OperatorStatus.Contains("non ouvert", StringComparison.OrdinalIgnoreCase),
    "Misleading initial UI status");

var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "GivreReplica", "TestRuns", runId);
Check(!Directory.Exists(path), "Constructor created a store directory");
var root = (Border)panel.Content!;
var content = (StackPanel)root.Child!;
var open = content.Children.OfType<Button>().Single();
Check(open.IsEnabled, "Explicit open button must be usable");
var editor = content.Children.OfType<StackPanel>().Single();
Check(!editor.IsVisible, "Editor not gated on consent");
var actions = editor.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
Check(actions.Length == 7 && actions.All(b => !b.IsEnabled), "Offline mutations must start disabled");
Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "AccountsView.axaml"))
    .Contains("<views:MZP4TestAccountsPanel />", StringComparison.Ordinal),
    "Real Accounts page does not mount production test panel");

// First assert zero side effects before the explicit click. Then exercise the
// same UI button a user would press with a disposable, generated TEST run key.
Check(!Directory.Exists(path), "Headless inspection wrote unexpectedly");
try
{
    open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var until = DateTime.UtcNow.AddSeconds(5);
    while (!panel.IsTestStoreOpen && DateTime.UtcNow < until)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Thread.Sleep(15);
    }
    Check(panel.IsTestStoreOpen, "Explicit UI click could not open own test store: " + panel.OperatorStatus +
        "; directory=" + Directory.Exists(path));
    Check(Directory.Exists(path), "Test-run file did not persist on disk");
    Check(editor.IsVisible && actions.All(b => b.IsEnabled), "UI editor did not unlock after real store opening");

    var alias = editor.Children.OfType<TextBox>().First();
    alias.Text = "mzp4-qa-nonsecret";
    actions[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(panel.TestProfileCount == 1, "UI create/save did not use real MZP3 store");
    Check(panel.OperatorStatus.Contains("enregistré", StringComparison.OrdinalIgnoreCase), "No UI save acknowledgement");
    var fields = editor.Children.OfType<TextBox>().ToArray();
    fields[1].Text = "offline-server";
    fields[2].Text = "offline-character";
    actions[3].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(panel.OperatorStatus.Contains("NON authentifiés", StringComparison.OrdinalIgnoreCase),
        "Candidate fields must never claim a server verification");
    var port = editor.Children.OfType<TextBox>().Last();
    var buttons = editor.Children.OfType<Button>().ToArray();
    Check(buttons.Length == 4, "Recovery, health, preflight and isolated browser buttons missing from real Accounts view");
    var preparation = buttons[0];
    var journalHealth = buttons[1];
    var preflight = buttons[2];
    var testBrowser = buttons[3];
    Check((testBrowser.Content?.ToString() ?? "").Contains("Authentification Ankama TEST"),
        "TEST browser UI button not integrated into Accounts view");
    Check(!testBrowser.IsEnabled,
        "Browser login must stay disabled for non-production isolated harness profile");
    Check(preflight.IsEnabled, "Preflight button not wired to real test store");
    port.Text = "5658";
    preflight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(panel.OperatorStatus.Contains("ProtectedOrInvalidPort"),
        "Original GivreClone port 5658 was not rejected");
    port.Text = "15432";
    preflight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(panel.OperatorStatus.Contains("JournalDirectoryMissing") &&
          panel.OperatorStatus.Contains("Aucun client"),
        "Preflight must refuse absent recovery without creating it or starting host");
    preparation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(panel.OperatorStatus.Contains("Journal TEST privé prêt"),
        "Explicit recovery preparation failed");
    Check(Directory.Exists(Path.Combine(path, "recovery")),
        "Private recovery directory missing after explicit preparation");
    journalHealth.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(panel.OperatorStatus.Contains("NoActiveGeneration") &&
          panel.OperatorStatus.Contains("ACK serveur NON certifiés"),
        "Real journal offline inspection should not imply a game session");
    Check(File.Exists(Path.Combine(path, "recovery", "mzp5-test-recovery.json.lck")),
        "Real journal lease file not initialized in isolated run");
    preflight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(panel.OperatorStatus.Contains("CANDIDAT uniquement") &&
          panel.OperatorStatus.Contains("ACK NON vérifiés"),
        "Valid local configuration must not become network/gameplay authority");
}
finally
{
    panel.Dispose();
    var full = Path.GetFullPath(path);
    var parent = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "GivreReplica", "TestRuns"));
    if (!full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(full).Equals(runId, StringComparison.Ordinal)
        || !runId.StartsWith("ui-", StringComparison.Ordinal))
        throw new InvalidOperationException("Unsafe cleanup target");
    if (Directory.Exists(full))
    {
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refuse cleanup of reparse directory");
        Directory.Delete(full, recursive: true);
    }
}
Check(!Directory.Exists(path), "Generated test folder not cleaned");
Console.WriteLine($"MZP4 real-Accounts-panel headless: PASS ({checks} checks). UI CRUD, isolated local disk; no game process touched.");

sealed class OfflineApp : Application { }
