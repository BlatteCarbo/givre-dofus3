-- Paysan-Alchimiste-1-200-Consommables.lua
-- Boucle hybride a faible risque : cereales (Paysan, JOB_ID 28) + plantes (Alchimiste, JOB_ID 26).
--   Paysan     : Ble -> Orge -> Avoine -> Houblon -> Riz
--   Alchimiste : Ortie -> Sauge -> Trefle -> Menthe
-- Zones, dans l'ordre : Astrub -> Amakna -> Cania -> Pandala. Recolte sans filtre (gatherAll), aucun combat voulu.
-- Banque automatique a PODS_MAX. Aucun craft, aucune vente : le bilan indique seulement quels consommables
-- (farines, pain, potions de rappel / de cite) deviennent fabricables.
-- Cartes : identifiants verifies (Astrub : scripts Nuit-*), sinon coordonnees "x,y" issues du rapport de recherche 03.
-- Les coordonnees ne sont PAS validees en jeu : l'arrivee est verifiee avec getCurrentPos() apres chaque trajet,
-- et une carte en echec est ecartee temporairement.
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

local JOBS = { { id = 28, label = "Paysan" }, { id = 26, label = "Alchimiste" } }
ZONE_ORDER = { "Astrub", "Amakna", "Cania", "Pandala" }

-- Cartes Pandala (secteur Plantala observe en jeu) : meme liste que Pandala-Farm-Prudent.
local PANDALA_MAPS = {}
for _, range in ipairs({ { 205520896, 205520903 }, { 205521409, 205521415 }, { 205521923, 205521927 }, { 205522436, 205522438 } }) do
    for id = range[1], range[2] do PANDALA_MAPS[#PANDALA_MAPS + 1] = { id, "Pandala " .. id } end
end

-- level = niveau de metier minimal ; maps = { identifiant de carte OU "x,y", libelle }
TIERS = {
    { job = 28, level = 1,   name = "Ble",     zone = "Astrub", maps = { {"7,-25", "7,-25"}, {"6,-24", "6,-24"} } },
    { job = 28, level = 1,   name = "Ble (Amakna)", zone = "Amakna", maps = { {"3,13", "3,13"}, {"5,13", "5,13"} } },
    { job = 28, level = 20,  name = "Orge",    zone = "Amakna", maps = { {"2,5", "2,5"}, {"5,8", "5,8"} } },
    { job = 28, level = 40,  name = "Avoine",  zone = "Astrub", maps = { {189795335, "9,-24"}, {188746753, "7,-21"}, {189792261, "3,-26"}, {189792771, "4,-28"}, {189792256, "3,-31"} } },
    { job = 28, level = 60,  name = "Houblon", zone = "Cania",  maps = { {"-28,-50", "-28,-50"} } },
    { job = 28, level = 120, name = "Riz",     zone = "Pandala", maps = PANDALA_MAPS },
    { job = 26, level = 1,   name = "Ortie",   zone = "Astrub", maps = { {188744197, "2,-17"}, {188744710, "3,-16"}, {188745223, "4,-15"}, {188744706, "3,-20"}, {188743683, "1,-19"} } },
    { job = 26, level = 20,  name = "Sauge",   zone = "Cania",  maps = { {"-28,-35", "-28,-35"} } },
    { job = 26, level = 40,  name = "Trefle",  zone = "Amakna", maps = { {"-1,13", "-1,13"} } },
    { job = 26, level = 60,  name = "Menthe",  zone = "Amakna", maps = { {"-1,28", "-1,28"} } },
}

-- Jalons consommables (seuils indicatifs du rapport de recherche 03) : annonce console uniquement.
MILESTONES = {
    { job = 28, level = 20,  name = "Farine de Ble / Pain",       note = "farines et pains fabricables (Ble + Orge)" },
    { job = 28, level = 60,  name = "Farines multi-cereales",      note = "farines Orge/Avoine/Houblon fabricables" },
    { job = 26, level = 20,  name = "Potions de rappel / de cite", note = "Ortie + Sauge : potions les plus liquides du HDV" },
    { job = 26, level = 40,  name = "Potions de soin",             note = "Trefle a 5 feuilles : potions de soin fabricables" },
}

-- Familles de consommables suivies dans le bilan (motifs Lua sur le nom en minuscules).
local FAMILIES = {
    { label = "Cereales", patterns = { "^bl", "orge", "avoine", "houblon", "riz" } },
    { label = "Plantes",  patterns = { "ortie", "sauge", "tr.-fle", "menthe" } },
}

-- ======================================================================
-- MOTEUR (ne pas modifier sans raison)
-- Le moteur Givre lit MAX_PODS, MIN_LIFE_PERCENT, OPEN_BAGS, MIN/MAX_MONSTERS.
-- ======================================================================
MAX_PODS = PODS_MAX
OPEN_BAGS = false
MIN_MONSTERS = 1
MAX_MONSTERS = 1 -- aucun combat volontaire ; seuls les protecteurs de ressources se battent

local TAG = "Paysan-Alchimiste"
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
    for _, family in ipairs(FAMILIES) do
        local total = 0
        for _, name in ipairs(harvestedOrder) do
            local lower = string.lower(name)
            for _, pattern in ipairs(family.patterns) do
                if string.find(lower, pattern) then total = total + harvested[name] break end
            end
        end
        log("CONSOMMABLES " .. family.label .. " recoltees = " .. total)
    end
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
