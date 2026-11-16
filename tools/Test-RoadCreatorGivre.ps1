# Test 100% hors ligne : MoonSharp interprète le Lua et simule quatre cartes.
# Aucun lancement de bot, aucune injection, aucun paquet réseau.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $env:USERPROFILE ".nuget\packages\moonsharp\2.0.0\lib\net40-client\MoonSharp.Interpreter.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "MoonSharp NuGet absent : $dll" }
Add-Type -Path $dll
$script = New-Object MoonSharp.Interpreter.Script
$source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "RoadCreator_Givre_v3_1.lua"))
$before = @'
hooks = {}
written = {}
io = {
    open = function(name, mode)
        if not name:match("^Road_[%d_]+%.lua$") or mode ~= "w" then error("fichier non autorisé : " .. tostring(name) .. " mode=" .. tostring(mode)) end
        return {
            write = function(self, value) written[name] = value end,
            close = function() return true end
        }
    end
}
developer = {
    registerMessage = function(self, name, callback) hooks[name] = callback end
}
global = {
    rcAskFight = function() return ", fight = true" end,
    rcAskGather = function() return ", gather = false" end,
    printSuccess = function(self, msg) end,
    printError = function(self, msg) error(msg) end,
    printWarning = function(self, msg) end,
    printMessage = function(self, msg) end
}
map = {
    currentMapId = function() return 100 end,
    currentCell = function() return 25 end,
    coordsOf = function(self, id) return id, 0 end,
    getMap = function() return { interactives = {
        { elementId = 7, cellId = 44, elementTypeId = -1 },
        { elementId = 8, cellId = 45, elementTypeId = 38 } } } end
}
function isInFight() return false end
'@
$after = @'
stage = "startup"
messagesRegistering()
assert(currentMap == 100)
stage = "map"
hooks["MapChangeRequest"]({ MapId = 101 })
hooks["MapComplementaryInformationEvent"]({ MapId = 101 })
assert(byMap[100].exit == "right", "transition cardinale")
stage = "teleport"
hooks["TeleportRequest"]({ SourceType = 1, MapId = 902 })
hooks["MapComplementaryInformationEvent"]({ MapId = 902 })
assert(byMap[101].exit == "zaapi(902)", "transition zaapi")
stage = "interactive"
hooks["InteractiveUseRequest"]({ ElementId = 8 })
assert(pending == nil, "la récolte ne doit pas être une porte")
hooks["InteractiveUseRequest"]({ ElementId = 7 })
hooks["MapComplementaryInformationEvent"]({ MapId = 903 })
assert(byMap[902].exit == "44" and byMap[902].exitType == "door", "porte")
hooks["MapComplementaryInformationEvent"]({ MapId = 903 })
assert(#steps == 4, "paquet map dupliqué")
assert(byMap[100].fight == true and byMap[100].gather == false, "choix de carte")
stage = "file validation"
local count = 0
for _, text in pairs(written) do
    count = count + 1
    assert(text:find('path = "right"', 1, true), "chemin absent")
    assert(text:find('path = "zaapi(902)"', 1, true), "zaapi absent")
    assert(text:find('door = "44"', 1, true), "porte absente")
    assert(text:find('MAX_MONSTERS = 8', 1, true), "plafond combat")
    generated = text
end
assert(count == 1, "un fichier par enregistrement")
stopped("user")
'@
try {
    $null = $script.DoString($before + "`n" + $source + "`n" + $after)
    $generatedScript = New-Object MoonSharp.Interpreter.Script
    $null = $generatedScript.LoadString($script.Globals.Get("generated").String)
    Write-Output "ROAD_CREATOR_GIVRE_OFFLINE_PASS (4 cartes, bord, zaapi, porte, doublon, fichiers et syntaxe)"
} catch {
    $stage = $script.Globals.Get("stage")
    Write-Error ("ROAD_CREATOR_GIVRE_OFFLINE_FAIL stage=" + $stage + " : " + $_.Exception.ToString())
    exit 1
}
