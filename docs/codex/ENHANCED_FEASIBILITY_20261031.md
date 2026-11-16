# Dofus Enhanced installation and FM advice bridge

Read-only research completed 2026-10-31. No game files changed, no downloaded code executed, no installation performed. The official ZIP was fetched into memory, inspected and discarded. The attempted background research delegation was refused by the session's maximum nesting depth; this child performed the research directly.

The active Givre checkout is `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`. An initial look at `%USERPROFILE%/Downloads/givrework` was superseded by inspection of this active checkout. Local citations below are relative to the active checkout. The requested no-shared-file-writes constraint takes precedence over the repo's `docs/codex/` note convention, so this handoff note is in the explicitly permitted temporary research directory.

## Decision

The official Enhanced release matches the installed game's exact binary fingerprints. Portable installation outside the game directory is feasible. Loading the full Enhanced module alongside the existing Givre CombatVisuals module is **not an evidenced compatible combination**: both hook the same compositor method and overlapping animation methods. Merely unticking animation options does not remove those hooks. Keep portable preparation separate from live module activation. [E1][E3][E4][L1][L2]

An in-game next-rune panel/highlight is feasible as new work, not an existing Enhanced feature. Prefer the source-owned ABI 8 host already staged per PID by Givre, with a small advice-only mod and pushed decisions from the existing forge engine. Do not deploy a second loader just for FM. An external transparent overlay is the lower-coupling alternative if a native UI binding cannot yet be verified. [E4][E5][L1][L2][L3][L4][P1]

## Exact official release and installation

GitHub's latest release endpoint returned:

- Tag `v1.20.2`, title `Dofus Enhanced 1.20.2 — Dofus 3.7.12.12`.
- Published **2026-10-27 09:06:17 UTC**, target commit `a92d9039b60d59f1dcd32707a29dbdc3dae5d513`.
- One release asset, `DofusMiniatures-r126-70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663-ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6.zip`.
- Size **2,285,967 bytes**, asset ID `681039335`.
- GitHub SHA-256 and independently computed in-memory ZIP SHA-256 both `e5f425dfe4d32abc32a832e56cdd3034abfbaa37d5b395deeb25f5fcb5664801`. ZIP CRC test returned no bad member. [E1][E2]

Exact download:

`https://github.com/Altansar69/dofus-enhanced/releases/download/v1.20.2/DofusMiniatures-r126-70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663-ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6.zip`

The inspected ZIP has exactly nine members, all under the historical folder `DofusMiniatures-3.7.11.11-r126/`: `DofusMiniatures.dll`, `ModuleLoader.exe`, `ModuleAutoLoader.exe`, `TreasureOverlay.exe`, `Update-GitHub.ps1`, `release.ini`, `update-files.ini`, `LISEZ-MOI.txt`, and `WEBVIEW2-LICENSE.txt`. There is no setup installer in this release. The folder's `3.7.11.11` is historical, not the actual support target: inspected `release.ini` says `clientVersion=3.7.12.12`, `revision=126`. This matches the packaging source. [E2][E6]

The inspected manifest pins these executable/module bytes:

| File | SHA-256 |
|---|---|
| DofusMiniatures.dll | `21e7e4b7d81270c9aee222a50a6e93a6a94b7b5a73463f7b8fa7b74a47e21212` |
| ModuleLoader.exe | `40b1bd9d590cdcc9c67de0ac117d0c349b1f9e1f3771282b25faeb6c68b285e3` |
| ModuleAutoLoader.exe | `3ff77cee1cae10e063ce558b28d3c376bcbd7154a05dfa76f27bfcf6d7025060` |
| TreasureOverlay.exe | `b024df0da8910b10872165f67516d9e9b6e2d5fdc5535be4cb084ed0d7409237` |
| Update-GitHub.ps1 | `baab21a6c02d5c7ab0b7854f2cc6787736c6e8b9397f46cbd064199558b4d8a6` |

These per-file values were read from the downloaded manifest; archive identity was independently verified against the GitHub asset digest. [E1][E2]

