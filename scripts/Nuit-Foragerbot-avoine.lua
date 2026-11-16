-- Recolte sans filtre : toutes ressources accessibles aux metiers du personnage.
-- Récolte de « Avoine » (Paysan niv. 40) — script généré par Givre.
-- Circuit de 5 carte(s), de la plus proche à la plus lointaine, puis on recommence. À adapter : banque, filtres, pauses.

MAX_PODS = 85

-- { id de carte, "x,y", points de récolte recensés }
local ROUTE = {
  { 189795335, "9,-24", 8 },
  { 188746753, "7,-21", 6 },
  { 189792261, "3,-26", 7 },
  { 189792771, "4,-28", 15 },
  { 189792256, "3,-31", 21 },
}

local losses = 0
local step = 0
local failures = 0
local empty = 0

function move()
  if losses >= 2 then printWarning("Deux defaites : arret pour diagnostic") return false end
  if getPodsPercent() >= MAX_PODS then return bank() end
  step = step % #ROUTE + 1
  local target = ROUTE[step]
  if getMapId() ~= target[1] then
    if not travelTo(target[1]) then
      print("Trajet impossible vers la carte " .. target[2] .. " (" .. target[1] .. ") : on passe à la suivante")
      failures = failures + 1
      if failures >= #ROUTE * 2 then printWarning("Circuit inaccessible : arret pour diagnostic") return false end
      delay(2000)
      return true
    end
  end
  failures = 0
  local harvested = gatherAll()
  if harvested then empty = 0 else empty = empty + 1 end
  if empty >= #ROUTE then print("Circuit vide : pause de 30 secondes") delay(30000) empty = 0 end
  return true
end

function bank()
  print("Banque : depot et reprise du circuit")
  if not travelTo(192415750) then printWarning("Banque inaccessible") return false end
  if not npc:npcBank() then printWarning("Ouverture banque impossible") return false end
  local deposited = exchange:putAllItems()
  bank:close()
  if not deposited or getPodsPercent() >= MAX_PODS then printWarning("Depot non confirme : arret") return false end
  return true
end

function onFightEnd(r)
  if r.won == false then losses = losses + 1 end
  print("Bilan protecteur : victoire=" .. tostring(r.won) .. " defaites=" .. losses)
end
