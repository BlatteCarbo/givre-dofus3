using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GivreClone.App.Themes;

// Thèmes précompilés (x:Class), chargés par type - pas d'URI runtime.
public partial class Blue    : ResourceDictionary { public Blue()    => AvaloniaXamlLoader.Load(this); }
public partial class Purple  : ResourceDictionary { public Purple()  => AvaloniaXamlLoader.Load(this); }
public partial class Emerald : ResourceDictionary { public Emerald() => AvaloniaXamlLoader.Load(this); }
public partial class Crimson : ResourceDictionary { public Crimson() => AvaloniaXamlLoader.Load(this); }
public partial class Amber   : ResourceDictionary { public Amber()   => AvaloniaXamlLoader.Load(this); }
public partial class Dark    : ResourceDictionary { public Dark()    => AvaloniaXamlLoader.Load(this); }
