# Safe live deploy for GivreClone.App (Release).
#
# Builds into a private artifacts folder (never GivreClone.App/bin), copies the full
# Release output to %TEMP%/givre-live-<yyyyMMdd-HHmm>/app, adds the data files the
# build does not carry, then verifies every required file before printing the folder.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/deploy-live.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/deploy-live.ps1 -CheckOnly <folder>
#
# Exit codes: 0 = ok (folder printed), 1 = build/copy failure or a required file is missing.
# This script never launches the app, stops a process, or connects an account.

[CmdletBinding()]
param(
    [string]$CheckOnly
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$AppProject = Join-Path $Root 'GivreClone.App\GivreClone.App.csproj'
$WorktreeData = Join-Path $Root 'data'
$PreviousData = '%USERPROFILE%\AppData\Local\Temp\givre-live-20261107c\app\data'

$RequiredFiles = @(
    'GivreClone.exe',
    'MoonSharp.Interpreter.dll',
    'GivreKama.dll',
    'GivreControl.dll',
    'data\matching_current.json',
    'data\bundle-build.json',
    'data\worldgraph5.json'
)

$WorktreeDataFiles = @('matching_current.json', 'bundle-build.json')

$MapDataFiles = @(
    'map-positions.json',
    'map_positions.json',
    'subarea-names-i18n.json',
    'subarea-names.json',
    'worldgraph5.json'
)

function Test-Deploy([string]$Folder) {
    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
        Write-Output "FAIL: deploy folder not found: $Folder"
        exit 1
    }
    foreach ($relative in $RequiredFiles) {
        $path = Join-Path $Folder $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Write-Output "FAIL: missing required file: $relative (in $Folder)"
            exit 1
        }
    }
}

if ($CheckOnly) {
    Test-Deploy $CheckOnly
    Write-Output "OK: all required files present in $CheckOnly"
    exit 0
}

$Stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$DeployDir = Join-Path $env:TEMP "givre-live-$Stamp"
$AppDir = Join-Path $DeployDir 'app'
$BuildArtifacts = Join-Path $env:TEMP "givre-live-$Stamp-artifacts"

if (Test-Path -LiteralPath $AppDir) {
    Write-Output "FAIL: $AppDir already exists; wait one minute so the stamp changes."
    exit 1
}

Write-Output "Building GivreClone.App (Release) into $BuildArtifacts ..."
& dotnet build $AppProject -c Release --artifacts-path $BuildArtifacts
if ($LASTEXITCODE -ne 0) {
    Write-Output "FAIL: dotnet build exited with $LASTEXITCODE"
    exit 1
}

$ExeHit = Get-ChildItem -LiteralPath (Join-Path $BuildArtifacts 'bin\GivreClone.App') -Filter 'GivreClone.exe' -Recurse -File |
    Where-Object { $_.FullName -match '\\release\\' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $ExeHit) {
    Write-Output "FAIL: no Release GivreClone.exe found under $BuildArtifacts"
    exit 1
}
$ReleaseDir = $ExeHit.Directory.FullName
Write-Output "Release output: $ReleaseDir"

New-Item -ItemType Directory -Path $AppDir -Force | Out-Null
Copy-Item -Path (Join-Path $ReleaseDir '*') -Destination $AppDir -Recurse -Force

$AppData = Join-Path $AppDir 'data'
New-Item -ItemType Directory -Path $AppData -Force | Out-Null

foreach ($name in $WorktreeDataFiles) {
    Copy-Item -LiteralPath (Join-Path $WorktreeData $name) -Destination $AppData -Force
}

foreach ($name in $MapDataFiles) {
    $target = Join-Path $AppData $name
    $source = Join-Path $PreviousData $name
    if (-not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $source)) {
        Copy-Item -LiteralPath $source -Destination $AppData -Force
    }
}

Test-Deploy $AppDir
Write-Output $AppDir
exit 0
