-- Preparation-Almanax-Et-Artisanat-Quotidien.lua
-- Constructeur de stock quotidien multi-ressources pour les offrandes Almanax et les crafts courants.
-- Ressources suivies (quotas configurables dans RESOURCES) :
--   Alchimiste : Ortie, Sauge, Trefle      Paysan : Ble, Orge
--   Bucheron   : Frene, Chataignier        Mineur : Fer, Cuivre
-- Principe : la liste RESOURCES est parcourue dans l'ordre ; le script recolte (gatherAll, sans filtre) sur les cartes de la
-- premiere ressource dont le stock est sous le quota, puis passe a la suivante. Quand tous les quotas sont atteints
-- (ou que les ressources restantes sont inaccessibles / hors niveau), il depose le sac, affiche le bilan et s'arrete.
-- Le stock compte = INITIAL_STOCK (a renseigner a la main, banque comprise) + recoltes de la session (suivies par differences de sac).
-- Aucune vente, aucun achat, aucun craft : le script prepare seulement la matiere premiere.
-- Cartes : identifiants verifies (Astrub : scripts Nuit-* et Mineur-1-200 ; Amakna : circuits Bastionbot-Bucheron), sinon
-- coordonnees "x,y" du rapport de recherche 03, NON validees en jeu : l'arrivee est verifiee et une carte en echec est ecartee.

-- ======================================================================
-- CONFIGURATION (modifier ici uniquement)
-- ======================================================================
PODS_MAX = 85                 -- % de pods declenchant le retour en banque
MAX_CONSECUTIVE_DEFEATS = 3   -- arret apres N defaites protecteur d'affilee ; 0 = illimite ; une victoire remet a 0
BANK_MAP = 192415750          -- banque d'Astrub (interieur), carte deja utilisee par les scripts du depot
MIN_LIFE_PERCENT = 90         -- regeneration avant de repartir (pourcentage de PV)
REGEN_MAX_WAIT = 180          -- attente maximale de regeneration, en secondes
JOB_LEVEL_OVERRIDE = 0        -- > 0 : force un niveau de metier (test ponctuel)
MAX_FAILURES = 6              -- trajets echoues d'affilee avant arret
MAX_BANK_FAILURES = 3         -- depots echoues d'affilee avant arret
BLOCK_VISITS = 30             -- une carte en echec est ecartee pour N visites
EMPTY_PAUSE_MS = 60000        -- pause quand toutes les cartes d'une ressource sont vides (repousse)
MAX_EMPTY_ROUNDS = 3          -- tours a vide d'une ressource avant de la mettre de cote pour la session
SUMMARY_EVERY = 10            -- bilan console tous les N passages
FINAL_DEPOSIT = true          -- deposer le sac une derniere fois quand tous les quotas sont atteints
-- Stock deja possede (banque + sac) par ressource : { Ortie = 40, Sauge = 12 }.
INITIAL_STOCK = {}

