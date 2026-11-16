function move()
 if getMapId()~=212600837 then return false end
 for _,gid in ipairs({8273,18689,2469,18428,8278,18427,1040,8271,18426,12713,13764,12686,972,33020}) do
  if not npc:npc(-1,6) then printWarning("HDV ouverture echouee") return false end
  local offers=sale:offers(gid)
  table.sort(offers,function(a,b) return (a.prices[1] or 999999999999)<(b.prices[1] or 999999999999) end)
  print("HDV_COUNT gid="..gid.." count="..#offers)
  for i=1,math.min(8,#offers) do
   local o=offers[i] local fx={}
   for _,e in ipairs(o.effects) do fx[#fx+1]=e.action..":"..e.value..":"..e.text end
   print("HDV_OFFRE gid="..gid.." uid="..o.uid.." prix="..(o.prices[1] or -1).." jets="..table.concat(fx,"|"))
  end
  leaveDialog() delay(300)
 end
 return false
end