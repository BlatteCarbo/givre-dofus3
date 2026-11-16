MIN_MONSTERS=1
MAX_MONSTERS=1
MIN_LIFE_PERCENT=95
REGEN_UP_TO_PERCENT=100
FORCE_MONSTERS={4046,4048,4049}
FORBIDDEN_MONSTERS={4047}
local count=0
function messagesRegistering()
 registerMessage("GameActionFightEvent",function(p)
  local v=p.life_points_lost
  if v and v.target_id==80480567649 then
   print("F02_LIFE loss="..tostring(v.loss or 0).." erosion="..tostring(v.permanent_damages or 0).." current="..character:lifePoints().." max="..character:maxLifePoints())
  end
 end)
 print("F02_LIFE_BASE current="..character:lifePoints().." max="..character:maxLifePoints())
end
local maps={153880064,153881089,153881090,153880578,153881088,153881600,153881601}
local idx=1
function move()
 if idx>#maps then return false end
 local map=maps[idx] idx=idx+1
 if travelTo(map) and fight() then return false end
 return true
end
function onFightEnd(r) print("F02_FIGHT_END won="..tostring(r.won)) end
