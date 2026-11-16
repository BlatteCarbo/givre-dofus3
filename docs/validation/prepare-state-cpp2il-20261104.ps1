$ErrorActionPreference = 'Stop'
$directory = '%USERPROFILE%/AppData/Local/Temp/givre-state-cpp2il-20261104'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$executable = Join-Path $directory 'Cpp2IL.exe'
$expected = '663fb432433b4371fd1ee0ebc321a8fff2a9aac5ac4230c843f9e03ddee4e04c'
if (!(Test-Path -LiteralPath $executable)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/SamboyCoding/Cpp2IL/releases/download/2022.1.0-pre-release.21/Cpp2IL-2022.1.0-pre-release.21-Windows.exe' -OutFile $executable
}
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $executable).Hash -ne $expected) {
    throw 'CPP2IL_HASH_MISMATCH'
}
Push-Location $directory
try { & $executable --help; exit $LASTEXITCODE }
finally { Pop-Location }
