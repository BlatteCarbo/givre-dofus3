using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.Views;
using GivreClone.App.Services.LotM;
using System.Security.Cryptography;

if (args is ["--dataset", var dataset])
{
    string Digest() { using var file = File.OpenRead(dataset); return Convert.ToHexString(SHA256.HashData(file)); }
    var before = Digest();
    int jobs, recipes;
    using (var db = GameDb.TryOpen(dataset) ?? throw new Exception("Real dataset could not be opened."))
    {
        var availableJobs = db.Jobs();
        jobs = availableJobs.Count;
        recipes = availableJobs.Sum(job => db.RecipesOfJob(job.Id, int.MaxValue).Count);
        if (jobs == 0 || recipes == 0 || string.IsNullOrWhiteSpace(db.ItemName(16828)))
            throw new Exception("Real dataset is missing expected jobs, recipes or translations.");
    }
    if (before != Digest()) throw new Exception("Read-only dataset changed.");
    Console.WriteLine($"PASS real dataset: {jobs} jobs, {recipes} recipes, translated items; source hash unchanged ({before}).");
    return;
}

AppBuilder.Configure<GivreClone.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var scratch = Path.Combine(Path.GetTempPath(), "Givre-R6-UI-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var preferences = new LocalNotificationPreferences(Path.Combine(scratch, "preferences.json"));
    var window = new LocalNotificationSettingsWindow(preferences, "fixture-account", () => "fixture");
    window.Show();
    Dispatcher.UIThread.RunJobs();
    var panel = (StackPanel)((ScrollViewer)window.Content!).Content!;
    var toggles = panel.Children.OfType<CheckBox>().ToArray();
    if (toggles.Length != 8 || toggles[0].IsChecked == true) throw new Exception("Notification opt-in controls missing.");
    toggles[0].IsChecked = true;
    toggles[1].IsChecked = false;
    panel.Children.OfType<Button>().First(b => b.GetType() == typeof(Button)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var reloaded = new LocalNotificationPreferences(Path.Combine(scratch, "preferences.json"));
    if (!reloaded.Get("fixture-account").Enabled || reloaded.Get("fixture-account").Events.HasFlag(LocalBotEvent.PodsFull)
        || reloaded.Get("another-account").Enabled)
        throw new Exception("UI preferences did not persist per account: " + reloaded.Get("fixture-account") + " / " +
            string.Join(" | ", panel.Children.OfType<TextBlock>().Select(t => t.Text)));
    window.Close();
    var uid = new HdvUidOperatorWindow();
    uid.Show();
    Dispatcher.UIThread.RunJobs();
    if (uid.Bounds.Width <= 0 || uid.Content is not Grid) throw new Exception("Production UID window did not render.");
    uid.Close();
    var settings = new SettingsView();
    if (settings.Content is not ScrollViewer) throw new Exception("Production settings did not construct.");
    Console.WriteLine("PASS production notification window save/reload/isolation, UID window layout, settings construction. No account loading, client or network.");
}
finally
{
    foreach (var file in Directory.GetFiles(scratch)) File.Delete(file);
    Directory.Delete(scratch);
}
