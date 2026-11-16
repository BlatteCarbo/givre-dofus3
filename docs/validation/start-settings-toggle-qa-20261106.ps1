param([string]$ArtifactName = 'givre-toggle-native-20261106')
$ErrorActionPreference = 'Stop'
$directory = "%USERPROFILE%/AppData/Local/Temp/$ArtifactName/bin/GivreClone.App/release"
$exe = Join-Path $directory 'GivreClone.exe'
foreach ($file in @($exe, (Join-Path $directory 'Avalonia.Controls.dll'))) {
    if (-not (Test-Path $file)) { throw "Incomplete QA payload: $file" }
}
if (@(Get-Process GivreClone -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }).Count -gt 0) { throw 'QA payload already running' }
$env:GIVRE_CLONE_RUN_NAME = 'parent-compact-20261105'
$env:GIVRE_CONTROL_PIPE = "Givre-qa-$ArtifactName"
$app = Start-Process $exe -WorkingDirectory $directory -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'QA message loop not ready' }
$app.Refresh()
if ($app.HasExited) { throw 'QA process exited' }
Write-Output "TOGGLE_QA_STARTED pid=$($app.Id) window=$($app.MainWindowHandle)"
