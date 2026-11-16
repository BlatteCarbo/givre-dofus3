function move()
 if getMapId()~=212601345 or not npc:npc(-1,6) then return false end
 local best=nil
 for _,o in ipairs(sale:offers(33020)) do
  local vita=0 local agi=0
  for _,e in ipairs(o.effects) do
   if e.action==125 then vita=e.value end
   if e.action==119 then agi=e.value end
  end
  if o.prices[1] and o.prices[1]>0 and o.prices[1]<=96627 and vita>=300 and agi>=100 then
   if not best or o.prices[1]<best.prices[1] then best=o end
  end
 end
 if best then
  print('MONTURE_PLAN uid='..best.uid..' prix='..best.prices[1])
  for _,e in ipairs(best.effects) do print('MONTURE_EFFET '..e.action..':'..e.value..':'..e.text) end
 else printWarning('MONTURE aucune niveau100 dans budget') end
 leaveDialog() return false
end
