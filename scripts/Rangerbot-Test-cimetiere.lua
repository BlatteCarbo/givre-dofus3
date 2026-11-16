MIN_MONSTERS=1
MAX_MONSTERS=1
MIN_LIFE_PERCENT=95
REGEN_UP_TO_PERCENT=100
FORCE_MONSTERS={4046,4048,4049}
FORBIDDEN_MONSTERS={4047}
local maps={153880064,153881089,153881090,153880578,153881088,153881600,153881601}
local idx=1
local fights=0
function move()
 if fights>=5 or getPodsPercent()>=90 then return false end
 if not travelTo(maps[idx]) then idx=idx%#maps+1 delay(2000) return true end
 if not fight() then idx=idx%#maps+1 delay(1000) end
 return true
end
function onFightEnd(r)
 fights=fights+1
 print("Cimetiere test="..fights.." victoire="..tostring(r.won).." niveau="..character:level().." xp="..character:experience())
 if not r.won then fights=5 end
end
