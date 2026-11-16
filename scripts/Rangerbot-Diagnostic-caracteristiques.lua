local captured=0
local function wire(v,depth)
 if type(v)~="table" then return tostring(v) end
 if depth<=0 then return "..." end
 local out={}
 local n=0
 for k,x in pairs(v) do
  n=n+1 if n>24 then break end
  out[#out+1]=tostring(k).."="..wire(x,depth-1)
 end
 return "{"..table.concat(out,",").."}"
end
function messagesRegistering()
 local seen=0
 for _,name in ipairs({"InventoryWeightEvent","UpdateLifePointsEvent","CharacterCharacteristicUpgradeRequest","CharacterCharacteristicUpgradeResultEvent"}) do
  local label=name
  registerMessage(label,function(p)
   if seen>=35 then return end seen=seen+1
   print("STATPROTO "..label.." "..wire(p,4))
  end,true)
 end
 registerMessage("CharacterCharacteristicsEvent",function(p)
  if seen>=35 then return end seen=seen+1
  for _,v in pairs((p.stats or {}).characteristics or {}) do
   local id=v.characteristic_id or 0
   if id<=15 or id==97 then print("STATENTRY "..wire(v,4)) end
  end
 end)
 print("Capture caracteristiques active")
end
MIN_MONSTERS=1
MAX_MONSTERS=1
MIN_LIFE_PERCENT=95
REGEN_UP_TO_PERCENT=100
FORCE_MONSTERS={4046,4048,4049}
FORBIDDEN_MONSTERS={4047}
local maps={153880064,153880578,153881090,153881089,153881088,153881600,153881601,153881089}
local idx=1
local fights=0
function move()
 if fights<0 or fights>=2 or getPodsPercent()>=90 then return false end
 if not travelTo(maps[idx]) then idx=idx%#maps+1 delay(2000) return true end
 if not fight() then idx=idx%#maps+1 delay(1000) end
 return true
end
function onFightEnd(r)
 fights=fights+1
 print("Cimetiere progression="..fights.." victoire="..tostring(r.won).." niveau="..character:level().." xp="..character:experience())
 if not r.won then fights=-1 printWarning("Defaite: parcours arrete pour verification") end
end
