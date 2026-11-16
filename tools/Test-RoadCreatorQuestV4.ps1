# Test 100% hors ligne : MoonSharp interprète le Lua et simule quatre cartes.
# Aucun lancement de bot, aucune injection, aucun paquet réseau.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $env:USERPROFILE ".nuget\packages\moonsharp\2.0.0\lib\net40-client\MoonSharp.Interpreter.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "MoonSharp NuGet absent : $dll" }
Add-Type -Path $dll
$script = [MoonSharp.Interpreter.Script]::new([MoonSharp.Interpreter.CoreModules]::Preset_SoftSandbox)
$source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "RoadCreator_Givre_v4_0.lua"))
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
    getResources = function() return {{cell = 45}} end,
    getMap = function() return { actors = {
        { contextualId = -77, npcId = 522 } }, interactives = {
        { elementId = 7, cellId = 44, elementTypeId = -1 },
        { elementId = 8, cellId = 45, elementTypeId = 38, resource = true },
        { elementId = 9, cellId = 47, elementTypeId = 600, resource = false } } } end
}
inventory = { items = function() return {{ uid = 901, gid = 1234 }} end }
function isInFight() return false end
'@
$after = @'
stage = "startup"
messagesRegistering()
assert(currentMap == 100)
stage = "npc and quest"
hooks["MapMovementRequest"]({ KeyCells = { 21, 55 + 4096 } })
assert(currentVisit.actions[1].type == "move" and currentVisit.actions[1].cell == 55, "mouvement intra-carte")
hooks["NpcGenericActionRequest"]({ NpcId = -77, NpcActionId = 3 })
hooks["NpcDialogQuestionEvent"]({ MessageId = 120 })
hooks["NpcDialogReplyRequest"]({ ReplyId = 333 })
hooks["DialogLeaveRequest"]({})
hooks["InteractiveUseRequest"]({ ElementId = 9 })
hooks["ObjectUseRequest"]({ ObjectUid = 901 })
hooks["EmotePlayRequest"]({ EmoteId = 8 })
hooks["QuestStartedEvent"]({ QuestId = 44 })
hooks["QuestStepStartedEvent"]({ QuestId = 44, StepId = 5 })
hooks["QuestObjectiveValidationRequest"]({ QuestId = 44, ObjectiveId = 301 })
assert(#currentVisit.actions == 8, "mouvement, PNJ, reply, leave, interactif, consommable, emote, objectif ; lu=" .. #currentVisit.actions)
assert(currentVisit.actions[2].npc == 522, "acteur PNJ -> modèle stable")
assert(currentVisit.actions[3].reply == 333 and currentVisit.actions[3].question == 120, "question / id réponse")
assert(currentVisit.actions[6].gid == 1234, "UID inventaire -> GID")
assert(#questEvents == 2, "journal quête")
assert(#visits == 1, "une visite initiale")
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
assert(#visits == 4, "historique visites")
assert(byMap[100].fight == true and byMap[100].gather == false, "choix de carte")
stage = "repeated and npc teleport"
hooks["MapChangeRequest"]({ MapId = 100 })
hooks["MapComplementaryInformationEvent"]({ MapId = 100 })
assert(#steps == 4 and #visits == 5, "revisite garde sa chronologie")
hooks["NpcGenericActionRequest"]({ NpcId = -77, NpcActionId = 3 })
hooks["NpcDialogQuestionEvent"]({ MessageId = 155 })
hooks["NpcDialogReplyRequest"]({ ReplyId = 444 })
hooks["MapComplementaryInformationEvent"]({ MapId = 1500 })
assert(visits[5].exitType == "npc", "téléport PNJ sans path inventé")
stage = "file validation"
local count = 0
for _, text in pairs(written) do
    count = count + 1
    assert(text:find('path = "right"', 1, true), "chemin absent")
    assert(text:find('path = "zaapi(902)"', 1, true), "zaapi absent")
    assert(text:find('door = "44"', 1, true), "porte absente")
    assert(text:find('MAX_MONSTERS = 8', 1, true), "plafond combat")
    assert(text:find('RC_MODE = "quest"', 1, true), "mode quête par défaut")
    assert(text:find('npc=522', 1, true), "PNJ modèle manquant")
    assert(text:find('reply=333', 1, true), "réponse PNJ manquante")
    assert(text:find('question=120', 1, true), "garde question manquante")
    assert(text:find('gid=1234', 1, true), "consommable absent")
    assert(text:find('type="move", cell=55', 1, true), "mouvement cell 55 absent")
    assert(text:find('QuestStartedEvent', 1, true), "quête manquante")
    assert(text:find('type="questValidate", questId=44, objectiveId=301', 1, true), "validation objectif manquante")
    assert(text:find('exitType="npc"', 1, true), "sortie PNJ manquante")
    generated = text
end
assert(count == 1, "un fichier par enregistrement")
stopped("user")
'@
try {
    $null = $script.DoString($before + "`n" + $source + "`n" + $after)
    $generatedScript = [MoonSharp.Interpreter.Script]::new([MoonSharp.Interpreter.CoreModules]::Preset_SoftSandbox)
    $null = $generatedScript.LoadString($script.Globals.Get("generated").String)
    # Relecture SIMULÉE du parcours sur la même carte : séquence garantie,
    # id de question/vraie réponse, checkpoint de mémoire et pause sur émote inconnue.
    $replayPrelude = @'
events = {}
mem = {}
stoppedByGuard = false
memory = {
  get = function(self, k, def) return mem[k] or def end,
  set = function(self, k, value) mem[k] = value; return true end,
  delete = function(self, k) mem[k] = nil; return true end
}
map = {
  currentMapId = function() return 100 end,
  moveToCell = function(self, cell) events[#events+1] = "move:"..cell; return true end,
  useCell = function(self, cell) events[#events+1] = "use:"..cell; return true end
}
npc = {
  npc = function(self, n, a) events[#events+1] = "npc:"..n; return true end,
  question = function() return { messageId = 120 } end,
  hasReply = function(self, reply) return reply == 333 end,
  sendReply = function(self, reply) events[#events+1] = "reply:"..reply; return true end
}
inventory = { useItem = function(self, gid, qty) events[#events+1] = "item:"..gid; return true end }
quest = { questObjectiveDone = function() return false end,
  validateObjective = function() return true end }
global = {
  delay = function() end, printWarning = function(self, msg) events[#events+1] = msg end,
  leaveDialog = function() return true end,
  finishScript = function() stoppedByGuard = true end
}
'@
    $replaySteps = @'
for n=1,6 do
  local t = move()
  assert(#t == 1 and type(t[1].custom) == "function", "étape de replay "..n)
  assert(t[1].fight and not t[1].gather, "options fight/gather perdues au replay")
  t[1].custom()
end
assert(table.concat(events, "|") == "move:55|npc:522|reply:333|use:47|item:1234",
       "ordre de replay : " .. table.concat(events, "|"))
local pause = move()
assert(#pause == 1 and type(pause[1].custom) == "function", "pause manuelle manquante")
pause[1].custom()
assert(stoppedByGuard and events[#events]:find("Intervention manuelle", 1, true),
       "garde émote non supportée : " .. tostring(events[#events]))
-- L'opérateur a traité manuellement l'émote : passer uniquement CE jalon.
RC_SKIP_CURRENT_MANUAL = true
local skipped = move()
assert(#skipped == 0 and RC_SKIP_CURRENT_MANUAL == false, "skip manuel one-shot")
local objective = move()
assert(#objective == 1 and type(objective[1].custom) == "function",
       "objectif de quête absent après étape manuelle")
objective[1].custom()
'@
    $replay = [MoonSharp.Interpreter.Script]::new([MoonSharp.Interpreter.CoreModules]::Preset_SoftSandbox)
    $null = $replay.DoString($replayPrelude + "`n" + $script.Globals.Get("generated").String + "`n" + $replaySteps)
    # La même capture ne doit JAMAIS répondre si la question du serveur a changé.
    $wrongQuestion = @'
for n=1,2 do local step = move(); step[1].custom() end
local step = move()
step[1].custom()
assert(stoppedByGuard, "garde question absente")
assert(table.concat(events, "|"):find("reply:333", 1, true) == nil,
       "réponse dangereuse émise")
'@
    $wrongPrelude = $replayPrelude.Replace("messageId = 120", "messageId = 999")
    $wrong = [MoonSharp.Interpreter.Script]::new([MoonSharp.Interpreter.CoreModules]::Preset_SoftSandbox)
    $null = $wrong.DoString($wrongPrelude + "`n" + $script.Globals.Get("generated").String + "`n" + $wrongQuestion)
    Write-Output "ROAD_QUEST_V4_OFFLINE_PASS (PNJ, replyId/question, quête, consommable, interactifs, émote, 6 visites, téléport PNJ, Lua)"
} catch {
    $stage = $script.Globals.Get("stage")
    Write-Error ("ROAD_CREATOR_GIVRE_OFFLINE_FAIL stage=" + $stage + " : " + $_.Exception.ToString())
    exit 1
}
