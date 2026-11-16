$ErrorActionPreference = 'Stop'
$root = '%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024'
$directory = '%USERPROFILE%/AppData/Local/Temp/givre-noroute-20261106/bin/GivreClone.App/release'
$exe = Join-Path $directory 'GivreClone.exe'
$oldPath = '%USERPROFILE%/AppData/Local/Temp/givre-zone-policy-20261106/bin/GivreClone.App/release/GivreClone.exe'
if (-not (Test-Path $exe)) { throw 'Verified executable missing' }
[xml]$main = Get-Content "$root/GivreClone.Tests/TestResults/noroute-full.trx"
if ($main.TestRun.ResultSummary.Counters.failed -ne '0' -or $main.TestRun.ResultSummary.Counters.passed -ne '4831') { throw 'Main validation receipt mismatch' }
$old = Get-Process -Id 29684 -ErrorAction Stop
if ([IO.Path]::GetFullPath($old.Path) -ne [IO.Path]::GetFullPath($oldPath)) { throw 'Live process identity changed' }
if (@(Get-Process GivreClone -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }).Count -gt 0) { throw 'Target executable already running' }
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'Givre-live-parent-20261105', [IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $writer = [IO.BinaryWriter]::new($pipe, [Text.Encoding]::UTF8, $true)
    $reader = [IO.BinaryReader]::new($pipe, [Text.Encoding]::UTF8, $true)
    $body = [Text.Encoding]::UTF8.GetBytes('{"tool":"list_bots","arguments":{}}')
    $writer.Write([int]$body.Length); $writer.Write($body); $writer.Flush()
    $length = $reader.ReadInt32()
    if ($length -le 0 -or $length -gt 2000000) { throw 'Invalid state frame' }
    $state = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json
    if ($null -eq $state.bots) { throw 'Unexpected roster' }
    foreach ($bot in $state.bots) {
        if ($null -eq $bot.inFight -or $null -eq $bot.scriptRunning -or $bot.inFight) { throw 'Combat active; live untouched' }
    }
    $online = @($state.bots | Where-Object { $_.status -ne 'offline' } | ForEach-Object { "$($_.accountId):$($_.name)" }) -join ','
} finally { $pipe.Dispose() }
$hash = (Get-FileHash (Join-Path $directory 'GivreClone.dll') -Algorithm SHA256).Hash
if (-not $old.CloseMainWindow()) { throw 'Graceful close refused' }
if (-not $old.WaitForExit(30000)) { throw 'Old live process still running' }
$env:GIVRE_CLONE_RUN_NAME = 'givre-app-offline'
$env:GIVRE_CONTROL_PIPE = 'Givre-live-parent-20261105'
$app = Start-Process $exe -WorkingDirectory $directory -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'New live message loop not ready' }
Write-Output "NOROUTE_LIVE_READY pid=$($app.Id) dllsha256=$hash previouslyOnline=$online"
