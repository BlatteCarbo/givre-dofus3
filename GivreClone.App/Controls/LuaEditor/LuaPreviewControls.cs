using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using GivreClone.App.Services.LuaEditor;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Controls.LuaEditor;

/// <summary>Aperçu Lua en lecture seule, coloré (modèles, snippets) : texte propre, jamais le document de l'éditeur.</summary>
public sealed class LuaCodePreview : UserControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<LuaCodePreview, string?>(nameof(Text));
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private readonly TextEditor _editor;

    public LuaCodePreview()
    {
        _editor = new TextEditor
        {
            IsReadOnly = true, ShowLineNumbers = true, FontFamily = LuaDocView.Mono, FontSize = 12.5,
            Background = Brushes.Transparent, Foreground = new ImmutableSolidColorBrush(Color.Parse("#D4D7DD")),
            LineNumbersForeground = new ImmutableSolidColorBrush(Color.Parse("#5B6474")),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4, 6), Document = new TextDocument(),
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.HighlightCurrentLine = false;
        Content = _editor;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            if (!string.IsNullOrEmpty(Text)) _editor.SyntaxHighlighting ??= LuaLanguage.Highlighting;   // au 1er aperçu réel (pas au démarrage)
            _editor.Document = new TextDocument(Text ?? "");
            _editor.ScrollToHome();
        }
    }
}

/// <summary>Fiche d'une entrée de la référence de l'API (signatures, badges, description, exemple).</summary>
public sealed class LuaApiCard : ContentControl
{
    public static readonly StyledProperty<LuaApiRow?> RowProperty = AvaloniaProperty.Register<LuaApiCard, LuaApiRow?>(nameof(Row));
    public LuaApiRow? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowProperty)
            Content = Row is { Overloads.Count: > 0 } r ? LuaDocView.Card(r.Overloads, maxWidth: 1000) : null;
    }
}
