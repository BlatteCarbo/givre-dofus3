# Deterministic, synthetic-only test for the R6 native log classifier.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repo 'artifacts\R6-combat-fixtures'
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
$log = Join-Path $fixture 'native-host-fixture.log'
$out = Join-Path $fixture 'verdict.json'
$script = Join-Path $PSScriptRoot 'Inspect-R6-CombatMods.ps1'
@(
 'Combat Animation Skipper ready: dual fight detection and Animator2D hooks installed.'
 'Combat Animation Skipper: combat transition ON; non-looping action animations accelerated.'
 'Combat Animation Skipper: 6 non-looping Animator2D animation(s) continuously fast-forwarded (total 6).'
 'Combat Animation Skipper: combat transition OFF; normal animation timing restored.'
 'Ready. Press F8 to toggle native tactical mode (Rust SDK, native sprites, ABI v8).'
 'Native tactical mode enabled on map 111 (560 native tacticalCell sprites).'
 'account=SECRET_SHOULD_NOT_APPEAR_IN_REPORT'
) | Set-Content -LiteralPath $log -Encoding UTF8
& $script -Pid 9999 -LogPath $log -ReportPath $out | Out-Null
$read = Get-Content -LiteralPath $out -Raw
$parsed = $read | ConvertFrom-Json
if ($parsed.verdict -ne 'RUNTIME_LOG_EVIDENCE_ONLY' -or $read.Contains('SECRET_SHOULD_NOT_APPEAR')) {
    throw 'Positive evidence case incorrect or private log contents leaked.'
}
'Combat Animation Skipper: Animator2D or InputChecker is unavailable.' | Set-Content -LiteralPath $log -Encoding UTF8
& $script -Pid 9999 -LogPath $log -ReportPath $out | Out-Null
$parsed = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
if ($parsed.verdict -ne 'STRUCTURAL_ERROR') { throw 'Missing structural class was not classified as failure.' }
& $script -Pid 9999 -LogPath (Join-Path $fixture 'missing.log') -ReportPath $out | Out-Null
$parsed = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
if ($parsed.verdict -ne 'MISSING_NATIVE_LOG') { throw 'Absent log must be classified as missing.' }
Write-Output 'R6 combat log classifier: 3/3 PASS'
