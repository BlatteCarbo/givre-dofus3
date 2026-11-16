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
if ($state.pid -ne 1256 -or $state.name -ne 'Bastionbot' -or $state.status -ne 'ingame' -or $state.inFight -or $state.scriptRunning) {
    throw 'Session not ready; no equipment request sent'
}
$inventory = Invoke-CloneTool 'get_inventory' @{ bot = 'ArcherbotLeRetour'; query = 'Korbax' }
if (-not ($inventory.items | Where-Object { $_.uid -eq 4098604 -and $_.gid -eq 14964 -and $_.position -eq 63 })) {
    throw 'Expected Korbax bag instance not found'
}
$code = @'
function move()
    local equipped = inventory:equipUid(4098604, 28)
    print("KORBAX_EQUIP confirmed=" .. tostring(equipped))
    for _, direction in ipairs({"top", "right", "bottom", "left"}) do
        print("FRIGOST_NEIGHBOUR " .. direction .. "=" .. tostring(map:neighbourId(direction)))
    end
    return false
end
'@
Invoke-CloneTool 'run_lua_script' @{ bot = 'ArcherbotLeRetour'; code = $code } | ConvertTo-Json -Compress -Depth 6
