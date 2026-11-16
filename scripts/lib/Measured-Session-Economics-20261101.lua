-- Read-only endpoint measurements. No actions, waits, files, or price estimates.
local M = {}
local first, result

local function amount(value)
    if type(value) == "number" and value >= 0 and value <= 9007199254740991
        and value == math.floor(value) then return value end
    return nil
end

local function snapshot()
    return {
        wallet = amount(getKamas()),
        character = getCharacterName(),
        server = getServerName(),
        hostElapsedHours = amount(global:elapsedTime())
    }
end

function M.start()
    assert(not first, "measurement already started")
    first = snapshot()
end

-- Optional evidence is an operator-supplied observed receipt subtotal, NOT a
-- complete ledger. Fees already paid from the wallet must not be subtracted twice.
function M.finish(evidence)
    if result then return result end
    assert(first, "measurement not started")
    if evidence ~= nil then
        assert(type(evidence) == "table", "evidence must be a table")
        assert(type(evidence.source) == "string" and #evidence.source > 0
            and #evidence.source <= 160, "receipt source must be 1..160 bytes")
        for _, field in ipairs({ "knownFees", "knownOtherCosts" }) do
            assert(evidence[field] == nil or amount(evidence[field]) ~= nil,
                "receipt amount must be a nonnegative safe integer")
        end
    end
    local last = snapshot()
    local sameIdentity = first.character ~= "" and first.server ~= ""
        and first.character == last.character and first.server == last.server
    result = {
        schemaVersion = 1, status = "finished", sampleCount = 2,
        knownWalletSamples = (first.wallet and 1 or 0) + (last.wallet and 1 or 0),
        first = first, last = last, sameIdentity = sameIdentity,
        walletSource = "getKamas-current-session-cache",
        walletFreshness = "unknown", sampleScope = "two-endpoint-reads-not-transactions",
        durationSource = "global.elapsedTime-whole-host-hours",
        durationStatus = "unknown", cashflowStatus = "unknown",
        feesStatus = "unknown", otherCostsStatus = "unknown",
        costBasisStatus = "unknown", realizedReceiptsStatus = "unknown",
        netProfitabilityStatus = "unknown", rankingStatus = "insufficient-evidence",
        missingEvidence = { "complete-realized-receipts", "complete-fees-and-costs",
            "consumed-stock-cost-basis", "exact-session-duration", "transfer-attribution" }
    }
    if sameIdentity and first.wallet ~= nil and last.wallet ~= nil then
        result.walletDelta = last.wallet - first.wallet
        result.cashflowStatus = "observed"
    end
    if first.hostElapsedHours and last.hostElapsedHours
        and last.hostElapsedHours >= first.hostElapsedHours then
        local hours = last.hostElapsedHours - first.hostElapsedHours
        result.durationLowerBoundSeconds = math.max(0, hours - 1) * 3600
        result.durationUpperBoundSecondsExclusive = (hours + 1) * 3600
    end
    if evidence ~= nil then
        for _, field in ipairs({ "knownFees", "knownOtherCosts" }) do
            if evidence[field] ~= nil then
                result[field] = evidence[field]
            end
        end
        result.costEvidenceSource = evidence.source
        result.costEvidenceScope = "caller-observed-subtotals-not-complete-ledger"
    end
    READONLY_SESSION_ECONOMICS_RESULT = result
    print("SESSION_ECONOMICS samples=2 knownWalletSamples=" .. result.knownWalletSamples
        .. " walletDelta=" .. tostring(result.walletDelta or "unknown")
        .. " knownFees=" .. tostring(result.knownFees or "unknown")
        .. " knownOtherCosts=" .. tostring(result.knownOtherCosts or "unknown")
        .. " durationSeconds=unknown netProfitability=unknown ranking=insufficient-evidence")
    return result
end

return M
