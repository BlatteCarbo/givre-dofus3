param([Parameter(Mandatory = $true)][int]$ExpectedClientPid, [ValidateRange(0,24)][int]$Completed = 0)
$ErrorActionPreference = 'Stop'
function Invoke-CloneTool([string]$Tool, [hashtable]$Arguments) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'GivreWork-97A1F223CE35E55D', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(3000)
        $writer = [IO.BinaryWriter]::new($pipe, [Text.Encoding]::UTF8, $true)
        $reader = [IO.BinaryReader]::new($pipe, [Text.Encoding]::UTF8, $true)
        $body = [Text.Encoding]::UTF8.GetBytes((@{ tool = $Tool; arguments = $Arguments } | ConvertTo-Json -Compress -Depth 5))
        $writer.Write([int]$body.Length)
        $writer.Write($body)
        $writer.Flush()
        $length = $reader.ReadInt32()
        if ($length -le 0 -or $length -gt 16000000) { throw 'Invalid IPC response length' }
        return [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json
    } finally { $pipe.Dispose() }
}
$state = Invoke-CloneTool 'get_bot_state' @{ bot = 'ArcherbotLeRetour' }
if ($state.pid -ne $ExpectedClientPid -or $state.name -ne 'Bastionbot' -or $state.status -ne 'ingame' -or $state.inFight -or $state.scriptRunning) {
    throw 'Session changed, not ready, or already active; no campaign started'
}
if ($state.mapId -notin @(54169896, 54169897, 54169384)) { throw 'Outside observed Frigost walking route; no campaign started' }
$inventory = Invoke-CloneTool 'get_inventory' @{ bot = 'ArcherbotLeRetour'; query = 'Korbax' }
if (-not ($inventory.items | Where-Object { $_.gid -eq 14964 -and $_.position -eq 28 })) { throw 'Korbax is not equipped; no campaign started' }
$code = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'frigost-25-combats-20261103.lua'))
$code = "FRIGOST25_COMPLETED=$Completed`nFRIGOST25_WINS=$Completed`nFRIGOST25_LOSSES=0`n" + $code
$result = Invoke-CloneTool 'run_lua_script' @{ bot = 'ArcherbotLeRetour'; code = $code }
$result | ConvertTo-Json -Compress -Depth 6
