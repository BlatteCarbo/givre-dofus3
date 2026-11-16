using System;
using System.Threading.Tasks;
using GivreClone.App.Services.DofusDude;

namespace GivreClone.App.Services.Treasure;

/// <summary>
/// Générateur de script Lua pour la quête quotidienne automatique de l'Almanax (1 Clic).
/// Utilise DofusDudeClient pour récupérer l'offrande, le nombre requis et les gains.
/// </summary>
public static class AlmanaxScript
{
    public const long BibliotempleZaapMapId = 144449; // Zaap Bibliotemple / Plaine des Porkass (-4,-24)
    public const long BibliotempleNpcMapId = 84674563; // Intérieur Bibliotemple
    public const int TheodoranAxNpcId = 2471; // PNJ Théodoran Ax

    /// <summary>
    /// Génère le script Lua complet et autonome pour valider l'Almanax du jour.
    /// </summary>
    public static async Task<string> BuildTodayScriptAsync()
    {
        var almanax = await DofusDudeClient.GetTodayAlmanaxAsync().ConfigureAwait(false);
        int itemGid = almanax?.ItemGid ?? 0;
        string itemName = almanax?.ItemName ?? "Objet du jour";
        int rewardKamas = almanax?.RewardKamas ?? 0;
        string bonus = almanax?.BonusDescription ?? "";

        return $@"-- Script Almanax Quotidien Automatique (DofusDude)
-- Date : {almanax?.Date ?? DateTime.UtcNow.ToString("yyyy-MM-dd")}
-- Offrande : {itemName} (GID: {itemGid})
-- Gain Kamas : {rewardKamas} | Bonus : {bonus}

local ITEM_GID = {itemGid}
local ITEM_QTY = 1
local BIBLIO_ZAAP = 144449
local BIBLIO_INSIDE = 84674563
local NPC_ID = 2471

local function log(msg)
    print('[Almanax 1-Clic] ' .. msg)
end

function move()
    if isInFight() or isDead() then return true end

    -- 1. Verifie si l'offrande est deja dans l'inventaire
    local count = inventory:itemCount(ITEM_GID)
    if count < ITEM_QTY then
        log('Offrande manquante : ' .. count .. '/' .. ITEM_QTY .. ' de {itemName}.')
        log('Achat automatique en HDV ou retrait en banque necessaire...')
        -- Si en HDV ressources, achete le lot necessaire
        if getMapId() == 191104004 or getMapId() == 212601350 then
            log('Achat en HDV...')
            bid:buyItem(ITEM_GID, ITEM_QTY)
            delay(1500)
            return true
        else
            log('Voyage vers l\'HDV pour achat...')
            travelTo(191104004)
            return true
        end
    end

    -- 2. Voyage vers le Bibliotemple
    if getMapId() ~= BIBLIO_INSIDE then
        log('Offrande prete ! En route vers le Bibliotemple...')
        travelTo(BIBLIO_INSIDE)
        return true
    end

    -- 3. Validation de la quete avec Theodoran Ax
    log('Validation de l\'Almanax avec Theodoran Ax...')
    if npc:talkToNpc(NPC_ID) then
        delay(1000)
        npc:reply(1) -- Accepter / Donner l'offrande
        delay(1000)
        leaveDialog()
        log('Quete de l\'Almanax VALIDE ! Recompense : +{rewardKamas} kamas.')
        stopScript()
    end

    return true
end
";
    }
}
