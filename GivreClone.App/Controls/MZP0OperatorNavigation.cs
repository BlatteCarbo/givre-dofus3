using Avalonia.Controls;

namespace GivreClone.App.Controls;

/// <summary>
/// Pure visual navigation seam for MainWindow's operator tab. It never calls game,
/// license, socket or process APIs, and never changes MainViewModel.CurrentPage.
/// The existing PageHost and ClassicShellBody retain their bindings and ownership.
/// </summary>
public static class MZP0OperatorNavigation
{
    public static bool IsExistingPageNavigation(Button? clicked, params Button[] pages)
    {
        if (clicked is null) return false;
        foreach (var page in pages)
            if (ReferenceEquals(clicked, page)) return true;
        return false;
    }

    public static void Apply(Control diagnosticsHost, Button sidebarTab, Button titleTab,
        bool isOpen, bool isClassicSkin)
    {
        // In the default skin keep the sidebar available for navigation; the classic
        // skin has its own navigation occupying both columns, so cover its content
        // and retain the always-available title-bar Diagnostics toggle for return.
        Grid.SetColumn(diagnosticsHost, isClassicSkin ? 0 : 1);
        Grid.SetColumnSpan(diagnosticsHost, isClassicSkin ? 2 : 1);
        diagnosticsHost.IsVisible = isOpen;
        SetActive(sidebarTab, isOpen);
        SetActive(titleTab, isOpen);
    }

    private static void SetActive(Button button, bool active)
    {
        if (active) button.Classes.Add("active");
        else button.Classes.Remove("active");
    }
}
