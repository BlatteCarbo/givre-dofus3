# Givre Turbo native runtime provenance

Source project: `https://github.com/AlpaGit/dofus3-native-host`

- Bootstrap/host upstream release: `v0.4.0`
- Bootstrap/host release tag commit: `e0ae4d522da59be84f81eee81625c1b5852366d5`
- Animation skipper source commit: `010eaac390c94b9e812e616b8c3ce9cd0525d13d`
- Animation skipper embedded mod version: `1.0.5`
- Release archive: `Dofus3NativeHost-windows-x64.zip`
- Published release archive SHA-256: `d1ccabb01c1ce57e7345c1164e14902dff7333ab62f7180d1b0ef1a8dbe37530`

Vendored binaries:

- `DofusNativeBootstrap.dll`
  - SHA-256: `2c24fa8f9905427a5e637484402df492d1942ea2de1253a3e8b49ab7e6dfda3d`
- `DofusNativeHost.dll`
  - SHA-256: `22d2c6743c23d48f80bc0a9e114ba13bcfb07587470aedb188a9fdba8846ec1b`
- `DofusCombatAnimationSkipper.dll`
  - SHA-256: `a71084fdd4a7eaf0085944280423c31a9d352fa1566a71331f1212f745c124a3`
  - Built locally from upstream source commit `010eaac390c94b9e812e616b8c3ce9cd0525d13d` with Rust `1.99.0` using the `x86_64-pc-windows-gnu` target.
  - PE machine: x86-64 (`0x8664`).
  - Exports: `DNM_Query`, `DNM_Load`, `DNM_Tick`, `DNM_Unload`.
  - Imports are Windows system libraries only (`KERNEL32.dll`, `msvcrt.dll`, `ntdll.dll`, `api-ms-win-core-synch-l1-2-0.dll`, `bcryptprimitives.dll`); no MinGW runtime DLL is required.

Why the skipper is newer than the host release:

- `mod-api` and `mod-sdk` are byte-for-byte source-compatible between the `v0.4.0` tag and commit `010eaac...`.
- The only host-native change after `v0.4.0` is in generic-method inflation (`crates/host/native/unity_bridge.cpp`).
- The `1.0.5` skipper does not use generic-method inflation; it uses ABI v8 APIs already provided by the `v0.4.0` host (`find_objects`, field access, method lookup by structural signature, hook API).
- The newer skipper is required for the current Dofus layout because it resolves `Animator2D.SetAnimation(string,bool,bool,int)`, adds the `InputChecker` fight witness, bounded fast-forward attempts, and dual fight-state recovery. The release `v0.4.0` skipper was the older `1.0.0` implementation and is intentionally not vendored here.

The runtime is MIT/BSD licensed; the upstream LICENSE/NOTICE and third-party license texts are vendored beside these binaries.

Givre does **not** install or replace `version.dll`, and does not write these binaries into the Dofus installation. It stages a private copy under `%LOCALAPPDATA%/GivreClone/NativeTurbo/<PID>/`, injects the uniquely named bootstrap explicitly, and controls the animation skipper through that host's per-process control inbox. The per-PID stage contains an empty `Dofus_Data` directory solely because the upstream standalone bootstrap uses that directory-presence check before binding to the already loaded `GameAssembly.dll` of the target `Dofus.exe` process.
