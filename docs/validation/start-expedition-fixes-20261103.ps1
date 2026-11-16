$ErrorActionPreference = 'Stop'
$dir = '%USERPROFILE%/AppData/Local/Temp/givre-zero-cast-repro-20261103/bin/GivreClone.App/release'
if (-not (Test-Path "$dir/GivreClone.exe")) { throw 'Verified build missing' }
$old = Get-Process -Id 15432 -ErrorAction SilentlyContinue
if ($old) {
    if ($old.ProcessName -ne 'GivreClone') { throw 'PID reused; no process closed' }
    if (-not $old.CloseMainWindow()) { throw 'Graceful close refused' }
    if (-not $old.WaitForExit(30000)) { throw 'Previous app still running' }
}
if (Get-Process GivreClone -ErrorAction SilentlyContinue) { throw 'Another app instance is running' }
$env:GIVRE_CLONE_RUN_NAME = 'givre-app-offline'
$env:GIVRE_CONTROL_PIPE = 'GivreWork-97A1F223CE35E55D'
$app = Start-Process "$dir/GivreClone.exe" -WorkingDirectory $dir -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'Application message loop not ready' }
Write-Output "EXPEDITION_FIXES_APP_READY pid=$($app.Id)"
