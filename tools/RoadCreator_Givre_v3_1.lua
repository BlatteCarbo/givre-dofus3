-- Road Creator pour GivreClone / Dofus Unity 3.7
-- Adaptation compatible avec l'outil historique de Pharwell / AnkaBot.
-- Enregistrement passif uniquement : aucun paquet n'est envoyé au jeu.
-- Requiert l'API io.open restreinte Road_*.lua et rcAsk* de GivreClone.
--
-- Le fichier produit se trouve dans %APPDATA%/GivreClone/scripts/Road_*.lua.

RC_PREFER_MAPID = true      -- false : coordonnées x,y (attention aux intérieurs)
RC_INCLUDE_CELLID = false  -- false recommandé : la cellule de sortie n'est pas toujours fiable
RC_ASK_FIGHT = true
RC_ASK_GATHER = true
RC_DEFAULT_FIGHT = false
RC_DEFAULT_GATHER = false
RC_DEBUG = false

MAX_PODS = 90
MIN_MONSTERS = 1
MAX_MONSTERS = 8

local filename = "Road_" .. tostring(math.floor(os.time())) .. "_" .. tostring(math.random(100000, 999999)) .. ".lua"
local steps, byMap = {}, {}
local currentMap = 0
local pending = nil
local changes = 0
local lastError = ""

local function field(t, ...)
    if type(t) ~= "table" then return nil end
    for _, key in ipairs({...}) do
        if t[key] ~= nil then return t[key] end
    end
    return nil
end

local function number(x)
    return tonumber(x) or 0
end

local function printableMap(id)
    if not RC_PREFER_MAPID then
        local x, y = map:coordsOf(id)
        if x ~= nil and y ~= nil then return tostring(x) .. "," .. tostring(y) end
    end
    return tostring(id)
end

local function quote(s)
    return string.format("%q", tostring(s))
end

local function ask(fieldName, enabled, fallback)
    if not enabled then return fallback end
    local ok, result = pcall(function()
        if fieldName == "fight" then return global:rcAskFight() end
        return global:rcAskGather()
    end)
    if not ok or type(result) ~= "string" then return fallback end
    local found = result:match(fieldName .. "%s*=%s*(true)")
    if found then return true end
    found = result:match(fieldName .. "%s*=%s*(false)")
    if found then return false end
    return fallback
end

