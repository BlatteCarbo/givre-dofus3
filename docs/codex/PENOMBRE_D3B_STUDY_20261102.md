# Pénombre / D3B study for GivreClone combat AI

Date: 2026-11-02. Research only; no combat implementation, builds, tests, or game actions were performed.

## Sources, revisions, and licenses

| Source | Examined revision | License and reuse decision |
| --- | --- | --- |
| `https://github.com/Sokenzane/Penombre` | `ac9ed3f6c98fdc76984f23a82568f1d938cdf342` | GPL-3.0-or-later: `LICENSE:1-3`, and the explicit version-or-later grant in `src/Penombre.cs:4-7`. Do not transplant its implementation into GivreClone without a separate license-compatibility decision. Reimplement any independently useful UI idea instead. |
| `https://github.com/AerafalDev/D3B` | `009749503938ea93d7731817f2494b21f1ce8592` | MIT: `LICENSE:1-21`, copyright Aerafal 2026. Copying substantial portions would require retaining the copyright and permission notice (`LICENSE:12-13`). This study recommends independently implementing mechanisms against our own data and contracts, not copying its files. |
| GivreClone | HEAD `e66edcc70fc39e19f58424da51ab6bab73df2991`, plus the existing worktree | Reviewed in `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`. Existing unrelated modified/untracked files were left alone. All local references below describe the worktree as read, not a clean checkout of HEAD. |

External references below are repository-relative at the revisions above. For example, a D3B reference `src/D3B.Engine/Tactics/FightTactics.cs:235-312` resolves under `https://github.com/AerafalDev/D3B/blob/009749503938ea93d7731817f2494b21f1ce8592/`. Local references are relative to GivreClone's root. Temporary shallow clones were used only for reading.

**Source correction:** the supplied Pénombre project is not a bot and contains no combat AI. It is a click-through visual night filter. Its README describes the filter and visual features (`README.en.md:5-8,56-71`); the source says it injects nothing and reads/sends nothing to the client (`src/Penombre.cs:19-31`). Its source tree consists of `src/Penombre.cs`, `src/Interface.cs`, and `src/SelfTest.cs`, not combat or spell modules. Consequently it has no learned-spell rotation, mobility-spell logic, Zobal AI, healing, targeting, summons, or kiting implementation to compare. Every combat comparison below has this explicit Pénombre result; D3B is the applicable combat reference. Do not attribute D3B's features to Pénombre.

## Findings and top five recommendations

1. **Replace the automatic mode's one-time spell ordering with bounded sequence planning.** Evaluate move -> cast -> move, the best attack available after movement, and the enemy reply. Retain the existing manual rotation path. D3B's concrete mechanism is a beam search plus end-cell evaluation and first-action execution, not a collection of breed-specific rotations.
2. **Discover learned utility spells through effect semantics, not positive damage.** Keep the server's learned list authoritative, but include recognized heals, shields, buffs, state changes, mobility, push/pull, and summons as candidates. Preserve explicit exclusions and settings. Separate generic cast limits from damage metadata before doing this.
3. **Make real kiting and approach spells compete in the same search.** Enumerate firing cells for each affordable spell, account for AP as well as MP lost to tackle, reserve remaining MP for retreat, and compare walking with teleport/pull/swap. Distance is a tie-break after survivability and useful casts, not the sole objective.
4. **Implement active states and Zobal mask prerequisites before claiming class-complete automation.** Track server state/buff removal, evaluate `statesCriterion`, simulate ordered mask subcasts, and keep search diversity across resulting state sets so a zero-damage setup action survives until its unlocked attack.
5. **Choose support by effective healing/shielding and predicted damage, not priority alone.** Compare self and ally survival, overheal, shield expiration, AP opportunity cost, and the damage removed by killing or disabling an enemy. Existing HP thresholds remain explicit user constraints.

These are recommendations, not changes already made. The implementation order is data/legality -> state/effect transitions -> bounded planner -> broader class coverage. The top-five ranking is by user-visible value, not a claim that item 1 can ship before its prerequisites.

## Our current combat inventory

### Entry point, actual spells, and configuration

- `LaunchService.RunFightTurn` is in `GivreClone.App/Services/LaunchService.Combat.Turn.cs:118`. It constructs a priority rotation at `:489-508`; `AutoSpells` augments it using `LearnedSpells(pid)` and `LearnedSpellLevel(pid,id)`, not the whole breed catalog.
- `GivreClone.App/Services/Combat/AutoSpellRotation.cs:8-27` represents a candidate as ID, AP, and average damage. `Build` adds only learned IDs with positive AP and positive average damage; profile entries, including disabled ones, block automatic replacement. Added spells get `PerTurn=99`; actual limits must come from cast data downstream.
- Learned IDs and grades originate in `SpellsEvent` at `GivreClone.App/Services/LaunchService.Packets.cs:1342-1351`. Accessors/storage are in `LaunchService.Combat.cs:625-631,909`. Controlled fighters already have a separate context (`LaunchService.Combat.Controlled.cs:14-54`), and `LearnedSpells`/`LearnedSpellLevel` honor that context. Do not replace it with the player's spellbook.
- Breed data is already available for the UI: `GameNameResolver.ReadSpellVariants` unions both sides of variant pairs and `SpellsForBreed` exposes them (`GivreClone.App/Services/GameNameResolver.cs:318-367`). This is a picker catalog, **not evidence that both variants are learned or legal**. `ClassIconProvider.BreedId` maps class indices (`GivreClone.App/Services/ClassIconProvider.cs:32`). No per-breed automatic strategy was found in the inspected turn-selection path.
- `SpellConfig` has self/ally targeting, explicit buffs and summons, cast counts, cooldown, AP/turn/enemy conditions, self/target HP windows, range overrides, and `AllowApproach` (`GivreClone.App/Services/CombatProfile.cs:83-127`). `AutoSpells` defaults off (`:153-156`). Automatic discovery is therefore not absent; utility discovery and planning are.

