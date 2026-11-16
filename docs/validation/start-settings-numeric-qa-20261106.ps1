param(
    [string]$ArtifactName = 'givre-settings-numeric-native-20261106',
    [string]$ReceiptName = 'settings-numeric-name-green.trx',
    [int]$ExpectedPassed = 6
)
$ErrorActionPreference = 'Stop'
$root = '%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024'
$directory = "%USERPROFILE%/AppData/Local/Temp/$ArtifactName/bin/GivreClone.App/release"
$exe = Join-Path $directory 'GivreClone.exe'
foreach ($file in @($exe, (Join-Path $directory 'Avalonia.Controls.dll'))) {
    if (-not (Test-Path $file)) { throw "Incomplete QA payload: $file" }
}
[xml]$receipt = Get-Content "$root/GivreClone.UiTests/TestResults/$ReceiptName"
if ($receipt.TestRun.ResultSummary.Counters.failed -ne '0' -or [int]$receipt.TestRun.ResultSummary.Counters.passed -ne $ExpectedPassed) { throw 'Keyboard validation receipt mismatch' }
if (@(Get-Process GivreClone -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }).Count -gt 0) { throw 'QA payload already running' }
$env:GIVRE_CLONE_RUN_NAME = 'parent-compact-20261105'
$env:GIVRE_CONTROL_PIPE = 'Givre-qa-settings-numeric-20261106'
$app = Start-Process $exe -WorkingDirectory $directory -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'QA message loop not ready' }
$app.Refresh()
if ($app.HasExited) { throw 'QA process exited after input-idle' }
Write-Output "SETTINGS_NUMERIC_QA_STARTED pid=$($app.Id) window=$($app.MainWindowHandle) nativeInventoryRequired=true"
