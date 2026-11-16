using System;
using Avalonia;
using Avalonia.Controls;
using GivreClone.App.Lang;

namespace GivreClone.App.Services;

// Localisation runtime (fr/en/es) : un ResourceDictionary par langue échangé dans les
// MergedDictionaries -> les {DynamicResource L.xxx} se rafraîchissent seuls.
// Changed re-localise ce qui est construit en code (VMs).
public static class Loc
{
    public static readonly string[] Codes = { "fr", "en", "es" };
    private static ResourceDictionary? _current;
    public static string CurrentCode { get; private set; } = "fr";
    public static event Action? Changed;

    public static void Apply(string code)
    {
        if (Array.IndexOf(Codes, code) < 0) code = "fr";
        ResourceDictionary dict = code switch { "en" => new En(), "es" => new Es(), _ => new Fr() };
        var app = Application.Current;
        if (app is null) return;
        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);   // en fin : pas de collision avec le thème
        _current = dict;
        CurrentCode = code;
        Changed?.Invoke();
    }

    // change la langue + persiste, marque l'app configurée
    public static void Set(string code)
    {
        Apply(code);
        AppConfig.Current.Language = CurrentCode;
        AppConfig.Current.Configured = true;
        AppConfig.Save();
    }

    // résout une clé depuis le code, fallback = la clé
    public static string T(string key) =>
        Application.Current?.Resources.TryGetResource(key, null, out var v) == true && v is string s ? s : key;

    // T + string.Format
    public static string Fmt(string key, params object?[] args)
    {
        try { return string.Format(T(key), args); } catch { return T(key); }
    }
}
