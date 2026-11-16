# State setup and conditional follow-up - 2026-11-03

CombatPlannerResolverTests now exercises an action-950 setup applying state
42 followed by a damage spell masked A,E42. Six AP permit both casts.
With setup available, the planner must choose IDs 10 then 20 for 30 damage;
without setup it must choose no action and deal zero damage.

Both cases run with confirmed empty initial states and with unknown initial
states (null). The resolver must not assume an unknown state is present;
the applied setup establishes the positive state needed by the follow-up.
The input snapshot must remain unchanged. No mock supplies the state change.

Release --no-restore filter CombatPlannerResolverTests completed with exit 0:
14 passed, zero failed or ignored, duration 28.0 seconds. These are synthetic
GivreClone planner/resolver fixtures, not JondoEmu parity or live class
certification; they require neither world.db nor external bomb data.

Integration gap: the examined live planner snapshot currently supplies no
States values. Spell-level StatesCriterion also remains excluded by
IsAdmitted. This result therefore does not establish live state-dependent
combos, duration/expiry, turn hooks, Telefrags or class-specific passives.

Additional class-identity gap: SpellFighter accepts Breed and mask evaluation
uses it for B/b filters, but PlannerFighter currently has no Breed field and
the planner's SpellFighter conversion therefore leaves Breed=0. The live
snapshot cannot preserve class identity through that route. A positive breed
filter will not match a real player class; a negative filter can misleadingly
match the default. No class-ID mapping is inferred from UI index here.
LaunchService.LevelUp.cs obtains the authenticated character's BreedIndex
and explicitly maps it through ClassIconProvider.BreedId for BreedData;
this is a potential source for the caster only, not proof of other fighters'
classes. The missing observation/propagation must be addressed before
claiming breed-dependent tactics work across the team.

## Class identity propagation follow-up

The gap above is now corrected for observed identity: PlannerFighter carries
nullable Breed; conversion passes its value and an explicit BreedKnown flag
to SpellFighter. Both positive B and negative b filters reject unobserved
class identity rather than comparing a fabricated zero. Existing direct
resolver callers retain their explicit identity contract.

The live adapter supplies ClassIconProvider.BreedId(BreedIndex) only when
the fighter actor ID equals the authenticated CharacterId. This avoids
assigning the master's class to a controlled companion. Other fighters'
classes remain unknown; no monster or ally class is guessed.

Six table cases cover matching, nonmatching and unknown classes for B14 and
b14. Release --no-restore filter CombatPlannerResolverTests, SpellResolverTests,
CombatTurnPlannerTests and ControlledCombatTests completed with exit 0:
120 passed, zero failed or ignored, duration 16.6 seconds. This does not
establish complete breed-dependent tactics or a live class-specific cast.

## Live state observation follow-up

The current fields.json identifies FightBoostUpdateEvent and
FightBoostUpdateStateEvent, but all their payload fields remain obfuscated.
FightRemovableEffect names target_id and dissipation_state, not the state
identifier or duration. No handler for those messages was found in the
examined LaunchService sources. A state tracker cannot safely infer an empty
set, expiry or removals from that evidence; the live States gap remains.

FightCharacterInformation.breed_id is separately named in the catalogue,
so observing other characters' class is a better-grounded next extension
than guessing the boost-state payload. Its nesting and lifecycle still need
to be traced before using it in planner snapshots.

## Named fighter class observation

FightStatsReader.ReadShowFighterBreed now follows the named catalogue path:
FightFighterShowEvent.information -> ActorPositionInformation.actor_information
-> ActorInformation.fighter -> FightFighterInformation.named_fighter ->
NamedFighterInformation.character_information -> FightCharacterInformation.breed_id.
It returns the actor ID and raw server breed, or null when either is absent.
Synthetic packet cases include breeds 4, 13, 14 and 20, not UI indices.

The in-fight packet handler retains this observation per PID/actor. Death
removes the actor entry and PurgeFightState drops the session collection.
Planner snapshots prefer observed identity, then the authenticated player's
known identity, otherwise null. Controlled actors do not inherit the master.

The first combined run passed 63/64 cases; only the manifest equality test
failed because FightCharacterInformation was inserted after FightEndEvent
rather than before FightCharacteristics in ordinal order. The manifest order
was corrected; no test was weakened. The repeat Release --no-restore run
(CombatForcedMovesReplayTests, MappingContractTests, CombatPlannerResolverTests,
ControlledCombatTests) completed with exit 0: 64 passed, zero failed or
ignored, duration 20.7 seconds. Full packet handler lifecycle and real-server
class observation remain unvalidated live.

