$ErrorActionPreference = 'Stop'
$env:GIVRE_CLONE_RUN_NAME = 'parent-compact-20261105'
$env:GIVRE_CONTROL_PIPE = 'Givre-qa-relaunch-name-20261106'
$exe = '%USERPROFILE%/AppData/Local/Temp/givre-planner-replay-20261106/bin/GivreClone.App/release/GivreClone.exe'
$existing = @(Get-Process GivreClone -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($existing.Count -gt 0) { throw 'QA executable already running; no duplicate launched' }
$app = Start-Process $exe -WorkingDirectory (Split-Path $exe) -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'QA message loop not ready' }
Write-Output "QA_STARTED pid=$($app.Id)"
