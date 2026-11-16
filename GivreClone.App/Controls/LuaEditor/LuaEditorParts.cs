using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Indentation;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Snippets;
using GivreClone.App.Services;
using GivreClone.App.Services.LuaEditor;
using GivreClone.Engine.LuaTooling;

namespace GivreClone.App.Controls.LuaEditor;

/// <summary>Rendu partagé (fiches de doc) : complétion, survol, référence de l'API.</summary>
internal static class LuaDocView
{
    private static readonly IBrush Primary = new ImmutableSolidColorBrush(Color.Parse("#E6E9EF"));
    private static readonly IBrush Secondary = new ImmutableSolidColorBrush(Color.Parse("#A9B1C0"));
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#7C8595"));
    private static readonly IBrush CodeBg = new ImmutableSolidColorBrush(Color.Parse("#11141A"));
    private static readonly IBrush Warn = new ImmutableSolidColorBrush(Color.Parse("#E8B339"));
    private static readonly IBrush Err = new ImmutableSolidColorBrush(Color.Parse("#FF6B7A"));
    private static readonly IBrush Info = new ImmutableSolidColorBrush(Color.Parse("#79C0FF"));
    internal static readonly FontFamily Mono = new("Cascadia Mono,Cascadia Code,Consolas,Menlo,monospace");

    public static IBrush KindBrush(LuaApiKind k) => new ImmutableSolidColorBrush(Color.Parse(k switch
    {
        LuaApiKind.Function => LuaLanguage.ColFunction,
        LuaApiKind.Callback => LuaLanguage.ColCallback,
        LuaApiKind.Constant => LuaLanguage.ColConstant,
        LuaApiKind.Namespace => LuaLanguage.ColNamespace,
        LuaApiKind.Keyword => LuaLanguage.ColKeyword,
        LuaApiKind.StdLib => LuaLanguage.ColStdLib,
        LuaApiKind.Snippet => "#B39DDB",
        _ => "#A9B1C0",
    }));

    public static string KindGlyph(LuaApiKind k) => k switch
    {
        LuaApiKind.Function => "ƒ", LuaApiKind.Callback => "⚡", LuaApiKind.Constant => "C", LuaApiKind.Namespace => "M",
        LuaApiKind.Keyword => "k", LuaApiKind.StdLib => "s", LuaApiKind.Snippet => "✦", _ => "x",
    };

    /// <summary>Signature affichable d'une entrée (« map:moveToward(mapId | x, y) → bool »).</summary>
    public static string Signature(LuaApiEntry e) => e.Display;

