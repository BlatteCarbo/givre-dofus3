using System;

namespace GivreClone.App.Services.Treasure;

public static class FouduglenScript
{
    public static string Build(int count, int budget, int maxUnitPrice)
    {
        if (count is < 1 or > 100 || budget < 0 || maxUnitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        return TreasureScript.Build(preserveHunt: true) + $$"""

local requested, budget, unitCap = {{count}}, {{budget}}, {{maxUnitPrice}}
local completed, spent, initialized = 0, 0, false
local finalSent, failed = false, false
local solveStep = move
local function halt(why)
  failed = true
  print("[fouduglen] ARRET " .. why .. " ; achats=" .. spent)
  stopScript()
  return false
end
local function machine(skillId)
  for _,e in ipairs(map:getInteractives()) do
    for _,s in ipairs(e.skills) do
      if s.id == skillId and s.enabled then return e,s.uid end
    end
  end
end
function onFightEnd(result)
  if not finalSent then return end
  finalSent = false
  if not result or not result.won then halt("combat non gagne") return end
  completed = completed + 1
  print("[fouduglen] VICTOIRE " .. completed .. "/" .. requested .. " achats=" .. spent)
end
function onTurn()
  combat:playTurn()
end
function move()
  if failed then return false end
  if completed >= requested then
    print("[fouduglen] TERMINE victoires=" .. completed .. " achats=" .. spent)
    return false
  end
  if not initialized then
    if treasure:isHunting() then return halt("une chasse est deja active") end
    local missingMaps = math.max(0, requested - getInventoryItemCount(15904))
    local needs = {
      math.max(0, missingMaps - getInventoryItemCount(15902)),
      math.max(0, missingMaps - getInventoryItemCount(15903))
    }
    if needs[1] + needs[2] > 0 then
      if not goToHdvBuy(191104004) then return halt("HDV ressources inaccessible") end
      for i,gid in ipairs({15902,15903}) do
        for n=1,needs[i] do
          local price = sale:GetPriceItem(gid,1)
          if price < 1 or price > unitCap or spent + price > budget
            or character:kamas() < price then return halt("prix hors plafond ou offre absente GID " .. gid) end
          local before, balance = getInventoryItemCount(gid), character:kamas()
          if not sale:buyItem(gid,1,price) then return halt("achat refuse GID " .. gid) end
          if getInventoryItemCount(gid) <= before then return halt("achat non confirme GID " .. gid) end
          spent = spent + math.max(0,balance-character:kamas())
          print("[fouduglen] ACHAT gid=" .. gid .. " prix=" .. price .. " total=" .. spent)
        end
      end
      global:leaveDialog()
    end
    if not travelTo(128452097) then return halt("Malle inaccessible") end
    if missingMaps > 0 then
      local e,uid = machine(275)
      if not e or not map:useById(e.elementId,uid) then return halt("pupitre inaccessible") end
      global:leaveDialog()
      if not craftItem(e.elementId,uid,{ {gid=15902,quantity=1}, {gid=15903,quantity=1} },missingMaps)
        or getInventoryItemCount(15904) < requested then return halt("assemblage non confirme") end
      print("[fouduglen] ASSEMBLAGE cartes=" .. getInventoryItemCount(15904))
    end
    initialized = true
  end
  if finalSent then return true end
  if not treasure:isHunting() then
    if getInventoryItemCount(15904) < 1 then return halt("carte absente") end
    if not travelTo(128452097) then return halt("retour Malle impossible") end
    local e,uid = machine(277)
    if not e or not map:useById(e.elementId,uid) then return halt("table inaccessible") end
    if not treasure:startLegendary(29) then return halt("lancement legendaire refuse") end
    print("[fouduglen] DEPART " .. (completed+1) .. "/" .. requested)
    return true
  end
  if treasure:isFightStep() then
    if not travelTo(treasure:getStartMapId()) then return halt("combat inaccessible") end
    finalSent = true
    if not treasure:huntDig() then finalSent=false return halt("combat non lance") end
    return true
  end
  return solveStep()
end
""";
    }
}
