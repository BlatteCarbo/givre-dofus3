using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GivreClone.App.Lang;

// Chargées par type (pas d'URI runtime), comme les Views.
public partial class Fr : ResourceDictionary { public Fr() => AvaloniaXamlLoader.Load(this); }
public partial class En : ResourceDictionary { public En() => AvaloniaXamlLoader.Load(this); }
public partial class Es : ResourceDictionary { public Es() => AvaloniaXamlLoader.Load(this); }