    /// <summary>Fiche complète : signatures, badges, description, exemple.</summary>
    public static Control Card(IReadOnlyList<LuaApiEntry> overloads, int highlightArg = -1, double maxWidth = 460)
    {
        var e = overloads[0];
        var panel = new StackPanel { Spacing = 6, MaxWidth = maxWidth };
        foreach (var o in overloads.Take(8)) panel.Children.Add(SignatureBlock(o, highlightArg));
        if (overloads.Count > 8) panel.Children.Add(Text($"+ {overloads.Count - 8} autres signatures", Muted, 11));
        var badges = e.Badges.Select(BadgeLabel).Where(b => b is not null).ToList();
        if (badges.Count > 0) panel.Children.Add(Text(string.Join("  ·  ", badges), Warn, 11));
        if (!string.IsNullOrWhiteSpace(e.Description)) panel.Children.Add(Text(e.Description, Secondary, 12.5, wrap: true));
        if (!string.IsNullOrWhiteSpace(e.Example))
            panel.Children.Add(new Border
            {
                Background = CodeBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6),
                Child = new TextBlock { Text = e.Example!.TrimEnd(), FontFamily = Mono, FontSize = 11.5, Foreground = Primary, TextWrapping = TextWrapping.Wrap },
            });
        if (!string.IsNullOrEmpty(e.CategoryTitle)) panel.Children.Add(Text(e.CategoryTitle, Muted, 10.5));
        return panel;
    }

    internal static Control SignatureBlock(LuaApiEntry e, int highlightArg)
    {
        var tb = new TextBlock { FontFamily = Mono, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Primary };
        var inl = tb.Inlines = new InlineCollection();
        if (e.Kind is LuaApiKind.Function or LuaApiKind.Callback or LuaApiKind.StdLib && e.Params is not null && (e.Args is not null || e.Params.Count > 0))
        {
            string head = (e.Namespace is { } ns ? ns + (e.Kind == LuaApiKind.StdLib ? "." : ":") : "") + e.Name;
            inl.Add(new Run(head) { Foreground = KindBrush(e.Kind), FontWeight = FontWeight.SemiBold });
            inl.Add(new Run("("));
            if (e.Params.Count > 0)
            {
                for (int i = 0; i < e.Params.Count; i++)
                {
                    if (i > 0) inl.Add(new Run(", "));
                    var p = e.Params[i];
                    string label = (p.Optional ? "[" : "") + p.Name + (p.Default is { } d ? "=" + d : "") + (p.Optional ? "]" : "");
                    var run = new Run(label);
                    if (i == highlightArg) { run.FontWeight = FontWeight.Bold; run.Foreground = new ImmutableSolidColorBrush(Color.Parse(LuaLanguage.ColConstant)); run.TextDecorations = TextDecorations.Underline; }
                    inl.Add(run);
                }
            }
            else if (!string.IsNullOrEmpty(e.Args)) inl.Add(new Run(e.Args));
            inl.Add(new Run(")"));
            if (!string.IsNullOrEmpty(e.Returns)) { inl.Add(new Run("  →  ") { Foreground = Muted }); inl.Add(new Run(e.Returns) { Foreground = Info }); }
        }
        else inl.Add(new Run(e.Display) { Foreground = KindBrush(e.Kind), FontWeight = FontWeight.SemiBold });
        return tb;
    }

    private static string? BadgeLabel(string b) => b switch
    {
        "blocking" => "⏳ bloquant (attend la fin de l'action)",
        _ when b.StartsWith("perm:", StringComparison.Ordinal) => "🔒 droit « " + b[5..] + " »",
        _ => null,
    };

    public static TextBlock Text(string s, IBrush fg, double size, bool wrap = false, FontWeight? weight = null)
        => new() { Text = s, Foreground = fg, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, FontWeight = weight ?? FontWeight.Normal };

    /// <summary>Fiche d'un diagnostic (survol d'un soulignement).</summary>
    public static Control DiagnosticCard(LuaDiagnostic d)
    {
        var p = new StackPanel { Spacing = 4, MaxWidth = 460 };
        var brush = d.Severity switch { LuaDiagnosticSeverity.Error => Err, LuaDiagnosticSeverity.Warning => Warn, _ => Info };
        string icon = d.Severity switch { LuaDiagnosticSeverity.Error => "✖", LuaDiagnosticSeverity.Warning => "⚠", _ => "ℹ" };
        p.Children.Add(Text($"{icon}  {d.Message}", brush, 12.5, wrap: true, FontWeight.SemiBold));
        if (!string.IsNullOrWhiteSpace(d.Hint)) p.Children.Add(Text(d.Hint!, Secondary, 12, wrap: true));
        p.Children.Add(Text(Loc.Fmt("L.Lua.Editor.LineCol", d.Line, d.Column + 1), Muted, 10.5));
        return p;
    }
}

/// <summary>Élément de la liste de complétion (API, bibliothèque standard, mot-clé, nom du script, snippet).</summary>
internal sealed class LuaCompletionItem : ICompletionData
{
    private readonly LuaApiEntry? _entry;
    private readonly IReadOnlyList<LuaApiEntry>? _overloads;
    private readonly LuaSnippet? _snippet;
    private readonly string? _detail;
    private readonly LuaApiKind _kind;

    public LuaCompletionItem(LuaApiEntry entry, IReadOnlyList<LuaApiEntry> overloads, double priority)
    { _entry = entry; _overloads = overloads; _kind = entry.Kind; Text = entry.Name; Priority = priority; }
    public LuaCompletionItem(string name, LuaApiKind kind, string? detail, double priority)
    { Text = name; _kind = kind; _detail = detail; Priority = priority; }
    public LuaCompletionItem(LuaSnippet snippet, double priority)
    { _snippet = snippet; _kind = LuaApiKind.Snippet; Text = snippet.Trigger; _detail = snippet.Title; Priority = priority; }

