-- Foragerbot seul, 00fe352d06bf491fb5abcc2029ce84d7.
-- Prix HDV observes le 26/10/2026 07:28 UTC : plume rouge 278/u, 2399/10.
-- Aucun achat, aucune vente, aucun retrait de kamas, aucune recolte.
MIN_MONSTERS = 1
MAX_MONSTERS = 1
FORCE_MONSTERS = {489}
MIN_LIFE_PERCENT = 90
MAX_PODS = 85
REGEN_MAX_WAIT = 120
FIGHT_LOCK_SPEC = true
FIGHT_LOCK_JOIN = true

local route = {191104004,191104002,191104000,191102976,191102978,191102980,191105028,191106052,191106050,191106048,191105024}
local index, failures, empty = 1, 0, 0
local halted = false

local function halt(reason)
    halted = true
    printWarning("ARRET FORAGERBOT: " .. reason)
    stopScript()
    return false
end

function onFightEnd(r)
    print("BILAN won="..tostring(r.won).." lost="..tostring(r.lost).." outcome="..tostring(r.outcome).." fightId="..tostring(r.fightId))
    if r.lost then halt("premiere defaite, diagnostic requis") return end
    if not r.won then halt("issue inconnue, verification requise") return end
    printSuccess("VICTOIRE FORAGERBOT, butin="..#r.loot.." lignes, kamas="..tostring(r.kamas))
    for _,item in ipairs(r.loot) do
        printSuccess("LOOT gid="..item.gid.." qty="..item.qty.." nom="..tostring(item.name))
    end
end

function bank()
    if halted then return false end
    local before = getPods()
    if not travelTo(192415750) then return halt("trajet banque echoue") end
    if not npc:npcBank() then return halt("ouverture banque echouee") end
    local deposited = exchange:putAllItems()
    leaveDialog()
    delay(1000)
    print("BANQUE pods avant="..before.." apres="..getPods().." depot="..tostring(deposited))
    if not deposited or getPodsPercent() >= 85 or (before > 0 and getPods() >= before) then
        return halt("depot non confirme; ne pas repeter aveuglement")
    end
    return true
end

function move()
    if halted then return false end
    if getPodsPercent() >= 85 then return bank() end
    local destination = route[index]
    index = index % #route + 1
    if not travelTo(destination) then
        failures = failures + 1
        if failures >= 2 then return halt("deux trajets echoues") end
        return true
    end
    failures = 0
    for _,g in ipairs(map:getMonsters()) do
        -- MAX=1 et FORCE=489 evitent les groupes mixtes; verifier aussi la composition observee.
        if g.count == 1 and not g.arch and g.level >= 11 and g.level <= 15
            and #g.monsters == 1 and g.monsters[1].gid == 489 then
            if not regenerate(90,120) then return halt("regeneration insuffisante") end
            print("CIBLE Piou Rouge seul map="..destination.." cell="..g.cell.." niveau="..g.level)
            local fought = fightCell(g.cell)
            if halted then return false end
            if fought then empty = 0 else printWarning("Groupe devenu indisponible") end
            return true
        end
    end
    empty = empty + 1
    if empty >= #route then
        print("Circuit sans Piou Rouge seul: pause 30 secondes")
        delay(30000)
        empty = 0
    end
    return true
end
