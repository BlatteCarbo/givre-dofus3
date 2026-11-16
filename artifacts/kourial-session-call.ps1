param([string]$Tool,[string]$Arguments='{}',[string]$ArgumentsFile='')
$ErrorActionPreference='Stop'
if($ArgumentsFile){$Arguments=[IO.File]::ReadAllText($ArgumentsFile)}
$sha=[Security.Cryptography.SHA256]::Create()
$hash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([Environment]::UserDomainName+'\'+[Environment]::UserName))).Replace('-','').Substring(0,16)
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.','GivreWork-Kourial-Eni-20261026',[IO.Pipes.PipeDirection]::InOut)
try {
$pipe.Connect(3000)
$body=[Text.Encoding]::UTF8.GetBytes((@{tool=$Tool;arguments=($Arguments|ConvertFrom-Json)}|ConvertTo-Json -Depth 40 -Compress))
$header=[BitConverter]::GetBytes([int]$body.Length)
$pipe.Write($header,0,4);$pipe.Write($body,0,$body.Length);$pipe.Flush()
$header=New-Object byte[] 4
$offset=0
while($offset -lt 4){$n=$pipe.Read($header,$offset,4-$offset);if($n -eq 0){throw 'Closed pipe'};$offset+=$n}
$length=[BitConverter]::ToInt32($header,0)
if($length -lt 1 -or $length -gt 2000000){throw 'Invalid length'}
$reply=New-Object byte[] $length
$offset=0
while($offset -lt $length){$n=$pipe.Read($reply,$offset,$length-$offset);if($n -eq 0){throw 'Closed pipe'};$offset+=$n}
[Text.Encoding]::UTF8.GetString($reply)
} finally {$pipe.Dispose()}
