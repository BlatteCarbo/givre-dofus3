$ErrorActionPreference = 'Stop'
$env:GIVRE_CLONE_RUN_NAME = 'parent-expedition-layout-20261106'
$env:GIVRE_CONTROL_PIPE = 'Givre-qa-expedition-layout-20261106'
$exe = '%USERPROFILE%/AppData/Local/Temp/givre-expedition-layout-20261106/bin/GivreClone.App/release/GivreClone.exe'
if (-not (Test-Path $exe)) { throw 'Validated QA executable missing' }
if (-not (Test-Path (Join-Path (Split-Path $exe) 'Avalonia.Controls.dll'))) { throw 'Incomplete QA payload: Avalonia.Controls.dll missing; no process launched' }
$existing = @(Get-Process GivreClone -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($existing.Count -gt 0) { throw 'QA executable already running; no duplicate launched' }
$app = Start-Process $exe -WorkingDirectory (Split-Path $exe) -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'QA message loop not ready' }
Write-Output "EXPEDITION_QA_READY pid=$($app.Id) profile=parent-expedition-layout-20261106"
