param([Parameter(Mandatory)][string]$Tool, [string]$ArgumentsJson='{}')
$ErrorActionPreference='Stop'
$identity=[Environment]::UserDomainName+'\'+[Environment]::UserName
$digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity)))
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.',('GivreWork-'+$digest.Substring(0,16)),[IO.Pipes.PipeDirection]::InOut)
try {
 $pipe.Connect(3000)
 $arguments=$ArgumentsJson | ConvertFrom-Json -AsHashtable
 $payload=[Text.Encoding]::UTF8.GetBytes((@{tool=$Tool;arguments=$arguments} | ConvertTo-Json -Depth 50 -Compress))
 $pipe.Write([BitConverter]::GetBytes([int]$payload.Length)); $pipe.Write($payload); $pipe.Flush()
 $header=[byte[]]::new(4); $pipe.ReadExactly($header)
 $length=[BitConverter]::ToInt32($header)
 if($length -lt 1 -or $length -gt 2000000){throw 'Invalid control frame'}
 $body=[byte[]]::new($length); $pipe.ReadExactly($body)
 [Text.Encoding]::UTF8.GetString($body)
} finally { $pipe.Dispose() }
