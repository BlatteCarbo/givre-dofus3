using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Controls.LuaEditor;
using GivreClone.App.Services;
using GivreClone.App.Services.LuaEditor;
using GivreClone.Engine.LuaTooling;

namespace GivreClone.App.ViewModels;

/// <summary>Onglet sélectionné == paramètre (RadioButton d'un segment lié à un index entier).</summary>
public sealed class LuaTabIs : Avalonia.Data.Converters.IValueConverter
{
    public static readonly LuaTabIs Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int i && int.TryParse(parameter?.ToString(), out int p) && i == p;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true && int.TryParse(parameter?.ToString(), out int p) ? p : Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Script récent (modale « Récents ») ou fichier du dossier des scripts.</summary>
public sealed record LuaRecentRow(string Name, string Folder, string Path, string When, bool Exists);

/// <summary>Catégorie de la référence de l'API.</summary>
public sealed record LuaApiCategoryRow(string Id, string Title, string? Blurb);

/// <summary>Entrée de la référence de l'API (liste) : un nom (ns:nom) et ses surcharges.</summary>
public sealed record LuaApiRow(string Title, string Summary, string KindGlyph, IBrush KindBrush, IReadOnlyList<LuaApiEntry> Overloads);

/// <summary>
/// Éditeur Lua de l'onglet Lua (et de la modale plein écran) : document partagé, analyse en arrière-plan (erreurs de syntaxe +
/// détections), fichier chargé / enregistrement, scripts récents, modèles & snippets, référence de l'API, aller à la ligne,
/// saut depuis la console. Les commandes d'édition (format, commentaire, insertion…) sont exécutées par l'éditeur ACTIF.
/// </summary>
public partial class BotsViewModel : ILuaEditorHost
{
    // ------------------------------------------------------------------ document

    /// <summary>Texte du script (partagé : éditeur de l'onglet + plein écran ; même historique d'annulation).</summary>
    public TextDocument LuaDocument { get; } = new(DefaultLuaScript);
    private string? _luaTextCache;              // texte du document, reconstruit à la demande après une modification
    private string _luaTextLast = DefaultLuaScript;   // dernier texte connu (lecture hors thread UI)
    private bool _luaDocHooked;

    private void EnsureLuaDocumentHooked()
    {
        if (_luaDocHooked) return;
        _luaDocHooked = true;
        LuaDocument.TextChanged += (_, _) =>
        {
            _luaTextCache = null;
            if (!_loadingScript) _luaDirty = true;
            OnPropertyChanged(nameof(LuaScript));
            ScheduleLuaAnalysis();
        };
        LuaLanguage.Warmup();
        ScheduleLuaAnalysis();
    }

    /// <summary>Texte du script Lua de l'éditeur. Écrire remplace tout le texte (annulable par Ctrl+Z).</summary>
    public string LuaScript
    {
        get
        {
            if (_luaTextCache is null)
            {
                if (!Dispatcher.UIThread.CheckAccess()) return _luaTextLast;
                _luaTextCache = _luaTextLast = LuaDocument.Text;
            }
            return _luaTextCache;
        }
        set
        {
            EnsureLuaDocumentHooked();
            value ??= "";
            if (value == LuaScript) return;
            LuaDocument.Text = value;
        }
    }

    // Remplacement « chargement » (fichier, bot sélectionné) : ne marque pas le script comme édité.
    private void SetLuaTextLoaded(string text, bool resetUndo)
    {
        EnsureLuaDocumentHooked();
        bool was = _loadingScript;
        _loadingScript = true;
        try
        {
            if (text != LuaScript) LuaDocument.Text = text;
            if (resetUndo) LuaDocument.UndoStack.ClearAll();
        }
        finally { _loadingScript = was; }
    }

    /// <summary>Nom du script dans les messages d'erreur (et pour relier une erreur de la console à l'éditeur).</summary>
    public string LuaChunkName => LoadedScriptPath is { Length: > 0 } p ? Path.GetFileName(p) : "script.lua";   // jamais le nom du perso (mode Stream)

    // ------------------------------------------------------------------ analyse (erreurs / avertissements)

    public IReadOnlyList<LuaDiagnostic> LuaDiagnostics { get; private set; } = Array.Empty<LuaDiagnostic>();
    public IReadOnlyList<(string Name, bool IsFunction, int Line, string? Args)> LuaDeclarations { get; private set; }
        = Array.Empty<(string, bool, int, string?)>();
    public event Action? LuaDiagnosticsChanged;
    public event Action<LuaEditorRequest>? LuaEditorRequested;

    private DispatcherTimer? _luaAnalyzeTimer;
    private int _luaAnalyzeSeq;

