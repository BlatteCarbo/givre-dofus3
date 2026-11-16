# Learned utility targeting audit - 2026-11-03

Read-only trace of the current automatic planner path; no production change
was warranted by the generated TargetKind default alone.

AutoSpellRotation.Build assigns Ally only to pure healing/shielding support
and otherwise defaults TargetKind to Enemy. This initially suggests that a
learned ally AP boost could be discarded. However, the execution adapter in
LaunchService.Combat.Turn.cs (plannerSpells construction, around lines
1012-1022) applies that configured-side filter only when the spell appears
in profile.Spells. Automatically appended spells therefore retain observed
allies in AllowedTargets, subject to TargetSpellGateOk. The resolver applies
the actual per-effect target mask; changing TargetKind here is not a proven
fix for generated ally boosts.

CombatTurnPlanner.CastOn updates the simulated board through SpellResolver
and credits AllyApGiven and AllyMpGiven in its control score (weights 6 and
3). This is an immediate resource heuristic, not a simulation of the ally's
future turn. Returned TeamSupportIntent contains only healing and shielding,
so AP/MP utility grants are not published as support reservations there.
Self elemental boosts affect subsequent casts through the simulated board;
equivalent optimal cross-character boost coordination is not established.

Scope limitations: source trace only, no new live cast or ally-boost replay.
Conditional masks still require their actual facts. A complete class profile
cannot be inferred from this generic utility admission. The separate class
mechanics audit is investigating current Sram/Roublard lifecycle gaps.

## Support-only planner admission

The outer execution gate previously required modeled enemy damage even when
the rotation contained only healing or shielding. AutoSpellRotation now owns
the admission policy used by LaunchService.Combat.Turn: modeled enemy damage
retains its existing precedence; otherwise an admitted immediate heal/shield
can enable planning only if no offensive effect or unresolved descriptor needs
the classic fallback. This preserves the earlier live Eniripsa attack fallback.

AutoSpellRotationTests covers pure heal, pure shield and support alongside an
unmodeled attack. Existing CombatTurnPlannerTests separately exercise survival
casts and ally support. The filtered test run under bash_734 exited 0: all 110
tests passed, zero failed or skipped, test duration 43.4 seconds. App and Tests
compiled successfully with 226 and 31 warnings respectively (257 total);
the complete build/test command took 193.1 seconds. No live companion cast has been
validated for this change. The C# language server also crashes during workspace
enumeration on access to artifacts/kourial-tools/pypdf; compiler output is the
available diagnostic evidence, not a successful language-server check.

The subsequent no-build/no-restore run of ControlledCombatTests,
CombatPlannerResolverTests and SpellResolverTests under bash_735 exited 0:
63 passed, zero failed or skipped, test duration 23.2 seconds (24.2 seconds
for the command). Together these two runs cover 173 passing cases; neither
is a live companion combat or complete class-mechanics certification.

The admission table was then extended to cover a modeled attack alongside
support, an unresolved descriptor alongside support, and modeled damage after
an unresolved descriptor (preserving the existing offensive precedence).
The AutoSpellRotationTests run under bash_736 exited 0: 45 passed, zero failed
or skipped, test duration 5.7 seconds; build/test took 22.7 seconds with 31
warnings. This is an overlapping rerun, not 45 additional distinct cases on
top of the earlier 173.

## Ally resource-only admission regression

Extending the existing admission table to ally-capable AP/MP gains reproduced
two failures under bash_750: nine cases, seven passed and two failed, exit 1
(Expected true, Actual false for the two resource-only rotations). The
unsupported-attack fallback case still passed. The policy now admits immediate
modeled actions 111/120/128 when their target mask includes allies, alongside
heals/shields. Self-only resource gains do not independently enable planning;
existing modeled attacks retain precedence. The planner still chooses whether
and whom to cast on, and server confirmation remains required during execution.

The four-suite regression run under bash_751 exited 0: 154 passed, zero
failed or skipped, test duration 15.6 seconds, total build/test 50.3 seconds
with 257 warnings. The admission table does not itself prove an ally-targeted
boost action or live companion support. Three additional planner scenarios
(111, 120 and 128 on ally-only mask g) now require one cast on the ally and
the exact resource control score. bash_752 exited 0: three passed, zero failed
or skipped, test duration 3.0 seconds (9.5 seconds build/test), with 31 warnings.
These scenarios exercise real planner/resolver decisions rather than only
the admission boolean. They do not simulate the ally's later turn or prove a
live server-confirmed companion boost.

