-- Vente ponctuelle du stock de laine de Boufton Noir dans le sac.
-- Prix reels par lot, sous-cote1k ; aucun achat ni retrait bancaire.
local done=false
function move()
 if done then return false end
 if isInFight() then return true end
 done=true
 leaveDialog()
 if not travelTo(212601350) or getMapId()~=212601350 then printWarning('LAINE trajet HDV echoue') return false end
 if not npc:npc(-1,6) then printWarning('LAINE ouverture achat echouee') return false end
 local prices={}
 for _,qty in ipairs({100,10,1}) do
  prices[qty]=sale:GetPriceItem(885,qty)
  print('LAINE COTE gid=885 lot='..qty..' prix='..prices[qty])
 end
 leaveDialog()
 if not npc:npc(-1,5) then printWarning('LAINE ouverture vente echouee') return false end
 local initial=getInventoryItemCount(885)
 local listed=0
 print('LAINE STOCK initial='..initial..' places='..sale:AvailableSpace())
 for _,qty in ipairs({100,10,1}) do
  while getInventoryItemCount(885)>=qty and listed+qty<=initial do
   local price=prices[qty]
   if not price or price<=1 then printWarning('LAINE prix inconnu lot='..qty..', aucune vente a prix suppose') leaveDialog() return false end
   if sale:AvailableSpace()<=0 then printWarning('LAINE places insuffisantes') leaveDialog() return false end
   local before=getInventoryItemCount(885)
   local ok=sale:SellItem(885,qty,price-1)
   local after=getInventoryItemCount(885)
   print('LAINE LOT qty='..qty..' prix='..(price-1)..' ok='..tostring(ok)..' stock='..before..'->'..after)
   if not ok or before-after~=qty then printWarning('LAINE resultat non confirme : pas de nouvel essai automatique') leaveDialog() return false end
   listed=listed+qty
  end
 end
 for i=1,sale:ItemsOnSale() do
  if sale:GetItemGID(i)==885 then printSuccess('LAINE ANNONCE qty='..sale:GetItemQuantity(i)..' prix='..sale:GetItemPrice(i)) end
 end
 printSuccess('LAINE FIN mises_en_vente='..listed..' restant='..getInventoryItemCount(885))
 leaveDialog() return false
end
