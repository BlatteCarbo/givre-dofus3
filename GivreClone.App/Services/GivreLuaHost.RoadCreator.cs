using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

/// <summary>
/// Compatibilité ciblée AnkaBot Road Creator : le Lua reste sandboxé.
/// Seuls les fichiers Road_*.lua plats peuvent être lus/écrits dans scripts/.
/// Jamais d'io système, de chemin absolu, de sous-dossier ou de fichier arbitraire.
/// </summary>
public sealed partial class GivreLuaHost
{
    private const int RoadFileLimitBytes = 512 * 1024;
    private static readonly Regex RoadNamePattern = new(
        @"\ARoad_[A-Za-z0-9_.-]{1,64}\.lua\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Question « récolter / combattre » fournie par l'interface de ce bot.</summary>
    public Func<long, CancellationToken, Task<MapPlan?>>? RoadMapQuestion { get; set; }

    /// <summary>Question libre d'un script (askInput / askQuestion) : (texte, réponse par défaut, oui/non ?) -> réponse, null si
    /// annulée. Sans interface (hôte de console, d'équipe…) : réponse vide / non.</summary>
    public Func<string, string, bool, CancellationToken, Task<string?>>? AskUser { get; set; }

    private string? AskUserBlocking(string prompt, string def, bool question, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (AskUser is null) return null;
        try { return AskUser(prompt, def, question, ct).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new ScriptStopException(); }
        finally { ThrowIfStopping(ct); }
    }

    private long _roadAnswerMap;
    private MapPlan? _roadAnswer;

    private void BindRoadCreator(Script s, CancellationToken ct)
    {
        _roadAnswerMap = long.MinValue;
        _roadAnswer = null;

        // io.open volontairement PARTIEL : nécessaire au Road Creator original.
        // Ne pas exposer io.popen, io.input, io.output, os.execute, etc.
        var io = new Table(s);
        io["open"] = DynValue.NewCallback((_, args) =>
        {
            int offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            string name = offset < args.Count ? args[offset].CastToString() ?? "" : "";
            string mode = offset + 1 < args.Count ? args[offset + 1].CastToString() ?? "r" : "r";
            if (!TryRoadPath(name, out string path) || mode is not ("r" or "w" or "a"))
            {
                Warn("trajet", "Road Creator : fichier ou mode refusé (Road_*.lua uniquement).");
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("fichier non autorisé"));
            }
            try
            {
                ThrowIfStopping(ct);
                string original = mode == "w" ? "" : File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
                if (mode == "r" && !File.Exists(path))
                    return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("fichier absent"));
                if (Encoding.UTF8.GetByteCount(original) > RoadFileLimitBytes)
                    return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("fichier trop volumineux"));

                var buffer = new StringBuilder(original);
                var handle = new Table(s);
                bool closed = false;
                handle["read"] = DynValue.NewCallback((_, a) =>
                {
                    if (closed || mode != "r") return DynValue.Nil;
                    string format = a.Count > 1 ? a[1].CastToString() ?? "" : "*all";
                    return format is "*all" or "*a" or "" ? DynValue.NewString(buffer.ToString()) : DynValue.Nil;
                });
                handle["write"] = DynValue.NewCallback((_, a) =>
                {
                    if (closed || mode == "r") return DynValue.Nil;
                    ThrowIfStopping(ct);
                    int start = a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;
                    for (int i = start; i < a.Count; i++)
                    {
                        var piece = a[i].CastToString() ?? "";
                        if (Encoding.UTF8.GetByteCount(buffer.ToString()) + Encoding.UTF8.GetByteCount(piece) > RoadFileLimitBytes)
                            throw new ScriptRuntimeException("Road Creator : taille maximale de 512 Kio dépassée");
                        buffer.Append(piece);
                    }
                    return DynValue.NewTable(handle);
                });
                handle["close"] = DynValue.NewCallback((_, _) =>
                {
                    if (closed) return DynValue.True;
                    ThrowIfStopping(ct);
                    if (mode != "r")
                    {
                        if (!TryRoadPath(name, out string verified) || !string.Equals(path, verified, StringComparison.OrdinalIgnoreCase))
                            throw new ScriptRuntimeException("Road Creator : fichier refusé lors de l'écriture");
                        WriteRoadAtomically(path, buffer.ToString());
                    }
                    closed = true;
                    return DynValue.True;
                });
                return DynValue.NewTable(handle);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warn("trajet", "Road Creator : lecture/écriture impossible (" + ex.GetType().Name + ").");
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("erreur de fichier"));
            }
        });
        s.Globals["io"] = DynValue.NewTable(io);

        // La version originale concatène le résultat directement dans une étape Lua.
        // À la différence des anciens stubs false, les méthodes renvoient toujours une CHAÎNE.
        var global = s.Globals.Get("global");
        if (global.Type != DataType.Table) return;
        global.Table["rcAskFight"] = DynValue.NewCallback((_, a) =>
            DynValue.NewString(", fight = " + (RoadChoice(ct).Fight ? "true" : "false")));
        global.Table["rcAskGather"] = DynValue.NewCallback((_, a) =>
            DynValue.NewString(", gather = " + (RoadChoice(ct).Gather ? "true" : "false")));
    }

    private MapPlan RoadChoice(CancellationToken ct)
    {
        ThrowIfStopping(ct);
        long mapId = _launch.CurrentMap(_pid) ?? 0;
        if (_roadAnswerMap == mapId && _roadAnswer is { } cached) return cached;
        _roadAnswerMap = mapId;
        _roadAnswer = null;
        if (mapId > 0 && RoadMapQuestion is not null)
        {
            try { _roadAnswer = RoadMapQuestion(mapId, ct).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new ScriptStopException(); }
        }
        ThrowIfStopping(ct);
        return _roadAnswer ?? new MapPlan(false, false);
    }

    private static bool TryRoadPath(string name, out string full)
    {
        full = "";
        if (!RoadNamePattern.IsMatch(name)) return false;
        try
        {
            // On interdit les jonctions sur le répertoire lui-même et sur le fichier.
            Directory.CreateDirectory(ScriptsRootDir);
            var dir = new DirectoryInfo(ScriptsRootDir);
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            full = Path.Combine(ScriptsRootDir, name);
            var fi = new FileInfo(full);
            return !fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }

    private static void WriteRoadAtomically(string target, string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > RoadFileLimitBytes)
            throw new ScriptRuntimeException("Road Creator : taille maximale de 512 Kio dépassée");
        // Écriture temporaire + rename pour ne pas tronquer un trajet en cas de crash.
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, target, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { /* nettoyage best effort */ } }
    }
}
