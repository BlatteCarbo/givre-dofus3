# Recovery and combat evidence, November 2, 2026

## Workspaces

- Main: `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.
- Combat implementation: sibling `combat-planner-slice1`, based on `e66edcc`.
- Interrupted Claude workers could not resume because authentication failed.
  Read-only investigations were recovered on an explicit ChatGPT model.
- Memory writes remain blocked by an unrelated dirty memory file,
  `reference/givre-fm-parity.md`. That file was not modified.

## Verified changes

### Tactical root

The previous live client built 560 tactical sprites without a visible grid.
The static root was an empty `parentTiles` object.

The module now resolves `GameObject.Find("map_<id>")` when the static root
does not have a supported visual branch. It requires a branch that the
existing suppression/restoration code knows how to handle. Merely naming an
object `parentTiles` no longer admits it.

- Native build: exit 0, eight tests passed.
- DLL SHA-256:
  `9e9a7983360e51b1992e0971ef58a7ca442e5ed38a0a725302bc2a72ba618532`.
- Runtime, native catalog and README pins updated together.
- Main Release suite: 3,908 passed, two existing skips, zero failures.
- Live follow-up, client PID 18668: prefab acquired, but
  `map_54165809 not found; static root: L0 'parentTiles' [0]`.
  The July upstream naming fallback does not resolve this active client's
  visual root. The grid remains unresolved; the module now refuses the empty
  root instead of claiming that an invisible overlay is usable.
- Native FFI behavior has not been verified under Miri or a sanitizer.

### Recovered combat planner

The interrupted implementation retained per-grade effect descriptors, a bounded
move/cast/move planner and an automatic-mode execution branch. It had no planner
tests and did not compile.

Recovery fixed an invalid pattern around `SelfCell()`, linked the new pure
sources into the existing test project and added deterministic scenarios.
Local ignored dependencies and the existing combat-visual DLL were connected
to the sibling worktree through directory junctions; no dependency source was
changed.

Targeted run: ten tests passed, zero skipped. Scenarios cover:

- approach to reach, cast, then retreat;
- heal or shield under low HP and incoming threat;
- teleport to finish a target;
- declining a teleport whose exposure outweighs its damage;
- global and per-spell approach prohibitions;
- finishing a low-HP enemy instead of damaging a healthy enemy.

Full sibling run: 3,915 passed, two existing skips, one failure:
`LuaApiInventoryTests.Inventory_covers_the_current_binding_sources`.
The failure is unrelated to combat changes: the inventory hashes raw source
bytes, and this checkout has different line endings in
`GivreLuaHost.Combat.cs`. Both worktrees have identical text after CRLF
normalization, but only the main worktree matches the stored byte hash.
The test was not weakened or skipped, and the inventory was not regenerated.

These changes remain on the sibling worktree. They are not merged, committed,
or live-tested. Green pure tests do not establish integration correctness,
whole-class support or correct installed spell data.

Further sibling changes:

- Planner actions retain the selected configuration index. Duplicate manual
  spell IDs with distinct targeting no longer dispatch the first matching row.
- `AutoElements` supports automatic selection or explicit combinations of
  neutral/earth/fire/water/air. Every damage effect must satisfy the selection;
  supported utility remains eligible.
- Generated attacks are ordered using per-effect damage estimates and observed
  character totals. Controlled fighters do not inherit the owner's element
  restriction or characteristics.
- Clone/import retain automatic settings, and priority generation saturates
  rather than overflowing the disabled-script sentinel `int.MaxValue`.
- Strict planner candidate generation no longer admits missing descriptors
  merely because aggregate damage is positive.
- Latest targeted run: 34 passed, zero failures or skips (planner, automatic
  rotation, import). The full suite has not been rerun after these additions.
- Element selection has no new UI yet; class-specific transitions remain
  unsupported as described below.

## Multi-action and companion follow-up

The user's Sacrieur/Roublard examples require sequences whose setup actions
have no immediate damage. Named spell rules have not been guessed from those
examples. A local spell-data probe was started to extract current installed
effects and per-grade constraints before admitting displacement transitions.

- Walking can now be expanded between casts, not only before the first cast.
  A synthetic teleport/walk/attack regression covers a blocked initial route.
- Controlled actors without their own observed stats receive zero budget,
  not the generic owner fallback of 6 AP / 3 MP.
- Both returning control to the owner and the free-control packet path use the
  same cancellation/epoch teardown and preserve companion cooldown history.
- Latest targeted run: 54 passed, zero failures or skips, covering planner,
  rotation, imports, controlled actors, revocation and control-message readers.
- Predictive pull/swap, boosts, bomb chains, traps and state-dependent setups
  are not implemented by these changes.

## Installed spell-data evidence

The local probe in
`%USERPROFILE%/AppData/Local/Temp/ulw-givre-100-20261101/spell-probe`
decoded the installed spell-level bundle through the application's existing
AssetsTools reader. Both probe runs exited 0.

- Raw immediate effects have `triggers="I"`, `random=0`, `delay=0`.
  The interrupted parser incorrectly classified every nonempty trigger string
  as conditional. It now recognizes the exact immediate trigger and continues
  to reject other trigger expressions.
- Explicit shape `P` is a point independently of the stored radius, matching
  the existing geometry contract. The prior `Size == 0` restriction excluded
  point effects with the installed default size.
- The installed Flèche de Barrage effect used by the real-data regression is
  a `T` zone, not a point. Its regression checks immediate-trigger decoding;
  separate point-radius cases cover point certainty.
- Attirance (12735), Transposition (12736) and Entourloupe (13439) were found
  in the installed data. Attirance contains state-filtered target masks;
  Transposition has a state criterion; Entourloupe includes nested-action and
  template/state-filtered effects. None can safely be replaced by a generic
  unconditioned swap/pull based on its name.
- MCP inspection of Bastionbot confirms learned Transposition grade 3 and
  Attirance grade 3, alongside Projection grade 1 and Assaut grade 3.
  Grade-1 probe examples are not assumed to describe those learned grades.
- Session inspection: in game, map 54165809, not in combat, 1,254,757 kamas.
  No fight or purchase was initiated during this recovery.

Latest full combat suite: 3,934 passed, two existing skips and the same single
raw-line-ending Lua inventory failure. The real installed-data regression
passes after the trigger correction. TRX output is in the scratch
`combat-results/combat-recovery.trx`. No combat changes have been integrated.

## Renderer diagnostic recovery

The existing runtime-inspector was compiled locally (release build exit 0),
then submitted through the live host's local mailbox. It refused to load:
`DNM_Load returned -1`. Native log identifies the required `get_isVisible`
binding as absent. Optional PNG encoding was also unavailable.

The inspector now treats missing visibility as unknown, includes active
renderers instead of silently discarding them, and writes `unknown` in the CSV.
Its test/build monitor is active; the modified inspector has not yet been
loaded or captured. This is a diagnostic change, not a fixed tactical grid.

Follow-up: inspector test command exited 0 with zero tests; release build
exited 0. Second live load still failed, now at required `get_sharedMaterials`.
No renderer capture was produced. These optional rendering/introspection APIs
need a deliberately smaller diagnostic surface rather than assuming the
whole July inspector works on the current client.

## Continuation handles and ownership

- QA application PID 12796, window 4460692, main tested recovery build.
- Dofus PID 18668, window 6293402, Bastionbot on Mikhal; no script or fight
  initiated in this recovery.
- Native host mailbox:
  `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/NativeTurbo/18668/NativeMods/control/18668`.
- Main runtime is still the main branch, not the modified combat worktree.
- C# language server installation was authorized by the user's tool-install
  permission and started through monitor `mon_KSWEAPMF0A6RXN9G`, bash `bash_38`.
  Installation completed successfully: `csharp-ls` 0.28.0.
  Its executable reports its version at
  `%USERPROFILE%/.dotnet/tools/csharp-ls.exe`. Explicit local `.omo/lsp.json`
  entries were added to both worktrees, preserving the main Lua settings.
  The session's LSP bridge still reports its old missing-command error;
  a successful symbol-resolution roundtrip has NOT been demonstrated.
- The JavaScript kernel has `rootResume`, `combatRoot`, `runTmp`, `nativeLive`,
  `callControlJson` (framed local named-pipe MCP), `fs`, and `crypto`.
- Latest complete combat TRX: 3,934 pass, one known raw-line-ending inventory
  failure, two prior skips; no other failures. No git commits or pushes made.
- Current uncommitted inspector patch makes `get_isVisible` optional and emits
  `unknown`; shared materials remain required and block loading.

## Next recovery iteration

- Tactical discovery delegated to background child `st_01a19bee`. Its edit
  scope is only `mods/tactical/**` and a diagnostic note. It must preserve
  leases/restoration and must not load anything into the live client.
- Parent added a deterministic planner regression:
  `Threat_search_leaves_budget_for_an_immediately_available_finishing_cast`.
  On a full board with many mobile enemies, building reply maps can consume
  the entire shared budget before any immediately legal spell is considered.
  Reproduction monitor: `mon_QQ194S433HJK4RYP`, bash `bash_40`.
  Reproduction failed as intended: `Assert.Single` received an empty action
  collection, exit 1. Production correction now caps threat-map work at half
  the shared budget, leaving the rest for action search. An unfinished enemy
  reach map makes every candidate cell dangerous; an unfinished firing check
  is also dangerous. `BudgetExhausted` signals either form of truncation.
  Targeted planner/rotation/controlled-turn/movement/revocation validation
  started as monitor `mon_TFARHJB189Z3A9MZ`, bash `bash_41`.
  No passing result or live validation is claimed yet.
- Follow-up: all 53 targeted planner/rotation/controlled-turn/movement/
  revocation tests passed, exit 0, including the previously failing finishing
  cast regression. Full suite started with TRX
  `combat-results/combat-budget-recovery.trx`, monitor
  `mon_DJ670M5P2Z9B4F56`, bash `bash_42`. Its Lua inventory failure has already
  been reported; final totals remain pending.
- Child `st_01a19bee` completed initial tactical investigation and tests:
  eight existing tactical tests pass; release build exit 0. It made no
  tactical source correction, only
  `docs/codex/TACTICAL_DISCOVERY_BLOCKER_20261102.md`. The actual module
  directory is `mods/native-tactical`, not `mods/tactical`.
  Child resumed to implement a bounded read-only hierarchy/transform capture
  there using existing available bindings, under a valid lease. Runtime
  ownership and transform placement must be established before suppression
  logic changes; parent will perform any live loading and validation.
- Memory save was attempted, but the memory tool refused because
  `reference/givre-fm-parity.md` has someone else's uncommitted changes.
  No memory files or commits were altered to bypass that refusal.

## Class and element audit

The user requested class/element-aware automatic configuration, including Sram,
Roublard and Zobal, in addition to kiting, heals, shields and utility spells.
This is pending scope, not delivered class coverage.

Read-only evidence:

- `CombatProfile.cs:83`: current generic rotation configuration.
- `CharacterSpellsReader.cs:19,36`: learned IDs and grades are available.
- `CharacterInfoReader.cs:142`: class identity is available.
- `GameNameResolver.cs:550,646`: legacy aggregation loses per-effect elements,
  conditions, durations and targeting.
- `Combat/DamageEstimator.cs:35`: target resistance inputs exist, but current
  live kill estimates do not pass them.
- `FightStatsReader.cs:10,94`: combat fighter stats drop the full characteristic
  dictionary needed for resistance-aware estimates.
- `StateCriterion.cs:71`: criterion evaluator exists, but no application cast
  path currently consumes it. Unsupported syntax is not proof of legality.
- `LaunchService.Combat.cs:668,694`: bombs are handled as generic summons;
  bomb age, combo, walls and detonation dependencies are absent.
- `server-mapping/fields.json:4542`: visibility updates have a named target.
- `server-mapping/fields.json:2788,5103,7894,7930`: mark lifecycle messages
  exist, but identity/geometry fields still need resolution.

The recovered planner supports only a restricted set of immediate point
effects, selected healing/shielding and straight teleportation. It rejects
nonempty state criteria and unsupported masks or zones. It does not model
Sram traps/invisibility, Roublard bombs or Zobal mask transitions.

Required order: preserve authoritative fighter facts and full spell semantics;
resolve state/mark lifecycle; enforce shared legality at planning and dispatch;
then add tested class-specific transitions. A list of class presets does not
replace those prerequisites.

## Budget correction validation

Full suite after the threat/action budget split: 3,935 passed, one failed,
two ignored, 3,938 total, exit 1. The failure remains
`LuaApiInventoryTests.Inventory_covers_the_current_binding_sources`.
TRX: scratch `combat-results/combat-budget-recovery.trx`.
Targeted combat scenarios: 53 passed, zero failures, exit 0.

The real application build (not just test-linked sources) is now running
through monitor `mon_SVA4QAMVK2038B5R`, bash `bash_43`, with isolated artifacts
under `%USERPROFILE%/AppData/Local/Temp/givre-recovery-combat-app`.
Its outcome is pending. No running application was replaced or restarted.

Follow-up: real application Release build completed successfully, exit 0.

## Preserving observed fighter characteristics

The combat worktree now preserves the decoded show-fighter characteristic
dictionary as an immutable property on `FighterStats`. Existing `with` life
updates retain it. `FightRefreshCharacterStatsEvent` updates that dictionary
for already observed actors before the existing PA/PM/range handlers run.
No synthetic snapshot is created for an unknown actor.

The existing protocol regression now supplies a fire-resistance entry with
base plus equipment and checks its preserved total after a life update.
Targeted protocol/planner/companion tests and an application rebuild are
running under `mon_PXM5D6J5MMGYFD7N`, bash `bash_44`. Results pending.
This only preserves received facts; resistance-aware planner scoring,
complete buff expiration coverage and class mechanics remain unfinished.

Follow-up: 42 targeted protocol/planner/companion tests passed; real application
build succeeded, combined command exit 0.

## Live diagnostic preparation

MCP confirms Bastionbot in game on map 54165809, out of combat, no running script,
1,254,757 kamas. Existing profile has Tactical=true. The expected staged lease
file is currently absent; no lease was fabricated.

Verified diagnostic DLL SHA-256:
`6d13faa2a8a552dd4df3c3b5ab46f23788ca69692a8a1a3af2fd989cc79bd88f`.
Copied to the existing session stage directory as
`DofusNativeTacticalDiagnostic.dll`, leaving the original binary untouched,
so the diagnostic uses the same legitimate lease directory.
Native RELOAD request submitted for `bubble.dofus3.native-tactical`;
response watch `mon_G9F95RE784GQK9W5`, native outbox
`recovery-hierarchy.response`. Its result and actual capture remain pending.

Desktop discovery did not expose the application windows even though the
PowerShell process query confirms both existing window handles and MCP
responds. No desktop input was attempted.

The native response watch timed out. Inspection confirms
`recovery-hierarchy.request` still exists in inbox and no corresponding outbox
response exists. Reload is NOT confirmed; no duplicate request was submitted.

## Additional user-provided references

User supplied Valentin-alix/BillBot, SneakyBass/dfs and AlpaGit/Bubble.D3.Bot.
Three explicit study tasks were appended rather than replacing existing work.
BillBot/dfs background research: `st_01a19bff`, `st_01a19c00`, restricted to
separate evidence notes. Bubble source review completed and recorded in
`BUBBLE_STUDY_20261102.md`, revision
`4cb70b04ad69d7850c76f91f7df108101670d9fc`. It identifies useful per-grade
translation, but also explicit AI exclusions and incomplete simulation
adapters; upstream library presence is not whole-class implementation.

## Resistance-aware planner follow-up

Planner fighter snapshots now carry the immutable observed characteristic
dictionary. The runtime passes the actor's own facts, and candidate damage
and teleport follow-up ranking pass elemental percent resistance and fixed
reduction to the existing estimator. The new two-row regression compares
an otherwise finishable resistant enemy with a genuinely finishable enemy.
Unknown resistance still uses the existing estimator's zero default; results
remain estimates, not certified kills. Buff expiration and all effect
families are not covered by this addition.

Targeted planner/protocol/estimator tests followed by application build:
monitor `mon_KDC2W7922942YHWX`, bash `bash_47`; outcome pending.
No changes have been merged or loaded into the running application.

## Disk-aware emulator investigation

User supplied Keka-Bron/JondoEmu and authorized local tools/emulator experiments
and cleanup of disposable builds, noting low disk space. C: free measurement:
27,431,325,696 bytes. Current owned test artifacts occupy 1,379,447,022 bytes,
app artifacts 721,516,891 bytes; these remain needed for ongoing verification.

The unused, rejected runtime-inspector build cache occupied 36,240,777 bytes
across 121 files. The native descriptor listed no loaded inspector. Removed
only scratch `ulw-givre-100-20261101/native-inspector-build`; command exit 0,
path absence confirmed. Subsequent free measurement: 27,502,034,944 bytes
(free-space delta includes other machine activity, not a cleanup size claim).
Source and current tactical binaries/evidence were preserved.

JondoEmu feasibility child `st_01a19c07` reads primary source remotely and
must not download the client, database or full archives. It will record exact
version/dependency limits and whether a small headless test subset is usable.
No emulator was installed or started yet.

Resistance-aware targeted run reported 36 tests passed, zero failures;
application build in the combined command remains pending.

Follow-up: resistance-aware application build succeeded, combined exit 0.

## Isolated JondoEmu baseline run

After the user explicitly approved using the available 27 GB, downloaded only
source revision `683ab49c73f65b7c4ab7c1134c448f7fa850f6bc`.
ZIP: 59,387,474 bytes; expanded archive entries total: 121,298,610 bytes.
No historical Dofus client, MelonLoader or game redirection was installed.
Archive extracted below
`%USERPROFILE%/AppData/Local/Temp/givre-jondo-lab-20261102/source/`.

Read project references/build hooks and test module initializers before
execution. The selected upstream tests construct fighter/fight objects;
the module initializer disables launcher server contact, disables traffic
logs and adjusts only existing emulator test database journal settings.
The existing source-root discovery uses the isolated checkout's data.

Started Release `Jondo.Unity.Tests` filtered to SramTests, BombRulesTests,
BombExplosionTests, BombWallTests, PullTests, PushDamageTests and
TeleportSymmetryTests. Monitor `mon_PZJ8P9YSKN4T4EVY`, bash `bash_55`.
TRX target: isolated lab `results/jondo-combat-baseline.trx`.
Package restore is running; no pass claim yet. No server listener or game
client was started. Tests describe this emulator, not authoritative current
official-server mechanics.

## All-class steering and emulator prerequisites

User explicitly clarified that this work covers all classes, including Xelor
Telefrags and Eliotrope portals, not only Sram/Roublard. Added explicit todo
items for those mechanics and cross-class transition coverage; the existing
scope remains open.

Initial upstream test run: 61 passed, 11 failed, 72 total, exit 1.
Data-dependent Sram/bomb tests produced empty effects; the isolated
`bases/world.db` was zero bytes. The delivered `datos/world.zip` had not been
extracted by test execution. Extracted its single world.db entry (249,536,512
bytes), only after checking that the destination was empty. No upstream
tests or code were weakened or changed. PowerShell's older ZipFile overload
required ExtractToFile rather than ExtractToDirectory's Boolean argument.

Second run uses installed data and adds XelorParityTests, PortalTests,
PreviousCellHistoryTests and SelatropPlannerTests to the original lot.
Monitor `mon_FPBYNZAE1M41G9T0`, bash `bash_60`; TRX
`results/jondo-data-classes.trx`. Result pending.
Some upstream history tests return early if SpellTable is not loaded; a
passing count alone cannot prove those branches exercised real spell data.

Source review notes XelorParityTests explicitly excludes bounce spell 13292.
Neither that test nor a green portal suite will establish full class support
or live-client compatibility.

Live-state change: PowerShell now reports only GivreClone PID 12796;
Dofus PID 18668 is absent. No process was killed by this recovery. Removed
our still-pending `recovery-hierarchy.request` to avoid delayed reload.
No native reload/capture has been confirmed.

Follow-up: second JondoEmu run completed successfully: 133 passed, zero failed,
zero reported skipped, 22 seconds, exit 0. TRX saved at the declared path.
This includes the original failing tests after installing their delivered
database prerequisite and the additional Xelor/portal/history/planner filters.
Early-return coverage limitations above still apply.

Feasibility child `st_01a19c07` initially ended at 1/4 internal tasks without
the requested final note. Resumed it specifically to finish source tracing
and record the report; no duplicate tests or installs requested.

## Shared utility follow-up

Reference source identifies immediate maximum-life healing as action 1109:
Jondo `Jondo.Unity.World/Combat/EffectSupport.cs:271-272` and
`EffectEngine.cs:5077-5088`. Unlike ordinary healing, this computes a share
of the receiver's maximum HP, not a flat amount scaled by intelligence.
GivreClone currently classifies this family but does not admit it to the
strict planner. Added a regression with observed low life, maximum life and
high intelligence to distinguish the two semantics. Red reproduction monitor
`mon_DNSZ34AT3QKW6B2R`, bash `bash_61`; implementation pending its result.
This is cross-class utility support, not a substitute for the still-open
state/history/portal/mark transitions.

Reproduction result: percentage-heal regression failed with an empty action
collection, exit 1. Strict descriptor admission now includes immediate,
unconditional point action 1109; planner computes maximum-life percentage on
the receiver, clipped to its maximum, without intelligence scaling.
Other state criteria, target-mask and shape restrictions remain in force.
Targeted planner/rotation/controlled/installed-spell tests plus application
build started as `mon_NJSV7KNSPQ2S0AMC`, bash `bash_62`. Outcome pending;
installed spell availability and live behavior remain unverified.

Follow-up: 42 targeted tests passed and application Release build succeeded,
combined exit 0. Full GivreClone suite now runs without rebuilding via
`mon_DRX2Y0W349Y90TVA`, bash `bash_64`, TRX `combat-utilities.trx`.

Additional JondoEmu source-unchanged class tests started:
ClassEffectTests, ClassPassivesTests, ClassStatesPlannerTests, ClassTriggerTests,
BotPlannerTests, PandawaPlannerTests, SteamerPlannerTests, TymadorPlannerTests,
ForgelanceTests, OuginakRageTests and SadidaLifecycleTests.
Monitor `mon_YPR5V320HJC2D820`, bash `bash_63`, TRX `jondo-multiclass.trx`;
results pending. These expand reference evidence, not GivreClone's delivered
class coverage.

Follow-up: additional multiclasse reference run passed 117 tests, zero failed
or reported skipped, 20 seconds, exit 0. Combined with the earlier disjoint
133-test selection, the two source-unchanged reference runs report 250 passing
test cases. This is not the full JondoEmu suite; early-return limitations and
official-server compatibility caveats remain.

## Current implementation ownership

Latest GivreClone full suite: 3,938 passed, one existing Lua inventory
failure, two ignored, 3,941 total, exit 1. `combat-utilities.trx`.

Child `st_01a19c18` owns only CombatTurnPlanner.cs, SpellDescriptor.cs and
CombatTurnPlannerTests.cs in the combat worktree to implement bounded generic
forced-movement transitions and their attack follow-ups. Parent does not edit
those files during its run. Collision damage, marks/portals/triggers and
nonempty criteria cannot be guessed; actual supported scope will be reported.

Parent found that new strict `GetSpellDescriptor` inherited legacy grade
fallback: an absent grade could select another grade, including maximum for
grade 0. Its only production callers are new automatic rotation/planning.
Added real installed-data regression for absent grades 0 and 999 while
checking grade 1 is actually available. Reproduction running as
`mon_AT9T0GX1EHRC6VZH`, bash `bash_65`.
Child told not to use shared build artifacts until parent sends GRADE_READY.

Exact-grade reproduction: both grade 0 and grade 999 returned grade 3 for
installed spell 32443, two failures, exit 1. Parent changed only
GetSpellDescriptor to return an exact dictionary match or null; legacy
SpellCombat/damage/zone fallbacks are untouched. Installed-data regression
suite now runs as `mon_P7SB2NS435DJQAFD`, bash `bash_66`.
Do not release shared artifacts to child until that run exits.

Follow-up: four installed-data tests passed, zero failed, exit 0. Sent
GRADE_READY to child; it may now use the shared isolated build directories.

## New live session observation

MCP now reports Bastionbot already connecting in Dofus PID 7156; parent did not
launch it. Its staged native descriptor exists and confirms PID 7156 with
ABI8. No request or lease for PID 18668 was reused. Registered native create
watch on the real stage `NativeTurbo/7156/tactical-map.lease`:
`mon_0F4FE6TEY1DDTD8D`. No lease was fabricated and no new native load was
requested while map readiness is unconfirmed.

Follow-up: map-ready file watcher for PID7156 timed out without confirmation.
Do not claim the client entered a map, or manufacture a lease to collect
diagnostics.

## Cross-class evidence and observed shields

Created `CLASS_TRANSITIONS_20261102.md` from primary local source inspection:
explicit shared-transition dependencies, Xelor history/Telefrag scoping and
Eliotrope network/projection rules, plus known inference/coverage boundaries.
It corrects obsolete source locations in several feasibility-note links.
The feasibility study is complete; implementation/class live parity remains
open.

Parent added characteristic96 (`FightStatsReader.IdShield`) to existing
protocol decode regression and passes observed nonnegative shield points into
planner fighters. Refresh characteristic events already update the retained
dictionary. Dedicated shield-loss and expiry event coverage is not proven.
Child `st_01a19c18` informed; it will validate the protocol case with its
planner checks. Parent has not run a concurrent build.

Measured isolated Jondo lab footprint: 709,966,467 bytes including source ZIP,
expanded source, database and local outputs; C: free 26,499,833,856 bytes.
No new client or global emulator service installed.

Forced-movement child returned implementation in its owned planner,
descriptor and test files. First targeted run: 44 passed; first application
build exit 0. Final targeted run/build were still pending in its completion;
requested their exact handles/results without starting duplicate commands.

Parent source review confirms branch-local fighter positions and forced
push/pull/swap transitions, but they require
`CombatTurnSnapshot.ForcedMovementSafe=true`. The default is false and the
live runtime does not establish this fact yet. Therefore these transitions
are NOT currently enabled for live automatic combat, regardless of pure
scenario test results. Required facts include movement-blocking states,
carrying relations, portals/marks and movement triggers. Do not bypass the
gate by asserting that missing observations mean safe.

A second durable-memory write attempt was refused by the same unrelated
dirty `reference/givre-fm-parity.md`; no foreign changes were committed,
discarded or bypassed.

## Shield absorption regression

The final forced-transition selection passed 67 tests and its application
build exited 0. Subsequent full suite reported 3,958 passed, one existing Lua
inventory byte-hash failure and two skipped (3,961 total), exit 1.
TRX: `combat-forced-transitions.trx`. The unrelated CRLF-sensitive inventory
failure remains unchanged.

Found that characteristic96 reached planner snapshots but absorption remained
stale after LifePointsLost. Catalog field2 is shield_loss, distinct from loss6
and permanent_damages1. Extended the decoder and runtime policy call.
Regression reproduced two failures: shield80 stayed80 instead of50 or0 after
confirmed absorption30 or150; negative absorption already passed.
The policy now decrements only an already-observed shield characteristic,
clamps at zero and preserves independent life/erosion handling. It does not
invent a shield observation when absent.

Post-fix F02/protocol/planner/controlled selection: 93 passed, zero failed,
zero skipped, exit0 (`mon_6ADGY83ZT7VB4XV0`, bash70).
Application Release build succeeded, exit0:
`mon_9G1PF5J5FPSRVG9R`, bash71.
Shield expiry and all-class lifecycle modeling remain unverified.
Latest read-only live observation: Bastionbot PID7156 still connecting, map0,
not in combat; no native map lease or live combat proof obtained.

Read-only child `st_01a19c29` maps existing protocol/runtime lifecycle events
for states, carrying, invisibility, marks, portals and movement histories.
No file ownership or game actions delegated; findings pending.

## Restart and server selection correction

User clarified that the preceding connection state was an unselected server,
then restarted the client. Previous map0 observation did not diagnose that
screen and should not have been described as a connection fault.
Read-only MCP now confirms Bastionbot in game, PID16248, map54165809, full life,
not in combat. The new PID has its own real map lease. Native logs confirm
the original tactical module loaded but still fails to resolve map_54165809.
Preparing the previously tested diagnostic module for this new stage; no
claim of successful reload or fixed tactical rendering yet.

Lifecycle investigation `st_01a19c29` completed read-only. It found named
visibility fields and existing relocation observations, but no retained
visibility, carry relationship, movement history or mark/portal lifecycle
in the planner. Many removable-effect and mark identity fields remain
obfuscated. Visibility tracking is the smallest catalog-defined runtime
unit; it cannot establish ForcedMovementSafe on its own.

Reload confirmed in PID16248: the copied diagnostic DLL SHA256 is
`6d13faa2a8a552dd4df3c3b5ab46f23788ca69692a8a1a3af2fd989cc79bd88f`.
Native outbox `recovery-hierarchy.response` returned OK for the tactical ID.
Host log at timestamp1793621634 records diagnostic BEGIN under actual map
54165809 and the fresh owned lease; END reports577 renderers,128 sampled,
24 paths emitted. MapController is a child of identity-transform
`MapRoot_54165809`, with sibling MapAnimatedElements, MapParticles and
PostProcess(Clone). Mesh renderer samples instead have top-level
`MapBackground` ancestors. Sampling is explicitly not ownership proof.
No tactical root fallback or global suppression was enabled.

Child `st_01a19c2e` now owns only native-tactical source and its separate
tactical note to implement the evidence-supported increment, or extend the
bounded diagnostic for missing ownership/frame facts. Parent retains live
reload control. Compilation and visual validation are still pending.

## Invocation occupancy prerequisite

Lifecycle inspection exposed another concrete protocol defect:
FromActorPosition mapped an omitted cell_id to -1 even when disposition was
present. Proto3 omits zero; OnSummoned then skipped occupancy registration
for a summon in cell0, including bombs.
Added catalog-built regression cases for actor and grouped summon shapes,
each with empty versus missing disposition. Red run
`mon_0G9PWRMZ783H2X1B`, bash73: two failures (expected0, actual-1), two
passes (missing disposition remains unknown), exit1.
Decoder now distinguishes absent disposition from omitted cell scalar.
Targeted summon/monster/planner/controlled/F02 validation pending as
`mon_A2AJ89SV9W4QJGZH`, bash74. This restores occupancy observations only;
it does not implement summon-specific states, bomb combos or portal rules.

First fix still failed the two cell0 cases (85 other targeted cases passed):
the generic Sub helper requires Message!=null, whereas an empty protobuf
length-delimited disposition has no parsed Message. Changed the lookup to
test the actual disposition field with WireType.Len, then read optional
children. This preserves present-empty versus absent without altering the
shared helper. New validation: `mon_KJM7ASCD4YNBJZ3B`, bash75.

Follow-up: all87 selected summon/monster/planner/controlled/F02 tests passed,
zero failed or skipped, exit0. Real application Release build started as
`mon_01TTC5TZGXG9JMDD`, bash76. This increment is still in the sibling combat
worktree, not the running primary application.

Follow-up: sibling application Release build succeeded, exit0.
Integration started with the independent protocol increment: FightStatsReader,
FightLifePolicy, LaunchService.LotF plus F02, Patch361112 and SummonReader
regressions are now applied in the primary worktree using apply_patch.
Before transfer, read-only Git checks confirmed no tracked combat/test
differences between sibling base e66edcc70fc39e19f58424da51ab6bab73df2991
and primary b4d28a80d71e6b98a95f6bb1d17c6dbead80b660 for these paths,
and no conflicting primary dirty files. Other primary edits were preserved.
Primary targeted validation pending: `mon_81BR6EY9B5NK1F48`, bash82.
Planner integration and running-application deployment remain pending.

Primary protocol selection passed65 tests, zero failed or skipped, exit0.
Integrated the next source tranche: SpellDescriptor, automatic rotation,
AutoElements profile/clone/import and exact-grade descriptor loading, with
their rotation/import/installed-data tests. Validation pending:
`mon_NDS5X3RNWYWRB1XA`, bash87. Runtime turn planner is not transferred yet.

Second tactical diagnostic artifact passed11 native tests and release build,
SHA256 `fb8756b6c1dc4181c272b2d6c8067052d3114dc0656e402b124b7254c9bab01a`.
Copied to distinct Diagnostic2 filename; PID16248 outbox recovery-fields
returned OK. Live fields confirm MapController.dhcf -> service epf, service
<doqb> -> ClientMapData id54165809 and static <doqc> -> empty parentTiles.
Service method xkz has signature Vector2(Int32); this signature alone does
not establish its coordinate semantics. Captured ClientCellData indices0,
14 and559 include actual cell numbers. Further ownership/frame interpretation
is required; production root admission and suppression are unchanged.

Durable memory creation was attempted again and refused because unrelated
reference/givre-fm-parity.md remains dirty. Its changes were not touched.

Next primary tranche is now applied: CombatTurnPlanner, its scenario tests,
turn-loop acknowledged one-action execution, companion resource/release
handling and observed-characteristic refresh. Descriptor/rotation selection
passed32 tests, exit0, before this tranche. Integrated planner validation
pending as `mon_A5T671BR8194Q1KD`, bash89.
No primary process restart or deployment performed; live combat remains open.

Full second diagnostic inspection found the12-object limit was consumed
before service.doow (type fsm, runtime epk) could be inspected. Its reference
was explicitly observed in FieldObject1; no ownership inference follows.
Parent changed only diagnostic seed priority: optional known epk reference
first, map accessor before controller, existing limits unchanged. No unknown
conversion method invoked, no root admitted and no suppression changed.
Native tests/release build pending as `mon_K39TPR11FXPQK7QT`, bash90.

Integrated planner selection passed81 tests, zero failed or skipped, exit0.
Primary application Release build succeeded, exit0 (`mon_P01CEHSK01V1CJEV`,
bash91). Full primary suite started as `mon_8W4DP5A7HG3T9V07`, bash93,
TRX primary-combat-integration.trx.
It exposed one directly caused contract failure: RequiredMapping omitted
LifePointsLost.shield_loss. Added that machine-consumed required field in
both worktrees without changing the test. Full run still pending; rebuild
and rerun after its exit are required for the changed manifest.

Targeted native diagnostic passed11 tests and Release build, exit0.
Diagnostic3 SHA256:
`f5c662ff2e6b23b16fdc162143e7ba2f1ba4681c6205701dfba1b9b10402ed4f`.
PID16248 acknowledged recovery-render-service reload OK. New capture shows
epk is a service container, not a proven rendering owner. Its dependencies
include fyb via several classes, but no admitted MapBackground reference.
Keep discovery/suppression unchanged until a concrete owner is resolved.

Full primary suite after required-field fix passed:3966 successful, zero
failed, two existing skipped,3968 total, exit0,79 seconds.
TRX primary-combat-mapping-fixed.trx. This supersedes sibling CRLF inventory
failure for the integrated primary tree, without modifying that inventory.

Live inspection finds Shadowbot's socket session actively running a script.
Do not restart the shared running application implicitly to deploy combat
changes; no live planner certification has occurred.
Parallel child st_01a19c44 owns the managed visibility observation unit and
its own tests/note. Parent now owns only native tactical and recovery docs.

Changed investigation angle from further blind object-graph expansion to
static native analysis. No REA tools are registered in the current tool
catalog and no rea executable is on PATH. Version-pinned npx rea-agents6.8.0
doctor finds existing Ghidra12.1.2 and Java21/native authority usable; overall
doctor exits1 for unrelated missing providers/configuration drift. No client
configuration changed. CLI-specific command help discovery is in progress.
The retained October18 Ghidra project targets SHA e08da74c..., not the pinned
current70b3861d... image; it cannot supply current method-address evidence.

REA CLI command help establishes inspect-il2cpp-metadata supports metadata39
and matching PE native address linkage; decompile accepts a binary path,
procedure/address, snapshot and provider. Started read-only current-client
metadata/native linkage as mon_Q1102YC33CXTMK5R, bash100. Requested complete
Evidence bundle below Temp/givre-tactical-il2cpp-20261102 and method seeds
Temp/givre-tactical-seeds-20261102.json; no artifact or address claim yet.
No Ghidra import, game execution or global configuration edit started.

REA metadata linkage returned resource_constraint:392887 methods exceeded
the retained complete-report budget; no complete inventory or native
identity/linkage was retained. Did not repeat that request.
Added bounded ordinary static inspector inspect-map-services.mjs beside the
existing verifier, with both pinned SHA checks and explicit class limits.
Its initial field/token inventory passed exit0 for epf,epk,fyb and
InteractiveCellManager. This is ordinary script evidence, not REA Evidence.

Native linkage attempt exposed a wrong assembly assumption: the unique
Assembly-CSharp.dll code-generation module has only4 methods, so it cannot
own the observed tokens. The inspector now resolves the metadata image
containing epf before selecting its native table. Run pending
mon_1TZNVVQ346QP5F6K,bash104; no method address is claimed until it passes.

The first metadata-image query also failed because it used pre38 section
layout. Consulted Il2CppInspectorRedux's actual Il2CppGlobalMetadataHeader,
Il2CppSectionMetadata, Il2CppImageDefinition and IIndexType sources.
Version39 uses12-byte section records and compressed UInt16 type indices;
the pinned image rows are36 bytes. Corrected these concrete layout facts.
Run mon_WST581CVVZJ6KGES,bash105 passed exit0: owner Core.dll, unique native
module raw96319920,113524 method pointers. Method-token owners are asserted.
Changed report formatting to one record per field/method to avoid PTY
single-line truncation; readable output run mon_2W6GMNF05EBFQ131,bash106.

User explicitly approved stopping Shadowbot's script and restarting GivreClone.
MCP stop_script on account264458b7d4924d36a9dd98ed9ba9e594 returned accepted;
subsequent state confirmed ingame,not in fight,scriptRunning=false.
Five configuration files were copied to a new owned backup directory
Temp/givre-before-combat-deploy-20261102 without exposing their contents.
Current old application PID12796 executable is under
Temp/givre-recovery-tactical-artifacts/bin/GivreClone.App/release/.

Visibility child returned47 passing targeted tests and build0, but every raw
state was Unknown, so the handoff was not deployable. Parent found explicit
BillBot common.proto enum INVISIBLE0/DETECTED1/VISIBLE2, added that reference
mapping plus separate authoritative position confirmation and transition
coverage. Full primary run passed3981,zero failed,two skipped,3983 total,
exit0,89s (mon_5F05G2WJHBXK5HEG,bash113). Later small ingress confirmations
and RequiredMapping.Extra additions need their final check.
Deployable primary Release build pending mon_1FHEWAD3D0XEPQ0H,bash115.

Native epf.xkz resolved to0x181839920 from Core.dll token0x0600da16.
First REA Ghidra import timed out330000ms, no pseudocode. No residual Java/
Ghidra process was found by corrected process inspection. Retried with the
provider-recommended REA_GHIDRA_STARTUP_TIMEOUT_MS=1200000, watchdog1500000:
mon_Y7PJT3F79V2GDDH1,bash112. Analysis pending; no coordinate semantics claimed.

Before closing PID12796, fresh UI and MCP observations showed user-changed
sessions: Bastionbot and Shadowbot offline, Sylvanbot actively running a socket script
and Valrianess connected. No close was dispatched. Asked whether the granted
restart should also interrupt Sylvanbot; preserve that new work until answered.
This is a changed live scope, not another request to stop Shadowbot.
Final selected visibility/mapping/planner checks pending mon_S05716ZTSVZ2TQ05,
bash116; primary deploy build pending mon_1FHEWAD3D0XEPQ0H,bash115.

Final primary Release build succeeded exit0. Selected visibility/mapping/
planner/placement tests passed64,exit0. Added one neighboring end-to-end
reducer-to-planner theory checking visible2 permits a real attack while
invisible0,detected1 and unknown987 do not. Its selection passed51 tests,
exit0 (mon_2GDNJFHSN5GB2DH3,bash117). No deployment until Sylvanbot decision.

Ghidra retry also timed out after1200000ms without pseudocode. Corrected
process check found no remaining Java/Ghidra process. Do not retry another
unchanged whole-client import. Switched to bounded ordinary disassembly:
installed Python already has Capstone; pinned inspector now extracts256
bytes only for resolved epf.xkz. Extraction mon_ZA9KS6PKWTYZPXCS,bash119
pending. This is not REA-derived pseudocode or runtime execution.

Bounded Capstone disassembly now covers epf.xkz at0x181839920 through ret/
failure trap0x181839a5c. It calls Core.dll oe.oxo at0x182e880f0 with cell
integer, preserves its packed two-float result, and in one branch adds an
Int32 read from returned object+0x2c to the second float. Another branch
returns the original pair. The optional object's semantic field and branch
condition are not identified yet, so do not call this proven elevation or
assume it matches the current hardcoded overlay coordinates.
The bounded inspector now resolves owning metadata image, token-native table
and direct callee identity with pinned hashes. An initial helper declaration
order error was corrected; final run exited0.
No game method was invoked and no client files were changed.

Further bounded disassembly resolves base conversion oe.oxo:
X uses integer sum*43 minus float580.5; Y uses integer difference times
float21.5 plus float483.75. Constants were read from the pinned PE at
0x18596f0f0,0x18596c704 and0x18596f0e4, respectively. Its input packing comes
from oe.owu at0x182e86d00, a560-cell lookup, not an arbitrary world vector.
These base constants corroborate the existing overlay constants; they do
not establish the optional Y adjustment or which scene root owns scenery.
epf field+0x118 is ClientMapData; +0xa0 is interface fpl. Never read these
as guessed renderer pointers. Inspector final run succeeded after adding
these narrowly selected dependencies.

Scenery ownership follow-up: ub declares four GameObject/Transform pairs at
0x30/0x38,0x40/0x48,0x50/0x58,0x60/0x68; uj declares two at0x10/0x18 and
0x20/0x28. These are candidate fields, not verified live scene ownership.
Bounded disassembly of their constructors only establishes pool allocation
and dependency assignment. ub.bzv consumes map data and calls a shared helper
three times with layer selectors0,1,2; it does not identify named scene roots.
ub.qih at0x181f8bff0 allocates an object, obtains three components, configures
one, stores component references on the returned component at+0x20,+0x28,
+0x30, and returns it. uj.qjy allocates an object, parents its transform to
uj+0x18, configures it, and returns a component. Neither observation proves
that hiding the whole owner preserves tactical overlays or interactive objects.
Next useful trace: the callers assigning the returned components to ub/uj
fields, then the exact component types behind the three ub.qih metadata slots.
All runs used the pinned read-only inspector and completed with exit0.
No runtime mutation or restart was performed.

Factory-reference follow-up: the executable-section scan finds a tail jump
at `0x1820c2d33` to `ub.qih`. The closest preceding registered method is
`Core.Rendering.MapElementsPool.qkc`, starting at `0x1820c2d20`.
The bounded instruction window loads `rcx` from `[rcx+0x20]`, checks it for
null and tail-jumps to `ub.qih`. This identifies a pool-to-owner forwarding
path, not the scene branch to hide. Aligned pointer references were found at
`0x186933018` (ub.qih) and `0x186933388` (uj.qjy); these alone can be code
registration entries and do not establish delegate construction. Both scans
completed with exit 0. The next read-only scan covers RIP-relative LEA
references to distinguish actual factory-address loads from registration.

Pool constructor extraction completed with exit 0. Both
`MapElementsPool..ctor` (`0x1820c2cd0`) and `MapParticlesPool..ctor`
(`0x1820c30b0`) retain their second argument at instance offset `0x20`
after a base-constructor call (`48 89 7b 20`). The preceding factory scan
found no RIP-relative LEA candidates. These constructors do not assign the
GameObject/Transform branches themselves. Continue with the component
metadata slots in `ub.qih`, not by treating the pool as a scene root.


## 2026-11-02 resume: visibility check and flee threshold
- The visibility/planner selection passed 65/65. The full suite passed 3985 with 2 skipped and 0 failed.
- combat-planner-slice1 contains nothing missing from the main worktree. The main worktree is a superset (it adds visibility gating and more tests).
- Added CombatProfile.FleeBelowHpPercent (0 = off). It is exposed in the Bots > Combat settings card (fr/en/es), in the import fields and in the control-pipe bounds (0..100), and is passed to CombatTurnSnapshot.
  Below the threshold the planner multiplies expected incoming damage by 4 and weights enemy distance 0.5 per cell instead of 0.02.
  A new theory (thresholds 0/50/70 at 60% HP) shows the same exposed attack is kept above the threshold and replaced by a retreat below it.
- Release build passed with 0 errors. The full suite passed 3988 with 2 skipped and 0 failed.
- Not validated live: GivreClone PID 12796 was not restarted, because Sylvanbot was running a user script. I asked the user whether to restart.
- Still unsupported (unchanged, exact): Sram traps/invisibility beyond the "not targetable" gate, Roublard bombs/combos, Xelor telefrags/history, Eliotrope portals, non-axis or partial push/pull and collisions, and companion turns. Enemy reach is estimated, and shield pools and tackle are unknown without observed escape costs.

## 2026-11-02 marks (glyphs/traps/runes): what the real logs show
- Scanned all 30 transport logs under TestRuns for GameActionFightEvent mark_cells (#9) and unmark_cells (#30).
  Only 8 placements exist, all from 2026-11-01 18:56. Every one is mark_type 4, authored by our own Huppermage (815878832421), spell 13686, one cell (MarkedCell #4 = cell, e.g. 261/290/288).
  There are also 3 unmark_cells frames, with mark_id 1/2/3.
- The placement carries a per-fight counter at FightMark wire #17 (1..8), which looks like the mark id. fields.json names #17 as nothing (FightMark has no mark_id), so placement and removal cannot be paired by name.
- No enemy mark, no Sram trap and no glyph has ever been captured.
- Implemented: MapFightReader.ReadMark/ReadUnmark (mark_type, author_id, MarkedCell.gask as the cell) and a `[combat] marque posée/retirée` log line. The RequiredMapping manifest was regenerated, and there is a test of the observed rune shape.
- Deliberately NOT implemented: trap avoidance in the planner/pathfinder.
  Reasons: (1) there is no evidence of which mark_type means trap or glyph; (2) a mark cannot be removed reliably without a named id; (3) the trigger effect and trigger zone are unknown.
  Blocking cells on guesses would wrongly shrink movement for every Huppermage rune. Next step: capture one fight against or with a Sram or Feca with these logs on.

## 2026-11-02 continuation: retreat before firing and scenery components

- Rechecked the current primary tree rather than rerunning the interrupted
  original agents. Initial planner/rotation/visibility/forced-move selection:
  78 passed, zero failed or skipped.
- Added `Never_approach_can_retreat_to_clear_a_firing_line_before_casting`.
  It failed before the correction and passed afterward. The planner had used
  `CanReposition` to disable all pre-cast walking for `NeverApproach`, despite
  its per-step policy allowing retreat. Pre-cast origins now come from `Reach`,
  which still enforces passive style, terrain, occupancy, tackle and
  non-decreasing enemy distance on every step.
- Planner and movement-policy selection: 57 passed. Full primary Release
  suite: 3,992 passed, zero failed, two existing skips, exit 0.
  Artifacts: `%USERPROFILE%/AppData/Local/Temp/givre-continuation-20261102`.
  This is not live certification or deployment. Existing compiler/analyzer
  warnings remain. The tool LSP bridge still cannot locate csharp-ls.
- A bounded Capstone disassembly of the pinned `ub.qih` factory identified
  three MethodInfo operands. The read-only pinned inspector now decodes
  their MethodRef specifications rather than inferring component types:
  - `0x186be2990`, spec 129426: `GameObject.AddComponent<MeshRenderer>`.
  - `0x186be2778`, spec 129425: `GameObject.AddComponent<MeshFilter>`.
  - `0x186be2360`, spec 129423:
    `GameObject.AddComponent<Core.Rendering.GameObjects.MapElement>`.
  The inspector completed with exit 0. This is ordinary static binary
  evidence, not a REA pseudocode result, a live capture, or proof that an
  arbitrary MeshRenderer belongs to the current map.
- The first scratch stdin decoder finished the relevant disassembly but
  failed on an unrelated multiline JSON record later in the inspector
  stream. Restricting it to scenery-initializer records fixed that parser;
  the rerun exited 0. No game method was invoked.
- Next static evidence is the exact `MapElement` field and ownership model.
  Its fields are now included in the inspector's bounded class inventory.
  Native module loading, renderer suppression and root admission remain
  unchanged.
- Memory saving was attempted through the memory tool and was refused
  because `reference/givre-fm-parity.md` still has unrelated uncommitted
  changes. Those changes were left untouched. No commits or pushes made.

### Native scenery follow-up

- `MapElement` declares exactly `meshRenderer` at offset `0x20`,
  `meshFilter` at `0x28`, and `elementTransform` at `0x30`; the factory's
  stores therefore match these fields. The bounded inspector run exited 0.
- `ub.hni` is another component factory. `ub.qii`, `ub.gvh` and `ub.eiu`
  acquire from the pool and call native `0x18352a6b0` with layer selectors
  0, 1 and 2. Their bounded windows do not establish the scene-root assignment.
  The next extraction decodes the concrete method specifications supplied
  at `0x186bc19f8` and `0x186bc17e0`.
- No exact `MapBackground` literal was found by the pinned string-index
  scan. Widening to short fragments found `Background` at literal index
  6715, slot `0x186c14658`, with code candidates `0x185572d99` and
  `0x1855738b9`. This does not identify map ownership and does not justify
  root admission. All these read-only inspector runs exited 0.
- Recovered the framed local control-pipe client and performed only
  `list_bots`. Current observation: one unnamed session connecting at
  PID 10648, Valrianess in game without a script or fight, others offline.
  No restart, disconnect, purchase or fight was requested by this probe.
- Concrete merged-layer MethodRefs decode to `ub.qij` with arguments
  `ClientMergedMapElements` (spec 137041) and
  `ClientIsometricMergedMapElements` (spec 137040). The call site uses native
  `0x18352a6b0`; it calls `ub.qik` (`0x181f8c470`) before dispatching
  element placement at `0x18352a830`.
- The complete selector branch of `ub.qik` proves layer 0 returns
  instance offset `0x38` (`dgii`), layer 1 returns `0x48` (`dgik`), and
  layer 2 returns `0x58` (`dgim`). All three fields are declared
  `UnityEngine.Transform`. This establishes the static layer-to-field
  relation, not the identity or lifetime of a live `ub` service instance.
  The extraction exited 0. A bounded metadata scan for concrete fields of
  type `ub`/`uj` is the next ownership step.
- That scan resolves the ownership path: `epf.doqa` has concrete type `ue`;
  `ue.dgjp` is `ub` and `ue.dgjr` is `uj`. `ue.dgjn` is a
  `MapController` reference at offset `0x20`, providing a potential live
  backlink check. `ue.dgjm` is a GameObject at `0x18`; its scene role
  remains unverified. The inspector's `ue` field inventory exited 0.
- A separate worker owns only `diagnostic.rs` and its new capture note,
  adding a targeted read-only traversal through this typed path. It must
  preserve existing map/lease validation and cannot admit a root or change
  suppression. No native diagnostic deployment has happened yet.
- Latest local control read shows Sylvanbot and Valrianess in game without
  scripts, remaining accounts offline. This observation supersedes the
  prior connecting session; no session was stopped or restarted.

### Targeted live owned-branch capture, PID 20192

- The diagnostic's private Cargo run passed 12 tests, zero failures or
  ignored tests, and Release build exited 0 without Cargo warnings.
  Artifact size 311,296 bytes; SHA-256
  `4aba293f4c30205b81adab4772c35ec7584bd7912de805103c7cf1c97cc2cb53`.
- Connected only Bastionbot on Mikhal, map 54165809, without a fight or script.
  The initial control call exceeded the client's five-second timeout but
  did launch Dofus; it was not retried. Server selection was verified by
  screenshots before selecting Mikhal and entering the game.
- Copied the verified artifact under a separate name,
  `NativeTurbo/20192/DofusNativeTacticalOwnedCapture.dll`, leaving catalog
  pins and the app-deployed DLL untouched. Native mailbox request
  `owned-branch-capture-20261102-20192` returned `OK` with the tactical
  module identity. The existing fresh app-owned map lease was used.
- `native-host.log` timestamp 1793632213 confirms:
  - `accessor.doqa` runtime class `ue`; `ue.dgjn` is exactly the current
    `MapController` object (`Some(true)`).
  - `ue.dgjp` runtime class `ub`.
  - Layer 0 `dgih/dgii`: `MapBackground`, 34 children.
  - Layer 1 `dgij/dgik`: `MapMiddleground`, 489 children.
  - Layer 2 `dgil/dgim`: `MapForeground`, six children.
  - Every GameObject Transform equals its paired stored Transform.
    All three are separate top-level roots with identity local/world
    transforms, not children of the controller root or `parentTiles`.
  - Fourth pair `dgin/dgio` is an empty `MapElementsPool`, not a visual
    branch to suppress.
  - `ue.dgjm` is `MapRoot_54165809`, with controller, animated elements,
    particles and postprocess children. It is separate from the three
    static scenery branches.
- Complete `OwnedBranches BEGIN` through `END` was read from the host log.
  A secondary PowerShell log watcher failed to parse its shell-quoted
  command; direct response/log reads provided the evidence above.
- A worker now owns the native correction using only this verified branch
  set, typed field traversal and controller backlink. No claim of visible
  grid, disable restoration or transition cleanup is made yet.
- Old source `tools/ExternalSourceBridge/candidates/3.7.7.6/non_obf/game/common.proto`
  documents GLYPH=0, TRAP=1, WALL=2, PORTAL=3, RUNE=4 and named `mark_id`.
  This is versioned reference evidence, not a current wire-ID mapping.
  Current `data/fields.json` still lacks the observed wire-17 mark identity;
  no trap avoidance was enabled from these old values.

### First visible owned-layer grid and restoration

- Owned-root correction passed 14 native tests, zero failures/ignored, and
  Release build exited 0 without compiler warnings. DLL size 313,344 bytes;
  SHA-256 `116529043282d9c7e403e8bb1a65aff3fa3d79f93dc5b175600af6bf1191e7f0`.
  Parent reviewed ownership, retention, suppression-after-build and cleanup.
- Fresh MCP state before loading: Bastionbot PID 20192, map 54165809, in game,
  no script or fight. Copied the checked DLL as
  `NativeTurbo/20192/DofusNativeTacticalOwnedFix.dll`; the distinct live
  mailbox reload request returned OK. Catalog pins and the app-delivered
  DLL were not replaced.
- Host log timestamp 1793633176:
  `verified map 54165809 owned layers ["MapBackground", "MapMiddleground", "MapForeground"]; 560 native tacticalCell sprites constructed`.
  Cell 0 read-back was local `(-580.5, 483.75, 0)`, non-null expected sprite
  matched, renderer enabled. Screenshot frame-7 independently shows the
  grey isometric tactical board and obstacles, with scenery hidden and
  player, monsters, particles, chat, minimap and other UI retained.
- Foreground F8 produced `Native tactical mode disabled` at timestamp
  1793633262. Screenshot frame-8 shows scenery restored, without the grid.
  The game's Options window was also present; whether F8 caused that or
  concurrent user input did was not established.
- Before re-enabling or changing maps, the window disappeared from visible
  enumeration. Process 20192 remained responding and MCP still said in game.
  Restored only that minimized test window after acquiring foreground
  control. New screenshot frame-9 shows windowed display mode, a different
  Options page and a changed character position.
- No further mouse/key input was sent after observing those changes. Asked
  whether Bastionbot is currently in use; until answered, leave the client
  untouched and preserve Sylvanbot/Valrianess sessions. Re-enable and map-change
  lifecycle verification remain open. Do not label tactical work complete
  or update shipped pins from these two successful checks alone.
- Scoped native whitespace check exited 0. No commits or pushes.

### HDV queued-mutation identity correction

- Live inputs remain paused after the unanswered question about current
  use of Bastionbot. This increment was entirely local; no trading command,
  purchase, restart or game input was issued.
- `HdvViewModel.Gated` previously serialized operations but did not retain
  the selected PID/profile while waiting. The new regression tests reproduced
  three invalid dispatches: changed PID, changed profile, disconnected PID.
  Baseline harness result: three failed, two passed.
- The gate now retains PID, scoped profile key and a session epoch before
  waiting, then checks them under the acquired semaphore immediately before
  dispatch. PID/profile changes advance the epoch, so returning to the same
  selection does not revive queued work. Cancellation is checked again under
  the gate, and rejection/exception still releases it.
- Manual repricing and automatic cycles now pass their captured PID rather
  than reading mutable `_pid` inside the delayed operation. Sale/reprice
  tax receipts retain the initiating PID and character name. Automatic
  cycles stop after their session epoch changes; a sale also rechecks its
  session after awaiting seller-mode setup.
- Actual test code is in `GivreClone.Tests/HdvMutationScopeTests.cs`.
  Seven deterministic tests passed, zero failed or skipped, exit 0, using
  the real aliased application ViewModel and its semaphore. Tests use task
  completion signals/cancellation, not sleeps or polling.
- Full repository test compilation stopped before these tests ran because
  concurrently added `TeamIntentBoardTests.cs:20,32` had CS8752 errors
  (`new(...)` targeting a params-array argument). Those unrelated edits
  were not changed, excluded, weakened or skipped in the repository.
  A separate owned scratch test project links the exact HDV test source
  and references the real application:
  `%USERPROFILE%/AppData/Local/Temp/givre-hdv-scope-harness-20261102/HdvScopeHarness.csproj`.
  It compiled the application and ran only this increment's seven scenarios.
  Artifacts: `%USERPROFILE%/AppData/Local/Temp/givre-hdv-scope-20261102`.
  This does not substitute for an eventual full repository run.
- `csharp-ls` now launches but crashes while recursively scanning the
  inaccessible unrelated `artifacts/kourial-tools/pypdf` directory.
  Cargo/.NET compilation was used as available verification; no LSP
  diagnostics success is claimed. Existing compiler warnings remain.
- Scoped `git diff --check` passed. No commits, pushes or deployment.
  Broader spending authorization/tax preflight and live HDV validation
  remain separate open tasks.

### Current combat admission and shared-worktree validation

- A later full `GivreClone.Tests` Release run reached compilation after the
  `TeamIntentBoardTests` errors were corrected by the concurrent work. It then
  stopped at CS0103 in `CombatPlannerResolverTests.cs:12` (`MapPathfinder`).
  No tests executed in that run; the earlier seven HDV harness passes and
  seventeen native passes remain distinct evidence, not a full-suite pass.
- This session did not edit the concurrent planner/resolver tests or source.
  The thread-addressing host was unavailable at `rpc.sock`; a persistent file
  monitor watches `CombatPlannerResolverTests.cs` for its next modification,
  rather than repeatedly launching builds or changing the other writer's file.
- Reading the actual runtime adapter in `LaunchService.Combat.Turn.cs` shows
  that planner admission checks learned spell IDs, live spell level/descriptors,
  resolver coverage, server refusals, caster gates, initial cooldown, previous
  casts, AP/range modifiers, turn/target caps and per-target gates. These are
  observed code paths, not proof of execution in a live fight.
- The adapter distinguishes explicitly configured support spells (threshold
  101) from discovered pure heal/shield spells (threshold 60), and passes
  allowed cast cells, zone rules and challenge end-cell constraints into the
  snapshot. These sections are changing concurrently; verification must use
  the eventual compiled state, not assume an earlier source read is current.
- Unknown fighter HP uses a large placeholder with MaxLife zero; enemy reply
  reach/damage remain conservative estimates, and the combat log labels enemy
  reach, shield pools and tackle uncertainty. No observed enemy spellbook or
  full class-mechanic model is established by this adapter.
- `ForcedMovementSafe` is not supplied by the inspected snapshot constructor,
  so its default false still prevents admitting non-teleport displacement
  transitions without established safety. This is a remaining limitation,
  not completed support for all approach/control spells.
- No client input, purchase, deployed DLL change, commit or push occurred.

### Full-suite execution resumed

The missing Protocol import was subsequently added to the concurrent
`CombatPlannerResolverTests.cs`. The full Release suite compiled and started
executing under monitor `suite complète après correction test résolveur`
(terminal `bash_106`), using the existing isolated HDV artifacts directory.

During execution, the existing performance test
`MapInspectionTests.Inspection_stays_fast_with_thousands_of_entities_across_several_bots`
failed: 120 updates of 3800 entities took **12029 ms**, against its unchanged
**12000 ms** bound. This is a measured failure, not a diagnosed regression or
a proven scheduling artifact. Neither the test nor its threshold was changed.
The two already-disabled GameCodec tests were reported skipped. At this
checkpoint the suite was still running; no final suite result is claimed.

The final result for this run is now available: **4045 passed, 1 failed,
2 skipped, 4048 total**, duration 3 minutes 21 seconds, exit 1. The only
reported failure was the inspection performance bound above. This validates
execution of the HDV regressions within the normal repository suite as well
as the current combat tests, but does not make the entire suite green.
An isolated execution of the unchanged inspection test is the next diagnostic
comparison; it must not replace or erase the failed full-suite result.

The unchanged inspection test passed in isolation using the exact compiled
test DLL from that failed full run (`--no-build --no-restore`): one passed,
exit 0, test duration reported as 1 second, total runner duration 4.3015
seconds. This establishes a substantial difference between isolated and
full-suite execution, but does not identify its cause. No test threshold,
parallelism setting or production inspection code was changed; the full-suite
failure remains recorded.

### Learned utility rotation regressions

Two tests added to `AutoSpellRotationTests.cs` verify the existing automatic
rotation contract using actual descriptors, not a mocked planner:

- Learned self-heal, ally shield and free-cell teleport remain candidates
  under a fire-element selection. Self/ally targeting, support tagging and
  each descriptor's distinct per-turn cap are preserved.
- A manually disabled heal is not reintroduced. A conditional heal and a
  spell requiring a class state remain excluded rather than assuming that
  state exists.

The complete `AutoSpellRotationTests` selection passed **15 tests, 0 failed,
0 skipped**, exit 0, duration 173 ms. This checks generated configuration,
not a live cast or full Sram/Roublard behavior. Production rotation code
was unchanged. The existing C# language-server workspace scan still fails
on the unrelated inaccessible artifacts directory; .NET compilation passed
with the existing warnings.

### Lua movement policy inspection

The tracked Lua movement task is not absent from production code:
`combat.moveToCell` and the compatibility alias `fightAction.moveTowardCell`
both invoke `ScriptCombatMoveAsync`. Immediately before injection that method
calls `CombatMovementDecision.CanMove` with the active profile and nearest
enemy distances at source and destination. Manual movement shares this method.
It also rejects stale hook epochs and closed turn windows, then rechecks the
geometry after its movement barrier.

This is only an endpoint policy check. The chosen path is still produced by
`CombatMovePlanner.PickDestination` with the terrain step predicate, and the
separate `CombatReachable` API returns terrain/occupancy reachability without
filtering by movement policy. Thus a non-approaching destination does not by
itself prove that intermediate cells respect NeverApproach. No Lua action was
executed, and the task remains open pending path-level policy verification.
Any correction must account for the shared manual-movement caller and must
not add an energy gate.

### Resolver-aware learned candidate admission

The automatic rotation's strict `CanPlan` filter was still excluding effects
already handled by the downstream planner/resolver. For calls requiring
descriptors (the non-script automatic planner path), `AutoSpellRotation`
now uses `CombatTurnPlanner.IsAdmitted`. Calls without that requirement retain
the existing strict descriptor policy. Element filtering, disabled/manual
entries, cost checks, order and cast caps are preserved.

The new tests first reproduced eight failures: four excluded immediate effects
(zone damage, AP removal, self AP gain, strength buff), excluded life steal,
and three wrongly included random/deferred/triggered damage descriptors.
After the filter correction, the combined rotation, turn planner, planner
resolver and resolver selection passed **88 tests, 0 failed, 0 skipped**,
exit 0, duration 10 seconds. The life-steal regression creates candidates
through the actual rotation and observes selected damage and healing through
the actual planner, covering the previously disconnected admission path.

This is candidate coverage, not complete class support. Spell-level state
criteria, unknown damage masks and pure deferred effects remain excluded.
The adapter still requires a modeled enemy attack to enter the planner;
utility-only kits fall back to the classic rotation. Recursive elemental
filtering, current actor class/state/template facts, trap trigger payloads,
Roublard detonation/chains/walls and summons' real combat stats remain
unverified or unmodeled. No game input, deployment, commit or push occurred.

### Lua movement policy applied to every step

`CombatMovementDecision.RestrictSteps` composes terrain connectivity with the
nearest-enemy distance policy. `ScriptCombatMoveAsync` now supplies that
predicate to initial destination selection, refreshed selection, reachability
accounting and actual `MoveTo` path encoding. After the movement barrier it
always refreshes PM, geometry and the policy snapshot, even if the caster did
not move. The existing endpoint check remains. The shared manual move caller
receives the same policy; `CombatReachable` now reports policy-permitted cells.

For partial moves, `CombatMovePlanner` reverses the step predicate when
building its reverse distance field from the goal. This preserves forward
one-way paths rather than incorrectly evaluating outgoing edges at the goal.
The regression covers a legal detour whose first steps do not reduce straight
line distance. Another covers a permitted endpoint accessible only through an
approaching step, which must be rejected under NeverApproach.

The first version of those test boards used invalid edge coordinates. The
directed-detour test failed; inspection also found that the policy test could
pass vacuously with target -1. Both were corrected to offsets around cell 300
and now explicitly reject invalid fixture cells. This was a test-fixture
correction, not an adjustment of expected movement behavior.

Final targeted Release run: **70 passed, 0 failed, 0 skipped**, exit 0,
duration 382 ms. Selection: CombatMovementDecisionTests, ScriptCombatTests,
CombatTurnPlannerTests. Compilation includes the real application; existing
warnings remain. No live Lua invocation or game input was performed. The
tracked movement task remains open for real-surface verification, and no
full-suite green result is claimed.

### Recovery code inspected before integrated validation

The current recovery code already separates the observed-life store from
estimated seeds: `AuthoritativeLifePercent` accepts `_observedSelfLife` only
when `_lifeStaleSinceFight` is clear. `ObservedLifeRegressionTests` exercises
the real LaunchService to establish that an estimated seed neither changes
observed HP nor advances the confirmation sequence, and cannot clear fight
staleness.

`PrepareOperatorRegeneration` requests `authoritative: true`; the passive
recovery path uses estimates only to schedule the sitting-emote exit request,
then still waits for an observed percentage. `RegenerationPolicy` applies the
stop threshold only while a member is recovering, and the start threshold
otherwise. Existing tests cover that distinction, unknown online HP and
offline-member exclusion. This inspection does not prove a live emote causes
a fresh HP packet, nor that the entire script wait path is free of deadlocks.
Those tasks remain open.

A new full Release suite is running after the admission and movement changes,
under monitor `suite complète rotation mouvement et soins` (`bash_114`).
It compiled and began execution; the final result is pending at this checkpoint.
No live recovery, food purchase or emote command was issued.

During this full run, `LiveCombatRegressionTests.Text_before_sequence_wakes_pending_cast_without_fictional_ack(170)`
reported a failure after 1 minute 32 seconds: the pending barrier returned false
at its assertion. The test arms a request, starts the real barrier, injects
the rejection through the private method, then awaits the result. Inspection
shows the production rejection stores its request-scoped refusal before
pulsing the barrier, and the barrier rechecks refusal after capturing its
signal to avoid a lost wake-up. The exact cause of the observed false result
is not yet established. Neither the 60-second barrier timeout nor the
20-second bounded test wait was changed, and no passing rerun is claimed.
The full run's final aggregate result is still pending.

Final full Release result after rotation and per-step movement changes:
**4071 passed, 2 failed, 2 skipped, 4075 total**, duration 5 minutes 10 seconds,
exit 1. Both failures were the refusal-barrier test variants (message 170:
1 minute 32 seconds; message 65: 1 minute 55 seconds). The inspection
performance test did not fail in this run. The earlier performance failure
is retained above.

A process probe also found a separate externally started `dotnet test
GivreClone.Tests -c Release` using the worktree's normal bin directory,
with a testhost around 2.6 GB working set. It was not stopped or modified.
This establishes concurrent machine activity, not the cause of either failure.
Our own builds used the isolated temp artifacts directory. A no-build,
no-restore diagnostic run of the two refusal tests alone is now running;
its result is pending. No timeouts, assertions, concurrency settings or
production barrier logic were changed to mask these failures.

The two refusal tests passed in the no-build isolated diagnostic with the
same compiled binary: message 170 took 17 seconds, message 65 took 31 seconds;
2 passed, exit 0, total runner duration 51.1607 seconds. These long isolated
durations still warrant investigation: a passing isolated run is not evidence
that the synchronous rejection path is fast or that the full suite is reliable.
No cause is yet established and the failed full-suite result is unchanged.

Temporary test-only phase measurements were then collected. Message 170:
constructor 153 ms; request at 179 ms; subscribed at 190 ms; rejection returned
at 196 ms; barrier resumed at 207 ms. Message 65: constructor/request 4 ms;
subscribed/rejection 5 ms; resumed 8 ms. Both passed (245 ms and 11 ms test
durations), exit 0. This run did not reproduce the slowdown; it rules out an
unconditional long delay in the tested path, not intermittent contention.
The temporary `[DEBUG-refusal-timing]` outputs and stopwatch were removed.
No production barrier or test timeout/assertion was changed.

### Automatic purchase boundary inspection

The current FM purchase path already revalidates its durable budget at the
injection boundary: `HdvBuyAsync` invokes `SendSmithSpend` from its actual send
callback, after taking the per-PID purchase semaphore. `SendSmithSpend`
checks disposed scope, PID and full account/server/character identity, then
under `_smithSpendGate` checks identity again and rejects another unfinished
operation. `SmithSpendingOperation.TrySend` reserves against current
`Kamas(pid)` through `SmithObjectiveStore.Reserve` before calling the injector.
The store refuses unresolved previous spending, an exceeded cumulative cap
or a wallet below the contractual floor. Existing tests include exact floor,
failed durable reservation and a disposed inherited spending scope.

This does not establish equivalent protections for every automatic expense.
For non-FM HDV buys the initial license and wallet checks in `HdvBuyAsync`
precede the semaphore wait; when there is no FM scope, `SendSmithSpend`
delegates directly to the injector. Rechecking those global conditions at
dispatch is a concrete remaining concern. No purchase was attempted and no
spending policy was changed during this inspection.

The identified HDV gap was subsequently narrowed: `HdvBuyAsync` now checks
cancellation, HDV permission and the current wallet after acquiring its
purchase lock and again inside the actual send callback, before
`SendSmithSpend`. Existing FM reservation/reconciliation remains in place.
Known insufficient funds are rejected; the existing behavior for an unknown
wallet was not changed. The targeted spending, HDV mutation, buyer variant
and receipt tests passed **41 tests, 0 failed, 0 skipped**, exit 0,
duration 1 second. These test the existing accounting/receipt boundaries and
compile the changed application; they do not simulate license revocation
while this real purchase semaphore is held. No integrated coverage or live
purchase for that scenario is claimed. The broader all-expenses authorization
task remains open.

### Observed template and summoner facts reach the planner

The class-data audit child stopped on provider usage limits without delivering
a usable report. The lead inspected the current sources directly.
`FightMonsterReader` already decodes monster template and summoner through the
live field catalog into `_fightMonsters`. The runtime snapshot adapter now
copies those observations into optional `PlannerFighter.MonsterTemplate` and
`SummonerId`; the planner forwards them to its root `SpellFighter` board.
Absent observations retain the existing unknown defaults, not guessed IDs.
The normal combat cleanup removes `_fightMonsters` at fight end.

Six planner cases verify matching, nonmatching and absent monster templates,
and own/other/absent summoners for invocation-filtered damage. Combined
CombatTurnPlannerTests, CombatPlannerResolverTests and FightMonsterReaderTests:
**68 passed, 0 failed, 0 skipped**, exit 0, duration 14 seconds. The tests
exercise the planner/resolver behavior and protocol reader; no live fight
demonstration of the runtime adapter is claimed.

This closes a data propagation gap, not Sram or Roublard mechanics. Actor
states, carrier identity, complete marks, trap triggering, bomb combos and
walls remain unmodeled or not supplied by this adapter. No client input,
deployment, repository commit or push occurred.

### Unknown class states are not confirmed absence

The resolver previously normalized a missing state collection to an empty set,
so a planner snapshot with no state observations could satisfy a negative
state mask such as `e42`. `PlannerFighter.States` is now optional: null means
unknown; an explicit empty immutable set means observed absence. The root
board preserves that distinction through `SpellFighter.StatesKnown`.

When the resolver encounters an `E`/`e` condition whose state is neither
known-present nor covered by a known state collection, it skips that effect
and reports the missing fact in `Unmodeled`. A state applied earlier in the
simulated cast can still establish presence. For incomplete collections,
state removal does not yet establish a known-absent individual state; this
is conservative until per-state observations are modeled. Direct standalone
resolver fixtures retain their existing known-state contract.

Three planner cases cover unknown, known absent and known present states for
negative state-gated damage. Combined rotation, turn planner, planner resolver
and resolver tests passed **101 tests, 0 failed, 0 skipped**, exit 0,
duration 9 seconds. The runtime adapter still does not claim a complete
server state collection; no state packet decoder or trap/bomb semantics was
invented by this change. No live client input, deployment, commit or push.

The current `fields.json` names `FightRemovableEffect.target_id` (8),
`dissipation_state` (9), and extended-effect action/source IDs, but retains
obfuscated names for the state payload and removal identifiers. The unrelated
`State` message is not evidence of a fighter-state schema. No old-version
field numbers were substituted for the missing current meanings.

An additional resolver regression exercises an incomplete state collection:
negative-mask damage is withheld, a state applied by the cast permits
positive-mask damage, and removing it does not claim complete observation.
The resolver test selection passed **12 tests, 0 failed, 0 skipped**, exit 0,
duration 149 ms. The output retains both missing-state reports and the
incomplete-knowledge flag.

### Installed bestiary failure in latest integrated run

The next full suite (`bash_121`, `suite intégrée identités états et HDV`)
reported a failure in the installed Sanglacier threat test. Inspection of
the exact external file
`%USERPROFILE%/AppData/Local/GivreReplica/dofensive/monster-threat.json`
found monster 2845 grades 1 through 5 all equal to `[10,5,0,0]`
(AP, MP, reach, turn damage). The file's SHA256 at inspection was
`327830c96749bebeed16e09c072b33360d95050a8c8d9603e13f67ca9d3a98f3`.
This explains why the installed test's expected damage floor cannot pass
with those data; no expected value or external file was changed.

The runtime currently uses fallback range 6 and damage at least 30 when
these threat fields are zero, but its `ThreatKnown` flag treats any found
bestiary row as known even when damage/reach are missing. That diagnostic
provenance remains a separate concern. This inspection does not establish
the original importer error or a correct replacement threat value.
The integrated suite's final aggregate result remains pending here.

That integrated run finished **4088 passed, 1 failed, 2 skipped, 4091 total**,
exit 1, duration 2 minutes 43 seconds. Only the installed bestiary test failed.
The later provenance correction adds `MonsterThreat.AttackKnown` and requires
both positive reach and positive damage before the runtime marks a bestiary
attack as known. Positive observed damage can independently mark it known.
The targeted bestiary/planner selection compiled and finished **60 passed,
1 failed**, exit 1; the unchanged installed test still reports `turn damage 0`.
No test was excluded or weakened.

The external data folder contains `compile_threat.py` and cached monster/spell
JSON. Read-only inspection found Sanglacier spells 2202/2203/2204 present.
Spell 2202 grade 1 contains fire damage 36–45, cost 5 AP, range 1 and two casts
per turn; monster grade 1 contains 460 Intelligence and 10 AP. These data are
not reflected in the zero-damage installed output. The generator was read but
not executed or edited; whether the output predates those cached spells is
not yet established. No external catalog was overwritten.

The source/output modification times now establish that compilation preceded
the required cached spells: `monster-threat.json` was modified at
2026-11-02 18:19:56.765 UTC; spell 2202 at 18:24:34.363, 2203 at
18:24:33.344 and 2204 at 18:24:33.541. Monster 2845 itself was present at
18:16:10.707. A bounded process probe found no active Python/uv process
running this scraper/compiler at inspection. The old zero output is therefore
stale relative to its now-present inputs. Recompilation from the complete
local cache is the next repair; do not replace zero with hand-authored combat
values or fetch another version to satisfy the test.

Recompilation completed from local cached inputs using the existing
generator's calculation, with its file-writing tail removed in an in-memory
AST. No source generator file was edited or network request issued.
5204 monster records were processed; **4529 referenced spells remain absent**,
so this is still a partial bestiary, not complete monster mechanics.
Sanglacier grades 1–5 now have AP 10, MP 5, reach 1 and respective turn
damage 454, 470, 486, 502, 518. The generated JSON SHA256 is
`15ada5be0a8d6b4bb589f9c7724c153b9706d3a15e3185f74f3e8f74fc26c12b`.

The original JSON was preserved with apply_patch at
`%USERPROFILE%/AppData/Local/Temp/givre-bestiary-recompile-20261102/monster-threat.before.json`.
The installed JSON was replaced with apply_patch only after exact comparison
with the previously read contents (no concurrent change). The integrated
suite is running again under `bash_123`, monitor
`suite intégrée après recompilation bestiaire`; final result pending.
Already-running processes may retain their lazy-loaded old catalog until
restart; no live game client was restarted or driven for this repair.

### Current runtime rechecked without game input

The latest `list_bots` MCP response identifies Bastionbot in **socket** mode
under virtual PID 1073741825, map 191105026, no running script/fight.
This is not the earlier native-client PID 20192 used for the tactical
captures. Do not reuse that old PID's mailbox as proof for this connection,
or disconnect/reconnect this socket session merely to resume visual tests.
The user was asked nonblockingly whether Bastionbot is available; no game
commands were issued. Tactical native lifecycle remains an independent
live gate, not something the current socket connection can demonstrate.

The full integrated suite after the provenance correction and local bestiary
recompilation finished with **4090 passed, 0 failed, 2 existing skips,
4092 total**, exit 0, duration 3 minutes 38 seconds (`bash_123`).
The installed Sanglacier regression now passes without changing its
expectation. This proves the compiled/tested scope only; it does not cover
the still-missing cached spells or native tactical map transitions.

### Unknown maximum life and spell percentage gates

Read-through of the planner found that `HpGate` previously accepted every
percentage condition when maximum life was absent (zero). This incorrectly
admitted configured self/target percentage restrictions using unknown HP.
It now admits only the unrestricted interval covering 0–100 in that case;
observed maximum life retains the existing percentage comparison.
Four deterministic theory cases cover self versus target and restricted
versus unrestricted conditions. No runtime life observation is fabricated.
The targeted planner suite is running as `bash_124`, monitor
`conditions PV inconnus planificateur`; result pending.

This is not completion of the broader confirmed-versus-estimated HP task:
the million-HP sentinel, percentage-dependent resolver effects and target
mask conditions still require an explicit knowledge contract. The native
tactical/client-use question timed out again, so no game input is authorized
by that unanswered question.

The first planner selection finished **60 passed, 0 failed**, exit 0.
A second read-through found the same missing-maximum issue in target masks
`V/v`: comparisons against maximum zero could silently admit `v` conditions.
The resolver now refuses both percentage predicates when maximum life is
unknown and records an unmodeled observation, including starred caster
predicates. Four additional deterministic cases cover `V/v` on target/caster.
The combined planner/resolver selection finished **76 passed, 0 failed,
0 skipped**, exit 0 (`bash_125`). This is targeted validation after the
latest changes, not another full-suite or live-combat result.

Effects 279 (caster missing-life damage), 1109 (target maximum-life heal)
and 1039 (target maximum-life shield) now report an unmodeled observation
when the required maximum is unknown, rather than using zero.
The combined suite with three missing-maximum regressions passed **79 tests**,
exit 0 (`bash_126`). Three positive-value cases were then added to assert
exact target life/shield values with observed maxima, preventing a fix that
simply suppresses these effects for all inputs.
The complete suite is now running as `bash_127`, monitor
`suite complète corrections PV connus et inconnus`; result pending.
The LSP server still crashes on access denied to the unrelated
`artifacts/kourial-tools/pypdf` folder; actual dotnet compilation is the
available diagnostic gate. That folder and its permissions were not changed.

## 2026-11-02 planner coverage audit (offline)
- New test Exposed_caster_ends_its_turn_behind_a_line_of_sight_blocker: an immobile range-4 enemy sees the start cell. With 0 AP and 4 MP, the planner ends the turn on a cell the wall hides, with incoming 0.
- The behaviours below were already in CombatTurnPlanner and pass existing tests (11/11 rerun):
  - finishing a target (Finishing_a_target..., Observed_resistance..., Threat_search_leaves_budget...)
  - move-cast-move kiting (Out_of_range_attack_approaches_casts_then_retreats, Mobility_cast_can_be_followed...)
  - threat map and safe end of turn (Moved_enemy_threat..., Low_life_under_threat...)
  - heals and shields by HP (Low_life_under_threat_prefers_survival...)
- Full suite: 3990 passed, 2 skipped, 0 failed.
- These are offline proofs only. No live fight has run on the new build yet (PID 12796 still runs the old Debug build).

## 2026-11-02 live validation (app restarted with the new builds)
- C1, planner live: build givre-live-20261102, Valrianess (Eniripsa lvl 37), AutoSpells=true, FleeBelowHpPercent=50, a Lua fight() against Piou.
  - 15:54: 11 lines `[combat-planner] turn=1..11`. The planner runs in a real fight.
  - Regression caught: damage=0 on every turn, 293 -> 27 HP, zero hits.
  - Diagnostic added (spells=N/M exclus=[id:reason]). Fight at 15:59: 3/4 spells excluded as non-plannable. 25871 has 7 effects and 25870 has 4, both with targets=None (unmodelled masks or effects). 25742 is an uncertain buff. The only eligible spell is 13175.
  - Fix: without at least one plannable offensive spell, the planner yields to the classic rotation and logs `[combat-planner] turn=N ignoré : aucun sort offensif planifiable`.
  - Fight at 16:08 (build out3): 6 rounds, spells 25742/25871/25870/13175 cast, 3 Piou killed, 174/284 HP at the end. Fixed.
- Exact limitation still open: the planner gives no tactical value on real spells that have several effects or non-standard target masks. It only plays when a damage spell has simple effects (P shape, mask A/C/a/g, actions 96..100).
- Other bug fixed (live): connect_bot "dedef" (offline, empty name) launched ArcherbotLeRetour in MITM mode with the real IP. RowOf matched "" == "".
  - Fix in BotMatcher.SameAccount, test Offline_bot_with_empty_name_never_matches_another_account_without_character.
- Expedition (user request): checkbox "Défaite : continuer l'expédition (sinon arrêt)", unchecked by default. The user stated that the game no longer has a Phoenix and the character comes straight back after dying.
  - Checked: no defeat stops the expedition or dungeon script (DEFEATS counts them for the console). Unchecked: stops at the first defeat, as before.
  - Saved in ExpeditionLast (field 15); older lines default to stop. Pipe and MCP: start_expedition continueAfterDeath.
  - Not tested live: an actual defeat during an expedition.

## 2026-11-02 why real Eniripsa spells are not plannable (installed client data, grade 1)
- 25870 (3 AP, range 1-4): damage action 91, water 16-18. Action 91 is a life-steal damage, but IsCertain only accepts plain damage 96..100.
  It also has 1160 triggers (cast another spell 25797/25889, masks 'a,P,F7370,F7371') and action 90 (8, element 5, on self/ally).
- 25871 (4 AP, range 0-4): zone shape C size 2.
  - Action 91 damage 18-20.
  - Actions 950/406/2973/2960: state, ally buff and other effects.
  - Action 1160: triggers spell 25890 on enemies.
  - The planner only simulates shape P (single cell).
- 25742 (2 AP, range 0-3): summon (1011, creature 7372), buffs 111/950 and 1160 triggers. Not a damage spell.
- 13175 (1 AP, range 1-5): plain 1-cell push (action 5). This is the only plannable spell.
- What is missing, exactly:
  - life-steal damage (91..95);
  - spell-cast triggers (1160), whose sub-spell effects are unknown;
  - non-P zones (C, X, L...) in the simulation;
  - state and buff effects (950, 406, 2973, 2960).
- Not done on purpose: treating 91 as plain damage while ignoring the 1160 triggers would claim a simulation that does not exist.
- The current behaviour is the honest one: classic rotation as the fallback, with a `[combat-planner] ... ignoré` log line.

## 2026-11-02 real per-class coverage audit (installed client, all spells from spellvariants, first known grade)
- Measured with a temporary probe (removed) on GameNameResolver.SpellsForBreed + GetSpellDescriptor.
- Result: 0 plannable damage spells for 19 classes out of 20. Breed 3 has 2, breed 7 has 1 plannable non-damage spell.
- 44 spells per class (26 for breed 19). 17 to 32 of them are damage spells.
- Consequence: with real spells, CombatTurnPlanner almost never takes the turn. The classic rotation plays, which is the fallback validated live on 2026-11-02.
- The planner's tests (kiting, threat map, line of sight, finishing) pass on synthetic spells, but those behaviours do not reach real fights.
- Most frequent blockers (count of effects):
  - 1160 cast-spell trigger: 693
  - 950 state buff: 253 (+85 with a non-standard mask)
  - 792: 234
  - zone C: 197
  - zone X: 112
  - 280 buff: 74
  - 2960: 65
  - 3792: 64
  - 406: 62
  - 181 summon: 62
  - zone Q: 59
  - 951 debuff: 55
  - zone a: 50
  - 2794: 47
  - 3793: 40
  - 1080: 38
  - 281: 37
  - 293: 35
  - zone G: 34
  - 1020 shield with a mask: 34
  - 1042: 30
  - damage 96/97 with an unsupported mask: 29/28
- Priorities to make the planner useful, in order:
  - (1) evaluate the damage of a spell while ignoring the non-damage side effects, instead of rejecting the whole spell;
  - (2) simulate zones C/X/Q/G;
  - (3) resolve the 1160 sub-spells.
- Point (1) changes the safety contract: the score becomes partial. It needs a design decision plus live validation; it was not done here.

## 2026-11-02 quick search (Ctrl+K) to the Bots tabs
- QuickNavigationCatalog: 17 "Bots › <tab>" entries (keys bots/<index>, in BotsView.axaml order), with keywords (expedition, forgemagie fm runes, hdv...).
- MainViewModel.ExecuteQuickNavigation sets BotsVM.RequestedTab, then BotsView selects the tab (applied on attach, on DataContext change, and on change).
- Tests: QuickNavigationCatalogTests 14/14 (31 entries, bots/3, bots/8, bots/16, invalid keys rejected).
- Live, build out4, PID 11508: Ctrl+K -> "expedition" shows only "Bots › Expédition". Pressing it (AX) opens the Bots page on the Expédition tab (capture ev/qnav3.png).
- Caveat: a pixel click on a coordinate guessed from the capture landed on the backdrop and closed the overlay. This is an imprecise test click, not an app bug.

## 2026-11-02 reliability fixes (the 5 todos)
- Already implemented in commit ba863eb (2026-11-01): observed HP kept separate from estimates, no mutual deadlock while waiting for the team, "never approach" applied to combat:moveToCell, automatic spending re-checked before sending, and passive heal confirmation (an estimate is never treated as observed).
- Rerun today: ObservedLifeRegression, CombatAndStatDispatchRegression and RegenerationPolicy, 31/31 green.
- Not validated live: regeneration and automatic spending were not observed on a real character.

## 2026-11-02 combat AI v2: real effect simulation (SpellResolver) and team coordination
- User: "trouve une vraie solution sans ignorer les effets secondaires" / "jouer mieux qu'un vrai joueur" / "si 4 comptes, qu'ils jouent très bien ensemble".
- Sources: official text of each effect (names.Effect + I18n), and JondoEmu 683ab49 docs/effect-coverage.txt (179 effects across 836 spells).
  - JondoEmu was used as a semantic reference only: no license, no code copied.
- SpellDescriptor: raw DiceNum/DiceSide/Value/Delay/Random/Triggers/Condition kept (read in GameNameResolver). Before this, the sub-spell id/grade of 1160 was lost.
- SpellResolver (new, pure, deterministic):
  - Zones via CombatZones; full target masks: a/A/g/c/C, summons i/I, monsters m/M, P/p, PB/pb, R/r, K, E/e states, F/f templates, B/b breeds, V/v HP%, '*' = condition on the caster.
  - Damage 96-100 and fixed damage; life steal 91-95 (heals the caster); %HP damage. Heals, incl. % of damage dealt 2973. Shields 1020/1039/1040.
  - AP/MP gains and losses; states 950/951/952; characteristic buffs.
  - Sub-casts 1160/2960/2794/792/1017/1019/2160, recursive with a depth limit of 4 and cycle detection.
  - Forced movement, summons, marks/glyphs/traps.
  - Rows triggered later (triggers/delay) are kept as Deferred, not applied. Everything else is an explicit Unmodeled entry, never a rejection of the whole spell.
- Measured on the installed client: immediate damage modeled for 814/862 class spells (94.4%). Was ~0 plannable spells before (19/20 classes at 0).
  - Weakest classes: breed 5 29/44, breed 6 34/44, breed 20 37/44.
- Largest remaining Unmodeled rows (occurrences):
  - 406 remove spell effects: 472
  - 2905: 228
  - 792 cases not resolved: 193
  - 120 AP returned: 192
  - 293 +base damage: 190
  - 666: 172
  - 280/281/285 spell modifiers: 71/53/47
  - 3792: 64
  - 1160 sub-spells missing: 50
  - 950 states with unsupported masks: 45
  - 335 look: 38
  - 1171 +% final damage: 30
- Still unsupported (exact): turn simulation (buff durations, hooks fired on a later trigger), random rows, Xelor telefrags, Eliotrope portals, Roublard bomb chains, Sram invisibility beyond the gate.
- TeamIntentBoard (new): each of our bots in the same fight publishes target, expected damage, kills and end cell. The next one avoids overkill on a doomed enemy, finishes enemies already hit, and gives support to the most exposed ally. Classic rotation is wired, with a `[combat-team]` log line. 8 tests.
- Tests: resolver + team 22/22. Full suite: 4028/4031 before the 25870 test fix (an assertion that was too strict on sub-casts reserved for the Eniripsa words).
- In progress: CombatTurnPlanner on top of SpellResolver (zone casts on the best cell, AP/MP removal, life steal, team view), then live validation solo and in a team.

## 2026-11-02 Accounts: accent-insensitive search and server filter
- Live (PID 11508, Accounts page): searching "VÂLRIANÉSS" -> 1 / 5 compte(s) (Valrianess). Clearing it -> 5 / 5 (ev/acc-accent.png, ev/acc-all.png).
- Server filter: the local accounts carry no server ("-"), so there is no option to pick live. The Avalonia combo box also does not open in the background (no AX pattern).
- Covered by a new test, AccountServerFilterTests 3/3:
  - choices merged ignoring case and spaces, "-" ignored;
  - intersection with state + search, then reset;
  - a server that disappears falls back to "Tous les serveurs".


## Soirée du 02/11 (18:00 → 22:10) : vitesse, équipe, bestiaire

### Correctifs livrés (non commités, suite complète 4108 réussis / 2 ignorés / 0 échec)
- **Vitesse** : plan_ms 7-120 ms par décision (avant 1,2-1,9 s) ; marche multi-cases d'un coup ; budget 60 000 avec les lancers depuis la case courante examinés AVANT les fuites (sinon 0 lancer avec un petit budget).
- **Refus de lancer** : un TextInformation sans séquence réveille l'attente tout de suite (avant 1,2 s perdue par sort) ; le réveil passe avant la résolution du texte. 145 lancers, 0 refus sur les 8 derniers combats.
- **Menace réelle** : plus gros coup observé par ennemi + bestiaire Dofensive (PA, PM, portée, dégâts par tour par grade ; 27 184 grades) + DofusDB (394 monstres immobiles -> 0 PM). Chaque ennemi n'est compté qu'UNE fois, sur l'allié qu'il blesse le plus (avant : compté sur chaque allié -> fuite en duo).
- **Approche** : potentiel du tour suivant (meilleur sort, portée + PM), gradué par la distance sur les tours de pure marche ; le frapper-puis-reculer et la téléportation inutile restent refusés.
- **Ennemi à 0 PV suivis** : reste ciblable tant que le jeu n'a pas annoncé sa mort (avant : 9 tours sans cible puis défaite). Écart de suivi des PV encore présent (part bouclier probable), journalisé.
- **Équipe** : suivi natif du jeu (PartyFollowMemberRequest confirmé) ; rattrapage rétabli quand le suiveur est sur une autre carte (havre-sac + zaap, 4 s). Auto-prêt non envoyé : champ fqcl non documenté.

### Preuves en vrai combat (Bastionbot Sacrieur 195 + Sylvanbot Sadida 157, même groupe de 2 monstres niv. 272-276)
| Combat | Résultat | Tours | Durée |
|---|---|---|---|
| n°4 | victoire | 6 | 13 s |
| n°5 | victoire | 9 | 30 s |
| n°6 | défaite (cible vivante perdue) | 17 | 32 s |
| n°7 | victoire | 10 | 31 s |
| n°8 | victoire, 23 sorts, 0 tour sans sort | 4 | 14 s |

Coordination observée au n°8 : focus commun sur -1, réservation de l'achèvement, bascule de Bastionbot sur -2.

### Non pris en charge (exact)
- Recalage des PV en combat : le serveur n'envoie que pertes et gains ; l'écart bouclier/PV demande une capture d'un coup sur cible à bouclier.
- Auto-prêt natif (FightAutoReadyActivationRequest) : champ fqcl de sémantique inconnue, non envoyé.
- Pièges Sram, bombes Roublard, téléfrags Xélor, portails Éliotrope : toujours non modélisés.
