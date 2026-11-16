function move()
 leaveDialog()
 if not travelTo(212601350) or getMapId()~=212601350 then printWarning('COTE trajet HDV ressources echoue') return false end
 for _,gid in ipairs({6900,384,885,407,362,2563,363}) do
  if not npc:npc(-1,6) then printWarning('COTE ouverture echouee') return false end
  print('COTE gid='..gid..' lot1='..sale:GetPriceItem(gid,1)..' lot10='..sale:GetPriceItem(gid,10)..' lot100='..sale:GetPriceItem(gid,100))
  leaveDialog() delay(300)
 end
 return false
end
