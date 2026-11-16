-- Coin des Bouftous + Tainela ; groupes1-8 contenant au moins un Boufton Noir.
-- Pause de groupe uniquement apres echec et cle limitee a sa carte/composition.
-- Aucun achat/retrait/vente. Banque85%, arret si defaite ou resultat inconnu.
MIN_MONSTERS=1
MAX_MONSTERS=8
MAX_PODS=85
MIN_LIFE_PERCENT=0
FORCE_MONSTERS={149}
FORBIDDEN_MONSTERS={}
FIGHT_LOCK_SPEC=true
FIGHT_LOCK_JOIN=true
local maps={88212240,88080656,88082704,88083215,88082703,88082191,88081679,88081167,88080655,88212239,88212238,88080654,88081166,88081678,88082190,88082702,88083214,88083725,88083213,88082701,88082189,88081677,88081165,88080653,88212237,88212236,88080652,88081164,88081676,88082188,88082700,88083212,88083724,88081163,88080651,88212235,120061954,120061955,120061956,120061957,120061958,120062470,120062469,120062468,120062467,120062466,120062465,120062977,120062978,120062979,120062980,120062981,120062982,120063492,120063491,120063490,120063489,120064002,120064003,120064515}
local coinMaps={[88212240]=true,[88080656]=true,[88082704]=true,[88083215]=true,[88082703]=true,[88082191]=true,[88081679]=true,[88081167]=true,[88080655]=true,[88212239]=true,[88212238]=true,[88080654]=true,[88081166]=true,[88081678]=true,[88082190]=true,[88082702]=true,[88083214]=true,[88083725]=true,[88083213]=true,[88082701]=true,[88082189]=true,[88081677]=true,[88081165]=true,[88080653]=true,[88212237]=true,[88212236]=true,[88080652]=true,[88081164]=true,[88081676]=true,[88082188]=true,[88082700]=true,[88083212]=true,[88083724]=true,[88081163]=true,[88080651]=true,[88212235]=true}
local index=1
local synced=false
local halted=false
local blocked={}
local unavailable={}
local refreshAfterFight=false
local empty=0
local wins=0
local function halt(reason)
 halted=true printWarning('ARRET TAINELA '..reason) stopScript() return false
end
function onFightEnd(r)
 if r.won~=true then halt('defaite ou resultat inconnu : diagnostic avant reprise') return end
 wins=wins+1
 refreshAfterFight=true
 printSuccess('TAINELA VICTOIRE '..wins..' kamas='..tostring(r.kamas)..' fightId='..tostring(r.fightId))
 for _,o in ipairs(r.loot or {}) do printSuccess('TAINELA BUTIN gid='..o.gid..' quantite='..o.qty..' nom='..tostring(o.name)) end
end
function bank()
 if isInFight() then return true end
 if halted or isDead() then return false end
 if not travelTo(192415750) or getMapId()~=192415750 then
  printWarning('TAINELA banque trajet temporairement impossible : pause60s') delay(60000) return true
 end
 if not npc:npcBank() then return halt('banque refusee, aucun retrait automatique') end
 local before=getPods()
 local deposited=exchange:putAllItems()
 leaveDialog()
 if not deposited or getPodsPercent()>=85 or getPods()>=before then return halt('depot non confirme') end
 printSuccess('TAINELA BANQUE '..before..'->'..getPods())
 return true
end
-- Les ActorId de groupes ne sont pas une identite globale inter-cartes.
local function groupKey(g)
 local parts={}
 for _,m in ipairs(g.monsters or {}) do parts[#parts+1]=m.gid.."x"..m.count end
 table.sort(parts)
 return tostring(getMapId())..":"..tostring(g.id)..":"..table.concat(parts,",")
end
local function candidate(g)
 if g.count<1 or g.count>8 or #g.monsters==0 then return 0 end
 local blacks=0 local count=0
 for _,m in ipairs(g.monsters) do
  count=count+m.count
  if m.gid==149 then blacks=blacks+m.count end
 end
 if count~=g.count or blacks==0 then return 0 end
 -- Tout groupe contenant un Noir reste eligible, meme sept Blancs + un Noir.
 -- Score strictement positif : seul l'ordre de preference varie.
 return 10000+blacks*1000-g.count*10
end
function move()
 if isInFight() then return true end
 if halted or isDead() then return halt('personnage mort ou arret de securite') end
 if refreshAfterFight then
  delay(1500) map:sendMapRequest() refreshAfterFight=false
 end
 if not synced then
  if not map:sendMapRequest() then printWarning('TAINELA resynchronisation en attente') delay(5000) return true end
  synced=true
 end
 if getPodsPercent()>=85 then return bank() end
 local destination=maps[index]
 if getMapId()~=destination then
  if not travelTo(destination) or getMapId()~=destination then
   if isInFight() then return true end
   blocked[destination]=os.time()+120
   map:sendMapRequest() synced=false
   index=index%#maps+1 delay(2000) return true
  end
 end
 local best=nil local score=0 local eligible=0 local waiting=0
 local zone=coinMaps[destination] and "Coin des Bouftous" or "Tainela"
 for k,untilTime in pairs(unavailable) do if os.time()>=untilTime then unavailable[k]=nil end end
 for _,g in ipairs(map:getMonsters()) do
  local value=candidate(g)
  if value>0 then
   eligible=eligible+1
   local k=groupKey(g)
   if not unavailable[k] then
    if value>score then best=g score=value end
   else waiting=waiting+1 end
  end
 end
 if best then
  print('BOUFTOUS zone='..zone..' CIBLE carte='..destination..' cellule='..best.cell..' monstres='..best.count..' niveau='..best.level)
  local key=groupKey(best)
  if fightCell(best.cell) then
   empty=0 unavailable[key]=nil
  else
   if isInFight() then return true end
   unavailable[key]=os.time()+30
   printWarning('BOUFTOUS echec engagement carte='..destination..' groupe='..best.id..' cellule='..best.cell..' : pause30s sur ce groupe uniquement')
   map:sendMapRequest() delay(1000)
  end
  return not halted
 end
 if waiting>0 then print('BOUFTOUS zone='..zone..' eligibles='..eligible..' en pause apres echec='..waiting) end
 empty=empty+1
 local found=false
 for _=1,#maps do
  index=index%#maps+1
  if not blocked[maps[index]] or os.time()>=blocked[maps[index]] then found=true break end
 end
 if not found then print('TAINELA cartes en pause : attente60s') delay(60000) blocked={} end
 if empty>=#maps then print('TAINELA ronde sans cible : attente30s') delay(30000) empty=0 end
 return true
end
