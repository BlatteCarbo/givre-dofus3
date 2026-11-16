using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace GivreClone.App.Skins;

// Palette de la skin « » (x:Class), chargée par type comme les thèmes d'accent.
public partial class ClassicPalette : ResourceDictionary { public ClassicPalette() => AvaloniaXamlLoader.Load(this); }

// Styles de contrôles de la skin (x:Class) : instanciés PAR TYPE (XAML compilé). Surtout pas de StyleInclude(Uri) au runtime :
// il passe par une recherche PAR RÉFLEXION du chargeur XAML de l'assembly, que NativeAOT peut élaguer -> crash au démarrage
// (« No precompiled XAML found for avares://GivreClone/Skins/ClassicControls.axaml », 1.1.8 avant correctif).
public partial class ClassicControls : Styles { public ClassicControls() => AvaloniaXamlLoader.Load(this); }
