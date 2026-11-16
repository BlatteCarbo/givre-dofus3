$ErrorActionPreference = 'Stop'
$cursor = 0L
for ($subscription = 0; $subscription -lt 180; $subscription++) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'GivreWork-97A1F223CE35E55D', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(3000)
        $writer = [IO.BinaryWriter]::new($pipe, [Text.Encoding]::UTF8, $true)
        $reader = [IO.BinaryReader]::new($pipe, [Text.Encoding]::UTF8, $true)
        $request = @{ tool = 'wait_console'; arguments = @{ bot = 'ArcherbotLeRetour'; since = $cursor; pattern = 'FRIGOST25_RESULT|FRIGOST25_STOP|FRIGOST25_PAUSE|FRIGOST25_DONE'; timeout = 60 } }
        $body = [Text.Encoding]::UTF8.GetBytes(($request | ConvertTo-Json -Compress -Depth 5))
        $writer.Write([int]$body.Length)
        $writer.Write($body)
        $writer.Flush()
        $length = $reader.ReadInt32()
        if ($length -le 0 -or $length -gt 16000000) { throw 'Invalid console response length' }
        $result = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json
        foreach ($line in $result.lines) {
            Write-Output $line.message
            if ($line.message -match 'FRIGOST25_(STOP|PAUSE|DONE)') { return }
        }
        if ($null -ne $result.cursor) { $cursor = [long]$result.cursor }
    } finally { $pipe.Dispose() }
}
Write-Output 'FRIGOST25_WATCH_ENDED subscription_limit'
