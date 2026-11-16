param([string]$MoonSharp,[string]$Source="$PSScriptRoot/Incarnam-Quetes-Valrianess-2026.lua")
$ErrorActionPreference='Stop'
Add-Type -Path $MoonSharp
$code=[IO.File]::ReadAllText($Source)
$prelude=@'
stoppedFlag=false replied=0 moved=0 ready=0
function stopScript() stoppedFlag=true end
function print(...) end function printError(...) end
function printWarning(...) end function printSuccess(...) end
function delay(...) end
function getCharacterName() return 'Valrianess' end
function getServerName() return 'Kourial' end
function getMapId() return 154010883 end
function isDead() return false end
function isInFight() return false end
function leaveDialog() end
function travelTo(...) moved=moved+1 return true end
quest={}
function quest:loaded() return true end
function quest:isFinished(...) return false end
function quest:questObjectiveDone(...) return false end
npc={}
function npc:npc(...) return true end
function npc:question() return {messageId=1,replies={999999},text='Question inconnue'} end
function npc:hasReply(id) return id==999999 end
function npc:reply(id) replied=id return false end
function npc:leaveDialog() end
map={}
function map:getNpcs() return {{npcId=7581}} end
function map:getInteractives() return {{elementId=123,skills={{id=12}}}} end
function map:useById(...) return true end
craft={}
function craft:isOpen() return true end
function craft:putItem(...) return false end
function craft:ready() ready=ready+1 return true end
'@
$cases=@(
 @{name='identite incorrecte';setup="function getCharacterName() return 'Foragerbot' end";check='assert(move()==false and stoppedFlag and moved==0)'},
 @{name='serveur incorrect';setup="function getServerName() return 'Dakal' end";check='assert(move()==false and stoppedFlag and moved==0)'},
 @{name='quetes non chargees';setup='function quest:loaded() return false end';check='assert(move()==true and not stoppedFlag and moved==0)'},
 @{name='quete finie objective fini';setup='function quest:isFinished(q) return q==1631 end';check='assert(odone(1631,9738)==true)'},
 @{name='pas de choix aveugle';setup='';check='assert(talk(7581,{},"TEST")==false and replied==0)'},
 @{name='preference absente refusee';setup='';check='assert(talk(7581,{77754},"TEST")==false and replied==0)'},
 @{name='reponse annoncee uniquement';setup='';check='assert(talk(7581,{999999},"TEST")==true and replied==999999)'},
 @{name='PNJ tardif attendu';setup='local n=0 function map:getNpcs() n=n+1 if n<3 then return {} end return {{npcId=7581}} end';check='assert(talk(7581,{999999},"TEST")==true and replied==999999)'},
 @{name='autre PNJ refuse';setup='function map:getNpcs() return {{npcId=999}} end';check='assert(talk(7581,{999999},"TEST")==false and replied==0)'},
 @{name='pose craft refusee';setup='';check='assert(craftAt(154010883,12,{{289,4}},"TEST")==false and ready==0)'},
 @{name='atelier publie apres arrivee';setup='local n=0 function map:getInteractives() n=n+1 if n<3 then return {} end return {{elementId=123,skills={{id=12}}}} end function craft:putItem() return true end';check='assert(craftAt(154010883,12,{{289,4}},"TEST")==true and ready==1)'},
 @{name='annexe taverne utilise interaction';setup='local here=153358340 function getMapId() return here end function map:useById(id,skill) assert(id==488963 and skill==184) here=153357316 return true end';check='assert(go(153357316)==true and moved==0)'},
 @{name='annexe refusee sans faux succes';setup='function getMapId() return 153358340 end function map:useById() return false end';check='assert(go(153357316)==false and moved==0)'},
 @{name='Aleha choisit les des avant biographie';initialized=$true;setup=@'
INIT=true
function os.time() return 0 end
function map:currentSubAreaId() return 1 end
function getMapId() return 153357316 end
function map:getNpcs() return {{npcId=2909}} end
function quest:isFinished(q) return q~=1634 end
function quest:isActive() return true end
function quest:refresh() end
function quest:questObjectiveDone(q,o) return o~=9775 end
local answered=false local opened=false
function npc:npc() opened=true return true end
function npc:question() if answered or not opened then return nil end return {messageId=20931,replies={25128,25137}} end
function npc:hasReply(id) return not answered and (id==25128 or id==25137) end
function npc:reply(id) replied=id answered=true return true end
'@;check='move() assert(replied==25137)'},
 @{name='defaite arrete';setup='';check='onFightEnd({won=false}) assert(stoppedFlag)'},
 @{name='IA native sans double moteur';setup='';check='assert(onTurn==nil and challengeManagement==nil and DISCORD_WEBHOOK=="")'}
)
foreach($case in $cases){
 $lua=[MoonSharp.Interpreter.Script]::new()
 $testedCode=if($case.initialized){$code.Replace('local INIT = false','local INIT = true')}else{$code}
 try { [void]$lua.DoString($prelude+"`n"+$testedCode+"`n"+$case.setup+"`n"+$case.check,$null,'incarnam-regression') }
 catch { throw "$($case.name): $($_.Exception.InnerException.DecoratedMessage)" }
 Write-Output "PASS $($case.name)"
}
