# Installs only source-owned, independently SHA-pinned binaries into THIS build output.
# Does not run/attach to Dofus, inject a DLL, modify GivreActive, or enable a mod.
param(
    [string]$SourceRoot = '',
    [ValidateSet('Debug','Release')] [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workspaceRoot = Split-Path $projectRoot -Parent
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Join-Path $workspaceRoot 'GivreWork\native\GivreTurboRuntime'
}
$source = [IO.Path]::GetFullPath($SourceRoot)
$dest = [IO.Path]::GetFullPath((Join-Path $projectRoot "GivreClone.App\bin\$Configuration\net10.0\NativeTurboRuntime"))
$prefix = $projectRoot.TrimEnd('\') + '\'
if (-not $dest.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Destination outside the GivreWork integration project'
}
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'Explicit source-owned native bundle folder is absent; no files staged'
}

$modules = @(
    @{ Source='ours-bootstrap-retry\DofusNativeBootstrap.dll'; Target='GivreTurboBootstrap.dll'; Hash='9B9C5F96DBF762EA7FB9D5AF395D4F22622781DA0B2B53FFA25C20C6C50EA75E' }
    @{ Source='v0.4.0-skipper1.0.5-speedhack1.0.3\DofusNativeHost.dll'; Target='DofusNativeHost.dll'; Hash='22D2C6743C23D48F80BC0A9E114BA13BCFB07587470AEDB188A9FDBA8846EC1B' }
    @{ Source='CombatSkipper\DofusCombatAnimationSkipper.dll'; Target='DofusCombatAnimationSkipper.dll'; Hash='7138D7DC21CD394FCF737B0DB26BFAD148341F147F5664F6290CEC563666A79B' }
    @{ Source='v0.4.0-skipper1.0.5-speedhack1.0.3\DofusCombatSpeedHack.dll'; Target='DofusCombatSpeedHack.dll'; Hash='895A195A6551EFB4A1085E500B745676CE8BF44A0967426DE3E14ABD5D1A0A24' }
    @{ Source='ItemIdOverlay\GivreItemIdOverlay.dll'; Target='GivreItemIdOverlay.dll'; Hash='A38B0C7432BBD2B2974DD7489E823B2706E23043A02BE9BDFB7B469E504F2FE9' }
    @{ Source='ours-010eaac\NativeMods\DofusNativeTactical.dll'; Target='DofusNativeTactical.dll'; Hash='21888FBA284112BF32AA368C2DA06D1DDE9D41D7D3D59347589C79A8A6F1B3FD' }
)

# Validate the whole source manifest before writing ANY destination.
foreach ($row in $modules) {
    $inputFile = [IO.Path]::GetFullPath((Join-Path $source $row.Source))
    if (-not $inputFile.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Invalid source path'
    }
    $file = Get-Item -LiteralPath $inputFile -ErrorAction Stop
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Linked source refused: $($row.Target)"
    }
    if ($file.Length -le 0 -or $file.Length -gt 16MB) {
        throw "Unexpected size: $($row.Target)"
    }
    if ((Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash -ne $row.Hash) {
        throw "Unpinned binary refused: $($row.Target)"
    }
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null
if ((Get-Item -LiteralPath $dest).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Linked output folder refused'
}
foreach ($row in $modules) {
    $target = Join-Path $dest $row.Target
    if ((Test-Path -LiteralPath $target) -and
        (Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Linked output file refused: $($row.Target)"
    }
    $tmp = $target + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Copy-Item -LiteralPath (Join-Path $source $row.Source) -Destination $tmp -ErrorAction Stop
        if ((Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash -ne $row.Hash) {
            throw "Copy hash mismatch: $($row.Target)"
        }
        Move-Item -LiteralPath $tmp -Destination $target -Force -ErrorAction Stop
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    }
    Write-Output "PINNED $($row.Target)"
}
Write-Output 'STAGED: visual modules prepared for owned test-client only; load/effect/3.7.12.12 NOT CERTIFIED'