local function getStep(id)
    if byMap[id] then return byMap[id] end
    local s = { id = id, fight = RC_DEFAULT_FIGHT, gather = RC_DEFAULT_GATHER }
    byMap[id] = s
    steps[#steps + 1] = s
    s.fight = ask("fight", RC_ASK_FIGHT, RC_DEFAULT_FIGHT)
    s.gather = ask("gather", RC_ASK_GATHER, RC_DEFAULT_GATHER)
    return s
end

-- Sauvegarde à CHAQUE transition. Un Stop/crash ne laisse pas de Lua incomplet.
local function save()
    local rows = {
        "-- Trajet créé par Road Creator Givre v3.1 (inspiré d'AnkaBot/Pharwell)",
        "MAX_PODS = 90",
        "MIN_MONSTERS = 1",
        "MAX_MONSTERS = 8",
        "ELEMENTS_TO_GATHER = {}",
        "",
        "function move()",
        "  return {"
    }
    for _, s in ipairs(steps) do
        local out = "    { map = " .. quote(printableMap(s.id))
            .. ", fight = " .. tostring(s.fight) .. ", gather = " .. tostring(s.gather)
        if s.exit then
            if s.exitType == "door" then
                out = out .. ", door = " .. quote(s.exit)
            else
                out = out .. ", path = " .. quote(s.exit)
            end
        end
        rows[#rows + 1] = out .. " },"
    end
    rows[#rows + 1] = "  }"
    rows[#rows + 1] = "end"
    local f, err = io.open(filename, "w")
    if not f then
        if err ~= lastError then global:printError("Road Creator : " .. tostring(err)) end
        lastError = tostring(err)
        return false
    end
    f:write(table.concat(rows, "\n") .. "\n")
    f:close()
    return true
end

local function begin(id)
    if id <= 0 then return end
    if currentMap == id then return end
    getStep(id)
    currentMap = id
    pending = nil
    save()
end

local function adjacentDirection(old, newId)
    local x1, y1 = map:coordsOf(old)
    local x2, y2 = map:coordsOf(newId)
    if x1 == nil or y1 == nil or x2 == nil or y2 == nil then return nil end
    if x2 == x1 + 1 and y2 == y1 then return "right" end
    if x2 == x1 - 1 and y2 == y1 then return "left" end
    if y2 == y1 + 1 and x2 == x1 then return "bottom" end
    if y2 == y1 - 1 and x2 == x1 then return "top" end
    return nil
end

local function newMap(packet)
    local dest = number(field(packet, "MapId", "mapId", "map_id"))
    if dest <= 0 then dest = number(map:currentMapId()) end
    if dest <= 0 or dest == currentMap or isInFight() then return end
    if currentMap > 0 then
        local source = getStep(currentMap)
        local direction = adjacentDirection(currentMap, dest)
        local exitKind, exit
        if pending and pending.kind == "door" then
            exitKind, exit = "door", tostring(pending.cell)
        elseif pending and pending.kind == "havenbag" then
            exitKind, exit = "path", "havenbag"
        elseif pending and pending.kind == "teleport" then
            exitKind, exit = "path", (pending.sourceType == 1 and "zaapi(" or "zaap(") .. tostring(dest) .. ")"
        else
            exitKind = "path"
            exit = direction or tostring(dest)
            if RC_INCLUDE_CELLID and direction and pending and pending.kind == "map"
               and pending.cell >= 0 and pending.cell <= 559 then
                exit = direction .. "(" .. tostring(pending.cell) .. ")"
            end
        end
        if source.exit == nil then
            source.exit, source.exitType = exit, exitKind
        elseif source.exit ~= exit and RC_DEBUG then
            global:printWarning("Sortie alternative ignorée sur carte " .. tostring(currentMap))
        end
    end
    changes = changes + 1
    begin(dest)
    if RC_DEBUG then global:printMessage("Road Creator : " .. changes .. " changements") end
end

local function onMapChange(packet)
    -- Requête sortante : utilisée seulement si le changement est confirmé ensuite.
    pending = { kind = "map", target = number(field(packet, "MapId", "mapId", "map_id")),
                cell = number(map:currentCell()) }
end

local function onTeleport(packet)
    pending = { kind = "teleport", sourceType = number(field(packet, "SourceType", "sourceType", "source_type")) }
end

local function onHavenBag(packet)
    pending = { kind = "havenbag" }
end

local function onInteractive(packet)
    local element = number(field(packet, "ElementId", "elementId", "element_id"))
    if element <= 0 then return end
    -- getMap() ne bloque pas pour attendre les bundles d'interactifs.
    local current = map:getMap()
    local items = current and current.interactives or {}
    for _, item in ipairs(items) do
        -- Même garde que l'enregistreur natif : type -1 = porte/escalier.
        -- Une ressource (blé, minerais...) ne doit jamais devenir une sortie door.
        if number(item.elementId) == element and number(item.elementTypeId) == -1 then
            local cell = number(field(item, "cellId", "cell"))
            if cell >= 0 and cell <= 559 then
                pending = { kind = "door", cell = cell }
            end
            return
        end
    end
end

function messagesRegistering()
    developer:registerMessage("MapComplementaryInformationEvent", newMap)
    developer:registerMessage("MapChangeRequest", onMapChange)
    developer:registerMessage("TeleportRequest", onTeleport)
    developer:registerMessage("HavenBagEnterRequest", onHavenBag)
    developer:registerMessage("InteractiveUseRequest", onInteractive)
    begin(number(map:currentMapId()))
    global:printSuccess("Road Creator Givre v3.1 actif. Fichier : scripts/" .. filename)
end

function stopped(reason)
    if #steps > 0 then save() end
    global:printSuccess("Road Creator arrêté (" .. tostring(reason) .. ") : " .. #steps
        .. " carte(s), " .. changes .. " transition(s). Fichier : scripts/" .. filename)
end
