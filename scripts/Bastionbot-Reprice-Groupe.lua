-- Reprix des annonces existantes, sans achat ni nouvelle mise en vente.
-- Une cotation par (GID, quantite), cible figee pour tous ses exemplaires.
-- Sous-cote 1K, plancher absolu 50% de la moyenne serveur (interface HDV).
local function snapshot()
    local groups, order = {}, {}
    for i=1,sale:ItemsOnSale() do
        local gid, qty = sale:GetItemGID(i), sale:GetItemQuantity(i)
        local key = tostring(gid)..":"..tostring(qty)
        if not groups[key] then
            groups[key]={gid=gid,qty=qty,rows={}}
            order[#order+1]=groups[key]
        end
        local rows=groups[key].rows
        rows[#rows+1]={uid=sale:GetItemGUID(i),price=sale:GetItemPrice(i)}
    end
    return order
end

function repriceListings()
    if getCharacterName()~="Bastionbot" or isInFight() then return false end
    if getMapId()~=206308355 then printError("Ouvrir le HDV ressources Pandala avant ce script") return false end
    delay(1000) -- le signal d'ouverture precede la publication des annonces
    local groups=snapshot()
    if #groups==0 then printError("Aucune annonce chargee ; aucun prix modifie") return false end
    local started=os.time()
    local confirmed, attempted, skipped=0,0,0
    print("REPRICE_GROUPE debut groupes="..#groups.." lots="..sale:ItemsOnSale())
    for _,g in ipairs(groups) do
        local market=sale:GetPriceItem(g.gid,g.qty)
        local avg=sale:getAveragePriceItem(g.gid,g.qty)
        if market and market>0 then
            local own=false
            for _,r in ipairs(g.rows) do if r.price==market then own=true end end
            local target=math.max(1,own and market or market-1)
            if avg and avg>0 then target=math.max(target,math.ceil(avg*0.5)) end
            for _,r in ipairs(g.rows) do
                if r.price>target then
                    attempted=attempted+1
                    local ok=sale:EditPrice(r.uid,target,g.qty)
                    if ok then confirmed=confirmed+1 end
                    print("REPRICE_LOT gid="..g.gid.." qty="..g.qty.." ancien="..r.price.." cible="..target.." confirme="..tostring(ok))
                else skipped=skipped+1 end
            end
        else skipped=skipped+#g.rows end
    end
    printSuccess("REPRICE_GROUPE fin confirmes="..confirmed.." tentatives="..attempted.." inchanges="..skipped.." secondes="..(os.time()-started))
    return true
end

function move() repriceListings() return false end