### Geometry, movement, and turn execution

- `CanCast` checks min/max range, line/diagonal alignment and required LoS (`LaunchService.Combat.cs:28-59`); live range modifiers are applied in the turn (`LaunchService.Combat.Turn.cs:941-947`) and `SpellCombatLive` (`LaunchService.Combat.SpellMods.cs:64-70`). `CommitCastModifiers` requires server evidence before applying modifier effects (`:34-61`).
- When a spell cannot be cast from here, `RunFightTurn` looks for a reachable firing cell and retries that spell after moving (`LaunchService.Combat.Turn.cs:1103-1159`). `CombatPositionPlanner.FindCastCell` selects the cheapest reachable firing cell, then its supplied tie-break (`GivreClone.Protocol/CombatPositionPlanner.cs:7-27`). The tie-break includes AoE and nearby-contact penalties (`LaunchService.Combat.Turn.cs:630-649`).
- `CombatMovementDecision.CanReposition` and `CanMove` enforce passive/never-approach behavior (`GivreClone.App/Services/CombatMovementDecision.cs:6-12`). These are user policy constraints, not tactical scoring.
- End movement is still style-driven: aggressive approach, fleeing distance bands, and distance-maximizing retreat (`LaunchService.Combat.Turn.cs:1393-1495`). Fleeing can approach when outside all spell ranges; it is incorrect to describe ours as only running away. What is missing is joint attack/retreat optimization and enemy-reach evaluation.
- Adjacency is treated as a reason not to reposition (`LaunchService.Combat.Turn.cs:1110-1113,1410-1417`). Geometry helpers consume MP as path length (`LaunchService.Combat.cs:107-162`); they do not model proportional AP/MP loss from tackle.
- `CombatPlacementSelector.Select` ranks placement by nearest-enemy distance and style (`GivreClone.App/Services/CombatPlacementSelector.cs:10-26`), called by `ChoosePlacementCell` (`LaunchService.Combat.PreFight.cs:165-174`). It does not predict damage or enemy reach.
- Challenges already constrain spells, targets, zones and end cells (`LaunchService.Combat.Challenges.cs:214-379`; turn integration `LaunchService.Combat.Turn.cs:597-624,1299-1391`). Script-owned turns, controlled turns, turn epochs, and existing cast/movement confirmation barriers must survive any planner integration.

### Current utility and evaluation limits

- Self spells are explicitly configured; a non-damaging self spell is effectively a buff and follows its conditions (`LaunchService.Combat.Turn.cs:887-931`). Ally spells choose the lowest-HP castable ally **without dedicated movement to heal** (`:956-979`). Self/target HP conditions are already evaluated (`LaunchService.Combat.cs:816-829`).
- Explicit summons choose the first legal free cell and respect the profile's live-summon cap (`LaunchService.Combat.Turn.cs:1013-1032`). Ownership/team tracking already protects summons (`LaunchService.Combat.cs:662-711`). This is not missing basic summon support; missing pieces are discovery, placement value, effect prediction, and downstream usefulness.
- Automatic offensive ordering uses damage, AoE hits and kills once before the main rotation (`LaunchService.Combat.Turn.cs:791-850`; `GivreClone.Engine/SpellChoicePlanner.cs:16-26,39-53`). It is not an AP-optimal sequence search. `KillBudget` is explicitly optimistic about geometry and ignores target resistance (`LaunchService.Combat.Turn.cs:525-552`). `BurstPlanner.Best` is a theoretical AP knapsack, not the live controller (`GivreClone.App/Services/Combat/BurstPlanner.cs:18-49`).
- `CombatSimulator.SimulateTurn` runs the priority behavior on a flat board, with a state containing self budgets and enemies, rather than full allies, shields, states, resistances and enemy replies (`GivreClone.App/Services/CombatSimulator.cs:9-18,24-62,96-126`). Its header's statement about AoE is stale relative to the `AoeHits` representation (`:79-83`); do not infer no AoE support from the comment. It is not yet a suitable transition model for mask/teleport/support search.
- `ReadSpellDamage` reads action IDs, masks and durations but retains only selected damage/zone/criterion/modifier projections (`GameNameResolver.cs:547-664`). It drops non-damaging spells from its damage dictionary at `:660-664`, taking their generic cast caps out of that projection too. `SpellStatesCriterion` explicitly says live evaluation is not wired (`:481-490`); the turn gates do not consult it. A parser already exists at `GivreClone.Protocol/StateCriterion.cs:18-99`, with intentionally limited grammar and fail-open handling. Reuse what it supports, not the assumption that diagnostics mean active enforcement.

