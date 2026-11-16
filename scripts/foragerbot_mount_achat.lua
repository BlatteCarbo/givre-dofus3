function move()
 if getMapId()~=212601345 or not npc:npc(-1,6) then return false end
 local valid=false
 for _,o in ipairs(sale:offers(33020)) do
  if o.uid==58620 and o.prices[1]==50000 then
   local vita=false local agi=false
   for _,e in ipairs(o.effects) do
    if e.action==125 and e.value==300 then vita=true end
    if e.action==119 and e.value==100 then agi=true end
   end
   valid=vita and agi
  end
 end
 if valid and sale:buyOffer(33020,58620,1,50000) then print('MONTURE_ACHAT_OK total_stuff=1453373')
 else printWarning('MONTURE_ACHAT non confirme, verifier inventaire avant repetition') end
 leaveDialog() return false
end
