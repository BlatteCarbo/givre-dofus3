# Controlled fighter characteristics

## User correction: companion shares owner level

The user clarified: "niveau du compagnon = celui du proprio hein".
The attempted controlled-root EngineCasterLevel=0 change and its mistaken
test expectations have been removed. The turn snapshot again supplies
CharacterLevel(pid) directly. Companion characteristics, range and spellbook
remain actor-specific; level is shared with the owner under this contract.
Earlier recommendations below for a separate companion level are superseded.

bash_238 returned283 passing tests,exit0,26s for the pre-correction version;
that receipt does not validate the restored source. A fresh focused build/test
is required. The separate recursive target-caster shield boundary remains:
an arbitrary recursive caster is not thereby established to be a companion.

Restored-source validation bash_241/mon_0X3HPWQBD6NWD5BH completed exit0:
283 passed,zero failures/skips,18s. Receipt:
GivreClone.Tests/TestResults/companion-owner-level-restored.trx.
This covers controlled identity/characteristics and resolver/planner cases,
not a fresh live companion cast or full-suite green result.

Full restored-source validation bash_242/mon_K15TQTRPYDYBJTN2 completed exit0:
4811 total,4809 passed,zero failed,2 existing skips,3m45s.
Receipt:GivreClone.Tests/TestResults/companion-owner-restored-full.trx.
This supersedes the earlier pending full-suite status. No live deployment
or live companion-level shield acceptance is claimed.

## November6 level-scaled shield boundary

Parent read the current turn snapshot, CharacterLevel, controlled context and
recursive resolver. Unlike SelfCharacteristics and SelfRange, CharacterLevel
reads only the authenticated character's _charLevel entry; it does not select
EngineControlled. The snapshot still supplies that value as CasterLevel.
SpellResolver action1020 uses that one value for percentage-of-level shields,
including nested casts that can switch caster to the effect target via
actions792/1017/2794. Those actors need not share the owner's level.

This is a concrete input-attribution gap, not evidence of a particular live
companion miscast. No production change was made: changing CharacterLevel
globally would also affect owner license and other non-combat consumers.
The next bounded correction needs an actor-specific observed-level input at
the combat boundary and a deterministic differing-owner/companion or recursive
caster shield scenario. Unknown actor levels must remain unknown rather than
borrow the owner's value. The existing passing characteristic/range tests do
not certify this separate level-scaled shielding path.

### Recursive-caster correction, bounded scope

Parent added a two-row resolver regression: action1160 keeps the original
caster, action792 changes it to a target whose level is unavailable. Both
reach the same action1020 child spell. bash_229/mon_J7SD1EEFH3M8RV0N exited1:
one failed,one passed,124ms. The changed-caster row expected0 shield but got100
using the original level200 and effect50percent. The original-caster control
passed. This is synthetic recursive-caster evidence, not a companion live cast.

Action1020 now reports an unmodeled effect when its current caster differs
from the original caster or the supplied level is nonpositive; it does not
invent a shield value. Other shield actions and owner-level consumers were
unchanged. This fixes recursive borrowing only, not the separately documented
controlled-root snapshot level gap. No actor-level storage was added.

bash_230/mon_BDES6EHBZ18DMXWH completed exit0:283 passed,zero failed/skipped,
11s, covering SpellResolverTests,CombatPlannerResolverTests,CombatTurnPlannerTests
and ControlledCombat fixtures. Full validation is pending as
bash_231/mon_JA6GH15ZGF19WY1A with recursive-level-full.trx on the same build.
No deployment or full companion-role completion is claimed.

The full run reported the existing PacketBus load test timing out at3s while
still running. Final totals remain pending; no threshold or PacketBus behavior
was changed. Follow-up source reading confirms controlled grants carry only
MasterId,ActorId and spell grades (FightControlReader), not actor level.
FightFighterShowEvent does retain ReadShowFighterLevel via NoteFighterLevel,
so a future controlled-root fix should reuse observed actor-specific data,
with the same fight cleanup and without altering CharacterLevel's owner role.

Follow-up correction: ReadShowFighterLevel currently reads only
ai_fighter.monster_fighter_information.creature_level, not every fighter
variant. Its existence does not establish companion-level coverage. The
controlled-root fix must not assume this dictionary contains companion levels
or silently fall back to the owner's level. No such reuse is implemented.
The recursive-level full run finished with4808 passing,one PacketBus timeout,
2 existing skips (4811 total). After fixture synchronization, all recursive
shield rows and PacketBus passed focused validation; the subsequent full run
also passed these cases but failed the unchanged relay observer test instead.

