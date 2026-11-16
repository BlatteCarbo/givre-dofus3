$ErrorActionPreference = 'Stop'
$env:GIVRE_CLONE_RUN_NAME = 'parent-compact-20261105'
$env:GIVRE_CONTROL_PIPE = 'Givre-qa-threshold-accessibility-20261106'
$exe = '%USERPROFILE%/AppData/Local/Temp/givre-threshold-native-20261106/bin/GivreClone.App/release/GivreClone.exe'
if (-not (Test-Path $exe)) { throw 'UI-validated executable missing' }
[xml]$receipt = Get-Content '%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.UiTests/TestResults/threshold-name-full.trx'
if ($receipt.TestRun.ResultSummary.Counters.failed -ne '0' -or $receipt.TestRun.ResultSummary.Counters.passed -ne '219') { throw 'UI validation receipt mismatch' }
$existing = @(Get-Process GivreClone -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($existing.Count -gt 0) { throw 'QA executable already running; no duplicate launched' }
$app = Start-Process $exe -WorkingDirectory (Split-Path $exe) -PassThru -RedirectStandardError '%USERPROFILE%/AppData/Local/Temp/givre-threshold-qa-stderr.log' -RedirectStandardOutput '%USERPROFILE%/AppData/Local/Temp/givre-threshold-qa-stdout.log'
if (-not $app.WaitForInputIdle(45000)) { throw 'QA message loop not ready' }
Write-Output "THRESHOLD_QA_READY pid=$($app.Id) profile=parent-compact-20261105"
$app.WaitForExit()
if ($null -eq $app.ExitCode) { throw 'QA process exited; exit code unavailable from launcher handle' }
Write-Output "THRESHOLD_QA_EXIT code=$($app.ExitCode)"
if ($app.ExitCode -ne 0) { throw "QA process failed with code $($app.ExitCode); inspect redirected stderr" }
