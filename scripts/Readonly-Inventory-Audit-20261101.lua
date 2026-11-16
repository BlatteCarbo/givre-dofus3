-- One-shot snapshot. No inventory, market, movement or character mutations.
local CONFIG = { maxRows = 40 } -- Integer 1..100; bag rows grouped by GID.
local ran = false

function move()
    if ran then return false end
    ran = true
    assert(CONFIG.maxRows >= 1 and CONFIG.maxRows <= 100
        and CONFIG.maxRows == math.floor(CONFIG.maxRows), "maxRows must be 1..100")
    local server = getServerName()
    if server == "" then server = "unknown" end
    local report = {
        server = server, priceSource = "cached-server-average",
        priceAge = "unknown", stockSource = "current-session-inventory",
        bagStacks = 0, equippedStacks = 0, bagQuantity = 0,
        knownSubtotal = 0, unknownPriceRows = 0, rows = {}
    }
    local groups = {}
    for _, item in ipairs(inventory:items()) do
        if item.pos == 63 then
            report.bagStacks = report.bagStacks + 1
            report.bagQuantity = report.bagQuantity + item.qty
            local row = groups[item.gid]
            if not row then
                row = { gid = item.gid, name = item.name, quantity = 0, stacks = 0 }
                groups[item.gid] = row
                report.rows[#report.rows + 1] = row
            end
            row.quantity = row.quantity + item.qty
            row.stacks = row.stacks + 1
        else
            report.equippedStacks = report.equippedStacks + 1
        end
    end
    table.sort(report.rows, function(a, b) return a.gid < b.gid end)
    print("INVENTORY_AUDIT server=" .. server
        .. " stock=current-session-inventory bank=excluded"
        .. " price=cached-server-average age=unknown")
    for index, row in ipairs(report.rows) do
        -- An unidentified server must not be presented as a known price scope.
        local price = 0
        if server ~= "unknown" then price = inventory:getAveragePrice(row.gid) end
        if price > 0 then
            row.unitAverage = price
            row.estimatedValue = price * row.quantity
            report.knownSubtotal = report.knownSubtotal + row.estimatedValue
        else
            report.unknownPriceRows = report.unknownPriceRows + 1
        end
        if index <= CONFIG.maxRows then
            print("BAG gid=" .. row.gid .. " qty=" .. row.quantity
                .. " stacks=" .. row.stacks .. " unitAverage="
                .. tostring(row.unitAverage or "unknown")
                .. " estimatedValue=" .. tostring(row.estimatedValue or "unknown"))
        end
    end
    report.omittedRows = math.max(0, #report.rows - CONFIG.maxRows)
    if report.unknownPriceRows == 0 then report.estimatedTotal = report.knownSubtotal end
    READONLY_DIAGNOSTIC_RESULT = report
    print("INVENTORY_SUMMARY bagStacks=" .. report.bagStacks
        .. " bagQuantity=" .. report.bagQuantity .. " uniqueBagGids=" .. #report.rows
        .. " equippedStacks=" .. report.equippedStacks
        .. " knownSubtotal=" .. report.knownSubtotal
        .. " unknownPriceRows=" .. report.unknownPriceRows
        .. " estimatedTotal=" .. tostring(report.estimatedTotal or "unknown")
        .. " omittedRows=" .. report.omittedRows)
    return false
end
