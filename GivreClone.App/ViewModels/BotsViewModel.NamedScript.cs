using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// Lancement d'un script GÉNÉRÉ par l'interface (chasse automatique…) sur un bot : mêmes gardes que le démarrage automatique
// (licence, niveau, suiveur d'équipe), même hôte Lua, même vitesse de déplacement ; le nom apparaît dans la console et au panel.
public partial class BotsViewModel
{
    private bool StartNamedScript(uint pid, string name, string code)
    {
        if (!_byPid.TryGetValue((int)pid, out var bot) || Stores.Launch.CurrentMap(pid) is null) return false;
        if (!LicenseService.IsAuthorized || LicenseService.IsSuspended || bot.FollowerLocked) return false;
        if (LevelCapRefusalReason((int)pid) is not null) { RefuseLevelCapped((int)pid, interactive: true); return false; }
        if (_luaHosts.TryGetValue((int)pid, out var running) && running.Running) return false;
        var host = GetOrCreateHost((int)pid);
        ApplyMovementSpeed(host, bot.Row.CharacterName);
        host.Start(code, name + ".lua");
        BotScripts.Set((int)pid, name + ".lua", true);
        if (SelectedBot?.Pid == (int)pid) LuaRunning = true;
        AppendLuaLine("script", $"« {name} » lancé.", LuaLogLevel.Success, (int)pid);
        return true;
    }

    /// <summary>Substitut de test (pid, raison) → hôte arrêté. Null en production : l'hôte Lua du pid.</summary>
    private Func<int, string, bool>? _huntHostStop;

    /// <summary>
    /// Arrête le script de chasse de ce pid seulement si <see cref="BotScripts"/> porte exactement ce fichier
    /// (<c>Chasse au trésor.lua</c> / <c>Fouduglen.lua</c>). <c>host.Stop("user")</c>, pause du planning, drapeaux de
    /// reprise effacés, script de relance MITM oublié, télémétrie arrêtée. Jamais d'abandon de combat ni de chasse.
    /// </summary>
    private bool StopNamedHuntScript(uint pid, string file)
    {
        int id = (int)pid;
        if (!string.Equals(BotScripts.Get(id)?.Name, file, StringComparison.Ordinal)) return false;
        if (!StopLiveHuntHost(id)) return false;
        _planningPaused.Add(id);
        _linkLostResume.Remove(id);
        _socketResume.Remove(id);
        if (_byPid.TryGetValue(id, out var bot) && bot.Row?.Model?.Id is { Length: > 0 } account)
            DropInPlaceScript(account);
        BotScripts.SetRunning(id, false);
        if (SelectedBot?.Pid == id) LuaRunning = false;
        return true;
    }

    private bool StopLiveHuntHost(int pid)
    {
        const string reason = "user";
        if (_huntHostStop is { } probe) return probe(pid, reason);
        if (!_luaHosts.TryGetValue(pid, out var host)) return false;
        host.Stop(reason);
        return true;
    }

    /// <summary>Vrai si la carte courante porte déjà l'élément 484993, ou si c'est une carte de coffre connue.</summary>
    private static bool HuntChestElementOnMap(uint pid, long map)
    {
        if (Services.Treasure.HuntChestMaps.Contains(map)) return true;
        if (pid == 0 || map <= 0) return false;
        int element = Services.Treasure.HuntChestMaps.ElementId;
        if (Stores.Launch.InteractivesDecodedMap(pid) == map)
            foreach (var item in Stores.World.Get(pid).Interactives)
                if (item.ElementId == element) return true;
        foreach (var item in Stores.Launch.PlacelessInteractives(pid, map))
            if (item.ElementId == element) return true;
        return false;
    }
}
