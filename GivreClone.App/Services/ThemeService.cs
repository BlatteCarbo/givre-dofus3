using System;
using Avalonia;
using Avalonia.Controls;
using GivreClone.App.Themes;

namespace GivreClone.App.Services;

// Bascule de thème runtime : chaque thème = un ResourceDictionary inséré en tête des
// MergedDictionaries pour que ses accents priment sur Theme.axaml.
public static class ThemeService
{
    public static readonly string[] Codes = { "blue", "purple", "emerald", "crimson", "amber", "dark" };

    // mode opaque = pas d'acrylique
    public static bool IsOpaqueMode => CurrentCode == "dark";

    // la fenêtre s'y abonne pour ajuster sa transparence
    public static event Action? Changed;

    private static IResourceProvider? _current;
    public static string CurrentCode { get; private set; } = "amber";

    public static void Apply(string code)
    {
        if (Array.IndexOf(Codes, code) < 0) code = "amber";

        ResourceDictionary dict = code switch
        {
            "blue"    => new Blue(),
            "purple"  => new Purple(),
            "emerald" => new Emerald(),
            "crimson" => new Crimson(),
            "dark"    => new Dark(),
            _         => new Amber(),
        };

        var app = Application.Current!;
        if (_current is not null)
            app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Insert(0, dict);   // en tête : priorité aux clés d'accent

        _current = dict;
        CurrentCode = code;
        Changed?.Invoke();
    }

    public static void Set(string code)
    {
        if (code == CurrentCode) return;
        Apply(code);
        AppConfig.Current.Theme = code;
        AppConfig.Save();
        Logger.Info($"Theme changed to {code}");
    }
}
