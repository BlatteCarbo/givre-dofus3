MIN_MONSTERS=1
MAX_MONSTERS=2
MIN_LIFE_PERCENT=95
REGEN_UP_TO_PERCENT=100
FORCE_MONSTERS={973,972,971}
FORBIDDEN_MONSTERS={984}
local maps={153879300,153879301,153879813,153879812,153880324,153880325,153880836}
local idx=1
local fights=0
function move()
 if fights>=5 then return false end
 config:setMaxMonsters(2)
 if getPodsPercent()>=90 then printWarning("Pods pleins") return false end
 if not travelTo(maps[idx]) then idx=idx%#maps+1 delay(2000) return true end
 if not fight() then idx=idx%#maps+1 delay(1000) end
 return true
end
function onFightEnd(r) fights=fights+1 if not r.won then fights=5 end print("Bilan victoire="..tostring(r.won).." niveau="..character:level().." xp="..character:experience()) end