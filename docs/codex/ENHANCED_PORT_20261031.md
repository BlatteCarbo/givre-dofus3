# Porting Dofus Enhanced into the Givre Mods section

Read-only evaluation, 2026-10-31. No game process was started, no module was injected, and no build or test was run. Source sizes are git blob sizes at tag `v1.20.2` (commit `a92d9039b60d59f1dcd32707a29dbdc3dae5d513`). Hook addresses are the `AddHook` / `AddNativeHook` / `AddSystemHook` sites in that tag, plus the detours already compiled into `native/GivreCombatVisuals`.

## Decision

Merge the visual features into the existing CombatVisuals provider. That DLL stays the only owner of `Compositor.Update` at RVA `0x20F9080` and of the twenty animation methods below. Put new visual choices on the existing Confort client card in the combat page.

Keep the manual exclusive switch for everything else. `DofusEnhanced.ActivateAsync` already unloads CombatVisuals, GivreMods, the animation skipper, the combat speedhack and the tactical mod before `ModuleLoader.exe` loads `DofusMiniatures.dll`. That remains the way to run upstream travel, soul equip, Metamob, the in-game panels and the upstream options tab. Those features are not worth a second hook owner next to a bot that already moves, reconnects and shows its own treasure, carnet and profession screens.

The upstream DLL cannot be dropped into `NativeMods/` beside CombatVisuals. `Features::Install` installs the appearance detours and then calls `InstallAnimation`, and `Worker` always detours `Compositor.Update`. `Api::NativeAt` rejects a method whose first 16 bytes no longer match the snapshot, with “Loaded method bytes differ from snapshot, or another module already hooks this method”. Unticking animation boxes does not remove those detours.

## License and what reuse it allows

Enhanced’s own `LICENSE` at tag `v1.20.2` is the Unlicense. The text allows anyone to copy, modify, publish, use, compile, sell and distribute that software, in source or binary, for any purpose. In jurisdictions that recognize copyright, the authors dedicate their copyright interest to the public domain. There is no warranty. Givre may vendor further Enhanced sources the same way `native/GivreCombatVisuals` already vendors the animation subset and keeps `vendor/dofus-enhanced/LICENSE`.

The Unlicense does not cover the dependencies named in `README_en.md`:

- MinHook, BSD-2-Clause, `third_party/minhook`. The native host already links MinHook and records that in `native/dofus3-native-host/NOTICE` (MIT host, UnityResolve MIT, MinHook MIT wrapper and BSD-2-Clause). A port uses the host’s `DnhHookApiV5` (`create`, `enable`, `disable`, `remove`). It does not call `MH_Initialize` or ship a second MinHook.
- nlohmann/json, MIT, `third_party/nlohmann`. CombatVisuals does not include it. The first slice does not need it.
- Microsoft WebView2 SDK, `third_party/webview2/LICENSE.txt`. CMake builds it only into the legacy `TreasureOverlay` executable. The native panels do not use it. Leave it out.
- `src/maps/resource-map-generated.hpp` (295,009 bytes). `README_en.md` says the Unlicense does not cover this generated file. `docs/RESOURCE-DATA.md` says the rows come from the public DofusDB `/skills` and `/recoltables2` endpoints, that the author indicated a DofusDB agreement, and that no Dofus or DofusDB images are in the repo. Copying that header is a separate data decision, not an Unlicense grant.
- Dofus names, assets and game content remain Ankama’s. The Unlicense is not an Ankama terms-of-use grant. The upstream README says a ban cannot be ruled out.

`MZP25NativeModCatalog` is a read-only hash inventory of a different host build (comment: dofus3-native-host commit `010eaac`). It does not load mods. Do not add `DofusMiniatures.dll` to it, and do not change its pins so an upstream DLL matches. CombatVisuals is pinned separately in `CombatVisualOptions` (`GivreCombatVisuals.dll`, SHA-256 `eca6acc6cfa393dc5166bead2863734fa04a4640254518ef863980f8f038efd8`, host `22d2c6743c23d48f80bc0a9e114ba13bcfb07587470aedb188a9fdba8846ec1b`).

## One compositor owner