    /// <summary>Nom de fonction appelée (pour ouvrir l'aide de signature juste après l'insertion) : (ns, nom).</summary>
    // Appelable : fonction de l'API (même sans argument : getPods()) ou fonction de la bibliothèque standard (Args null = valeur, ex. math.pi).
    private bool IsCallable => _entry is { Kind: LuaApiKind.Function } || _entry is { Kind: LuaApiKind.StdLib, Args: not null };
    /// <summary>Fonction À ARGUMENTS appelée (aide de signature juste après l'insertion) : (ns, nom).</summary>
    public (string? Ns, string Name)? CallTarget => IsCallable && _entry!.Params.Count > 0 ? (_entry.Namespace, _entry.Name) : null;
    public bool IsCallback => _kind == LuaApiKind.Callback;
    public LuaApiEntry? Entry => _entry;
    public LuaSnippet? Snippet => _snippet;
    /// <summary>Rappel de l'éditeur : « () » insérés (offset de la « ) », sans argument ?) -> saut de parenthèse à la frappe.</summary>
    internal Action<int, bool>? ParensInserted { get; set; }

    public IImage? Image => null;
    public string Text { get; }
    public double Priority { get; }

    public object Content
    {
        get
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("18,Auto,*"), MinWidth = 280 };
            g.Children.Add(new TextBlock { Text = LuaDocView.KindGlyph(_kind), Foreground = LuaDocView.KindBrush(_kind), FontWeight = FontWeight.Bold,
                                           HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = Text, FontFamily = LuaDocView.Mono, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
            Grid.SetColumn(name, 1); g.Children.Add(name);
            string? dim = _entry is { } e ? Tail(e) : _detail;
            if (!string.IsNullOrEmpty(dim))
            {
                var d = new TextBlock { Text = dim, Opacity = 0.55, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis,
                                        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 260 };
                Grid.SetColumn(d, 2); g.Children.Add(d);
            }
            return g;
        }
    }

    private static string? Tail(LuaApiEntry e) => e.Kind switch
    {
        LuaApiKind.Constant => e.ConstType is { } t ? t + (e.ConstDefault is { } d ? " = " + d : "") : null,
        LuaApiKind.Namespace => "module",
        _ => "(" + (e.Args ?? string.Join(", ", e.Params.Select(p => p.Optional ? "[" + p.Name + "]" : p.Name))) + ")" + (string.IsNullOrEmpty(e.Returns) ? "" : " → " + e.Returns),
    };

    public object? Description
    {
        get
        {
            if (_overloads is { Count: > 0 }) return LuaDocView.Card(_overloads, maxWidth: 420);
            if (_snippet is { } s)
            {
                var p = new StackPanel { Spacing = 6, MaxWidth = 420 };
                p.Children.Add(LuaDocView.Text(s.Title, LuaDocView.KindBrush(LuaApiKind.Snippet), 12.5, weight: FontWeight.SemiBold));
                if (!string.IsNullOrWhiteSpace(s.Description)) p.Children.Add(LuaDocView.Text(s.Description, Brushes.Gainsboro, 12, wrap: true));
                p.Children.Add(new TextBlock { Text = LuaSnippetBuilder.Preview(s.Body), FontFamily = LuaDocView.Mono, FontSize = 11.5, Opacity = 0.9 });
                return p;
            }
            return _detail;
        }
    }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        if (_snippet is { } sn && insertionRequestEventArgs is not Avalonia.Input.TextInputEventArgs)
        {
            textArea.Document.Remove(completionSegment.Offset, completionSegment.Length);
            LuaSnippetBuilder.Build(sn.Body).Insert(textArea);
            return;
        }
        // Validation par « ( » : le « ( » est tapé ensuite par l'utilisateur -> on n'insère QUE le nom (sinon « name()( »).
        bool byParen = insertionRequestEventArgs is Avalonia.Input.TextInputEventArgs t && t.Text == "(";
        if (_kind == LuaApiKind.Callback && _entry is { } cb && !byParen && LineBefore(textArea, completionSegment.Offset).TrimEnd().EndsWith("function", StringComparison.Ordinal))
        {
            // « function onTu| » -> squelette complet du callback, curseur dans le corps.
            textArea.Document.Remove(completionSegment.Offset, completionSegment.Length);
            string args = cb.Params.Count > 0 ? string.Join(", ", cb.Params.Select(p => p.Name)) : "";
            LuaSnippetBuilder.Build(cb.Name + "(" + args + ")\n    $0\nend").Insert(textArea);
            return;
        }
        // Déjà suivi d'une « ( » (renommage d'un appel existant) : le nom seul, les arguments restent en place.
        int start = completionSegment.Offset, end = completionSegment.EndOffset;   // lus AVANT le remplacement (ancre déplacée ensuite)
        bool parenFollows = end < textArea.Document.TextLength && textArea.Document.GetCharAt(end) == '(';
        if (IsCallable && !byParen && !parenFollows)
        {
            // « nom() » : curseur entre les parenthèses s'il y a des arguments (l'aide de signature s'ouvre), après sinon.
            textArea.Document.Replace(start, end - start, Text + "()");
            int close = start + Text.Length + 1;   // offset de la « ) »
            textArea.Caret.Offset = CallTarget is not null ? close : close + 1;
            ParensInserted?.Invoke(close, CallTarget is null);
            return;
        }
        textArea.Document.Replace(completionSegment, Text);
    }

    private static string LineBefore(TextArea ta, int offset)
    {
        var line = ta.Document.GetLineByOffset(offset);
        return ta.Document.GetText(line.Offset, offset - line.Offset);
    }
}

