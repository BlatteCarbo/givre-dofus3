using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GivreClone.App.ViewModels;

/// <summary>Un personnage dont on peut importer le profil de combat.</summary>
public sealed class CombatImportSource
{
    public string Name { get; init; } = "";
    /// <summary>« Iop · niv. 120 » (ce qui est connu).</summary>
    public string Detail { get; init; } = "";
    /// <summary>Même classe que le personnage courant : ses sorts existent pour lui.</summary>
    public bool SameClass { get; init; }
    public override string ToString() => Name;
}

/// <summary>Une ligne de la fenêtre d'import : un réglage ou un sort, avec sa valeur courante et celle importée.</summary>
public sealed partial class CombatImportItem : ObservableObject
{
    /// <summary>Clé du réglage (ex. « Style ») ou id du sort en texte.</summary>
    public string Key { get; init; } = "";
    public int SpellId { get; init; }
    public bool IsSpell => SpellId != 0;
    public string Header { get; init; } = "";
    public bool HasHeader => !string.IsNullOrEmpty(Header);
    public string Label { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public string Current { get; init; } = "";
    public string Imported { get; init; } = "";
    public string Note { get; init; } = "";
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool IsNew { get; init; }
    public bool Differs { get; init; }
    public bool Identical => !Differs;
    public bool IsSame => Identical && !IsNew;
    public bool IsChanged => Differs && !IsNew;
    /// <summary>Un élément identique n'a rien à importer : il n'est pas cochable.</summary>
    public bool CanSelect => Differs;
    [ObservableProperty] public partial bool IsSelected { get; set; }
}