## Gap table

Effort is relative engineering scope, not a delivery promise: S = localized change; M = several cooperating data/rule changes; L = engine plus integration and fixtures; XL = a substantial new mechanism. Risk describes correctness risk. `P0` means Pénombre has no combat feature, as established by `src/Penombre.cs:19-31`; it applies to every combat row.

| Feature | Theirs file:line | Ours file:line or missing | Recommended port / effort / risk |
| --- | --- | --- | --- |
| Actual learned spells without manual configuration | D3B `src/D3B.Bot/Fights/CharacterSpells.cs:30-64,83-104`: server list, grades/unlocks, selected variant, fight/transformed context and weapon blow; P0 | `LaunchService.Combat.Turn.cs:489-504`, `Combat/AutoSpellRotation.cs:17-27`: already learned, but only damage auto-added | Extend the candidate schema with recognized effects and legal targets, preserving manual overrides/exclusions. M; medium, especially variants and transformed contexts. Do not blindly auto-add the breed picker. |
| Cast limits independent of damage | D3B `src/D3B.Engine/Fights/CastRules.cs:153-214`, `CharacterSpells.cs:83-104`: AP, states, cooldown, turn/target caps, free/taken cells, summon slots | `GameNameResolver.cs:575-585,660-664`; turn caps use `GetSpellDamage` (`LaunchService.Combat.Turn.cs:863-868`) | Retain AP/cooldown/initial cooldown/caps/requirements for all grades separately from damage. M; high if support gets `PerTurn=99` before this. |
| Joint AP/MP sequence planning | D3B `src/D3B.Engine/Tactics/FightTactics.cs:235-312,400-428`: beam search, end walk, enemy reply; `Imagination.cs:180-204` spends cast AP | `LaunchService.Combat.Turn.cs:791-859,1393-1495`; `SpellChoicePlanner.cs:22-26` ranks one cast | Implement a pure bounded planner and execute its first action from live state. L; high integration risk, strongest overall value. |
| Teleport/approach/reposition spells | D3B `src/D3B.Engine/Effects/EffectActions.cs:53-59`, `Tactics/Imagination.cs:328-343,473-486`, `FightTactics.cs:547-587`: teleport/free-cell, pull and swap primitives | `Combat/AutoSpellRotation.cs:10-14` excludes non-damaging movement; turn movement is walking (`LaunchService.Combat.Turn.cs:1137-1159`) | Simulate recognized movement effects and compare AP spent approaching against follow-up attacks/escape. L; high, needs target masks and states. Not a named-spell script. |
| Zobal masks and prerequisite actions | D3B `src/D3B.Engine/Tactics/FightTactics.cs:122-150,277-283`, `Fights/CastRules.cs:217-231`, `Tactics/Imagination.cs:240-291,345-360`; `tests/D3B.Engine.Tests/Tactics/MaskTacticsTests.cs:127-188` | Criterion data/parser exist (`GameNameResolver.cs:481-490`, `StateCriterion.cs:18-99`), but automatic rotation has no mask/state planning | Track active state lifecycle, ordered subcasts/dispel/stack replacement and keep best sequence for each resulting state set. L; high. Specific missing capability, not evidence for a large breed switch. |
| Heals on self/allies | D3B `src/D3B.Engine/Tactics/Imagination.cs:142-145,322-324`, `FightJudge.cs:168-179,539-585`, `tests/D3B.Engine.Tests/Tactics/FightTacticsTests.cs:114-122` | Configured HP windows (`CombatProfile.cs:109-118`), self casts (`LaunchService.Combat.Turn.cs:887-931`), lowest-HP ally from here (`:956-979`) | Auto-discover heal effects, value effective healing and avoided death; search movement to reach allies as well as enemies. M after effect model; medium/high from masks, overheal and AP trade-offs. |
| Shields and incoming damage | D3B `src/D3B.Engine/Tactics/Imagination.cs:325-327`, `FightJudge.cs:177-179,539-638`: shield adds effective life and enemy threat affects value | Manual self/ally buff path above; no shield-specific expected incoming-damage comparison in the inspected scoring (`SpellChoicePlanner.cs:16-26`) | Value useful absorption before expiration, not nominal shield magnitude; compare killing/disabling the source. M/L; high without enemy spell data. |
| Real kite: shortest useful approach, cast, retreat | D3B `src/D3B.Engine/Tactics/FightTactics.cs:494-543,400-428`, `FightJudge.cs:602-638,818-877`: reachable casting cells and evaluated end walks | Closest firing cell already exists (`CombatPositionPlanner.cs:7-27`); fixed style/band end move (`LaunchService.Combat.Turn.cs:1457-1495`) | Compare complete candidate sequences over all affordable useful spells, reserve MP implicitly via end-cell search, maximize safe separation on ties. L; high. D3B scores threat, not an unconditional farthest-cell rule. |
| Tackle / escape costs | D3B `src/D3B.Engine/Fights/FightMoves.cs:64-94,136-191`: dodge/lock and tackle immunity, AP/MP loss on each departing cell | `LaunchService.Combat.Turn.cs:1110-1113` treats all contact as a blocker; `LaunchService.Combat.cs:107-162` budgets path length | Add budget-labelled reachability with AP/MP losses and movement states; retain non-dominated labels. M/L; high, roundings and multiple tacklers must match observed rules. |
| Buff/debuff duration and opportunity cost | D3B `src/D3B.Engine/Tactics/Imagination.cs:345-360`, `FightJudge.cs:724-805`: changed characteristics/states, retained durations, future damage gains/losses | Manually scheduled self/ally buffs and confirmed spell modifiers (`LaunchService.Combat.SpellMods.cs:34-70`); non-offensive slots are not reordered (`SpellChoicePlanner.cs:39-53`) | Simulate buffs/debuffs before subsequent casts; cap long-horizon value by expected fight duration and expiration. L; high. Keep existing modifier confirmation as authoritative execution. |
| Push/pull/swap tactical value | D3B `src/D3B.Engine/Tactics/Imagination.cs:328-343,435-470`: slide, collision, pull stop, swap | Adjacent targeting exists (`LaunchService.Combat.Turn.cs:981-1011`); no predicted displacement in `SpellChoicePlanner.Candidate` (`:16`) or `CombatSimulator.SimSpell` (`:49-62`) | Recompute reach, LoS, occupancy and danger after each displacement. M/L; high: anchored/carried states and effect origin matter. |
| Summon placement and usefulness | D3B `src/D3B.Engine/Tactics/Imagination.cs:473-515`, `Fights/CastRules.cs:365-383`; live monster/summon spell knowledge `src/D3B.Bot/Fights/ImaginedFights.cs:53-69` | Explicit summon flag, first valid free cell and profile cap (`LaunchService.Combat.Turn.cs:1013-1032`); controlled fighter context already exists | Auto-detect summon effects, enforce actual slots, score blockage/protection/future contribution. L; high. Do not treat D3B as complete autonomous/controlled summon AI: roadmap 9f still lists that work. |
| Target danger, resistances, enemy reach | D3B `src/D3B.Engine/Tactics/FightJudge.cs:277-384,539-638`, `src/D3B.Bot/Fights/ImaginedFights.cs:53-69`: effect-based damage and enemy grade spells | Target order and optimistic kill budget (`LaunchService.Combat.cs:92-101`, `LaunchService.Combat.Turn.cs:525-564`); `DamageEstimator` call explicitly without resistance (`:586`) | Use per-target resistance, threat and turn-order-aware kill value in the shared evaluation. L; high when monster knowledge is unknown. |
| Range modifiers and LoS | D3B `src/D3B.Engine/Fights/CastRules.cs:76-125,208-214`, `SpellRange.cs:35-77` | Already supports live modifiers, range bonuses/maluses and LoS (`LaunchService.Combat.SpellMods.cs:64-70`, `LaunchService.Combat.Turn.cs:933-947`) | Reuse ours, do not rebuild solved parts. Audit diagonal semantics independently: theirs uses line OR diagonal and diagonal steps unless proportional, ours uses conjunction and Manhattan distance (`LaunchService.Combat.cs:42-50`). M; high semantic risk, verify captured examples before changing. |
| Target masks, multi-effect zones | D3B `src/D3B.Engine/Fights/CastRules.cs:267-337`, `Effects/SpellEffects.cs:139-186`: per-effect reached fighters and masks | AoE safety already exists (`LaunchService.Combat.Turn.cs:611-624,1042-1093`), but extraction projects one selected zone (`GameNameResolver.cs:605-640,654-659`) | Keep per-effect masks and zones for mixed heal/damage/caster effects; do not mark an entire spell ally-only/enemy-only from one flag. L; high. |
| Glyphs/traps/portals/advanced class mechanisms | D3B `docs/roadmap.md:242` explicitly leaves these hard cases; `EffectActions.cs:130-136` returns `Other` for unrecognized effects; `Imagination.OnCell` only teleport/summon (`:473-486`) | No persistent hazard field/trigger model in `CombatSimulator.SimState` (`:36-45`) or current scoring; generic casts do not establish glyph/trap understanding | Neither source supplies a complete mechanism to port. Later implement persistent zones, triggers, visibility and walk hazards with fixtures. XL; very high. |
| Threat-aware initial placement | D3B `src/D3B.Engine/Tactics/FightTactics.cs:201-224` judges free starting cells | `CombatPlacementSelector.cs:10-26` is distance/style-based | Reuse the planner's evaluation for placement after it exists. S/M incremental; medium. |
| Replay validation of effects | D3B `scripts/fights/CastBench.cs:287-306,383-447,449-487,526-577`: compare prediction to observed heal, push, state, buff and movement outcomes | `Combat/CombatJournal.cs:25-27,48-75` is log reconstruction; `CaptureDecoder.cs:16-32` already supplies decoded packets; flat simulator is not differential prediction | Add an offline predicted-vs-observed transition report using existing capture/journal inputs. M/L; medium. High-value prerequisite for expanding class support. |

