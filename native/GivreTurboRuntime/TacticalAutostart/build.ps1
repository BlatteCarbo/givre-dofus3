$ErrorActionPreference = 'Stop'
$rustc = Join-Path $env:ProgramFiles 'Rust stable MSVC 1.99\bin\rustc.exe'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\dofus3-native-host'))
$obj = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Path $obj -Force | Out-Null
function Rust([string[]]$Arguments) { & $rustc @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Compilation Rust échouée' } }
Rust @('--crate-name','dofus_native_mod_api','--crate-type','rlib','--edition=2024',(Join-Path $source 'crates\mod-api\src\lib.rs'),'-O','-o',(Join-Path $obj 'libdofus_native_mod_api.rlib'))
Rust @('--crate-name','dofus_native_mod_sdk','--crate-type','rlib','--edition=2024',(Join-Path $source 'crates\mod-sdk\src\lib.rs'),'-O','--extern',('dofus_native_mod_api='+(Join-Path $obj 'libdofus_native_mod_api.rlib')),'-L',('dependency='+$obj),'-o',(Join-Path $obj 'libdofus_native_mod_sdk.rlib'))
Rust @('--crate-name','dofus_native_tactical','--test','--edition=2024',(Join-Path $source 'mods\native-tactical\src\lib.rs'),'--extern',('dofus_native_mod_api='+(Join-Path $obj 'libdofus_native_mod_api.rlib')),'--extern',('dofus_native_mod_sdk='+(Join-Path $obj 'libdofus_native_mod_sdk.rlib')),'-L',('dependency='+$obj),'-o',(Join-Path $obj 'tactical-tests.exe'))
& (Join-Path $obj 'tactical-tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests tactiques echoues' }
Rust @('--crate-name','dofus_native_tactical','--crate-type','cdylib','--edition=2024',(Join-Path $source 'mods\native-tactical\src\lib.rs'),'-O','--extern',('dofus_native_mod_api='+(Join-Path $obj 'libdofus_native_mod_api.rlib')),'--extern',('dofus_native_mod_sdk='+(Join-Path $obj 'libdofus_native_mod_sdk.rlib')),'-L',('dependency='+$obj),'-o',(Join-Path $PSScriptRoot 'DofusNativeTactical.dll'))
Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'DofusNativeTactical.dll') -Algorithm SHA256