## Resource-removal admission regression

The existing Learned_point_removal_reaches_the_planner_as_enemy_control
scenarios previously called Choose directly after generating the rotation.
They now also exercise ShouldUsePlanner, the policy used by the live adapter.
Both failed under bash_753 (Expected true, Actual false; two failures, exit 1,
4.0 seconds), exposing a gap between supported simulated control and admission.

ShouldUsePlanner now also admits immediate modeled enemy-targeted resource
removal actions 1079/1080/168/169. It still rejects a utility-only takeover
when an unresolved descriptor or unmodeled offensive spell needs the classic
rotation. No resolver scoring, target eligibility or server-confirmation rule
was changed. The combined AutoSpellRotationTests, CombatTurnPlannerTests,
CombatPlannerResolverTests and ControlledCombatTests run under bash_754 exited
0: 157 passed, zero failed or skipped, duration 30.3 seconds (57.9 seconds
build/test), with 257 warnings. The same removal table was then extended to
actions 168/169; bash_755 exited 0 with four passed, zero failed or skipped,
duration 2.9 seconds (9.5 seconds build/test), with 31 warnings. Live enemy
resource removal remains unverified.

## Do not reward unused self resources

Two deterministic cases cap the turn at one cast and disable movement, offering
an immediately reachable 10-damage attack versus a self AP or MP gain. Both
failed under bash_756: the planner chose boost spell 20 rather than attack 10,
two failures, exit 1, duration 3.0 seconds. The resource control reward counted
the caster as an ally, even though no follow-up could spend the gained points.

CombatTurnPlanner now awards the resource support heuristic only to positive
PointsResolved events on other allies. Self gains still update the simulated
AP/MP and participate in the existing budgeted follow-up search; they must
earn value through an actual attack or movement outcome. Resolver resource
totals remain unchanged. Five-suite regression validation under bash_757 exited
0: 186 passed, zero failed or skipped, test duration 19.5 seconds (54.9 seconds
build/test), with 257 warnings. This includes learned AP/MP combos, ally-only
boosts and controlled actors. No live combat result is claimed for this
scoring correction.

The existing learned-MP combo table was extended with eight competing ranged
attacks. All four enabled/disabled cases passed with one-damage alternatives
(bash_758, exit 0, 3.2 seconds), then again with five-damage alternatives versus
the ten-damage combo (bash_759, exit 0, 2.9 seconds). Enabled still chooses boost,
two walking steps and attack; disabled chooses the first available ranged
alternative or no action when none exists. This did not reproduce a pruning
bug, so no further search expansion or work-budget increase was introduced.

## Observed per-turn AP budgets

The planner previously initialized every non-caster SpellFighter with 12 AP
and divided simulated remaining AP by 12 for its reply estimate. The new
five-case table reproduced three failures under bash_760 (exit 1): a six-AP
target retained too much estimated threat after removal, and two/zero-AP
targets earned excessive control credit. The unknown-budget and unchanged
initial-threat controls already passed.

PlannerFighter now carries optional TurnAp. The live adapter supplies MaxPa
only when FighterStats includes the AP characteristic; an absent observation
stays null and retains the prior 12-AP estimate. Current Pa is deliberately not
used, because previously spent points are not the next turn's resource budget.
The resolver starts with the observed budget and saturates removal against it;
reply damage is normalized by that same initial budget rather than 12. A known
zero budget has zero modeled AP-dependent reply. Plan reuse also compares
TurnAp. There is no search-budget increase.

Five-suite validation under bash_761 exited 0: 193 passed, zero failed or
skipped, 26.7 seconds test duration (64.6 seconds build/test), 259 warnings.
Inspection of the compiler warnings found a new CS8602 in EnemyReply: the
conditional zero-AP branch prevented nullable-flow tracking from carrying
across the subsequent MP expression. Returning early for a known zero budget
keeps identical behavior without that warning or a new suppression. Final
five-suite validation after this adjustment under bash_765 exited 0: 193
passed, zero failed or skipped, 36.3 seconds test duration (69.7 seconds
build/test), 257 warnings (App 226, Tests 31). No diagnostic for the three
modified combat files appears in artifacts/combat-ap-final-warnings.log. The diagnostic rebuild
also reported existing warnings in other files, including xUnit2031 in
CombatMovementDecisionTests.cs:118; that file is not modified by this increment.
No live observation-to-action result is claimed. This remains an approximation of enemy response: AP-free
attacks, AP regeneration, removal resistance/dodge and timed resource changes
are not proved by these fixtures.

