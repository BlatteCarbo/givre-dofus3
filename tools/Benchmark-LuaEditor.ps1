param([ValidateRange(1, 10)][int]$Runs = 3)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$previous = $env:GIVRE_LUA_PERF_STRICT
try {
    $env:GIVRE_LUA_PERF_STRICT = '1'
    Push-Location $root
    try {
        Write-Host "Benchmark Lua Release, seuil strict 300 ms, essais=$Runs (sans compilation concurrente)."
        for ($i = 1; $i -le $Runs; $i++) {
            Write-Host "Essai $i/$Runs"
            dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore -m:1 --filter 'FullyQualifiedName~LuaToolingPerfTests.Lint_fold_and_format_of_10k_lines_under_300_ms' --logger 'console;verbosity=detailed'
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    } finally { Pop-Location }
} finally { $env:GIVRE_LUA_PERF_STRICT = $previous }