/// <summary>Aide de signature (fenêtre sous le curseur pendant la saisie des arguments) : surcharges + argument courant.</summary>
internal sealed class LuaSignatureProvider : IOverloadProvider
{
    private readonly IReadOnlyList<LuaApiEntry> _overloads;
    private int _selected;
    private int _arg;
    public LuaSignatureProvider(IReadOnlyList<LuaApiEntry> overloads, int argIndex)
    {
        _overloads = overloads; _arg = argIndex;
        // Surcharge par défaut : la première qui a assez de paramètres pour l'argument courant.
        int best = overloads.ToList().FindIndex(o => o.Params.Count > argIndex);
        _selected = best >= 0 ? best : 0;
    }
    public int ArgIndex { get => _arg; set { if (_arg == value) return; _arg = value; Raise(nameof(CurrentHeader)); Raise(nameof(CurrentContent)); } }
    public (string? Ns, string Name) Target => (_overloads[0].Namespace, _overloads[0].Name);
    public int SelectedIndex
    {
        get => _selected;
        set { _selected = Math.Clamp(value, 0, Math.Max(0, _overloads.Count - 1)); Raise(nameof(SelectedIndex)); Raise(nameof(CurrentIndexText)); Raise(nameof(CurrentHeader)); Raise(nameof(CurrentContent)); }
    }
    public int Count => _overloads.Count;
    public string? CurrentIndexText => _overloads.Count > 1 ? $"{_selected + 1}/{_overloads.Count}" : null;
    public object CurrentHeader => LuaDocView.SignatureBlock(_overloads[_selected], _arg);
    public object CurrentContent
    {
        get
        {
            var e = _overloads[_selected];
            var p = new StackPanel { Spacing = 4, MaxWidth = 520 };
            if (_arg >= 0 && _arg < e.Params.Count)
            {
                var prm = e.Params[_arg];
                p.Children.Add(LuaDocView.Text(prm.Name + (prm.Optional ? " (optionnel" + (prm.Default is { } d ? ", défaut " + d : "") + ")" : ""),
                                               LuaDocView.KindBrush(LuaApiKind.Constant), 12, weight: FontWeight.SemiBold));
            }
            if (!string.IsNullOrWhiteSpace(e.Description)) p.Children.Add(LuaDocView.Text(e.Description, Brushes.Gainsboro, 12, wrap: true));
            return p;
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Soulignements ondulés des diagnostics (erreur rouge, avertissement ambre, info bleu pointillé) + teinte de la ligne en erreur.</summary>
internal sealed class LuaDiagnosticRenderer : IBackgroundRenderer
{
    private static readonly IPen ErrPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#FF5C6C")), 1.3);
    private static readonly IPen WarnPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E8B339")), 1.2);
    private static readonly IPen InfoPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#79C0FF")), 1.0, new ImmutableDashStyle(new double[] { 2, 2 }, 0));
    private static readonly IBrush ErrLine = new ImmutableSolidColorBrush(Color.Parse("#26FF5C6C"));
    private static readonly IBrush JumpLine = new ImmutableSolidColorBrush(Color.Parse("#335C9DFF"));

    public IReadOnlyList<LuaDiagnostic> Diagnostics { get; set; } = Array.Empty<LuaDiagnostic>();
    /// <summary>Ligne mise en évidence après un saut depuis la console (s'efface d'elle-même).</summary>
    public int FlashLine { get; set; }
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var doc = textView.Document;
        if (doc is null || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
        int first = textView.VisualLines[0].FirstDocumentLine.LineNumber, last = textView.VisualLines[^1].LastDocumentLine.LineNumber;
        if (FlashLine >= first && FlashLine <= last && FlashLine <= doc.LineCount)
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, doc.GetLineByNumber(FlashLine), true))
                drawingContext.FillRectangle(JumpLine, new Rect(0, r.Top, textView.Bounds.Width, r.Height));
        foreach (var d in Diagnostics)
        {
            if (d.EndLine < first || d.Line > last) continue;
            if (!LuaEditorGeometry.TrySegment(doc, d, out var seg)) continue;
            if (d.Severity == LuaDiagnosticSeverity.Error && d.Line <= doc.LineCount)
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, doc.GetLineByNumber(d.Line), true))
                    drawingContext.FillRectangle(ErrLine, new Rect(0, r.Top, textView.Bounds.Width, r.Height));
            var pen = d.Severity switch { LuaDiagnosticSeverity.Error => ErrPen, LuaDiagnosticSeverity.Warning => WarnPen, _ => InfoPen };
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
            {
                double y = r.Bottom - 1.5;
                if (d.Severity == LuaDiagnosticSeverity.Info) { drawingContext.DrawLine(pen, new Point(r.Left, y + 0.5), new Point(r.Right, y + 0.5)); continue; }
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(r.Left, y), false);
                    bool up = true;
                    for (double x = r.Left + 2.5; x <= r.Right + 0.1; x += 2.5) { ctx.LineTo(new Point(x, up ? y - 2 : y)); up = !up; }
                    ctx.EndFigure(false);
                }
                drawingContext.DrawGeometry(null, pen, geo);
            }
        }
    }
}

