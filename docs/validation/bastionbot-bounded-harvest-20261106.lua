-- Append to the inspected Frigost harvest source. No commerce or monster attack.
local harvestMove = move
local harvestFightEnd = onFightEnd
local finishedFight = false
local passages = 0

function onFightEnd(result)
    finishedFight = true
    harvestFightEnd(result)
    print("ACCEPTANCE BASTIONBOT combat termine; examen du journal requis")
end

function move()
    if isInFight() then return true end
    if finishedFight or passages >= 3 then
        print("ACCEPTANCE BASTIONBOT arret borne passages=" .. passages)
        return false
    end
    passages = passages + 1
    return harvestMove()
end
