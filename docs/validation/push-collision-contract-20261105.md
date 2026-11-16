# Push collision implementation contract investigation (2026-11-05)

## Decision and evidence boundary

There is enough evidence to specify a narrow, explicit **candidate** for ordinary
straight-line action-5 collisions, but not enough to call a general Dofus
3.7.13.17 collision implementation exact. Do not replace the existing uncertainty
with an unqualified damage prediction. The missing evidence is identified below;
it is not the absence of any known formula or of all live characteristics.

Only this document was edited. No game, account, build, test, git command, or
production change was performed. Existing source and extracted data were read.
The reverse-engineer-anything skill and its connection/recovery guide were read.
REA managed inspection was not available through tool discovery. One existing
metadata-reader executable was run as a read-only probe, described below; its
output is ordinary local inspection, not REA Evidence.

## Existing application path

Paths in this section are relative to this worktree.

* `GivreClone.App/Services/Combat/SpellResolver.cs:11-18`: `SpellFighter`
  carries characteristics, elemental percent resistance and flat reductions,
  summon ownership, and state knowledge, but **no fighter level**.
* `SpellResolver.cs:128`: `Resolve` receives one `casterLevel` integer.
  `:191-207` walks at most the requested steps, stops on invalid/unwalkable or
  occupied cells, records movement, and emits `Push collision damage is not
  modeled` only for a blocked action 5. It currently discards the number of
  successful steps and the blocker identity. Full travel causes no collision
  event. `:309` averages effect Min/Max, and `:442` converts this amount to an
  integer for actions 5, 1103 and 6. This is existing simulation behavior, not
  evidence that a random distance can safely use a mean.
* `SpellResolver.cs:191-197` chooses both coordinate signs from target to caster.
  This makes every off-axis direction diagonal. The Jondo implementation below
  instead uses the dominant axis except for an exact diagonal. AoE origin also
  differs: this resolver always uses caster position; Jondo chooses the effect
  center except when target equals center. Do not silently broaden a damage-only
  patch to assert those trajectories exact.
* `SpellResolver.cs:161-175` has a shared damage application path with shield
  before life, clamping, death, target side, and state flags. A collision event
  must preserve action 5 and must not run through elemental `DamageEstimator`.
  Elemental resistance dictionaries do not encode push resistance.
* `GivreClone.App/Services/Combat/DamageEstimator.cs:8-17` defines elemental
  characteristic IDs but no push IDs. Its estimate explicitly excludes push.
* `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:196-206` copies all
  fighter characteristic dictionaries into the resolver board. `:488-489`
  forwards snapshot CasterLevel. **`:506-511` rejects every blocked movement,
  and requires ForcedMovementSafe for non-teleport movement.** Therefore adding
  resolver collision damage alone will not make a wall collision beat a direct
  damage spell in live planning. A separate, evidenced admission change is needed;
  deleting the blocked guard globally would also admit unsafe pulls and other
  interactions. ForcedMovementSafe defaults false (`:46`) and the inspected live
  snapshot construction does not set it.

### What live inputs actually exist

`GivreClone.App/Services/LaunchService.Combat.Turn.cs:962-1005` merges owner
characteristics with live owner stats and supplies each observed fighter's
`stats?.Characteristics` unchanged. `:1080-1088` constructs the snapshot with
`CasterLevel: CharacterLevel(pid)`. `LaunchService.Character.cs:110` returns
zero when that owner level has not been observed.

`GivreClone.Protocol/FightStatsReader.cs:48-84` sums named characteristic
components and keeps arbitrary characteristic IDs, and `:88-111` retains the
whole dictionary on FighterStats. There is no 84/85 whitelist preventing those
inputs from reaching the board. They are available **when actually observed**;
neither a nullable dictionary nor a missing 85 establishes a known zero. No
coverage/completeness bit for those values reaches SpellFighter.

