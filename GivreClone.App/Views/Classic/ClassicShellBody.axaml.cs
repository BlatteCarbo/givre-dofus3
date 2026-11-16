using Avalonia.Controls;

namespace GivreClone.App.Views.Classic;

/// <summary>Corps de la fenêtre pour la skin « un bot tiers » (barre de menu + arbre chef/mules + page + statut).
/// Bind le MÊME MainViewModel que le shell par défaut ; hébergé dans MainWindow, visible selon UiSkin.</summary>
public partial class ClassicShellBody : UserControl
{
    public ClassicShellBody() => InitializeComponent();
}
