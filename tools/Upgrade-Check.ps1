# Offline Dofus version + protocol impact audit. Does not launch Dofus or contact any server.
[CmdletBinding()]
param(
    [string]$GameRoot = "$env:LOCALAPPDATA\Ankama\Games\dofus",
    [string]$BundleBuildPath,
    [string]$OutputDir,
    [switch]$RunProtoMap,
    [string]$BaseVersion,
    [string]$TargetVersion,
    [string]$BaseProto,
    [string]$TargetProto,
    [string]$DriRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BundleBuildPath) { $BundleBuildPath = Join-Path $repoRoot 'data\bundle-build.json' }
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'artifacts\R6-upgrade-check' }
if (-not $DriRoot) { $DriRoot = Join-Path $repoRoot '..\DofusReverseIndexer' }
$output = [IO.Path]::GetFullPath($OutputDir)
if (-not $output.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Le dossier de rapport doit rester dans GivreWork.'
}
New-Item -Path $output -ItemType Directory -Force | Out-Null

$declared = $null
if (Test-Path -LiteralPath $BundleBuildPath -PathType Leaf) {
    $declared = (Get-Content -LiteralPath $BundleBuildPath -Raw | ConvertFrom-Json).build
}
$marker = Join-Path $GameRoot 'Dofus_Data\StreamingAssets\version'
$installed = $null
$markerSha256 = $null
if (Test-Path -LiteralPath $marker -PathType Leaf) {
    $rawMarker = Get-Content -LiteralPath $marker -Raw
    $match = [regex]::Match($rawMarker, '(?m)^Version=(\d+\.\d+\.\d+\.\d+)\s*$')
    if ($match.Success) {
        $installed = $match.Groups[1].Value
        $markerSha256 = (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$status = if (-not $installed) { 'MISSING_LOCAL_VERSION' }
    elseif (-not $declared) { 'MISSING_BUNDLE_DECLARATION' }
    elseif ($installed -ne $declared) { 'VERSION_MISMATCH' }
    else { 'VERSION_MATCH_METADATA_ONLY' }
$summary = [ordered]@{
    status = $status
    installedVersion = $installed
    declaredBundleVersion = $declared
    versionMarkerSha256 = $markerSha256
    protocolCertified = $false
    gameplayCertified = $false
    note = 'Version is local metadata only; mapping provenance, wire IDs and server ACK still require independent proof.'
}

if ($RunProtoMap) {
    foreach ($value in @($BaseVersion, $TargetVersion, $BaseProto, $TargetProto)) {
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw 'Avec -RunProtoMap, renseigner BaseVersion, TargetVersion, BaseProto et TargetProto.'
        }
    }
    if ($installed -ne $TargetVersion) {
        throw 'Le TargetVersion doit correspondre exactement au marqueur local du client.'
    }
    if (-not (Test-Path -LiteralPath $BaseProto -PathType Leaf) -or
        -not (Test-Path -LiteralPath $TargetProto -PathType Leaf)) {
        throw 'Fichiers .proto absents : aucun rapprochement spéculatif.'
    }
    $driSource = Join-Path $DriRoot 'src'
    if (-not (Test-Path -LiteralPath (Join-Path $driSource 'dri\__main__.py') -PathType Leaf)) {
        throw 'DofusReverseIndexer/src/dri absent.'
    }
    $mappingPath = Join-Path $output 'proto-map.review.json'
    $priorPythonPath = $env:PYTHONPATH
    $priorNoByteCode = $env:PYTHONDONTWRITEBYTECODE
    try {
        $env:PYTHONPATH = $driSource
        $env:PYTHONDONTWRITEBYTECODE = '1'
        & python -B -m dri proto-map --base "$BaseVersion=$BaseProto" --target "$TargetVersion=$TargetProto" --output $mappingPath --summary
        if ($LASTEXITCODE -ne 0) { throw "dri proto-map a échoué (exit=$LASTEXITCODE)." }
    }
    finally {
        $env:PYTHONPATH = $priorPythonPath
        $env:PYTHONDONTWRITEBYTECODE = $priorNoByteCode
    }
    $mapping = Get-Content -LiteralPath $mappingPath -Raw | ConvertFrom-Json
    $sourceHash = (Get-FileHash -LiteralPath $BaseProto -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $TargetProto -Algorithm SHA256).Hash
    $summary.identicalProtoInputs = $sourceHash -eq $targetHash
    $summary.crossVersionEvidence = $false
    if ($summary.identicalProtoInputs) {
        $summary.protoInputWarning = 'The two version-labelled files are byte-identical. This comparison cannot prove compatibility across versions.'
    }
    $pair = @($mapping.pairs | Where-Object {
        $_.source_version -eq $BaseVersion -and $_.target_version -eq $TargetVersion
    })
    if ($pair.Count -ne 1) { throw 'Paire de versions absente ou ambiguë dans le rapport DRI.' }
    $manifestPath = Join-Path $repoRoot 'GivreClone.Protocol\RequiredMapping.cs'
    $requiredSource = Get-Content -LiteralPath $manifestPath -Raw
    $required = [regex]::Matches($requiredSource, '\("([a-zA-Z_][a-zA-Z_0-9]*)",\s*"[^"]+"\)') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $rows = foreach ($message in $required) {
        $matches = @($pair[0].mappings | Where-Object {
            $_.source -eq $message -or $_.source.EndsWith('.' + $message)
        })
        if ($matches.Count -ne 1) {
            [pscustomobject]@{Message=$message; Target=''; Status='UNRESOLVED_OR_AMBIGUOUS'; ChangedTags=''; Evidence='none'}
            continue
        }
        $mappingEntry = $matches[0]
        $changed = @($mappingEntry.fields | Where-Object { $_.source_tag -ne $_.target_tag })
        $unresolved = @($mappingEntry.unresolved_source_tags.PSObject.Properties.Name)
        $statusText = if ($changed.Count -gt 0 -or $unresolved.Count -gt 0) { 'REVIEW_TAGS' } else { 'STRUCTURAL_CANDIDATE' }
        [pscustomobject]@{
            Message = $message
            Target = $mappingEntry.target
            Status = $statusText
            ChangedTags = (($changed | ForEach-Object { "$($_.source_tag)>$($_.target_tag)" }) -join ';')
            Evidence = $mappingEntry.basis
        }
    }
    $csvPath = Join-Path $output 'used-messages-review.csv'
    $rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
    $summary.protoMapReport = $mappingPath
    $summary.usedMessagesCsv = $csvPath
    $summary.reviewRequired = @($rows | Where-Object Status -ne 'STRUCTURAL_CANDIDATE').Count
}
$summaryPath = Join-Path $output 'upgrade-summary.json'
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Output "Upgrade report: $summaryPath"
Write-Output "Decision: $status (mapping/gameplay not certified)"
