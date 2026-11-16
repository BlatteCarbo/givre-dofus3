-- Chasseur-Anomalies-Temporelles-Bonus.lua
-- Chasseur d'anomalies temporelles : farm de combat dans une sous-zone dont l'anomalie est OUVERTE (bonus de recompense).
--   - ANOMALY_ZONES : liste ordonnee de sous-zones candidates (sous-zone = identifiant OU nom, comme anomaly:isOpen) + cartes de combat.
--   - A chaque passage, la 1re zone de la liste dont l'anomalie est ouverte (anomaly:isOpen) est choisie ;
--     si aucune n'est ouverte, repli sur la zone par defaut (FALLBACK_TO_DEFAULT = true) ou attente (false).
--   - L'anomalie est re-verifiee avant chaque combat : fermeture = retour a la selection au passage suivant.
--   - Groupes de MIN_MONSTERS a MAX_MONSTERS (bornes 1..MONSTERS_HARD_CAP imposees par le script).
--   - MAX_CONSECUTIVE_DEFEATS : arret propre apres N defaites d'affilee (0 = illimite), une victoire remet la serie a 0.
--   - Depot en banque automatique quand les pods atteignent PODS_MAX.
-- Aucune vente, aucun achat, aucun craft. Le combat est joue par le moteur (auto-combat).
-- Cartes : Plantala (Pandala) = plages observees en jeu ; Cimetiere d'Incarnam = sept cartes exterieures validees (docs/codex/DAKAL_LIVE).
-- Le nom de sous-zone "Plantala" n'est pas confirme dans la base : si anomaly:isOpen ne repond jamais vrai, remplacer par l'identifiant
-- numerique de la sous-zone (onglet Expedition > anomalies). Rien n'a ete lance sur un compte reel.

-- ======================================================================
-- CONFIGURATION (modifier ici uniquement)
-- ======================================================================
PODS_MAX = 85                 -- % de pods declenchant le retour en banque
MAX_CONSECUTIVE_DEFEATS = 3   -- arret apres N defaites d'affilee ; 0 = illimite ; une victoire remet a 0
BANK_MAP = 192415750          -- banque d'Astrub (interieur) ; trajet long depuis Pandala
MIN_MONSTERS = 1              -- taille minimale d'un groupe attaque
MAX_MONSTERS = 3              -- taille maximale d'un groupe attaque
MONSTERS_HARD_CAP = 8         -- plafond absolu impose a MAX_MONSTERS (un groupe de Dofus 3 ne depasse pas 8)
FALLBACK_TO_DEFAULT = true    -- true : aucune anomalie ouverte -> farm de la zone par defaut ; false : attendre l'ouverture
DEFAULT_ZONE = 1              -- rang (dans la liste des zones valides) de la zone par defaut
CLOSED_WAIT_MS = 60000        -- pause entre deux verifications quand on attend une anomalie (FALLBACK_TO_DEFAULT = false)
MAX_IDLE_CHECKS = 0           -- verifications a vide avant arret quand on attend ; 0 = attendre sans limite
FIGHTS_PER_MAP = 2            -- combats maximum lances par passage sur une carte
MIN_LIFE_PERCENT = 90         -- regeneration avant de repartir (pourcentage de PV)
REGEN_MAX_WAIT = 300          -- attente maximale de regeneration, en secondes
MAX_FAILURES = 4              -- trajets echoues d'affilee avant arret
MAX_BANK_FAILURES = 3         -- depots echoues d'affilee avant arret
SUMMARY_EVERY = 10            -- bilan console tous les N passages

-- subarea = identifiant ou nom de sous-zone ; maps = identifiants de cartes ; ranges = plages { debut, fin } ;
-- minLevel / maxLevel = fenetre de niveau du personnage (0 = pas de limite). L'ordre de la liste est l'ordre de priorite.
ANOMALY_ZONES = {
    { name = "Plantala (Pandala)", subarea = "Plantala", minLevel = 100, maxLevel = 0,
      ranges = { { 205520896, 205520903 }, { 205521409, 205521415 }, { 205521923, 205521927 }, { 205522436, 205522438 } } },
    { name = "Cimetiere (Incarnam)", subarea = 449, minLevel = 1, maxLevel = 25,
      maps = { 153880064, 153880578, 153881090, 153881089, 153881088, 153881600, 153881601 } },
}

-- ======================================================================
-- MOTEUR (ne pas modifier sans raison)
-- Le moteur Givre lit MAX_PODS, MIN_LIFE_PERCENT, OPEN_BAGS, MIN/MAX_MONSTERS.
-- ======================================================================
MAX_PODS = PODS_MAX
OPEN_BAGS = false

local TAG = "Anomalie bonus"
local zones, defaultZone = {}, nil
local streak, wins, losses = 0, 0, 0
local stopAsked = false
local failures, bankFailures, banks = 0, 0, 0
local passes, fights = 0, 0
local cursors, stats = {}, {}
local idleChecks = 0
local initialized = false
local currentZone, currentBonus = nil, false
local lastMode = ""
local apiWarned = false

