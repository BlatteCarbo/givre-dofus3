# Read-only evidence extractor for a single owned native host test log.
# Outputs booleans/counts; never copies arbitrary game/UI or account text.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('Pid')][ValidateRange(1, 2147483647)][int]$GamePid,
    [string]$LogPath,
    [string]$ReportPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $LogPath) {
    $cloneData = Join-Path $env:LOCALAPPDATA 'GivreReplica\TestRuns\givre-app-offline\userdata'
    $LogPath = Join-Path $cloneData "NativeTurbo\$GamePid\NativeMods\native-host.log"
}
if (-not $ReportPath) {
    $ReportPath = Join-Path $repo "artifacts\R6-combat-mods\pid-$GamePid-diagnostics.json"
}
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $report.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Le rapport doit rester dans le dépôt GivreWork.'
}
$counts = [ordered]@{
    skipperReady = 0
    fightOn = 0
    fightOff = 0
    acceleratedSummary = 0
    tacticalReady = 0
    tacticalEnabled = 0
    tacticalDisabled = 0
    nativeMissingClassOrSignature = 0
    nativeLoadFailure = 0
}
$patterns = @{
    skipperReady = 'Combat Animation Skipper ready:'
    fightOn = 'Combat Animation Skipper: combat transition ON;'
    fightOff = 'Combat Animation Skipper: combat transition OFF;'
    acceleratedSummary = 'Combat Animation Skipper: [0-9]+ non-looping Animator2D animation\(s\) continuously fast-forwarded'
    tacticalReady = 'Ready\. Press F8 to toggle native tactical mode'
    tacticalEnabled = 'Native tactical mode enabled on map [0-9]+'
    tacticalDisabled = 'Native tactical mode disabled\.'
    nativeMissingClassOrSignature = 'Combat Animation Skipper: Animator2D or InputChecker is unavailable|Native Tactical resolver expected one structural TacticalDebugCell match|Native Tactical could not resolve the cell methods'
    nativeLoadFailure = 'failed to create .* hook|failed to enable .* hook|creation du hook .* impossible|Runtime rejected the negotiated host ABI'
}
$exists = Test-Path -LiteralPath $LogPath -PathType Leaf
$processed = 0
$truncated = $false
if ($exists) {
    foreach ($line in [IO.File]::ReadLines([IO.Path]::GetFullPath($LogPath))) {
        $processed++
        if ($processed -gt 200000) { $truncated = $true; break }
        foreach ($key in $patterns.Keys) {
            if ([regex]::IsMatch($line, $patterns[$key], [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $counts[$key]++
            }
        }
    }
}
$verdict = if (-not $exists) { 'MISSING_NATIVE_LOG' }
    elseif ($truncated) { 'TRUNCATED_LOG_REVIEW_REQUIRED' }
    elseif ($counts.nativeLoadFailure -gt 0 -or $counts.nativeMissingClassOrSignature -gt 0) { 'STRUCTURAL_ERROR' }
    elseif ($counts.skipperReady -gt 0 -and $counts.fightOn -gt 0 -and $counts.fightOff -gt 0 -and $counts.acceleratedSummary -gt 0 -and $counts.tacticalReady -gt 0 -and $counts.tacticalEnabled -gt 0) { 'RUNTIME_LOG_EVIDENCE_ONLY' }
    else { 'INCOMPLETE_RUNTIME_EVIDENCE' }
$payload = [ordered]@{
    pid = $GamePid
    verdict = $verdict
    observations = $counts
    processedLines = $processed
    truncated = $truncated
    note = 'The native log is a local witness only. STATUS/LOAD by the host and visual functionality require separate verification. No gameplay ACK implied.'
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $report) | Out-Null
$payload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Output "R6 combat log: $verdict for pid $GamePid; report $report"
