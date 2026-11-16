local function dump(t,depth)
 if type(t) ~= "table" then return tostring(t) end
 if depth>7 then return "..." end
 local out={}
 for k,v in pairs(t) do out[#out+1]=tostring(k).."="..dump(v,depth+1) end
 return "{"..table.concat(out,",").."}"
end
function messagesRegistering()
 registerMessage("ExchangeTypesItemsExchangerDescriptionForUserEvent",function(m) print("STUFF_OFFERS "..dump(m,0)) end)
end
function move()
 if not travelTo(191106052) or getMapId()~=191106052 then printWarning("STUFF trajet echoue") return false end
 if not npc:npc(-1,6) then printWarning("STUFF HDV impossible") return false end
 for _,gid in ipairs({8273,18689,2469,18428,8278,18427,1040,8271,18426,12713,13764,12686,972}) do
  print("STUFF_PRIX gid="..gid.." prix="..sale:GetPriceItem(gid,1))
  delay(500)
 end
 leaveDialog()
 return false
end