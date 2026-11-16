param([string]$MoonSharp)
$ErrorActionPreference='Stop'
Add-Type -Path $MoonSharp
$code=Get-Content "$PSScriptRoot/Mineurs-H24-Watchdog.lua" -Raw
$setup=@'
now=1000 os.time=function() return now end
starts=0 stops=0 disconnects=0 reconnects=0
function print(...) end function printWarning(...) end function delay(ms) end
b={online=true,inGame=true,inFight=false,scriptRunning=true,mapId=1,pods=10,life=100}
targets={{id="test",code="test",nextTry=0}}
bots={}
function bots:get(id) return b end
function bots:startScript(id,code) starts=starts+1 b.scriptRunning=true return true end
function bots:stopScript(id) stops=stops+1 b.scriptRunning=false return true end
function bots:disconnect(id) disconnects=disconnects+1 b.online=false b.inGame=false return true end
function bots:reconnect(id) reconnects=reconnects+1 b.online=true b.inGame=true b.scriptRunning=false return true end
'@
$cases=@(
 @{name='blocage relance puis reconnexion et script';check='move() now=1181 move() assert(stops==1) now=1192 move() assert(starts==1) now=1373 move() assert(disconnects==1) now=1384 move() assert(reconnects==1) now=1445 move() assert(starts==2)'},
 @{name='combat long preserve';check='b.inFight=true move() now=2000 move() assert(stops==0 and disconnects==0)'},
 @{name='progression et regeneration preservees';check='move() now=1170 b.life=110 move() now=1300 move() assert(stops==0) b.pods=20 now=1400 move() assert(stops==0)'},
 @{name='script termine repris';check='b.scriptRunning=false move() assert(starts==1)'},
 @{name='hors ligne reconnecte';check='b.online=false move() assert(reconnects==1) now=1010 move() assert(reconnects==1)'},
 @{name='moderateur suspend';check='onBotEvent(b,"moderator",{}) b.online=false move() assert(reconnects==0 and starts==0)'}
)
foreach($case in $cases){
 $lua=[MoonSharp.Interpreter.Script]::new()
 try{[void]$lua.DoString($setup+"`n"+$code+"`n"+$case.check,$null,'mineurs-watchdog')}
 catch{throw "$($case.name): $($_.Exception.InnerException.DecoratedMessage)"}
 "PASS $($case.name)"
}