Official installation contract: Windows x64; retain the complete extracted folder at a short path **outside** the game folder, for example `C:/DofusEnhanced/DofusMiniatures-3.7.11.11-r126`. Do not download GitHub's source-code ZIP as the runtime package. Choose either `ModuleLoader.exe --interactive` for an already logged-in character or `ModuleLoader.exe --enable-auto "<absolute Dofus.exe path>"` for future launches. Already running clients are ignored by the autoloader. After initial interactive success, autoload is enabled by default. Settings UI is **Options -> Dofus Enhanced**. None of these commands was run here. [E3][E7]

For this computer the absolute client argument is `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus.exe`. Before activation, resolve native module ownership as described below. Uninstall according to upstream: `ModuleLoader.exe --disable-auto`, close the game, then remove only the Enhanced folder. [E3]

Runtime prerequisites are not the source build toolchain. The native mod/loader is C++ with the MSVC runtime configured static (`MultiThreaded`), not a .NET/MelonLoader plugin. Building from source needs Windows x64 Visual Studio C++ tools, CMake >= 3.20, Ninja, Python 3, Windows PowerShell and the MinHook submodule. The legacy `TreasureOverlay.exe` remains packaged for older-loader compatibility; upstream says the native panel introduced at r95 does not launch it or depend on its WebView2 engine. Do not install WebView2 merely because this helper is present. [E6][E8]

### Installed-client verification

Read-only PowerShell completed with exit code 0 on 2026-10-31:

| Installed input | Actual SHA-256 | Match |
|---|---|---|
| `GameAssembly.dll` | `70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663` | Enhanced r126 exact |
| `Dofus_Data/il2cpp_data/Metadata/global-metadata.dat` | `ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6` | Enhanced r126 exact |

Upstream checks both hashes before loading, and checks individual native method signatures again before hooks. Thus the installed binary pair is verified compatible with the release identified as 3.7.12.12. `Dofus.exe` Windows version information is Unity `6000.3.16f1 (a56f230f6470)`, not the Dofus patch version; `.release.infos.json` only identifies `gameUid=dofus`, `release=dofus3`. Neither should be used as proof of a Dofus patch number. [E2][E9][E10]

## Architecture, miniatures and overhead

Enhanced is a standalone injected native module: the loader opens the selected Dofus process, verifies identity/path and exact client hashes, then loads the DLL using Windows `LoadLibraryW` machinery. Its module attaches to IL2CPP, resolves game services and methods, and installs MinHook detours. It builds native panels from the game's own UI Toolkit/Figma components. It is not a DNH ABI 8 plugin that can be placed in `NativeMods/` unchanged. Compare its `DllMain`/worker lifecycle with the host's required `DNM_Query`, `DNM_Load`, `DNM_Tick`, `DNM_Unload` exports. [E4][E5][E7][L3]

“Miniatures” means reducing the rendered game entities while retaining their normal looks, not operating-system thumbnail windows, inventory icon miniatures, screenshots, or an FM API. Upstream documents 60% original scale, with a lower size bound relative to player height for monsters/summons, separate combat/out-of-combat choices and entity-category checkboxes. “Creatures” is a distinct simplified-look mode. Historical artifact/DLL names `DofusMiniatures-*` remain after the project became Dofus Enhanced. [E3][E11]

The built-in outline feature targets world entities/interactives; it is not a generic “highlight this rune slot” endpoint. Existing native panel creation is useful reference, but a rune widget binding must be implemented and verified. The source tree and reviewed documentation expose no shipped bot-to-Enhanced FM advice API. The craft-statistics hook explicitly excludes the smithmagic object variant and therefore cannot be treated as an existing FM state feed. [E3][E5][E12][E13]

Enhanced is not strictly idle-event-driven: its autoloader takes process snapshots every **1,000 ms**; its compositor update hook pumps several feature tick functions; native panels/render hooks have their own work. Autoload uses `HKCU/Software/Microsoft/Windows/CurrentVersion/Run`, value `DofusMiniatures`. Updates are checked at new-client startup; release selection verifies client and artifact hashes. A new FM bridge should not duplicate the process watcher, file rereads, whole-UI scans or broad network dumper. [E4][E7][E14]

## Existing Givre reuse and concrete conflicts

