using System;
using System.Text;

namespace GivreClone.App.Services.Treasure;

/// <summary>Script Lua de chasse au trésor automatique. Pur : testable (syntaxe). Utilise l'API existante du module treasure: (startHunt, stepType, getNextFlagName / Direction, getStartMapId, getPos,
/// huntFlag, isReadyToDig, huntDig, quit) et le résolveur d'indices treasure.findClues (DofusDB, cache disque). Les phorreurs (étapes « hint ») n'ont pas de résolveur : le script avance carte par carte
/// dans la direction demandée (10 cartes au plus) jusqu'à ce que le phorreur soit sur la carte.</summary>
public static class TreasureScript
{
    public static string Build(int huntLevel = 0, int maxFails = 3, bool preserveHunt = false)
    {
        maxFails = Math.Clamp(maxFails, 1, 10);
        var sb = new StringBuilder();
        sb.Append("-- Chasse au trésor automatique — script généré par Givre.\n");
        sb.Append("-- Démarre une chasse au coffre de la carte où tu lances le script, résout chaque indice avec DofusDB (treasure.findClues), va sur la carte, pose le drapeau,\n");
        sb.Append("-- creuse, enchaîne le combat final puis retourne au coffre pour la chasse suivante. Phorreurs : avance carte par carte dans la direction indiquée.\n");
        sb.Append("-- Après ").Append(maxFails).Append(" échecs sur un même indice, la chasse est abandonnée proprement (treasure:quit).\n\n");
        sb.Append("local HUNT_LEVEL = ").Append(Math.Clamp(huntLevel, 0, 200)).Append("   -- niveau de chasse voulu (0 = le premier choix du coffre)\n");
        sb.Append("local MAX_FAILS = ").Append(maxFails).Append("\n");
        sb.Append("local home = nil          -- carte du coffre de chasse (là où le script a été lancé)\n");
        sb.Append("local fails = 0\n");
        sb.Append("local startRefusals = 0  -- refus de démarrage consécutifs (pas de coffre de chasse ici) : arrêt au 3e\n");
        sb.Append("local candidate = 1       -- carte candidate essayée pour l'indice en cours (1 = la plus proche)\n");
        sb.Append("local lastKey = \"\"\n");
        sb.Append("local walked = 0          -- cartes traversées pour un phorreur\n\n");
        sb.Append("local function coords(mapId)\n");
        sb.Append("  local s = treasure:getPos(mapId)\n");
        sb.Append("  local x, y = string.match(s or \"\", \"%[(-?%d+),%s*(-?%d+)%]\")\n");
        sb.Append("  return tonumber(x), tonumber(y)\n");
        sb.Append("end\n\n");
        sb.Append("local function giveUp(why)\n");
        if (preserveHunt)
        {
            sb.Append("  print(\"[chasse] arrêt : \" .. why .. \", chasse conservée\") stopScript() return\n");
        }
        else
        {
        sb.Append("  print(\"[chasse] abandon : \" .. why)\n");
        sb.Append("  if not treasure:quit() or treasure:isHunting() then\n");
        sb.Append("    print(\"[chasse] arrêt : abandon non confirmé, chasse conservée\") stopScript() return\n");
        sb.Append("  end\n");
        sb.Append("  fails, candidate, lastKey, walked = 0, 1, \"\", 0\n");
        sb.Append("  local wait = treasure:giveUpCooldown()\n");
        sb.Append("  if wait and wait > 0 then print(\"[chasse] nouvelle chasse possible dans \" .. wait .. \" min\") delay(wait * 60000) end\n");
        }
        sb.Append("end\n\n");
        sb.Append("local function goHome()\n");
        sb.Append("  if home and getMapId() ~= home then\n");
        sb.Append("    if not travelTo(home) then print(\"[chasse] impossible de revenir au coffre\") delay(30000) return false end\n");
        sb.Append("  end\n");
        sb.Append("  return home ~= nil and getMapId() == home\n");
        sb.Append("end\n\n");
        sb.Append("function move()\n");
        sb.Append("  home = home or getMapId()\n\n");
        sb.Append("  -- 1) pas de chasse : retour au coffre puis démarrage\n");
        sb.Append("  if not treasure:isHunting() then\n");
        sb.Append("    if not goHome() then return true end\n");
        sb.Append("    local r = treasure:startHunt(HUNT_LEVEL)\n");
        sb.Append("    if r < 0 then print(\"[chasse] nouvelle chasse dans \" .. (-r) .. \" min\") delay(60000)\n");
        sb.Append("    elseif r ~= 1 and r ~= 5 then\n");
        sb.Append("      startRefusals = startRefusals + 1\n");
        sb.Append("      print(\"[chasse] démarrage refusé (code \" .. r .. \") \" .. startRefusals .. \"/3 : coffre absent de la carte ou indisponible (chasse quotidienne déjà faite ?)\")\n");
        sb.Append("      if startRefusals >= 3 then print(\"[chasse] arrêt : aucune chasse ne démarre ici\") stopScript() return false end\n");
        sb.Append("      delay(30000)\n");
        sb.Append("    else startRefusals = 0 end\n");
        sb.Append("    fails, candidate, lastKey, walked = 0, 1, \"\", 0\n");
        sb.Append("    return true\n");
        sb.Append("  end\n\n");
        sb.Append("  -- 2) tous les drapeaux posés (ou combat final) : on creuse\n");
        sb.Append("  if treasure:isReadyToDig() or treasure:isFightStep() then\n");
        sb.Append("    if not treasure:huntDig() then\n");
        sb.Append("      fails = fails + 1\n");
        sb.Append("      if fails >= MAX_FAILS then giveUp(\"creusage refusé \" .. fails .. \" fois\") end\n");
        sb.Append("    else fails = 0 end\n");
        sb.Append("    return true\n");
        sb.Append("  end\n\n");
        sb.Append("  local stype = treasure:stepType()\n");
        sb.Append("  local name = treasure:getNextFlagName()\n");
        sb.Append("  local dir = treasure:getNextFlagDirection()\n\n");
        sb.Append("  -- Étape direction : seules les transitions confirmées comptent.\n");
        sb.Append("  if stype == \"direction\" then\n");
        sb.Append("    if treasure:getStartMapId() <= 0 then print(\"[chasse] arrêt : origine de l'étape inconnue, chasse conservée\") stopScript() return false end\n");
        sb.Append("    local count = treasure:getNextFlagMapCount()\n");
        sb.Append("    if count < 1 or count > 100 then giveUp(\"nombre de cartes invalide\") return true end\n");
        sb.Append("    local key = \"direction|\" .. treasure:getStartMapId() .. \"|\" .. dir .. \"|\" .. count\n");
        sb.Append("    if key ~= lastKey then\n");
        sb.Append("      local origin = treasure:getStartMapId()\n");
        sb.Append("      if getMapId() ~= origin and not travelTo(origin) then\n");
        sb.Append("        fails = fails + 1\n");
        sb.Append("        if fails >= MAX_FAILS then giveUp(\"départ de l’étape direction inaccessible\") end\n");
        sb.Append("        return true\n");
        sb.Append("      end\n");
        sb.Append("      lastKey, walked = key, 0\n");
        sb.Append("    end\n");
        sb.Append("    if walked < count then\n");
        sb.Append("      if changeMap(dir) then walked = walked + 1 else fails = fails + 1 end\n");
        sb.Append("    elseif treasure:huntFlag() then fails, walked, lastKey = 0, 0, \"\"\n");
        sb.Append("    else fails = fails + 1 end\n");
        sb.Append("    if fails >= MAX_FAILS then giveUp(\"étape direction refusée\") end\n");
        sb.Append("    return true\n");
        sb.Append("  end\n");
        sb.Append("\n");
        sb.Append("  -- 3) phorreur : pas de résolveur, on avance carte par carte dans la direction jusqu'à le trouver\n");
        sb.Append("  if stype == \"hint\" then\n");
        sb.Append("    if treasure:getStartMapId() <= 0 then print(\"[chasse] arrêt : origine du phorreur inconnue, chasse conservée\") stopScript() return false end\n");
        sb.Append("    local key = \"hint|\" .. treasure:getStartMapId() .. \"|\" .. dir .. \"|\" .. name\n");
        sb.Append("    if key ~= lastKey then\n");
        sb.Append("      local origin = treasure:getStartMapId()\n");
        sb.Append("      if getMapId() ~= origin and not travelTo(origin) then\n");
        sb.Append("        fails = fails + 1\n");
        sb.Append("        if fails >= MAX_FAILS then giveUp(\"départ du phorreur inaccessible\") end\n");
        sb.Append("        return true\n");
        sb.Append("      end\n");
        sb.Append("      lastKey, walked = key, 0\n");
        sb.Append("    end\n");
        sb.Append("    if treasure:isPhorreurInMap() then\n");
        sb.Append("      if treasure:huntFlag() then fails, walked, lastKey = 0, 0, \"\" else fails = fails + 1 end\n");
        sb.Append("    elseif walked >= 10 then giveUp(\"phorreur « \" .. name .. \" » introuvable en 10 cartes\")\n");
        sb.Append("    else\n");
        sb.Append("      if changeMap(dir) then walked = walked + 1 else fails = fails + 1 end\n");
        sb.Append("    end\n");
        sb.Append("    if fails >= MAX_FAILS then giveUp(\"déplacements refusés vers le phorreur\") end\n");
        sb.Append("    return true\n");
        sb.Append("  end\n\n");
        sb.Append("  -- 4) indice : où se trouve « name » dans cette direction depuis le dernier drapeau ?\n");
        sb.Append("  if stype == \"poi\" then\n");
        sb.Append("    local sx, sy = coords(treasure:getStartMapId())\n");
        sb.Append("    if not sx then giveUp(\"coordonnées de départ inconnues\") return true end\n");
        sb.Append("    local key = name .. \"|\" .. sx .. \",\" .. sy .. \"|\" .. dir\n");
        sb.Append("    if key ~= lastKey then lastKey, candidate = key, 1 end\n");
        sb.Append("    local maps = treasure.findClues(name, sx, sy, dir)\n");
        sb.Append("    local c = maps[candidate]\n");
        sb.Append("    if not c then giveUp(\"indice « \" .. name .. \" » introuvable (\" .. #maps .. \" carte(s) testée(s))\") return true end\n");
        sb.Append("    print(\"[chasse] \" .. name .. \" (\" .. dir .. \") -> [\" .. c.x .. \",\" .. c.y .. \"] à \" .. c.distance .. \" carte(s)\")\n");
        sb.Append("    local ok = true\n");
        sb.Append("    if c.mapId and getMapId() ~= c.mapId then ok = travelTo(c.mapId) elseif not c.mapId then ok = travelTo(c.x, c.y) end\n");
        sb.Append("    if not ok then print(\"[chasse] arrêt : trajet vers l'indice impossible, chasse conservée\") stopScript() return false end\n");
        sb.Append("    if ok and treasure:huntFlag() then fails, candidate = 0, 1\n");
        sb.Append("    else\n");
        sb.Append("      candidate = candidate + 1   -- cette carte n'a pas pris le drapeau : on essaie la suivante\n");
        sb.Append("      fails = fails + 1\n");
        sb.Append("      if fails >= MAX_FAILS then giveUp(\"drapeau refusé pour « \" .. name .. \" »\") end\n");
        sb.Append("    end\n");
        sb.Append("    return true\n");
        sb.Append("  end\n\n");
        sb.Append("  -- 5) étape inconnue : on laisse un peu de temps puis on abandonne si ça dure\n");
        sb.Append("  fails = fails + 1\n");
        sb.Append("  print(\"[chasse] étape inconnue (\" .. tostring(stype) .. \")\")\n");
        sb.Append("  if fails >= MAX_FAILS then giveUp(\"étape non reconnue\") end\n");
        sb.Append("  delay(2000)\n");
        sb.Append("  return true\n");
        sb.Append("end\n");
        return sb.ToString();
    }
}