CombatVisuals (`bridge.cpp`) and upstream `src/core/module.cpp` both detour `Core.Engine.Compositor.Update` at RVA `0x20F9080`. Upstream then runs every feature pump from that detour: a 200 ms `Pump`, and every frame `CustomCursorFrame`, `TickMapPreload`, `TickChatTravelPlanning`, `MapInfoFrame`, `PerfOverlayFrame`, `VerticalGaugesFrame`, the glow fallback and `resourceMap::PumpRender`. Glow prefers a second detour, `UIElementsRuntimeUtility.RepaintPanels` at RVA `0x5660750`, and uses the compositor only when that frame hook has been idle for a second.

CombatVisuals already installs the same twenty animation detours as `v1.20.2` `src/animations/`. Both trees pin GameAssembly SHA-256 `70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663`. The vendored snapshot is Enhanced commit `7bec4ce032d8847940e18f9474ca00c1e094f14b`; its `signatures.hpp` already contains the four appearance RVAs and `0x5660750`. It does not contain later rows such as cursor `0x54024F0`, gauges `0x2815820` / `0x2092DE0` / `0x2092A80`, preset `0x20926E0`, or relogin `0x19F22F0`. A later slice copies those 16-byte rows from `v1.20.2` `src/core/signatures.hpp`. It does not invent them. `Api::NativeAt` throws “Missing native signature guard” when the row is absent.

| RVA | CombatVisuals detour | Upstream file |
|---|---|---|
| `0x20F9080` | `Update` | `src/core/module.cpp` |
| `0x39005D0` | `OptionGetterHook` | `src/animations/animations.cpp` |
| `0x20853E0` | `AnimationStartHook` | `src/animations/animations.cpp` |
| `0x2616140` | `MoveHook` | `src/animations/movement-dispatch.cpp` |
| `0x78F5D0` | `LandingHook` | `src/animations/movement.cpp` |
| `0x263E860` | walk `StartHook` | `src/animations/movement.cpp` |
| `0x263EE10` | walk `NextHook` | `src/animations/movement.cpp` |
| `0x263FA00` | walk `FinishHook` | `src/animations/movement.cpp` |
| `0x2646B60` | push `StartHook` | `src/animations/push-movement.cpp` |
| `0x264A8D0` | throw `StartHook` | `src/animations/throw-movement.cpp` |
| `0x264B810` | throw `FinishHook` | `src/animations/throw-movement.cpp` |
| `0x2643FE0` | jump `StartHook` | `src/animations/jump-movement.cpp` |
| `0x2644790` | jump `NextHook` | `src/animations/jump-movement.cpp` |
| `0x1F83E30` | `MonsterAnimationHook` | `src/animations/movement-waits.cpp` |
| `0x779910` | `PendingAnimationHook` | `src/animations/movement-waits.cpp` |
| `0x785ED0` | `AppearanceReadyHook` | `src/animations/movement-waits.cpp` |
| `0x208A020` | `WaitStartHook` | `src/animations/instant-waits.cpp` |
| `0x208A1A0` | `DelayCtorHook` | `src/animations/instant-waits.cpp` |
| `0x2089970` | `ScriptCtorHook` | `src/animations/script-waits.cpp` |
| `0x2089A50` | `ScriptStartHook` | `src/animations/script-waits.cpp` |
| `0x2081890` | `AnimationCtorHook` | `src/animations/combat-cleanup.cpp` |

No other Enhanced feature detours any of those twenty animation RVAs. Their collision with CombatVisuals is the shared compositor detour, plus `InstallAnimation` inside `Features::Install`. The host can load several ABI 8 mods: tactical, speedhack and the item-id overlay already do. `native/dofus3-native-host/mods` has no `0x20F9080` reference. A sibling mod coexists when its detours stay off this list and it does not install `Compositor.Update`. The bootstrap calls `DNH_Tick` about every 16 ms on the Unity thread (`kTickIntervalMilliseconds = 16`). The control mailbox is read every 15 of those ticks and only accepts `STATUS`, `LIST`, `LOAD`, `UNLOAD` and `RELOAD`.