    private void ScheduleLuaAnalysis()
    {
        _luaAnalyzeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) => { _luaAnalyzeTimer!.Stop(); RunLuaAnalysis(); });
        _luaAnalyzeTimer.Stop();
        _luaAnalyzeTimer.Start();
    }

    private void RunLuaAnalysis()
    {
        string text = LuaScript, chunk = LuaChunkName;
        int seq = ++_luaAnalyzeSeq;
        UpdateLuaFileModified(text);
        Task.Run(() =>
        {
            IReadOnlyList<LuaDiagnostic> diags;
            IReadOnlyList<(string, bool, int, string?)> decls;
            try { diags = LuaLanguage.Analyze(text, chunk); } catch { diags = Array.Empty<LuaDiagnostic>(); }
            try { decls = LuaCallContext.Declarations(text); } catch { decls = Array.Empty<(string, bool, int, string?)>(); }
            Dispatcher.UIThread.Post(() =>
            {
                if (seq != _luaAnalyzeSeq) return;   // texte modifié entre-temps : le passage suivant fera foi
                LuaDiagnostics = diags;
                LuaDeclarations = decls;
                RaiseLuaStatus();
                LuaDiagnosticsChanged?.Invoke();
            });
        });
    }

    private static readonly IBrush StatusOk = new ImmutableSolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush StatusWarn = new ImmutableSolidColorBrush(Color.Parse("#E8B339"));
    private static readonly IBrush StatusErr = new ImmutableSolidColorBrush(Color.Parse("#FF6B7A"));

    public int LuaErrorCount => LuaDiagnostics.Count(d => d.Severity == LuaDiagnosticSeverity.Error);
    public int LuaWarningCount => LuaDiagnostics.Count(d => d.Severity == LuaDiagnosticSeverity.Warning);
    /// <summary>« ✓ Aucun problème » / « ✖ 1 erreur · ⚠ 2 avertissements ».</summary>
    public string LuaStatusText
    {
        get
        {
            int e = LuaErrorCount, w = LuaWarningCount;
            if (e == 0 && w == 0) return Loc.T("L.Lua.Editor.NoProblem");
            var parts = new List<string>();
            if (e > 0) parts.Add("✖ " + Loc.Fmt(e > 1 ? "L.Lua.Editor.Errors" : "L.Lua.Editor.Error", e));
            if (w > 0) parts.Add("⚠ " + Loc.Fmt(w > 1 ? "L.Lua.Editor.Warnings" : "L.Lua.Editor.Warning", w));
            return string.Join("  ·  ", parts);
        }
    }
    public IBrush LuaStatusBrush => LuaErrorCount > 0 ? StatusErr : LuaWarningCount > 0 ? StatusWarn : StatusOk;
    /// <summary>Premier problème (erreur d'abord) : « Ligne 12 : … » — cliquable.</summary>
    public string LuaFirstProblemText
    {
        get
        {
            var d = FirstProblem();
            return d is null ? "" : Loc.Fmt("L.Lua.Editor.ProblemAt", d.Line, d.Message + (d.Hint is { } h ? " — " + h : ""));
        }
    }
    public bool LuaHasProblem => FirstProblem() is not null;
    private LuaDiagnostic? FirstProblem()
        => LuaDiagnostics.FirstOrDefault(d => d.Severity == LuaDiagnosticSeverity.Error)
           ?? LuaDiagnostics.FirstOrDefault(d => d.Severity == LuaDiagnosticSeverity.Warning);

    private void RaiseLuaStatus()
    {
        OnPropertyChanged(nameof(LuaStatusText)); OnPropertyChanged(nameof(LuaStatusBrush));
        OnPropertyChanged(nameof(LuaFirstProblemText)); OnPropertyChanged(nameof(LuaHasProblem));
        OnPropertyChanged(nameof(LuaErrorCount)); OnPropertyChanged(nameof(LuaWarningCount));
    }

    [RelayCommand]
    private void LuaJumpToFirstProblem()
    {
        if (FirstProblem() is { } d) RequestLuaEditor(new LuaEditorRequest(LuaEditorAction.Jump, d.Line, d.Column + 1));
    }

    private void RequestLuaEditor(LuaEditorRequest r) => LuaEditorRequested?.Invoke(r);

    // ------------------------------------------------------------------ barre d'outils

    [RelayCommand] private void LuaFormat() => RequestLuaEditor(new(LuaEditorAction.Format));
    [RelayCommand] private void LuaToggleComment() => RequestLuaEditor(new(LuaEditorAction.ToggleComment));
    [RelayCommand] private void LuaFind() => RequestLuaEditor(new(LuaEditorAction.Find));
    [RelayCommand] private void LuaNextProblem() => RequestLuaEditor(new(LuaEditorAction.NextProblem));

    /// <summary>Vérifie le script maintenant (sans attendre la pause de frappe) et annonce le résultat dans la console.</summary>
    [RelayCommand]
    private void LuaCheck()
    {
        var diags = LuaLanguage.Analyze(LuaScript, LuaChunkName);
        LuaDiagnostics = diags; RaiseLuaStatus(); LuaDiagnosticsChanged?.Invoke();
        int e = LuaErrorCount, w = LuaWarningCount;
        if (e == 0 && w == 0) AppendLuaLine("script", Loc.T("L.Lua.Editor.CheckOk"), LuaLogLevel.Success);
        else
        {
            foreach (var d in diags.Where(d => d.Severity != LuaDiagnosticSeverity.Info).Take(20))
                AppendLuaLine("script", $"{LuaChunkName}:{d.Line} — {d.Message}" + (d.Hint is { } h ? " (" + h + ")" : ""),
                              d.Severity == LuaDiagnosticSeverity.Error ? LuaLogLevel.Danger : LuaLogLevel.Warning, -1,
                              new LuaErrorLocation(LuaChunkName, d.Line, d.Column, d.EndLine, d.EndColumn, d.Message));
            LuaJumpToFirstProblem();
        }
    }

    /// <summary>F5 : lance le script, ou le REDÉMARRE avec le texte de l'éditeur s'il tourne déjà.</summary>
    public void LuaRunOrRestart() => RunLua();
    public void LuaStop() { if (LuaRunning) StopLua(); }
    [RelayCommand] private void LuaRestart() => RunLua();

    // Contrôle AVANT lancement : une erreur de SYNTAXE bloque (message avec la ligne exacte + saut dans l'éditeur).
    private bool LuaPrelaunchBlocked(int pid)
    {
        var err = LuaLanguage.SyntaxError(LuaScript, LuaChunkName);
        if (err is null) return false;
        AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.LaunchBlocked", $"{LuaChunkName}:{err.Line} — {err.Message}" + (err.Hint is { } h ? " (" + h + ")" : "")),
                      LuaLogLevel.Danger, pid, new LuaErrorLocation(LuaChunkName, err.Line, err.Column, err.EndLine, err.EndColumn, err.Message));
        RequestLuaEditor(new LuaEditorRequest(LuaEditorAction.Jump, err.Line, err.Column + 1));
        return true;
    }

    /// <summary>Clic sur une erreur de la console : ouvre le script à la bonne ligne (si l'erreur vient du script de l'éditeur).</summary>
    [RelayCommand]
    private void LuaJumpToLog(LuaLogRow? row)
    {
        if (row?.Location is not { } loc) return;
        // Le script EXÉCUTÉ doit être celui de l'éditeur : même texte que le dernier lancement de ce bot (le nom seul ne suffit
        // pas : planning, portail et équipe lancent aussi « script.lua »), ou erreur relevée par la vérification de l'éditeur.
        bool mainChunk = !loc.Chunk.StartsWith("lib:", StringComparison.Ordinal) && !loc.Chunk.StartsWith("file:", StringComparison.Ordinal)
                         && loc.Chunk is not ("cmd" or "equipe.lua" or "chunk");
        string? ran = row.Pid > 0 && _luaHosts.TryGetValue(row.Pid, out var h) ? h.LastScriptText : null;
        bool sameText = ran is not null && string.Equals(ran, LuaScript, StringComparison.Ordinal);
        bool sameName = string.Equals(loc.Chunk, LuaChunkName, StringComparison.OrdinalIgnoreCase);
        if (_editorLocations.Contains(loc) || (mainChunk && (sameText || (ran is null && sameName))))
        {
            RequestLuaEditor(new LuaEditorRequest(LuaEditorAction.Jump, loc.Line, loc.Column + 1));
            return;
        }
        // Autre script (bibliothèque require/dofile, script de plage, commande) : on l'indique, sans remplacer le script du perso
        // ni ouvrir un fichier dont le nom vient du script lui-même.
        Toasts.Show(Loc.Fmt("L.Lua.Editor.OtherScript", loc.Chunk, loc.Line), success: false, sound: false);
    }

    // ------------------------------------------------------------------ hauteur de l'éditeur (poignée)

    public double LuaEditorHeight
    {
        get => Math.Clamp(AppConfig.Current.LuaEditorHeight, 200, 1600);
        set
        {
            double v = Math.Clamp(value, 200, 1600);
            if (Math.Abs(v - AppConfig.Current.LuaEditorHeight) < 0.5) return;
            AppConfig.Current.LuaEditorHeight = v;
            OnPropertyChanged();
            _luaHeightSave ??= new DispatcherTimer(TimeSpan.FromMilliseconds(800), DispatcherPriority.Background, (_, _) => { _luaHeightSave!.Stop(); AppConfig.Save(); });
            _luaHeightSave.Stop(); _luaHeightSave.Start();
        }
    }
    private DispatcherTimer? _luaHeightSave;

    // ------------------------------------------------------------------ fichier : ouvrir / enregistrer / récents

    private string? _luaFileBaseline;   // contenu du fichier chargé tel qu'ouvert / enregistré (modifs non enregistrées)
    [ObservableProperty] public partial bool LuaFileModified { get; set; }
    /// <summary>Nom affiché dans la puce de l'éditeur : fichier chargé, sinon « script de &lt;perso&gt; ».</summary>
    public string LuaDocTitle => LoadedScriptPath is { Length: > 0 } p ? Path.GetFileName(p)
        : !string.IsNullOrWhiteSpace(_luaLoadedFor) ? Loc.Fmt("L.Lua.Editor.CharScript", _luaLoadedFor) : Loc.T("L.Lua.Editor.Untitled");

    partial void OnLoadedScriptPathChanged(string? value) { OnPropertyChanged(nameof(LuaDocTitle)); OnPropertyChanged(nameof(LuaChunkName)); }

    private void UpdateLuaFileModified(string text)
        => LuaFileModified = LoadedScriptPath is { Length: > 0 } && _luaFileBaseline is not null && !string.Equals(text, _luaFileBaseline, StringComparison.Ordinal);

    private static IStorageProvider? Storage =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;

    /// <summary>Ctrl+S : enregistre le script du perso, et le fichier chargé s'il y en a un.</summary>
    public void LuaSave() => _ = LuaSaveAsync();
    [RelayCommand] private Task LuaSaveNow() => LuaSaveAsync();

    private async Task LuaSaveAsync()
    {
        bool persisted = false;
        if (LuaEditorCharacter is { } ch) { _luaDirty = true; PersistLuaScript(ch); persisted = true; }
        if (LoadedScriptPath is { Length: > 0 } path && Path.IsPathRooted(path))
        {
            try
            {
                string text = LuaScript;
                await File.WriteAllTextAsync(path, text, new System.Text.UTF8Encoding(false));
                _luaFileBaseline = text; UpdateLuaFileModified(text);
                AddLuaRecent(path);
                Toasts.Show(Loc.Fmt("L.Lua.Editor.Saved", Path.GetFileName(path)), success: true, sound: false);
            }
            catch (Exception e) { AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.SaveFailed", e.Message), LuaLogLevel.Danger); }
        }
        else if (persisted) Toasts.Show(Loc.T("L.Lua.Editor.SavedCharShort"), success: true, sound: false);
        else Toasts.Show(Loc.T("L.Lua.Editor.SaveNeedsChar"), success: false, sound: false);   // perso pas identifié, aucun fichier
    }

    /// <summary>Ctrl+Maj+S : enregistrer sous (fichier .lua choisi).</summary>
    public void LuaSaveAsRequested() => _ = LuaSaveAsAsync();
    [RelayCommand] private Task LuaSaveAs() => LuaSaveAsAsync();

    private async Task LuaSaveAsAsync()
    {
        var storage = Storage;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("L.Lua.Editor.SaveAsTitle"),
            SuggestedFileName = LoadedScriptPath is { Length: > 0 } p ? Path.GetFileNameWithoutExtension(p) : (_luaLoadedFor ?? "script"),
            DefaultExtension = "lua",
            FileTypeChoices = new[] { new FilePickerFileType("Script Lua") { Patterns = new[] { "*.lua" } } },
        });
        if (file is null) return;
        try
        {
            string text = LuaScript;
            await using (var stream = await file.OpenWriteAsync())
            await using (var w = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) await w.WriteAsync(text);
            string path = file.TryGetLocalPath() ?? file.Name;
            LuaFileSaved(path, text);
            if (LuaEditorCharacter is { } ch) { _luaDirty = true; PersistLuaScript(ch); }
            Toasts.Show(Loc.Fmt("L.Lua.Editor.Saved", Path.GetFileName(path)), success: true, sound: false);
        }
        catch (Exception e) { AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.SaveFailed", e.Message), LuaLogLevel.Danger); }
    }

    /// <summary>« Ouvrir » : sélecteur de fichier .lua.</summary>
    [RelayCommand]
    private async Task LuaOpen()
    {
        if (SelectedBot?.FollowerLocked == true) return;
        var storage = Storage;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("L.Lua.Editor.OpenTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Script Lua") { Patterns = new[] { "*.lua" } }, FilePickerFileTypes.All },
        });
        if (files.Count == 0) return;
        string? local = files[0].TryGetLocalPath();
        if (local is not null) { await OpenLuaFileAsync(local); return; }
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var r = new StreamReader(s);
            ApplyOpenedLua(await r.ReadToEndAsync(), files[0].Name, files[0].Name);
        }
        catch (Exception e) { AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.OpenFailed", e.Message), LuaLogLevel.Danger); }
    }

    /// <summary>Ouvre un fichier .lua dans l'éditeur (confirmation si le fichier courant a des modifications non enregistrées).
    /// Le remplacement est ANNULABLE (Ctrl+Z rend le script précédent).</summary>
    public async Task OpenLuaFileAsync(string path, int jumpLine = 0, int jumpCol = 1)
    {
        if (SelectedBot?.FollowerLocked == true) return;   // script d'un suiveur : piloté par le chef
        if (LuaFileModified && !string.Equals(path, LoadedScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            var choice = await AskLuaUnsavedAsync();
            if (choice == LuaUnsavedChoice.Cancel) return;
            if (choice == LuaUnsavedChoice.Save) await LuaSaveAsync();
        }
        try
        {
            string text = await File.ReadAllTextAsync(path);
            ApplyOpenedLua(text, path, Path.GetFileName(path));
            AddLuaRecent(path);
            if (jumpLine > 0) Dispatcher.UIThread.Post(() => RequestLuaEditor(new(LuaEditorAction.Jump, jumpLine, jumpCol)), DispatcherPriority.Background);
        }
        catch (Exception e) { AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.OpenFailed", e.Message), LuaLogLevel.Danger); }
    }

    private void ApplyOpenedLua(string text, string path, string name)
    {
        ReplaceLuaWithBinding(text, path, text);   // annulable : Ctrl+Z rend le script ET le fichier d'avant
        int lines = text.Count(c => c == '\n') + 1;
        AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.Opened", path, lines), LuaLogLevel.Success);
        _ = LicenseService.PushScriptAsync(name, text);   // librairie portail + inventaire admin (best-effort), comme « Charger »
        LuaRecentOpen = false;
        RequestLuaEditor(new(LuaEditorAction.Focus));
    }

    /// <summary>Recharge le fichier chargé depuis le disque (modifié à l'extérieur).</summary>
    [RelayCommand]
    private async Task LuaReloadFromDisk()
    {
        if (LoadedScriptPath is not { Length: > 0 } p || !File.Exists(p) || SelectedBot?.FollowerLocked == true) return;
        try { string text = await File.ReadAllTextAsync(p); ReplaceLuaWithBinding(text, p, text); }
        catch (Exception e) { AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.OpenFailed", e.Message), LuaLogLevel.Danger); }
    }

    // Association éditeur <-> fichier (chemin + contenu de référence) ENREGISTRÉE DANS L'HISTORIQUE D'ANNULATION, dans le même
    // groupe que le remplacement du texte : Ctrl+Z / Ctrl+Y la font suivre exactement le texte (ouvertures successives,
    // rétablissement, annulation après un enregistrement…) -> Ctrl+S n'écrit jamais un texte sans rapport dans un fichier.
    private sealed class LuaFileBindingOp(BotsViewModel vm, (string? Path, string? Baseline) before, (string? Path, string? Baseline) after)
        : AvaloniaEdit.Document.IUndoableOperation
    {
        public void Undo() => vm.SetFileBinding(before);
        public void Redo() => vm.SetFileBinding(after);
    }

    private void SetFileBinding((string? Path, string? Baseline) b)
    {
        LoadedScriptPath = b.Path;
        _luaFileBaseline = b.Baseline;
        ScheduleLuaAnalysis();   // « modifié » recalculé une fois l'annulation complète appliquée
    }

    /// <summary>Remplace le texte ET l'association au fichier en UNE étape annulable.</summary>
    private void ReplaceLuaWithBinding(string text, string? path, string? baseline)
    {
        EnsureLuaDocumentHooked();
        var before = (LoadedScriptPath, _luaFileBaseline);
        var after = (path, baseline);
        using (LuaDocument.RunUpdate())
        {
            if (!string.Equals(text, LuaScript, StringComparison.Ordinal)) LuaDocument.Text = text;
            LuaDocument.UndoStack.Push(new LuaFileBindingOp(this, before, after));
        }
        SetFileBinding(after);
        UpdateLuaFileModified(text);
    }

    // Emplacements d'erreurs relevés par la vérification de l'éditeur (Vérifier / avant lancement) : toujours reliés à l'éditeur.
    private readonly HashSet<LuaErrorLocation> _editorLocations = new();

    /// <summary>Perso dont l'éditeur montre le script (celui chargé, même si le nom de la ligne a changé depuis).</summary>
    private string? LuaEditorCharacter => !string.IsNullOrEmpty(_luaLoadedFor) ? _luaLoadedFor : SelectedBot?.Row.CharacterName is { Length: > 0 } n ? n : null;

    /// <summary>Script GÉNÉRÉ (enregistreur de trajet) : remplace le texte (annulable) et détache le fichier ouvert — Ctrl+S
    /// n'écrira jamais le trajet dans le fichier précédemment chargé.</summary>
    public bool ApplyGeneratedLua(string lua)
    {
        if (SelectedBot?.FollowerLocked == true) return false;
        ReplaceLuaWithBinding(lua, null, null);
        RequestLuaEditor(new(LuaEditorAction.Focus));
        return true;
    }

    /// <summary>Le texte vient d'être écrit dans <paramref name="path"/> (enregistrer sous, trajet enregistré).</summary>
    public void LuaFileSaved(string path, string text)
    {
        // Lié seulement si l'éditeur montre bien ce texte (sinon : trajet non injecté, autre bot sélectionné entre-temps…).
        if (string.Equals(LuaScript, text, StringComparison.Ordinal)) { SetFileBinding((path, text)); UpdateLuaFileModified(text); }
        if (Path.IsPathRooted(path)) AddLuaRecent(path);
    }

    private void AddLuaRecent(string path)
    {
        var list = AppConfig.Current.RecentLuaScripts;
        list.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, new RecentLuaScript { Path = path, OpenedAt = DateTime.Now });
        while (list.Count > 12) list.RemoveAt(list.Count - 1);
        AppConfig.Save();
    }

    // --- modale « Récents »
    [ObservableProperty] public partial bool LuaRecentOpen { get; set; }
    public ObservableCollection<LuaRecentRow> LuaRecentFiles { get; } = new();
    public ObservableCollection<LuaRecentRow> LuaLibraryFiles { get; } = new();
    public bool LuaHasRecent => LuaRecentFiles.Count > 0;
    public bool LuaHasLibrary => LuaLibraryFiles.Count > 0;

    [RelayCommand]
    private void LuaShowRecent()
    {
        LuaRecentFiles.Clear();
        foreach (var r in AppConfig.Current.RecentLuaScripts)
            LuaRecentFiles.Add(new LuaRecentRow(Path.GetFileName(r.Path), Path.GetDirectoryName(r.Path) ?? "", r.Path,
                                                LocDate.DayMonth(r.OpenedAt) + " " + LocDate.Hm(r.OpenedAt), File.Exists(r.Path)));
        LuaLibraryFiles.Clear();
        try
        {
            string root = Path.Combine(MZP19LocalClone.DataRoot, "scripts");
            if (Directory.Exists(root))
                foreach (var f in Directory.EnumerateFiles(root, "*.lua", SearchOption.AllDirectories).Take(200)
                                           .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime))
                    LuaLibraryFiles.Add(new LuaRecentRow(f.Name, Path.GetRelativePath(root, f.DirectoryName ?? root), f.FullName,
                                                         LocDate.DayMonth(f.LastWriteTime) + " " + LocDate.Hm(f.LastWriteTime), true));
        }
        catch { /* dossier illisible : section vide */ }
        OnPropertyChanged(nameof(LuaHasRecent)); OnPropertyChanged(nameof(LuaHasLibrary));
        LuaRecentOpen = true;
    }

    [RelayCommand] private void LuaCloseRecent() => LuaRecentOpen = false;

    [RelayCommand]
    private async Task LuaOpenRecent(LuaRecentRow? row)
    {
        if (row is null) return;
        if (!File.Exists(row.Path)) { Toasts.Show(Loc.Fmt("L.Lua.Editor.Missing", row.Name), success: false, sound: false); return; }
        LuaRecentOpen = false;
        await OpenLuaFileAsync(row.Path);
    }

    [RelayCommand]
    private void LuaForgetRecent(LuaRecentRow? row)
    {
        if (row is null) return;
        AppConfig.Current.RecentLuaScripts.RemoveAll(r => string.Equals(r.Path, row.Path, StringComparison.OrdinalIgnoreCase));
        AppConfig.Save();
        LuaRecentFiles.Remove(row);
        OnPropertyChanged(nameof(LuaHasRecent));
    }

    [RelayCommand]
    private async Task LuaOpenFromRecentPicker() { LuaRecentOpen = false; await LuaOpen(); }

    // --- confirmation « modifications non enregistrées » (modale au niveau fenêtre, au-dessus du plein écran)
    public enum LuaUnsavedChoice { Save, Discard, Cancel }
    [ObservableProperty] public partial bool LuaUnsavedOpen { get; set; }
    public string LuaUnsavedText => Loc.Fmt("L.Lua.Editor.UnsavedBody", LoadedScriptPath is { } p ? Path.GetFileName(p) : "");
    private TaskCompletionSource<LuaUnsavedChoice>? _luaUnsavedTcs;
    private Task<LuaUnsavedChoice> AskLuaUnsavedAsync()
    {
        _luaUnsavedTcs?.TrySetResult(LuaUnsavedChoice.Cancel);
        _luaUnsavedTcs = new TaskCompletionSource<LuaUnsavedChoice>();
        OnPropertyChanged(nameof(LuaUnsavedText));
        LuaUnsavedOpen = true;
        return _luaUnsavedTcs.Task;
    }
    [RelayCommand] private void LuaUnsavedSave() { LuaUnsavedOpen = false; _luaUnsavedTcs?.TrySetResult(LuaUnsavedChoice.Save); }
    [RelayCommand] private void LuaUnsavedDiscard() { LuaUnsavedOpen = false; _luaUnsavedTcs?.TrySetResult(LuaUnsavedChoice.Discard); }
    [RelayCommand] private void LuaUnsavedCancel() { LuaUnsavedOpen = false; _luaUnsavedTcs?.TrySetResult(LuaUnsavedChoice.Cancel); }

    // ------------------------------------------------------------------ plein écran

    [ObservableProperty] public partial bool LuaFullscreenOpen { get; set; }
    partial void OnLuaFullscreenOpenChanged(bool value) => RequestLuaEditor(new(LuaEditorAction.CloseAssist));
    /// <summary>Position du curseur de l'éditeur actif (reprise par l'autre éditeur à l'ouverture / fermeture du plein écran).</summary>
    public int LuaCaretOffset { get; set; }
    [RelayCommand] private void LuaOpenFullscreen() { LuaFullscreenOpen = true; RequestLuaEditor(new(LuaEditorAction.Focus)); }
    [RelayCommand] private void LuaCloseFullscreen() { LuaFullscreenOpen = false; RequestLuaEditor(new(LuaEditorAction.Focus)); }

    // ------------------------------------------------------------------ aller à la ligne (Ctrl+G)

    [ObservableProperty] public partial bool LuaGoToOpen { get; set; }
    [ObservableProperty] public partial string LuaGoToText { get; set; } = "";
    public string LuaGoToHint => Loc.Fmt("L.Lua.Editor.GoToHint", LuaDocument.LineCount);
    public void LuaOpenGoToLine() { LuaGoToText = ""; OnPropertyChanged(nameof(LuaGoToHint)); LuaGoToOpen = true; }
    [RelayCommand] private void LuaShowGoTo() => LuaOpenGoToLine();
    [RelayCommand] private void LuaGoToCancel() { LuaGoToOpen = false; RequestLuaEditor(new(LuaEditorAction.Focus)); }
    [RelayCommand]
    private void LuaGoToConfirm()
    {
        var parts = (LuaGoToText ?? "").Split(new[] { ':', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        LuaGoToOpen = false;
        if (parts.Length == 0 || !int.TryParse(parts[0], out int line)) { RequestLuaEditor(new(LuaEditorAction.Focus)); return; }
        int col = parts.Length > 1 && int.TryParse(parts[1], out int c) ? c : 1;
        RequestLuaEditor(new LuaEditorRequest(LuaEditorAction.GoToLine, line, col));
    }

    // ------------------------------------------------------------------ modèles & snippets

    [ObservableProperty] public partial bool LuaTemplatesOpen { get; set; }
    public IReadOnlyList<LuaScriptTemplate> LuaScriptTemplates => LuaTemplates.Scripts;
    public IReadOnlyList<LuaSnippet> LuaSnippets => LuaTemplates.Snippets;
    [ObservableProperty] public partial LuaScriptTemplate? SelectedLuaTemplate { get; set; }
    [ObservableProperty] public partial LuaSnippet? SelectedLuaSnippet { get; set; }
    /// <summary>0 = modèles de script, 1 = snippets.</summary>
    [ObservableProperty] public partial int LuaTemplatesTab { get; set; }
    public string LuaTemplatePreview => LuaTemplatesTab == 1
        ? (SelectedLuaSnippet is { } s ? LuaSnippetPreview(s.Body) : "")
        : SelectedLuaTemplate?.Body ?? "";
    partial void OnSelectedLuaTemplateChanged(LuaScriptTemplate? value) => OnPropertyChanged(nameof(LuaTemplatePreview));
    partial void OnSelectedLuaSnippetChanged(LuaSnippet? value) => OnPropertyChanged(nameof(LuaTemplatePreview));
    partial void OnLuaTemplatesTabChanged(int value) => OnPropertyChanged(nameof(LuaTemplatePreview));
    private static string LuaSnippetPreview(string body) => SnippetPreviewText(body);

    [RelayCommand]
    private void LuaShowTemplates()
    {
        SelectedLuaTemplate ??= LuaScriptTemplates.FirstOrDefault();
        SelectedLuaSnippet ??= LuaSnippets.FirstOrDefault();
        LuaTemplatesOpen = true;
    }
    [RelayCommand] private void LuaCloseTemplates() { LuaTemplatesOpen = false; RequestLuaEditor(new(LuaEditorAction.Focus)); }

    /// <summary>Remplace tout le script par le modèle choisi (annulable : Ctrl+Z).</summary>
    [RelayCommand]
    private void LuaApplyTemplate()
    {
        if (SelectedBot?.FollowerLocked == true) { LuaTemplatesOpen = false; return; }   // script d'un suiveur : piloté par le chef
        if (LuaTemplatesTab == 1) { LuaInsertTemplate(); return; }
        if (SelectedLuaTemplate is not { } t) return;
        ReplaceLuaWithBinding(t.Body, null, null);
        LuaTemplatesOpen = false;
        AppendLuaLine("script", Loc.Fmt("L.Lua.Editor.TemplateApplied", t.Title), LuaLogLevel.Success);
        RequestLuaEditor(new(LuaEditorAction.Focus));
    }

    /// <summary>Insère le modèle / snippet au curseur de l'éditeur actif.</summary>
    [RelayCommand]
    private void LuaInsertTemplate()
    {
        LuaTemplatesOpen = false;
        if (SelectedBot?.FollowerLocked == true) return;
        if (LuaTemplatesTab == 1) { if (SelectedLuaSnippet is { } s) RequestLuaEditor(new(LuaEditorAction.InsertSnippet, Text: s.Body)); return; }
        if (SelectedLuaTemplate is { } t) RequestLuaEditor(new(LuaEditorAction.Insert, Text: t.Body));
    }

    // ------------------------------------------------------------------ référence de l'API

    [ObservableProperty] public partial bool LuaApiRefOpen { get; set; }
    [ObservableProperty] public partial string LuaApiQuery { get; set; } = "";
    public ObservableCollection<LuaApiCategoryRow> LuaApiCategories { get; } = new();
    [ObservableProperty] public partial int LuaApiCategoryIndex { get; set; } = -1;
    public ObservableCollection<LuaApiRow> LuaApiResults { get; } = new();
    [ObservableProperty] public partial LuaApiRow? SelectedLuaApiRow { get; set; }
    public string LuaApiCategoryBlurb => LuaApiCategoryIndex >= 0 && LuaApiCategoryIndex < LuaApiCategories.Count ? LuaApiCategories[LuaApiCategoryIndex].Blurb ?? "" : "";
    public string LuaApiCount => Loc.Fmt("L.Lua.Editor.ApiCount", LuaApiResults.Count);
    public bool LuaApiEmpty => LuaApiResults.Count == 0;

    partial void OnLuaApiQueryChanged(string value) => RefreshLuaApiResults();
    partial void OnLuaApiCategoryIndexChanged(int value) { if (value >= 0 && !string.IsNullOrEmpty(LuaApiQuery)) LuaApiQuery = ""; RefreshLuaApiResults(); OnPropertyChanged(nameof(LuaApiCategoryBlurb)); }

    [RelayCommand]
    private void LuaShowApiRef()
    {
        var cat = LuaLanguage.Catalog;
        if (LuaApiCategories.Count == 0)
        {
            foreach (var c in cat.Categories) LuaApiCategories.Add(new LuaApiCategoryRow(c.Id, c.Title, c.Blurb));
            if (LuaStdLib.Entries.Count > 0) LuaApiCategories.Add(new LuaApiCategoryRow("stdlib", "Lua standard", null));
        }
        if (LuaApiCategoryIndex < 0 && string.IsNullOrEmpty(LuaApiQuery)) LuaApiCategoryIndex = 0;
        else RefreshLuaApiResults();
        LuaApiRefOpen = true;
    }
    [RelayCommand] private void LuaCloseApiRef() { LuaApiRefOpen = false; RequestLuaEditor(new(LuaEditorAction.Focus)); }

    private void RefreshLuaApiResults()
    {
        var cat = LuaLanguage.Catalog;
        IEnumerable<LuaApiEntry> src;
        if (!string.IsNullOrWhiteSpace(LuaApiQuery)) src = cat.Search(LuaApiQuery.Trim(), 300);
        else if (LuaApiCategoryIndex >= 0 && LuaApiCategoryIndex < LuaApiCategories.Count)
        {
            string id = LuaApiCategories[LuaApiCategoryIndex].Id;
            src = id == "stdlib" ? LuaStdLib.Entries : cat.InCategory(id);
        }
        else src = Array.Empty<LuaApiEntry>();
        LuaApiResults.Clear();
        foreach (var g in src.GroupBy(e => (e.Namespace, e.Name)))
        {
            var list = g.ToList();
            var e = list[0];
            string title = (e.Namespace is { } ns ? ns + (e.Kind == LuaApiKind.StdLib ? "." : ":") : "") + e.Name;
            string summary = e.Description.Length > 110 ? e.Description[..110].TrimEnd() + "…" : e.Description;
            LuaApiResults.Add(new LuaApiRow(title, summary, LuaDocView.KindGlyph(e.Kind), LuaDocView.KindBrush(e.Kind), list));
        }
        SelectedLuaApiRow = LuaApiResults.FirstOrDefault();
        OnPropertyChanged(nameof(LuaApiCount)); OnPropertyChanged(nameof(LuaApiEmpty));
    }

    /// <summary>Insère l'appel choisi au curseur (« map:getPods() ») et ferme la référence.</summary>
    [RelayCommand]
    private void LuaInsertApi(LuaApiRow? row)
    {
        row ??= SelectedLuaApiRow;
        if (row is null) return;
        if (SelectedBot?.FollowerLocked == true) { LuaApiRefOpen = false; return; }
        var e = row.Overloads[0];
        string text = e.Kind switch
        {
            LuaApiKind.Constant => e.Name + " = " + (e.ConstDefault ?? "nil"),
            LuaApiKind.Namespace => e.Name + ":",
            LuaApiKind.Callback => "function " + e.Name + "(" + string.Join(", ", e.Params.Select(p => p.Name)) + ")\n    \nend\n",
            _ => row.Title + "(" + ")",
        };
        LuaApiRefOpen = false;
        RequestLuaEditor(new(LuaEditorAction.Insert, Text: text));
    }

    // ------------------------------------------------------------------ utilitaires

    private static string SnippetPreviewText(string body)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '$' && i + 1 < body.Length)
            {
                if (body[i + 1] == '0') { i++; continue; }
                if (body[i + 1] == '{')
                {
                    int close = body.IndexOf('}', i + 2), colon = body.IndexOf(':', i + 2);
                    if (close > 0 && colon > 0 && colon < close) { sb.Append(body, colon + 1, close - colon - 1); i = close; continue; }
                }
            }
            sb.Append(body[i]);
        }
        return sb.ToString();
    }
}
