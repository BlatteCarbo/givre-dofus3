using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.App.Services.Supervisor;

namespace GivreClone.App.ViewModels;

// Ordres des superviseurs sur les scripts des bots (bots:startScript / stopScript / run / stopAllScripts). Thread UI uniquement.
// Mêmes gardes que le pilotage distant existant (OnScriptControlRequested) : licence, reconnexion en cours, cap de niveau.
public partial class BotsViewModel
{
    private static string ScriptsRoot => Path.Combine(MZP19LocalClone.DataRoot, "scripts");

    /// <summary>Bibliothèque de scripts du planning de ce perso : (nom, corps).</summary>
    internal IEnumerable<(string Name, string Body)> ScriptLibraryOf(string? character)
        => string.IsNullOrWhiteSpace(character) ? Enumerable.Empty<(string, string)>() : PlanningStore.Get(character!).Scripts.Select(s => (s.Name, s.Body));

    /// <summary>Lit un fichier du dossier scripts par nom simple (jamais un chemin : le résolveur l'a déjà refusé) ; null si absent.</summary>
    internal static string? ReadScriptFile(string simpleName)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(ScriptsRoot, simpleName));
            if (!path.StartsWith(Path.GetFullPath(ScriptsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(path) && new FileInfo(path).Length <= 2_000_000 ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    /// <summary>Le script PRINCIPAL du bot tourne-t-il (et non une commande /cmd) ? Sert au bus de messages.</summary>
    internal bool IsMainScriptRunning(int pid) => _luaHosts.TryGetValue(pid, out var h) && h.Running;

    internal OrderResult OrderStartScript(int pid, string scriptOrCode)
    {
        if (!_byPid.TryGetValue(pid, out var bot)) return OrderResult.No("bot introuvable ou hors ligne");
        if (!LicenseService.IsAuthorized) return OrderResult.No("session non autorisée");
        if (ScriptStartBlockedByReconnect(bot)) return OrderResult.No("reconnexion en cours");
        if (bot.FollowerLocked) return OrderResult.No("suiveur d'équipe : piloté par son chef");
        if (LevelCapRefusalReason(pid) is { } cap) return OrderResult.No(cap);
        var (script, why) = ScriptResolver.Resolve(scriptOrCode, ScriptLibraryOf(bot.Row.CharacterName), ReadScriptFile);
        if (script is null) return OrderResult.No(why);

        var host = GetOrCreateHost(pid);
        ApplyMovementSpeed(host, bot.Row.CharacterName);
        _planningPaused.Remove(pid);
        _linkLostResume.Remove(pid);
        host.Start(script.Code, "script.lua");   // ne remplace PAS le script enregistré du perso (planning, éditeur)
        BotScripts.Set(pid, script.FromCode ? "code" : script.Name, true);
        if (SelectedBot?.Pid == pid) LuaRunning = true;
        LicenseService.PulseBotTelemetry();
        return OrderResult.Yes;
    }

    internal OrderResult OrderStopScript(int pid)
    {
        if (!_byPid.TryGetValue(pid, out var bot)) return OrderResult.No("bot introuvable ou hors ligne");
        HoldSupervisorResume(pid, bot.Row.Model.Id);
        Stores.Launch.CancelAutomaticStatUpgrade((uint)pid);
        // Pendant une coupure, Running est déjà faux mais le script reste en attente de reprise.
        if (!_luaHosts.TryGetValue(pid, out var host) || !host.Running) return OrderResult.Yes;
        host.Stop("supervisor");   // stopped(reason) reçoit "supervisor"
        BotScripts.SetRunning(pid, false);
        if (SelectedBot?.Pid == pid) LuaRunning = false;
        LicenseService.PulseBotTelemetry();
        return OrderResult.Yes;
    }

    internal OrderResult OrderRun(int pid, string code)
    {
        if (!_byPid.TryGetValue(pid, out var bot)) return OrderResult.No("bot introuvable ou hors ligne");
        if (!LicenseService.IsAuthorized) return OrderResult.No("session non autorisée");
        if (string.IsNullOrWhiteSpace(code)) return OrderResult.No("code vide");
        RunConsoleLua(bot, code);   // même chemin qu'une commande /cmd : hôte transitoire, annulable par /abortCmd
        return _cmdHosts.ContainsKey(pid) ? OrderResult.Yes : OrderResult.No("exécution refusée (voir la console du bot)");
    }

    private void HoldSupervisorResume(int pid, string accountId)
    {
        _planningPaused.Add(pid);
        _linkLostResume.Remove(pid);
        _socketResume.Remove(pid);
        DropInPlaceScript(accountId);
    }

    /// <summary>Arrêt d'urgence : arrête le script de chaque bot dont un script tourne ; rend le nombre arrêté.</summary>
    internal int OrderStopAllScripts()
    {
        int n = 0;
        foreach (var (pid, bot) in _byPid.ToList()) HoldSupervisorResume(pid, bot.Row.Model.Id);
        foreach (var (pid, host) in _luaHosts.ToList())
        {
            if (!host.Running) continue;
            host.Stop("supervisor");
            _planningPaused.Add(pid);
            _linkLostResume.Remove(pid);
            _socketResume.Remove(pid);
            BotScripts.SetRunning(pid, false);
            n++;
        }
        LuaRunning = false;
        LicenseService.PulseBotTelemetry();
        return n;
    }
}