ABI 8 is `DNM_Query`, `DNM_Load`, `DNM_Tick`, `DNM_Unload` in `native/dofus3-native-host/include/dofus_native_mod_api.h`. CombatVisuals already returns that ABI. Upstream `DllMain` starts a worker, calls `MH_Initialize`, and has none of those exports. CMake links the module with static `MultiThreaded` MSVC runtime; `build.cmd` for CombatVisuals uses `/MD` and the host hook API. A port keeps the CombatVisuals link line.

## Feature table

“Pump” means the upstream feature runs from the `0x20F9080` detour. “Own detours” are the other `AddHook` sites. “Beside CombatVisuals” means a second DLL loaded by the same per-PID host while CombatVisuals is resident. Source bytes are the `v1.20.2` blobs for that feature’s files.

| Feature | Own detours | Pump on `0x20F9080` | Overlap with the animation methods | Beside the loaded CombatVisuals module | Source bytes | Value on the Mods page |
|---|---|---|---|---|---|---|
| Animations, six fight checkboxes | The twenty RVAs in the table above | Yes. CombatVisuals pumps waits and cleanup here | This is that set. Already installed by `bridge.cpp` | Already the owner. A second installer fails the byte check | 67,147 (`src/animations/`) | Shipped. Keep this owner |
| Appearance: Normal / Créatures / Miniatures, per category, in and out of combat | `0x19755D0` `exf.bazg`, `0x2614FF0` `AnimatedEntity.rez`, `0x2652CF0` `yh.rmo`, `0x2652E50` `yh.rmp`. `Install` also calls `InstallAnimation` | Yes. `Features::Tick` measures sizes and refreshes looks | Own RVAs are free. The trailing `InstallAnimation` is not | Same DLL only. Call appearance install from the existing `Update`, and do not call `InstallAnimation` twice | 54,424 for the look sources (`features`, `appearance-refresh`, `size-*`, `miniature-category-labels`, `mode-policy`, `render-policy`, `startup-options`). Menus are the next row | High. This is the missing half of the vendored module. `settings.mode` defaults to 2, so a port must default Givre to 0 unless the profile opts in |
| In-game creature button and map right-click (Affichage, Animations, Surbrillance) | `0x1726AE0`, `0x2C63890`, `0x2C63540`, `0x2C65410`, `0x2C63270`; menu makers `0x1700070`, `0x16DC360`, callback `0x16CA600` | Yes, `TickDisplayShortcut` and `TickContextDisplayMenu` | None of these RVAs | Hooks fit a sibling. The page below replaces them | 61,570 (`display-shortcut`, `context-display`) | Low. The Mods card is the settings surface |
| Highlight, six outline categories, modes off / UI / UI and walls / always | None of its own. End-of-frame detour is `RepaintPanels` `0x5660750`. Resolved Unity and map methods are calls, not detours | Fallback only, after one second without a repaint | None | Sibling if it hooks only `0x5660750`. Safer inside the owner so the fallback does not grow a second `Update` | 132,070 (`src/glow/`) | High as the second visual slice. Cap stays 32 outlines in the upstream description |
| Cursor, Dofus-style pointer, 15 colours, three sizes, optional game cursors | `Cursor.SetCursor` `0x54024F0` and a `LoadImageW` system detour. Per-frame work runs before the service gate | Yes, every compositor frame | None of these RVAs. The frame pump is the compositor | Same DLL. A sibling would need its own `Update` | 111,811 (`custom-cursor*`, `cursor-art`, `game-theme`) | Low for a bot client. Cosmetic, and the system detour is process-wide |
| Vertical gauges | `SaveUiPositionById` `0x2092DE0`, `SaveUiDimensionById` `0x2092A80`, `CreateGenericGaugeSection` `0x2815820` | Yes, `VerticalGaugesFrame` every frame and `TickVerticalGauges` on the 200 ms pump | None | Same DLL for the frame pump. The three RVAs themselves do not collide | 83,200 (`vertical-gauges.cpp`, header, `vertical-gauge-policy.hpp`) | Low. Layout cosmetic. Signature rows are not in the vendored `signatures.hpp` |
| Saved layouts for mod windows | `SavePreset` `0x20926E0`, `AddPreset` `0x2090D40`, `RemovePreset` `0x2091F60`, `ApplyPreset` `0x2090E40`, `EditPreset` `0x20916A0` | Yes, `TickPresetWindows` | None | Sibling possible. Useless until in-game mod windows exist | 44,117 (`preset-windows*`) | Low. Depends on panels this port does not take |
| Resource markers, world map and minimap, recenter, resource bar, right-click map settings | World-map UI `OnEnable` `0x1BAEA20`, `OnDisable` `0x1BAE560`. Render methods are calls | Yes, `PumpRender` every frame and `TickUi` on the pump | None | Sibling for the two UI detours; render wants the frame pump, so the safe place is the owner | 382,808 (`src/maps/resource-map*`), of which 295,009 are the generated DofusDB header | Medium as a display, after the license for that header is accepted. Known spots, not live availability. Large |
| Map hover: zone name and dimension | Tooltip `fhw.berz` `0x1BEF880` and a builder `Update` `0x15EFCA0` (not the compositor) | Yes, `MapInfoFrame` | None | Sibling for the two detours; frame text wants the owner pump | 22,568 (`map-info.cpp`, header) | Medium. Small and display-only |
| Ochre quest zones on the world map and minimap | No detour. Many `FindVerified` calls, including zone draw and data-center reads | Yes, `TickArchiZones` | None | Sibling via `DNM_Tick` | 102,681 (`archi-zones.cpp`, header) | Medium. Large, and it wants Metamob or quest state for “missing” |
| Fast map transitions | `DOColor` `0x30AAEC0` | Installed beside the pump; the hook itself is the feature | None. Signature row is already in the vendored table | Sibling | 3,758 (`map-transitions.cpp`, header) | Medium. One hook, shorter fades. Upstream marks loading and the server as still required |
| Next-map preload | No detour. Addressables calls `0x4D786C0`, `0x527B010`, `0x527BFD0` and related | Yes, `TickMapPreload` every frame once services exist | None | Same DLL if it must stay on the frame pump | 12,489 (`map-preload.cpp`, header) | Low. Upstream calls it experimental |
| Smarter travel: chat TP, double-click, “Voyager jusqu’à cette position”, zaaps, Havenbag | `fhw.betw` `0x1BF78A0`, `fhw.betn` `0x1BF5E50`, `fhw.beuf` `0x1BF8F40`, `fhw.beud` `0x1BF88F0`, minimap `bedm` `0x1B93460`, big-map `bedm` `0x1BECC50` | Yes, `TickMapTravel` | None | Hooks fit a sibling. Behavior drives autopilot on the same client the bot moves | 35,559 (`map-travel`) plus route graph 22,278 | Exclusive. A bot path and this planner on one client will both issue travel |
| Roublard secret passage and temple | No detour. It calls NPC menu and dialog methods (`OnNPCMenuClick` `0x16E89A0`, `SelectReply` `0x2A64DB0`, `OnClose` `0x2A61860`) | Used by the travel planner | None | Sibling in form only | 59,907 (`roublard-passage`) | Exclusive. It talks to an NPC and chooses replies |
| Spouse teleport and party-member travel button | Spouse menu `CreateMenu` `0x16ECE90`. Party button is built by calls, including autopilot `esg.zhy` `0x18BD470` | Yes | None | Sibling in form only | 65,092 (`src/social/`) | Exclusive. Click path starts a trip |
| Soul alert, auto-equip, capture panel | Phase `fxj.bhyl` `0x1E05670`; slot mouse and click `0x165C170`, `0x165C200`, `0x165BDF0`, `0x165B840`, `0x165CF10`; layout `0x2091060`, `0x207F0C0`; panel drag and menu `0x16D04D0`, `0x166DF30`, `0x1733850`, `0x16702F0`, `0x1670470`, `0x16709C0`; group menu `0x16E72F0`; widget toggle `0x1715030`. Capture detection itself has no detour | Yes | None | Sibling in form only | 176,256 (`src/souls/` without the chat files) | Exclusive. Auto-equip changes equipment. The green/red alert is the only display-only piece, and it is tied to this panel |
| Metamob sync and stored API key | No detour found in `metamob-sync.cpp`. HTTP and DPAPI live beside the pump | Yes, `TickSync` | None | Sibling in form only | 152,618 (metamob sources except the calendar) | Exclusive. It writes a third-party quest count and stores a key |
| Chat: TP, soul-in-bag, Ochre, Metamob count, dimension, clickable coordinates | `BindItem` `0x2D8E4C0`, `TryDecodeText` `0x168D990`, `HyperlinkService.TryDecode` `0x169CE90`, click `0x168E880`, tooltip `0x168E760`; zaap list `0x1A4AA10` and quote `0x1C4A5D0` | Yes | None | Sibling in form only | 156,479 (travel chat files 120,029 plus `chat-soul-*` 36,450) | Exclusive. TP and the travel hooks start a trip |
| Kralamoure openings in the calendar | Calendar `OnEnable` `0x2E065E0`, `OnDisable` `0x2E06550` | Yes, `TickCalendarKralove` | None | Sibling | 52,594 (`calendar-kralove`) | Low. Needs the Metamob key, at most one read per 10 minutes while the calendar is open |
| Treasure-hunt panel | No detour. Reads the hunt service and calls DofusDB | Yes, `TickTreasurePanel` | None | Sibling via `DNM_Tick` | 76,342 (`src/treasure/`) | Low. Givre already has a treasure screen. Upstream does not validate steps |
| Personal notebook | No detour. `notebook-native.cpp` calls flag and chat send methods | Yes, `TickNotebookPanel` | None | Sibling in form only | 106,999 (`src/notebook/`) | Low. `VisualCarnet` already stores user coordinates. The send-to-chat path is an action |
| Profession XP tab | Job UI `OnEnable` `0x2AF67C0`, `OnDisable` `0x2AF6780`, `SwitchTab` `0x2ABC770` | Yes, `TickJobsXp` | None | Sibling | 45,322 (`src/jobs/`) | Low. DofusDB recipes, and the app already has profession and craft planners |
| Performance overlay | No detour | Yes, `PerfOverlayFrame` every frame | None | Same DLL for a per-frame overlay; `DNM_Tick` at 16 ms is the sibling substitute | 114,905 (`perf-overlay.cpp`, header) | Low. FPS, memory, CPU, GPU and disk for the game process. Large |
| Per-character statistics | Kamas `0x2AF6F20`, inventory content `0x1B07A50`, storage `0x190B360`, trade `0x1A05F60`, harvest `0x1A87B60`, craft `0x19D18D0`, quest `0x1AA7380`, achievement `0x1A90D70`, fight action `0x1EA0910`, teleport `0x1A4A5E0`, inventory `0x1B57D20` | Yes | None. The craft hook is an ordinary craft counter; the feasibility note already records that it skips the smithmagic object variant | Sibling | 234,725 (`src/stats/`) | Low. Local counters the bot already tracks in its own session. Many detours for a duplicate |
| Top-bar buttons and header submenu | `CreateHeaderSubMenu` `0x17D9B00`, `SetAllHeaderState` `0x17DDDD0`. Shared click helper `ProcessUpEvent` `0x172B6A0` | Yes, `toolsUi::Tick` | None | Sibling, and only useful with the in-game panels | 23,121 (`tools-ui`) plus click helper 3,452 | Skip. The Mods card opens nothing inside the game frame |
| In-game Options → Dofus Enhanced tab | General `OnEnable` `0x2D80470`, tab `Init` `0x2D84EC0`, text-button `0x2D88800` and `0x2D885A0` | Yes, `ui->Tick` | None. `0x2D80470` and `0x2D84EC0` are signature rows in the vendor file and are not detoured by CombatVisuals | Sibling in form only | 128,247 (`options-ui.cpp`, header) | Skip. The Confort client card replaces this page |
| Relogin after an inactivity disconnect | Handler `phz` `0x19F22F0` | Installed at startup; `InstallReloginGuard` does not throw on failure | None. Signature row is absent from the vendored table | Sibling in form only | 4,146 (`relogin-guard`) | Exclusive. Givre already owns reconnect. This changes the launcher login path |
| Autoload, GitHub update, `ModuleLoader` | No game-method detour. The loader is a separate process. Autoload polls every 1,000 ms and registers `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `DofusMiniatures` | The DLL’s `DllMain` worker is what installs `0x20F9080` | The loader is how the colliding DLL enters the process | Not an ABI 8 mod | 55,112 (`src/loader/`) | Do not port. `NativeTurboRuntime` already stages bootstrap, host and mods per PID and never drops a new `version.dll` into the game directory |

`src/` at this tag is 2,827,064 bytes across those folders. Porting the tree as one module would copy the loader, the options page and the action features along with the visuals.

## How options map onto the Mods page

The Mods surface that already owns these choices is the Confort client card in `GivreClone.App/Views/BotsView.axaml` (the combat page card whose comment names `GivreMods.dll`). It is not `VisualModsPanel`. That panel is `VisualCarnet`: local coordinates and notes, with no process and no hooks.

The six animation boxes already match `Settings` and the ini mask. `NativeTurboRuntime.WriteVisualOptions` writes `mask=` and keeps only bits 0..5 (`mask & 63`). `bridge.cpp` `ReadOptions` rejects a value above 63. Death stays animated while instant movement or visual-wait removal is on, which is the upstream rule and the current Givre tip.

| Enhanced field | Bits or control today | Profile |
|---|---|---|
| `noSpellEffects` | bit 1, default on | `NoSpellEffects` |
| `shortSpellCasts` | bit 2, default on | `ShortSpellCasts` |
| `noAttackAnimations` | bit 4 | `NoAnim` |
| `noDeathAnimations` | bit 8 | `NoDeathAnimations` |
| `instantMovements` | bit 16 | `InstantMove` |
| `noVisualWaits` | bit 32 | `Collapse` |

Add appearance on that same card, as extra ini lines. Do not widen the mask. The current reader ignores lines after `mask=`, so an older DLL keeps working.

| Enhanced field | Card control | Ini line | Givre default |
|---|---|---|---|
| `mode` 0 Normal, 1 Créatures, 2 Miniatures | Combat appearance combo | `combat-mode=` | 0. The struct default is 2; leaving it would shrink fighters for every current user |
| `roleplayMode` same three values | Out-of-combat combo | `roleplay-mode=` | 0, matching the struct |
| `CombatMiniatureToggles` (self, allied players, allied summons, own summons, enemies, enemy summons) | Six boxes, enabled when combat mode is Miniatures | `combat-miniatures=` six `0`/`1` characters | The upstream defaults, all on, and applied only when mode is Miniatures |
| `RoleplayMiniatureToggles` (self, party, other players, monsters, NPCs) | Five boxes | `roleplay-miniatures=` five characters | Upstream defaults: all on except NPCs |

`EnableAsync` loads CombatVisuals only when `VisualMask != 0`. An appearance-only profile has mask 0 today, so the module would never load. The load predicate has to treat a non-zero appearance choice as a reason to load, and teardown (`WriteVisualOptions(root, 0)` outside a profile push) has to write the modes as 0. `CombatVisualOptions.IsProfilePush` already exists so a cleanup does not resurrect the default animation bits. Appearance needs the same rule.

Glow, cursor, gauges and the map overlays can use the same card later, one group at a time. Travel, souls, Metamob, relogin, autoload and GitHub updates stay off this card. The existing **Activer Dofus Enhanced** button remains the exclusive switch. Its tooltip already says CombatVisuals is unloaded and does not stay active. `EnhancedOwnsHooks` then forces the visual mask to 0 so the host does not put CombatVisuals back.

Socket sessions stay without a graphical mod, as `LaunchService.VisualOptions` and `CanCombatVisualOptions` already require a non-socket PID.

## First port slice

Appearance only, inside `givre.dofus3.combat-visuals`. No new mod id, no `DllMain`, no `MH_Initialize`, no `resource-map-generated.hpp`, no WebView2, no options tab, no travel.

Copy these `v1.20.2` files into `native/GivreCombatVisuals/vendor/dofus-enhanced/src/appearance/`. `mode-policy.hpp` is already there; diff it and keep one copy.

- `features.cpp`
- `features.hpp`
- `appearance-refresh.cpp`
- `appearance-refresh.hpp`
- `size-measurements.cpp`
- `size-measurements.hpp`
- `size-policy.hpp`
- `miniature-category-labels.hpp`
- `render-policy.hpp`

Leave `context-display.cpp`, `display-shortcut.cpp` and `startup-options.cpp` out of this slice. The card replaces the in-game menus, and startup status belongs to the upstream loader.

`features.cpp` calls `AddHook`, `module->creatures`, `module->FindService`, `module->fight` and `Features::Tick`. The adapter module has `fight` and a local `Service` helper, and it has no `FindService` and no `creatures` field. `Features::Install` ends in `InstallAnimation`. `DNM_Load` already calls `InstallAnimation`.

Edit only:

- `native/GivreCombatVisuals/adapter/core/module.hpp` — hold the creatures service and a `FindService` that uses the helper already in `bridge.cpp`.
- `native/GivreCombatVisuals/adapter/appearance/features.hpp` — replace the animation-only stub with the upstream `Features` declarations. Keep `InstallAnimation` defined in `animations.cpp`.
- `native/GivreCombatVisuals/bridge.cpp` — store the creatures service in `Discover`; read the new ini lines in `ReadOptions` and force mode 0 when the line is absent; call `Features::Tick` from the existing `Update`; call appearance install once from `DNM_Load` and do not install the twenty animation detours a second time. One `Features::Restore` must clear both animation waits and appearance state.
- `native/GivreCombatVisuals/build.cmd` — the `cl` line compiles `animations\*.cpp` only. Add the appearance `.cpp` files. Keep `/MD` and the host include. Do not compile `src/core/module.cpp`.
- `native/GivreCombatVisuals/vendor/dofus-enhanced/src/animations/animations.cpp` — leave the animation detours. `sizes.pending` stays owned by `Features::Tick` in the new appearance file. Do not restore a second cleanup.

The four appearance RVAs and `0x2615260` (`rfa`, checked with `api.Native`, not detoured) are already in the vendored `signatures.hpp` (lines 89, 245, 246, 339, 340). This slice adds no signature bytes.

Application side, same slice:

- `GivreClone.App/Services/CombatProfile.cs` — combat mode, roleplay mode, and the eleven category flags.
- `GivreClone.App/Services/CombatVisualOptions.cs` — keep `Mask` on the six bits; add the ini lines and a load predicate that is true for appearance as well as for a non-zero mask.
- `GivreClone.App/Services/LaunchService.VisualOptions.cs` — `NativeVisualOptions` currently returns only `Mask`. The writer needs the appearance fields from the same profile, and a non-push teardown must still persist zeros.
- `GivreClone.App/Services/NativeTurboRuntime.cs` — `WriteVisualOptions`, `DesiredState.VisualMask`, and `wantVisuals = desired.VisualMask != 0` in `EnableAsync`. Cleanup that writes `mask=0` must also write the modes as 0.
- `GivreClone.App/ViewModels/BotsViewModel.CombatVisualMods.cs` — bindings, `NotifyVisualProperties`, and the two preset buttons so “Animations normales” also clears appearance mode.
- `GivreClone.App/ViewModels/BotsViewModel.Combat.cs` — only if the new properties need the same save path as `CombatCollapse` and `CombatInstantMove`.
- `GivreClone.App/Views/BotsView.axaml` — the Confort client card, directly under the six animation boxes.
- `GivreClone.App/Lang/fr.axaml`, `en.axaml`, `es.axaml` — strings next to `L.Bots.Combat.Visuals.NoSpellEffects`.
- `GivreClone.Tests/CombatVisualOptionsTests.cs` — the 64 mask combinations stay valid; add the new fields and the “missing key means mode 0” case.

Do not edit `DofusEnhanced.cs`, `MZP25NativeModCatalog.cs`, or the Activer Dofus Enhanced button. After this slice, exclusive activation still unloads the enlarged CombatVisuals DLL before the upstream loader runs.

Rebuild would change `CombatVisualOptions.Sha256`. This evaluation did not rebuild. The pin is updated only with a real binary. In-game size and category behavior stay unverified until one option is enabled at a time against `visual-options.status`.

## What stays exclusive

`GivreClone.App/Services/DofusEnhanced.cs` checks the r126 loader and DLL hashes, refuses a fight and a PID this bot does not own, calls `PrepareForEnhancedAsync`, and only then starts `ModuleLoader.exe` with the PID and `DofusMiniatures.dll`. `PrepareForEnhancedAsync` sets `AppConfig.DofusEnhancedEnabled` and requires GivreMods, CombatVisuals, the skipper, the speedhack and the tactical mod to be gone. That is the whole upstream product: travel, Roublard dialogue, spouse and party trips, soul equip, Metamob, calendar, statistics hooks, relogin, and the in-game options tab. Merging them into CombatVisuals would copy action code onto a client the bot also drives, and would still leave `0x20F9080` with one owner.

## Sources

Upstream files are tag `v1.20.2` at https://github.com/Altansar69/dofus-enhanced/tree/v1.20.2. Local files are the active checkout.

- `LICENSE`, `README_en.md`, `CMakeLists.txt`, `docs/RESOURCE-DATA.md`
- `src/core/module.cpp` (`UpdateHook`, `RepaintPanelsHook`, `Worker`, `DllMain`), `src/core/module.hpp`, `src/core/runtime.cpp` line 182, `src/core/settings.hpp`, `src/core/signatures.hpp`, `src/core/relogin-guard.cpp`
- `src/appearance/features.cpp` `Features::Install` and `Features::Tick`; `src/animations/*.cpp`; `src/glow/*`; `src/ui/custom-cursor.cpp`, `vertical-gauges.cpp`, `preset-windows.cpp`, `options-ui.cpp`, `perf-overlay.cpp`, `tools-ui.cpp`, `native-click.cpp`
- `src/maps/map-transitions.cpp`, `map-preload.cpp`, `map-info.cpp`, `resource-map-ui.cpp`, `archi-zones.cpp`
- `src/travel/map-travel.cpp`, `chat-travel.cpp`, `chat-tp-links.cpp`, `roublard-passage.cpp`
- `src/social/*`, `src/souls/*`, `src/metamob/*`, `src/treasure/*`, `src/notebook/*`, `src/jobs/jobs-xp-panel.cpp`, `src/stats/stats-economy.cpp`, `stats-fights.cpp`, `stats-travel.cpp`
- `src/loader/autoload.cpp`, `src/loader/launcher.cpp`
- `native/GivreCombatVisuals/bridge.cpp`, `build.cmd`, `README.md`, `adapter/core/module.hpp`, `adapter/appearance/features.hpp`, `vendor/dofus-enhanced/LICENSE`, `vendor/dofus-enhanced/src/core/signatures.hpp`, `vendor/dofus-enhanced/src/core/settings.hpp`, `vendor/dofus-enhanced/src/core/runtime.cpp`, `vendor/dofus-enhanced/src/animations/*.cpp`
- `native/dofus3-native-host/include/dofus_native_mod_api.h`, `NOTICE`, `crates/host/src/lib.rs` (`execute_control_command`, `process_control_requests`, `DNH_Tick`), `bootstrap/src/version_proxy.cpp` (`kTickIntervalMilliseconds`)
- `GivreClone.App/Services/NativeTurboRuntime.cs`, `CombatVisualOptions.cs`, `DofusEnhanced.cs`, `MZP25NativeModCatalog.cs`, `LaunchService.VisualOptions.cs`, `VisualMods/VisualCarnet.cs`
- `GivreClone.App/Views/BotsView.axaml` Confort client card and Activer Dofus Enhanced
- `GivreClone.App/ViewModels/BotsViewModel.CombatVisualMods.cs`
- `GivreClone.Tests/CombatVisualOptionsTests.cs`
- `docs/codex/ENHANCED_FEASIBILITY_20261031.md` for the r126 release identity, the installed GameAssembly and metadata hashes, and the earlier conclusion that the two DLLs do not coexist
