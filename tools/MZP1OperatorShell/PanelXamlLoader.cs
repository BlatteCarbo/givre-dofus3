using Avalonia.Markup.Xaml;

namespace GivreClone.App.Controls;

// In this separate host the source XAML is linked as an external resource;
// the original App's auto-generated InitializeComponent does not participate.
// Load the real XAML resource, without duplicating or changing the panel.
public partial class MZP0OperatorPanel
{
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