## Controlled companion coordination

The production team-intent adapter was account-scoped, not companion-scoped.
LaunchService.Combat.Turn explicitly set sharedFight to null for a controlled
actor, while TeamIntentFight counts connected account actors and requires at
least two. PublishTeamIntent also requires the current SelfActorId. A single
account plus its server-controlled companion therefore does not share heal,
shield, target or end-cell intentions, even though the companion uses its own
spellbook and can choose modeled support actions. Enabling publication alone
would not fix the count, lifetime or authority contract.

The real LaunchService fixture reproduced empty master-to-companion views in
both current-turn and late-turn cases (bash_766, two failed, exit 1). The adapter
now counts distinct account identities plus companions previously granted by
the server in this fight, restricted to the current allied roster and matching
master. History permits reading the companion's intent after control returns
to its master, but does not authorize publication: a companion publication must
match the active engine context, the current server-controlled context and
the server turn actor. Automatic turns now enable the same publication path
for the controlled actor. Existing fight teardown clears the shared board and
controlled history; no new persistent cache is introduced.

Four-suite validation under bash_767 exited 0: 127 passed, zero failed or
skipped, 20.9 seconds test duration (46.5 seconds build/test), 257 warnings.
The publication table now also covers explicit control revocation while the
old async engine context and server turn actor remain; its three-case run
under bash_768 exited 0: three passed, zero failed or skipped, 3.3 seconds
test duration (12.5 seconds build/test), 31 test-project warnings. These tests do not establish
live companion support or forced placement. Resource-boost reservations are
still absent from TeamSupportIntent, and the existing heal/shield intent model
is retained rather than claiming full future-turn simulation.

## Do not count confirmed healing twice

MostExposedAlly previously added every promised heal to observed life, even
after that heal had arrived. A three-case table starts at 20 HP, promises 40
healing, then observes 20, 40 or 60 HP against another ally at 65 HP. bash_769
reproduced two wrong target selections (two failed, one passed, exit 1).

TeamSupportIntent now includes optional LifeBefore. The priority calculation
uses the greater of observed life and baseline plus promised known-baseline
healing, caps effective life at MaxLife, and retains additive treatment for
legacy intentions without a baseline. The planner and acknowledged-outcome
adapter supply their pre-action life; classic casts supply turn-start life.
This changes priority hints, not observed fighter HP or cast authorization.
Shield intentions still have no observed-shield reconciliation, and intervening
damage or multiple differently timed promises remain an approximation rather
than an event ledger. Four-suite validation under bash_770 exited 0: 131
passed, zero failed or skipped, 26.3 seconds test duration (54.0 seconds
build/test), 257 warnings (App 226, Tests 31).

## Reconcile shield observations in support priority

The four-case shield table promises 40 shield at baseline zero, then observes
0, 20, 40 or 60 shield. bash_771 reproduced the missing-observation bug:
three passed and one failed, exit 1, because an ally protected beyond the
promise was still treated as the weakest. The intermediate cases also reject
blindly adding observed shield to the entire promise.

TeamAllyExposure now carries observed shield and TeamSupportIntent carries an
optional ShieldBefore. Priority takes the greater of observed shield and
baseline plus known-baseline promises; legacy baseline-free promises remain
additive. Planner publication and acknowledged outcomes supply their snapshot
baseline; classic publication supplies the turn-start shield only if the
shield characteristic was observed. No new state cache is added. Four-suite
validation under bash_772 exited 0: 135 passed, zero failed or skipped,
17.7 seconds test duration (44.7 seconds build/test), 257 warnings (App 226,
Tests 31). Shield consumption between publications is
still a timing approximation, not a fully reconciled combat event ledger.