internal static class LuaEditorGeometry
{
    /// <summary>Segment de document couvert par un diagnostic (colonnes 0-based ; vide -> étendu au mot / au 1er caractère).</summary>
    public static bool TrySegment(TextDocument doc, LuaDiagnostic d, out TextSegment seg)
    {
        seg = null!;
        if (doc.LineCount == 0) return false;
        int l1 = Math.Clamp(d.Line, 1, doc.LineCount), l2 = Math.Clamp(d.EndLine, l1, doc.LineCount);
        var line1 = doc.GetLineByNumber(l1); var line2 = doc.GetLineByNumber(l2);
        int s = line1.Offset + Math.Clamp(d.Column, 0, line1.Length);
        int e = line2.Offset + Math.Clamp(d.EndColumn, 0, line2.Length);
        if (e <= s)
        {
            // Plage vide (fin de fichier, position ponctuelle) : le mot sous la position, sinon le dernier caractère de la ligne.
            int we = s; while (we < line1.EndOffset && IsWord(doc.GetCharAt(we))) we++;
            if (we > s) e = we;
            else if (line1.Length > 0) { e = Math.Min(line1.EndOffset, s + 1); if (e == s) { s = Math.Max(line1.Offset, line1.EndOffset - 1); e = line1.EndOffset; } }
            else return false;
        }
        seg = new TextSegment { StartOffset = s, EndOffset = e };
        return true;
    }
    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}

/// <summary>Indentation automatique à Entrée : reprend l'indentation de la ligne précédente, +1 niveau après une ouverture de bloc.</summary>
internal sealed class LuaIndentationStrategy : IIndentationStrategy
{
    private readonly Func<string> _unit;
    public LuaIndentationStrategy(Func<string> indentUnit) => _unit = indentUnit;

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        var prev = line.PreviousLine;
        if (prev is null) return;
        // Entrée après « end » / « else » / « until »… : la ligne quittée se ré-aligne d'abord sur son bloc.
        LuaEditorText.ReindentDedentLine(document, prev, _unit());
        while (prev is not null && document.GetText(prev).Trim().Length == 0) prev = prev.PreviousLine;   // lignes vides ignorées
        if (prev is null) return;
        string prevText = document.GetText(prev);
        string indent = prevText[..(prevText.Length - prevText.TrimStart().Length)];
        if (LuaFormatter.IndentDeltaAfterLine(prevText) > 0) indent += _unit();
        var cur = document.GetText(line);
        string curTrim = cur.TrimStart();
        if (curTrim.Length > 0 && LuaFormatter.IsDedentLine(curTrim)) indent = LuaEditorText.Dedent(indent, _unit());
        int ws = cur.Length - curTrim.Length;
        document.Replace(line.Offset, ws, indent);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine) { }
}

