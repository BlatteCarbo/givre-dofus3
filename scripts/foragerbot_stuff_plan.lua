local ids={8273,18689,2469,18428,8278,18427,1040,8271,18426,12713,13764,12686,972}
local function stat(o,id)
 for _,e in ipairs(o.effects) do if e.action==id then return e.value end end
 return 0
end
local function valid(o,gid)
 if gid==2469 then return stat(o,111)>=1 and stat(o,125)>=120 end
 if gid==8273 then return stat(o,111)>=1 and stat(o,125)>=100 and stat(o,138)>=25 end
 if gid==8278 then return stat(o,128)>=1 and stat(o,138)>=25 end
 if gid==18689 or gid==18426 then return stat(o,117)>=1 and stat(o,119)>=25 end
 return true
end
local function score(o)
 return stat(o,125)+3*stat(o,119)+3*stat(o,138)+15*stat(o,112)+15*stat(o,428)+20*stat(o,117)+80*stat(o,128)+80*stat(o,111)
end
function move()
 if getMapId()~=212600837 then return false end
 local total=0
 for _,gid in ipairs(ids) do
  if not npc:npc(-1,6) then return false end
  local offers=sale:offers(gid) local min=999999999
  for _,o in ipairs(offers) do if o.prices[1] and o.prices[1]>0 and valid(o,gid) then min=math.min(min,o.prices[1]) end end
  local best=nil local bestScore=-999999999
  for _,o in ipairs(offers) do
   local p=o.prices[1]
   if p and p>0 and p<=min+20000 and valid(o,gid) then
    local s=score(o)-(p-min)/1000
    if s>bestScore then best=o bestScore=s end
   end
  end
  if not best then printWarning('PLAN_MISSING '..gid) leaveDialog() return false end
  local fx={} for _,e in ipairs(best.effects) do fx[#fx+1]=e.action..':'..e.value end
  print('PLAN gid='..gid..' uid='..best.uid..' prix='..best.prices[1]..' jets='..table.concat(fx,'|'))
  total=total+best.prices[1]
  leaveDialog() delay(300)
 end
 print('PLAN_TOTAL '..total)
 return false
end
