local n=0
local function dump(v,d)
 if type(v)~="table" then return tostring(v) end
 if d<=0 then return "..." end
 local out={} for k,x in pairs(v) do out[#out+1]=tostring(k).."="..dump(x,d-1) end return "{"..table.concat(out,",").."}"
end
function messagesRegistering()
 for _,name in ipairs({"CharacterCharacteristicUpgradeRequest","CharacterCharacteristicUpgradeResultEvent","CharacterCharacteristicsEvent"}) do
 local label=name
 registerMessage(label,function(p)
 if n>=20 then return end n=n+1
 if label~="CharacterCharacteristicsEvent" then print("F02_OFFICIAL "..label.." "..dump(p,3))
 else print("F02_OFFICIAL_SHEET") end
 end,true)
 end
 print("F02_OFFICIAL_CAPTURE_READY")
end
function move() delay(500) return true end
