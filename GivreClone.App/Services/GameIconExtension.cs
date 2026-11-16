using System;
using Avalonia.Markup.Xaml;

namespace GivreClone.App.Services;

/// <summary>
/// Extension XAML : rend l'icône DU JEU nommée (Texture2D de uiassets) pour un <c>Image.Source</c> statique, ex
/// <c>&lt;Image Source="{svc:GameIcon map}" /&gt;</c>. Évaluée au chargement du XAML ; <see cref="UiGameIconProvider.Get"/>
/// charge le bundle à la volée si le préchargement de fond n'a pas encore tourné. Renvoie null si Dofus/le bundle est
/// absent -> l'Image reste vide (repli : garder l'ancienne icône PNG/vectorielle là où l'asset pourrait manquer).
/// </summary>
public sealed class GameIconExtension : MarkupExtension
{
    public GameIconExtension() { }
    public GameIconExtension(string name) => Name = name;

    /// <summary>Nom de l'asset (ex « map », « bank », « inventory », « characteristic », « calendar », « tx_pods »).</summary>
    public string Name { get; set; } = "";

    public override object? ProvideValue(IServiceProvider serviceProvider) => Stores.UiIcons.Get(Name);
}
