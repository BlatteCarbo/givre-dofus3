-- Foragerbot : mineur automatique, sans filtre de recolte ni regeneration.
-- Recolte sans filtre : toutes ressources accessibles aux metiers du personnage.
-- Mineur 1-200 : ronde multi-mines, paliers, banque et reprise.
-- Cartes issues du catalogue local. Un emplacement recense n'est pas une garantie
-- d'acces ni de disponibilite : verification de la carte apres chaque trajet.
-- JOB_ID=24 confirme par les fiches des personnages (26 = Alchimiste).
MAX_PODS = 85
MIN_LIFE_PERCENT = 0
REGEN_MAX_WAIT = 180
OPEN_BAGS = false

-- Decalage du point de depart pour repartir les personnages dans les mines.
local OFFSET = 0
local BANK_MAP = 192415750
-- min niveau utile, carte, position. Le circuit se renouvelle aux paliers.
local MAPS = {
    {1,188483076,"2,-18"}, {1,188484100,"3,-17"},
    {1,188482052,"1,-17"}, 
    {60,193332229,"8,-17"}, {60,193200131,"10,-19"},
    {80,4460544,"1,-8"}, {80,4460546,"0,-8"},
    {100,149948416,"-11,-16"}, {120,149949440,"-8,-13"},
    {140,97260047,"-2,-5"}, {140,104858121,"-4,2 entree mine"}, {140,104860169,"-4,2"},
    {160,97258013,"-2,2"}, {180,205259780,"24,-22"},
    {180,205259779,"24,-23"}, {180,207621132,"25,-22"}
}
local level = -1
local step = OFFSET
local visits = 0
local failures = 0
local empty = 0
local losses = 0
local blocked = {}
local route = {}
local synced = false

local function updateLevel()
    local current = getJobLevel(24)
    if current < 1 then printWarning("Mineur inconnu : attendre la fiche metiers") return false end
    if current == level then return true end
    level = current
    -- Ronde multi-mines, dans un ordre fixe par secteur : chaque carte est
    -- visitee une fois avant de recommencer. Pas de camping sur deux salles.
    -- Les cartes insulaires >=180 restent exclues tant que leur acces n'est
    -- pas confirme. Tous les minerais deja debloques restent recoltables.
    route = {}
    for _, m in ipairs(MAPS) do
        if m[1] <= level and m[1] <= 160 then table.insert(route, m) end
    end
    step = OFFSET
    print("MINEUR niveau=" .. level .. " recolte=toutes ressources accessibles" .. " cartes=" .. #route)
    return #route > 0
end

function bank()
    if losses > 0 then printWarning("Defaite protecteur : diagnostic avant reprise") return false end
    print("BANQUE debut pods=" .. getPodsPercent() .. " kamas=" .. getKamas())
    if not travelTo(BANK_MAP) or getMapId() ~= BANK_MAP then
        printWarning("BANQUE trajet non confirme : arret") return false
    end
    if not npc:npcBank() then
        printWarning("BANQUE ouverture refusee : verifier frais et solde, aucun retrait automatique") return false
    end
    local before = getPodsPercent()
    local deposited = exchange:putAllItems()
    leaveDialog()
    local after = getPodsPercent()
    if not deposited or after >= MAX_PODS or after >= before then
        printWarning("BANQUE depot non confirme : aucun nouvel essai automatique") return false
    end
    printSuccess("BANQUE depot confirme pods=" .. before .. "->" .. after)
    return true
end

function move()
    if isInFight() then return true end
    if losses > 0 then printWarning("ARRET protecteur perdu : ne pas relancer sans diagnostic") return false end
    if isDead() then printWarning("ARRET personnage mort") return false end
    if not updateLevel() then return false end
    if not synced then
        if not map:sendMapRequest() then printWarning("ARRET resynchronisation carte impossible") return false end
        synced = true
    end
    if getPodsPercent() >= MAX_PODS then return bank() end
    visits = visits + 1
    local m
    -- Une carte en echec est mise de cote pour 30 visites, sans boucle infinie.
    for n=1,#route do
        step = step % #route + 1
        local candidate = route[step]
        if not blocked[candidate[2]] or visits >= blocked[candidate[2]] then m=candidate break end
    end
    if not m then
        printWarning("MINEUR cartes en pause : attente 60s puis nouvelle ronde")
        delay(60000) blocked={} failures=0
        synced=false return true
    end
    print("MINEUR trajet=" .. m[2] .. " [" .. m[3] .. "] niveau=" .. level)
    if getMapId() ~= m[2] and not travelTo(m[2]) then
        if isInFight() then return true end
        map:sendMapRequest()
        blocked[m[2]] = visits + 30
        failures = failures + 1
        if failures >= 6 then
            printWarning("MINEUR six trajets echoues : pause 60s et resynchronisation")
            delay(60000) failures=0 synced=false
        end
        delay(2000) return true
    end
    if getMapId() ~= m[2] then printWarning("ARRET carte cible non confirmee") return false end
    failures = 0
    local harvested = gatherAll()
    if isInFight() then return true end
    if losses > 0 then return false end
    if harvested then empty=0 else empty=empty+1 end
    print("MINEUR passage termine carte=" .. m[2] .. " recolte=" .. tostring(harvested) .. " pods=" .. getPodsPercent())
    if empty >= #route then
        print("MINEUR circuit vide : attente repousse 60 secondes")
        delay(60000) empty=0
    end
    return true
end

function onFightEnd(result)
    if result.won ~= true then losses=losses+1 end
    print("PROTECTEUR victoire=" .. tostring(result.won) .. " defaites=" .. losses)
end