The previously reviewed `docs/codex/DOFUS_ENHANCED_IDEES.md` is an October 23 read-only assessment of older Enhanced commit `509a92538acfb97b746c291c359bc9d356e6dcb8`, chiefly local Avalonia feature adaptations. It does not establish a current full Enhanced installation. [L5]

The active checkout now has compiled/wired CombatVisuals according to `native/GivreCombatVisuals/README.md`, its current binary fingerprint constant and `NativeTurboRuntime` loading path. Source is selected from Enhanced commit `7bec4ce032d8847940e18f9474ca00c1e094f14b`; Givre adds an ABI 8 bridge, per-PID `visual-options.ini` mask and status file. Its six animation flags already overlap Enhanced. The top “final state after administrator resume” section of `RENDU_CODEX_20261023.md` explicitly supersedes older bottom paragraphs saying uncompiled/unwired. Do not repeat those superseded paragraphs as current status. Actual visual effect remains unconfirmed by those notes. [L1][L2][L6][L7][L16]

Concrete collision: `native/GivreCombatVisuals/bridge.cpp` installs `Compositor.Update` at **RVA `0x20F9080`**. Full Enhanced `src/core/module.cpp` installs its hook at that exact address too. Shared vendor animation hooks overlap as well. Enhanced `Api::Native` checks original bytes and rejects already patched methods with “Loaded method bytes differ from snapshot, or another module already hooks this method”. Checkbox defaults/mask 0 change behavior, not installation of the compositor detour. Therefore side-by-side auto-injection is not supported by this evidence; do not claim that disabling “no-animation” alone solves it. [E4][E10][L1]

`NativeTurboRuntime` already arbitrates GivreMods, the legacy animation skipper and CombatVisuals, unloading the previous provider. It stages its host/bootstrap/mod files per PID, rather than placing a new `version.dll` in the game directory. Its reviewed selection path contains no analogous Enhanced detection/arbitration. A source-level provider exclusion is a needed integration change if full Enhanced and Givre are to be offered on the same client. Preparing portable files alone does not solve activation ownership. [L2]

`MZP25NativeModCatalog` is a read-only pinned inventory, not a loader. Its comments correctly distinguish hash identity from successful in-game load. `CombatVisualOptions.cs` separately pins CombatVisuals and its runtime prerequisites. Do not overwrite those pins to make an unrelated upstream DLL pass. `Services/VisualMods/VisualCarnet.cs` is local versioned JSON storage for user-entered coordinates/notes; it is not an in-game plugin host or live game-state feed. [L7][L8][L9]

The reusable host ABI 8 exposes IL2CPP access, structural signature resolution, hook APIs and `DNM_Tick`. Its existing control mailbox handles only `STATUS/LIST/LOAD/UNLOAD/RELOAD`: no rune-advice command exists. The mailbox scans its directory every 15 ticks. CombatVisuals itself rereads its option file at 250 ms and rediscovers services at 500 ms. These are existing polling costs, not an event-driven advice transport to copy. A new mod can use `DNM_Tick` only as a constant-time dirty-flag/queue drain, without another compositor hook. [L1][L3][L4]

### Proposed FM data flow, not shipped capability

