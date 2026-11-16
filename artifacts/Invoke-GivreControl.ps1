param([Parameter(Mandatory)][string]$Tool, [string]$ArgumentsJson = '{}', [string]$CodeFile, [string]$PipeName)
$argsObject = $ArgumentsJson | ConvertFrom-Json -AsHashtable
if ($CodeFile) { $argsObject['code'] = Get-Content -LiteralPath $CodeFile -Raw }
$key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([Environment]::UserDomainName+'\'+[Environment]::UserName))).Substring(0,16)
if (!$PipeName) { $PipeName = 'GivreWork-'+$key }
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $PipeName,[IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(3000)
    $body = [Text.Encoding]::UTF8.GetBytes((@{tool=$Tool;arguments=$argsObject} | ConvertTo-Json -Depth 30 -Compress))
    $pipe.Write([BitConverter]::GetBytes([int]$body.Length)); $pipe.Write($body); $pipe.Flush()
    $head = [byte[]]::new(4); $pipe.ReadExactly($head)
    $length = [BitConverter]::ToInt32($head)
    if ($length -lt 1 -or $length -gt 2000000) { throw 'Invalid control response size' }
    $response = [byte[]]::new($length); $pipe.ReadExactly($response)
    [Text.Encoding]::UTF8.GetString($response)
} finally { $pipe.Dispose() }
