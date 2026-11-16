-- Adaptation 2026 du parcours fourni (Kirua, 2020). Original conserve.
-- Cartes extraites de la base installee ; sorts appris geres par AutoSpells.
-- Aucun achat, prix fixe, destruction, retrait bancaire ou ancien XML.
-- givre-worlds: [1,2,41]
MIN_MONSTERS=1
MAX_MONSTERS=3
MAX_PODS=85
MIN_LIFE_PERCENT=0 -- regeneration explicite, refus de combattre si elle echoue
FIGHT_LOCK_SPEC=true
FIGHT_LOCK_JOIN=true
local expectedCharacter='Valrianess'
local halted=false
local phase=nil
local index=1
local wins=0
local failures=0
local refresh=true
local excluded={}
local phases={
 {name='Route des ames',untilLevel=5,world=2,maps={153878787,153879299,153879811,153880323,153880835,154010371,154010883},monsters={3270,3271,3272,3273}},
 {name='Champs Incarnam',untilLevel=12,world=2,maps={122684931,153878788,153878789,154010372,154010373,154010374,154010884,154010885,154010886,154011397,154011398,153879300,153879301,153879812,153879813,153880324,153880325,153880836},monsters={970,979,980,981,971,972,973,984}},
 {name='Pious Astrub',untilLevel=45,world=1,maps={67372032,188743681,188743682,188743683,188743684,188743685,188743686,188743687,188744193,188744194,188744195,188744196,188744197,188744198,188744199,188744705,188744706,188744710,188744711,188745217,188745218,188745222,188745223,188745729,188745730,188745734,188746241,188746242,188746246,188746247,188746753,188746754,188746755,188746756,188746757,188746758,188746759,191102976,191102978,191102980,191104000,191104002,191104004,191105024,191105026,191105028,191106048,191106050,191106052},monsters={489,490,491,492,493,494}},
 {name='Scarafeuilles',untilLevel=65,world=1,maps={88080384,88080385,88080386,88080387,88080388,88080389,88080390,88080391,88080641,88080896,88080897,88080898,88080899,88080900,88080901,88080902,88080903,88080904,88080905,88081153,88081408,88081409,88081410,88081411,88081412,88081413,88081414,88081415,88081416,88081417,88081665,88081923,88081924,88081925,88081926,88081927,88081928,88081929,88082177,88082437,88082438,88082439,88082440,88082441,88211968,88211969,88211970,88211971,88211972,88211973,88211974,88211975,88212480,88212481,88212737,88212992,88213249,88215041,88215043,88215045,88215047,88215297},monsters={194,198,240,241}},
 {name='Gelees',untilLevel=200,world=1,maps={88082950,88082951,88082952,88083462,88083463,88083464,88083974,88083975,88083976,88084485,88084486,88084487,88084997,88084998,88084999,88085509,88085510,88085511},monsters={55,56,57}}
}
local function halt(reason)
 halted=true
 printWarning('ENI ARRET '..reason)
 stopScript()
 return false
end
local function contains(list,value)
 for _,v in ipairs(list) do if v==value then return true end end
 return false
end
local function world(id)
 for _,p in ipairs(phases) do
  if p.world==2 and contains(p.maps,id) then return 2 end
 end
 return (id>=153878528 and id<=154011398) and 2 or 1
end
local function choosePhase(level)
 for _,p in ipairs(phases) do if level<p.untilLevel then return p end end
 return nil
end
local function descend()
 if not travelTo(153880835) or getMapId()~=153880835 then return false end
 if not npc:npc(4398,3) then return false end
 -- IDs relus dans NpcData 4398, selection uniquement parmi les reponses recues.
 for _=1,3 do
  local q=npc:question()
  if not q then break end
  local selected=nil
  for _,id in ipairs({36977,36980,36976}) do
   if contains(q.replies,id) then selected=id break end
  end
  if not selected then leaveDialog() return false end
  npc:reply(selected)
  delay(1000)
  if getMapId()~=153880835 then break end
 end
 leaveDialog()
 return getMapId()~=153880835 and world(getMapId())~=2
