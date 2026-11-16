param([string]$MoonSharp)
$ErrorActionPreference='Stop'
Add-Type -Path $MoonSharp
$code=[IO.File]::ReadAllText("$PSScriptRoot/Bastionbot-Multi-Metiers-Cycle.lua")
$setup=@'
memory={} banks=0 sales=0 reprices=0 here=0 weight=900 waits=0
global={}
function global:addInMemory(k,v) memory[k]=v end
function global:remember(k) return memory[k] end
function memory:set(k,v) self[k]=tonumber(v) end
function memory:get(k) return self[k] end
function print(...) end function printWarning(...) end function printSuccess(...) end function printError(...) end
function delay(ms) waits=waits+ms end
function stopScript() stopped=true end
function isInFight() return false end
function leaveDialog() end
function getMapId() return here end
function travelTo(m) here=m weight=900 return true end
function getPods() return weight end
function getPodsPercent() return weight/10 end
npc={} function npc:npcBank() banks=banks+1 return true end
exchange={} function exchange:putAllItems() weight=100 return true end
function sellBagResources() sales=sales+1 here=206308355 weight=900 return true end
function repriceListings() reprices=reprices+1 return true end
'@
$cases=@(
 @{name='six banques puis septieme HDV';extra='';check='for i=1,6 do handleReturn() assert(banks==i and sales==0 and memory.bastionbot_multi_metiers_bank_v1==i) end handleReturn() assert(banks==7 and sales==1 and reprices==1 and memory.bastionbot_multi_metiers_bank_v1==0)'},
 @{name='depot refuse non compte';extra='function exchange:putAllItems() return false end';check='handleReturn() assert(banks==1 and sales==0 and memory.bastionbot_multi_metiers_bank_v1==nil and waits>=15000)'},
 @{name='combat interdit retour';extra='function isInFight() return true end';check='handleReturn() assert(banks==0 and sales==0)'},
 @{name='aucun filtre de recolte';extra='';check='assert(next(ITEMS_TO_GATHER)==nil and next(ELEMENTS_TO_GATHER)==nil and next(GATHER)==nil)'},
 @{name='echec HDV conserve compteur';extra='function sellBagResources() sales=sales+1 here=123 return false end';check='for i=1,6 do handleReturn() end handleReturn() assert(banks==6 and memory.bastionbot_multi_metiers_bank_v1==6)'}
 @{name='vente vide le sac';extra='function sellBagResources() sales=sales+1 here=206308355 weight=100 return true end';check='for i=1,6 do handleReturn() end handleReturn() assert(banks==6 and sales==1 and memory.bastionbot_multi_metiers_bank_v1==0)'}
 @{name='vente incertaine arrete';extra='function sellBagResources() sales=sales+1 here=206308355 return false end';check='for i=1,6 do handleReturn() end handleReturn() assert(stopped and banks==6 and reprices==0 and memory.bastionbot_multi_metiers_bank_v1==6)'}
)
foreach($case in $cases){
 $lua=[MoonSharp.Interpreter.Script]::new()
 try{[void]$lua.DoString($code+"`n"+$setup+"`n"+$case.extra+"`n"+$case.check,$null,'bastionbot-cycle')}
 catch{throw "$($case.name): $($_.Exception.InnerException.DecoratedMessage)"}
 "PASS $($case.name)"
}