-- key = nom affiche ; quota = stock vise ; job / level = metier et niveau minimal ; patterns = motifs Lua (nom en minuscules,
-- ".-" absorbe l'accent eventuel ; \195\169 = e accent aigu en UTF-8) ; maps = { identifiant de carte OU "x,y", libelle }
RESOURCES = {
    { key = "Ortie", quota = 100, job = 26, level = 1, patterns = { "^ortie$" },
      maps = { {188744197, "2,-17"}, {188744710, "3,-16"}, {188745223, "4,-15"}, {188744706, "3,-20"}, {188743683, "1,-19"} } },
    { key = "Sauge", quota = 100, job = 26, level = 20, patterns = { "^sauge$" },
      maps = { {"-28,-35", "-28,-35"} } },
    { key = "Trefle", quota = 60, job = 26, level = 40, patterns = { "^tr.-fle" },
      maps = { {"-1,13", "-1,13"} } },
    { key = "Ble", quota = 100, job = 28, level = 1, patterns = { "^ble$", "^bl\195\169$" },
      maps = { {"7,-25", "7,-25"}, {"6,-24", "6,-24"}, {"3,13", "3,13"}, {"5,13", "5,13"} } },
    { key = "Orge", quota = 100, job = 28, level = 20, patterns = { "^orge$" },
      maps = { {"2,5", "2,5"}, {"5,8", "5,8"} } },
    { key = "Frene", quota = 100, job = 2, level = 1, patterns = { "^fr.-ne$" },
      maps = { {88082192, "4,7"}, {88081680, "3,7"}, {88081679, "3,8"}, {88081678, "3,9"}, {88081166, "2,9"}, {88080654, "1,9"},
               {88212238, "0,9"}, {88212239, "0,8"}, {88080655, "1,8"} } },
    { key = "Chataignier", quota = 100, job = 2, level = 20, patterns = { "^ch.-taignier$" },
      maps = { {88081415, "3,30"}, {88080903, "2,30"}, {88080391, "1,30"}, {88080902, "2,29"}, {88081414, "3,29"},
               {88081926, "4,29"}, {88082438, "5,29"}, {88082439, "5,30"}, {88081927, "4,30"} } },
    { key = "Fer", quota = 100, job = 24, level = 1, patterns = { "^fer$" },
      maps = { {188483076, "2,-18"}, {188484100, "3,-17"}, {188482052, "1,-17"} } },
    { key = "Cuivre", quota = 100, job = 24, level = 20, patterns = { "^cuivre$" },
      maps = { {188483076, "2,-18"}, {188484100, "3,-17"}, {188482052, "1,-17"} } },
}
JOB_LABELS = { [26] = "Alchimiste", [28] = "Paysan", [2] = "Bucheron", [24] = "Mineur" }

-- ======================================================================
-- MOTEUR (ne pas modifier sans raison)
-- Le moteur Givre lit MAX_PODS, MIN_LIFE_PERCENT, OPEN_BAGS, MIN/MAX_MONSTERS.
-- ======================================================================
MAX_PODS = PODS_MAX
OPEN_BAGS = false
MIN_MONSTERS = 1
MAX_MONSTERS = 1 -- aucun combat volontaire ; seuls les protecteurs de ressources se battent

local TAG = "Almanax-Artisanat"
local BAR_WIDTH = 20
local streak, wins, losses = 0, 0, 0
local stopAsked = false
local failures, bankFailures, banks = 0, 0, 0
local passes, visits, unknownLevelReads = 0, 0, 0
local sinceBank = 0
local initialized, synced = false, false
local state = {}
local levels = {}
local active = nil

local function log(message)
    print("[" .. TAG .. "] " .. message)
end

local function haltWith(reason)
    printWarning("[" .. TAG .. "] ARRET " .. reason)
    return false
end

local function setup()
    state = {}
    for index, res in ipairs(RESOURCES) do
        local start = tonumber(INITIAL_STOCK[res.key]) or 0
        state[index] = { res = res, stock = start, session = 0, cursor = 0, blocked = {}, emptyRounds = 0, skipped = nil }
    end
    initialized = true
    log("ressources=" .. #state .. " banque a " .. PODS_MAX .. "% pods, arret apres " .. MAX_CONSECUTIVE_DEFEATS
        .. " defaites consecutives (0 = illimite)")
end

local function readLevels()
    local any = false
    for _, row in ipairs(state) do
        local job = row.res.job
        if levels[job] == nil or levels[job] == 0 then
            local level = tonumber(getJobLevel(job)) or 0
            if JOB_LEVEL_OVERRIDE > 0 then level = JOB_LEVEL_OVERRIDE end
            levels[job] = level
        end
        if levels[job] > 0 then any = true end
    end
    return any
end

local function deficit(row)
    return row.res.quota - row.stock
end

local function percentOf(row)
    if row.res.quota <= 0 then return 100 end
    return math.min(100, math.floor(row.stock * 100 / row.res.quota))
end

local function bar(row)
    local filled = math.floor(percentOf(row) * BAR_WIDTH / 100)
    return "[" .. string.rep("#", filled) .. string.rep("-", BAR_WIDTH - filled) .. "]"
end

local function statusOf(row)
    if deficit(row) <= 0 then return "OK" end
    if row.skipped then return "MIS DE COTE : " .. row.skipped end
    if (levels[row.res.job] or 0) < row.res.level then
        return "niveau " .. (JOB_LABELS[row.res.job] or row.res.job) .. " " .. row.res.level .. " requis"
    end
    return "A RECOLTER"
end

local function printSummary(title)
    log("===== " .. title .. " =====")
    local complete, total = 0, #state
    for _, row in ipairs(state) do
        if deficit(row) <= 0 then complete = complete + 1 end
        log("STOCK " .. row.res.key .. " " .. row.stock .. "/" .. row.res.quota .. " " .. bar(row) .. " " .. percentOf(row)
            .. "% (session +" .. row.session .. ") " .. statusOf(row))
    end
    log("QUOTAS atteints " .. complete .. "/" .. total .. " | passages=" .. passes .. " | banques=" .. banks
        .. " | pods=" .. getPodsPercent() .. "% | victoires=" .. wins .. " defaites=" .. losses)
end

local function usable(row)
    return deficit(row) > 0 and not row.skipped and (levels[row.res.job] or 0) >= row.res.level
end

local function pickResource()
    for _, row in ipairs(state) do
        if usable(row) then return row end
    end
    return nil
end

local function arrived(ref)
    if type(ref) == "number" then return getMapId() == ref end
    return getCurrentPos() == ref
end

-- ref = identifiant de carte (nombre) ou coordonnees "x,y" (texte).
local function goTo(ref)
    if arrived(ref) then return true end
    local ok
    if type(ref) == "number" then
        ok = travelTo(ref)
    else
        local x, y = string.match(ref, "^(-?%d+),(-?%d+)$")
        ok = travelTo(tonumber(x), tonumber(y))
    end
    return ok and arrived(ref)
end

local function nextMap(row)
    for _ = 1, #row.res.maps do
        row.cursor = row.cursor % #row.res.maps + 1
        local entry = row.res.maps[row.cursor]
        local until_ = row.blocked[tostring(entry[1])]
        if not until_ or visits >= until_ then return entry end
    end
    return nil
end

local function bagSnapshot()
    local snapshot = {}
    for _, item in ipairs(inventory:items()) do
        if item.pos == 63 then
            local row = snapshot[item.gid]
            if row then row.qty = row.qty + item.qty else snapshot[item.gid] = { name = item.name, qty = item.qty } end
        end
    end
    return snapshot
end

local function rowOfName(name)
    local lower = string.lower(tostring(name))
    for _, row in ipairs(state) do
        for _, pattern in ipairs(row.res.patterns) do
            if string.find(lower, pattern) then return row end
        end
    end
    return nil
end

local function recordGains(before, after)
    local total = 0
    for gid, item in pairs(after) do
        local gain = item.qty - (before[gid] and before[gid].qty or 0)
        if gain > 0 then
            local row = rowOfName(item.name)
            if row then
                row.stock = row.stock + gain
                row.session = row.session + gain
                total = total + gain
            end
        end
    end
    sinceBank = sinceBank + total
    return total
end

function bank()
    if isInFight() then return true end
    if stopAsked then return haltWith("serie de defaites : pas de depot automatique") end
    leaveDialog()
    local before = getPods()
    log("BANQUE debut pods=" .. getPodsPercent() .. "% kamas=" .. getKamas())
    local problem = nil
    if not goTo(BANK_MAP) then
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
    sinceBank = 0
    synced = false
    printSuccess("[" .. TAG .. "] BANQUE depot confirme pods " .. before .. " -> " .. getPods())
    printSummary("BILAN BANQUE")
    return true
end

local function finish()
    if FINAL_DEPOSIT and sinceBank > 0 then return bank() end
    printSummary("BILAN FINAL")
    printSuccess("[" .. TAG .. "] TERMINE : plus aucune ressource a recolter (quotas atteints ou ressources mises de cote)")
    return false
end

function move()
    if isInFight() then return true end
    if not initialized then setup() end
    if stopAsked then
        printSummary("BILAN FINAL")
        return haltWith(streak .. " defaites consecutives (MAX_CONSECUTIVE_DEFEATS=" .. MAX_CONSECUTIVE_DEFEATS .. ")")
    end
    if isDead() then return haltWith("personnage mort") end
    if not readLevels() then
        unknownLevelReads = unknownLevelReads + 1
        if unknownLevelReads >= 6 then return haltWith("niveau de metier inconnu apres six lectures") end
        log("niveau de metier en attente, nouvelle lecture dans 10 s")
        delay(10000)
        return true
    end
    unknownLevelReads = 0
    if not synced then
        if not map:sendMapRequest() then
            log("resynchronisation de la carte en attente")
            delay(5000)
            return true
        end
        synced = true
    end
    if getPodsPercent() >= PODS_MAX then return bank() end
    local row = pickResource()
    if not row then return finish() end
    if row ~= active then
        active = row
        log("RESSOURCE " .. row.res.key .. " : stock " .. row.stock .. "/" .. row.res.quota .. ", reste " .. deficit(row))
    end
    if MIN_LIFE_PERCENT > 0 and getLifePointsPercent() < MIN_LIFE_PERCENT then
        if not regenerate(MIN_LIFE_PERCENT, REGEN_MAX_WAIT) then
            if isInFight() then return true end
            return haltWith("regeneration non confirmee")
        end
    end
    visits = visits + 1
    local entry = nextMap(row)
    if not entry then
        row.skipped = "toutes les cartes sont inaccessibles"
        printWarning("[" .. TAG .. "] " .. row.res.key .. " mise de cote : " .. row.skipped)
        return true
    end
    if not goTo(entry[1]) then
        if isInFight() then return true end
        row.blocked[tostring(entry[1])] = visits + BLOCK_VISITS
        failures = failures + 1
        printWarning("[" .. TAG .. "] trajet non confirme vers " .. entry[2] .. " (" .. failures .. "/" .. MAX_FAILURES .. ")")
        if failures >= MAX_FAILURES then return haltWith("trop de trajets echoues consecutifs") end
        delay(2000)
        return true
    end
    failures = 0
    local before = bagSnapshot()
    local gathered = gatherAll()
    if isInFight() then return true end
    if stopAsked then return true end
    local gained = recordGains(before, bagSnapshot())
    passes = passes + 1
    if gathered or gained > 0 then
        row.emptyRounds = 0
    else
        row.emptyRounds = row.emptyRounds + 1
        if row.emptyRounds >= #row.res.maps * MAX_EMPTY_ROUNDS then
            row.skipped = "cartes vides apres " .. row.emptyRounds .. " passages"
            printWarning("[" .. TAG .. "] " .. row.res.key .. " mise de cote : " .. row.skipped)
        elseif row.emptyRounds % #row.res.maps == 0 then
            log("toutes les cartes de " .. row.res.key .. " sont vides : attente de repousse " .. (EMPTY_PAUSE_MS / 1000) .. " s")
            delay(EMPTY_PAUSE_MS)
        end
    end
    log("PASSAGE " .. row.res.key .. " carte=" .. entry[2] .. " gain=" .. gained .. " stock=" .. row.stock .. "/" .. row.res.quota
        .. " pods=" .. getPodsPercent() .. "%")
    if passes % SUMMARY_EVERY == 0 then printSummary("BILAN") end
    return true
end

function onFightEnd(result)
    synced = false
    if result and result.won == true then
        wins = wins + 1
        streak = 0
    else
        losses = losses + 1
        streak = streak + 1
        if MAX_CONSECUTIVE_DEFEATS > 0 and streak >= MAX_CONSECUTIVE_DEFEATS then stopAsked = true end
    end
    log("PROTECTEUR victoire=" .. tostring(result and result.won) .. " defaites consecutives=" .. streak)
end

function stopped(reason)
    if not initialized then setup() end
    printSummary("BILAN D'ARRET (" .. tostring(reason) .. ")")
end

log("pret : stock quotidien Almanax / artisanat, banque a " .. PODS_MAX .. "% pods, arret apres "
    .. MAX_CONSECUTIVE_DEFEATS .. " defaites consecutives (0 = illimite)")
