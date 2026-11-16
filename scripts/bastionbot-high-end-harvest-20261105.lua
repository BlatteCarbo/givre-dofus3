-- Bastionbot: ressources Frigost, depot en banque, aucune vente ni achat.
MAX_PODS = 85
OPEN_BAGS = false
MIN_LIFE_PERCENT = 0
local maps = {54168355,54169377,54169888,54169378,54168869,54168357,
    54162742,54162232,54161209,54162235,54163261,54162752}
local point = 1
local losses = 0
local failures = 0
local initialized = false

function onFightEnd(result)
    if not result or result.won ~= true then losses = losses + 1 end
    print("PROTECTEUR victoire=" .. tostring(result and result.won) .. " defaites=" .. losses)
end

function bank()
    if losses > 0 or isInFight() then return false end
    leaveDialog()
    if not travelTo(54534165) or getMapId() ~= 54534165 then
        printError("ARRET banque Frigost inaccessible") return false
    end
    if not npc:npcBank() then printError("ARRET ouverture banque refusee") return false end
    local before = getPods()
    local deposited = exchange:putAllItems()
    leaveDialog()
    if not deposited or getPods() >= before or getPodsPercent() >= MAX_PODS then
        printError("ARRET depot non confirme") return false
    end
    printSuccess("BANQUE depot confirme " .. before .. "->" .. getPods())
    return true
end

function move()
    if getCharacterName() ~= "Bastionbot" then printError("Mauvais personnage") return false end
    if isInFight() then return true end
    if losses > 0 or isDead() then printError("ARRET apres combat perdu ou mort") return false end
    if not initialized then
        combat:setAutoFight(true)
        combat:setAutoSpells(true)
        combat:setAutoPreFight(true)
        combat:setChallengeMode("off")
        initialized = true
        print("FRIGOST ressources accessibles: Frostiz, Perce-Neige, Tremble et environs; aucune vente")
    end
    -- Observed passive recovery is about 2 HP/s; 180s cannot recover
    -- from the protector's 57 percent to 90 percent of 3063 HP.
    if character:lifePointsP() < 90 and not regenerate(90,900) then
        printError("ARRET regeneration non confirmee") return false
    end
    if getPodsPercent() >= MAX_PODS then return bank() end
    local target = maps[point]
    if getMapId() ~= target and (not travelTo(target) or getMapId() ~= target) then
        if isInFight() then return true end
        failures = failures + 1
        point = point % #maps + 1
        printWarning("FRIGOST carte inaccessible " .. target .. " echecs=" .. failures)
        if failures >= 3 then printError("ARRET trois trajets refuses") return false end
        return true
    end
    failures = 0
    local before = getCountGather()
    gatherAll()
    if isInFight() then return true end
    if losses > 0 then return false end
    print("FRIGOST carte=" .. target .. " recoltes=" .. (getCountGather()-before) .. " pods=" .. getPodsPercent())
    point = point % #maps + 1
    return true
end
