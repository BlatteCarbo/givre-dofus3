using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.Services.UiNavigation;
using GivreClone.App.ViewModels;
using GivreClone.Engine;

namespace GivreClone.App.Views;

public partial class BotsView : UserControl
{
    private BotsViewModel? _luaScrollModel;
    private TopLevel? _mapSizeOwner;
    private readonly DispatcherTimer _gameImgTimer;
    private readonly DispatcherTimer _fightRefreshTimer;

    public BotsView()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, OnCompactFieldGotFocus, RoutingStrategies.Bubble);
        // La vue est recréée à chaque visite de la page Bots : on rouvre le dernier onglet consulté.
        if (_lastTab is not null) SelectBotTab(_lastTab);
        _gameImgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _gameImgTimer.Tick += async (_, _) => { await SyncGameImageAsync(); await SyncSelfLookAsync(); SyncEquipStats(); };
        // Petit écran (portable 14") : la carte et son panneau s'adaptent à la hauteur / largeur de la fenêtre au lieu d'une taille fixe.
        AttachedToVisualTree += (_, _) =>
        {
            _gameImgTimer.Start();
            _fightRefreshTimer.Start();
            if (_mapSizeOwner is not null) _mapSizeOwner.SizeChanged -= OnMapHostSizeChanged;
            _mapSizeOwner = TopLevel.GetTopLevel(this);
            if (_mapSizeOwner is not null)
            {
                _mapSizeOwner.SizeChanged += OnMapHostSizeChanged;
                FitMapTab(_mapSizeOwner);
            }
            HookLuaAutoScroll(this, EventArgs.Empty);
            ApplyRequestedTab();
        };
        DataContextChanged += (_, _) =>
        {
            if (_requestedTabModel is not null) _requestedTabModel.PropertyChanged -= OnRequestedTabChanged;
            _requestedTabModel = DataContext as BotsViewModel;
            if (_requestedTabModel is not null) _requestedTabModel.PropertyChanged += OnRequestedTabChanged;
            ApplyRequestedTab();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _gameImgTimer.Stop();
            _fightRefreshTimer.Stop();
            if (_mapSizeOwner is not null) _mapSizeOwner.SizeChanged -= OnMapHostSizeChanged;
            _mapSizeOwner = null;
            if (_luaScrollModel is not null) _luaScrollModel.LuaOutput.CollectionChanged -= OnLuaOutputChanged;
            _luaScrollModel = null;
        };
        // Clic gauche sur la carte -> deplacement du perso (forge MapMovementRequest cote VM/relais).
        Map.MoveRequested += (source, target) => (DataContext as BotsViewModel)?.OnMoveRequested(source, target);
        // Clic droit sur un interactif -> "Recolter" : marche vers une cellule adjacente puis InteractiveUseRequest.
        Map.HarvestRequested += it => (DataContext as BotsViewModel)?.OnHarvestRequested(it);
        // Clic droit sur un groupe : capturer l'identité du bot/carte AU MOMENT
        // d'ouvrir le menu, pas au clic tardif sur "Attaquer".
        Map.AttackOriginProvider = () =>
            (DataContext as BotsViewModel)?.CaptureAttackMenuOrigin(Map.Actors, Map.MapEpoch);
        _fightRefreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            (_, _) => (DataContext as BotsViewModel)?.SelectedBot?.RefreshFight());
        Map.FightCellClicked += (cell, right) => (DataContext as BotsViewModel)?.SelectedBot?.OnFightCell(cell, right);
        Map.PlayerActionRequested += (pl, act) => (DataContext as BotsViewModel)?.OnPlayerAction(pl, act);
        Map.AttackRequested += request => (DataContext as BotsViewModel)?.OnAttackRequested(request);
        // Inspection : clic gauche sur une entite -> fiche (au lieu d'un deplacement) ; Echap ferme, fleches parcourent.
        Map.CellInspector = cell =>
        {
            var insp = (DataContext as BotsViewModel)?.SelectedBot?.Inspector;
            if (insp is null) return false;
            return insp.InspectCell(cell);   // ouvert depuis le menu clic droit « Fiche »
        };
        // Aperçu de trajet : le nombre de pas calculé par la carte remonte au bot affiché.
        Map.PropertyChanged += (_, e) =>
        {
            if (e.Property == Controls.IsoMapControl.PreviewStepsProperty && (DataContext as BotsViewModel)?.SelectedBot?.Inspector is { } insp0)
                insp0.PreviewSteps = Map.PreviewSteps;
        };
        Map.KeyAction += act =>
        {
            var insp = (DataContext as BotsViewModel)?.SelectedBot?.Inspector;
            if (insp is null) return;
            if (act == "escape") insp.ClearSelection(); else insp.Cycle(act == "next" ? 1 : -1);
        };
        // Auto-defilement de la console Lua : on suit toujours la ligne la plus recente.
        DataContextChanged += HookLuaAutoScroll;
        // Glisser-déposer pour réordonner la rotation de sorts (poignée = badge de priorité) : on autorise le drop sur la
        // liste et on écoute DragOver/Drop au niveau du conteneur (les events remontent depuis la ligne survolée).
        DragDrop.SetAllowDrop(SpellsList, true);
        SpellsList.AddHandler(DragDrop.DragOverEvent, OnSpellDragOver);
        SpellsList.AddHandler(DragDrop.DropEvent, OnSpellDrop);
    }

    // Format in-process : on transporte la SpellRowViewModel VIVE (référence, aucune sérialisation) le temps du glisser.
    private static readonly DataFormat<SpellRowViewModel> SpellDragFormat = DataFormat.CreateInProcessFormat<SpellRowViewModel>("givre.spellrow");

    // Début du glisser depuis le badge de priorité d'une ligne de sort.
    private async void OnSpellDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not SpellRowViewModel row) return;
        if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(SpellDragFormat, row));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnSpellDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(SpellDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    // Dépôt : on retrouve la ligne cible sous le pointeur (remontée visuelle depuis e.Source) et on réordonne.
    private void OnSpellDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not BotsViewModel vm || e.DataTransfer.TryGetValue(SpellDragFormat) is not { } dragged) return;
        var target = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(x => x.DataContext).OfType<SpellRowViewModel>().FirstOrDefault();
        if (target is not null) vm.MoveSpellTo(dragged, target);
        e.Handled = true;
    }

    private void HookLuaAutoScroll(object? sender, EventArgs e)
    {
        if (_luaScrollModel is not null) _luaScrollModel.LuaOutput.CollectionChanged -= OnLuaOutputChanged;
        _luaScrollModel = TopLevel.GetTopLevel(this) is null ? null : DataContext as BotsViewModel;
        if (_luaScrollModel is not null) _luaScrollModel.LuaOutput.CollectionChanged += OnLuaOutputChanged;
    }

    private void OnLuaOutputChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action != NotifyCollectionChangedAction.Add || _luaScrollModel is not { } model) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_luaScrollModel == model && model.LuaOutput.Count > 0)
                LuaConsole.ScrollIntoView(model.LuaOutput[^1]);
        }, DispatcherPriority.Background);
    }

    private void OnMapHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_mapSizeOwner is not null) FitMapTab(_mapSizeOwner);
    }

    // Fondu doux au changement d'onglet : on repose l'opacite du conteneur de contenu a 0 puis a 1 -> la transition
    // d'opacite (DoubleTransition sur PART_SelectedContentHost, cf axaml) anime le fondu d'entree. Filtre les
    // SelectionChanged qui REMONTENT d'un ComboBox interne (combat) : on n'agit que sur une vraie bascule d'onglet.
    private string _equipShown = "";

    // Total des caractéristiques de l'équipement porté (somme des lignes d'infobulle de chaque objet, malus retranchés ; bonus de panoplie non inclus).
    private int _equipStamp = -1; private long _equipTick; private uint _equipPid;

    private void SyncEquipStats()
    {
        if (DataContext is not BotsViewModel { SelectedBot: { Pid: > 0 } bot }) return;
        uint pid = (uint)bot.Pid;
        // Pas de recalcul à chaque tick : seulement si les effets d'objets ont changé (ou au plus toutes les 10 s) -> moins d'allocations (pauses du ramasse-miettes).
        int stamp = Stores.Launch.ItemEffectsStamp(pid); long now = Environment.TickCount64;
        if (pid == _equipPid && stamp == _equipStamp && now - _equipTick < 10_000) return;
        _equipPid = pid; _equipStamp = stamp; _equipTick = now;
        var worn = Stores.Launch.Inventory.Equipped(pid);
        var totals = Services.Equipment.BuildStats.Aggregate(worn.SelectMany(i => Stores.Launch.ItemEffectLines(pid, i.Uid)).Concat(Stores.Launch.SetBonusLines(pid)));
        string text = worn.Count == 0 ? "Aucun objet équipé connu (reconnecte le perso pour charger le sac)."
            : $"{worn.Count} objet(s) équipé(s){Environment.NewLine}" + string.Join(Environment.NewLine, totals.Select(t => t.Text)) + $"{Environment.NewLine}(bonus de panoplie inclus)";
        if (text == _equipShown) return;
        _equipShown = text;
        EquipStats.Text = text;
    }

    private string _lookShown = "";
    private string _lookKey = "";
    private bool _lookBusy;
    // Vue « Personnage » : quand le perso est monté, le moteur d'Ankama ne dessine que la monture (il ignore le cavalier
    // en sous-entité) -> on rend le cavalier seul, et à défaut l'icône de classe.
    private bool _showRider;

    private void OnToggleLookView(object? sender, RoutedEventArgs e)
    {
        _showRider = !_showRider;
        _lookKey = "";   // force le rendu au prochain tick
        _ = SyncSelfLookAsync();
    }

    // Affiche l'apparence du perso du bot sélectionné (look lu dans les paquets, rendu via Ankama) ; se met à jour toute seule quand le look change.
    private async System.Threading.Tasks.Task SyncSelfLookAsync()
    {
        if (_lookBusy || DataContext is not BotsViewModel { SelectedBot: { Pid: > 0 } bot }) return;
        var info = Stores.Launch.SelfLook((uint)bot.Pid);
        string look = info?.ToLookString() ?? "";
        var riderInfo = info?.CharacterLook;
        bool mounted = info is not null && !ReferenceEquals(riderInfo, info);
        bool riderView = mounted && _showRider;
        string key = (riderView ? "rider:" : "full:") + look;
        if (key == _lookKey) return;
        _lookBusy = true;
        try
        {
            _lookShown = look;
            _lookKey = key;
            LookViewToggle.IsVisible = mounted;
            LookViewToggleText.Text = riderView ? "Voir la monture" : "Voir le personnage";
            if (look.Length == 0) { SelfLookImage.Source = null; return; }
            string target = riderView ? riderInfo!.ToLookString() : look;
            var path = await System.Threading.Tasks.Task.Run(() => LookRenderer.EnsureAsync(target)).ConfigureAwait(true);
            if (_lookKey != key) return;
            // Repli : si le moteur d'Ankama ne rend pas ce look, on montre au moins l'icône de classe (au lieu d'un cadre vide).
            Avalonia.Media.Imaging.Bitmap? fallback = null;
            if (path is null && bot.Row.BreedIndex is int bi) { try { Stores.ClassIcons.EnsureLoaded(); } catch { } fallback = Stores.ClassIcons.Get(bi, bot.Row.GenderIndex ?? 0); }
            SelfLookImage.Source = path is null ? fallback : new Avalonia.Media.Imaging.Bitmap(path);
            SelfLookStatus.Text = path is not null
                ? (riderView ? "Vue personnage (cavalier seul)." : mounted ? "Vue monture (look complet reçu)." : "Apparence mise à jour automatiquement.")
                : riderView ? "Le cavalier ne peut pas être rendu seul : icône de classe affichée."
                : $"Rendu indisponible ({LookRenderer.LastFailure}). Look : {look}";
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { SelfLookImage.Source = null; }
        finally { _lookBusy = false; }
    }

    private async void OnCopyLook(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lookShown)) await CopyToClipboardAsync(_lookShown);
    }

    private long _gameImgMap;
    private bool _gameImgBusy;

    // Charge (cache disque / réseau) l'image DofusDB de la carte du bot affiché, ou la retire quand l'option est décochée.
    private async System.Threading.Tasks.Task SyncGameImageAsync()
    {
        if (_gameImgBusy || DataContext is not BotsViewModel vm) return;
        long map = vm.ShowGameImage ? (vm.SelectedBot?.MapId ?? 0) : 0;
        if (map == _gameImgMap) return;
        _gameImgBusy = true;
        try
        {
            _gameImgMap = map;
            if (map == 0) { Map.BackgroundImage = null; return; }
            Services.WorldMap.DofusDbWorldMap.CacheFolder = Path.Combine(MZP19LocalClone.DataRoot, "worldmap");
            var path = await System.Threading.Tasks.Task.Run(() => Services.WorldMap.DofusDbWorldMap.EnsureFullImageAsync(map)).ConfigureAwait(true);
            if (_gameImgMap != map) return;   // la carte a encore changé pendant le chargement
            Map.BackgroundImage = path is null ? null : new Avalonia.Media.Imaging.Bitmap(path);
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { Map.BackgroundImage = null; }
        finally { _gameImgBusy = false; }
    }

    // Clic sur un texte (id de carte, position, cellule) = copie dans le presse-papiers.
    private async void OnCopyText(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock { Text: { Length: > 0 } text }) return;
        await CopyToClipboardAsync(text.Trim());
        e.Handled = true;
    }

    private async void OnCopyMapInfo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BotsViewModel { SelectedBot: { } bot }) return;
        string cell = Map.HoveredCell is int c ? c.ToString(System.Globalization.CultureInfo.InvariantCulture) : (bot.SelfCellId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?");
        await CopyToClipboardAsync($"{bot.Row.MapId} | {bot.Row.MapCoords} | cellule {cell}");
    }

    private async System.Threading.Tasks.Task CopyToClipboardAsync(string text)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null) return;
        await clip.SetTextAsync(text);
        Toasts.Show("Copié : " + text, success: true, sound: false);
    }

    // Expédition : plusieurs sous-zones combinées (cartes et monstres réunis).
    private void OnExpSubAreasChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is BotsViewModel vm && sender is ListBox lb)
            vm.SetExpSubAreas(lb.SelectedItems?.OfType<Services.LotM.ExpSubArea>().ToList() ?? new());
    }

    private void FitMapTab(TopLevel tl)
    {
        double h = Math.Clamp(tl.Bounds.Height - 330, 360, 700);
        MapTabGrid.Height = h; MapTabBorder.Height = h;
        double panel = tl.Bounds.Width < 1250 ? 250 : 320;
        MapTabGrid.ColumnDefinitions[1].Width = new GridLength(panel);
        // La carte garde au moins la largeur de sa fenêtre d'interaction (500 + marges) : en fenêtre étroite,
        // MapTabScroll défile horizontalement au lieu d'écraser la carte et de rogner la banque / l'échange.
        MapTabGrid.MinWidth = MapInteractionMinWidth + MapTabGrid.ColumnSpacing + panel;
    }

    private const double MapInteractionMinWidth = 520;
    private const double BotListCompactBreakpoint = 960;
    private const double BotListCompactWidth = 208;
    private const double ExpeditionStackBreakpoint = 640;

    // Fenêtre compacte : la liste « Bots en jeu » se resserre pour laisser la place au détail du bot.
    private void OnBotsPageSizeChanged(object? sender, SizeChangedEventArgs e)
        => BotListPanel.Width = e.NewSize.Width < BotListCompactBreakpoint ? BotListCompactWidth : 272;

    // Expédition : réglages et détails côte à côte quand la place le permet, empilés sinon (jamais de colonne de 16 px).
    private void OnExpeditionLayoutSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        bool stacked = e.NewSize.Width < ExpeditionStackBreakpoint;
        if (stacked == (Grid.GetRow(ExpeditionDetails) == 1)) return;
        ExpeditionLayout.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "300,*");
        ExpeditionLayout.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "*");
        ExpeditionLayout.RowSpacing = stacked ? ExpeditionLayout.ColumnSpacing : 0;
        Grid.SetRow(ExpeditionDetails, stacked ? 1 : 0);
        Grid.SetColumn(ExpeditionDetails, stacked ? 0 : 1);
    }

    // Dernier onglet Bots consulté, par Tag stable (réordonner ou masquer un onglet ne décale rien).
    private static string? _lastTab;
    private BotsViewModel? _requestedTabModel;

    private void OnRequestedTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BotsViewModel.RequestedTab)) ApplyRequestedTab();
        // Sortie du mode Développeur sur un onglet développeur (MCP, Paquets) : on revient à la Console.
        else if (e.PropertyName == nameof(BotsViewModel.IsDeveloperUi) && BotTabs.SelectedItem is TabItem { IsVisible: false })
            SelectBotTab(QuickNavigationCatalog.BotTabTags[0]);
    }

    // Recherche rapide (Ctrl+K) / retour Alt+← -> onglet Bots demandé (clé « bots/<n> » = BotTabTags[n]) ; consommé une fois.
    private void ApplyRequestedTab()
    {
        if (DataContext is not BotsViewModel vm || vm.RequestedTab < 0) return;
        if (QuickNavigationCatalog.BotTabTagAt(vm.RequestedTab) is { } tag) SelectBotTab(tag);
        vm.RequestedTab = -1;
    }

    /// <summary>Sélectionne l'onglet Bots portant ce Tag ; sans effet si aucun onglet ne le porte.</summary>
    private bool SelectBotTab(string tag)
    {
        var item = BotTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag as string == tag);
        if (item is null) return false;
        BotTabs.SelectedItem = item;
        return true;
    }

    // Ctrl+F : recherche du Journal (si la console est affichée) ; Échap dans la recherche la vide.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control) && LuaSearchBox.IsEffectivelyVisible)
        { LuaSearchBox.Focus(); LuaSearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape && LuaSearchBox.IsFocused && DataContext is BotsViewModel vm)
        { vm.LuaSearch = ""; e.Handled = true; }
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tab || e.AddedItems.Count == 0 || e.AddedItems[0] is not TabItem) return;
        if (tab == BotTabs)
        {
            var tag = (e.AddedItems[0] as TabItem)?.Tag as string;
            _lastTab = tag;
            // Alt+← revient aussi à l'onglet précédent (index canonique du Tag, pas la position dans le XAML).
            if (QuickNavigationCatalog.BotTabIndexOf(tag) is int canonical) MainViewModel.Instance?.RecordBotTab(canonical);
        }
        var host = tab.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(c => c.Name == "PART_SelectedContentHost");
        if (host is null) return;
        host.Opacity = 0;
        Dispatcher.UIThread.Post(() => host.Opacity = 1, DispatcherPriority.Render);
    }

    private void OnClearConsole(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BotsViewModel vm) return;
        // Journal = la console Lua du bot courant ; technique : celle du transport. Dans les deux cas, seulement le bot sélectionné.
        if (vm.ConsoleJournal) { vm.ClearLuaLogCommand.Execute(null); return; }
        if (vm.ConsoleChat) { vm.ClearChatCommand.Execute(null); return; }
        if (vm.SelectedBot is { } bot) TransportDiag.ClearFor((uint)bot.Pid);
    }

    // Entrée dans la barre de chat = envoyer (sans Shift, pour laisser une éventuelle multi-ligne future).
    private void OnChatKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (DataContext is BotsViewModel vm && vm.SendChatCommand.CanExecute(null)) { vm.SendChatCommand.Execute(null); e.Handled = true; }
    }

    // Copie le texte de l'inspecteur (arbre / JSON / hex selon la vue courante) du paquet sélectionné.
    private async void OnCopyPacket(object? sender, RoutedEventArgs e)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null || DataContext is not BotsViewModel vm || vm.SelectedBot?.InspectorText is not { Length: > 0 } text) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        await clip.SetDataAsync(data);
    }

    private async void OnCopyConsole(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BotsViewModel jvm && jvm.ConsoleJournal) { await CopyLuaRows(JournalConsole); return; }
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null || DataContext is not BotsViewModel vm || vm.SelectedBot is not { } bot) return;
        // Sélection multiple (Ctrl/Shift) -> on copie UNIQUEMENT les lignes choisies (dans l'ordre) ; sinon toute la console.
        var sel = TransportConsole.SelectedItems;
        var lines = sel is { Count: > 0 } ? bot.TransportConsole.Where(l => sel.Contains(l)) : bot.TransportConsole.AsEnumerable();
        var text = vm.ConsoleChat ? string.Join(Environment.NewLine, vm.ChatLines.Select(l => l.Head + l.ReadableText))
            : string.Join(Environment.NewLine, lines);
        if (text.Length == 0) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        await clip.SetDataAsync(data);
    }

    private async void OnCopyLua(object? sender, RoutedEventArgs e) => await CopyLuaRows(LuaConsole);

    // Copie des lignes colorées (console Lua ou journal de l'onglet Console) : sélection, sinon toutes les lignes affichées.
    private async System.Threading.Tasks.Task CopyLuaRows(ListBox list)
    {
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null || DataContext is not BotsViewModel vm) return;
        // Sélection multiple -> uniquement les lignes choisies ; sinon toute la console Lua.
        var sel = list.SelectedItems;
        var rows = sel is { Count: > 0 } ? vm.LuaOutput.Where(r => sel.Contains(r)) : vm.LuaOutput.AsEnumerable();
        var text = string.Join(Environment.NewLine, rows.Select(r => $"{r.Time}  {r.Category,-11} {r.Message}"));
        if (text.Length == 0) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        await clip.SetDataAsync(data);
    }

    // Ctrl+C dans une console -> copie la sélection (mêmes règles que le bouton Copier).
    private void OnConsoleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (ReferenceEquals(sender, LuaConsole)) OnCopyLua(sender, new RoutedEventArgs());
        else if (ReferenceEquals(sender, JournalConsole)) _ = CopyLuaRows(JournalConsole);
        else OnCopyConsole(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    // Poignée sous l'éditeur Lua : ajuste sa hauteur (mémorisée dans la config).
    private void OnLuaEditorResize(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        if (DataContext is BotsViewModel vm) vm.LuaEditorHeight += e.Vector.Y;
    }

    // Double-clic sur une ligne d'erreur de la console Lua : ouvre le script à la ligne fautive.
    private void OnLuaConsoleDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not BotsViewModel vm) return;
        var row = (e.Source as Avalonia.StyledElement)?.DataContext as LuaLogRow;
        if (row is { HasLocation: true }) vm.LuaJumpToLogCommand.Execute(row);
    }

    // Enregistreur de trajet (Road Creator). Démarre : observe les changements de carte manuels (modale récolter/combattre
    // par carte, servie par la VM). Arrête : génère le trajet déclaratif, l'injecte dans l'éditeur Lua ET propose de
    // l'enregistrer en .lua sur le disque (les deux, comme demandé).
    private async void OnToggleRecording(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BotsViewModel vm) return;
        if (!vm.Recording) { vm.StartRecording(); return; }

        string lua = vm.StopRecording();
        if (string.IsNullOrEmpty(lua)) return;

        // (a) Éditeur : le trajet devient le script du perso, éditeur déplié pour relecture immédiate.
        // Annulable (Ctrl+Z) ; détache le fichier ouvert (Ctrl+S ne l'écrasera pas). Refusé pour un suiveur d'équipe.
        if (vm.ApplyGeneratedLua(lua))
        {
            int lines = lua.Count(c => c == '\n') + 1;
            vm.AppendLuaLine("trajet", $"Trajet injecté dans l'éditeur ({lines} lignes). Relis-le puis Lance-le.", LuaLogLevel.Success);
        }

        // (b) Disque : proposer l'enregistrement d'un fichier .lua (annulable, best-effort).
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("L.Bots.Road.SaveTitle"),
            SuggestedFileName = "Trajet",
            DefaultExtension = "lua",
            FileTypeChoices = new[] { new FilePickerFileType("Script Lua") { Patterns = new[] { "*.lua" } } },
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(lua);
            string path = file.TryGetLocalPath() ?? file.Name;
            vm.LuaFileSaved(path, lua);
            vm.AppendLuaLine("trajet", $"Trajet enregistré : {path}.", LuaLogLevel.Success);
        }
        catch (Exception ex)
        {
            vm.AppendLuaLine("trajet", $"Échec de l'enregistrement du trajet : {ex.Message}", LuaLogLevel.Danger);
        }
    }

    // Export CSV des stats de la fenêtre courante (période OU jour précis) du perso sélectionné.
    private async void OnExportStats(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BotsViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("L.Bots.Stats.ExportCsv"),
            SuggestedFileName = vm.Stats.CsvSuggestedName(),
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        if (file is null) return;

        try
        {
            var csv = vm.Stats.BuildCsv();
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true));   // BOM -> Excel FR lit bien les accents
            await writer.WriteAsync(csv);
        }
        catch { /* best-effort : l'utilisateur peut réessayer */ }
    }

    // Export CSV du journal HDV (ventes/taxes) du perso sélectionné, sur la période choisie. Même patron que OnExportStats.
    private async void OnExportHdvStats(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BotsViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("L.Bots.Hdv.ExportCsv"),
            SuggestedFileName = vm.Hdv.Stats.CsvSuggestedName(),
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        if (file is null) return;

        try
        {
            var csv = vm.Hdv.Stats.BuildCsv();
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true));
            await writer.WriteAsync(csv);
        }
        catch { /* best-effort */ }
    }

    private Avalonia.Controls.Primitives.FlyoutBase? _quickFlyout;
    private Avalonia.Controls.Primitives.FlyoutBase? _characterFlyout;

    private void OnQuickTeleportOpened(object? sender, EventArgs e)
    {
        _quickFlyout = sender as Avalonia.Controls.Primitives.FlyoutBase;
        if (DataContext is not BotsViewModel vm) return;
        if (sender is Flyout { Content: StyledElement panel }) panel.DataContext = vm;
        vm.RefreshQuickActions();
    }

    private void OnQuickZaapiOpened(object? sender, EventArgs e)
    {
        OnQuickTeleportOpened(sender, e);
        (DataContext as BotsViewModel)?.RequestZaapiListIfMissing();
    }

    private void OnQuickTeleportClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (sender is Button { Command: { } command } button && command.CanExecute(button.CommandParameter))
                command.Execute(button.CommandParameter);
        }
        finally { _quickFlyout?.Hide(); }
    }

    private void OnCharacterChoicesOpened(object? sender, EventArgs e)
    {
        _characterFlyout = sender as Avalonia.Controls.Primitives.FlyoutBase;
        if (DataContext is not BotsViewModel vm) return;
        if (sender is Flyout { Content: StyledElement panel }) panel.DataContext = vm;
        vm.RebuildCharacterChoices();
    }

    private void OnObservedCharacterClick(object? sender, RoutedEventArgs e)
    {
        // Handled empêche un second déclenchement. La commande part avant la fermeture du menu.
        e.Handled = true;
        try
        {
            if (sender is Button { Command: { } command } button && command.CanExecute(button.CommandParameter))
                command.Execute(button.CommandParameter);
        }
        finally { _characterFlyout?.Hide(); }
    }
}

/// <summary>Texte français de l'état de changement. Pendant l'opération, l'état vivant prime sur un message déjà périmé.</summary>
public sealed class SessionTransitionLiveConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3 || values[0] is not SessionTransitionState state) return "";
        if (values[2] is true) return BotsViewModel.FrenchSessionState(state);
        string message = values[1] as string ?? "";
        string translated = BotsViewModel.FrenchSessionMessage(message);
        return translated.Length > 0 ? translated : BotsViewModel.FrenchSessionState(state);
    }
}

/// <summary>Le texte d'état reste visible après la fin, et disparaît tant qu'aucun changement n'a eu lieu.</summary>
public sealed class SessionTransitionVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SessionTransitionState state && state != SessionTransitionState.Idle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