`FightStatsReader.cs:115-131` can read a monster's creature level, and
`LaunchService.Packets.cs:1313` retains it for challenge ordering. That value is
not present on PlannerFighter/SpellFighter. SummonerId is available, but the
summoner's level is not. Recursive casting can change caster to the effect
target (`SpellResolver.cs:339-344`), while Resolve still has the one owner
level. Consequently owner CharacterLevel is usable only for an established
owner-origin cast; it cannot be reused as the level of an arbitrary controlled
companion, enemy sub-caster, or summon. Evidence deciding whether a summon uses
its own or its owner's level is also missing. Zero/unknown owner level must not
be silently promoted to a real level-one or level-zero collision prediction.

## Concrete source evidence, with version qualification

### Dofus 3.6 emulator oracle (specific, not current-client authority)

Existing local tree:

`%USERPROFILE%/AppData/Local/Temp/givre-jondo-lab-20261102/source/JondoEmu-683ab49c73f65b7c4ab7c1134c448f7fa850f6bc`

Pinned source links:

* [EffectEngine.cs](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Server/Managers/EffectEngine.cs#L634-L748)
* [PushDamageTests.cs](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/Combat/PushDamageTests.cs#L6-L115)
* [Zone.cs](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.World/Maps/Zone.cs#L667-L819)
* [ZonePushTests.cs](https://github.com/Keka-Bron/JondoEmu/blob/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/Jondo.Unity.Tests/World/ZonePushTests.cs#L9-L128)

`EffectEngine.cs:670-677` implements exactly:

```text
if blockedCells <= 0: return 0
per = integer_truncate(level / 2) + pusher84 - target85 + 32
damage = max(0, integer_truncate(blockedCells * per / 4))
```

Its tests state that 127 collision messages among 401 captures motivated it;
that is the author's claim, not independently inspected raw capture evidence.
The README supports 3.6.10.10/11, not the installed 3.7.13.17 (see also
`docs/codex/JONDOEMU_FEASIBILITY_20261102.md:19-20`). This is Unity/Dofus 3
evidence, not Retro, but still requires current-version corroboration.

`EffectEngine.cs:714-748` applies it only to normal action 5, not 1103; excludes
portal and Roublard wall stop kinds (ordinary terrain obstacle is distinct);
uses pusher PushDamage plus live buff 84, target stat plus buff 85; and gives
the immediate occupying fighter `damage / 2`. It does **not** read that
blocker's 85 and does **not** propagate to further fighters. This is an exact
description of the emulator, not proof those omissions match the server.
`:727-731` explicitly calls percent characteristic 158 multiplication
**INFERRED** and uses Math.Round. It must not become an exact rule by copying it.

`Zone.cs:682-690` uses the dominant axis for off-axis targets, exact coordinate
signs for an aligned/diagonal target; `:714-723` chooses effect-center/caster
origin. `:734-819` counts one per coordinate step and returns
`BlockedCells = cells - successfulSteps`. There is no diagonal halving in this
method. `ZonePushTests.cs:45-96` independently anchors full travel, immediate
terrain obstacle, blocker identity, and partial travel. `Fighter.cs:45` uses
the **pusher's** StatLevel, with scaling/player cap, not a separately looked-up
summoner level; it is not a summon inheritance test.

### Characteristic IDs from extracted client data

Existing local file:

`%USERPROFILE%/Downloads/billbot-study-st01a19bff/DBDofusUnity/datas/bundles/data/CharacteristicsDataRoot.json`

* `:832-835`: id 84, keyword `pushDamageBonus`, asset `tx_push`.
* `:844-847`: id 85, keyword `pushDamageReduction`, asset `tx_pushReduction`.
* `:1456-1459`: id 158, keyword `pushDamagePercent`.

This is stronger than guessing an elemental resistance ID. This checkout's
data extraction has not been matched here to installed 3.7.13.17 content; it
cannot establish current percent semantics, missing-value defaults, or formula.
BillBot's `src/core/engine/fights/attack/push.py:16-44` independently implements
the integer-level candidate with zero resistance only, and calls it a mirror
of PushUtils.GetCollisionDamage. That comment is not a recovered current-client
method body. BillBot does not establish propagation.

### Installed 3.7.13.17 evidence and exact limit

`docs/validation/combat-visuals-signatures-371317-20261104.md:15-22` identifies
the installed GameAssembly and metadata hashes. The existing address-annotated
extraction is under
`%USERPROFILE%/AppData/Local/Temp/givre-state-cpp2il-20261104/dll-addresses`.

Ran existing inspector (no compile, no game loading):

```text
dotnet %USERPROFILE%/AppData/Local/Temp/givre-state-reader-20261105/bin/NativeAddressLookup/debug/NativeAddressLookup.dll %USERPROFILE%/AppData/Local/Temp/givre-state-cpp2il-20261104/dll-addresses/Core.dll find:Push
```

It exited 1 with no matches. Its inspected source in
`docs/validation/NativeAddressLookup/Program.cs:20-29` only searches **type
names**, not all method names or native bodies. Thus it does not prove the
formula absent; it locates the limit: no named PushUtils type is recoverable
through this query in this obfuscated Core.dll. The dump is address metadata,
not executable method semantics. A current collision estimator native method
attributed from call-site/dataflow, or raw version-pinned server captures, is
still required. No assertion that native method analysis was completed is made.

### Historical web evidence to exclude from an exact contract

[JeuxOnline formula wiki](https://forums.jeuxonline.info/sujet/801243/les-formules-de-calcul-dans-dofus)
explicitly labels its push section version **1.27.0**. It gives the random
`(8 + R * level / 50) * distance + bonus - resistance` rule and repeated half
damage minus each subsequent target's resistance. That is historical/Retro-era
evidence, not a competing 3.7 formula. Do not import its propagation into the
Unity candidate merely because the wiki is detailed.

[Stuffgaming's Do Pou article](https://stuffgaming.fr/dofus-comment-fonctionnent-les-do-pou/)
quotes a diagonal distance conversion `ceil(line distance / 2)`, but does not
pin it to 3.7 or supply the exact damage arithmetic. This conflicts with simply
trusting Jondo's unconverted coordinate steps and remains a question to measure,
not authority to halve either movement or damage automatically.

## Minimal implementation contract supported by this investigation

1. Keep collision damage distinct from direct elemental damage: preserve action
   5 in damage events, and combine event totals only in the existing scoring
   layer. The spell effect Min/Max is **push distance**, not base HP damage.
2. First implementation may expose a clearly labelled estimate only for an
   ordinary owner-origin, straight-line, fixed-distance action 5 with confirmed
   trajectory, known positive even owner level, known pusher84/target85, and confirmed
   absence of special movement/state/portal/mark interactions. Use observed zero
   explicitly; do not manufacture zero for an absent dictionary entry.
3. Preserve requested distance, successful displacement, blocker kind and actor
   identity. No collision if full distance completed; no damage for 1103 or
   action-6 pull on this evidence. Ordinary terrain/edge collisions and fighter
   collisions must remain distinguishable from special walls and portals.
4. For that narrow **estimate**, the independently corroborated candidate is
   `max(0, (requested - moved) * (floor(level/2) + bonus84 - resist85 + 32) / 4)`
   with final integer truncation, nonnegative final damage, and arithmetic wide
   enough to preserve ordinary numeric results. Do not apply element stats,
   elemental resistance, direct-spell power, or direct critical bonuses.
5. Keep fighter-blocker secondary damage, longer chains, odd-level rounding,
   diagonal pushes, nonzero/unknown percent158, summon/sub-caster/companion level,
   and special immunity interactions explicitly unsupported until corroborated.
   If an estimate includes Jondo's immediate `floor(primary/2)` secondary hit,
   label that emulator behavior rather than exact Dofus 3.7 semantics.
6. Apply an admitted estimate through the existing shield/life/death event path
   and retain immutable input/output snapshots. Report uncertainty rather than
   an invented collision total when required inputs are missing. Do not permit
   the planner's generic blocked-movement rejection to erase the new event, nor
   remove it indiscriminately; any admission change needs a narrow predicate
   matching the supported collision case and established movement safety.

This is not an authorization to ship a general exact formula: items 2-6 describe
the smallest defensible *labelled estimate*. Exact 3.7 support remains gated by
the evidence below.

## Independently derived test vectors

These values are arithmetic cross-checks, not passing application-test claims.
Let B be requested distance minus successful steps. Inputs are L, bonus84,
resist85, B; all are explicit, not defaulted from missing observations.

| Inputs | Candidate result | What it distinguishes |
|---|---:|---|
| 200, 0, 0, 0 | 0 | Full travel has no collision |
| 200, 0, 0, 1 / 2 / 3 / 4 | 33 / 66 / 99 / 132 | Distance weighting |
| 200, 100 / 140 / 180 / 220, 0, 1 | 58 / 68 / 78 / 88 | Bonus84 contribution |
| 200, 561, 30, 2 | 331 | `2 * (100+561-30+32) / 4`, resistance inside scaling |
| 200, 561, 174, 3 | 389 | One final truncation, not 3 times truncated per-cell damage |
| 1, 0, 500, 3 | 0 | No healing from large resistance |
| 200, 0, 0, requested4/moved1 | 99 | Successful movement is subtracted, not total requested distance |

Exact missing-evidence probes (record output; do not assert either column as
current truth):

| Scenario | Competing predicted outputs |
|---|---|
| L165, bonus3, resist0, B3 | **87** with integer L/2; **88** with real L/2 before final floor |
| Primary132 into blocker with85=40 | Jondo:66; subtracting40 after half:26; recomputing resistance inside half-scaled formula:46 |
| Primary132 into two aligned blockers, both85=0 | Jondo implementation:66 then no third hit; repeated half-chain:66 then33 |
| Requested3 diagonal, immediately terrain-blocked, L200, no bonus/resist | Unconverted B3:99; converted ceil(3/2)=2:66 (needs actual distance convention established) |
| L200 no bonus/resist, B1, percent158=50 | Multiply33 then floor:49; Math.Round as Jondo:50 (its rule is expressly inferred) |

The existing L165/B2 oracle anchor returns57 under both integer-level and real
level division. Its comment says114.5 per four whereas its C# code computes114;
the assertion alone cannot resolve that discrepancy. Use the discriminating
L165/bonus3/B3 vector above instead.

For integration, pair the geometry fixtures with independent direct-damage
effects, shield clipping and kills; verify the collision is action5 while the
direct hit retains its own action. Include a zero-travel collision, mixed ally
blocker, unknown85 and unknown level, 1103, pull, diagonal, and sub-caster cases.
Only promote planner selection of a collision over a direct spell after its
specific admission path is supported and observable, not from resolver arithmetic
alone. Existing `SpellResolverTests.cs:423-435` covers teleport blocking/swap,
not collision HP loss. `CombatForcedMovesReplayTests.cs:9-16,90-116` is a
synthetic 3.7.11.11 mapping reconstruction of slide handling; `:128-139` tests a
separate direct life-loss action91. It is not raw 3.7.13.17 collision evidence.

## Exact evidence still needed before calling this complete

* A version-identified 3.7.13.17 ordinary collision with level, 84, 85,
  requested/moved distances and authoritative HP/shield events; use one of the
  independent vectors above to verify the candidate rather than a level200
  zero-stat example alone.
* An odd-level, odd-residue example that discriminates intermediate rounding.
* Two/three-fighter collisions with unequal resistances to determine whether
  secondary resistance is reapplied, from which pre/post-resistance quantity,
  and whether and how farther actors receive damage.
* Diagonal distance accounting, origin for off-axis/AoE effects, and the
  relationship of effect distance to coordinate steps.
* A nonzero158 observation to establish multiplication order and rounding, or
  an explicit known-zero restriction for the first supported case.
* Actor-scoped pusher level and summon/owner inheritance evidence; a selected
  account's level is not a substitute for every caster on the board.
* A source-backed completeness rule for missing push characteristics, or
  explicit unknown handling through the adapter. The current pipeline preserves
  present values but does not supply that knowledge contract.

These are concrete missing semantics and live knowledge fields. They prevent
an exact general implementation claim; they do not justify reverting to a
generic unsupported finding that ignores the available formula and inputs.
