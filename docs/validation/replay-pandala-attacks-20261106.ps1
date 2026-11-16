$ErrorActionPreference = 'Stop'
$exe = '%USERPROFILE%/AppData/Local/Temp/givre-pandala-replay-20261106/bin/CombatPlannerReplay/release/CombatPlannerReplay.exe'
$log = '%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/logs/transport-2026-11-06_08-03-26.log'
foreach ($spell in 12757,12727,12726,12745,12741,12734,12750,12733,12746,12738,12748,12732) {
    & $exe $log 1073741827 2 60000 --at 09:43:43.033 --spell $spell
    if ($LASTEXITCODE -ne 0) { throw "Replay failed for spell $spell" }
}