The controlled-turn adapter discarded observed companion characteristics
and supplied an empty dictionary to generated spell ordering and the caster
snapshot. SelfCharacteristics itself always returned the authenticated
character's dictionary, unlike SelfStats and LearnedSpells, which already
select the active server-granted controlled context.

The existing identity/resource/spellbook fixture was extended with distinct
characteristics: master 400, companion 150. In the controlled engine context
it reproduced expected 150, actual 400 (one failed, exit 1, duration 2.3
seconds). Its first attempted run failed compilation because the fixture
assigned Dictionary instead of ImmutableDictionary; only the fixture type
was corrected before the behavioral reproduction.

SelfCharacteristics now returns the observed controlled fighter statistics
inside that context, or an empty immutable dictionary when none are known.
Outside the context the master's dictionary remains unchanged. The live
turn adapter now calls this accessor instead of discarding companion data.
No master fallback, inferred class or fabricated characteristic is added.

The combined controlled/rotation/resolver/planner test run passed: 144 tests,
zero failed or skipped, exit 0, duration 32.4 seconds. The identity fixture
was then expanded to include unknown companion characteristics; the focused
controlled-combat run passed all 14 tests, zero failed or skipped, exit 0,
duration 10.5 seconds.

The same fixture exposed a second isolation defect: SelfRange returned the
master's +6 in the controlled context instead of the companion's observed
-2 or unknown 0. Both rows failed before correction (two failures, exit 1,
duration 3.2 seconds). The turn adapter separately forced companion range
to zero at initialization and each planner decision, ignoring observed
bonuses and penalties. SelfRange now selects the controlled actor's range
characteristic, without master fallback, and both turn sites use it.
The combined verification after this range correction passed all 145 tests,
zero failed or skipped, exit 0, duration 19.4 seconds.
This is not live companion-combat verification or complete companion role
planning. State, portal, trap and forced-movement safety limitations remain.

## Server-confirmed changes between planner decisions

Inspection found a separate stale-read defect: the turn adapter captured
SelfCharacteristics before its decision loop, while FightRefreshCharacterStatsEvent
updated the tracked actor's FighterStats.Characteristics. CombatTurnPlanner
uses snapshot.Characteristics for its caster, overriding the otherwise fresh
characteristic map projected into PlannerFighter. Thus later decisions could
continue using pre-buff damage/healing characteristics.

The adapter now rereads SelfCharacteristics at each decision and overlays the
current actor's observed FighterStats.Characteristics before rebuilding the
snapshot. Missing combat entries retain the known sheet values; controlled
actors retain their existing no-master-fallback behavior. This does not infer
or predict unconfirmed buff values.

The related controlled/rotation/resolver/planner suite is running under
bash_722. No existing test seam was found that drives two real automatic
decisions with an intervening stats packet; the existing snapshot-only tests
cannot prove that adapter integration. No artificial snapshot test was added
as a substitute. Live mid-turn buff verification remains outstanding.
The C# language server still crashes while enumerating the unrelated denied
artifacts/kourial-tools/pypdf directory; no ACL change was attempted.

The related suite completed under bash_722: 145 passed, zero failed or skipped,
test duration 43.2 seconds, exit 0. Total build/test duration was 175 seconds
with 257 warnings; none were suppressed. This confirms compilation and the
existing related contracts, not the missing two-decision adapter scenario or
live confirmation of a mid-turn characteristic change.

Related tactical safety verification on the compiled build subsequently passed
28 tests from FightVisibilityTests, CombatSelfZoneSafetyTests and
CombatForcedMovesReplayTests under bash_726: zero failed or skipped, duration
2.5 seconds, exit 0. These cover visibility observations, conservative targeting,
self-zone and forced-movement replay safety. They do not establish complete
Sram trap/invisibility planning or live companion behavior.

A further inspection found that the retained walk-prefix continuation checked
fighter maps but not the merged caster sheet used by the snapshot. Added that
map to the existing continuation validity check and stored it with the retained
plan, so a sheet change that does not alter FighterStats also forces replanning.
The combined 145-test subset is being rebuilt/rerun under bash_727 after this
additional change. The preceding pass does not validate the new edit.

The post-continuation-change verification under bash_727 passed all 145 tests,
zero failed or skipped, duration 22.3 seconds, exit 0. The missing real
two-decision integration and live mid-turn buff verification remain open.
