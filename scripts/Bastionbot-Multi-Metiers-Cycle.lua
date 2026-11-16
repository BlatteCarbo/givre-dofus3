-- Genere par build-bastionbot-cycle.py ; sources adjacentes.
-- Ressources du sac uniquement. Types categoryId=2 de la base installee.
local RESOURCE_TYPES={15,26,34,35,36,38,39,40,41,46,47,48,50,51,53,54,55,56,57,58,59,60,61,63,65,66,68,70,71,78,84,95,96,98,103,104,105,106,107,108,109,110,111,119,125,152,153,154,164,174,175,179,183,185,189,195,197,209,211,219,228,229,230,231,232,233,241,258,262,266,278,309,312,315,323,326,335,336}
local allowed={}
for _,id in ipairs(RESOURCE_TYPES) do allowed[id]=true end

function sellBagResources()
    if getCharacterName()~="Bastionbot" or isInFight() then return false end
    leaveDialog()
    if not goToHdv(206308355) then printWarning("VENTE ouverture HDV non confirmee") return false end
    delay(1000)
    local merged=false
    if sale:AvailableSpace()>0 then
        merged=sale:includeStorages(true,true)
        if not merged then printWarning("VENTE reserves non chargees : sac seulement") end
    end
    local sold, failed=0,0
    local gids, seen={},{}
    for _,it in ipairs(inventory:items()) do
        if it.pos==63 and allowed[inventory:typeId(it.gid)] and not seen[it.gid] then
            gids[#gids+1]=it.gid seen[it.gid]=true
        end
    end
    for _,gid in ipairs(gids) do
        if sale:AvailableSpace()==0 then break end
        local prices={}
        for _,qty in ipairs({1000,100,10,1}) do prices[qty]=sale:GetPriceItem(gid,qty) end
        local avg=sale:getAveragePriceItem(gid,1)
        local mine={}
        for i=1,sale:ItemsOnSale() do
            if sale:GetItemGID(i)==gid then
                local q,p=sale:GetItemQuantity(i),sale:GetItemPrice(i)
                mine[q]=math.min(mine[q] or p,p)
            end
        end
        local abort=false
        for _,qty in ipairs({1000,100,10,1}) do
            local market=prices[qty]
            if market and market>0 then
                local target=math.max(1,mine[qty]==market and market or market-1)
                if avg and avg>0 then target=math.max(target,math.ceil(avg*qty*0.5)) end
                -- Ne pas creer une annonce hors marche quand le plancher est superieur.
                if target<=market then
                    while inventory:count(gid)>=qty and sale:AvailableSpace()>0 do
                        local before=inventory:count(gid)
                        local ok=sale:SellItem(gid,qty,target)
                        if ok then
                            for _=1,75 do if inventory:count(gid)<=before-qty then break end delay(20) end
                        end
                        local after=inventory:count(gid)
                        print("VENTE_LOT gid="..gid.." qty="..qty.." prix="..target.." confirme="..tostring(ok).." stock="..before.."->"..after)
                        if not ok or before-after~=qty then failed=failed+1 abort=true break end
                        sold=sold+1
                    end
                end
            end
            if abort then break end
        end
    end
    printSuccess("VENTE_FIN lots="..sold.." resultats_non_confirmes="..failed.." places="..sale:AvailableSpace())
    -- Toujours restaurer le sac seul avant voyage/recolte/banque.
    if merged and not sale:includeStorages(false,false) then
        printError("Inventaire du sac non resynchronise ; reprise apres reconnexion") stopScript() return false
    end
    return failed==0
end



-- Reprix des annonces existantes, sans achat ni nouvelle mise en vente.
-- Une cotation par (GID, quantite), cible figee pour tous ses exemplaires.
-- Sous-cote 1K, plancher absolu 50% de la moyenne serveur (interface HDV).
local function snapshot()
    local groups, order = {}, {}
    for i=1,sale:ItemsOnSale() do
        local gid, qty = sale:GetItemGID(i), sale:GetItemQuantity(i)
        local key = tostring(gid)..":"..tostring(qty)
        if not groups[key] then
            groups[key]={gid=gid,qty=qty,rows={}}
            order[#order+1]=groups[key]
        end
        local rows=groups[key].rows
        rows[#rows+1]={uid=sale:GetItemGUID(i),price=sale:GetItemPrice(i)}
    end
    return order
end

function repriceListings()
    if getCharacterName()~="Bastionbot" or isInFight() then return false end
    if getMapId()~=206308355 then printError("Ouvrir le HDV ressources Pandala avant ce script") return false end
    delay(1000) -- le signal d'ouverture precede la publication des annonces
    local groups=snapshot()
    if #groups==0 then printError("Aucune annonce chargee ; aucun prix modifie") return false end
    local started=os.time()
    local confirmed, attempted, skipped=0,0,0
    print("REPRICE_GROUPE debut groupes="..#groups.." lots="..sale:ItemsOnSale())
    for _,g in ipairs(groups) do
        local market=sale:GetPriceItem(g.gid,g.qty)
        local avg=sale:getAveragePriceItem(g.gid,g.qty)
        if market and market>0 then
            local own=false
            for _,r in ipairs(g.rows) do if r.price==market then own=true end end
            local target=math.max(1,own and market or market-1)
            if avg and avg>0 then target=math.max(target,math.ceil(avg*0.5)) end
            for _,r in ipairs(g.rows) do
                if r.price>target then
                    attempted=attempted+1
                    local ok=sale:EditPrice(r.uid,target,g.qty)
                    if ok then confirmed=confirmed+1 end
                    print("REPRICE_LOT gid="..g.gid.." qty="..g.qty.." ancien="..r.price.." cible="..target.." confirme="..tostring(ok))
                else skipped=skipped+1 end
            end
        else skipped=skipped+#g.rows end
    end
    printSuccess("REPRICE_GROUPE fin confirmes="..confirmed.." tentatives="..attempted.." inchanges="..skipped.." secondes="..(os.time()-started))
    return true
end



-- Assemble avec les deux modules de commerce par build-bastionbot-cycle.py.
MAX_PODS=85
OPEN_BAGS=true
MIN_LIFE_PERCENT=0
ELEMENTS_TO_GATHER={}
ITEMS_TO_GATHER={}
GATHER={}
local routes={
 {name="Frigost Frostiz et Perce-Neige",bank=54534165,maps={54168355,54169377,54169888,54169378,54168869,54168357,54168358,54167847,54168871,54168872,54168360,54167848}},
 {name="Frigost Tremble et Obsidienne",bank=54534165,maps={54162742,54162232,54161209,54162235,54163261,54162752,57017861,56885763,57016837,56886787,57016835,54163765,54165809,54166319,54166831,54166832,54170414}},
 {name="Mine Obsidienne Frigost",bank=54534165,maps={57017863,57016839,56886791}},
 {name="Otomai Mais et ressources environnantes",bank=192415750,maps={159235,159236,157703,157704,157193,157194,156683}},
 {name="Millet et bois des plaines",bank=192415750,maps={126093078,126092566,126092567,126092055,126092056,126092057,126091543,126093080,126093081,126093592,126093593,126094104,126095131,126094107,126093595}}
}
local KEY="bastionbot_multi_metiers_bank_v1"
local returns=0
local routeIndex,point,lap=1,1,0
local blocked,empty,failures={},0,0
local initialized,refresh=false,true
local recoverUntil=0
local fightUncertain=false
local function log(s) print("[Multi Bastionbot] "..s) end
local function pause() delay(15000) return true end
local function nextRoute()
 routeIndex=routeIndex%#routes+1 point=1 empty=0
 log("circuit "..routes[routeIndex].name.." ; aucune ressource exclue")
end
local function commerce()
 leaveDialog()
 if not sellBagResources() then
  log("vente non confirmee : arret pour verification, sans nouvelle tentative")
  stopScript() return false
 end
 if getMapId()~=206308355 or not repriceListings() then
  log("reprix non confirme : arret pour verification")
  stopScript() return false
 end
 leaveDialog()
 return true
end
function handleReturn()
 if isInFight() then return true end
 local shop=returns>=6
 if shop then
  log("retour 7 : HDV, vente des ressources du sac ET des reserves, puis reprix")
  if not commerce() then return false end
  if getPodsPercent()<MAX_PODS then
   returns=0
   memory:set(KEY,tostring(returns))
   log("HDV termine ; sac sous le seuil, nouveau cycle")
   nextRoute() refresh=true
   return true
  end
 end
 local bank=routes[routeIndex].bank
 leaveDialog()
 if not travelTo(bank) or getMapId()~=bank then log("banque inaccessible") return pause() end
 if not npc:npcBank() then log("banque non ouverte") return pause() end
 local before=getPods()
 local ok=exchange:putAllItems()
 leaveDialog()
 for _=1,50 do if getPods()<before then break end delay(100) end
 if not ok or getPodsPercent()>=MAX_PODS or getPods()>=before then
  log("depot non confirme : compteur conserve") return pause()
 end
 returns=shop and 0 or returns+1
 memory:set(KEY,tostring(returns))
 log("depot confirme "..before.."->"..getPods().." ; retours banque="..returns.."/6")
 nextRoute() refresh=true
 return true
end
function onFightEnd(result)
 refresh=true
 if result and result.won then log("protecteur gagne ; reprise du circuit")
 else
  fightUncertain=true
  log("combat perdu/inconnu : arret de la recolte pour verification, sans relance automatique")
 end
end
function move()
 if getCharacterName()~="Bastionbot" then printError("Mauvais personnage") return false end
 if isInFight() then return true end
 if fightUncertain then return false end
 if not initialized then
  returns=tonumber(memory:get(KEY)) or 0
  if returns<0 or returns>6 then returns=0 end
  combat:setAutoFight(true) combat:setAutoSpells(true) combat:setAutoPreFight(true) combat:setSpeed("Turbo")
  combat:setChallengeMode("off")
  initialized=true
  log("6 banques puis HDV ; compteur="..returns.." ; cinq circuits, recolte de TOUT")
 end
 if isDead() or os.time()<recoverUntil then return pause() end
 if character:lifePointsP()<90 and not regenerate(90,180) then return pause() end
 if getPodsPercent()>=MAX_PODS then return handleReturn() end
 if refresh then
  if not map:sendMapRequest() then return pause() end
  refresh=false
 end
 local route=routes[routeIndex]
 local target=route.maps[point]
 if (blocked[target] or 0)>os.time() then
  point=point%#route.maps+1 failures=failures+1
  if failures>=#route.maps then failures=0 nextRoute() end
  return true
 end
 leaveDialog()
 if getMapId()~=target and (not travelTo(target) or getMapId()~=target) then
  if isInFight() then return true end
  blocked[target]=os.time()+600 failures=failures+1 point=point%#route.maps+1
  log("carte "..target.." inaccessible, exclue 10min")
  if failures>=3 then failures=0 nextRoute() end
  return true
 end
 local before=getCountGather()
 gatherAll()
 if isInFight() or isDead() then return true end
 if getPodsPercent()>=MAX_PODS then return handleReturn() end
 local gained=getCountGather()-before
 log(route.name.." carte="..target.." recoltes="..gained.." pods="..getPodsPercent().."%")
 empty=gained>0 and 0 or empty+1
 failures=0
 point=point+1
 if point>#route.maps then
  lap=lap+1
  -- Inverser le sens a chaque tour, puis changer de secteur.
  local reversed={}
  for i=#route.maps,1,-1 do reversed[#reversed+1]=route.maps[i] end
  route.maps=reversed
  nextRoute()
 elseif empty>=5 then nextRoute() end
 return true
end
