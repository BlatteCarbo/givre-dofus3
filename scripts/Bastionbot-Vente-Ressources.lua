-- Ressources du sac uniquement. Types categoryId=2 de la base installee.
local RESOURCE_TYPES={15,26,34,35,36,38,39,40,41,46,47,48,50,51,53,54,55,56,57,58,59,60,61,63,65,66,68,70,71,78,84,95,96,98,103,104,105,106,107,108,109,110,111,119,125,152,153,154,164,174,175,179,183,185,189,195,197,209,211,219,228,229,230,231,232,233,241,258,262,266,278,309,312,315,323,326,335,336}
local allowed={}
for _,id in ipairs(RESOURCE_TYPES) do allowed[id]=true end

function sellBagResources()
    if getCharacterName()~="Bastionbot" or isInFight() then return false end
    leaveDialog()
    if not goToHdv(206308355) then printWarning("VENTE ouverture HDV non confirmee") return false end
    delay(1000)
    local merged=false
    if sale:AvailableSpace()>0 then
        merged=sale:includeStorages(true,true)
        if not merged then printWarning("VENTE reserves non chargees : sac seulement") end
    end
    local sold, failed=0,0
    local gids, seen={},{}
    for _,it in ipairs(inventory:items()) do
        if it.pos==63 and allowed[inventory:typeId(it.gid)] and not seen[it.gid] then
            gids[#gids+1]=it.gid seen[it.gid]=true
        end
    end
    for _,gid in ipairs(gids) do
        if sale:AvailableSpace()==0 then break end
        local prices={}
        for _,qty in ipairs({1000,100,10,1}) do prices[qty]=sale:GetPriceItem(gid,qty) end
        local avg=sale:getAveragePriceItem(gid,1)
        local mine={}
        for i=1,sale:ItemsOnSale() do
            if sale:GetItemGID(i)==gid then
                local q,p=sale:GetItemQuantity(i),sale:GetItemPrice(i)
                mine[q]=math.min(mine[q] or p,p)
            end
        end
        local abort=false
        for _,qty in ipairs({1000,100,10,1}) do
            local market=prices[qty]
            if market and market>0 then
                local target=math.max(1,mine[qty]==market and market or market-1)
                if avg and avg>0 then target=math.max(target,math.ceil(avg*qty*0.5)) end
                -- Ne pas creer une annonce hors marche quand le plancher est superieur.
                if target<=market then
                    while inventory:count(gid)>=qty and sale:AvailableSpace()>0 do
                        local before=inventory:count(gid)
                        local ok=sale:SellItem(gid,qty,target)
                        if ok then
                            for _=1,75 do if inventory:count(gid)<=before-qty then break end delay(20) end
                        end
                        local after=inventory:count(gid)
                        print("VENTE_LOT gid="..gid.." qty="..qty.." prix="..target.." confirme="..tostring(ok).." stock="..before.."->"..after)
                        if not ok or before-after~=qty then failed=failed+1 abort=true break end
                        sold=sold+1
                    end
                end
            end
            if abort then break end
        end
    end
    printSuccess("VENTE_FIN lots="..sold.." resultats_non_confirmes="..failed.." places="..sale:AvailableSpace())
    -- Toujours restaurer le sac seul avant voyage/recolte/banque.
    if merged and not sale:includeStorages(false,false) then
        printError("Inventaire du sac non resynchronise ; reprise apres reconnexion") stopScript() return false
    end
    return failed==0
end

function move() sellBagResources() return false end