local function log(message)
    print("[" .. TAG .. "] " .. message)
end

local function haltWith(reason)
    printWarning("[" .. TAG .. "] ARRET " .. reason)
    return false
end

local function clampInt(value, fallback, low, high)
    local n = math.floor(tonumber(value) or fallback)
    if n < low then n = low end
    if n > high then n = high end
    return n
end

local function statOf(zone)
    if not stats[zone.index] then stats[zone.index] = { bonus = 0, fallback = 0, wins = 0, losses = 0 } end
    return stats[zone.index]
end

local function buildZones()
    zones = {}
    for _, entry in ipairs(ANOMALY_ZONES) do
        local list = {}
        for _, id in ipairs(entry.maps or {}) do list[#list + 1] = id end
        for _, range in ipairs(entry.ranges or {}) do
            for id = range[1], range[2] do list[#list + 1] = id end
        end
        if entry.subarea ~= nil and #list > 0 then
            local index = #zones + 1
            zones[index] = {
                index = index,
                name = entry.name or tostring(entry.subarea),
                subarea = entry.subarea,
                maps = list,
                minLevel = tonumber(entry.minLevel) or 0,
                maxLevel = tonumber(entry.maxLevel) or 0,
            }
            cursors[index] = 0
        end
    end
    defaultZone = zones[DEFAULT_ZONE] or zones[1]
end

local function setup()
    buildZones()
    local low = clampInt(MIN_MONSTERS, 1, 1, MONSTERS_HARD_CAP)
    local high = clampInt(MAX_MONSTERS, low, 1, MONSTERS_HARD_CAP)
    if high < low then high = low end
    MIN_MONSTERS = low
    MAX_MONSTERS = high
    config:setMinMonsters(low)
    config:setMaxMonsters(high)
    initialized = true
    log("zones=" .. #zones .. " groupes " .. low .. ".." .. high .. " repli=" .. tostring(FALLBACK_TO_DEFAULT)
        .. " arret=" .. MAX_CONSECUTIVE_DEFEATS .. " defaites consecutives")
end

local function printSummary(title)
    log("===== " .. title .. " =====")
    log("passages=" .. passes .. " combats=" .. fights .. " victoires=" .. wins .. " defaites=" .. losses
        .. " serie=" .. streak .. "/" .. MAX_CONSECUTIVE_DEFEATS .. " banques=" .. banks
        .. " pods=" .. getPodsPercent() .. "% kamas=" .. getKamas())
    for _, zone in ipairs(zones) do
        local row = statOf(zone)
        log("ZONE " .. zone.name .. " : combats bonus=" .. row.bonus .. " combats repli=" .. row.fallback
            .. " victoires=" .. row.wins .. " defaites=" .. row.losses)
    end
end

local function isOpen(zone)
    if not anomaly then
        if not apiWarned then
            apiWarned = true
            printWarning("[" .. TAG .. "] module anomaly indisponible : aucune anomalie ne sera detectee")
        end
        return false
    end
    return anomaly:isOpen(zone.subarea) == true
end

local function levelAllowed(zone)
    local level = tonumber(getCharacterLevel()) or 0
    if zone.minLevel > 0 and level < zone.minLevel then return false end
    if zone.maxLevel > 0 and level > zone.maxLevel then return false end
    return true
end

-- Zone de repli : la zone par defaut si le niveau le permet, sinon la premiere zone autorisee.
local function fallbackZone()
    if defaultZone and levelAllowed(defaultZone) then return defaultZone end
    for _, zone in ipairs(zones) do
        if levelAllowed(zone) then return zone end
    end
    return nil
end

-- Retourne la zone choisie et true si son anomalie est ouverte ; zone de repli (false) sinon ; nil si on attend.
local function selectZone()
    for _, zone in ipairs(zones) do
        if levelAllowed(zone) and isOpen(zone) then return zone, true end
    end
    if FALLBACK_TO_DEFAULT then return fallbackZone(), false end
    return nil, false
end

local function announceMode(zone, bonus)
    local mode = zone and (zone.name .. (bonus and "#bonus" or "#repli")) or "attente"
    if mode == lastMode then return end
    lastMode = mode
    if not zone then
        log("AUCUNE ANOMALIE OUVERTE : attente (repli desactive ou aucune zone autorisee pour ce niveau)")
    elseif bonus then
        printSuccess("[" .. TAG .. "] ANOMALIE OUVERTE : " .. zone.name .. " (bonus de recompense actif)")
    else
        log("AUCUNE ANOMALIE OUVERTE : repli sur la zone par defaut " .. zone.name .. " (sans bonus)")
    end
end

local function arrived(id)
    return getMapId() == id
end

function bank()
    if isInFight() then return true end
    if stopAsked then return haltWith("serie de defaites : pas de depot automatique") end
    leaveDialog()
    local before = getPods()
    log("BANQUE debut pods=" .. getPodsPercent() .. "%")
    local problem = nil
    if not travelTo(BANK_MAP) or not arrived(BANK_MAP) then
        problem = "trajet banque non confirme"
    elseif not npc:npcBank() then
        problem = "ouverture banque refusee (frais ou solde ?)"
    else
        local deposited = exchange:putAllItems()
        leaveDialog()
        if not deposited or getPods() >= before then problem = "depot non confirme" end
    end
    if isInFight() then return true end
    if problem then
        bankFailures = bankFailures + 1
        printWarning("[" .. TAG .. "] BANQUE " .. problem .. " " .. bankFailures .. "/" .. MAX_BANK_FAILURES)
        if bankFailures >= MAX_BANK_FAILURES then return haltWith("banque inutilisable") end
        delay(15000)
        return true
    end
    bankFailures = 0
    banks = banks + 1
    printSuccess("[" .. TAG .. "] BANQUE depot confirme pods " .. before .. " -> " .. getPods())
    printSummary("BILAN BANQUE")
    return true
end

function move()
    if isInFight() then return true end
    if not initialized then setup() end
    if stopAsked then
        printSummary("BILAN FINAL")
        return haltWith(streak .. " defaites consecutives (MAX_CONSECUTIVE_DEFEATS=" .. MAX_CONSECUTIVE_DEFEATS .. ")")
    end
    if isDead() then return haltWith("personnage mort") end
    if #zones == 0 then return haltWith("ANOMALY_ZONES vide ou sans carte") end
    if getPodsPercent() >= PODS_MAX then return bank() end
    if MIN_LIFE_PERCENT > 0 and getLifePointsPercent() < MIN_LIFE_PERCENT then
        if not regenerate(MIN_LIFE_PERCENT, REGEN_MAX_WAIT) then
            if isInFight() then return true end
            return haltWith("regeneration non confirmee")
        end
    end
    local zone, bonus = selectZone()
    currentZone, currentBonus = zone, bonus
    announceMode(zone, bonus)
    if not zone then
        idleChecks = idleChecks + 1
        if MAX_IDLE_CHECKS > 0 and idleChecks >= MAX_IDLE_CHECKS then
            return haltWith("aucune anomalie ouverte apres " .. idleChecks .. " verifications")
        end
        delay(CLOSED_WAIT_MS)
        return true
    end
    idleChecks = 0
    cursors[zone.index] = cursors[zone.index] % #zone.maps + 1
    local target = zone.maps[cursors[zone.index]]
    if not arrived(target) and (not travelTo(target) or not arrived(target)) then
        if isInFight() then return true end
        failures = failures + 1
        printWarning("[" .. TAG .. "] trajet non confirme vers " .. target .. " (" .. failures .. "/" .. MAX_FAILURES .. ")")
        if failures >= MAX_FAILURES then return haltWith("trop de trajets echoues consecutifs") end
        delay(2000)
        return true
    end
    failures = 0
    local fought = 0
    while fought < FIGHTS_PER_MAP and not stopAsked and not isDead() do
        if getPodsPercent() >= PODS_MAX then break end
        if bonus and not isOpen(zone) then
            log("anomalie fermee pendant le farm de " .. zone.name .. " : nouvelle selection au prochain passage")
            break
        end
        if not fight() then break end
        fought = fought + 1
        fights = fights + 1
        local row = statOf(zone)
        if bonus then row.bonus = row.bonus + 1 else row.fallback = row.fallback + 1 end
    end
    passes = passes + 1
    log("PASSAGE " .. zone.name .. " carte=" .. target .. " combats=" .. fought .. " bonus=" .. tostring(bonus)
        .. " pods=" .. getPodsPercent() .. "%")
    if passes % SUMMARY_EVERY == 0 then printSummary("BILAN") end
    return true
end

function onFightEnd(result)
    local row = currentZone and statOf(currentZone) or nil
    if result and result.won == true then
        wins = wins + 1
        streak = 0
        if row then row.wins = row.wins + 1 end
    else
        losses = losses + 1
        streak = streak + 1
        if row then row.losses = row.losses + 1 end
        if MAX_CONSECUTIVE_DEFEATS > 0 and streak >= MAX_CONSECUTIVE_DEFEATS then stopAsked = true end
    end
    log("COMBAT victoire=" .. tostring(result and result.won) .. " defaites consecutives=" .. streak
        .. " (limite " .. MAX_CONSECUTIVE_DEFEATS .. ")")
end

function stopped(reason)
    printSummary("BILAN D'ARRET (" .. tostring(reason) .. ")")
end

log("pret : anomalies temporelles, groupes " .. MIN_MONSTERS .. ".." .. MAX_MONSTERS .. ", arret apres "
    .. MAX_CONSECUTIVE_DEFEATS .. " defaites consecutives (0 = illimite)")
