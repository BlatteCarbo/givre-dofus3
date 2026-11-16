-- Pandala-Farm-Prudent-1-3-Mobs-Recolte.lua
-- Farm prudent sur Pandala (secteur Plantala, 23 cartes observees en jeu) pour Shadowbot (Sram 110/120)
-- et Bastionbot (Sacrieur 160/120). Aucune restriction de nom : le profil est choisi par PROFILE.
--   - groupes de monstres de GROUP_MIN a GROUP_MAX (borne 1..3 par ce script) ;
--   - recolte des ressources de Pandala sur le trajet (gatherAll prend tout ce que le personnage sait recolter :
--     une ressource non debloquee est simplement ignoree) ;
--   - compte les defaites CONSECUTIVES : arret propre a MAX_CONSECUTIVE_DEFEATS (0 = illimite), une victoire remet a 0 ;
--   - depot en banque quand les pods atteignent PODS_MAX.
-- Aucune vente, aucun achat, aucun craft. Le combat est joue par le moteur (auto-combat).
-- Prudence : une defaite ou un combat bloque doit etre diagnostique avant de relancer (ne pas laisser tourner seul).

-- ======================================================================
-- CONFIGURATION (modifier ici uniquement)
-- ======================================================================
PODS_MAX = 85                 -- % de pods declenchant le retour en banque
MAX_CONSECUTIVE_DEFEATS = 3   -- arret apres N defaites d'affilee ; 0 = illimite ; une victoire remet a 0
BANK_MAP = 192415750          -- banque d'Astrub (interieur) ; le trajet depuis Pandala passe par le zaap/havre-sac
PROFILE = "auto"              -- "Shadowbot", "Bastionbot" ou "auto" (lit getCharacterName ; sinon profil par defaut)
HARVEST = true                -- recolter les ressources de Pandala sur le trajet
FIGHTS_PER_MAP = 2            -- combats maximum lances par passage sur une carte
MAX_FAILURES = 4              -- trajets echoues d'affilee avant arret
MAX_BANK_FAILURES = 3         -- depots echoues d'affilee avant arret
SUMMARY_EVERY = 10            -- bilan console tous les N passages
-- Profils : groupe min/max (1..3), PV minimum avant combat, attente maximale de regeneration (secondes).
PROFILES = {
    Shadowbot   = { groupMin = 1, groupMax = 2, minLife = 90, regenWait = 300 },
    Bastionbot = { groupMin = 1, groupMax = 3, minLife = 90, regenWait = 900 },
    default = { groupMin = 1, groupMax = 2, minLife = 90, regenWait = 300 },
}
MAX_PODS = PODS_MAX           -- variable lue par le moteur Givre
OPEN_BAGS = false
-- Cartes Pandala : secteur Plantala (23 cartes, plages d'identifiants observees pendant les runs d'expedition).
MAP_RANGES = { { 205520896, 205520903 }, { 205521409, 205521415 }, { 205521923, 205521927 }, { 205522436, 205522438 } }

-- ======================================================================
-- MOTEUR
-- ======================================================================
local TAG = "Pandala prudent"
local settings = PROFILES.default
local profileName = "default"
local maps = {}
for _, range in ipairs(MAP_RANGES) do
    for id = range[1], range[2] do maps[#maps + 1] = id end
end

local streak, wins, losses = 0, 0, 0
local stopAsked = false
local failures, bankFailures, banks = 0, 0, 0
local passes, fights, cursor = 0, 0, 0
local initialized = false
local gatherAtStart = 0
local harvestedTotal = 0

local function log(message)
    print("[" .. TAG .. "] " .. message)
end

local function haltWith(reason)
    printWarning("[" .. TAG .. "] ARRET " .. reason)
    return false
end

local function clampGroup(value, fallback)
    local n = math.floor(tonumber(value) or fallback)
    if n < 1 then n = 1 end
    if n > 3 then n = 3 end
    return n
end

local function printSummary(title)
    log("===== " .. title .. " =====")
    log("profil=" .. profileName .. " groupes=" .. MIN_MONSTERS .. ".." .. MAX_MONSTERS .. " passages=" .. passes
        .. " combats=" .. fights .. " victoires=" .. wins .. " defaites=" .. losses .. " serie=" .. streak
        .. "/" .. MAX_CONSECUTIVE_DEFEATS .. " recoltes=" .. harvestedTotal .. " banques=" .. banks
        .. " pods=" .. getPodsPercent() .. "% kamas=" .. getKamas())
end

local function setup()
    profileName = PROFILE
    if profileName == "auto" then profileName = getCharacterName() end
    settings = PROFILES[profileName]
    if not settings then
        settings = PROFILES.default
        profileName = "default"
    end
    local low = clampGroup(settings.groupMin, 1)
    local high = clampGroup(settings.groupMax, 2)
    if high < low then high = low end
    MIN_MONSTERS = low
    MAX_MONSTERS = high
    MIN_LIFE_PERCENT = settings.minLife
    config:setMinMonsters(low)
    config:setMaxMonsters(high)
    combat:setAutoFight(true)
    combat:setAutoPreFight(true)
    combat:setChallengeMode("off")
    initialized = true
    log("profil=" .. profileName .. " groupes " .. low .. ".." .. high .. " cartes=" .. #maps
        .. " recolte=" .. tostring(HARVEST) .. " arret=" .. MAX_CONSECUTIVE_DEFEATS .. " defaites consecutives")
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
    if getPodsPercent() >= PODS_MAX then return bank() end
    if settings.minLife > 0 and getLifePointsPercent() < settings.minLife then
        if not regenerate(settings.minLife, settings.regenWait) then
            if isInFight() then return true end
            return haltWith("regeneration non confirmee")
        end
    end
    cursor = cursor % #maps + 1
    local target = maps[cursor]
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
        if not fight() then break end
        fought = fought + 1
        fights = fights + 1
    end
    if stopAsked then return true end
    if HARVEST and not isDead() then
        local before = getCountGather()
        gatherAll()
        if isInFight() then return true end
        harvestedTotal = harvestedTotal + (getCountGather() - before)
    end
    passes = passes + 1
    log("PASSAGE carte=" .. target .. " combats=" .. fought .. " pods=" .. getPodsPercent() .. "%")
    if passes % SUMMARY_EVERY == 0 then printSummary("BILAN") end
    return true
end

function onFightEnd(result)
    if result and result.won == true then
        wins = wins + 1
        streak = 0
    else
        losses = losses + 1
        streak = streak + 1
        if MAX_CONSECUTIVE_DEFEATS > 0 and streak >= MAX_CONSECUTIVE_DEFEATS then stopAsked = true end
    end
    log("COMBAT victoire=" .. tostring(result and result.won) .. " defaites consecutives=" .. streak
        .. " (limite " .. MAX_CONSECUTIVE_DEFEATS .. ")")
end

function stopped(reason)
    printSummary("BILAN D'ARRET (" .. tostring(reason) .. ")")
end

log("pret : Shadowbot / Bastionbot, groupes 1..3, arret apres " .. MAX_CONSECUTIVE_DEFEATS .. " defaites consecutives (0 = illimite)")
