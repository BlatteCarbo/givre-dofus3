# BillBot source study - 2026-11-02

## Evidence boundary

Studied primary Python source from `Valentin-alix/BillBot`, detached at
`5373d0dea2ffa6731cf81dd716f04d0c95d7ed14` (HEAD verified locally). Source is
present, not merely a README or binary distribution. All upstream links below
are pinned to that revision. Scratch clone: `%USERPROFILE%/Downloads/billbot-study-st01a19bff`.
No upstream application, tests, binaries, or live actions were executed.

Local comparison: `docs/codex/RECOVERY_COMBAT_20261102.md` and sibling
`combat-planner-slice1/GivreClone.App/Services/Combat/{CombatTurnPlanner,SpellDescriptor,AutoSpellRotation}.cs`.
These local paths describe the inspected working copies, not immutable upstream evidence.

## Implemented mechanisms and limits

- **Turn planning:** the [turn driver, lines 33-159][turn] prioritizes urgent breed
  support, emergency self-heal, repeated move-and-attack, ordinary heal, self-buff,
  breed support, retry without reserved AP, then movement/pass. Each callback
  obtains a fresh context; [lines 180-218][callbacks] schedule casts and restart
  selection after completion. The [attacker, lines 61-254][attacker] enumerates
  reachable walking origins, damage spells, aim cells, zones and positive weights.
  This is greedy next-action selection with live replanning, not a simulated
  multi-cast turn search. Its [lethality helper, lines 8-45][lethality] explicitly
  extrapolates best damage/AP rather than simulating a turn.
- **Learned spells and grades:** [fight frame, lines 130-149][spells] stores
  `human_spells`; [spell filtering, lines 52-86][filter] resolves learned grades
  through `[spell_level - 1]`. The variant handler instead appends a missing
  variant using its first static level: do not copy that as authoritative grade
  acquisition. [Damage spell collection, lines 122-145][damage-spells] rejects
  nonempty `statesCriterion`; support collection does not implement a general
  state-criterion evaluator. [Turn validity, lines 88-139][filter] handles AP,
  initial/global/minimum cooldown, per-turn counts and active stacks.
  [Cast validation, lines 12-53][validator] adds map-cell and LOS checks, but its
  per-target cap uses a spell-wide turn count, not target-indexed history. This
  validator is not evidence of complete masks, states, triggers or cast legality.
- **Damage and movement:** [scoring, lines 46-119 and 122-263][weights] includes
  co-zone damage effects, kill/summon weighting, ally-hit penalties, life recovery
  and reduced weight for suspected invisible cells. [Damage calculation,
  lines 52-176][damage] includes monster-grade elemental resistance, stats, spell
  modifiers and expected critical damage; unknown resistance becomes zero and
  special redirections are excluded. [Reachability, lines 24-81][reach] stops
  expansion adjacent to enemies rather than modeling full AP/MP tackle losses.
  [End movement, lines 91-145][retreat] uses enemy MP plus maximum range, then
  distance fallback, not a legal incoming-spell threat search.
- **Displacement:** [push estimation, lines 16-47][push] counts blocked push
  squares and estimates collision damage with zero push resistance. It is called
  by [damage scoring, lines 139-241][weights]. It does not update fighter positions
  or create a follow-up board. No generic combat pull/swap/teleport transition
  planner was identified in the traced attack and turn paths. Thus this is not
  implementation evidence for Attirance, Transposition or Entourloupe semantics.
- **Heal/shield:** [heal helpers, lines 17-47][heal] define 25% emergency and 50%
  ordinary thresholds and estimate flat/max-life healing. [Attacker, lines
  256-289][self-support] clips healing to missing HP and ranks heal/AP; self-buff
  selection chooses the cheapest valid candidate. [Buff filtering, lines 15-22][buff]
  admits offensive self-buffs and shields. Shield bonuses also affect attack life
  recovery weight; this is not an incoming-damage-aware shield plan or ally-heal
  search. [Effect helpers, lines 103-175][effects] contain description-based
  classification and substring self-mask checks, not a full mask interpreter.
- **Class-specific support:** the [breed registry, lines 12-23][breeds] registers
  only Sacrier. [Sacrier, lines 18-138][sacrier] uses Epee Vorace `12744`, summon
  capacity and placement, AP reservation, and Mutilation `12737` HP/prior-cast
  rules. It skips support when the approximate lethality helper predicts a finish.
  The [Sacrier tests, lines 58-110][sacrier-tests] exercise threshold and prior-cast
  rules with mocks; inspected, not executed. This is a small rule set, not full
  Sacrier mechanics.
- **Companions:** entity observation is not actor control. [Context construction,
  lines 143-200][context] always selects `player.character_id`, player learned
  spells and player stats; [cast completion, lines 23-32][cast] accepts only that
  player's author ID. [Turn tracking, lines 312-323][actor-turn] is likewise owner
  based. No actor-scoped companion/slave-control planner was identified in the
  inspected source paths. These paths cannot justify replacing GivreClone's
  controlled-actor budgets, cooldowns or revoke/epoch lifecycle with owner defaults.