end
function onFightEnd(result)
 if result.won~=true then halt('defaite ou resultat inconnu ; aucune reprise automatique') return end
 wins=wins+1
 refresh=true
 failures=0
 printSuccess('ENI VICTOIRE '..wins..' niveau='..getCharacterLevel()..' xp='..getCharacterXp())
end
function bank()
 -- Le nouveau personnage ne connait pas encore de banque : conserver tous les objets.
 return halt('pods >=85% : depot bancaire a verifier avant de poursuivre ; aucun objet supprime')
end
function move()
 if isInFight() then return true end
 if halted then return false end
 if getCharacterName()~=expectedCharacter or getServerName()~='Kourial' then
  return halt('identite/serveur inattendu : '..getCharacterName()..' / '..getServerName())
 end
 if isDead() then return halt('personnage mort') end
 local level=getCharacterLevel()
 if level<=0 then delay(2000) return true end
 if level>=200 then printSuccess('ENI objectif niveau200 atteint') stopScript() return false end
 if getPodsPercent()>=85 then return bank() end
 if refresh then map:sendMapRequest() refresh=false end
 local p=choosePhase(level)
 if not p then return halt('aucun palier disponible') end
 if phase~=p then
  phase=p index=1 excluded={}
  printSuccess('ENI PALIER '..p.name..' niveau='..level)
 end
 if p.world==1 and world(getMapId())==2 then
  if not descend() then return halt('sortie Incarnam non confirmee, verifier dialogue du portail') end
 end
 if getMapId()==241445377 then
  leaveDialog()
  goUseInteractive(475)
  delay(1500)
  if getMapId()==241445377 then return halt('porte du temple non franchie') end
 end
 if not contains(p.maps,getMapId()) then
  local destination=p.maps[index]
  if not travelTo(destination) or getMapId()~=destination then
   if isInFight() then return true end
   failures=failures+1 index=index%#p.maps+1
   if failures>=6 then return halt('six trajets refuses : diagnostic navigation requis') end
   delay(3000) return true
  end
 end
 local best=nil
 local maxGroup=level<12 and 3 or (level<30 and 4 or 8)
 local maxLevel=math.max(6,level*2+2)
 -- Filtres moteur plus selection locale : refuser les especes hors palier.
 MAX_MONSTERS=maxGroup
 FORCE_MONSTERS=p.monsters
 FORBIDDEN_MONSTERS={}
 for _,g in ipairs(map:getMonsters()) do
  local valid=g.count>=1 and g.count<=maxGroup and g.level>0 and g.level<=maxLevel and not g.arch
  local count=0
  for _,m in ipairs(g.monsters or {}) do
   count=count+m.count
   if not contains(p.monsters,m.gid) then valid=false end
  end
  local key=tostring(getMapId())..':'..tostring(g.id)
  if count~=g.count or count==0 then valid=false end
  if valid and (not excluded[key] or os.time()>=excluded[key]) and (not best or g.count>best.count or (g.count==best.count and g.level<best.level)) then best=g end
 end
 if best then
  local hp=getLifePointsPercent()
  if hp<0 then return halt('PV inconnus') end
  if hp<90 and not regenerate(90,180) then return halt('regeneration non confirmee') end
  if isInFight() then return true end
  if getLifePointsPercent()<90 then return halt('PV insuffisants apres regeneration') end
  print('ENI CIBLE carte='..getMapId()..' cellule='..best.cell..' nombre='..best.count..' niveau='..best.level)
  if not fightCell(best.cell) and not isInFight() then
   excluded[tostring(getMapId())..':'..tostring(best.id)]=os.time()+60
   refresh=true delay(1500)
  end
  return not halted
 end
 index=index%#p.maps+1
 if index==1 then print('ENI ronde sans cible admissible : pause20s') delay(20000) end
 local destination=p.maps[index]
 if getMapId()~=destination then
  if not travelTo(destination) then
   failures=failures+1
   if failures>=6 then return halt('six trajets refuses') end
  else failures=0 end
 end
 delay(1000)
 return true
end