1. Keep objective, selected item UID/GID, item effects, stock and rule evaluation in Givre. Use the existing packet event stream: workshop start/leave, item add/modify, craft result and relevant inventory updates. `LaunchService.Forge.cs` already subscribes to `PacketDecoded`, parses `InventoryReader.ReadMagicResult`, updates `ForgeState.Stats` and reads `m.Pool`. It also sends actions; **do not call `ForgeAsync` merely to get advice**, because it opens the workshop, moves items and consumes runes. Extract/reuse its pure data/decision dependencies in an observation-only controller. [L10][L11]
2. Reuse `ForgeEngine.DecideTargets` / `DecideConditions` for configured rules and `RuneCatalog`/`RuneItemsByGid` for actual rune identity. The catalog includes `Ga Pa`, effect 111. The simpler `RuneAdvisor` has configurable gains, but its default table does not include PA and explicitly says pool/reliquat is not modeled; it is not an EXO solver. I found no dedicated automatic optimal EXO-PA policy in the reviewed engine. Define user repair thresholds, priority and the final Ga Pa attempt explicitly, with target AP relative to that item's natural baseline; do not invent a success chance or infer unknown pool as zero. Do not create “Pa Ga Pa” or “Ra Ga Pa” items from generic tier multiplication: exact game-catalog matching is required. [L10][L11][L12][L13]
3. On an objective/state revision, compute one advice result. Push a bounded versioned record containing PID plus process-creation identity, character/session identity, item UID/GID, item-state revision, objective revision, selected rune GID, text/reason and status (`ready`, `done`, `missing-rune`, `unknown-state`). Use a blocking/overlapped per-PID local named-pipe receiver, or a directory-change notification if a file transport is preferred. This is a proposed transport, not an existing host command. No idle HTTP requests, file polling or screenshots are necessary for the advice itself.
4. The receiver queues an immutable result; `DNM_Tick` on the existing Unity thread applies only changed results to one native panel. Show the next-rune name/reason first. For native-slot highlighting, inspect the actual FM window once on open/rebuild using the existing interface-analyzer approach, identify the slot by rune/item identity and reuse its current VisualElement; do not continuously enumerate the tree or match screen coordinates. Rebind only on a relevant rebuild, remove highlight on close/disconnect/item change, and ignore stale/out-of-order results. Exact FM widget/handler signatures are **not yet verified**, so highlight remains a proposal. [L3][L14]
5. Keep advice manual: the user still chooses/applies the rune. No call into move/ready/send APIs is required. Standalone unproxied Dofus would need a newly verified read-only FM state hook or explicit user state input; Enhanced's ordinary craft counter cannot supply that state. A Socket bot has no graphical Dofus PID to annotate and should retain app-only advice. [E12][L10]

Recommended first increment: observation-only Givre controller + push transport + native text panel under the existing per-PID host; add exact rune-slot highlight after a real FM UI binding is evidenced. Installing full Enhanced is a separate visual-feature choice, not a prerequisite.

## Penombre

The relevant first-party project is `Sokenzane/Penombre`, an external night-mode filter over Dofus windows. Latest release returned on 2026-10-31 is **v0.5.1**, published **2026-10-31 11:15:08 UTC**, `Penombre.exe` **545,792 bytes**, SHA-256 `f905209905fc2acd699573e9963485aa1f9c4d15e8be4136928841c230c800cb`. Exact asset: `https://github.com/Sokenzane/Penombre/releases/download/v0.5.1/Penombre.exe`. It was not downloaded or run. [P2]

It uses Windows WinForms/C# 5/.NET Framework 4, x64, click-through overlay windows, public Win32 window discovery and Magnification APIs; it does not inject or read game memory. Requires windowed/borderless Dofus, not exclusive fullscreen. The source registers WinEvent hooks for foreground, minimize, destroy/reorder, location and cloak state; however it still drives timers/render frames and documents 10 checks/second in slow mode. It is not a rune advice engine, a miniature mode or an FM state bridge. [P1][P3][P4]

Useful inference: its per-window clipping/position tracking is reference for an **external** advice overlay that follows Dofus without touching game UI. Reusing its whole shader/filter/render loop would add unnecessary continual work for static rune text; use event-driven window geometry and invalidate only when advice/geometry changes. An external overlay cannot reliably identify a native rune slot from these sources alone; exact slot coordinates need separate state/layout integration. [P1][P3]

## Licenses

- Enhanced's own `LICENSE` is **Unlicense**. Preserve the separately licensed dependencies: MinHook BSD-2-Clause, nlohmann/json MIT, WebView2 SDK license. This is code-distribution provenance, not evidence of Ankama approval. [E8][E15]
- Givre CombatVisuals preserves the vendored Enhanced Unlicense; native-host NOTICE identifies MIT and its MIT/BSD dependency notices. [L6][L15]
- Penombre is GPL-3.0; source headers permit version 3 or later. Nunito font is SIL OFL 1.1. Copying its implementation is a different licensing choice from using Windows overlay ideas independently. [P1][P3][P5]

## Remaining blockers and checks

Release/client file compatibility is verified; runtime loading, all UI rendering and native-mod coexistence were not tested. Do not equate ZIP hash, DLL existence or offline upstream tests with in-game success. Upstream v1.20.2 release notes themselves say in-game rendering remains to confirm. [E1]

