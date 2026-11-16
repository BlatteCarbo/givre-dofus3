[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $root 'artifacts\R6-tool-fixtures'
New-Item -Path $scratch -ItemType Directory -Force | Out-Null
$gameRoot = Join-Path $scratch 'game-install'
$markerDir = Join-Path $gameRoot 'Dofus_Data\StreamingAssets'
New-Item -Path $markerDir -ItemType Directory -Force | Out-Null
$marker = Join-Path $markerDir 'version'
$bundle = Join-Path $scratch 'bundle-build.json'
$upgradeOutput = Join-Path $scratch 'upgrade'
$upgradeScript = Join-Path $PSScriptRoot 'Upgrade-Check.ps1'
$hdvScript = Join-Path $PSScriptRoot 'R6-Hdv-Operator-Test.ps1'

'Version=3.7.11.11' | Set-Content -LiteralPath $marker -Encoding ASCII
'{"build":"3.7.11.11"}' | Set-Content -LiteralPath $bundle -Encoding ASCII
& $upgradeScript -GameRoot $gameRoot -BundleBuildPath $bundle -OutputDir $upgradeOutput | Out-Null
$result = Get-Content -LiteralPath (Join-Path $upgradeOutput 'upgrade-summary.json') -Raw | ConvertFrom-Json
if ($result.status -ne 'VERSION_MATCH_METADATA_ONLY' -or $result.protocolCertified -ne $false) {
    throw 'Matching versions must remain metadata-only.'
}
'{"build":"3.7.12.12"}' | Set-Content -LiteralPath $bundle -Encoding ASCII
& $upgradeScript -GameRoot $gameRoot -BundleBuildPath $bundle -OutputDir $upgradeOutput | Out-Null
$result = Get-Content -LiteralPath (Join-Path $upgradeOutput 'upgrade-summary.json') -Raw | ConvertFrom-Json
if ($result.status -ne 'VERSION_MISMATCH') { throw 'Mismatched versions were not rejected.' }
& $upgradeScript -GameRoot (Join-Path $scratch 'missing-install') -BundleBuildPath $bundle -OutputDir $upgradeOutput | Out-Null
$result = Get-Content -LiteralPath (Join-Path $upgradeOutput 'upgrade-summary.json') -Raw | ConvertFrom-Json
if ($result.status -ne 'MISSING_LOCAL_VERSION') { throw 'Missing installation was not rejected.' }

$logDir = Join-Path $scratch 'fake-logs'
New-Item -Path $logDir -ItemType Directory -Force | Out-Null
$fakeLog = Join-Path $logDir 'transport-offline-fixture.log'
@(
    '[hdv] pid 12 : vente demande uid-sac=123 quantite=1 inject=OK (aucun ACK impliqué).'
    '[hdv] pid 12 : annonce reçue uid-lot=? (candidat, non corrélé).'
    '[hdv] pid 12 : annonce reçue uid-lot=321 (candidat, non corrélé). token=private_should_not_copy'
    '[hdv] pid 12 : vente demande uid-sac=123 quantite=1 inject=OK (aucun ACK impliqué). prix=987'
    '[hdv] pid 34 : annonce reçue uid-lot=98 (candidat, non corrélé).'
) | Set-Content -LiteralPath $fakeLog -Encoding UTF8
$csv = Join-Path $scratch 'uid-only-fixture.csv'
& $hdvScript -LogDirectory $logDir -PidFilter 12 -OutputFile $csv | Out-Null
$rows = @(Import-Csv -LiteralPath $csv)
if ($rows.Count -ne 2) { throw "Le filtre UID doit isoler 2 lignes, trouvé $($rows.Count)." }
$content = Get-Content -LiteralPath $csv -Raw
if ($content.Contains('private_should_not_copy') -or $content.Contains('prix=') -or $content.Contains('987')) {
    throw 'Le filtre UID divulgue du contenu de journal inattendu.'
}
Write-Output 'R6 offline tool fixtures: 4/4 checks PASS (metadata, mismatch, missing, UID privacy/PID).'
