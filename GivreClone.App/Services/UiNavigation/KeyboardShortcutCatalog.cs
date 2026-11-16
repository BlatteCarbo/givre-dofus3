using System.Collections.Generic;

namespace GivreClone.App.Services.UiNavigation;

/// <summary>Un raccourci clavier réellement câblé (Gesture = texte affiché, DescriptionKey = clé L.* localisée).</summary>
public sealed record KeyboardShortcut(string Gesture, string DescriptionKey, string Scope);

/// <summary>Source unique des raccourcis affichés par l'aide F1 : chaque entrée correspond à un gestionnaire existant
/// (MainWindow.OnQuickNavigationKeyDown, HotKey de BotsView, éditeur Lua). Ajouter un raccourci = l'ajouter ici aussi.</summary>
public static class KeyboardShortcutCatalog
{
    public const string ScopeGlobal = "global";
    public const string ScopeBots = "bots";
    public const string ScopeLua = "lua";

    public static IReadOnlyList<KeyboardShortcut> All { get; } = new[]
    {
        new KeyboardShortcut("F1", "L.Shortcuts.Help", ScopeGlobal),
        new KeyboardShortcut("Ctrl+K", "L.Shortcuts.QuickNav", ScopeGlobal),
        new KeyboardShortcut("Alt+←", "L.Shortcuts.Back", ScopeGlobal),
        new KeyboardShortcut("Esc", "L.Shortcuts.Escape", ScopeGlobal),
        new KeyboardShortcut("Enter", "L.Shortcuts.QuickNavEnter", ScopeGlobal),
        new KeyboardShortcut("Ctrl+PageUp", "L.Shortcuts.PrevBot", ScopeBots),
        new KeyboardShortcut("Ctrl+PageDown", "L.Shortcuts.NextBot", ScopeBots),
        new KeyboardShortcut("Ctrl+S", "L.Shortcuts.LuaSave", ScopeLua),
        new KeyboardShortcut("F5", "L.Shortcuts.LuaRun", ScopeLua),
    };
}