The live remaining-plan reuse check now compares Breed as well as position,
life, visibility and characteristics. A newly observed or changed class must
therefore force replanning. The subsequent Release planner/control regression
run passed 33 tests, zero failed or ignored, exit 0, duration 14.0 seconds.
This compilation and suite do not independently drive the private reuse
branch through a live movement/observation race.

Mutation proof for class propagation: temporarily replacing the planner's
`Breed: f.Breed ?? 0` with `Breed: 0` made two of the six breed-filter cases
fail (expected 0/actual 30 and expected 30/actual 0); exit 1, 5.6 seconds.
The original expression was restored. The same six cases then passed with
exit 0, zero failed or ignored, duration 4.7 seconds. No mutation remains.

## Shared transition map and integration limits

Current implementation boundaries, inspected in SpellResolver.cs and
CombatTurnPlanner.cs:

| Transition | Immediate simulation | Live/planner boundary |
| --- | --- | --- |
| Damage, shields, healing, life steal | Ordered events update life and shield; deterministic mean rolls | Damage-based scores include friendly fire; critical hits and stochastic outcomes are not simulated |
| AP/MP gain or removal | Changes the target resource budget; removal ignores dodge | Expected self gains are reconciled with server observations, not treated as confirmed resources |
| Apply/remove state (950/951/952) | Changes the simulated state set before subsequent effects | Unknown initial states stay unknown; no live boost-state reducer was found in LaunchService sources |
| Stat changes | Supported characteristic deltas change subsequent calculations | Duration, dispel and expiry are not modeled |
| Push, pull, teleport, swap, symmetry | Moves fighters with passability and occupancy checks | Does not establish Telefrag creation, class passives or triggered trap effects |
| Nested spell | Resolves a child descriptor using lookup and recursion guards | Missing children and unsupported conditions are reported, not invented |
| Delayed/triggered effect | Retained in DeferredSpellEffect | No turn scheduler or trigger execution; retained data is not simulated future value |
| Ground mark | Retains owner, cells, source spell/grade and duration | No activation, removal identity, expiry or trap-chain simulation; live packets remain logging-only |
| Summon | Adds occupancy and template identity | Placeholder 1/1 life is not monster-grade combat data or bomb-combo simulation |
| Class filter | Uses observed breed with explicit unknown identity | Does not infer class mechanics from a matching breed |

Planner admission is not a completeness certificate: it requires an immediate
modeled row, rejects spell-level StatesCriterion, and rejects unsupported damage
rows, but can still admit spells with unmodeled non-damage side effects. Such
effects remain uncertainty, not proof that a class-specific combo works.
Live state tracking, duration/expiry, turn hooks, Sram trap activation and
invisibility simulation, Roublard bomb age/walls/chains, Xelor Telefrags/history
and Eliotrope portal routing therefore remain separate unfinished work.

## Full-suite integration check

Release --no-restore full GivreClone.Tests run completed with exit 1:
4,243 total, 4,240 passed, one failed, two ignored, duration 135.8 seconds.
The sole failure was LuaApiInventoryTests.Inventory_covers_the_current_binding_sources:
the stored SHA-256 for GivreClone.App/Services/GivreLuaHost.Fm.cs is stale.
The test compares source bytes with docs/portal/lua-f07-inventory.json; it
did not report a combat assertion failure. The FM source and its inventory
were not changed by this combat increment. Their earlier provenance was not
reconstructed, so this is recorded as an unrelated workspace failure, not a
proven pre-existing baseline. Neither the test nor the inventory was modified
to make the run green. The two ignored cases are the existing disabled
GameCodec generated-move and captured-whoami parity tests. Their skip
messages identify frozen 3.6.10.11 generated protobuf classes with obsolete
envelope/movement fields and no production callers; they are not combat
tests. No skipped test was added or re-enabled in this increment.

## Partial state knowledge after explicit removal

SpellFighter now retains AbsentStates separately from StatesKnown. An
immediate 951/952 removal establishes absence of that state only; 950
application removes its absence fact. Both collections are copied into
immutable snapshots and persist across resolver calls. E/e masks may use
that specific fact while unrelated state IDs remain unknown.

The previous incomplete-state test expected the final e42 attack to remain
unknown even after an explicit removal; its assertion has been updated for
the new targeted-knowledge contract, not to declare the entire set known.
A separate two-cast regression checks that e42 succeeds after removal while
e43 remains unmodeled. The first run passed 80/81; the new test's input
immutability assertion incorrectly treated its null States fixture as an
empty collection. That assertion was corrected to Assert.Null. The repeat
passed 81/81, zero failed or ignored, exit 0, duration 18.1 seconds.
This is simulation knowledge only, not server state observation or expiry.
The planner-level pair then reproduced a distinct failure: with removal
available it returned [] instead of [10,20], while the no-removal case passed.
Exit 1, one of two failed, duration 11.3 seconds. CastOn used only emitted
events to discover affected targets and discarded the knowledge-only setup.
It now includes newly established AbsentStates in affected targets without
manufacturing a StateResolved event or awarding control value. The broader
resolver/rotation/planner regression run passed after that correction:
147 passed, zero failed or ignored, exit 0, duration 20.5 seconds.

