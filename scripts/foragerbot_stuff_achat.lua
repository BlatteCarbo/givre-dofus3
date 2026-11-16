local plan={
 {8273,140190,69000,125,171},{18689,135706,35999,125,159},
 {2469,7760,105000,111,1},{18428,155811,28000,119,40},
 {8278,156848,38000,128,1},{18427,44707,50000,119,49},
 {1040,154473,54999,128,1},{8271,126223,81000,125,170},
 {18426,136144,20999,119,53},{12713,32996,120994,119,30},
 {13764,35277,135899,119,60},{12686,32973,173483,428,6},
 {972,157563,490000,124,60}}
function move()
 if getMapId()~=212600837 then return false end
 local total=0 for _,p in ipairs(plan) do total=total+p[3] end
 if total>1500000 then printWarning('ACHAT budget depasse') return false end
 local spent=0
 for _,p in ipairs(plan) do
  if not npc:npc(-1,6) then printWarning('ACHAT ouverture echouee') return false end
  local found=false
  for _,o in ipairs(sale:offers(p[1])) do
   if o.uid==p[2] and o.prices[1]==p[3] then
    for _,e in ipairs(o.effects) do if e.action==p[4] and e.value==p[5] then found=true end end
    if p[1]==2469 then
     local vita=false for _,e in ipairs(o.effects) do if e.action==125 and e.value==165 then vita=true end end
     found=found and vita
    end
   end
  end
  if not found then printWarning('ACHAT offre changee gid='..p[1]) leaveDialog() return false end
  if not sale:buyOffer(p[1],p[2],1,p[3]) then printWarning('ACHAT ECHEC gid='..p[1]..' ne pas repeter sans inventaire') leaveDialog() return false end
  spent=spent+p[3]
  print('ACHAT_OK gid='..p[1]..' prix='..p[3]..' cumul='..spent)
  leaveDialog() delay(300)
 end
 print('ACHAT_TERMINE total='..spent)
 return false
end
