$ErrorActionPreference = 'Stop'
$oldPath = '%USERPROFILE%/AppData/Local/Temp/givre-origin-live-20261106/bin/GivreClone.App/release/GivreClone.exe'
$directory = '%USERPROFILE%/AppData/Local/Temp/givre-caster-side-effect-20261106/bin/GivreClone.App/release'
$exe = Join-Path $directory 'GivreClone.exe'
if (-not (Test-Path $exe)) { throw 'Verified executable missing' }
[xml]$trx = Get-Content '%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/TestResults/companion-owner-restored-full.trx'
if ($trx.TestRun.ResultSummary.Counters.failed -ne '0' -or $trx.TestRun.ResultSummary.Counters.passed -ne '4809') {
    throw 'Expected full validation receipt missing'
}
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'Givre-live-parent-20261105', [IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $writer = [IO.BinaryWriter]::new($pipe, [Text.Encoding]::UTF8, $true)
    $reader = [IO.BinaryReader]::new($pipe, [Text.Encoding]::UTF8, $true)
    $body = [Text.Encoding]::UTF8.GetBytes('{"tool":"list_bots","arguments":{}}')
    $writer.Write([int]$body.Length)
    $writer.Write($body)
    $writer.Flush()
    $length = $reader.ReadInt32()
    if ($length -le 0 -or $length -gt 2000000) { throw 'Invalid state frame' }
    $state = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json
    if ($null -eq $state.bots -or @($state.bots).Count -ne 5) { throw 'Unexpected live account roster' }
    foreach ($bot in $state.bots) {
        if ($null -eq $bot.inFight -or $null -eq $bot.scriptRunning -or $bot.inFight -or $bot.scriptRunning) {
            throw 'Combat or script still active; live app untouched'
        }
    }
} finally { $pipe.Dispose() }
$old = Get-Process -Id 36248 -ErrorAction Stop
if ([IO.Path]::GetFullPath($old.Path) -ne [IO.Path]::GetFullPath($oldPath)) { throw 'Live PID identity changed' }
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
if (-not $old.CloseMainWindow()) { throw 'Graceful close refused' }
if (-not $old.WaitForExit(30000)) { throw 'Old live process still running' }
$env:GIVRE_CLONE_RUN_NAME = 'givre-app-offline'
$env:GIVRE_CONTROL_PIPE = 'Givre-live-parent-20261105'
$app = Start-Process $exe -WorkingDirectory $directory -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'New live message loop not ready' }
Write-Output "VERIFIED_LIVE_READY pid=$($app.Id) sha256=$hash profile=givre-app-offline pipe=Givre-live-parent-20261105"
