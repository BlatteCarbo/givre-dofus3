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
