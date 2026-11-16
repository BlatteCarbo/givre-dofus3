<#
.SYNOPSIS
  Validate opt-in, source-based MSBuild project-link recovery. No binaries or stubs.
.DESCRIPTION
  Default is diagnostic-only and does not build anything. To build, both exact
  source projects and expected SHA-256 of their .csproj files must be provided.
  Paths are restricted to /givre. No project is modified; ProjectLinks.targets
  is injected using DirectoryBuildTargetsPath only for this dotnet invocation.
  --no-restore avoids network/package source or credential use.
.EXAMPLE
  powershell -NoProfile -File tools/MZP1DependencyBuild.ps1
.EXAMPLE
  powershell -NoProfile -File tools/MZP1DependencyBuild.ps1 -Mode Build `
      -CoreProject 'C:\...\GivreCore.csproj' -CoreSha256 '<64 hex>' `
      -LauncherProject 'C:\...\GivreLauncher.Core.csproj' -LauncherSha256 '<64 hex>'
#>
[CmdletBinding()]
param(
    [ValidateSet('Diagnose', 'Evaluate', 'Build')][string]$Mode = 'Diagnose',
    [string]$CoreProject = '',
    [string]$CoreSha256 = '',
    [string]$LauncherProject = '',
    [string]$LauncherSha256 = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$approved = [IO.Path]::GetFullPath((Join-Path $repo '..'))
$expectedCore = Join-Path $approved 'GivreProxy\GivreCore\GivreCore.csproj'
$expectedLauncher = Join-Path $approved 'GivreLauncher\GivreLauncher.Core\GivreLauncher.Core.csproj'
$targets = Join-Path $PSScriptRoot 'MZP1DependencyRecovery\ProjectLinks.targets'
$app = Join-Path $repo 'GivreClone.App\GivreClone.App.csproj'
$engine = Join-Path $repo 'GivreClone.Engine\GivreClone.Engine.csproj'
$protocol = Join-Path $repo 'GivreClone.Protocol\GivreClone.Protocol.csproj'

function Get-ProjectState([string]$project) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { return 'MISSING_SOURCE' }
    return 'SOURCE_FILE_EXISTS_NOT_ATTESTED'
}

Write-Output 'MZP1 source-only project-link audit (no client or process access).'
Write-Output ('GivreCore default: {0}' -f (Get-ProjectState $expectedCore))
Write-Output ('GivreLauncher.Core default: {0}' -f (Get-ProjectState $expectedLauncher))
foreach ($project in @($app, $engine, $protocol)) {
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Unexpected missing product project: $project" }
    $sha = (Get-FileHash -LiteralPath $project -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output ('source project {0} SHA256 {1}' -f [IO.Path]::GetFileName($project), $sha)
}
if ($Mode -eq 'Diagnose') {
    Write-Output 'BLOCKED: no real sources are supplied or verified. No compile attempted.'
    exit 2
}

function Test-PinnedRealSource([string]$path, [string]$expectedSha, [string]$filename) {
    if ([string]::IsNullOrWhiteSpace($path) -or [string]::IsNullOrWhiteSpace($expectedSha)) {
        throw "Missing $filename source and/or independent SHA256 pin."
    }
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($approved + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Source path must stay under approved /givre: $filename"
    }
    if ([IO.Path]::GetFileName($full) -cne $filename) { throw "Wrong project filename for $filename" }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Source project absent: $filename" }

    # FullPath only normalizes dot segments; it does not resolve directory
    # junctions. Refuse any reparse point between /givre and the source file.
    $node = $full
    while ($node.Length -gt $approved.Length) {
        $attributes = [IO.File]::GetAttributes($node)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Source link/junction is not allowed: $filename"
        }
        $node = [IO.Path]::GetDirectoryName($node)
    }
    if ($expectedSha -notmatch '^[0-9A-Fa-f]{64}$') { throw "Invalid expected SHA256 for $filename" }
    $actualSha = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
    if ($actualSha -ne $expectedSha) { throw "Source SHA256 mismatch: $filename" }

    $directory = Split-Path -Parent $full
    $stack = New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push($directory)
    $seenDirectories = 0
    $codeCount = 0
    while ($stack.Count -gt 0) {
        if ($seenDirectories++ -gt 5000) { throw "Source tree too large for safe inspection: $filename" }
        $folder = $stack.Pop()
        foreach ($child in [IO.Directory]::EnumerateFiles($folder, '*.cs')) {
            if (($codeCount++) -gt 20000) { throw "C# source file count above safe bound: $filename" }
        }
        foreach ($child in [IO.Directory]::EnumerateDirectories($folder)) {
            $basename = [IO.Path]::GetFileName($child)
            if ($basename -in @('bin','obj','.git','.vs','node_modules')) { continue }
            $attr = [IO.File]::GetAttributes($child)
            if (($attr -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            $stack.Push($child)
        }
    }
    if ($codeCount -lt 1) { throw "No real C# source files near $filename" }
    # This structural check and a matching hash do not attest EXE v1.3.1 parity.
    Write-Output ('PIN_OK {0}: {1} C# files, SHA256 {2}' -f $filename, $codeCount, $actualSha.ToLowerInvariant())
    return $full
}

$core = Test-PinnedRealSource $CoreProject $CoreSha256 'GivreCore.csproj'
$launcher = Test-PinnedRealSource $LauncherProject $LauncherSha256 'GivreLauncher.Core.csproj'
if (-not (Test-Path -LiteralPath $targets -PathType Leaf)) { throw 'MZP1 ProjectLinks.targets missing' }

$properties = @(
    '-p:MZP1DependencyRecovery=true',
    ('-p:DirectoryBuildTargetsPath={0}' -f $targets),
    ('-p:MZP1CoreSourceProject={0}' -f $core),
    ('-p:MZP1LauncherSourceProject={0}' -f $launcher)
)

if ($Mode -eq 'Evaluate') {
    foreach ($project in @($protocol, $engine, $app)) {
        Write-Output ('EVALUATE ProjectReference: {0}' -f [IO.Path]::GetFileName($project))
        & dotnet msbuild $project -getItem:ProjectReference @properties
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    exit 0
}

Write-Output 'BUILD: real source projects only. No restore, app execution or game process access.'
& dotnet build $app -c Debug --no-restore @properties -p:EnableAot=false -v:minimal
exit $LASTEXITCODE
