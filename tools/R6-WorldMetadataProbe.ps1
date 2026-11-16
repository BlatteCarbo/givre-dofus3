[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MetadataPath,
    [ValidatePattern('^[a-fA-F0-9]{64}$')]
    [string]$ExpectedSha256 = 'b04b4cee266b0a9f0aa6023b15e389e3c0dec6f054947288d8511416dbcf8a36',
    [ValidateRange(1, 1024)][int]$MaxCandidates = 256
)
$ErrorActionPreference = 'Stop'
# Offline identifier inventory only. No metadata record layout or native ABI is assumed.
$path = (Resolve-Path -LiteralPath $MetadataPath).Path
$stream = [IO.File]::Open($path, 'Open', 'Read', 'Read')
try {
    if ($stream.Length -lt 8 -or $stream.Length -gt 64MB) { throw 'METADATA_SIZE_REJECTED' }
    $bytes = New-Object byte[] ([int]$stream.Length)
    $read = 0
    while ($read -lt $bytes.Length) {
        $n = $stream.Read($bytes, $read, $bytes.Length - $read)
        if ($n -eq 0) { throw 'METADATA_SHORT_READ' }
        $read += $n
    }
} finally { $stream.Dispose() }
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
if ($hash -ne $ExpectedSha256.ToLowerInvariant()) { throw 'METADATA_HASH_MISMATCH' }
if ([BitConverter]::ToUInt32($bytes, 0) -ne [Convert]::ToUInt32('FAB11BAF', 16)) { throw 'METADATA_MAGIC_REJECTED' }
$version = [BitConverter]::ToUInt32($bytes, 4)
$text = [Text.Encoding]::ASCII.GetString($bytes)
$identifier = [regex]::new('(?<=\x00)[A-Za-z_][A-Za-z0-9_`.+<>]{1,159}(?=\x00)', 'CultureInvariant', [TimeSpan]::FromSeconds(15))
$topics = [ordered]@{
    hover = '(?i)hover|mouseover|mouseenter|pointerenter|cellover'
    map = '(?i)mapid|maprenderer|mapservice|mapmanager|mappoint|cellid|entityid'
    animation = '(?i)animator2d|setanimation|animationrun|movement|instantmove|fightentitiesservice|inputchecker'
}
$counts = [ordered]@{ hover = 0; map = 0; animation = 0 }
$rows = [Collections.Generic.List[object]]::new()
$candidatePattern = '(?:' + ($topics.Values -replace '\(\?i\)', '' -join '|') + ')'
$matched = 0
foreach ($match in $identifier.Matches($text)) {
    if ($match.Value -notmatch $candidatePattern) { continue }
    $groups = @($topics.Keys | Where-Object { $match.Value -match $topics[$_] })
    if ($groups.Count -eq 0) { continue }
    $matched++
    foreach ($group in $groups) { $counts[$group]++ }
    if ($rows.Count -lt $MaxCandidates) {
        $rows.Add([ordered]@{ identifier = $match.Value; fileOffset = ('0x{0:x}' -f $match.Index); topics = $groups })
    }
}
[ordered]@{
    schemaVersion = 1
    status = 'LITERAL_CANDIDATES_ONLY'
    metadataSha256 = $hash
    metadataBytes = $bytes.Length
    metadataHeaderVersion = $version
    scope = 'NUL-delimited ASCII identifiers; no record attribution, signatures, addresses or ABI'
    matchedOccurrences = $matched
    emittedOccurrences = $rows.Count
    truncated = ($matched -gt $rows.Count)
    topicOccurrences = $counts
    candidates = @($rows.ToArray())
    hookAuthorized = $false
    structuralProof = $false
    semanticProof = $false
} | ConvertTo-Json -Depth 6
