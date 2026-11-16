<#
  Source-only regression checks. Never builds/launches GivreClone or Dofus,
  creates no fake dependency project, and does not read local secrets/config.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$audit = Join-Path $PSScriptRoot 'MZP1DependencyAudit.ps1'
$build = Join-Path $PSScriptRoot 'MZP1DependencyBuild.ps1'
$targets = Join-Path $PSScriptRoot 'MZP1DependencyRecovery\ProjectLinks.targets'
$count = 0

function Assert-True([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL: $name" }
    $script:count++
    Write-Output "PASS: $name"
}

$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    # -Mode Diagnose does not call MSBuild and must report missing projects.
    $diagnostic = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $build -Mode Diagnose 2>&1)
    $diagnosticExit = $LASTEXITCODE
    Assert-True ($diagnosticExit -eq 2 -and ($diagnostic -join "`n") -match 'BLOCKED') 'missing dependencies => no build'

    # A path outside /givre is rejected *before any enumeration*.
    $outside = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $audit -WorkspaceRoot ([IO.Path]::GetPathRoot($repo)) 2>&1)
    $outsideExit = $LASTEXITCODE
    Assert-True ($outsideExit -ne 0 -and ($outside -join "`n") -match 'approved /givre') 'out-of-workspace scan refused'

    # Bounding the scan must cause an explicit incomplete signal.
    $truncated = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $audit -MaximumDirectories 1 -AsJson 2>&1)
    $truncatedExit = $LASTEXITCODE
    Assert-True ($truncatedExit -eq 3 -and ($truncated -join "`n") -match '"scanTruncated"\s*:\s*true') 'partial scan refuses completeness'

    $invalid = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $build -Mode Build 2>&1)
    $invalidExit = $LASTEXITCODE
    Assert-True ($invalidExit -ne 0 -and ($invalid -join "`n") -match 'Missing GivreCore.csproj') 'build with no pinned source refused'
} finally { $ErrorActionPreference = $previous }

# MSBuild evaluation is ONLY a structural check of opt-in project reference
# redirects. The diagnostic paths do not exist and are never compiled.
$marker = Join-Path $PSScriptRoot 'MZP1DependencyRecovery\DIAGNOSTIC_ONLY'
$core = Join-Path $marker 'GivreCore.csproj'
$launcher = Join-Path $marker 'GivreLauncher.Core.csproj'
$properties = @(
    '-p:MZP1DependencyRecovery=true',
    ('-p:DirectoryBuildTargetsPath=' + $targets),
    ('-p:MZP1CoreSourceProject=' + $core),
    ('-p:MZP1LauncherSourceProject=' + $launcher)
)
foreach ($entry in @(
    @{ project='GivreClone.Protocol\GivreClone.Protocol.csproj'; dependency=$core },
    @{ project='GivreClone.Engine\GivreClone.Engine.csproj'; dependency=$core },
    @{ project='GivreClone.App\GivreClone.App.csproj'; dependency=$launcher }
)) {
    $project = Join-Path $repo $entry.project
    $json = @(& dotnet msbuild $project -getItem:ProjectReference @properties)
    if ($LASTEXITCODE -ne 0) { throw "FAIL: MSBuild project evaluation: $($entry.project)" }
    $obj = ($json -join "`n") | ConvertFrom-Json
    $refs = @($obj.Items.ProjectReference | ForEach-Object { $_.Identity })
    $name = ('reversible reference redirect: ' + $entry.project)
    Assert-True ($refs -contains $entry.dependency) $name
}

Write-Output "MZP1 PASS: $count checks. No source recovered, no product compiled or launched."