- **Sram/Roublard:** no class-specific implementation was identified. Targeted
  searches of `src/core` for Sram/Roublard, trap/bomb and control-switch terms found
  no matches; the positive registry evidence above contains only Sacrier. Enemy
  invisibility tracking and suspected-cell scoring are not Sram stealth/trap
  planning. No trap placement/activation, bomb ownership/arming, wall, combo or
  detonation state transitions were found. Static spell/protocol data alone would
  not establish that these mechanics are implemented.
- **Renderer:** [GridView, lines 32-53 and 82-151][grid] is a separate PyQt QWidget
  rendering map-cell polygons with QPainter/QPicture and signal-fed actor state.
  This is not an injected Unity tactical renderer. No `parentTiles`/`GameObject`
  root discovery was identified in the searched core source. It supplies no fix
  for GivreClone's empty `parentTiles` or missing `map_<id>` hierarchy fallback.

## Practical next changes for GivreClone

1. Keep the existing bounded beam search, legal walk/cast histories, simple
   teleport state transitions and exposure-aware heal/shield scoring. BillBot's
   greedy loop and fixed HP thresholds are useful comparisons, not upgrades to
   that planner. Replanning after acknowledged actions remains a useful pattern.
2. Extend damage scoring with verified target resistance and same-zone effect
   composition, using the full characteristic dictionaries now retained locally.
   Preserve uncertainty for unknown resistance; do not copy upstream's zero default,
   localized description parsing or incomplete target-mask checks as correctness.
3. Gate new displacement on exact learned grade, immediate trigger, mask/state and
   destination rules. Add a board transition plus follow-up legality tests before
   scoring push/pull/swap as setup. BillBot's collision estimate is only a heuristic
   reference; the local recovery note already explains why grade-1 names/examples
   do not establish live grade-3 Attirance/Transposition behavior.
4. Keep companions actor-scoped and unsupported mechanics fail-closed. Build Sram
   traps and Roublard bombs only after mark lifecycle/ownership and trigger evidence
   is captured. Do not generalize the Sacrier registry into presumed class coverage.
5. Continue Unity renderer-root diagnostics separately; a standalone PyQt grid is
   not evidence for the active map's native hierarchy, visibility or materials.

## Exact missing evidence / version assumptions

- No runtime validation against the user's installed game build, server or learned
  spell set. No claim that upstream tests pass or live combat succeeds.
- Grade-list ordering, action/effect IDs, protocol mapping and static JSON data must
  match the installed version. [DataReader, lines 71-124][data-reader] reads external
  data bundles; source formulas alone do not verify those bundle contents. French
  text heuristics and the first-grade variant fallback are version-sensitive.
- Still needed locally: authoritative learned grade plus complete raw effects,
  masks/state predicates, nested triggers/templates and destination rules for each
  newly supported displacement/support spell; target-specific cast history and
  observed before/after position/resource/state events for transition validation.
- Still needed for companions: control acquisition/revocation events and actor-owned
  spell/grade/stat/cooldown snapshots. Upstream owner-only paths do not supply them.
- Still needed for Sram/Roublard: mark creation/removal/activation payloads, owner,
  cells/zone, duration, arming/trigger order, bomb combo/wall/detonation rules and
  replay fixtures. No such implemented class model was established here.
- Still needed for tactical rendering: active-map native root/descendant evidence,
  supported visibility/material accessors and observed overlay output. BillBot
  provides no matching Unity root implementation.

The [README, lines 1-17][readme] broadly describes Dofus Unity automation; it does
not establish multi-action search, complete class coverage, companion control or
native tactical-root support. Those capabilities remain unsupported by the
inspected implementation rather than inferred from project positioning.

[turn]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/behaviors/farms/fight/fight_turn_behavior.py#L33-L159
[callbacks]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/behaviors/farms/fight/fight_turn_behavior.py#L180-L218
[attacker]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/attacker.py#L61-L254
[lethality]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/lethality.py#L8-L45
[spells]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/frames/fight_frame.py#L130-L149
[filter]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/spell_filter.py#L52-L139
[damage-spells]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/spell.py#L122-L145
[validator]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/cast_validator.py#L12-L53
[weights]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/weight_calculator.py#L46-L263
[damage]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/damage_calculator.py#L52-L176
[reach]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/reachable_cells/fight_reachable_cells.py#L24-L81
[retreat]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/behaviors/farms/fight/fight_movement_behavior.py#L91-L145
[push]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/push.py#L16-L47
[heal]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/heal.py#L17-L47
[self-support]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/attacker.py#L256-L289
[buff]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/buff.py#L15-L22
[effects]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/effect.py#L103-L175
[breeds]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/breed_abilities.py#L12-L23
[sacrier]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/engine/fights/attack/breed/sacrier.py#L18-L138
[sacrier-tests]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/tests/test_engine_logic/test_sacrier.py#L58-L110
[context]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/states/game_state.py#L143-L200
[cast]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/behaviors/farms/fight/fight_spell_behavior.py#L23-L32
[actor-turn]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/core/frames/fight_frame.py#L312-L323
[grid]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/src/gui/components/graphics/grid_widget.py#L32-L151
[data-reader]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/DBDofusUnity/dofus_unity_reader/data_center/data_reader.py#L71-L124
[readme]: https://github.com/Valentin-alix/BillBot/blob/5373d0dea2ffa6731cf81dd716f04d0c95d7ed14/README.md#L1-L17