For full Enhanced activation, choose one owner for compositor/animation hooks before starting a client; inspect actual loaded modules and ensure Givre will not reactivate its conflicting provider later. For the FM bridge, new work remains: observation-only controller, explicit EXO rule configuration, push transport, native UI panel and exact slot binding. Unknown snapshots must disable advice rather than reuse stale decisions. No changes implementing these were made in this research.

Temporary resource cleanup: no executable/archive was written to disk; the only intended retained research artifact is this note. The ownership marker is removed at handoff. No shared repo or game file was edited.

## Primary sources

All online sources accessed 2026-10-31. Enhanced code links use release tag v1.20.2; Penombre main was inspected on that date and release metadata is pinned below.

- [E1] GitHub release API: https://api.github.com/repos/Altansar69/dofus-enhanced/releases/latest ; release: https://github.com/Altansar69/dofus-enhanced/releases/tag/v1.20.2
- [E2] Exact release asset linked above, inspected ZIP manifests `release.ini` and `update-files.ini`; asset API: https://api.github.com/repos/Altansar69/dofus-enhanced/releases/assets/681039335
- [E3] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/README_en.md
- [E4] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/core/module.cpp
- [E5] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/notebook/notebook-panel.cpp
- [E6] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/tools/package-release.py
- [E7] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/loader/launcher.cpp
- [E8] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/docs/GUIDE.md ; https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/CMakeLists.txt
- [E9] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/core/common.hpp ; https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/core/common.cpp
- [E10] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/core/runtime.cpp
- [E11] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/appearance/features.cpp ; https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/appearance/miniature-category-labels.hpp
- [E12] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/stats/stats-economy.cpp (Crafted, around lines 483-515; InstallCraft, around 606-626)
- [E13] https://api.github.com/repos/Altansar69/dofus-enhanced/git/trees/main?recursive=1
- [E14] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/src/loader/autoload.cpp
- [E15] https://github.com/Altansar69/dofus-enhanced/blob/v1.20.2/LICENSE
- [P1] https://github.com/Sokenzane/Penombre/blob/main/README.md
- [P2] https://api.github.com/repos/Sokenzane/Penombre/releases/latest ; https://github.com/Sokenzane/Penombre/releases/tag/v0.5.1
- [P3] https://github.com/Sokenzane/Penombre/blob/main/src/Penombre.cs (OverlayManager around lines 1838-2345)
- [P4] https://github.com/Sokenzane/Penombre/blob/main/build.bat
- [P5] https://github.com/Sokenzane/Penombre/blob/main/LICENSE
- [L1] `native/GivreCombatVisuals/bridge.cpp` (ReadOptions, Update, DNM_Load, DNM_Tick)
- [L2] `GivreClone.App/Services/NativeTurboRuntime.cs:422-552`
- [L3] `native/dofus3-native-host/include/dofus_native_mod_api.h` (ABI 8 structures and DNM exports)
- [L4] `native/dofus3-native-host/crates/host/src/lib.rs:1590-1680` and `DNH_Tick`
- [L5] `docs/codex/DOFUS_ENHANCED_IDEES.md`
- [L6] `native/GivreCombatVisuals/README.md`
- [L7] `GivreClone.App/Services/CombatVisualOptions.cs`
- [L8] `GivreClone.App/Services/MZP25NativeModCatalog.cs`
- [L9] `GivreClone.App/Services/VisualMods/VisualCarnet.cs:1-100`
- [L10] `GivreClone.App/Services/LaunchService.Forge.cs:1-155`
- [L11] `GivreClone.App/Services/Eco/ForgeState.cs` and `ForgeEngine.cs`
- [L12] `GivreClone.App/Services/Forgemagie/RuneAdvisor.cs`
- [L13] `GivreClone.App/Services/Eco/RuneCatalog.cs`
- [L14] `native/dofus3-native-host/mods/interface-analyzer/README.md`
- [L15] `native/dofus3-native-host/NOTICE`
- [L16] `docs/codex/RENDU_CODEX_20261023.md` (current top final-state section supersedes older status)