The planner-level scenario Team_healer_prioritizes_the_unprotected_ally_using_observed_shield
also passed under bash_773 (exit 0, one passed, 2.8 seconds test duration,
8.9 seconds build/test, 31 test-project warnings). Two equally injured allies
are eligible; the planner chooses the one without the observed 80-point
shield and publishes a 20-point heal with LifeBefore=20 and ShieldBefore=0.
This establishes the observation-to-planner-choice wiring in simulation, not
live packet decoding, server execution or companion placement.

## Ignore stale placement authors

TeamBlocking previously reserved end cells and firing lines for every stored
intent, including authors absent from the current board or at zero simulated
life. bash_774 reproduced two unnecessary moves for absent/dead allies; the
living-author control passed (two failed, one passed, exit 1).

The planner now applies both placement penalties only when the author is a
different allied fighter still alive in the simulated node. The intent board
retains its existing round expiry; no new cache or event is introduced, and
movement policies and server gates are unchanged. Four-suite validation under
bash_775 exited 0: 139 passed, zero failed or skipped, summary test duration
20.4 seconds (62.1 seconds build/test), 257 warnings (App 226, Tests 31).
This covers ordinary walking coordination, not forced-movement
authorization or portal/mark interactions.

## Marginal healing after an allied reservation

The planner previously used shared healing only to choose a priority ally,
but still rewarded the full heal on an ally whose missing life was already
covered by a teammate. In the two-case planner scenario
Team_healer_spends_its_cast_on_missing_healing_not_another_allies_reserved_heal,
ally 3 has 4/100 life and ally 4 has 40/100. Without a reservation, healing
ally 3 is correct. With 96 healing promised at baseline 4, the next healer
should choose ally 4. bash_797 reproduced the failure: expected actor 4,
actual actor 3 (exit 1). The initial fixture placed ally 4 at the exclusive
60-percent support threshold; it is now at 40 percent so the alternative
heal is eligible independently of the reservation correction.

TeamIntentView exposes its existing baseline-reconciled effective life;
CombatTurnPlanner caps the reward for a local heal to the missing life left
after that reservation and healing already explored in the branch. The
resolver board and incoming-damage survival scoring still use actual life,
not promised life. This is resource coordination, not a claim that an ally
has already received a planned heal. bash_798 was unable to build because
the owned Debug application PID 10680 held GivreClone.exe; bash_799 closed
that exact instance gracefully. The four-suite retry bash_800 exited 0.
The corrected alternative at 40 percent was then mutation-checked:
bash_801 removed only the marginal-healing cap, passed the no-reservation
control, and failed the reservation case (expected 4, actual 3; one passed,
one failed, 18.3 seconds summary duration, exit 1). The cap is restored;
four-suite validation bash_802 exited 0: 141 executed, 141 passed, zero
failed or not executed. The saved GivreClone.Tests/TestResults/team-healing-final.trx
records start 16:46:25 and finish 16:46:41. The language server still exits
while scanning the unrelated access-denied artifacts/kourial-tools/pypdf
directory; compilation and test execution supplied diagnostics instead.
Shield marginal value and AP/MP boost
reservations are not added by this correction, and no live cast is proven.

## Marginal shielding after an allied reservation

The planner-level two-case table Team_shield_reward_excludes_promised_protection_without_assuming_it_for_survival
places an ally at 40/100 life under an 80-damage reply, with a 40-point
shield available. With no promise its shielding reward is 20. With an
80-point promise the reward must be zero, but the local shield still reduces
the actual 80-damage reply to 40 and retains the death penalty (Incoming=140)
because promised protection is not confirmed. bash_803 reproduced the
duplicate reward: expected 0, actual 20, exit 1.

TeamIntentView now exposes its baseline-reconciled EffectiveShield. The
planner discounts only the shielding reward using promised protection plus
the local branch's shield change. It does not put promises on the resolver
board or subtract them from survival risk. Four-suite validation bash_804
exited 0. GivreClone.Tests/TestResults/team-shield-final.trx records 143
executed and passed, zero failed or not executed, start 16:56:12 and finish
16:56:30. This validates the marginal shielding correction in simulation;
AP/MP boost reservations and live companion evidence remain open.

## Element selection retains modeled friendly damage costs