internal static class LuaEditorText
{
    /// <summary>Ré-aligne une ligne qui commence par une fin de bloc (end, else, elseif, until, }, )) sur la ligne non vide
    /// précédente : même niveau si celle-ci ouvre un bloc, sinon un niveau de moins.</summary>
    public static void ReindentDedentLine(TextDocument doc, DocumentLine line, string unit)
    {
        string text = doc.GetText(line);
        string trim = text.TrimStart();
        if (trim.Length == 0 || !LuaFormatter.IsDedentLine(trim)) return;
        var p = line.PreviousLine;
        while (p is not null && doc.GetText(p).Trim().Length == 0) p = p.PreviousLine;
        if (p is null) return;
        string pt = doc.GetText(p);
        string pIndent = pt[..(pt.Length - pt.TrimStart().Length)];
        string cur = text[..(text.Length - trim.Length)];
        // Déjà désindentée par rapport à la ligne précédente : code aligné à la main / construction sur plusieurs lignes -> on n'y touche pas.
        if (Width(cur) < Width(pIndent)) return;
        // Dans une chaîne longue [[ … ]] ou un commentaire long : texte, pas du code.
        int from = Math.Max(0, line.Offset - 20_000);
        if (from > 0) from = doc.GetLineByOffset(from).Offset;
        int at = line.Offset + cur.Length;
        int len = Math.Min(at + 1, doc.TextLength) - from;
        if (!LuaLexer.IsInCodeAt(doc.GetText(from, len), at - from)) return;
        string want = LuaFormatter.IndentDeltaAfterLine(pt) > 0 ? pIndent : Dedent(pIndent, unit);
        if (cur != want) doc.Replace(line.Offset, cur.Length, want);
    }

    private static int Width(string indent) { int w = 0; foreach (char c in indent) w += c == '\t' ? 4 : 1; return w; }

    public static string Dedent(string indent, string unit)
    {
        if (indent.EndsWith(unit, StringComparison.Ordinal)) return indent[..^unit.Length];
        if (indent.EndsWith('\t')) return indent[..^1];
        int n = 0; while (n < indent.Length && n < unit.Length && indent[^(n + 1)] == ' ') n++;
        return indent[..^n];
    }
}

/// <summary>Snippets : « ${1:texte} » = zone éditable (Tab pour passer à la suivante, répétitions liées), « $0 » = curseur final.</summary>
internal static class LuaSnippetBuilder
{
    public static Snippet Build(string body)
    {
        var sn = new Snippet();
        var fields = new Dictionary<int, SnippetReplaceableTextElement>();
        var text = new StringBuilder();
        void Flush() { if (text.Length > 0) { sn.Elements.Add(new SnippetTextElement { Text = text.ToString() }); text.Clear(); } }
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '$' && i + 1 < body.Length)
            {
                if (body[i + 1] == '0') { Flush(); sn.Elements.Add(new SnippetCaretElement()); i++; continue; }
                if (body[i + 1] == '{')
                {
                    int close = body.IndexOf('}', i + 2);
                    int colon = body.IndexOf(':', i + 2);
                    if (close > 0 && colon > 0 && colon < close && int.TryParse(body.AsSpan(i + 2, colon - i - 2), out int n))
                    {
                        Flush();
                        string def = body.Substring(colon + 1, close - colon - 1);
                        if (fields.TryGetValue(n, out var target)) sn.Elements.Add(new SnippetBoundElement { TargetElement = target });
                        else { var f = new SnippetReplaceableTextElement { Text = def }; fields[n] = f; sn.Elements.Add(f); }
                        i = close; continue;
                    }
                }
            }
            text.Append(c);
        }
        Flush();
        return sn;
    }

    /// <summary>Texte du snippet avec les valeurs par défaut (aperçu).</summary>
    public static string Preview(string body)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '$' && i + 1 < body.Length)
            {
                if (body[i + 1] == '0') { i++; continue; }
                if (body[i + 1] == '{')
                {
                    int close = body.IndexOf('}', i + 2), colon = body.IndexOf(':', i + 2);
                    if (close > 0 && colon > 0 && colon < close) { sb.Append(body, colon + 1, close - colon - 1); i = close; continue; }
                }
            }
            sb.Append(body[i]);
        }
        return sb.ToString();
    }
}
