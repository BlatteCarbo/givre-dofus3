# JondoEmu feasibility as a GivreClone combat oracle

Audit date: 2026-11-02  
Audited repository: <https://github.com/Keka-Bron/JondoEmu>  
Pinned revision: [`683ab49c73f65b7c4ab7c1134c448f7fa850f6bc`](https://github.com/Keka-Bron/JondoEmu/commit/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc)  
Pinned tree: `f7b861bf3f0ee8ce4cb0cdc024ab531785fe41e4`  
Disk free supplied for this audit: `27,431,325,696` bytes (about 25.55 GiB).

## Decision

**Conditionally useful, but begin only with a source-based, method-filtered unit-test probe. Do not install the release, game client, or database merely to obtain the first oracle signal.**

JondoEmu contains unusually specific executable tests and implementation for the combat areas relevant to GivreClone: Sram traps/invisibility, Roublard bombs/detonation/combo, displacement/collision damage, states, and spell grades. The strongest small starting oracle is the pure displacement subset (`PullTests`, `PushDamageTests`, and `TeleportSymmetryTests`). It does not require the Dofus client or `world.db` at test runtime.

That subset is not lightweight at build time: it resides in a `net10.0-windows` test project that references the World, Server, Studio, Launcher, and WinForms contract projects. A later run therefore needs a source checkout, Windows, the .NET 10 SDK, and a NuGet restore/build of that dependency graph. The repository explicitly says Windows-targeted projects can only be restored elsewhere and still require Windows to build ([`Directory.Build.props`, lines 3-17](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Directory.Build.props#L3-L17)); the combat test project itself targets `net10.0-windows`, enables WinForms, and names its broad project references ([`Jondo.Unity.Tests.csproj`, lines 3-34](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Jondo.Unity.Tests.csproj#L3-L34)).

Spell-driven Sram, bomb, and combo tests are a second tier. They need the repository's `datos/world.zip` expanded as `bases/world.db`; bomb behavior also reads `datos/bombas.json`. Missing database data can yield empty spell-effect lists rather than an unequivocal startup failure, so an indiscriminate green run without the DB is not valid evidence. The project's own CI warns that 64 DB-dependent tests otherwise return early and are counted as passing ([`.github/workflows/ci.yml`, lines 22-32](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/.github/workflows/ci.yml#L22-L32)).

Live-client use is **blocked for the currently installed game**: the pinned README supports Dofus `3.6.10.11` or `3.6.10.10`, while the supplied context says the installed game differs. Do not infer protocol compatibility. The README itself says the then-live `3.6.11.12` client was unsupported ([`README.md`, lines 54-66](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/README.md#L54-L66)).

No JondoEmu archive, client, database, package, clone, installation, build, or test was downloaded or run during this audit. The commands below are evidenced recipes, **not successful-run claims**.

## Compatibility and prerequisite matrix

| Item | Evidence at pinned revision | Consequence |
|---|---|---|
| Source revision | Commit `683ab49c73f65b7c4ab7c1134c448f7fa850f6bc`; tree `f7b861bf3f0ee8ce4cb0cdc024ab531785fe41e4` | All source conclusions below are tied to this revision. |
| SDK for source tests | CI installs `.NET 10.0.x`, restores and builds the solution, then tests ([`ci.yml`, lines 17-48](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/.github/workflows/ci.yml#L17-L48)) | Use the .NET 10 **SDK**, not only the Desktop Runtime mentioned for release users. NuGet restore is required. |
| Combat-test platform | `Jondo.Unity.Tests` targets `net10.0-windows`, `UseWindowsForms=true` ([project, lines 3-12](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Jondo.Unity.Tests.csproj#L3-L12)) | Run this test project on Windows. |
| Full-client versions | README: Dofus `3.6.10.11`, with `3.6.10.10` also working; `3.6.11.12` unsupported ([README, lines 54-66](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/README.md#L54-L66)) | Current installed client cannot be assumed compatible. No client is needed for unit tests. |
| Client injection | MelonLoader `0.7.x`, tested against `0.7.3`; `0.6.x` does not work ([README, lines 68-74](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/README.md#L68-L74)) | Relevant only to later live-client testing, not headless tests. |
| World data | First release run expands `datos/world.zip` to `bases/world.db`, described as about 240 MB, and creates `bases/auth.db` ([README, lines 82-97](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/README.md#L82-L97)) | Spell-catalogue combat tests need `world.db`; pure displacement tests do not. README's 240 MB is an approximate claim, not measured here. |
| Bomb metadata | `Bombs` loads `datos/bombas.json` and otherwise leaves an empty bomb table ([`Bombs.cs`, lines 25-42 and 66-104](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/Bombs.cs#L25-L104)) | Data-backed bomb tests require this tracked JSON as well as spell rows used by the tested path. |
| State flags | `SpellStates` loads `spell_states.json`; without it no flagged-state rules are loaded ([`SpellStates.cs`, lines 133-179](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/SpellStates.cs#L133-L179)) | Preserve this data file for tests involving invulnerability, pinned movement, pacifism, incurability, or portals. |

## Smallest viable source probes

These commands assume a later, separately authorized source checkout at the pinned commit. They intentionally omit the game client, release archives, server launch, and account database.

### Tier 1: no client, no `world.db`

Prerequisites:

1. Windows.
2. A checkout fixed to commit `683ab49c73f65b7c4ab7c1134c448f7fa850f6bc`.
3. .NET 10 SDK and network/disk permission for NuGet restore.
4. Enough temporary disk for the source tree, NuGet cache, and compilation outputs. Their exact sizes are not published and were not measured in this no-download audit.

From the repository root:

```powershell
dotnet test Jondo.Unity.Tests/Jondo.Unity.Tests.csproj `
  --configuration Release `
  --filter "FullyQualifiedName~Jondo.Unity.Tests.Combat.PullTests|FullyQualifiedName~Jondo.Unity.Tests.Combat.PushDamageTests|FullyQualifiedName~Jondo.Unity.Tests.Combat.TeleportSymmetryTests"
```

Why this subset: the selected classes exercise coordinate and fighter logic without requesting catalogue spell rows. The tests cover pull stopping rules and direction ([`PullTests.cs`, lines 25-103](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/PullTests.cs#L25-L103)), collision-damage arithmetic and boundaries ([`PushDamageTests.cs`, lines 6-115](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/PushDamageTests.cs#L6-L115)), and symmetric teleport coordinates, invalid cells, involution, and previous-cell memory ([`TeleportSymmetryTests.cs`, lines 7-71](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/TeleportSymmetryTests.cs#L7-L71)).

Use exact class or method filters. Do not broaden this to all `Combat` tests without first provisioning data.

### Tier 2: data-backed spell behavior, still no game client

Prerequisites are Tier 1 plus extraction of the tracked database archive exactly as CI does:

```powershell
New-Item -ItemType Directory -Force bases | Out-Null
Expand-Archive -Path datos/world.zip -DestinationPath bases -Force
Get-Item bases/world.db | Select-Object Name,Length
```

Then run narrowly selected data-backed classes, for example:

```powershell
dotnet test Jondo.Unity.Tests/Jondo.Unity.Tests.csproj `
  --configuration Release `
  --filter "FullyQualifiedName~Jondo.Unity.Tests.Combat.SramTests|FullyQualifiedName~Jondo.Unity.Tests.Combat.BombExplosionTests|FullyQualifiedName~Jondo.Unity.Tests.Combat.ComboTests"
```

For the broadest trustworthy project result, follow the CI order: extract `world.db`, restore, build, then test ([`ci.yml`, lines 22-48](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/.github/workflows/ci.yml#L22-L48)). If minimizing work, retain exact method-level filters after inspecting the desired test because some classes mix pure setup with `EffectEngine.Resolver` calls that load spell rows.

### Precise blockers to a smaller run

- There is no separate lightweight combat test project. The relevant tests compile through the broad Windows project references ([project, lines 21-34](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Jondo.Unity.Tests.csproj#L21-L34)).
- A filtered `dotnet test` still restores/builds the project graph unless an already built output is supplied.
- Spell effects are read from `SpellLevels` keyed by spell and grade; database/query failures are caught and cached as empty results ([`SpellEffects.cs`, lines 191-251](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/SpellEffects.cs#L191-L251)). Thus absence of data may masquerade as no effects rather than fail loudly.
- CI's explicit warning about 64 early-returning tests means a green no-DB whole-suite run is incomplete evidence, not an acceptable oracle baseline ([`ci.yml`, lines 22-27](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/.github/workflows/ci.yml#L22-L27)).
- Live-client comparison remains blocked until an explicitly supported Dofus build is available; the currently installed differing version must not be reused by assumption.

## Combat semantics found in implementation and tests

Labels used below:

- **Tested**: an assertion exists in the pinned source.
- **Implemented**: production code contains the behavior, but that statement alone is not a passing-run claim.
- **Claimed**: documentation/commentary states it; it was not independently executed here.

### Sram: traps and invisibility

- **Tested:** the Doble's grade-1 end-turn swap/death hook belongs to the double, while the Sram receives the grade-2 state ([`SramTests.cs`, lines 17-42](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/SramTests.cs#L17-L42)). This is evidence that grade is semantically significant, not merely cosmetic.
- **Tested:** Arsénico applies damage at the target's turn start rather than immediately on cast ([`SramTests.cs`, lines 44-63](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/SramTests.cs#L44-L63)).
- **Implemented:** revealing invisibility removes active visibility-effect buffs ([`EffectEngine.cs`, lines 4030-4042](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Handlers/EffectEngine.cs#L4030-L4042)); effect 150 is normalized to value one when its catalogue values are empty ([same file, lines 5121-5126](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Handlers/EffectEngine.cs#L5121-L5126)).
- **Implemented:** an invisible fighter retains the last cell visible to opponents ([`Fighter.cs`, lines 219-222](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.World/Fights/Fighter.cs#L219-L222)).
- **Implemented:** the `Trampa` effect creates a ground object that triggers `AlPisar` ([`EffectEngine.cs`, lines 4045-4082](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Handlers/EffectEngine.cs#L4045-L4082)); traps, glyphs, and runes are distinct ground-object kinds and a trap is spent on its first step trigger ([`Glyphs.cs`, lines 6-12, 30-40, and 82-104](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/Glyphs.cs#L6-L104)).

### Roublard: bombs, detonation, and combo

- **Tested:** Detonador uses effect 1009; supported bombs map to an explosion; the explosion kills the bomb, reaches a radius-two circle, and carries elemental damage ([`BombExplosionTests.cs`, lines 63-95](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/BombExplosionTests.cs#L63-L95)). Nested explosion damage originates from the bomb and preserves element metadata ([lines 97-142](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/BombExplosionTests.cs#L97-L142)); a bomb cannot detonate twice in one chain ([lines 144-169](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/BombExplosionTests.cs#L144-L169)).
- **Tested:** targeting an occupied cell explodes immediately, while an empty cell places a bomb ([`BombExplosionTests.cs`, lines 171-218](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/BombExplosionTests.cs#L171-L218)). Wall-chain and adjacent-bomb propagation are tested, as are radius two and failure to propagate to a bomb outside the line ([lines 220-305](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/BombExplosionTests.cs#L220-L305)).
- **Tested:** bombs consume no ordinary summon capacity; dead bombs stop counting; the three-bomb cap is per Roublard; Tymobot remains distinct from bombs; and a fourth bomb is rejected before AP payment while a third succeeds and pays ([`BombRulesTests.cs`, lines 48-57, 90-188](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/BombRulesTests.cs#L48-L188)).
- **Tested:** combo has an 18-state catalogue ladder but displayed damage caps at its fifteenth value, 360%; progression, bomb-only ownership, a single active rung, and the level-15 cap are asserted ([`ComboTests.cs`, lines 23-167](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/ComboTests.cs#L23-L167)). Captured bytes are used to check combo announcements and bomb look changes ([lines 169-215](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/ComboTests.cs#L169-L215)); size is 100 plus live effect-1060 buffs and bonus-spell grade selection is tested ([lines 217-252](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/ComboTests.cs#L217-L252)); two combos per turn and unique buff-number/removal accounting are tested ([lines 257-305](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/ComboTests.cs#L257-L305)).
- **Implemented:** combo progression is derived from spells 20497 and 20500 in `world.db`, not hard-coded percentages. The manager reads ladder state and percentage/grade data through `SpellEffects` ([`Combo.cs`, lines 38-45 and 113-168](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/Combo.cs#L38-L168)). State add/remove processing enforces bomb-only combo ownership, no backward progression, old-rung cleanup, and unique stacked buff IDs ([`EffectEngine.cs`, lines 4754-4864](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Handlers/EffectEngine.cs#L4754-L4864)).

### Displacement and collision semantics

- **Tested:** pull stops at its center, stops adjacent when the center is occupied, chooses an off-axis direction, handles short pulls, and remains distinct from ordinary push direction ([`PullTests.cs`, lines 25-103](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/PullTests.cs#L25-L103)).
- **Tested:** collision damage is pinned to `blockedCells * (level/2 + pushDamage - pushResistance + 32) / 4`, including level, stat, resistance, zero/negative, floor, and overflow boundaries ([`PushDamageTests.cs`, lines 6-115](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/PushDamageTests.cs#L6-L115)).
- **Tested:** symmetric teleport uses coordinate reflection, is an involution, rejects invalid cells, and remembers previous cells ([`TeleportSymmetryTests.cs`, lines 7-71](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/TeleportSymmetryTests.cs#L7-L71)).
- **Implemented:** the effect engine handles symmetric teleport, delayed return movement, swaps, push/pull variants, walkability/occupancy, state-based pinning, and collision outcomes in one displacement region ([`EffectEngine.cs`, lines 4511-4681](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Handlers/EffectEngine.cs#L4511-L4681)).

### State and spell-grade semantics

- **Implemented:** loaded state flags drive invulnerability, movement pinning, pacifism, incurability, and portal exclusion ([`SpellStates.cs`, lines 70-129](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/SpellStates.cs#L70-L129)).
- **Implemented:** effects are cached by `(spell, max(1, grade))`, so absent/zero grade resolves as grade 1 ([`SpellEffects.cs`, lines 207-251](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/SpellEffects.cs#L207-L251)). The unlocked grade is the highest grade whose `MinPlayerLevel` is met, and AP cost is obtained from that row ([lines 341-366](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/SpellEffects.cs#L341-L366)).
- **Implemented:** child-spell chaining interprets the parent row's `diceSide` as the child grade and defaults to grade 1 ([`EffectEngine.cs`, lines 4704-4719](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Handlers/EffectEngine.cs#L4704-L4719)); effect removal can target a specific spell grade ([lines 4896-4905](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Handlers/EffectEngine.cs#L4896-L4905)).

## Network defaults if a later server run is authorized

The default is local-only. `ServerBinding.TcpAddress` selects `IPAddress.Loopback`; only `JONDO_PUBLIC_BIND=1`, `true`, or `si` selects `IPAddress.Any` ([`ServerBinding.cs`, lines 22-40](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Network/ServerBinding.cs#L22-L40)). HAAPI similarly registers only `localhost` and `127.0.0.1` prefixes unless public mode is set ([`HaapiServer.cs`, lines 14-29](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Network/HaapiServer.cs#L14-L29)).

The pinned server constants use Zaap `8888`, HAAPI `15881`, game `5555`, and game node `5556` ([`Program.cs`, lines 14-17](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Program.cs#L14-L17)); server startup opens chat on `6337` ([`Program.cs`, lines 204-206](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Program.cs#L204-L206)). The README describes JondoFix redirecting client services to localhost ports `8888`, `5555`, `15881`, and `6337` ([README, line 80](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/README.md#L80)). Keep public bind unset for any future local oracle run.

## Disk and release metadata

Official GitHub release metadata was read without downloading the assets. The release record reports `immutable: false`, so the asset IDs, byte sizes, and SHA-256 digests below are recorded as observed on 2026-11-02; unlike the source links above, the release record is not cryptographically pinned by the audited commit.

| Official asset | Compressed bytes | SHA-256 | Expanded/install size |
|---|---:|---|---|
| `Jondo-Full-v0.1.0-win-x64.zip` | 79,328,577 | `e97f56ebbc469fa600ba5dc463ad737bdc19ba2966c0b5febdc487264a2ead4e` | **Unknown**; official metadata does not publish it. |
| `Jondo-Launcher-v0.1.0-win-x64.zip` | 34,326,664 | `29293698f807be45cd2d71fafa570cfc48cbf81933c3edaa6944aa46b878bed8` | **Unknown**. |
| `tests.trx` | 3,383,734 | `14d30d1464d75c4787b97ae0c94adc0e0a1b45474da71e024c512a9f0112c4f1` | Not an install; parsed/in-memory footprint not reported. |

Primary metadata: [v0.1.0 release API record](https://api.github.com/repos/Keka-Bron/JondoEmu/releases/415692082), [asset 650036310](https://api.github.com/repos/Keka-Bron/JondoEmu/releases/assets/650036310), [asset 650036253](https://api.github.com/repos/Keka-Bron/JondoEmu/releases/assets/650036253), and [asset 650036384](https://api.github.com/repos/Keka-Bron/JondoEmu/releases/assets/650036384). The v0.2.0 prerelease had no assets when inspected through the [official releases API](https://api.github.com/repos/Keka-Bron/JondoEmu/releases).

GitHub's repository API reported repository `size: 374189` KB during the audit. That is GitHub repository metadata, **not** a clone, working-tree, NuGet, build-output, or installed-size guarantee. The expanded `world.db` is only documented as "about 240 MB" in the README; exact archive and expanded sizes were not downloaded/measured. The Dofus client, MelonLoader installation, source checkout, package cache, and build-output footprints remain unknown.

With 25.55 GiB free, the compressed release assets alone are small, but the unknown client/source/package/build expansion makes a full-install decision unjustified under the disk constraint. A later authorized Tier 1 source probe is the appropriate next measurement point: record free space before checkout, after checkout, after restore, and after build/test, then decide whether to retain or delete caches and outputs.

## Recommendation to the parent task

1. **First oracle attempt:** authorize only a pinned source checkout plus .NET 10 SDK use on Windows, then run the Tier 1 exact filters. Do not obtain a Dofus client, release ZIP, or DB for this first step.
2. **If Tier 1 is useful:** extract the repository's existing `datos/world.zip` and run exact Sram/bomb/combo methods or classes. Treat only data-provisioned results as evidence for catalogue-driven behavior.
3. **Do not perform live-client testing yet:** the installed game version is not the documented supported version. Require an explicit compatibility plan and separate disk budget first.
4. **Oracle interpretation:** JondoEmu is a useful independent executable specification, not ground truth by itself. Prefer assertions backed by both production implementation and focused tests; distinguish those from README claims and source comments as done above.
5. **Disk guardrail:** expanded release, client, source, NuGet, and build sizes are unknown. Measure each authorized stage rather than extrapolating from compressed assets or GitHub's repository-size field.

This completes the remote-only feasibility track. No claim in this note depends on executing JondoEmu locally.
