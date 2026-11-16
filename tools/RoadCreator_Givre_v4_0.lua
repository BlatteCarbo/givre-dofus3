-- Road & Quest Creator v4.0 pour GivreClone / Dofus Unity 3.7
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
RC_RECORD_QUESTS = true   -- captures PNJ, réponses, dialogues, interactions et validations de quête
RC_SAVE_MOVEMENTS = true -- étapes de déplacement intra-carte si key_cells décodé
RC_RECORD_ITEMS = true    -- utilisation des consommables (GID seulement si résolu via l'inventaire)
RC_RECORD_EMOTES = true   -- émotes consignées, jamais exécutées sans API
RC_MAX_ACTIONS = 2000     -- limite anti-fichier énorme

MAX_PODS = 90
MIN_MONSTERS = 1
MAX_MONSTERS = 8

local filename = "Road_" .. tostring(math.floor(os.time())) .. "_" .. tostring(math.random(100000, 999999)) .. ".lua"
local steps, byMap = {}, {}
local visits, questEvents = {}, {}
local currentVisit = nil
local lastQuestion = 0
local actionCount = 0
local currentMap = 0
local pending = nil
local changes = 0
local lastError = ""
local save, field

local function addAction(action)
    if not RC_RECORD_QUESTS or not currentVisit or actionCount >= RC_MAX_ACTIONS then return end
    currentVisit.actions[#currentVisit.actions + 1] = action
    actionCount = actionCount + 1
    save() -- défini plus bas comme fonction locale déclarée par avance
end

local function addQuestEvent(kind, packet)
    if not RC_RECORD_QUESTS or #questEvents >= RC_MAX_ACTIONS then return end
    local e = {
        kind = kind, map = currentMap,
        questId = tonumber(field(packet, "QuestId", "questId", "quest_id")) or 0,
        stepId = tonumber(field(packet, "StepId", "stepId", "step_id")) or 0,
        objectiveId = tonumber(field(packet, "ObjectiveId", "objectiveId", "objective_id")) or 0
    }
    local old = questEvents[#questEvents]
    if old and old.kind == e.kind and old.map == e.map
       and old.questId == e.questId and old.stepId == e.stepId
       and old.objectiveId == e.objectiveId then return end
    questEvents[#questEvents + 1] = e
    save()
end

field = function(t, ...)
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
save = function()
    local rows = {
        "-- Trajet et journal de quête créés par Road Creator Givre v4.0 (inspiré d'AnkaBot/Pharwell)",
        "-- RC_MODE='quest' rejoue les actions dans l'ordre, RC_MODE='route' suit le circuit classique.",
        "MAX_PODS = 90",
        "MIN_MONSTERS = 1",
        "MAX_MONSTERS = 8",
        "ELEMENTS_TO_GATHER = {}",
        "",
        "local function routeMove()",
        "  return {"
    }
    for _, s in ipairs(steps) do
        local out = "    { map = " .. quote(printableMap(s.id))
            .. ", fight = " .. tostring(s.fight) .. ", gather = " .. tostring(s.gather)
        if s.exit then
            if s.exitType == "door" then
                out = out .. ", door = " .. quote(s.exit)
            elseif s.exitType == "npc" then
                -- On NE transforme PAS une téléportation PNJ/consommable en BFS arbitraire.
                -- L'action correspondante est conservée dans RC_MODE='quest'.
            else
                out = out .. ", path = " .. quote(s.exit)
            end
        end
        rows[#rows + 1] = out .. " },"
    end
    rows[#rows + 1] = "  }"
    rows[#rows + 1] = "end"

    -- Le trajet classique fusionne les passages par map; les visites QUEST, non.
    -- Export append-only chronologique : même PNJ à deux moments ≠ action unique.
    rows[#rows + 1] = ""
    rows[#rows + 1] = "-- Événements de quête OBSERVÉS (informatifs; ne pas supposer la validation future)."
    for _, e in ipairs(questEvents) do
        rows[#rows + 1] = "-- QUEST " .. e.kind .. " map=" .. tostring(e.map)
            .. " quest=" .. tostring(e.questId) .. " step=" .. tostring(e.stepId)
            .. " objective=" .. tostring(e.objectiveId)
    end
    rows[#rows + 1] = "local questSteps = {"
    for _, visit in ipairs(visits) do
        local exitText = visit.exit and (", exit=" .. quote(visit.exit)
            .. ", exitType=" .. quote(visit.exitType)) or ""
        rows[#rows + 1] = "  { map = " .. quote(tostring(visit.id))
            .. ", fight=" .. tostring(visit.fight) .. ", gather=" .. tostring(visit.gather)
            .. exitText .. ", actions = {"
        for _, a in ipairs(visit.actions) do
            local fields = { "type=" .. quote(a.type) }
            for _, key in ipairs({ "npc", "action", "reply", "question", "cell", "gid", "questId", "objectiveId" }) do
                if a[key] ~= nil then fields[#fields + 1] = key .. "=" .. tostring(a[key]) end
            end
            if a.note then fields[#fields + 1] = "note=" .. quote(a.note) end
            rows[#rows + 1] = "    { " .. table.concat(fields, ", ") .. " },"
        end
        rows[#rows + 1] = "  } },"
    end
    rows[#rows + 1] = "}"
    local hasQuestActions = false
    for _, visit in ipairs(visits) do
        for _, a in ipairs(visit.actions) do
            if a.type ~= "move" then hasQuestActions = true; break end
        end
        if hasQuestActions then break end
    end
    rows[#rows + 1] = "RC_MODE = " .. quote(hasQuestActions and "quest" or "route")
    rows[#rows + 1] = "RC_STRICT = true -- arrête au premier PNJ/réponse/action impossible au lieu de passer à la suite"
    rows[#rows + 1] = "RC_RESET_PROGRESS = false -- passer à true pour recommencer la quête depuis le début"
    rows[#rows + 1] = "RC_REPEAT = false -- répéter UNIQUEMENT si le dernier passage retourne à la première carte"
    rows[#rows + 1] = "RC_SKIP_CURRENT_MANUAL = false -- après avoir fait MANUELLEMENT une action bloquée, sauter CETTE SEULE action"
    rows[#rows + 1] = "local stateKey = " .. quote("roadQuestV4_" .. filename)
    rows[#rows + 1] = [[
local waitingSince = nil
local function failQuest(msg)
  global:printWarning("[Quest Creator] " .. tostring(msg))
  global:finishScript()
  return nil
end
local function resetStep(i)
  memory:set(stateKey .. "_step", tostring(i))
  memory:set(stateKey .. "_action", "1")
  waitingSince = nil
end
local function questMove()
  if RC_RESET_PROGRESS then
    memory:delete(stateKey .. "_step")
    memory:delete(stateKey .. "_action")
    RC_RESET_PROGRESS = false
  end
  local i = tonumber(memory:get(stateKey .. "_step", "1")) or 1
  local actionId = tonumber(memory:get(stateKey .. "_action", "1")) or 1
  local step = questSteps[i]
  if not step then return failQuest("Parcours terminé") end
  local here = map:currentMapId()
  -- Une carte change après le dernier paquet confirmé, jamais après l'injection seule.
  if here ~= tonumber(step.map) then
    local nextStep = questSteps[i + 1]
    if not nextStep or here ~= tonumber(nextStep.map) then
      if RC_REPEAT and not nextStep and #questSteps > 1
          and here == tonumber(questSteps[1].map) then
        i = 1
        resetStep(i)
        actionId = 1
        step = questSteps[1]
      else
        return failQuest("Carte inattendue : " .. tostring(here) .. ", attendue " .. step.map)
      end
    else
      i = i + 1
      resetStep(i)
      actionId = 1
      step = nextStep
    end
  end
  local action = step.actions[actionId]
  if action and action.type == "manual" and RC_SKIP_CURRENT_MANUAL then
    RC_SKIP_CURRENT_MANUAL = false
    memory:set(stateKey .. "_action", tostring(actionId + 1))
    global:printWarning("[Quest Creator] Action manuelle confirmée par l'utilisateur : " .. tostring(action.note))
    return {}
  end
  if action then
    return {{ map = step.map, fight = step.fight, gather = step.gather, custom = function()
      local ok = false
      if action.type == "npc" then
        ok = npc:npc(action.npc, action.action or 3)
      elseif action.type == "reply" then
        local q = npc:question()
        if not q then return failQuest("Dialogue PNJ fermé avant la réponse") end
        if action.question and action.question > 0 and q.messageId ~= action.question then
          return failQuest("Question PNJ modifiée (attendue " .. action.question .. ", lue " .. tostring(q.messageId) .. ")")
        end
        if not npc:hasReply(action.reply) then
          return failQuest("Réponse PNJ " .. tostring(action.reply) .. " non visible")
        end
        ok = npc:sendReply(action.reply)
        if ok then global:delay(400) end -- attendre la question suivante / fermeture
      elseif action.type == "leave" then
        ok = global:leaveDialog()
      elseif action.type == "use" then
        ok = map:useCell(action.cell)
      elseif action.type == "item" then
        ok = inventory:useItem(action.gid, 1)
      elseif action.type == "move" then
        ok = map:moveToCell(action.cell)
      elseif action.type == "questValidate" then
        ok = quest:questObjectiveDone(action.questId, action.objectiveId)
              or quest:validateObjective(action.questId, action.objectiveId)
      else
        return failQuest("Intervention manuelle requise : " .. tostring(action.note))
      end
      if not ok then return failQuest("Action échouée : " .. tostring(action.type) .. " (" .. actionId .. ")") end
      memory:set(stateKey .. "_action", tostring(actionId + 1))
    end }}
  end
  if step.exitType == "npc" then
    if not waitingSince then waitingSince = os.time() end
    if os.time() - waitingSince > 20 then
      return failQuest("Le PNJ n'a pas changé la carte après 20 s")
    end
    global:delay(250)
    return {}
  end
  if step.exit then
    if step.exitType == "door" then return {{map=step.map, fight=step.fight, gather=step.gather, door=step.exit}} end
    return {{map=step.map, fight=step.fight, gather=step.gather, path=step.exit}}
  end
  return failQuest("Dernière carte atteinte; valider les objectifs de quête.")
end
function move()
  if RC_MODE == "quest" then return questMove() end
  return routeMove()
end
-- Rejouer le parcours depuis le début : supprimer les deux clés memory
-- stateKey..'_step' et stateKey..'_action' dans un script temporaire.
]]
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
    local plan = getStep(id)
    currentMap = id
    currentVisit = { id = id, actions = {}, fight = plan.fight, gather = plan.gather }
    visits[#visits + 1] = currentVisit
    lastQuestion = 0
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
        if pending and pending.kind == "door" and os.time() - pending.at <= 20 then
            exitKind, exit = "door", tostring(pending.cell)
        elseif pending and pending.kind == "havenbag" and os.time() - pending.at <= 20 then
            exitKind, exit = "path", "havenbag"
        elseif pending and pending.kind == "teleport" and os.time() - pending.at <= 20 then
            exitKind, exit = "path", (pending.sourceType == 1 and "zaapi(" or "zaap(") .. tostring(dest) .. ")"
        elseif pending and (pending.kind == "npc" or pending.kind == "item")
                and os.time() - pending.at <= 20 then
            -- Le dernier replyNpc/useItem est responsable du changement.
            -- Aucun path artificiel à injecter : la transition est déclenchée par cette action.
            exitKind, exit = "npc", tostring(dest)
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
        if currentVisit then
            currentVisit.exit, currentVisit.exitType = exit, exitKind
            -- L'interactif a provoqué la transition : rejouer via door, pas deux fois via useCell.
            if exitKind == "door" then
                local last = currentVisit.actions[#currentVisit.actions]
                if last and last.type == "use" and last.cell == pending.cell then
                    table.remove(currentVisit.actions)
                end
            end
        end
    end
    changes = changes + 1
    begin(dest)
    if RC_DEBUG then global:printMessage("Road Creator : " .. changes .. " changements") end
end

local function onMapChange(packet)
    -- Requête sortante : utilisée seulement si le changement est confirmé ensuite.
    pending = { kind = "map", target = number(field(packet, "MapId", "mapId", "map_id")),
                cell = number(map:currentCell()), at = os.time() }
end

local function onMovement(packet)
    if not RC_SAVE_MOVEMENTS or isInFight() then return end
    -- Liste des keyMovements : orientation << 12 | cellId.
    -- Une valeur packed non reconnue N'EST JAMAIS prise pour une cellule.
    local keys = field(packet, "KeyCells", "keyCells", "key_cells")
    if type(keys) ~= "table" or #keys == 0 then return end
    local encoded = tonumber(keys[#keys])
    if not encoded or encoded < 0 or encoded > 32767 then return end
    local cell = encoded % 4096
    if cell < 0 or cell >= 560 then return end
    if currentVisit then
        local last = currentVisit.actions[#currentVisit.actions]
        if last and last.type == "move" and last.cell == cell then return end
    end
    addAction({ type = "move", cell = cell })
end

local function onTeleport(packet)
    pending = { kind = "teleport", sourceType = number(field(packet, "SourceType", "sourceType", "source_type")), at = os.time() }
end

local function onHavenBag(packet)
    pending = { kind = "havenbag", at = os.time() }
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
        if number(item.elementId) == element then
            local cell = number(field(item, "cellId", "cell"))
            if cell >= 0 and cell <= 559 then
                -- Resource-only : ne jamais rejouer une récolte comme levier/porte.
                if number(item.elementTypeId) ~= -1 and item.resource then return end
                local resources = map:getResources()
                for _, resource in ipairs(resources or {}) do
                    if number(field(resource, "cell", "cellId")) == cell then return end
                end
                addAction({ type = "use", cell = cell })
                if number(item.elementTypeId) == -1 then
                    pending = { kind = "door", cell = cell, at = os.time() }
                else
                    pending = { kind = "interactive", cell = cell, at = os.time() }
                end
            end
            return
        end
    end
    addAction({ type = "manual", note = "Interactif inconnu : elementId " .. tostring(element) })
end

local function onNpcTalk(packet)
    local actor = number(field(packet, "NpcId", "npcId", "npc_id"))
    local actionId = number(field(packet, "NpcActionId", "npcActionId", "npc_action_id",
                                    "ActionId", "actionId", "action_id"))
    if actionId <= 0 then actionId = 3 end
    if actor == 0 then
        addAction({ type = "manual", note = "PNJ acteur inconnu" })
        return
    end
    local npcId = actor
    local data = map:getMap()
    for _, a in ipairs(data and data.actors or {}) do
        if number(a.contextualId) == actor and number(a.npcId) > 0 then
            npcId = number(a.npcId) -- modèle stable, plutôt que contextualId de session
            break
        end
    end
    addAction({ type = "npc", npc = npcId, action = actionId })
    pending = { kind = "npc", at = os.time() }
end

local function onNpcQuestion(packet)
    lastQuestion = number(field(packet, "MessageId", "messageId", "message_id",
                                        "Message", "message"))
end

local function onNpcReply(packet)
    local reply = number(field(packet, "ReplyId", "replyId", "reply_id"))
    if reply <= 0 then
        addAction({ type = "manual", note = "Identifiant de réponse PNJ inconnu" })
    else
        addAction({ type = "reply", reply = reply, question = lastQuestion })
    end
    pending = { kind = "npc", at = os.time() }
end

local function onDialogLeave(packet)
    -- Un dialogue terminé dans la même carte fait partie de la quête.
    addAction({ type = "leave" })
    if pending and pending.kind == "npc" then pending = nil end
end

local function onObjectUse(packet)
    if not RC_RECORD_ITEMS then return end
    local uid = number(field(packet, "ObjectUid", "objectUid", "object_uid",
                                    "ObjectUID", "objectUID", "objectUid"))
    local gid = 0
    for _, item in ipairs(inventory:items() or {}) do
        if number(item.uid) == uid and uid > 0 then
            gid = number(item.gid)
            break
        end
    end
    if gid > 0 then
        addAction({ type = "item", gid = gid })
        pending = { kind = "item", at = os.time() }
    else
        addAction({ type = "manual", note = "Consommable UID " .. tostring(uid)
            .. " : GID inconnu (aucune utilisation automatique)" })
    end
end

local function onEmote(packet)
    if not RC_RECORD_EMOTES then return end
    local id = number(field(packet, "EmoteId", "emoteId", "emote_id"))
    addAction({ type = "manual", note = "Émote " .. tostring(id)
        .. " : API d'exécution non confirmée" })
end

local function onQuestValidate(packet)
    local questId = number(field(packet, "QuestId", "questId", "quest_id"))
    local objectiveId = number(field(packet, "ObjectiveId", "objectiveId", "objective_id"))
    if questId > 0 and objectiveId > 0 then
        addAction({ type = "questValidate", questId = questId, objectiveId = objectiveId })
    else
        addAction({ type = "manual", note = "Validation de quête : IDs non décodés" })
    end
end

local function onManualAction(name)
    -- Captures utiles en quête/donjon, mais PAS de protocole d'exécution
    -- fiable et vérifié : la relecture s'arrêtera avec une consigne explicite.
    addAction({ type = "manual", note = name .. " (relecture manuelle requise)" })
end

function messagesRegistering()
    developer:registerMessage("MapComplementaryInformationEvent", newMap)
    developer:registerMessage("MapChangeRequest", onMapChange)
    developer:registerMessage("MapMovementRequest", onMovement)
    developer:registerMessage("TeleportRequest", onTeleport)
    developer:registerMessage("HavenBagEnterRequest", onHavenBag)
    developer:registerMessage("InteractiveUseRequest", onInteractive)
    -- Dialogues PNJ : capture aussi les actions SANS changement de carte.
    developer:registerMessage("NpcGenericActionRequest", onNpcTalk)
    developer:registerMessage("NpcDialogQuestionEvent", onNpcQuestion)
    developer:registerMessage("NpcDialogReplyRequest", onNpcReply)
    developer:registerMessage("DialogLeaveRequest", onDialogLeave)
    developer:registerMessage("ObjectUseRequest", onObjectUse)
    developer:registerMessage("EmotePlayRequest", onEmote)
    developer:registerMessage("QuestObjectiveValidationRequest", onQuestValidate)
    developer:registerMessage("QuestStartRequest", function(p) addQuestEvent("QuestStartRequest", p) end)
    for _, message in ipairs({
        "ObjectUseOnCellRequest", "ObjectUseOnCharacterRequest",
        "ExchangeBuyRequest", "ExchangeSellRequest", "ExchangeObjectMoveRequest",
        "ExchangeReadyRequest", "ExchangeCraftCountRequest",
    }) do
        local name = message
        developer:registerMessage(name, function() onManualAction(name) end)
    end
    -- Les notifications de quête sont des points de contrôle, pas des actions à fabriquer.
    for _, message in ipairs({
        "QuestStartedEvent", "QuestStepStartedEvent", "QuestStepValidatedEvent",
        "QuestObjectiveValidatedEvent", "QuestValidatedEvent",
    }) do
        local name = message
        developer:registerMessage(name, function(packet) addQuestEvent(name, packet) end)
    end
    begin(number(map:currentMapId()))
    global:printSuccess("Road & Quest Creator Givre v4.0 actif. Fichier : scripts/" .. filename)
end

function stopped(reason)
    if #steps > 0 then save() end
    global:printSuccess("Road Creator arrêté (" .. tostring(reason) .. ") : " .. #steps
        .. " carte(s), " .. changes .. " transition(s), " .. actionCount
        .. " action(s) et " .. #questEvents .. " événement(s) de quête. Fichier : scripts/" .. filename)
end
