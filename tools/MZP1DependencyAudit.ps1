<#
.SYNOPSIS
  Offline, read-only inventory of authorized source for GivreCore and GivreLauncher.Core.
.DESCRIPTION
  Never loads GivreClone, game processes, credentials, settings or assemblies. Enumerates
  only project/zip metadata under the approved /givre workspace, excluding build
  outputs/dependency caches. ZIP inspection reads the central directory only.
  Valid sources require real .csproj and at least one C# source per project;
  no binary, dumped DLL, zip filename or untrusted manifest proves source parity.
.EXAMPLE
  powershell -NoProfile -File tools/MZP1DependencyAudit.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkspaceRoot = '',
    [int]$MaximumDirectories = 120000,
    [int]$MaximumArchives = 500,
    [switch]$AsJson
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$approved = [IO.Path]::GetFullPath((Join-Path $repository '..'))
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) { $WorkspaceRoot = $approved }
$root = [IO.Path]::GetFullPath($WorkspaceRoot)
$comparison = [StringComparison]::OrdinalIgnoreCase
if (-not ($root.Equals($approved, $comparison) -or $root.StartsWith($approved + [IO.Path]::DirectorySeparatorChar, $comparison))) {
    throw 'WorkspaceRoot must remain inside the approved /givre tree.'
}
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'WorkspaceRoot does not exist.' }

$skip = @{
    '.git'=$true; '.svn'=$true; '.hg'=$true; '.venv'=$true; 'venv'=$true;
    'node_modules'=$true; 'site-packages'=$true; 'obj'=$true; 'bin'=$true;
    '.cache'=$true; 'cache'=$true; 'caches'=$true; '.vs'=$true;
    'dist'=$true; 'packages'=$true; '__pycache__'=$true; 'logs'=$true;
    '.nuget'=$true; 'artifacts'=$true
}
$needed = @('GivreCore.csproj', 'GivreLauncher.Core.csproj')
$stack = New-Object 'System.Collections.Generic.Stack[string]'
$stack.Push($root)
$projects = New-Object 'System.Collections.Generic.List[object]'
$archives = New-Object 'System.Collections.Generic.List[object]'
$dirs = 0
$truncated = $false
$archiveCount = 0
$archiveInspected = 0
$archiveSkippedOverSize = 0
$archiveUnreadable = 0
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Relative([string]$full) {
    return $full.Substring($root.Length).TrimStart([char]'\', [char]'/').Replace('\', '/')
}

function Get-Fingerprint([string]$source) {
    return (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-SourceCount([string]$project) {
    # Project-relative code, not source from obj/bin. Avoid recursive reads of giant trees.
    $parent = [IO.Path]::GetDirectoryName($project)
    $children = @(Get-ChildItem -LiteralPath $parent -File -Filter '*.cs' -ErrorAction SilentlyContinue)
    if ($children.Count -gt 0) { return $children.Count }
    $subdirs = @(Get-ChildItem -LiteralPath $parent -Directory -ErrorAction SilentlyContinue |
        Where-Object { -not $skip.ContainsKey($_.Name.ToLowerInvariant()) })
    foreach ($sub in $subdirs) {
        $children = @(Get-ChildItem -LiteralPath $sub.FullName -File -Filter '*.cs' -ErrorAction SilentlyContinue)
        if ($children.Count -gt 0) { return $children.Count }
    }
    return 0
}

while ($stack.Count -gt 0) {
    if ($dirs -ge $MaximumDirectories) { $truncated = $true; break }
    $dir = $stack.Pop(); $dirs++
    try { $files = [IO.Directory]::EnumerateFiles($dir) }
    catch { continue }
    foreach ($file in $files) {
        $name = [IO.Path]::GetFileName($file)
        if ($needed -contains $name) {
            try {
                $projects.Add([pscustomobject]@{
                    project=$name; path=Get-Relative $file;
                    sha256=Get-Fingerprint $file; sourceFilesNear=Get-SourceCount $file
                })
            } catch { }
            continue
        }
        # ZIP central directory only. Even an archive with a suggestive name is NOT
        # accepted unless it genuinely embeds both a project file and C# source files.
        if ($archiveCount -ge $MaximumArchives) { $truncated = $true; continue }
        if ([IO.Path]::GetExtension($file).ToLowerInvariant() -ne '.zip') { continue }
        $archiveCount++
        try {
            $info = New-Object IO.FileInfo($file)
            if ($info.Length -gt 1073741824L) {
                $archiveSkippedOverSize++; $truncated = $true; continue
            } # avoid reading huge artifacts
            $zip = [IO.Compression.ZipFile]::OpenRead($file)
            $archiveInspected++
            try {
                $paths = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
                $hits = @($paths | Where-Object { $_ -match '(^|/)(GivreCore|GivreLauncher\.Core)\.csproj$' })
                if ($hits.Count -eq 0) { continue }
                $projectStats = @()
                foreach ($hit in $hits) {
                    $prefix = $hit.Substring(0, $hit.LastIndexOf('/') + 1)
                    $codeCount = @($paths | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and $_ -match '\.cs$' -and $_ -notmatch '(^|/)(obj|bin)/' }).Count
                    $projectStats += [pscustomobject]@{ entry=$hit; sourceFiles=$codeCount }
                }
                $archives.Add([pscustomobject]@{
                    archive=Get-Relative $file; length=$info.Length;
                    sha256=Get-Fingerprint $file; projects=$projectStats
                })
            } finally { $zip.Dispose() }
        } catch { $archiveUnreadable++; $truncated = $true }
    }
    try { $children = [IO.Directory]::EnumerateDirectories($dir) }
    catch { continue }
    foreach ($child in $children) {
        $name = [IO.Path]::GetFileName($child).ToLowerInvariant()
        if ($skip.ContainsKey($name)) { continue }
        # Do not follow junctions/symlinks out of /givre.
        try {
            if (([IO.File]::GetAttributes($child) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        } catch { continue }
        $stack.Push($child)
    }
}

$result = [ordered]@{
    schema='MZP1_DEPENDENCIES_V1'; scope='SOURCE_ONLY'; directoryCount=$dirs;
    archiveCount=$archiveCount; archivesInspected=$archiveInspected;
    archiveSkippedOverSize=$archiveSkippedOverSize;
    archiveUnreadable=$archiveUnreadable; scanTruncated=$truncated;
    expectedProjects=@(
        'GivreProxy/GivreCore/GivreCore.csproj',
        'GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj'
    );
    sourceProjects=@($projects | Sort-Object path);
    sourceArchives=@($archives | Sort-Object archive);
    runtimeParity='NOT_VERIFIED'; productionRestore='NOT_PERFORMED'
}
$json = ConvertTo-Json -InputObject $result -Depth 8
if ($AsJson) { Write-Output $json }
else {
    Write-Output ('MZP1 source inventory: {0} folders, {1} ZIPs, incomplete={2}' -f $dirs, $archiveCount, $truncated)
    Write-Output $json
}
if ($truncated) { exit 3 }
if ($projects.Count -eq 0 -and $archives.Count -eq 0) { exit 2 }
exit 0