## Answers about class-specific approach and mechanics

### Learned spells and automatic rotation

D3B's `CharacterSpells.Of` begins with the server list, raises grades according to level, and fills newly unlocked breed spells while avoiding the unselected variant (`src/D3B.Bot/Fights/CharacterSpells.cs:30-64`). `InFight` prefers the fight-context list, then transformed spells, then the character's normal list; weapon spell 0 is appended only when the list and equipped supported weapon justify it (`:83-104`). `FightTactics.Casts` enumerates this knowledge, not a manual rotation (`src/D3B.Engine/Tactics/FightTactics.cs:494-543`). Its monster spells come from grade data (`src/D3B.Bot/Fights/ImaginedFights.cs:53-69`).

Our actual-spell source is already correct for normal and controlled actors. The important gap is throwing away non-damaging candidates before planning. Do not copy D3B's inference about level-up unlocks without checking our protocol/build evidence: our picker unions variants and could accidentally invent a spell the character did not select. Fight/transformation-context precedence and level-up grade refresh should be audited with captures, not inferred from display names.

**Concrete first implementation step (1):** change `GameNameResolver.ReadSpellDamage` (`GameNameResolver.cs:547`) to retain a separate per-grade cast/effect descriptor for every spell, including non-damaging ones. Keep `GetSpellDamage` as a damage projection. Extend `LearnedSpellInfo` and `AutoSpellRotation.Build` (`Combat/AutoSpellRotation.cs:8,17`) so recognized utility spells become candidates with legal target/effect semantics; update the candidate construction in `LaunchService.RunFightTurn` (`LaunchService.Combat.Turn.cs:493-497`). Retain disabled profile entries, grade lookup, and controlled-actor resolution. Do not launch every newly discovered utility once per rotation: only the planner chooses when it improves a sequence.

