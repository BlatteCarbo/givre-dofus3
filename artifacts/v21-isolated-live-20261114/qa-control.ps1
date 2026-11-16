Set-StrictMode -Version Latest

# This helper addresses only the isolated QA run named below. It deliberately
# does not use the default/production pipe and never prints request payloads.
function Invoke-V21QaControl {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [hashtable]$Arguments = @{}
    )

    $runName = 'v21-uid-live-20261114'
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $prefix = [BitConverter]::ToString($sha.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes([Environment]::UserDomainName + '\' + [Environment]::UserName))).Replace('-', '').Substring(0, 16)
        $suffix = [BitConverter]::ToString($sha.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($runName))).Replace('-', '').Substring(0, 16)
    }
    finally { $sha.Dispose() }
    $pipeName = 'GivreWork-' + $prefix + '-qa-' + $suffix

    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
        '.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(4000)
        $request = @{ tool = $Tool; arguments = $Arguments } | ConvertTo-Json -Compress -Depth 30
        $body = [Text.Encoding]::UTF8.GetBytes($request)
        if ($body.Length -lt 1 -or $body.Length -gt 2000000) { throw 'QA request exceeds wire bound' }
        $header = [BitConverter]::GetBytes([int]$body.Length)
        $pipe.Write($header, 0, 4)
        $pipe.Write($body, 0, $body.Length)
        $pipe.Flush()

        $responseHeader = New-Object byte[] 4
        $offset = 0
        while ($offset -lt 4) {
            $n = $pipe.Read($responseHeader, $offset, 4 - $offset)
            if ($n -le 0) { throw 'QA pipe closed before response header' }
            $offset += $n
        }
        $size = [BitConverter]::ToInt32($responseHeader, 0)
        if ($size -lt 1 -or $size -gt 2000000) { throw 'Invalid QA response bound' }
        $response = New-Object byte[] $size
        $offset = 0
        while ($offset -lt $size) {
            $n = $pipe.Read($response, $offset, $size - $offset)
            if ($n -le 0) { throw 'QA pipe closed before complete response' }
            $offset += $n
        }
        $json = [Text.Encoding]::UTF8.GetString($response)
        $root = ConvertFrom-Json -InputObject $json
        if ($root.PSObject.Properties.Match('isError').Count -gt 0 -and
            $root.isError -eq $true) {
            throw "QA rejected $Tool (inspect private UI for details)"
        }
        return $root
    }
    finally { $pipe.Dispose() }
}
