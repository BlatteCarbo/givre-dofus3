using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.App.Services;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;

Environment.SetEnvironmentVariable("GIVRE_CLONE_RUN_NAME", "ui-harness-" + Guid.NewGuid().ToString("N"));
AppBuilder.Configure<SmokeApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var checks = 0;
void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
var window = new Window { Width = 900, Height = 700 };
window.Show();
if (args is ["--page-timing"])
{
    var factories = new (string Name, Func<Control> Create)[]
    {
        ("Bots", () => new BotsView()), ("Settings", () => new SettingsView()),
        ("SmithMagic", () => new SmithMagicView()),
    };
    foreach (var (name, create) in factories)
    {
        var samples = new List<double>();
        for (int run = 0; run < 6; run++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            window.Content = create();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            watch.Stop();
            if (run > 0) samples.Add(watch.Elapsed.TotalMilliseconds);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
        }
        samples.Sort();
        Console.WriteLine($"PAGE {name}: median={samples[samples.Count / 2]:F1}ms max={samples[^1]:F1}ms (5 warm runs, construct+layout, headless)");
    }
    window.Close();
    return;
}
if (args.Length == 2 && args[0] == "--provider-capture")
{
    var model = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
    var view = new BotsView();
    var diagnostic = view.GetLogicalDescendants().OfType<TextBlock>()
        .Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Diagnostic des mods visuels de combat");
    var parent = (Panel)diagnostic.Parent!;
    parent.Children.Remove(diagnostic);
    var profileField = typeof(BotsViewModel).GetField("_profile", BindingFlags.Instance | BindingFlags.NonPublic)!;
    profileField.SetValue(model, Activator.CreateInstance(profileField.FieldType));
    AppConfig.Current.ModsEnabled = true;
    AppConfig.Current.DofusEnhancedEnabled = true;
    model.RefreshCombatVisualStatusCommand.Execute(null);
    // Hold bundle availability constant so this check exercises provider exclusion,
    // not the absence of packaged DLLs in the harness output directory.
    typeof(BotsViewModel).GetField("_visualModulesAvailable", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(model, true);
    AppConfig.Current.DofusEnhancedEnabled = false;
    Check(model.CanCombatVisualOptions, "Standalone controls are available with permissions and verified modules");
    AppConfig.Current.DofusEnhancedEnabled = true;
    Check(!model.CanCombatVisualOptions, "Enhanced excludes standalone visual controls");
    diagnostic.DataContext = model;
    var panel = new StackPanel { Margin = new Thickness(16) };
    panel.Children.Add(diagnostic);
    window.Content = panel;
    window.Width = 600; window.Height = 160;
    Dispatcher.UIThread.RunJobs();
    Check(diagnostic.Text == model.CombatVisualStatus && !string.IsNullOrEmpty(diagnostic.Text), "Production diagnostic binds the provider state");
    Check(diagnostic.Bounds.Height >= diagnostic.TextLayout.Height, "Provider diagnostic fits vertically");
    using var frame = window.CaptureRenderedFrame() ?? throw new Exception("Provider diagnostic not rendered");
    frame.Save(args[1]);
    window.Close();
    Console.WriteLine("PASS provider diagnostic capture and standalone exclusion; no client or network");
    return;
}
var lifeRow = (BotRowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotRowViewModel));
lifeRow.CharLife = 400; lifeRow.CharMaxLife = 1000;
var lifeLabel = new TextBlock { DataContext = lifeRow };
lifeLabel.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(BotRowViewModel.LifeText)));
window.Content = lifeLabel; Dispatcher.UIThread.RunJobs();
string observedLifeText = lifeLabel.Text!;
lifeRow.LifeIsEstimated = true; Dispatcher.UIThread.RunJobs();
Check(lifeLabel.Text != observedLifeText && lifeLabel.Text!.StartsWith(observedLifeText), "Estimated life is visibly distinguished without losing its values");
lifeRow.LifeIsEstimated = false; Dispatcher.UIThread.RunJobs();
Check(lifeLabel.Text == observedLifeText, "A server-confirmed life observation clears the estimated indication through binding");
if (args.Length == 2 && args[0] == "--life-capture")
{
    var statsView = new BotsView();
    var stats = statsView.FindControl<Grid>("LiveCharacterStats") ?? throw new Exception("Live statistics grid missing");
    var parent = (Panel)stats.Parent!;
    parent.Children.Remove(stats);
    var statsModel = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
    var selectedField = typeof(BotsViewModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(f => f.FieldType == typeof(BotRowViewModel) && f.Name.Contains("selectedBot", StringComparison.OrdinalIgnoreCase));
    selectedField.SetValue(statsModel, lifeRow);
    stats.DataContext = statsModel;
    window.Content = stats;
    window.Width = 600; window.Height = 120;
    lifeRow.CharLife = 10000; lifeRow.CharMaxLife = 15000;
    lifeRow.LifeIsEstimated = true;
    Dispatcher.UIThread.RunJobs();
    var renderedLife = stats.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == lifeRow.LifeText);
    Console.WriteLine($"Life layout: text={renderedLife.Text}; available={renderedLife.Bounds.Width}; measured={renderedLife.TextLayout.Width}");
    Check(renderedLife.TextLayout.Width <= renderedLife.Bounds.Width, "Estimated life text must fit the real statistics column");
    using var frame = window.CaptureRenderedFrame() ?? throw new Exception("Life label frame not rendered");
    frame.Save(args[1]);
    window.Close();
    Console.WriteLine("PASS real compiled character statistics grid captured with estimated life; not the full bot screen");
    return;
}
var smith = new SmithMagicViewModel(); window.Content = new SmithMagicView { DataContext = smith }; Dispatcher.UIThread.RunJobs();
Check(!smith.CanStart && !smith.Running, "Unrecovered transport must not claim readiness");
smith.TargetName = "Vitalité"; smith.TargetCurrent = 10; smith.TargetMinimum = 20; smith.TargetNaturalMaximum = 40; smith.AddTargetCommand.Execute(null);
Check(smith.Lines.Count == 1 && smith.Lines[0].Min == 20, "Local goal editor");
smith.PerfectRollCommand.Execute(null); Check(smith.Lines[0].Min == 40, "Perfect-roll goal");
smith.BagChoices.Add(new(100, "Objet de test", "Observation de test")); smith.BagIndex = 0; smith.AddToQueueCommand.Execute(null); smith.AddToQueueCommand.Execute(null);
Check(smith.Queue.Count == 1, "Queue UID deduplication");
smith.AttachPid(0); // Does not consult Stores; no game or account is launched.
window.Content = new TreasureView(); Dispatcher.UIThread.RunJobs(); Check(window.Content is TreasureView, "Treasure XAML must instantiate offline");
// Charge les vrais XAML sans construire Stores, démarrer le relais ou lire un compte.
var hdv = (HdvViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HdvViewModel));
var bots = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
// Exercise the actual preview command without constructing Stores or any transport.
typeof(BotsViewModel).GetField("<StatChoices>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
    .SetValue(bots, new[] { "Vitalité", "Sagesse", "Force", "Intelligence", "Chance", "Agilité" });
typeof(BotsViewModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
    .Single(f => f.FieldType == typeof(BotRowViewModel) && f.Name.Contains("selectedBot", StringComparison.OrdinalIgnoreCase))
    .SetValue(bots, lifeRow);
typeof(BotRowViewModel).GetProperty(nameof(BotRowViewModel.Pid))!.SetValue(lifeRow, 101);
bots.StatAmount = 25;
var preview = bots.SpendStatPointsCommand.ExecuteAsync(null);
Check(preview.IsCompletedSuccessfully && bots.StatSpendArmed && !bots.StatSpendBusy, "First allocation click completes as a local preview without sending");
bots.StatAmount = 26;
Check(!bots.StatSpendArmed, "Changing the allocation budget invalidates its previous confirmation");
bots.SpendStatPointsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
bots.StatChoiceIndex = 1;
Check(!bots.StatSpendArmed, "Changing the characteristic invalidates its previous confirmation");
var invalidLua = GivreClone.App.Services.LuaEditor.LuaLanguage.SyntaxError("local x = 1\nlocal y = )", "preview.lua");
Check(invalidLua is { Line: 2 }, "Prelaunch compiler locates invalid syntax on the second line");
Check(GivreClone.App.Services.LuaEditor.LuaLanguage.SyntaxError("error('must not execute during preview')", "preview.lua") is null,
    "Prelaunch validation compiles without executing valid script bodies");
typeof(BotsViewModel).GetField("<LuaDocument>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
    .SetValue(bots, new AvaloniaEdit.Document.TextDocument());
var persistDraft = typeof(BotsViewModel).GetMethod("PersistLuaScript", BindingFlags.Instance | BindingFlags.NonPublic)!;
var loadDraft = typeof(BotsViewModel).GetMethod("LoadLuaScript", BindingFlags.Instance | BindingFlags.NonPublic)!;
const string draftA = "function move() return false end -- draft A";
const string draftB = "function move() return false end -- draft B";
bots.LuaScript = draftA;
persistDraft.Invoke(bots, ["HarnessA"]);
bots.LuaScript = draftB;
persistDraft.Invoke(bots, ["HarnessB"]);
PlanningStore.Load();
loadDraft.Invoke(bots, ["HarnessA"]);
Check(bots.LuaScript == draftA, "Unlaunched draft A survives a real JSON store reload");
loadDraft.Invoke(bots, ["HarnessB"]);
Check(bots.LuaScript == draftB, "Unlaunched draft B remains isolated from character A");
var documented = GivreClone.App.Services.LuaEditor.LuaLanguage.Catalog.Entries
    .First(e => e.Params.Any(p => p.Optional && p.Default is not null));
var apiCard = new GivreClone.App.Controls.LuaEditor.LuaApiCard
{
    Row = new LuaApiRow(documented.QualifiedName, documented.Description, "", Brushes.White, new[] { documented })
};
window.Content = apiCard;
Dispatcher.UIThread.RunJobs();
var renderedRuns = apiCard.GetVisualDescendants().OfType<TextBlock>()
    .Where(t => t.Inlines is not null).SelectMany(t => t.Inlines!).OfType<Avalonia.Controls.Documents.Run>()
    .Select(r => r.Text).ToArray();
foreach (var parameter in documented.Params)
{
    var expectedParameter = (parameter.Optional ? "[" : "") + parameter.Name
        + (parameter.Default is { } value ? "=" + value : "") + (parameter.Optional ? "]" : "");
    Check(renderedRuns.Contains(expectedParameter), "API card renders the embedded parameter name, optionality and default");
}
typeof(BotsViewModel).GetField("_luaHosts", BindingFlags.Instance | BindingFlags.NonPublic)!
    .SetValue(bots, new Dictionary<int, GivreLuaHost>());
typeof(BotsViewModel).GetField("_editorLocations", BindingFlags.Instance | BindingFlags.NonPublic)!
    .SetValue(bots, new HashSet<GivreClone.Engine.LuaTooling.LuaErrorLocation>());
GivreClone.App.Controls.LuaEditor.LuaEditorRequest? jump = null;
bots.LuaEditorRequested += request => jump = request;
var errorRow = new LuaLogRow("", "script", "runtime error", Brushes.White, Brushes.White, FontWeight.Normal,
    LuaLogLevel.Danger, 0, new GivreClone.Engine.LuaTooling.LuaErrorLocation("script.lua", 7, 3, null, null, "runtime error"));
bots.LuaJumpToLogCommand.Execute(errorRow);
Check(jump is { Action: GivreClone.App.Controls.LuaEditor.LuaEditorAction.Jump, Line: 7, Column: 4 },
    "Clicking a located console error requests the exact line and one-based editor column");
var codePreview = new GivreClone.App.Controls.LuaEditor.LuaCodePreview { Text = "error('preview must not execute')" };
window.Content = codePreview;
Dispatcher.UIThread.RunJobs();
var previewEditor = codePreview.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
Check(previewEditor.IsReadOnly && previewEditor.Text == codePreview.Text && !ReferenceEquals(previewEditor.Document, bots.LuaDocument),
    "Template preview displays inert code in its own read-only document");
codePreview.Text = "function move() return false end";
Check(bots.LuaScript == draftB, "Changing a template preview does not overwrite the character draft");
bots.NoCodeMaps = "12345:right";
bots.NoCodeGather = false;
bots.NoCodeFight = false;
bots.NoCodeMaxPods = "101";
bots.NoCodePreviewCommand.Execute(null);
bots.NoCodeRequestReplaceCommand.Execute(null);
bots.NoCodeConfirmReplaceCommand.Execute(null);
Check(!bots.NoCodePreviewReady && bots.LuaScript == draftB, "Out-of-range parameters cannot replace the character draft through import commands");
var template = bots.LuaScriptTemplates.First();
string templateSource = template.Body;
bots.SelectedLuaTemplate = template;
bots.LuaTemplatesTab = 0;
bots.LuaInsertTemplateCommand.Execute(null);
Check(jump is { Action: GivreClone.App.Controls.LuaEditor.LuaEditorAction.Insert } && jump.Text == templateSource,
    "Template command supplies the selected preset to the editor without executing it");
Check(bots.ApplyGeneratedLua(jump!.Text!), "Template text can be copied into an editable draft");
bots.LuaScript += "\n-- independent local edit";
Check(template.Body == templateSource && bots.LuaScript != templateSource,
    "Editing a copied preset leaves its library source intact");
typeof(BotsViewModel).GetField("_hdv", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bots, hdv);
var overlay = new HdvSellOverlay { DataContext = bots };
window.Content = overlay; Dispatcher.UIThread.RunJobs();
Check(overlay.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Actualiser sac + banque")), "Compiled HDV overlay exposes bank refresh");
Check(Application.Current!.TryFindResource("L.Bots.Hdv.BankUnavailable", out var bankUnavailable) && bankUnavailable is string text && text.Contains("non confirmée"), "Failed bank load must not claim success");
var botsView = new BotsView();
window.Content = botsView; Dispatcher.UIThread.RunJobs();
Check(botsView.FindControl<Expander>("ExpeditionRoutePreview") is { } routePreview && Equals(routePreview.Header, "Aperçu du parcours"), "Compiled expedition route preview is localized");
Check(botsView.FindControl<TextBlock>("ExpeditionRouteLastVisit") is not null, "Compiled expedition last-visit control exists");
var progressByPid = new Dictionary<int, string>();
var visitsByPid = new Dictionary<int, long>();
typeof(BotsViewModel).GetField("_expVisitsByPid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bots, visitsByPid);
typeof(BotsViewModel).GetField("_expProgressByPid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bots, progressByPid);
var noteProgress = typeof(BotsViewModel).GetMethod("NoteExpeditionProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
noteProgress.Invoke(bots, [101, "[expedition-route] 2/5 map=191104000"]);
noteProgress.Invoke(bots, [102, "[expedition-route] 1/3 map=-5,19"]);
Check(progressByPid.Count == 2 && progressByPid[101].Contains("2/5") && progressByPid[102].Contains("1/3"), "Expedition progress is isolated by bot PID");
noteProgress.Invoke(bots, [101, "[expedition-route] 9/5 map=1"]);
Check(progressByPid[101].Contains("2/5"), "Malformed progress cannot overwrite a valid visit");
noteProgress.Invoke(bots, [101, "[expedition-route] 1/5 map=191104000"]);
Check(visitsByPid[101] == 2 && visitsByPid[102] == 1, "Cumulative visits increase across route wraparound and remain isolated by PID");
typeof(BotsViewModel).GetMethod("ClearExpeditionProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bots, [101]);
Check(!progressByPid.ContainsKey(101) && progressByPid.ContainsKey(102), "Starting a replacement script clears only its own stale route progress");
Check(!visitsByPid.ContainsKey(101) && visitsByPid[102] == 1, "Replacement resets only its own cumulative visit counter");
// Le bundle installé est une fixture de lecture seule, pas une preuve d'action en jeu.
var bundle = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ankama", "Dofus-dofus3", "Dofus_Data", "StreamingAssets", "Content", "Data", "data_assets_spellvariantsdataroot.asset.bundle");
if (File.Exists(bundle))
{
    object?[] variantArgs = [bundle, null];
    var breeds = (System.Collections.IDictionary)typeof(GameNameResolver).GetMethod("ReadSpellVariants", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, variantArgs)!;
    var pairs = (Dictionary<int, int[]>)variantArgs[1]!;
    Check(breeds.Count > 0 && pairs.Count > 0, "Installed 3.7 spell variant bundle decodes real pairs");
    Check(pairs.All(p => p.Value.Contains(p.Key) && p.Value.Length > 1), "Variant pairs retain both real spell IDs");
    Console.WriteLine($"Read-only installed bundle: {breeds.Count} breeds, {pairs.Count} spell pair entries");
}
else Console.WriteLine("SKIP installed spell variant bundle: absent");
window.Close(); Console.WriteLine($"PASS {checks} recovered UI checks; no game, accounts, login or network started");
sealed class SmokeApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme()); Resources["Bg1Brush"] = Brushes.Black;
        var baseUri = new Uri("avares://GivreClone/");
        foreach (string path in new[] { "Styles/Theme.axaml", "Styles/Icons.axaml", "Lang/fr.axaml" })
            Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(baseUri) { Source = new Uri("avares://GivreClone/" + path) });
    }
}
