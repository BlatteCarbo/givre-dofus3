-- Expands a recipe and estimates ONLY missing leaf resources. Never crafts/buys.
local CONFIG = {
    targetGid = 16520, -- Same concrete diagnostic target as check-craft-plan-readonly.lua.
    quantity = 1,     -- Desired total, NOT additional crafts; existing stock is consumed first.
    maxRows = 40      -- Combined material/craft detail lines, integer 1..100.
}
local ran = false

function move()
    if ran then return false end
    ran = true
    assert(CONFIG.targetGid >= 1 and CONFIG.targetGid <= 2147483647
        and CONFIG.targetGid == math.floor(CONFIG.targetGid), "targetGid must be a positive int")
    assert(CONFIG.quantity >= 1 and CONFIG.quantity <= 1000
        and CONFIG.quantity == math.floor(CONFIG.quantity), "quantity must be 1..1000")
    assert(CONFIG.maxRows >= 1 and CONFIG.maxRows <= 100
        and CONFIG.maxRows == math.floor(CONFIG.maxRows), "maxRows must be 1..100")
    local server = getServerName()
    if server == "" then server = "unknown" end
    local report = {
        server = server, targetGid = CONFIG.targetGid, quantity = CONFIG.quantity,
        priceSource = "cached-server-average", priceAge = "unknown",
        stockSource = "session-inventory-plus-known-bank", knownSubtotal = 0,
        unknownPriceRows = 0, missingQuantity = 0, jobTooLowSteps = 0, rows = {}
    }
    READONLY_DIAGNOSTIC_RESULT = report
    print("CRAFT_COST server=" .. server .. " targetGid=" .. CONFIG.targetGid
        .. " quantity=" .. CONFIG.quantity
        .. " price=cached-server-average age=unknown bank=known-snapshot")
    local plan, reason = craft:plan(CONFIG.targetGid, CONFIG.quantity)
    if not plan then
        report.status = "unavailable"
        report.reason = reason
        -- Binding errors are kept as data; bounded output does not dump arbitrary tables.
        print("CRAFT_UNAVAILABLE reason=" .. string.sub(tostring(reason), 1, 240))
        return false
    end
    report.status = "ok"
    report.alreadyOwned = plan.alreadyOwned
    table.sort(plan.materials, function(a, b) return a.gid < b.gid end)
    local shown = 0
    for _, material in ipairs(plan.materials) do
        local row = { gid = material.gid, missing = material.missing, name = material.name }
        report.rows[#report.rows + 1] = row
        report.missingQuantity = report.missingQuantity + row.missing
        local price = 0
        if server ~= "unknown" then price = inventory:getAveragePrice(row.gid) end
        if price > 0 then
            row.unitAverage = price
            row.estimatedCost = price * row.missing
            report.knownSubtotal = report.knownSubtotal + row.estimatedCost
        else
            report.unknownPriceRows = report.unknownPriceRows + 1
        end
        if shown < CONFIG.maxRows then
            print("MISSING gid=" .. row.gid .. " qty=" .. row.missing
                .. " unitAverage=" .. tostring(row.unitAverage or "unknown")
                .. " estimatedCost=" .. tostring(row.estimatedCost or "unknown"))
            shown = shown + 1
        end
    end
    for _, step in ipairs(plan.crafts) do
        if step.jobTooLow then report.jobTooLowSteps = report.jobTooLowSteps + 1 end
        if shown < CONFIG.maxRows then
            print("CRAFT_STEP gid=" .. step.gid .. " crafts=" .. step.crafts
                .. " requiredLevel=" .. step.jobLevel
                .. " characterJobLevel=" .. step.characterJobLevel
                .. " jobTooLow=" .. tostring(step.jobTooLow))
            shown = shown + 1
        end
    end
    report.craftSteps = #plan.crafts
    report.omittedRows = #plan.materials + #plan.crafts - shown
    if report.unknownPriceRows == 0 then report.estimatedMissingCost = report.knownSubtotal end
    print("CRAFT_SUMMARY missingGids=" .. #report.rows
        .. " missingQuantity=" .. report.missingQuantity
        .. " craftSteps=" .. report.craftSteps .. " jobTooLowSteps=" .. report.jobTooLowSteps
        .. " knownSubtotal=" .. report.knownSubtotal
        .. " unknownPriceRows=" .. report.unknownPriceRows
        .. " estimatedMissingCost=" .. tostring(report.estimatedMissingCost or "unknown")
        .. " omittedRows=" .. report.omittedRows)
    return false
end
