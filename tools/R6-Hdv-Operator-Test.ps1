# Safe local UID extraction only. Never sends orders and never opens Dofus.
[CmdletBinding()]
param(
    [string]$LogDirectory,
    [uint32]$PidFilter = 0,
    [string]$OutputFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $LogDirectory) {
    $LogDirectory = Join-Path $env:LOCALAPPDATA 'GivreReplica\TestRuns\givre-app-offline\userdata\logs'
}
if (-not $OutputFile) {
    $OutputFile = Join-Path $root 'artifacts\R6-hdv-operator\uid-only-review.csv'
}
$target = [IO.Path]::GetFullPath($OutputFile)
if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Le rapport ne peut être écrit que sous GivreWork.'
}
if (-not (Test-Path -LiteralPath $LogDirectory -PathType Container)) {
    throw 'Dossier transport absent : aucune extraction effectuée.'
}
$files = @(Get-ChildItem -LiteralPath $LogDirectory -Filter 'transport-*.log' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 20)
if ($files.Count -eq 0) { throw 'Aucun transport-*.log disponible.' }

# Whitelist strictly numeric UID diagnostic lines only; do not copy arbitrary
# log text (which may contain account, prices, payloads or tokens).
$recognized = @(
    'vente demande uid-sac=[0-9]+ quantite=[0-9]+ inject=(?:OK|ECHEC) \(aucun ACK impliqué\)\.',
    'annonce reçue uid-lot=(?:[0-9]+|\?) \(candidat, non corrélé\)\.',
    'vente uid-sac=[0-9]+ disposition=(?:Confirmed|NeedsReview|Refused) sell_id=(?:[0-9]+|\?) event=[A-Za-z]+\.',
    'reprice ancien sell_id=[0-9]+ uid-lot-annonce=(?:[0-9]+|\?) \(réponse non corrélée à la demande\)\.',
    'retrait demande sell_id=[0-9]+ quantite=[0-9]+ inject=(?:OK|ECHEC) lien-sac-verifie=(?:True|False|true|false) \(aucun ACK impliqué\)\.',
    'retrait reponse event=[A-Za-z]+ sell_id-demande=[0-9]+ disposition=(?:Confirmed|NeedsReview|Refused) lien-sac-verifie=(?:True|False|true|false)\.'
)
$pattern = '^\[hdv\] pid (?<pid>[0-9]+) : (?<diagnostic>(?:' + ($recognized -join '|') + '))$'
$events = New-Object System.Collections.Generic.List[object]
foreach ($file in $files) {
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if ($events.Count -ge 10000) { break }
        # The match covers the ENTIRE line, so even a numeric field followed
        # by additional private data is rejected.
        $match = [regex]::Match($line.Trim(), $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $match.Success) { continue }
        $pidValue = [uint32]::Parse($match.Groups['pid'].Value, [Globalization.CultureInfo]::InvariantCulture)
        if ($PidFilter -ne 0 -and $PidFilter -ne $pidValue) { continue }
        $events.Add([pscustomobject]@{
            Pid = $pidValue
            Diagnostic = $match.Groups['diagnostic'].Value
            # Source file name only: never a path containing an account profile
            LogSegment = $file.Name
        })
    }
}
$dir = Split-Path -Parent $target
New-Item -ItemType Directory -Path $dir -Force | Out-Null
if ($events.Count -eq 0) {
    # Explicitly overwrite any stale report to avoid misinterpreting an older
    # session as evidence of the current one.
    'Pid,Diagnostic,LogSegment' | Set-Content -LiteralPath $target -Encoding UTF8
} else {
    $events | Export-Csv -LiteralPath $target -NoTypeInformation -Encoding UTF8
}
Write-Output "UID diagnostics: $($events.Count) line(s) -> $target"
Write-Output 'Aucune preuve ACK ou vente confirmée ne peut être déduite de ce CSV seul.'
