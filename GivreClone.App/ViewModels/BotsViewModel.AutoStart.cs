using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// Option de compte « Démarrer automatiquement le script à la connexion » : dès que le perso est en jeu, son script (le dernier
// chargé dans l'éditeur Lua, celui que le panel et le planning utilisent aussi) démarre sans clic. Une fois par session de jeu
// (pid). Cède toujours la place à ce qui décide déjà : reprise après reconnexion, planning actif, chef d'équipe, arrêt manuel.
public partial class BotsViewModel
{
    private readonly HashSet<int> _autoStartHandled = new();   // pids déjà traités (lancés ou écartés) : une décision par session

    /// <summary>Tableau de bord : nombre de comptes activés prêts à être lancés en un clic.</summary>
    public int DashboardLaunchableCount => _accounts?.DashboardLaunchTargets().Count ?? 0;   // null : VM construite sans comptes (tests)

    /// <summary>Tableau de bord : connecte en jeu tous les comptes activés et démarre leur script (une fois par session).</summary>
    public void LaunchEnabledWithScripts() => _accounts.LaunchEnabledWithScripts();

    private void DriveAutoStartScripts()
    {
        foreach (var bot in _byPid.Values)
        {
            int pid = bot.Pid;
            string? name = bot.Row.CharacterName;
            if (pid <= 0 || _autoStartHandled.Contains(pid)
                || !(bot.Row.Model.AutoStartScript || _accounts.ScriptOnNextSession.Contains(bot.Row.Model.Id))) continue;
            if (string.IsNullOrEmpty(name) || Stores.Launch.CurrentMap((uint)pid) is null) continue;   // pas encore en jeu
            // Encore en attente : licence en reprise, reconnexion en cours, reprise de script programmée (elle prime).
            if (!LicenseService.IsAuthorized || LicenseService.IsSuspended || ScriptStartBlockedByReconnect(bot)
                || _scriptReconnects.Any(r => r.AccountId == bot.Row.Model.Id)) continue;

            _autoStartHandled.Add(pid);
            _accounts.ScriptOnNextSession.Remove(bot.Row.Model.Id);   // demande du tableau de bord : consommée par cette session
            if (BotScripts.Get(pid) is { Running: true } || (_luaHosts.TryGetValue(pid, out var running) && running.Running)) continue;   // déjà lancé (reprise, planning…)
            if (bot.FollowerLocked) continue;                    // suiveur d'équipe : piloté par son chef
            if (_planningPaused.Contains(pid)) continue;          // script arrêté à la main sur cette session
            var plan = PlanningStore.Get(name!);
            if (plan.Enabled)
            {
                AppendLuaLine("compte", Loc.T("L.AutoStart.PlanningOwns"), LuaLogLevel.Info, pid);
                continue;
            }
            if (LevelCapRefusalReason(pid) is not null) { RefuseLevelCapped(pid, interactive: false); continue; }
            var code = plan.Script;
            if (string.IsNullOrWhiteSpace(code) || IsSampleScript(code))
            {
                AppendLuaLine("compte", Loc.T("L.AutoStart.NoScript"), LuaLogLevel.Warning, pid);
                continue;
            }
            var host = GetOrCreateHost(pid);
            ApplyMovementSpeed(host, name);
            host.Start(code, "script.lua");
            BotScripts.Set(pid, BotScripts.Get(pid)?.Name ?? name + ".lua", true);
            if (SelectedBot?.Pid == pid) LuaRunning = true;
            AppendLuaLine("compte", Loc.T("L.AutoStart.Started"), LuaLogLevel.Success, pid);
        }
        // Sessions terminées : on oublie leur pid (un pid réutilisé repartira de zéro).
        if (_autoStartHandled.Count > 0) _autoStartHandled.RemoveWhere(p => !_byPid.ContainsKey(p));
    }
}
