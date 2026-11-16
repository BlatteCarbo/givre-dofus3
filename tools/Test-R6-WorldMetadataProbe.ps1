$ErrorActionPreference = 'Stop'
$probe = Join-Path $PSScriptRoot 'R6-WorldMetadataProbe.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('R6-world-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$passed = 0
function Assert-True([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw "FAIL: $Label" }
    $script:passed++
}
try {
    # Given adjacent identifiers, including two occurrences of one name.
    $file = Join-Path $temp 'metadata.dat'
    [byte[]]$bytes = @(0xAF,0x1B,0xB1,0xFA,31,0,0,0) + [Text.Encoding]::ASCII.GetBytes("`0MapId`0OnPointerEnter`0Animator2D`0MapId`0Unrelated`0")
    [IO.File]::WriteAllBytes($file, $bytes)
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    # When scanning all candidates, then preserve adjacent names and exact offsets.
    $report = (& $probe -MetadataPath $file -ExpectedSha256 $hash | ConvertFrom-Json)
    Assert-True ($report.matchedOccurrences -eq 4 -and $report.candidates[0].fileOffset -eq '0x9') 'adjacency and offset'
    Assert-True ($report.candidates[1].identifier -eq 'OnPointerEnter' -and $report.topicOccurrences.hover -eq 1) 'hover topic'
    Assert-True (-not $report.structuralProof -and -not $report.semanticProof -and -not $report.hookAuthorized) 'no promotion'
    # When capped, then count all occurrences and explicitly report truncation.
    $limited = (& $probe -MetadataPath $file -ExpectedSha256 $hash -MaxCandidates 1 | ConvertFrom-Json)
    Assert-True ($limited.truncated -and $limited.matchedOccurrences -eq 4 -and $limited.emittedOccurrences -eq 1) 'cap is honest'
    # When the fingerprint differs, then reject before emitting a candidate report.
    $failure = ''
    try { & $probe -MetadataPath $file -ExpectedSha256 ('0' * 64) | Out-Null } catch { $failure = $_.Exception.Message }
    Assert-True ($failure -eq 'METADATA_HASH_MISMATCH') 'wrong hash rejected'
    # Given a correctly hashed non-metadata file, then reject its header.
    $bytes[0] = 0
    [IO.File]::WriteAllBytes($file, $bytes)
    $failure = ''
    try { & $probe -MetadataPath $file -ExpectedSha256 (Get-FileHash $file).Hash | Out-Null } catch { $failure = $_.Exception.Message }
    Assert-True ($failure -eq 'METADATA_MAGIC_REJECTED') 'wrong magic rejected'
    Write-Output "PASS $passed/6 offline world metadata checks"
} finally {
    # Only delete the one explicitly created fixture file and empty directory.
    if (Test-Path -LiteralPath (Join-Path $temp 'metadata.dat')) { Remove-Item -LiteralPath (Join-Path $temp 'metadata.dat') }
    [IO.Directory]::Delete($temp)
}
