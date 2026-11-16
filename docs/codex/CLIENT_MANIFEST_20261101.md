# Installed client fingerprint — November 1, 2026

Observed at `2026-11-01T22:19:51.3792100Z`, read-only.
Installation: `%LOCALAPPDATA%/Ankama/Dofus-dofus3`.
No custom `DofusExePath` was configured. This explicitly fingerprints that
standard installation, not every installed client.

| Relative path | Bytes | SHA-256 |
|---|---:|---|
| `Dofus.exe` | 677200 | `16481b50404f7052a8e02dda2e321c48c3c8000358e99352ed22c5423d8dc59f` |
| `GameAssembly.dll` | 126063104 | `70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663` |
| `Dofus_Data/il2cpp_data/Metadata/global-metadata.dat` | 43799660 | `ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6` |

The executable's product version is Unity `6000.3.16f1 (a56f230f6470)`;
file version is `6000.3.16.10841891`. Neither value is the Dofus game/protocol
version. Metadata format is 39, independently observed by the native worker.
The matching native analysis is
`native/GivreTurboRuntime/TacticalAutostart/METADATA-3.7.12.12.md`.

Invocation:
`powershell -NoProfile -ExecutionPolicy Bypass -File %USERPROFILE%/AppData/Local/Temp/ulw-givre-100-20261101/client-manifest.ps1`.
It used `Get-Item` and `Get-FileHash -Algorithm SHA256` on exactly these three
files and wrote `client-manifest.json`; completion sentinel:
`CLIENT_MANIFEST_READY files=3`. No game process or injection was started.
