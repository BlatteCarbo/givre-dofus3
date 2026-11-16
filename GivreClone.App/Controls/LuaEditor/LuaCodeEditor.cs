using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.Services.LuaEditor;
using GivreClone.Engine.LuaTooling;

namespace GivreClone.App.Controls.LuaEditor;

/// <summary>Commande d'édition relayée par la barre d'outils / les modales vers l'éditeur ACTIF (onglet ou plein écran).</summary>
public enum LuaEditorAction { Format, ToggleComment, Find, Replace, GoToLine, Jump, Insert, InsertSnippet, NextProblem, PreviousProblem, Focus, CloseAssist }

/// <summary>Requête d'édition (voir <see cref="LuaEditorAction"/>).</summary>
public sealed record LuaEditorRequest(LuaEditorAction Action, int Line = 0, int Column = 0, string? Text = null);

/// <summary>Hôte d'un éditeur Lua pour le contrôle (implémenté par BotsViewModel) : document partagé, diagnostics, requêtes.</summary>
public interface ILuaEditorHost
{
    TextDocument LuaDocument { get; }
    IReadOnlyList<LuaDiagnostic> LuaDiagnostics { get; }
    IReadOnlyList<(string Name, bool IsFunction, int Line, string? Args)> LuaDeclarations { get; }
    bool LuaFullscreenOpen { get; }
    int LuaCaretOffset { get; set; }
    event Action? LuaDiagnosticsChanged;
    event Action<LuaEditorRequest>? LuaEditorRequested;
    void LuaSave();
    void LuaSaveAsRequested();
    void LuaRunOrRestart();
    void LuaStop();
    void LuaOpenGoToLine();
}

/// <summary>
/// Éditeur de code Lua (AvaloniaEdit) : coloration de l'API Givre, numéros de ligne, repli des blocs, indentation automatique,
/// complétion de toute l'API (+ bibliothèque standard, mots-clés, noms du script, snippets), aide de signature, fiche au survol,
/// soulignement des erreurs, Ctrl+F / Ctrl+H (recherche intégrée), Ctrl+S, Maj+Alt+F (format), Ctrl+/ (commentaire),
/// Ctrl+G (aller à la ligne), F8 (problème suivant), F5 (lancer / redémarrer), Maj+F5 (arrêter).
/// Le texte vit dans le document du ViewModel (<see cref="ILuaEditorHost.LuaDocument"/>) : partagé avec l'éditeur plein écran.
/// </summary>
public sealed class LuaCodeEditor : UserControl
{
    public static readonly StyledProperty<bool> IsFullscreenProperty = AvaloniaProperty.Register<LuaCodeEditor, bool>(nameof(IsFullscreen));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<LuaCodeEditor, bool>(nameof(IsReadOnly));
    public static readonly DirectProperty<LuaCodeEditor, string> CaretTextProperty =
        AvaloniaProperty.RegisterDirect<LuaCodeEditor, string>(nameof(CaretText), o => o.CaretText);

    /// <summary>Éditeur de la modale plein écran (reçoit les commandes quand elle est ouverte).</summary>
    public bool IsFullscreen { get => GetValue(IsFullscreenProperty); set => SetValue(IsFullscreenProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    private string _caretText = "";
    /// <summary>« Ln 12, Col 5 · 148 lignes » (barre d'état).</summary>
    public string CaretText { get => _caretText; private set => SetAndRaise(CaretTextProperty, ref _caretText, value); }

    public TextEditor Editor { get; }
    private readonly LuaDiagnosticRenderer _diag = new();
    private FoldingManager? _folding;
    private CompletionWindow? _completion;
    private OverloadInsightWindow? _insight;
    private LuaSignatureProvider? _signature;
    private readonly Popup _hover;
    private readonly Border _hoverCard;
    private ILuaEditorHost? _host;
    private readonly DispatcherTimer _foldTimer;
    private readonly DispatcherTimer _flashTimer;
    private int _foldSeq;
    private string _indentUnit = "    ";
    private int _hoverLine;
    private bool _attaching;   // affectation du document en cours : le curseur remis au début n'est pas une position à partager   // ligne d'une fiche ouverte au clavier (F8) ; 0 = fiche de survol souris

    public LuaCodeEditor()
    {
        Editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = LuaDocView.Mono,
            FontSize = 13,
            Background = Brushes.Transparent,
            Foreground = new ImmutableSolidColorBrush(Color.Parse("#D4D7DD")),
            LineNumbersForeground = new ImmutableSolidColorBrush(Color.Parse("#5B6474")),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            WordWrap = false,
            Padding = new Thickness(4, 6, 4, 6),
        };
        var o = Editor.Options;
        o.ConvertTabsToSpaces = true; o.IndentationSize = 4; o.HighlightCurrentLine = true; o.EnableHyperlinks = false;
        o.EnableEmailHyperlinks = false; o.AllowScrollBelowDocument = true; o.CutCopyWholeLine = true; o.EnableRectangularSelection = true;
        Editor.TextArea.TextView.CurrentLineBackground = new ImmutableSolidColorBrush(Color.Parse("#12FFFFFF"));
        Editor.TextArea.TextView.CurrentLineBorder = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#1FFFFFFF")), 1);
        Editor.TextArea.SelectionBrush = new ImmutableSolidColorBrush(Color.Parse("#553B82F6"));
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.IndentationStrategy = new LuaIndentationStrategy(() => _indentUnit);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_diag);
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            UpdateCaretText(); UpdateSignatureOnCaret();
            if (!_attaching && IsActive && _host is not null && Editor.Document is not null) _host.LuaCaretOffset = Editor.CaretOffset;
            // Fiche ouverte au clavier (F8) : se ferme dès que le curseur quitte sa ligne.
            if (_hover.IsOpen && _hoverLine > 0 && Editor.TextArea.Caret.Line != _hoverLine) { _hover.IsOpen = false; _hoverLine = 0; }
        };
        Editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        Editor.PointerHover += OnPointerHover;
        Editor.PointerHoverStopped += (_, _) => _hover!.IsOpen = false;
        Editor.DocumentChanged += (_, _) => OnDocumentSwapped();