### Iop Bond, Ecaflip, Sacrieur, Xelor, and Zobal movement

D3B has **generic effect implementations**, not verified named scripts for each requested spell:

| User example | What the examined source establishes | What it does not establish |
| --- | --- | --- |
| Iop Bond | A recognized teleport action can move the caster to a free cell, and the search can spend AP then attack: `EffectActions.cs:57`, `Imagination.cs:473-486`, `FightTactics.cs:547-587`. | No dedicated `Bond` policy or pinned real spell-level fixture was found in the inspected combat paths. Need the installed spell's actual effects/flags and a captured transition. |
| Ecaflip approach/reposition | The same generic teleport/push/pull/swap actions are candidates, using current legal targets and budgets (`EffectActions.cs:53-59`). | No general claim of every Ecaflip dash's special semantics, random/conditional effects or class-complete behavior follows from those primitives. |
| Sacrieur Attirance / Transposition | Pull and swap are explicitly simulated (`Imagination.cs:331-343,435-470`), then later actions see changed positions. | A named spell's ally/enemy targeting, immunity and state conditions still come from its data. There is no evidence here of a hand-authored Sacrieur combo routine. |
| Xelor | Ordinary teleport/swap primitives are present. | Telefrag mechanism is explicitly listed as further hard-case work (`docs/roadmap.md:242`). Do not advertise complete Xelor AI from effect 4/8 support. |
| Zobal | Mask state transitions, nested casts, buff replacement and state-diverse search are specifically implemented and tested (`Imagination.cs:240-291,345-360`; `MaskTacticsTests.cs:127-188`). | Full Zobal movement spell coverage or all masks' live behavior is not established by the synthetic tests; the newest search correction is described as not yet measured in game (`docs/roadmap.md:246`). |

Pénombre supplies none of these mechanisms (`src/Penombre.cs:19-31`). Avoid a recommendation to copy imagined class modules that neither repository contains.

**Concrete first implementation step (2):** add recognized teleport/pull/push/swap effects to the descriptor retained by `GameNameResolver.ReadSpellDamage`; extend the pure state/transition surface currently exposed by `CombatSimulator.SimState`, `SimSpell`, and `SimulateTurn` (`CombatSimulator.cs:36,49,96`) or extract a shared pure transition model from those contracts. `RunFightTurn`'s movement-only `FindCastCell` block (`:1137-1159`) should call the proposed planner for automatic mode, letting a mobility cast compete with walking and preserving existing `Cast`, `TryMove`, `WaitAfterCast`, policy gates and epoch checks as execution machinery. `AllowApproach` governs repositioning intent, including spell-mediated movement; do not use teleport to bypass never-approach/passive constraints. For pull/swap, evaluate the resulting board, not just caster-target distance.

Ship a measured straight teleport plus attack slice first. Pull/swap need displaced target state and occupancy before they can be safely evaluated, and dash-to-adjacent behavior must not be mislabeled as generic free-cell teleport unless the actual action data says so.

### The specific Zobal gap

D3B's strongest class-related lesson is not a class-name switch. A mask can have zero immediate damage but unlock a stronger attack at the next search depth. `WithEachStates` preserves a best candidate from each otherwise-pruned state set (`FightTactics.cs:122-150`), and both pruning stages use it (`:277-283`). `CastRules.StatesAllow` evaluates the required mask (`CastRules.cs:217-231`). `Imagination` processes chained spell effects in order, allowing mask removal, new state, and old buff dispel to affect the next step. The test expects mask first then Furia (`MaskTacticsTests.cs:155-163`).

