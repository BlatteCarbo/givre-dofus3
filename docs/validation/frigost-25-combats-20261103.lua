-- Bounded live validation: only finished fights count, never an accepted request.
MIN_MONSTERS = 1
MAX_MONSTERS = 2
MIN_LIFE_PERCENT = 95
REGEN_UP_TO_PERCENT = 100

local completed = FRIGOST25_COMPLETED or 0
local wins = FRIGOST25_WINS or 0
local losses = FRIGOST25_LOSSES or 0
local route = { 54169896, 54169897, 54169896, 54169384 }

function move()
    for circuit = 1, 30 do
        for _, destination in ipairs(route) do
            if completed >= 25 then
                print("FRIGOST25_DONE completed=" .. completed .. " wins=" .. wins .. " losses=" .. losses)
                return false
            end
            if map:currentMapId() ~= destination then
                local direction = nil
                for _, candidate in ipairs({ "top", "right", "bottom", "left" }) do
                    if map:neighbourId(candidate) == destination then direction = candidate break end
                end
                if not direction or not map:changeMap(direction) then
                    print("FRIGOST25_STOP no_adjacent_walk map=" .. tostring(map:currentMapId()) .. " target=" .. destination)
                    return false
                end
            end
            for attempt = 1, 3 do
                if completed >= 25 then break end
                local before = combat:lastFightResult()
                local previous = before and before.fightId or -1
                print("FRIGOST25_BEGIN next=" .. (completed + 1) .. " map=" .. tostring(map:currentMapId()))
                if not fight() then break end
                local result = combat:lastFightResult()
                if isInFight() or not result or not result.decoded or result.fightId == previous then
                    print("FRIGOST25_STOP unconfirmed_result completed=" .. completed)
                    return false
                end
                completed = completed + 1
                if result.won then wins = wins + 1 end
                if result.lost then losses = losses + 1 end
                print("FRIGOST25_RESULT n=" .. completed .. " fight=" .. result.fightId
                    .. " outcome=" .. result.outcome .. " durationMs=" .. result.durationMs
                    .. " wins=" .. wins .. " losses=" .. losses)
                if not result.won then
                    print("FRIGOST25_PAUSE inspect_non_win completed=" .. completed)
                    return false
                end
            end
        end
    end
    print("FRIGOST25_STOP route_exhausted completed=" .. completed)
    return false
end
