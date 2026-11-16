-- Append to the existing miner script for one bounded acceptance run.
-- Keep its bank, travel and defeat handling; stop after the first fight
-- (win or loss), or twelve move callbacks if no protector appears.
local minerMove = move
local minerFightEnd = onFightEnd
local acceptanceFightEnded = false
local acceptancePassages = 0

function onFightEnd(result)
    acceptanceFightEnded = true
    minerFightEnd(result)
    print("ACCEPTANCE SYLVANBOT combat termine; examen du journal requis")
end

function move()
    if isInFight() then return true end
    if acceptanceFightEnded or acceptancePassages >= 12 then
        print("ACCEPTANCE SYLVANBOT arret borne passages=" .. acceptancePassages)
        return false
    end
    acceptancePassages = acceptancePassages + 1
    return minerMove()
end
