function move()
 leaveDialog()
 if not travelTo(212601345) then printWarning('MONTURE trajet HDV echoue') return false end
 print('MONTURE carte='..getMapId())
 if not npc:npc(-1,6) then printWarning('MONTURE ouverture echouee') leaveDialog() return false end
 local offers=sale:offers(33020)
 print('MONTURE count='..#offers)
 table.sort(offers,function(a,b) return (a.prices[1] or 999999999)<(b.prices[1] or 999999999) end)
 for i=1,math.min(10,#offers) do
  local o=offers[i] local fx={}
  for _,e in ipairs(o.effects) do fx[#fx+1]=e.action..':'..e.value..':'..e.text end
  print('MONTURE uid='..o.uid..' prix='..(o.prices[1] or -1)..' jets='..table.concat(fx,'|'))
 end
 leaveDialog() return false
end