        _hoverCard = new Border
        {
            Background = new ImmutableSolidColorBrush(Color.Parse("#1B2029")), BorderBrush = new ImmutableSolidColorBrush(Color.Parse("#343B49")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9), MaxWidth = 500,
            BoxShadow = BoxShadows.Parse("0 6 18 0 #70000000"),
        };
        _hover = new Popup { Child = _hoverCard, Placement = Avalonia.Controls.PlacementMode.Pointer, IsLightDismissEnabled = false, PlacementTarget = Editor };

        _foldTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) => { _foldTimer!.Stop(); UpdateFoldings(); });
        _flashTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1600), DispatcherPriority.Background, (_, _) => { _flashTimer!.Stop(); _diag.FlashLine = 0; Editor.TextArea.TextView.InvalidateLayer(_diag.Layer); });

        var root = new Grid();
        root.Children.Add(Editor);
        root.Children.Add(_hover);
        Content = root;
    }

    // ------------------------------------------------------------------ liaison à l'hôte (ViewModel)

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null) Attach(DataContext as ILuaEditorHost);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attach(DataContext as ILuaEditorHost);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Attach(null);   // la vue Bots est recréée à chaque navigation : aucun abonnement ne doit survivre au contrôle
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsReadOnlyProperty) Editor.IsReadOnly = IsReadOnly;
    }

    private int _savedCaret = -1;
    private double _savedScroll;
    private TextDocument? _savedFor;

    private void Attach(ILuaEditorHost? host)
    {
        if (ReferenceEquals(host, _host)) return;
        if (_host is not null)
        {
            _host.LuaDiagnosticsChanged -= OnDiagnosticsChanged;
            _host.LuaEditorRequested -= OnRequest;
            // Détaché (autre onglet, autre page) : on lâche le document du ViewModel (aucune référence ne survit au contrôle)
            // en retenant curseur et défilement pour les rendre au retour.
            if (Editor.Document is { } d) { _savedFor = d; _savedCaret = Editor.CaretOffset; _savedScroll = Editor.VerticalOffset; }
            CloseAssist();
            Editor.Document = null;
        }
        _host = host;
        if (host is null) return;
        host.LuaDiagnosticsChanged += OnDiagnosticsChanged;
        host.LuaEditorRequested += OnRequest;
        Editor.SyntaxHighlighting ??= LuaLanguage.Highlighting;
        int hostCaret = host.LuaCaretOffset;
        _attaching = true;
        try { Editor.Document = host.LuaDocument; } finally { _attaching = false; }
        if (ReferenceEquals(_savedFor, host.LuaDocument) && _savedCaret >= 0)
        {
            int caret = Math.Min(_savedCaret, host.LuaDocument.TextLength), scroll = (int)_savedScroll;
            Dispatcher.UIThread.Post(() => { Editor.CaretOffset = caret; Editor.ScrollToVerticalOffset(scroll); }, DispatcherPriority.Loaded);
        }
        else
        {
            // Premier affichage (ex. plein écran qu'on ouvre) : reprend la position de l'autre éditeur.
            int caret = Math.Clamp(hostCaret, 0, host.LuaDocument.TextLength);
            Dispatcher.UIThread.Post(() => { if (Editor.Document is null) return; Editor.CaretOffset = caret; Editor.TextArea.Caret.BringCaretToView(); }, DispatcherPriority.Loaded);
        }
        OnDiagnosticsChanged();
    }

    private TextDocument? _watchedDoc;

    private void OnDocumentSwapped()
    {
        if (_folding is not null) { try { FoldingManager.Uninstall(_folding); } catch { } _folding = null; }
        if (_watchedDoc is not null) { _watchedDoc.TextChanged -= OnDocTextChanged; _watchedDoc = null; }
        if (Editor.Document is not { } doc) { _foldTimer.Stop(); return; }
        _folding = FoldingManager.Install(Editor.TextArea);
        // Marqueurs de repli accordés au thème sombre (défaut AvaloniaEdit : carrés blancs).
        foreach (var m in Editor.TextArea.LeftMargins.OfType<FoldingMargin>())
        {
            m.FoldingMarkerBrush = new ImmutableSolidColorBrush(Color.Parse("#6B7485"));
            m.FoldingMarkerBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#171B23"));
            m.SelectedFoldingMarkerBrush = new ImmutableSolidColorBrush(Color.Parse("#C9D1DE"));
            m.SelectedFoldingMarkerBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#232834"));
        }
        doc.TextChanged += OnDocTextChanged;
        _watchedDoc = doc;
        _indentUnit = DetectIndentUnit(doc);
        UpdateFoldings();
        UpdateCaretText();
    }

    private void OnDocTextChanged(object? sender, EventArgs e) { _foldTimer.Stop(); _foldTimer.Start(); UpdateCaretText(); }

    private static string DetectIndentUnit(TextDocument doc)
    {
        try
        {
            var o = LuaFormatter.DetectOptions(doc.TextLength > 200_000 ? doc.GetText(0, 200_000) : doc.Text);
            return o.UseTabs ? "\t" : new string(' ', Math.Clamp(o.IndentSize, 1, 8));
        }
        catch { return "    "; }
    }

    private void OnDiagnosticsChanged()
    {
        _diag.Diagnostics = _host?.LuaDiagnostics ?? Array.Empty<LuaDiagnostic>();
        Editor.TextArea.TextView.InvalidateLayer(_diag.Layer);
    }

    // Seul l'éditeur « actif » exécute les commandes : plein écran s'il est ouvert, sinon celui de l'onglet.
    private bool IsActive => _host is not null && _host.LuaFullscreenOpen == IsFullscreen;

    private void OnRequest(LuaEditorRequest r)
    {
        if (r.Action == LuaEditorAction.CloseAssist)
        {
            // Bascule plein écran <-> onglet : popups fermées ; l'éditeur qui redevient actif reprend la position de l'autre.
            CloseAssist();
            if (IsActive && _host is not null && Editor.Document is { } d)
            {
                Editor.CaretOffset = Math.Clamp(_host.LuaCaretOffset, 0, d.TextLength);
                Dispatcher.UIThread.Post(() => Editor.TextArea.Caret.BringCaretToView(), DispatcherPriority.Loaded);
            }
            return;
        }
        if (!IsActive) return;
        switch (r.Action)
        {
            case LuaEditorAction.Format: Format(); break;
            case LuaEditorAction.ToggleComment: ToggleComment(); break;
            case LuaEditorAction.Find: OpenSearch(replace: false); break;
            case LuaEditorAction.Replace: OpenSearch(replace: true); break;
            case LuaEditorAction.GoToLine:
            case LuaEditorAction.Jump: JumpTo(r.Line, Math.Max(1, r.Column)); break;
            case LuaEditorAction.Insert when r.Text is { } t: InsertAtCaret(t); break;
            case LuaEditorAction.InsertSnippet when r.Text is { } s && !Editor.IsReadOnly: FocusEditor(); LuaSnippetBuilder.Build(s).Insert(Editor.TextArea); break;
            case LuaEditorAction.NextProblem: GoProblem(+1); break;
            case LuaEditorAction.PreviousProblem: GoProblem(-1); break;
            case LuaEditorAction.Focus: FocusEditor(); break;
        }
    }

    private void FocusEditor() => Dispatcher.UIThread.Post(() => Editor.TextArea.Focus(), DispatcherPriority.Input);

    // Recherche intégrée : le champ n'existe qu'après la 1re mise en page du panneau -> focus différé (sinon la saisie part dans
    // le script) ; mode remplacer seulement sur demande ; sélection d'une ligne = motif pré-rempli.
    private void OpenSearch(bool replace)
    {
        var sp = Editor.SearchPanel;
        if (sp is null) return;
        if (!ReferenceEquals(_searchHooked, sp)) { _searchHooked = sp; sp.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(() => WireSearchPanel(sp), DispatcherPriority.Loaded); }
        sp.IsReplaceMode = replace;
        var sel = Editor.TextArea.Selection;
        if (!sel.IsEmpty && !sel.IsMultiline) sp.SearchPattern = Editor.SelectedText;
        sp.Open();
        Dispatcher.UIThread.Post(() => { WireSearchPanel(sp); sp.Reactivate(); }, DispatcherPriority.Loaded);
    }

    // Boutons du panneau de recherche d'AvaloniaEdit : ses commandes « routées » évaluent CanExecute UNE seule fois, sur le
    // dernier élément focalisé de TOUTE l'app (souvent notre bouton de barre d'outils) et ne se réévaluent jamais -> flèches,
    // remplacer et fermer restaient inertes. On retire ces commandes et on appelle directement le panneau. Textes en français.
    private SearchPanel? _searchHooked, _searchWired;
    private void WireSearchPanel(SearchPanel sp)
    {
        if (ReferenceEquals(_searchWired, sp)) return;
        var visuals = sp.GetVisualDescendants().ToList();
        var buttons = visuals.OfType<Button>().Where(b => b.Command is not null).ToList();
        if (buttons.Count == 0) return;   // modèle pas encore appliqué : réessayé à l'application du modèle
        // Options (casse, mot entier, regex) et bascule du remplacement : infobulles en français.
        foreach (var t in visuals.OfType<Avalonia.Controls.Primitives.ToggleButton>())
        {
            string? key = (ToolTip.GetTip(t) switch { TextBlock tb => tb.Text, var o => o?.ToString() }) switch
            {
                "Match case" => "L.Lua.Editor.SearchMatchCase",
                "Match whole words" => "L.Lua.Editor.SearchWholeWords",
                "Use regular expressions" => "L.Lua.Editor.SearchRegex",
                _ => ToolTip.GetTip(t) is TextBlock ? "L.Lua.Editor.SearchToggleReplace" : null,
            };
            if (key is not null) ToolTip.SetTip(t, Loc.T(key));
        }
        foreach (var b in buttons)
        {
            var cmd = b.Command;
            Action? act = null; string? tip = null;
            if (ReferenceEquals(cmd, SearchCommands.FindNext)) { act = () => sp.FindNext(); tip = "L.Lua.Editor.SearchNext"; }
            else if (ReferenceEquals(cmd, SearchCommands.FindPrevious)) { act = sp.FindPrevious; tip = "L.Lua.Editor.SearchPrev"; }
            else if (ReferenceEquals(cmd, SearchCommands.ReplaceNext)) { act = sp.ReplaceNext; tip = "L.Lua.Editor.SearchReplaceOne"; }
            else if (ReferenceEquals(cmd, SearchCommands.ReplaceAll)) { act = sp.ReplaceAll; tip = "L.Lua.Editor.SearchReplaceAll"; }
            else if (ReferenceEquals(cmd, SearchCommands.CloseSearchPanel)) { act = () => { sp.Close(); FocusEditor(); }; tip = "L.Lua.Editor.SearchClose"; }
            if (act is null) continue;
            b.Command = null;
            b.IsEnabled = true;
            b.Click += (_, e) => { act(); e.Handled = true; };
            ToolTip.SetTip(b, Loc.T(tip!));
        }
        foreach (var tb in visuals.OfType<TextBox>())
        {
            if (tb.Name == "PART_searchTextBox") tb.PlaceholderText = Loc.T("L.Lua.Editor.SearchFind");
            else if (tb.Name == "PART_replaceTextBox") tb.PlaceholderText = Loc.T("L.Lua.Editor.SearchReplace");
        }
        _searchWired = sp;
    }

    /// <summary>Place le curseur à (ligne, colonne 1-based), fait défiler et met la ligne en évidence un instant.</summary>
    public void JumpTo(int line, int column)
    {
        var doc = Editor.Document;
        if (doc is null || doc.LineCount == 0) return;
        line = Math.Clamp(line, 1, doc.LineCount);
        var dl = doc.GetLineByNumber(line);
        column = Math.Clamp(column, 1, dl.Length + 1);
        Editor.TextArea.ClearSelection();
        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = column;
        Editor.ScrollTo(line, column);
        Editor.TextArea.Caret.BringCaretToView();
        _diag.FlashLine = line;
        Editor.TextArea.TextView.InvalidateLayer(_diag.Layer);
        _flashTimer.Stop(); _flashTimer.Start();
        FocusEditor();
    }

    private void InsertAtCaret(string text)
    {
        if (Editor.IsReadOnly || Editor.Document is null) return;
        var seg = Editor.TextArea.Selection.IsEmpty ? null : Editor.TextArea.Selection.SurroundingSegment;
        if (seg is not null) Editor.Document.Replace(seg, text);
        else Editor.Document.Insert(Editor.CaretOffset, text);
        FocusEditor();
    }

    // ------------------------------------------------------------------ format / commentaire / problèmes

    /// <summary>Reformate tout le script (indentation par blocs) en gardant le curseur sur la même ligne. Annulable (Ctrl+Z).</summary>
    public void Format()
    {
        var doc = Editor.Document;
        if (doc is null || Editor.IsReadOnly) return;
        string before = doc.Text;
        string after;
        try { after = LuaFormatter.Format(before, LuaFormatter.DetectOptions(before)); }
        catch { return; }
        if (after == before) return;
        int line = Editor.TextArea.Caret.Line, col = Editor.TextArea.Caret.Column;
        using (doc.RunUpdate()) doc.Replace(0, doc.TextLength, after);
        line = Math.Clamp(line, 1, doc.LineCount);
        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = Math.Clamp(col, 1, doc.GetLineByNumber(line).Length + 1);
        Editor.TextArea.Caret.BringCaretToView();
    }

    /// <summary>Commente / décommente (« -- ») les lignes sélectionnées ou la ligne courante.</summary>
    public void ToggleComment()
    {
        var doc = Editor.Document;
        if (doc is null || Editor.IsReadOnly) return;
        int startLine, endLine;
        if (Editor.TextArea.Selection.IsEmpty) startLine = endLine = Editor.TextArea.Caret.Line;
        else
        {
            var seg = Editor.TextArea.Selection.SurroundingSegment;
            startLine = doc.GetLineByOffset(seg.Offset).LineNumber;
            var endL = doc.GetLineByOffset(seg.EndOffset);
            endLine = endL.LineNumber - (seg.EndOffset == endL.Offset && endL.LineNumber > startLine ? 1 : 0);
        }
        var lines = Enumerable.Range(startLine, endLine - startLine + 1).Select(doc.GetLineByNumber).ToList();
        bool allCommented = lines.Where(l => doc.GetText(l).Trim().Length > 0).All(l => doc.GetText(l).TrimStart().StartsWith("--", StringComparison.Ordinal));
        int minIndent = lines.Where(l => doc.GetText(l).Trim().Length > 0).Select(l => { var t = doc.GetText(l); return t.Length - t.TrimStart().Length; }).DefaultIfEmpty(0).Min();
        using (doc.RunUpdate())
        {
            foreach (var l in lines.AsEnumerable().Reverse())
            {
                string t = doc.GetText(l);
                if (t.Trim().Length == 0) continue;
                if (allCommented)
                {
                    int i = t.IndexOf("--", StringComparison.Ordinal);
                    int len = i + 2 < t.Length && t[i + 2] == ' ' ? 3 : 2;
                    doc.Remove(l.Offset + i, len);
                }
                else doc.Insert(l.Offset + Math.Min(minIndent, t.Length), "-- ");
            }
        }
    }

    private void GoProblem(int dir)
    {
        var diags = _host?.LuaDiagnostics;
        if (diags is null || diags.Count == 0) return;
        int line = Editor.TextArea.Caret.Line, col = Editor.TextArea.Caret.Column - 1;
        LuaDiagnostic? pick = dir > 0
            ? diags.FirstOrDefault(d => d.Line > line || (d.Line == line && d.Column > col)) ?? diags[0]
            : diags.LastOrDefault(d => d.Line < line || (d.Line == line && d.Column < col)) ?? diags[^1];
        JumpTo(pick.Line, pick.Column + 1);
        ShowHoverCard(LuaDocView.DiagnosticCard(pick), atCaret: true);
        _hoverLine = pick.Line;
    }

    // ------------------------------------------------------------------ clavier

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        var m = e.KeyModifiers;
        bool ctrl = m.HasFlag(KeyModifiers.Control), shift = m.HasFlag(KeyModifiers.Shift), alt = m.HasFlag(KeyModifiers.Alt);
        // Frappe dans le panneau de recherche : seuls enregistrer / lancer / arrêter restent actifs (pas de format, commentaire,
        // complétion… appliqués au script depuis le champ de recherche).
        if (e.Source is TextBox && !(e.Key == Key.S && ctrl) && e.Key != Key.F5) return;
        switch (e.Key)
        {
            case Key.S when ctrl && shift: _host?.LuaSaveAsRequested(); e.Handled = true; break;
            case Key.S when ctrl: _host?.LuaSave(); e.Handled = true; break;
            case Key.F when shift && alt: Format(); e.Handled = true; break;
            case Key.Oem2 when ctrl:
            case Key.Divide when ctrl: ToggleComment(); e.Handled = true; break;
            case Key.G when ctrl: _host?.LuaOpenGoToLine(); e.Handled = true; break;
            case Key.H when ctrl: OpenSearch(replace: true); e.Handled = true; break;
            case Key.F when ctrl && !shift && !alt: OpenSearch(replace: false); e.Handled = true; break;
            case Key.Space when ctrl && shift: ShowSignatureAtCaret(force: true); e.Handled = true; break;
            case Key.Space when ctrl: ShowCompletion(explicitRequest: true); e.Handled = true; break;
            case Key.F8: GoProblem(shift ? -1 : +1); e.Handled = true; break;
            case Key.F5 when shift: _host?.LuaStop(); e.Handled = true; break;
            case Key.F5: _host?.LuaRunOrRestart(); e.Handled = true; break;
            case Key.Escape when _hover.IsOpen: _hover.IsOpen = false; _hoverLine = 0; e.Handled = true; break;
        }
    }

    // ------------------------------------------------------------------ complétion & signature

    // Parenthèses insérées par la complétion : « ) » tapé devant la « ) » automatique la saute ; « ( » tapé juste après un
    // « nom() » sans argument rentre dans les parenthèses (pas de « getPods()() »).
    private readonly List<TextAnchor> _autoClose = new();   // « ) » automatiques en attente (appels imbriqués : une par niveau)
    private TextAnchor? _autoZeroArgEnd;
    internal void NoteAutoParens(int closeParenOffset, bool zeroArg)
    {
        var doc = Editor.Document;
        if (doc is null) return;
        _autoClose.RemoveAll(a => a.IsDeleted);
        if (zeroArg) _autoZeroArgEnd = doc.CreateAnchor(closeParenOffset + 1);   // « nom() » sans argument : ne touche pas aux appels englobants
        else { _autoClose.Add(doc.CreateAnchor(closeParenOffset)); _autoZeroArgEnd = null; }
    }

    private string TypedPrefix(CompletionWindow w)
    {
        int caret = Editor.CaretOffset;
        return caret > w.StartOffset ? Editor.Document.GetText(w.StartOffset, caret - w.StartOffset) : "";
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: > 0 } t || Editor.IsReadOnly) return;
        char c = t[0];
        int caret = Editor.CaretOffset;
        if (c == ')' && caret < Editor.Document.TextLength && Editor.Document.GetCharAt(caret) == ')'
            && _autoClose.FindLastIndex(a => !a.IsDeleted && a.Offset == caret) is var ai && ai >= 0)
        { _autoClose.RemoveAt(ai); Editor.CaretOffset = caret + 1; e.Handled = true; UpdateSignatureOnCaret(); return; }
        if (c == '(' && _autoZeroArgEnd is { IsDeleted: false } za && za.Offset == caret && caret >= 2 && Editor.Document.GetText(caret - 2, 2) == "()")
        { _autoZeroArgEnd = null; Editor.CaretOffset = caret - 1; _autoClose.Add(Editor.Document.CreateAnchor(caret - 1)); e.Handled = true; return; }
        if (_completion is { } w && !IsIdentChar(c))
        {
            // Seul « ( » valide, et seulement si ce qui est tapé est le DÉBUT exact du nom choisi (jamais un snippet) ; espace, =,
            // virgule, « . », « : »… ferment la liste et laissent le texte tel qu'il a été tapé.
            string typed = TypedPrefix(w);
            if (c == '(' && typed.Length > 0 && w.CompletionList.SelectedItem is LuaCompletionItem { Snippet: null } sel
                && sel.Text.StartsWith(typed, StringComparison.Ordinal))
                w.CompletionList.RequestInsertion(e);
            else w.Close();
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: 1 } t || Editor.IsReadOnly) return;
        char c = t[0];
        if (!IsIdentChar(c)) ReindentAfterToken(c);
        if (c == '(') { ShowSignatureAtCaret(force: false); return; }
        if (c == ',') { ShowSignatureAtCaret(force: false); return; }
        if (c == ')') { UpdateSignatureOnCaret(); return; }
        if (c is ':' or '.') { ShowCompletion(explicitRequest: false, memberTrigger: true); return; }
        if (!IsIdentChar(c)) return;
        if (_completion is null) ShowCompletion(explicitRequest: false);
        // Mot-clé complet (then, do, end, else…) : la liste se ferme -> Entrée passe à la ligne (pas besoin de la taper deux fois).
        else if (_completion.CloseWhenCaretAtBeginning && LuaLexer.IsKeyword(TypedPrefix(_completion))) _completion.Close();
    }

    // Mot-clé de fin de bloc COMPLET en début de ligne (« end » suivi d'un espace / « ) » / « , »…, ou « } » / « ) » seuls) :
    // la ligne se ré-aligne sur son bloc. Jamais sur un identifiant qui commence pareil (endCell, ending…).
    private void ReindentAfterToken(char typed)
    {
        var doc = Editor.Document;
        var line = doc.GetLineByOffset(Editor.CaretOffset);
        // Texte de la ligne jusqu'au curseur (sans l'indentation), caractère qui vient d'être tapé compris.
        string before = doc.GetText(line.Offset, Editor.CaretOffset - line.Offset).TrimStart();
        if (before.Length == 0) return;
        bool closer = before.Length == 1 && typed is '}' or ')';
        bool keyword = before[..^1] is "end" or "else" or "elseif" or "until";   // le mot est complet : on vient de taper le caractère qui le suit
        if (!closer && !keyword) return;
        LuaEditorText.ReindentDedentLine(doc, line, _indentUnit);
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // Fenêtre de texte avant le curseur (au plus ~12 000 caractères, alignée sur un début de ligne) : l'analyse de contexte
    // n'a pas besoin du document entier (gros scripts : pas d'allocation complète à chaque frappe).
    private (string Text, int Base) Window(int caret, int before = 12_000)
    {
        var doc = Editor.Document;
        int start = Math.Max(0, caret - before);
        if (start > 0) start = doc.GetLineByOffset(start).Offset;
        return (doc.GetText(start, caret - start), start);
    }

    private void ShowCompletion(bool explicitRequest, bool memberTrigger = false)
    {
        if (Editor.IsReadOnly || _host is null) return;
        int caret = Editor.CaretOffset;
        var (text, basis) = Window(caret);
        var ctx = LuaCallContext.CompletionAt(text, text.Length);
        if (ctx is null) return;
        var (qual, sep, prefix, prefixStart) = ctx.Value;
        if (!explicitRequest && !memberTrigger && qual is null && prefix.Length < 2) return;
        if (memberTrigger && qual is null) return;

        var cat = LuaLanguage.Catalog;
        var items = new List<LuaCompletionItem>();
        if (qual is not null)
        {
            var members = cat.MembersOfNamespace(qual);
            if (members.Count == 0) return;
            foreach (var g in members.GroupBy(m => m.Name, StringComparer.Ordinal))
                items.Add(new LuaCompletionItem(g.First(), g.ToList(), 1));
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dl = Editor.Document.GetLineByOffset(caret);
            string lineBefore = Editor.Document.GetText(dl.Offset, caret - dl.Offset);
            string beforePrefix = prefix.Length <= lineBefore.Length ? lineBefore[..^prefix.Length] : lineBefore;
            bool afterFunction = beforePrefix.TrimEnd().EndsWith("function", StringComparison.Ordinal);
            foreach (var g in cat.Globals.GroupBy(e => e.Name, StringComparer.Ordinal))
            {
                var first = g.First();
                double prio = first.Kind switch { LuaApiKind.Callback => afterFunction ? 4 : 0.5, LuaApiKind.Function => 2, LuaApiKind.Namespace => 1.5, LuaApiKind.Constant => 1.2, _ => 1 };
                if (seen.Add(first.Name)) items.Add(new LuaCompletionItem(first, g.ToList(), prio));
            }
            foreach (var d in _host.LuaDeclarations)
                if (seen.Add(d.Name)) items.Add(new LuaCompletionItem(d.Name, LuaApiKind.Local, (d.IsFunction ? "fonction du script" : "variable du script") + $" · ligne {d.Line}", 3));
            foreach (var k in LuaLexer.Keywords)
                if (seen.Add(k)) items.Add(new LuaCompletionItem(k, LuaApiKind.Keyword, "mot-clé", 0.8));
            foreach (var s in LuaTemplates.Snippets)
                items.Add(new LuaCompletionItem(s, 0.7));   // sous le mot-clé de même nom : « if » + Entrée = le mot, pas le snippet
        }
        if (items.Count == 0) return;
        if (prefix.Length > 0 && !items.Any(i => i.Text.Contains(prefix, StringComparison.OrdinalIgnoreCase))) return;   // rien à proposer : pas de liste vide
        if (!explicitRequest && qual is null && LuaLexer.IsKeyword(prefix)) return;   // mot-clé déjà complet

        _completion?.Close();
        var w = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = basis + prefixStart,
            CloseWhenCaretAtBeginning = qual is null,
            CloseAutomatically = true,
            Width = 460,
        };
        w.CompletionList.IsFiltering = true;
        foreach (var i in items.OrderByDescending(i => i.Priority).ThenBy(i => i.Text, StringComparer.OrdinalIgnoreCase))
        {
            i.ParensInserted = NoteAutoParens;
            w.CompletionList.CompletionData.Add(i);
        }
        w.Closed += (_, _) => { if (ReferenceEquals(_completion, w)) _completion = null; };
        w.CompletionList.InsertionRequested += (_, _) =>
        {
            // Après l'insertion d'une fonction à arguments (« nom() », curseur dedans) : aide de signature immédiate.
            if (w.CompletionList.SelectedItem is LuaCompletionItem { CallTarget: not null })
                Dispatcher.UIThread.Post(() => ShowSignatureAtCaret(false), DispatcherPriority.Background);
        };
        _completion = w;
        w.Show();
        if (prefix.Length > 0) w.CompletionList.SelectItem(prefix);
    }

    private void ShowSignatureAtCaret(bool force)
    {
        if (_host is null) return;
        int caret = Editor.CaretOffset;
        var (text, _) = Window(caret, 6000);
        var call = LuaCallContext.At(text, text.Length);
        if (call is null) { if (force) CloseSignature(); return; }
        var (ns, name, arg, _) = call.Value;
        var overloads = LuaLanguage.Catalog.Overloads(ns, name);
        if (overloads.Count == 0) { CloseSignature(); return; }
        if (_insight is not null && _signature is not null && _signature.Target == (overloads[0].Namespace, overloads[0].Name))
        { _signature.ArgIndex = arg; return; }
        CloseSignature();
        _signature = new LuaSignatureProvider(overloads, arg);
        var w = new OverloadInsightWindow(Editor.TextArea) { Provider = _signature, MaxWidth = 560 };
        w.Closed += (_, _) => { if (ReferenceEquals(_insight, w)) { _insight = null; _signature = null; } };
        _insight = w;
        w.Show();
    }

    private void UpdateSignatureOnCaret()
    {
        if (_insight is null || _signature is null) return;
        int caret = Editor.CaretOffset;
        var (text, _) = Window(caret, 6000);
        var call = LuaCallContext.At(text, text.Length);
        if (call is null) { CloseSignature(); return; }
        var (ns, name, arg, _) = call.Value;
        var ov = LuaLanguage.Catalog.Overloads(ns, name);
        if (ov.Count == 0 || _signature.Target != (ov[0].Namespace, ov[0].Name)) { CloseSignature(); if (ov.Count > 0) ShowSignatureAtCaret(false); return; }
        _signature.ArgIndex = arg;
    }

    private void CloseSignature() { _insight?.Close(); _insight = null; _signature = null; }
    private void CloseAssist() { _completion?.Close(); _completion = null; CloseSignature(); _hover.IsOpen = false; }

    // ------------------------------------------------------------------ survol

    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        var doc = Editor.Document;
        if (doc is null || _completion is not null) return;
        var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (pos is null) return;
        int offset = doc.GetOffset(pos.Value.Location);
        int line = pos.Value.Line, col0 = pos.Value.Column - 1;
        // 1) Diagnostic sous la souris (priorité : c'est ce que l'utilisateur regarde).
        var diag = _host?.LuaDiagnostics.FirstOrDefault(d =>
            LuaEditorGeometry.TrySegment(doc, d, out var seg) && offset >= seg.StartOffset && offset <= seg.EndOffset);
        if (diag is not null) { ShowHoverCard(LuaDocView.DiagnosticCard(diag)); e.Handled = true; return; }
        // 2) Symbole de l'API / de la bibliothèque standard.
        var dl = doc.GetLineByNumber(line);
        int from = Math.Max(dl.Offset, offset - 200), to = Math.Min(dl.EndOffset, offset + 200);
        string snippet = doc.GetText(from, to - from);
        var sym = LuaCallContext.SymbolAt(snippet, offset - from);
        if (sym is null) return;
        var (ns, name, _, _) = sym.Value;
        var ov = LuaLanguage.Catalog.Overloads(ns, name);
        if (ov.Count == 0 && ns is null)
        {
            var decl = _host?.LuaDeclarations.FirstOrDefault(d => d.Name == name);
            if (decl is { Name: not null } dd && dd.Name.Length > 0)
            {
                ShowHoverCard(LuaDocView.Text((dd.IsFunction ? "fonction " : "variable ") + dd.Name + (dd.Args is { } a ? "(" + a + ")" : "")
                                              + "  ·  " + Loc.Fmt("L.Lua.Editor.DeclaredAt", dd.Line), Brushes.Gainsboro, 12.5));
                e.Handled = true;
            }
            return;
        }
        if (ov.Count == 0) return;
        ShowHoverCard(LuaDocView.Card(ov));
        e.Handled = true;
    }

    private void ShowHoverCard(Control content, bool atCaret = false)
    {
        _hoverCard.Child = content;
        if (atCaret)
        {
            var loc = Editor.TextArea.TextView.GetVisualPosition(Editor.TextArea.Caret.Position, AvaloniaEdit.Rendering.VisualYPosition.LineBottom)
                      - Editor.TextArea.TextView.ScrollOffset;
            _hover.Placement = Avalonia.Controls.PlacementMode.AnchorAndGravity;
            _hover.PlacementTarget = Editor.TextArea.TextView;
            _hover.PlacementRect = new Rect(loc.X, loc.Y, 1, 1);
            _hover.PlacementAnchor = PopupAnchor.TopLeft;
            _hover.PlacementGravity = PopupGravity.BottomRight;
        }
        else
        {
            _hover.Placement = Avalonia.Controls.PlacementMode.Pointer;
            _hover.PlacementTarget = Editor;
            _hover.PlacementRect = null;
        }
        _hover.IsOpen = false;
        _hover.IsOpen = true;
    }

    // ------------------------------------------------------------------ repli des blocs / barre d'état

    private void UpdateFoldings()
    {
        var doc = Editor.Document;
        if (_folding is null || doc is null) return;
        string text = doc.Text;
        int seq = ++_foldSeq;
        Task.Run(() =>
        {
            IReadOnlyList<LuaFold> folds;
            try { folds = LuaFolding.Compute(text); } catch { return; }
            Dispatcher.UIThread.Post(() =>
            {
                if (seq != _foldSeq || _folding is null || !ReferenceEquals(doc, Editor.Document)) return;
                int len = doc.TextLength;
                var list = folds.Where(f => f.StartOffset >= 0 && f.EndOffset <= len && f.EndOffset > f.StartOffset)
                                .OrderBy(f => f.StartOffset)
                                .Select(f => new NewFolding(f.StartOffset, f.EndOffset) { Name = string.IsNullOrEmpty(f.Title) ? "…" : f.Title }).ToList();
                try { _folding.UpdateFoldings(list, -1); } catch { /* document modifié entre-temps : prochain passage */ }
            });
        });
    }

    private void UpdateCaretText()
    {
        var doc = Editor.Document;
        if (doc is null) { CaretText = ""; return; }
        var c = Editor.TextArea.Caret;
        CaretText = Loc.Fmt("L.Lua.Editor.Caret", c.Line, c.Column, doc.LineCount);
    }
}
