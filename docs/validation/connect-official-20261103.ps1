$ErrorActionPreference = 'Stop'
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'GivreWork-97A1F223CE35E55D', [IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $writer = [IO.BinaryWriter]::new($pipe, [Text.Encoding]::UTF8, $true)
    $reader = [IO.BinaryReader]::new($pipe, [Text.Encoding]::UTF8, $true)
    $body = [Text.Encoding]::UTF8.GetBytes('{"tool":"connect_bot","arguments":{"bot":"ArcherbotLeRetour","mode":"mitm"}}')
    $writer.Write([int]$body.Length)
    $writer.Write($body)
    $writer.Flush()
    $length = $reader.ReadInt32()
    if ($length -le 0 -or $length -gt 16000000) { throw 'Invalid IPC response length' }
    Write-Output ([Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)))
} finally { $pipe.Dispose() }
