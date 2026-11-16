param(
    [Parameter(Mandatory = $true)][int]$ExpectedAppPid,
    [string]$VerifiedBuildDirectory = '%USERPROFILE%/AppData/Local/Temp/givre-companion-chunks-20261104/bin/GivreClone.App/release'
)
$ErrorActionPreference = 'Stop'
$dir = $VerifiedBuildDirectory
if (-not (Test-Path "$dir/GivreClone.exe")) { throw 'Verified build missing' }
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'GivreWork-97A1F223CE35E55D', [IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $writer = [IO.BinaryWriter]::new($pipe, [Text.Encoding]::UTF8, $true)
    $reader = [IO.BinaryReader]::new($pipe, [Text.Encoding]::UTF8, $true)
    $body = [Text.Encoding]::UTF8.GetBytes('{"tool":"get_bot_state","arguments":{"bot":"ArcherbotLeRetour"}}')
    $writer.Write([int]$body.Length)
    $writer.Write($body)
    $writer.Flush()
    $length = $reader.ReadInt32()
    if ($length -le 0 -or $length -gt 16000000) { throw 'Invalid IPC response length' }
    $state = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json
    if ($state.name -ne 'Bastionbot' -or $state.inFight -or $state.scriptRunning -or $null -eq $state.inFight) {
        throw 'Session not identified or combat/script still active; app left untouched'
    }
} finally { $pipe.Dispose() }
$old = Get-Process -Id $ExpectedAppPid -ErrorAction Stop
if ($old.ProcessName -ne 'GivreClone') { throw 'PID reused; no process closed' }
if (-not $old.CloseMainWindow()) { throw 'Graceful close refused' }
if (-not $old.WaitForExit(30000)) { throw 'Previous app still running' }
if (Get-Process GivreClone -ErrorAction SilentlyContinue) { throw 'Another app instance is running' }
$env:GIVRE_CLONE_RUN_NAME = 'givre-app-offline'
$env:GIVRE_CONTROL_PIPE = 'GivreWork-97A1F223CE35E55D'
$app = Start-Process "$dir/GivreClone.exe" -WorkingDirectory $dir -PassThru
if (-not $app.WaitForInputIdle(45000)) { throw 'Application message loop not ready' }
Write-Output "COMPANION_TIMING_APP_READY pid=$($app.Id)"