The preceding AutoSpellRotationTests run passed 37 cases, zero failed or
ignored, exit 0, duration 6.5 seconds. It includes learned 1079/1080 resource
removal reaching the planner and receiving control value without damage.

Follow-up controls: the absence-combo table explicitly asserts zero control
reward and includes removal without an attack, which must choose no actions.
All three cases passed, exit 0, duration 3.4 seconds. Reapplication of state
42 removes its absence fact while preserving the earlier snapshot; the
resolver/planner suite including that regression passed 48 cases, zero
failed or ignored, exit 0, duration 27.7 seconds.

## Three-cast placement combo

CombatTurnPlannerTests.Swap_then_pull_opens_a_melee_attack_without_walking
uses nine AP, zero MP, an ally at step 2 and an enemy at step 5. It requires
swap (20) -> pull (30) -> melee attack (10), aimed at steps 2, 5 and 3.
It passed with exit 0, one test, duration 3.0 seconds; input fighter positions
remain unchanged. This establishes the synthetic three-cast search only.

Critical live boundary: the fixture sets ForcedMovementSafe=true explicitly.
The examined LaunchService.Combat.Turn.cs snapshot does not supply that
argument, so it remains false in live automatic combat. The snapshot contract
requires absence of movement-blocking states, carrying, portals, marks and
movement triggers before forced movement is safe. Missing observations do
not establish those absences. The swap/pull combo is therefore NOT enabled
live by this test and the broader placement task remains open. Live casts
also discard the simulated continuation and replan from server observations;
simulated absent-state facts are not persisted as server facts.

The next prerequisite investigation is version-matched state packet evidence,
not flipping ForcedMovementSafe. Current fields.json still has anonymous
FightBoostUpdateStateEvent fields 1/3 and FightBoostUpdateEvent fields 1/2/3;
FightRemovableEffect supplies only target_id and dissipation_state meanings.
A read-only artifact investigation is active to seek a serializer/handler
trace for one safe state observation decoder, without launching the client
or substituting historical field numbers.

That investigation returned no grounded schema from the inspected mapping,
recovery notes, tactical metadata notes or installed .backup/3.7.12.12 files.
It did not inspect inaccessible artifact directories; no global absence claim
follows. No reader was added from obfuscated names alone.

## State setup survives beam pruning

Adding eight legal one-damage alternatives reproduced another failure: the
beam retained [100,101] for two damage rather than [10,20] for 30. Three of
four table cases passed; exit 1, duration 3.8 seconds. Rank previously looked
ahead only after teleport/forced movement. It now also looks ahead after
950/951/952 state setup, using the existing legal, affordable CastOn path and
shared work budget. It does not add speculative value to the final score.
The four-suite regression run passed after this correction: 151 passed,
zero failed or ignored, exit 0, duration 28.6 seconds. A separate generated
rotation table now checks an analogous elemental boost setup with eight
weak alternatives, requiring Strength setup for Earth but not Fire. Its
result reproduced the same pruning bug: Earth returned [71] rather than
[70,71], one of four cases failed, exit 1, duration 2.8 seconds. Rank now
includes Buff effects in the same budgeted follow-up path. The four suites
then passed 153 cases, zero failed or ignored, exit 0, duration 22.9 seconds.
Fire still chooses only its direct attack; a nominal buff is not given a
fixed preference or speculative final-score reward.

## Turn cast cap

A table exercising MaxCasts 0, 1 and 2 found that the initial in-range
candidate pass bypassed the zero limit: one cast instead of zero. The other
two cases passed (three total, one failed, exit 1, duration 18.2 seconds).
That pass now requires MaxCasts > 0. Ranking also stops anticipating a
follow-up at the configured or fixed maximum cast depth; otherwise terminal
branches could consume work and be ranked using a fourth, unreachable cast.
The combined regression run passed: 156 passed, zero failed or ignored,
exit 0, duration 27.2 seconds. No live safety gate was relaxed.

## External oracle status

A peer investigation reported Jondo main commit
683ab49c73f65b7c4ab7c1134c448f7fa850f6bc, a net10.0-windows combat test
project with Windows Forms dependencies, and database-dependent cases that
can return without exercising assertions when world.db is absent. These
claims have not been independently verified in this worktree. No Jondo run
or parity result is claimed. Any future external-oracle run must establish
which cases actually executed, database availability and version alignment;
a green filtered run alone is insufficient. The peer reports support for
client 3.6.10.11/.10, not the currently inspected 3.7.12.12 client.