AutoSpellRotation previously checked every damage row against AutoElements,
including a row explicitly restricted to the caster. A Fire attack with a
Neutral self-damage cost was therefore excluded before the planner could
evaluate its benefit and risk. The two-case regression under bash_817 failed
at rotation admission for both healthy (100 life) and unsafe (4 life)
casters: the collection was empty, two failed, exit 1.

The element predicate now excludes rows whose masks contain only the
existing friendly categories C/c/a/g from the element compatibility check.
Those rows are retained in the descriptor, not dropped from resolution.
Unknown or mixed hostile masks remain subject to the element check. The
scenario uses 40 Fire enemy damage and 5 Neutral caster damage; it requires
the healthy caster to retain the modeled friendly-fire penalty and the
4-life caster to refuse the lethal action. Five-suite validation bash_818
exited 0: 197 executed and passed, zero failed or not executed. The report
GivreClone.Tests/TestResults/element-self-cost-final.trx records start
17:07:35 and finish 17:07:59. This generic admission
correction is not full Sacrier mechanics or live class validation.

## Initial attack order does not reward self-damage

The existing elemental-stat ordering table now also gives the weaker attack
a 100-point Neutral caster-only cost. bash_819 reproduced the ranking error
in both stat configurations: the cost was added as positive attack damage,
so the weaker spell moved first (expected 20/actual 21 and the reverse).
The no-cost controls remain part of the same table.

AutoSpellRotation's stat-based ranking now excludes friendly-only damage
rows using the same conservative mask categories as its element selection.
The descriptor remains intact for the planner's friendly-fire and lethal
self-damage checks. This is the initial generated rotation order, not an
additional tactical reward or a new class policy. Five-suite validation
bash_820 exited 0: 199 executed and passed, zero failed or not executed.
GivreClone.Tests/TestResults/element-ranking-final.trx records start
17:12:08 and finish 17:12:25.

## Damage action classification and dynamic elements

Action 2832 was modeled by SpellResolver but classified Unknown at data
import. A mixed attack containing that action could bypass AutoElements.
bash_821 reproduced admission under a Fire-only filter where the second
damage effect should exclude it (expected empty, actual spell 20, exit 1).

SpellEffectDescriptor now classifies the already recognized damage actions
89, 279, 1223 and 2832 as damage. Action 1223 remains explicitly unsupported
by RowProblem; its classification does not authorize execution. Actions
2822/2828 choose the strongest elemental stat and 2832/2890 the weakest in
the existing resolver. DamageEstimator now owns that selection for both
the resolver and generated rotation filtering/ranking, preserving the
resolver's stable Earth/Fire/Water/Air tie order. Fixed-action resolution
is otherwise unchanged.

The mixed-action fixture now supplies characteristics that make Water the
unique weakest stat instead of implying raw effectElement overrides a
dynamic action. Validation bash_822 exited 0 across rotation, planner,
resolver and estimator tests: 198 executed and passed, zero failed or not
executed. GivreClone.Tests/TestResults/element-action-final.trx records
start 17:18:40 and finish 17:18:58. This
does not establish full class mechanics or live execution.

The filter table was strengthened with all four dynamic actions and a raw
Air element deliberately different from the computed Fire (best stat) or
Water (lowest stat). Strength=100, Intelligence=200, Chance=0, Agility=200
also preserves the existing stable tie order. bash_828 exited 0; the saved
dynamic-element-contract.trx records nine passed, zero failed/not executed,
start 17:38:30 and finish 17:38:43. This proves the filter uses computed
elements rather than coincidentally passing a matching raw element.

## Setup movement does not count self-damage as future attack potential

NextTurnPotential was still adding every damage row to its small placement
bonus. In the two-case regression a caster with zero AP can walk one cell
into an enemy's 10-damage reply range. The attack has 40 hostile damage and
either zero or 500 caster-only damage. bash_823 reproduced the bad movement
only with the caster cost: Assert.Empty failed because it advanced. The
no-cost control stays in the same table.

The future-potential estimate now includes only immediate modeled damage
rows not restricted to friendly categories. It remains a discounted dice
heuristic, not a simulation of the next turn. Actual casts still resolve
all costs and side effects. Five-suite validation bash_824 exited 0: 203
executed and passed, zero failed or not executed. The saved report
GivreClone.Tests/TestResults/next-turn-self-cost-final.trx records start
17:25:09 and finish 17:25:33.
