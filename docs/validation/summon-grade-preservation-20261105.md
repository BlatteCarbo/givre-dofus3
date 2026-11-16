# Simulated summon grade preservation, 2026-11-05

## Contract and narrow scope

The installed 3.7.13.17 export establishes a meaningful monster-grade value
independent of the casting spell's grade:

- `docs/cos-dofus-video/rapports-v2/N01/EFFECTS_3.7.13.17.csv:579-590`:
  Explobombe 13444, action 1008, diceNum 3112, diceSide 1/2/3. The monster
  grade join is documented in `roublard-bomb-contract-20261105.md`.
- The same export at lines 1020/1022: Synchro 13247, action 181,
  diceNum 3958, diceSide 1/2.
- Lines 1225/1226: Cadran de Xelor 13258, action 181, diceNum 3960,
  diceSide 2/3 even though the casting spell grades are 1/2. Copying the
  casting spell grade would lose the effect contract.

Only actions 1008 and 181 have this grade interpretation in this change.
Action 1011 shares existing placement logic but has no established grade
contract here; its grade remains 0. Missing diceSide also remains 0 (unknown),
never 1 and never a fallback to the casting spell grade.

`SpellFighter.MonsterGrade` and `SummonResolved.Grade` are optional fields
defaulting to 0. They preserve metadata, not final monster statistics.
Existing 1/1 occupancy placeholders are not claims about bomb health. No HP,
stats, startup spell, owner scaling, damage, explosion, wall, age, combo or
chain-reaction behavior is introduced.

## Board conversion trace

`SpellResolver.Resolve` snapshots fighters with record `with` copies, then
creates a summoned fighter and event from the effect. Its returned immutable
board retains the grade through later casts and fighter updates.

`CombatTurnPlanner` converts `PlannerFighter` snapshots once when creating
the root `SpellBoard`. Subsequent casts use `n.Board`, update the caster with
a record copy, call the real resolver, and retain `resolved.Board` on the
successor node. Position/life/shield projections do not rebuild fighters.
Walking changes the node's position and the next cast updates the caster by
record copy, so it does not discard summon metadata.

This is simulation metadata only. The current observed summon boundary
(`FightStatsReader.ReadSummons` / `SummonedFighter`) supplies id, cell, team
and summoner, not an established observed monster-grade value. Live snapshot
creation must continue to leave this field unknown unless a later evidence-led
protocol change supplies it. This patch does not establish live bomb correctness.

## Behavioral validation

The adjacent resolver theory drives the actual `Resolve` API, checks the emitted
summon event and the fighter in a subsequent resolved board, and verifies the
input board is unchanged. Its table covers Explobombe grades 1/2/3, absent
grade 0, action 181 samples, and action 1011 as an unproven-contract control.
No source-text assertion, reflection or public test-only seam is used.

The planner currently exposes a plan and score, not its simulated final board,
and has no grade-sensitive production behavior. Therefore a grade assertion
through `Choose` would require an artificial consumer or test-only seam. The
root mapping and sequential-board retention are inspected directly; existing
planner resolver and summon-capacity tests remain the behavioral checks for
those search paths.

The red run used monitor `mon_0JR3HQF22N8DJWC5` / process `bash_3` and exited
1: 9 cases, 6 failed, 3 passed. Each known-grade case lost its grade at the
event boundary, for example expected `(3960, 3, 230, 1)` versus actual
`(3960, 0, 230, 1)`. The absent-grade and unproven-action controls passed.
The optional fields existed for this run, but the summon assignment was still
unchanged, so this was a behavioral failure, not a missing-member compile error.

Both runs use the existing test project with isolated artifacts and an offline
local package source:

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --artifacts-path %USERPROFILE%/AppData/Local/Temp/givre-summon-grade-20261105 -p:RestoreSources=%USERPROFILE%/.nuget/packages -p:NuGetAudit=false --filter "FullyQualifiedName~SpellResolverTests|FullyQualifiedName~CombatPlannerResolverTests|FullyQualifiedName~CombatSummonCapacityTests" --logger "console;verbosity=minimal"
```

The red run used the narrower filter
`FullyQualifiedName~Summon_retains_effect_monster_grade`. The green run uses
the three adjacent suites above, monitor `mon_7WDXS1GK845D63H9` / `bash_5`.

Fresh LSP diagnostics found no diagnostics in either changed production file.
The test file's LSP reported CS0229/CS0121 duplicate-type/member ambiguities
throughout existing aliased App tests as well as the new assertion; the red
command nevertheless compiled that file and executed all 9 cases. The actual
compiler/test result is the validation authority for this linked-source setup.
The red build also emitted 257 unrelated repository warnings, including existing
CS8826 partial-method signature warnings in Hdv view models; none were suppressed
or changed.

The same green run completed with exit code 0: 105 tests passed, 0 failed,
0 skipped. Test execution summary duration was 35.1 seconds; total command
duration was 89.1 seconds. It compiled the real App and linked-source test
project, then ran the three selected suites including all 9 new grade cases.
No production mutations or duplicate compiles were performed while it ran.

## Parent verification

The parent read both record extensions, the summon assignment, planner root
mapping, complete new theory and the validation document. The new theory's
Assert.Single(collection.Where(predicate)) was replaced with the predicate
overload, retaining the same assertion while removing its new xUnit2031
warning. Existing analyzer findings elsewhere are not suppressed.

Independent fresh compilation and tests completed as
mon_222QPT800Q8H7TH5 / bash_82 in
%USERPROFILE%/AppData/Local/Temp/givre-parent-summon-grade-20261105,
covering SpellResolverTests, CombatPlannerResolverTests,
CombatSummonCapacityTests, SummonDispatchIntegrationTests and
SramLearnedDispatchIntegrationTests. The test-file LSP still reports the
documented duplicate-alias ambiguities, so it is not clean diagnostic evidence.
The command exited 0 with 116 passed, zero failed or skipped, reported test
duration 10 seconds. This accepts the bounded metadata-preservation change and
its adjacent offline integrations. It does not establish observed summon grade,
bomb statistics, lifecycle or live tactical correctness.

Artifacts are under
`%USERPROFILE%/AppData/Local/Temp/givre-summon-grade-20261105/`;
the executed test assembly is
`bin/GivreClone.Tests/debug/GivreClone.Tests.dll`.
The console logger was used; no TRX report was requested.

The green build retained 256 warnings. These include an xUnit2031 analyzer
warning on the new regression's `Assert.Single(...Where(...))`, matching an
existing test idiom, as well as unrelated existing repository warnings. This
warning is reported rather than represented as a warning-free build. Fresh
production-file diagnostics were clean; the test-file LSP alias ambiguities
remain an editor diagnostic limitation, not compiler failures in either run.

Only four files were changed: the resolver, the planner's optional field and
root mapping, the adjacent resolver theory, and this validation note.
`CombatPlannerResolverTests.cs` was exercised but not edited: the planner's
grade mapping has no observable grade-sensitive consumer in its current public
plan output, as explained above. The change is complete only for preservation
of simulated summon metadata, not for any whole-class or live combat claim.
