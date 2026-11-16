-- Bucheron-1-200-Planches-Substrats.lua
-- Bucheron (JOB_ID 2) : boucle progressive 1 -> 200 par niveau de metier.
--   Frene -> Chataignier -> Noyer/Chene -> Bombu/Oliviolet -> Erable/If -> Bambou/Merisier
--   -> Orme/Bambou Sombre -> Tremble/Bambou Sacre
-- Recolte sans filtre (gatherAll). Banque a PODS_MAX (le bois est lourd : ne jamais garder de buches en sac).
-- Les planches et substrats ne sont PAS craftes ici : la console annonce quand le niveau permet de les fabriquer
-- et resume le stock de bois recolte (preparation des substrats, qui demandent aussi des potions d'alchimiste).
-- Cartes : sources exactes des circuits du script Bastionbot-Bucheron-1-200-ameliore.lua (paliers 1..160).
-- ======================================================================
-- CONFIGURATION (modifier ici uniquement)
-- ======================================================================
PODS_MAX = 85                 -- % de pods declenchant le retour en banque
MAX_CONSECUTIVE_DEFEATS = 3   -- arret apres N defaites protecteur d'affilee ; 0 = illimite ; une victoire remet a 0
BANK_MAP = 192415750          -- banque d'Astrub (interieur), carte deja utilisee par les scripts du depot
MIN_LIFE_PERCENT = 90         -- regeneration avant de repartir (pourcentage de PV)
REGEN_MAX_WAIT = 180          -- attente maximale de regeneration, en secondes
JOB_LEVEL_OVERRIDE = 0        -- > 0 : force un niveau de metier (test ponctuel)
TIERS_IN_ROTATION = 3         -- nombre de paliers debloques les plus hauts parcourus en rotation
MAX_FAILURES = 6              -- trajets echoues d'affilee avant arret
MAX_BANK_FAILURES = 3         -- depots echoues d'affilee avant arret
BLOCK_VISITS = 30             -- une carte en echec est ecartee pour N visites
EMPTY_PAUSE_MS = 60000        -- pause quand tout le circuit est vide (repousse)
SUMMARY_EVERY = 10            -- bilan console tous les N passages
SUMMARY_ROWS = 12             -- lignes de ressources dans le bilan
SUBSTRATE_WOOD_TARGET = 100 -- bois recolte (session) a partir duquel un craft de planches est conseille

local JOBS = { { id = 2, label = "Bucheron" } }
ZONE_ORDER = { "Amakna", "Cania" }

-- level = niveau de metier minimal ; maps = { identifiant de carte, "x,y" de la carte source }
TIERS = {
    { job = 2, level = 1, name = "Frene", zone = "Amakna", maps = {
        {88082192, "4,7"}, {88081680, "3,7"}, {88081679, "3,8"},
        {88081678, "3,9"}, {88081166, "2,9"}, {88080654, "1,9"},
        {88212238, "0,9"}, {88212239, "0,8"}, {88080655, "1,8"},
    } },
    { job = 2, level = 20, name = "Chataignier", zone = "Amakna", maps = {
        {88081415, "3,30"}, {88080903, "2,30"}, {88080391, "1,30"},
        {88080902, "2,29"}, {88081414, "3,29"}, {88081926, "4,29"},
        {88082438, "5,29"}, {88082439, "5,30"}, {88081927, "4,30"},
    } },
    { job = 2, level = 40, name = "Noyer/Chene", zone = "Amakna", maps = {
        {88083463, "7,30"}, {88082951, "6,30"}, {88082950, "6,29"},
        {88082438, "5,29"}, {88081926, "4,29"}, {88081927, "4,30"},
        {88081928, "4,31"}, {88081416, "3,31"}, {88081415, "3,30"},
    } },
    { job = 2, level = 60, name = "Bombu/Oliviolet", zone = "Cania", maps = {
        {155976713, "-13,-50"}, {155976714, "-13,-49"}, {155976202, "-14,-49"},
        {155976201, "-14,-50"}, {155976200, "-14,-51"}, {155975688, "-15,-51"},
        {155975687, "-15,-52"}, {155975686, "-15,-53"}, {155976198, "-14,-53"},
    } },
    { job = 2, level = 80, name = "Erable/If", zone = "Cania", maps = {
        {155976713, "-13,-50"}, {155976712, "-13,-51"}, {155976711, "-13,-52"},
        {155976710, "-13,-53"}, {155977222, "-12,-53"}, {155977221, "-12,-54"},
        {155976199, "-14,-52"}, {155976198, "-14,-53"}, {155976200, "-14,-51"},
    } },
    { job = 2, level = 100, name = "Bambou/Merisier", zone = "Cania", maps = {
        {155976705, "-13,-58"}, {155976706, "-13,-57"}, {155976707, "-13,-56"},
        {155976704, "-13,-59"}, {155977216, "-12,-59"}, {155977728, "-11,-59"},
        {155977729, "-11,-58"}, {155977217, "-12,-58"}, {155977218, "-12,-57"},
    } },
    { job = 2, level = 140, name = "Orme/Bambou Sombre", zone = "Cania", maps = {
        {155977221, "-12,-54"}, {155977222, "-12,-53"}, {155976710, "-13,-53"},
        {155976711, "-13,-52"}, {155976712, "-13,-51"}, {155976713, "-13,-50"},
        {155976714, "-13,-49"}, {155977226, "-12,-49"}, {155977227, "-12,-48"},
    } },
    { job = 2, level = 160, name = "Tremble/Bambou Sacre", zone = "Cania", maps = {
        {155977218, "-12,-57"}, {155977217, "-12,-58"}, {155976705, "-13,-58"},
        {155976706, "-13,-57"}, {155976707, "-13,-56"}, {155976704, "-13,-59"},
        {155977216, "-12,-59"}, {155977728, "-11,-59"}, {155976193, "-14,-58"},
    } },
}

-- Jalons planches / substrats (seuils indicatifs du rapport de recherche 03).
MILESTONES = {
    { job = 2, level = 20,  name = "Planche agglomeree",   note = "crafter les planches de Frene/Chataignier" },
    { job = 2, level = 60,  name = "Planche Chene/Noyer",  note = "crafter les planches de Chene/Noyer" },
    { job = 2, level = 60,  name = "Substrat de Bocage",   note = "planches + potions d'alchimiste : necessite aussi le metier Alchimiste" },
    { job = 2, level = 100, name = "Planche contreplaquee", note = "crafter les planches multi-bois" },
}

-- ======================================================================
-- MOTEUR (ne pas modifier sans raison)
-- Le moteur Givre lit MAX_PODS, MIN_LIFE_PERCENT, OPEN_BAGS, MIN/MAX_MONSTERS.
-- ======================================================================
MAX_PODS = PODS_MAX
OPEN_BAGS = false
MIN_MONSTERS = 1
MAX_MONSTERS = 1 -- aucun combat volontaire ; seuls les protecteurs de ressources se battent

local TAG = "Bucheron"
local streak, wins, losses = 0, 0, 0
local stopAsked = false
local failures, bankFailures, banks = 0, 0, 0
local emptyPasses, visits, passes = 0, 0, 0
local unknownLevelReads = 0
local cursor = 0
local synced = false
local blocked = {}
local route, routeKey = {}, ""
local levels = {}
local harvested, harvestedOrder = {}, {}
local milestoneSeen = {}

local function log(message)
    print("[" .. TAG .. "] " .. message)
end

local function haltWith(reason)
    printWarning("[" .. TAG .. "] ARRET " .. reason)
    return false
end

local function readLevels()
    local any = false
    for _, job in ipairs(JOBS) do
        local level = tonumber(getJobLevel(job.id)) or 0
        if JOB_LEVEL_OVERRIDE > 0 then level = JOB_LEVEL_OVERRIDE end
        levels[job.id] = level
        if level > 0 then any = true end
    end
    return any
end

local function levelsText()
    local parts = {}
    for _, job in ipairs(JOBS) do
        parts[#parts + 1] = job.label .. "=" .. tostring(levels[job.id] or 0)
    end
    return table.concat(parts, " ")
end

local function zoneRank(tier)
    for index, zone in ipairs(ZONE_ORDER) do
        if zone == tier.zone then return index end
    end
    return 1
end

local function buildRoute()
    local key = ""
    local list, seen = {}, {}
    for _, job in ipairs(JOBS) do
        local level = levels[job.id] or 0
        local unlocked = {}
        for _, tier in ipairs(TIERS) do
            if tier.job == job.id and tier.level <= level then unlocked[#unlocked + 1] = tier end
        end
        local first = math.max(1, #unlocked - TIERS_IN_ROTATION + 1)
        for index = first, #unlocked do
            local tier = unlocked[index]
            key = key .. tier.name .. ";"
            for _, entry in ipairs(tier.maps) do
                local mapKey = tostring(entry[1])
                if not seen[mapKey] then
                    seen[mapKey] = true
                    list[#list + 1] = { ref = entry[1], pos = entry[2] or tostring(entry[1]), tier = tier, rank = zoneRank(tier), order = #list }
                end
            end
        end
    end
    if key ~= routeKey then
        table.sort(list, function(a, b)
            if a.rank ~= b.rank then return a.rank < b.rank end
            return a.order < b.order
        end)
        route, routeKey, cursor = list, key, 0
        log("niveaux " .. levelsText() .. " | paliers actifs: " .. key .. " | cartes=" .. #route)
    end
    return #route > 0
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

local function nextTarget()
    for _ = 1, #route do
        cursor = cursor % #route + 1
        local target = route[cursor]
        local until_ = blocked[tostring(target.ref)]
        if not until_ or visits >= until_ then return target end
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

local function recordGains(before, after)
    local total = 0
    for gid, row in pairs(after) do
        local old = before[gid] and before[gid].qty or 0
        local gain = row.qty - old
        if gain > 0 then
            total = total + gain
            local name = tostring(row.name)
            if not harvested[name] then
                harvested[name] = 0
                harvestedOrder[#harvestedOrder + 1] = name
            end
            harvested[name] = harvested[name] + gain
        end
    end
    return total
end

local function checkMilestones()
    for _, m in ipairs(MILESTONES) do
        local level = levels[m.job] or 0
        if level >= m.level and not milestoneSeen[m.name] then
            milestoneSeen[m.name] = true
            printSuccess("[" .. TAG .. "] JALON " .. m.name .. " atteint (niveau " .. level .. " >= " .. m.level .. ") : " .. m.note)
        end
    end
end

local function printSummary(title)
    log("===== " .. title .. " =====")
    log("niveaux " .. levelsText() .. " | passages=" .. passes .. " | recoltes moteur=" .. getCountGather()
        .. " | banques=" .. banks .. " | victoires=" .. wins .. " defaites=" .. losses)
    local names = {}
    for _, name in ipairs(harvestedOrder) do names[#names + 1] = name end
    table.sort(names, function(a, b)
        if harvested[a] ~= harvested[b] then return harvested[a] > harvested[b] end
        return a < b
    end)
    if #names == 0 then log("RECOLTE aucune ressource comptee pour l'instant") end
    for index = 1, math.min(#names, SUMMARY_ROWS) do
        log("RECOLTE " .. names[index] .. " x" .. harvested[names[index]])
    end
    for _, m in ipairs(MILESTONES) do
        local level = levels[m.job] or 0
        local state = level >= m.level and "ATTEINT" or ("niveau " .. m.level .. " requis (actuel " .. level .. ")")
        log("JALON " .. m.name .. " : " .. state)
    end
    local wood = 0
    for _, name in ipairs(harvestedOrder) do wood = wood + harvested[name] end
    local ready = wood >= SUBSTRATE_WOOD_TARGET
    log("SUBSTRATS bois recolte=" .. wood .. "/" .. SUBSTRATE_WOOD_TARGET .. " -> " .. (ready and "PRET : crafter planches/substrats apres le depot" or "stock insuffisant, continuer la recolte"))
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
    synced = false
    printSuccess("[" .. TAG .. "] BANQUE depot confirme pods " .. before .. " -> " .. getPods())
    printSummary("BILAN BANQUE")
    return true
end

function move()
    if isInFight() then return true end
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
    checkMilestones()
    if not buildRoute() then return haltWith("aucune carte debloquee pour ce niveau") end
    if not synced then
        if not map:sendMapRequest() then
            log("resynchronisation de la carte en attente")
            delay(5000)
            return true
        end
        synced = true
    end
    if getPodsPercent() >= PODS_MAX then return bank() end
    if MIN_LIFE_PERCENT > 0 and getLifePointsPercent() < MIN_LIFE_PERCENT then
        if not regenerate(MIN_LIFE_PERCENT, REGEN_MAX_WAIT) then
            if isInFight() then return true end
            return haltWith("regeneration non confirmee")
        end
    end
    visits = visits + 1
    local target = nextTarget()
    if not target then
        log("toutes les cartes sont en pause : attente 60 s")
        blocked = {}
        delay(60000)
        return true
    end
    if not goTo(target.ref) then
        if isInFight() then return true end
        blocked[tostring(target.ref)] = visits + BLOCK_VISITS
        failures = failures + 1
        printWarning("[" .. TAG .. "] trajet non confirme vers " .. target.pos .. " (" .. failures .. "/" .. MAX_FAILURES .. ")")
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
    if gathered or gained > 0 then emptyPasses = 0 else emptyPasses = emptyPasses + 1 end
    log("PASSAGE " .. target.tier.name .. " carte=" .. target.pos .. " gain=" .. gained .. " pods=" .. getPodsPercent() .. "%")
    if emptyPasses >= #route then
        log("circuit vide : attente de repousse " .. (EMPTY_PAUSE_MS / 1000) .. " s")
        delay(EMPTY_PAUSE_MS)
        emptyPasses = 0
    end
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
    printSummary("BILAN D'ARRET (" .. tostring(reason) .. ")")
end

log("pret : paliers par niveau de metier, banque a " .. PODS_MAX .. "% pods, arret apres " .. MAX_CONSECUTIVE_DEFEATS .. " defaites consecutives (0 = illimite)")