Our `SpellStatesCriterion` and `StateCriterion` are starting points, not an implemented mask AI. HP gates and geometry will not detect an unmet mask requirement.

**Concrete first implementation step (3):** retain active state/buff ownership and removal from server messages in the combat state handled by `LaunchService.Packets.cs`; wire `GameNameResolver.SpellStatesCriterion` through `StateCriterion.CanCast` into `SelfSpellGateOk` and `ScriptCanCast` (`LaunchService.Combat.cs:816`; `LaunchService.Combat.Script.cs:464`). Unknown/unsupported criterion syntax must be represented as uncertain, not confidently simulated as legal. Add ordered state/subcast/dispel/stack transitions to the pure model, then make automatic `RunFightTurn` search mask -> attack sequences. Preserve one best candidate per distinct relevant state set during beam pruning. Extend the deduplication key with states, buffs, cooldown/history and resource budgets; D3B's simple position/life/AP/MP signature (`FightTactics.cs:101-114`) is not sufficient as our full equivalence contract.

This first step needs actual state lifecycle data; merely changing the criterion getter or inserting a mask spell ID into the manual list will not solve the root cause.

## Proposed per-turn planner for GivreClone

This is a concrete design proposal to implement in our engine, not a claim that the study has changed runtime behavior. It replaces fixed tactical sequencing in automatic mode, while retaining explicit movement prohibitions, challenge rules, profile exclusions and manually configured HP/cast conditions.

### 1. Authoritative snapshot and rule boundary

At each server-confirmed decision point in `RunFightTurn`, construct an immutable snapshot:

- Map walk/LoS cells, sectors, occupancy, actor/team/turn order.
- Current HP/max HP, shield pools and expiration, current AP/MP, range, damage/resistance/heal characteristics, lock/dodge and movement/state immunities.
- Actually learned/current-context spell levels; generic cast caps and cooldowns; state criteria; per-effect target masks, zones, duration, triggers and supported action semantics.
- Already spent casts by actor and target, current challenge plan, profile conditions, `MovementPolicy`, `AllowApproach`, and refused actions for this turn.
- Known enemy spell levels/AP/MP and the provenance/confidence of any threat estimate.

Read existing state rather than predicting successful network actions. Preserve `TurnStartBudget`, script carry-over budgets, controlled actor context and confirmed modifier refresh. New fields such as shield/state lifecycle and enemy spell knowledge need actual protocol/data support; they are implementation prerequisites, not fields assumed already present.

### 2. Candidate generation and movement budgets

For each node, enumerate: end now; legal casts here (self, ally, enemy, free cell); legal walks to useful cast cells; recognized mobility/displacement casts; and the best legal end walks after every cast. Generate target cells by each effect's mask and zone, including empty AoE centers. Keep self/ally support targets and escape destinations as separate candidate groups so offensive near-enemy ordering cannot starve them.

