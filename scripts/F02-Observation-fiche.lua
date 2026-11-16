local seen=0
local function dump(v,d)
 if type(v)~="table" then return tostring(v) end
 if d<=0 then return "..." end
 local out={} local n=0
 for k,x in pairs(v) do n=n+1 if n>30 then break end out[#out+1]=tostring(k).."="..dump(x,d-1) end
 return "{"..table.concat(out,",").."}"
end
function messagesRegistering()
 for _,name in ipairs({"CharacterCharacteristicsEvent","InventoryWeightEvent","ObjectAddedEvent","ObjectsAddedEvent","ObjectModifiedEvent","ObjectMovementEvent","SetUpdateEvent","CharacterCharacteristicUpgradeResultEvent"}) do
 local label=name
 registerMessage(label,function(p)
 if seen>=30 then return end seen=seen+1
 if label=="CharacterCharacteristicsEvent" then
  print("F02_STATS_HEAD "..dump(p.stats,1))
  for _,v in pairs((p.stats or {}).characteristics or (p.stats or {}).f1 or {}) do
   local id=v.characteristic_id or v.f2 or 0
   if id<=15 or id==97 then print("F02_STAT "..dump(v,4)) end
  end
 else print("F02_EVENT "..label.." "..dump(p,5)) end
 end)
 end
 print("F02_CAPTURE_READY")
end
function move() delay(500) return true end
