using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class PlanningEditor : UserControl
{
    public PlanningEditor()
    {
        InitializeComponent();
        // Renommage d'un script de la bibliothèque (TextBox « scriptname », saisie dans Draft) : VALIDÉ à Entrée et à la perte
        // du focus, abandonné par Échap. (Le rechargement de l'éditeur pendant la frappe le valide aussi : PlanningViewModel.Load.)
        AddHandler(KeyDownEvent, OnScriptNameKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnScriptNameLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static PlanningScriptRow? ScriptRowOf(object? source)
        => (source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is { } tb && tb.Classes.Contains("scriptname")
            ? tb.DataContext as PlanningScriptRow
            : null;

    private void OnScriptNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (ScriptRowOf(e.Source) is not { } row) return;
        if (e.Key is Key.Enter or Key.Return) { row.CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { row.CancelRename(); e.Handled = true; }
    }

    private void OnScriptNameLostFocus(object? sender, RoutedEventArgs e) => ScriptRowOf(e.Source)?.CommitRename();

    // Charge un (ou plusieurs) fichier .lua depuis le disque DIRECTEMENT dans la bibliothèque du planning (nom = nom du
    // fichier), sans passer par l'éditeur Lua puis « figer ». Chaque fichier devient une entrée nommée de la bibliothèque ;
    // son chemin est mémorisé pour le recharger au début de chaque plage. Le retour passe par PlanningViewModel.Log
    // (console du bot affiché, ou notification pour un perso hors ligne).
    private async void OnPlanningLoadScript(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlanningViewModel pvm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("L.Bots.Planning.PickTitle"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc.T("L.Bots.Planning.PickType")) { Patterns = new[] { "*.lua" } },
                FilePickerFileTypes.All,
            },
        });
        if (files.Count == 0) return;

        foreach (var f in files)
        {
            try
            {
                await using var stream = await f.OpenReadAsync();
                using var reader = new StreamReader(stream);
                string body = await reader.ReadToEndAsync();
                // Chemin mémorisé (.lua seulement) -> script rechargé depuis le fichier à chaque début de plage.
                string? src = f.TryGetLocalPath();
                if (src is not null && !src.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) src = null;
                pvm.AddScriptFromFile(Path.GetFileNameWithoutExtension(f.Name), body, src);
                pvm.Log?.Invoke(Loc.Fmt("L.Bots.Planning.FileAdded", f.Name, body.Count(c => c == '\n') + 1), LuaLogLevel.Success);
                _ = LicenseService.PushScriptAsync(f.Name, body);   // -> librairie portail + inventaire admin (révisions serveur), best-effort
            }
            catch (Exception ex)
            {
                pvm.Log?.Invoke(Loc.Fmt("L.Bots.Planning.FileFailed", f.Name, ex.Message), LuaLogLevel.Danger);
            }
        }
    }
}
