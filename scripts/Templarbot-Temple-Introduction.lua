-- Scoutbot / Templarbot uniquement : introduction au Temple Celeste, quete 1631.
-- Aucun combat volontaire, achat, vente, banque, equipement ou notification externe.
-- Dialogue borne et reponses connues seulement ; toute scene inconnue arrete le script.
MIN_LIFE_PERCENT=0
OPEN_BAGS=false
local TEMPLE={[241443329]=true,[241444353]=true,[241445377]=true}
local REPLIES={77751,77741,83193,83200,77754,77756,77752,77755}
local attempts=0
local stoppedAfterFight=false

local function log(s) print("[Temple Templarbot] "..s) end
local function allowed()
    return getCharacterName()=="Templarbot" and getServerName()=="Kourial" and TEMPLE[getMapId()]==true
end

function onFightEnd(result)
    stoppedAfterFight=true
end

function move()
    if not allowed() then printError("Temple : personnage, serveur ou carte inattendu") return false end
    if stoppedAfterFight then printWarning("Temple : combat inattendu, verification requise") return false end
    if isInFight() then return true end
    if not quest:loaded() then
        attempts=attempts+1
        if attempts>=5 then printWarning("Temple : etat des quetes inconnu") return false end
        delay(1000) return true
    end
    if quest:isFinished(1631) then log("quete 1631 terminee, arret de cette introduction") return false end
    if attempts>=5 then printWarning("Temple : limite de tentatives atteinte") return false end
    attempts=attempts+1
    if getMapId()==241443329 and quest:isActive(1631)
        and quest:questObjectiveDone(1631,9738) and quest:questObjectiveDone(1631,9739) then
        travelTo(241444353)
        if getMapId()~=241444353 then printWarning("Temple : piece suivante non confirmee") return false end
    end
    local present=false
    for _,p in ipairs(map:getNpcs()) do if p.npcId==7581 then present=true end end
    if not present then printWarning("Temple : Ganymede absent, aucune substitution") return false end
    if not npc:npc(7581,3) then printWarning("Temple : dialogue non ouvert") return false end
    local used={}
    for _=1,8 do
        if not allowed() then npc:leaveDialog() return false end
        local q=npc:question()
        if not q then break end
        log("question="..tostring(q.messageId).." texte="..string.sub(tostring(q.text),1,160))
        local chosen=nil
        for _,id in ipairs(REPLIES) do
            if not used[id] and npc:hasReply(id) then chosen=id break end
        end
        if not chosen then
            log("reponses inconnues="..table.concat(q.replies or {},","))
            npc:leaveDialog() return false
        end
        used[chosen]=true
        log("reponse="..chosen)
        if not npc:reply(chosen) then
            -- false = aucune nouvelle question, pas necessairement une reponse refusee.
            log("pas de nouvelle question ; verification de l'etat serveur")
            break
        end
    end
    npc:leaveDialog()
    delay(800)
    if not allowed() then return false end
    quest:refresh(1631)
    log("carte="..getMapId().." active="..tostring(quest:isActive(1631)).." terminee="..tostring(quest:isFinished(1631)))
    for _,o in ipairs(quest:objectives(1631)) do
        log("objectif="..tostring(o.id).." atteint="..tostring(o.reached))
    end
    -- Les scenes DOFUS/LIVRE ne sont pas considerees terminees sans evenement serveur.
    return false
end
