# MZP19 — GivreClone 1.3.1 target, independently local startup

## Reproduced defect and cause

The historical reconstructed `GivreClone.App` reported `Service indisponible` at startup because `MainWindow.OnOpened` called `LicenseService.CheckVersionAsync()` against the legacy provider's signed `/version` endpoint. Unreachable/unsigned responses are expressly fail-closed in the historic implementation and show `ControlOverlay`. The old login gate also required the legacy Givre license service.

## Changes limited to reconstructed clone

- `MZP19LocalClone.Enabled = true` opts the **reconstructed** app into local startup; it is not a change to the original installed GivreClone.
- Launcher startup does not call the legacy version endpoint/watch; it does not automatically authenticate with a legacy Givre license key or show the legacy license login overlay.
- The main shell is accessible after the local language selection, with no online Givre license session or remote kill switch.
- Legacy application persistence (configuration, logs, lua, combat, maps, planning, device IDs, etc.) moved out of `%APPDATA%/GivreClone` to `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata` to avoid reading/writing installed GivreClone state.
- UI labels say `v1.3.1 · clone local` and `Mode local · fonctions Dofus non certifiées`. **1.3.1 is a reconstruction target**, not proof of binary or feature parity with the installed/native original.
- Legacy Givre logout-on-exit omitted in local mode.

## Validation

- Compiled `dotnet build GivreClone.App/GivreClone.App.csproj -c Release -r win-x64 -m:1 -v:q -p:OutputPath=%USERPROFILE%\Videos\GIVRE\GivreShareWork\artifacts\MZP19LocalBuild\` — 0 errors, 161 warnings.
- Launched `artifacts/MZP19LocalBuild/GivreClone.exe`; actual Windows window title `GivreClone v1.3.1 · clone local`; accessibility inspection confirms populated main navigation and `Aucun bot en jeu` rather than a blocking licensing error.
- Historical authentic `offline-evidence/v131-app/verified-binaries/GivreClone.exe` has NOT been executed, modified or installed over the original.

## Not certified / remaining work

The local UI opens, but actual 3.7.11.11 gameplay transport, independently attributable TEST Dofus launch, decoded inbound protocol and correlated server ACK are **not certified**. The previous fail-closed launch gates remain. This repair must never be described as a complete operational v1.3.1 clone or as a way to bypass Ankama authentication. The legacy service and permission code remain compiled for historical reconstruction; local mode does not mean every optional integration is network-free. Review each explicit feature before enabling it in TEST.

The user's separately installed/running GivreClone and all original bot clients and credentials must remain untouched. No original Givre data is copied into this local profile.