Reachability labels must include `(cell, AP left, MP left)` after each departure's tackle loss and each step. Use the actual lock/dodge/state rules and measured rounding; multiple tacklers combine. Keep non-dominated labels when a cell has AP-vs-MP trade-offs. Our existing geometric BFS remains reusable for untackled paths, but unconditional `EnemyAdjacent => don't move` must not be the planner's rule. If lock/dodge data is unavailable, mark escape uncertain and avoid promising a follow-up cast that depends on an unproven AP budget.

For the user's exact kite behavior, if no useful attack is castable here:

1. Enumerate affordable attack spells and every reachable firing cell satisfying their effective range, LoS and target constraints.
2. For each spell/target, include its nearest legal firing cell; retain other cells only when they offer a meaningful safety/AoE/retreat advantage.
3. Simulate the move, verify AP after tackle still pays the spell, cast, then search end cells with the remaining MP on the **post-cast** board.
4. Compare the completed sequences, not the best damage/AP spell in isolation. An inexpensive or shorter-range spell can beat the nominal strongest one because it leaves an escape route.
5. Among tactically equivalent safe outcomes maximize minimum enemy separation, then prefer fewer spent MP/stable cell IDs. If a wall breaks enemy LoS, a nearer sheltered cell can be safer than the farthest visible one.

If no cast is reachable this turn, compare a measured approach for next turn with healing, shielding, summoning, buffing, retreating or passing. With zero MP, the planner can still cast a legal mobility spell; with insufficient AP after mobility, it must not pretend an attack follows.

### 3. Transition model and scoring

Maintain one pure effect transition implementation for the planner and simulator. Apply effects in their declared order. Update occupancy after displacement/summoning, shield before life loss, effective healing capped by missing HP, AP/MP buffs immediately where appropriate, state/buff replacement and cooldown/history after casts. Unknown effects must not create invented positive utility. Triggers/glyphs/traps require explicit modeling before being credited.

Use an incremental score relative to ending now, expressed in comparable life/survival units:

`score = enemy effective-life reduction + threat-weighted kill value + ally survival/effective-life gain + useful future buff/debuff value + next-turn useful attack value - predicted enemy reply loss - uncertainty cost`

- Give survival nonlinear value as HP falls. Include death removal of an enemy's future actions, not just a fixed kill bonus.
- Recompute enemy legal reach, LoS, damage and target selection from the resulting board, including AP/MP reduction and changed states. Start with a bounded one-reply estimate; unknown enemy spells must remain visibly unknown, not zero danger.
- Use range/LoS and actual AP feasibility to estimate next-turn value. Reduce buff value when the fight is likely to end before it pays back.
- Treat leftover AP/MP as enabling actions rather than unconditional positive score; otherwise passing or useless movement can dominate.
- Respect explicit style as a preference and hard movement restrictions as constraints. Increase survival preference smoothly with missing HP/predicted lethal damage; choose attack, kite or retreat from circumstances, not a permanent breed label.

This incorporates D3B's effective-life and enemy-reply ideas, but avoids copying its exact weights or claiming they are calibrated for ours.

### 4. Heals, shields, buffs/debuffs, push/pull and summons

**Heals:** retain `SelfHpMin/Max` and `TgtHpMin/Max` as user gates. For fully automatic support, generate self and all legal allied targets. Value `min(heal, missing HP)` through the survival curve, plus avoided death before the next allied opportunity to act; subtract the attack or escape forgone with those AP. A starting HP threshold can prioritize candidate generation (for example below 60%, a tuning proposal), but must not forbid a heal above it when predicted incoming damage is lethal or force overhealing below it when an enemy kill removes the threat. Ally movement competes with attack movement, unlike today's stationary ally branch.

**Shields:** project damage that can arrive during the shield's remaining duration and value useful absorption, capped by that exposure and stacking rules. Full HP can still justify a shield against incoming burst. Do not spend AP refreshing a shield that will expire unused, or ignore existing shields when computing urgency. Compare a kill, debuff, push or retreat that prevents the same damage.

**Buffs/debuffs:** simulate AP/range/stat/state changes before enumerating the next cast. Score the effect over its useful remaining duration, bounded by expected fight length and relevant turn order. Include target resistance and current immunity. A range/AP debuff may win by removing the enemy's next attack even without immediate HP damage.

**Push/pull/swap:** simulate destination, stopping cells, collision damage, occupancy, anchoring/carrying restrictions and target masks; then recalculate castability, enemy reach and escape costs. Do not cast a pull just because the enemy becomes closer: it may eliminate the retreat or expose an ally.

**Summons:** generate legal free cells with actual slot accounting, score occupied lanes/LoS/protection and bounded future contribution of recognized summon types. Avoid giving every created creature its full nominal HP as free team value, which rewards summon spam without usefulness. Retain our controlled-fighter support; autonomous/controlled invocation modes and class-specific evolutions need separate evidence. Neither D3B's first-generation summon model nor our first-free-cell placement solves all summon tactics.

### 5. Bounded search, execution, and observability

Suggested initial limits are engineering defaults to measure, not proven performance: beam width 8, depth 3 casts, explicit node/effect budget, a short wall-clock ceiling covering **all** evaluation including replies, and a small finalist set. Retain end-now and immediate safe/legal actions regardless of pruning. Keep at least one viable mask/state-setup branch per distinct relevant state set, with a bounded diversity allowance.

Deduplicate only truly equivalent states: positions, HP/shields, AP/MP, relevant characteristics/states, duration/stacked buffs, cast histories/cooldowns, summons/occupancy and challenge obligations. Deterministic IDs and stable ties make offline fixtures repeatable. No sleeps or timing luck in tests.

Execute only the best plan's first action using existing `Cast`/`TryMove` and sequence acknowledgments. Rebuild from the server result and search again; a pushed target, mask removal, unexpected tackle or resisted effect invalidates the remaining imagined sequence. Integrate into automatic mode before the old style-driven finishing phase; do not subsequently run an independent aggressive/fleeing move that undoes the selected retreat. Preserve challenge end-cell enforcement and script ownership. Log predicted sequence, top alternatives, AP/MP costs, estimated threat, uncertainty and observed outcome through existing combat diagnostics/journal.

D3B does this first-action/replan loop at `src/D3B.Bot/Fights/FightPlay.cs:183-239`, registering movement/cast expectations before sending at `:241-297`. Its search has important limitations not to inherit blindly: only a few pre-cast walks/target cells are retained (`TurnSearchLimits.cs:11-23`), free-cell candidates cluster around fighters (`FightTactics.cs:547-587`), and the main depth loop checks the time ceiling before a separate finalist reply phase (`:251-312`). These are heuristics, not exhaustive tactical guarantees.

## Validation slices for the later implementation

No tests were run for this research-only document. The following are proposed regression scenarios, to be placed beside existing combat/planner tests when runtime work is authorized:

- Actual learned list excludes an unlearned variant; disabled manual spell stays excluded; controlled actor uses its own grades/spells.
- Non-damaging utility obeys native cooldown, initial cooldown, turn/target caps, costs and state requirements without manual setup.
- No attack in range: walk to the nearest valid range+LoS cell, cast the highest-value feasible spell, retreat with remaining MP; wall/LoS and tackle AP loss change the chosen sequence.
- Escape impossible at current lock/dodge: attack/push/teleport rather than an impossible walk; high dodge or immunity permits an escape the current blanket adjacency rule would suppress.
- Same HP but different incoming damage chooses heal/shield/kill/retreat differently; ally rescue can beat self heal; overheal and expired/unneeded shield are not rewarded.
- Mask -> Furia wins over weak immediate attack; old mask state/buff removed; no illegal old-mask follow-up; beam pruning preserves the setup branch.
- Teleport AP leaves enough for the follow-up, or planner rejects that combo; pull/swap changes occupancy before the next cast; never-approach still applies.
- Summon slot/placement and ally masks prevent friendly-fire or impossible summon plans.
- Existing challenge/end-cell, manual rotation, script turn, controlled fighter, server refusal and epoch cancellation behaviors remain intact.

Replay captured actions and compare predicted vs observed AP/MP, position, HP/shield, states and cooldowns before broadening effect support. D3B's `CastBench.Settle` (`scripts/fights/CastBench.cs:287-306`) is the useful pattern. Its source tests and README measurements are evidence of its intent and reported results, not tests executed during this study.

## Broader bot improvements, ranked by value / effort

These are additions or refinements, not claims that GivreClone lacks harvesting, banking, routes, updating or replay altogether. Pénombre has visual features only; D3B's README also lists marketplace/crafting and multi-account groups as future work (`README.md:55-57`), so those are not shipped features to port from it.

| Rank | Improvement and value | Theirs evidence | Ours evidence / gap | Effort and risk |
| --- | --- | --- | --- | --- |
| 1 | Offline differential combat replay. Makes every further class/effect addition measurable and catches protocol drift. | D3B `scripts/fights/CastBench.cs:287-306,383-487,526-577` compares the imagined action to the server. | We already decode raw packets (`GivreClone.App/Services/CaptureDecoder.cs:16-32`) and reconstruct readable combat journals (`Combat/CombatJournal.cs:25-27,48-75`); those are not an effect-prediction oracle. | M/L, medium; build on existing capture tooling rather than another log viewer. |
| 2 | Group eligibility informed by the character's actual damage/survivability, not level alone. Reduces hopeless engagements and explains refusals. | D3B `src/D3B.Bot/Fighting/GroupJudge.cs:63-71,82-127` estimates turns to win/lose, with an explicitly permissive calibrated margin (`:27-29`). | `GivreClone.App/Services/Progression/ZoneAdvisor.cs:14-28` ranks zone difficulty from monster levels and explicitly does not measure resistance/damage; this is the nearest reviewed advisory surface, not proof that every attack filter is absent. | M after threat model, high calibration risk. Do not copy its 0.25 margin as a universal survival threshold. |
| 3 | Shared resource-regrowth learning and route forecasts. Distinguish a continuously witnessed regrowth from an upper bound observed on revisit. | D3B `src/D3B.Bot/Harvesting/RegrowthLearner.cs:12-22,43-84`, `Regrowths.cs:5-16,92-133`: cross-connection observations and retained skill timing. | Current reviewed gather loop chooses available nearby resources and resets per-map blocks (`GivreLuaHost.Gather.cs:92-110`); forcegather camps for respawns (`:163` onward). No learned skill-regrowth model was found in the inspected services. | M/L, medium; persist estimates/provenance, never promise exact respawn timestamps. |
| 4 | Threat-aware automatic placement. Reuse the completed combat evaluator before ready. | D3B `src/D3B.Engine/Tactics/FightTactics.cs:201-224`. | `CombatPlacementSelector.cs:10-26` ranks distance, with explicit keep/near/far semantics already available. | S/M once planner exists, medium; preserve manual/Keep placement. |
| 5 | Optional account-specific visual comfort overlay, bright UI cutouts and transient peek. Low combat value, possible user comfort value. | Pénombre `src/Penombre.cs:22-31`, account settings `:1331-1346`, sun-cycle scheduling `:974-1042`; `README.en.md:58-71`. | `GivreClone.App/Services/CombatProfile.cs:174-180` already offers visual combat mods/tactical mode, but not the independent scheduled color filter described here. | M/L, low tactical value and licensing sensitivity. Reimplement only if requested; no GPL source transplant. |

### What not to port

- A large per-class rotation table: D3B's demonstrated Zobal improvement comes from effect/state correctness and search diversity, not a fixed class script.
- D3B's reduced effect model as if complete: conditional/triggered effects are approximated (`Imagination.cs:18-25,304-312`), and glyphs, traps, telefrags, portals and other mechanisms remain explicit roadmap work (`docs/roadmap.md:242`).
- Another range/LoS implementation without an observed mismatch. Ours already has these capabilities; diagonal behavior is a concrete discrepancy to test, not grounds to wholesale replace geometry.
- Pénombre combat logic: there is none at the supplied URL.

## Research completion

Only this document was created in GivreClone. No source files were edited, no builds/tests/game actions were executed, and no commits were made. Source revisions and exact local/external evidence are recorded above. Temporary clones under `%USERPROFILE%/AppData/Local/Temp/ulw-givre-100-20261101/study` are removed as the final research cleanup.
