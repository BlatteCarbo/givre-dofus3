# Observed summon grade adapter - 2026-11-05

## Contract and bounded change

The existing catalog-driven `FightMonsterReader` decodes `MonsterFighter.creature_grade`
separately from level. `ObserveTreasureFighters` stores the resulting record under
`(pid, ActorId)`. `RunFightTurn` already retrieves that same actor's record for
`MonsterTemplate`; the adapter supplies its `Grade` unchanged to `MonsterGrade`,
or zero when the record or scalar is absent.

The production change is one named argument in
`GivreClone.App/Services/LaunchService.Combat.Turn.cs`. It does not alter the
decoder, observation merging, owner precedence, visibility, threat fallback,
HP, characteristics, or context mapping.

## Regression boundary

`GivreClone.Tests/ObservedMonsterGradeDispatchTests.cs` uses the established
dispatch fixture pattern and invokes the real `ObserveTreasureFighters` and
`RunFightTurn`. It publishes catalog-keyed synthetic Show and Refresh trees,
then parses the producer's emitted empty-plan diagnostic snapshot. The two
theory rows run under player and controlled-fighter contexts with zero observed
AP/MP, avoiding casts, movement, server-confirmation waits, and scheduler delays.

Two different actor IDs share template 1234 but carry grades 2 and 5. Their
explicit owner observations intentionally differ from their decoded owners,
so the snapshot checks existing owner precedence independently of grade.
An omitted grade, a bare context spawn, and an actor with no monster observation
must remain grade zero. The unknown-HP placeholder remains 1000000 with max HP
zero and null characteristics; no stats are derived from grade. The active
actor also remains grade zero. Each row emits only the normal finish-turn frame.

These are synthetic decoded trees, not passive wire captures. This verifies
the decoded scalar's transport into the planner. It does not prove an ordinal
domain of `1..n`, alignment with monster-data array indexes, real missing-context
semantics, or any bomb lifecycle behavior.

## Fixture correction

The preliminary red run `mon_B7VA02SDV7HNKYXA` exited 1 with two failures before
the diagnostic snapshot, not at the grade assertion. The reflection fixture
used `RuntimeHelpers.GetUninitializedObject`, which bypasses field initializers.
Adding actual decoded monsters exercised `MonsterThreat` and dereferenced its
uninitialized `_bestiary` lazy catalog. The fixture now initializes that field
with the real `MonsterThreatCatalog.Empty`, without reading a local bestiary or
adding a production seam. The preliminary TRX is retained separately as
`%USERPROFILE%/AppData/Local/Temp/givre-observed-grade-20261105/results/observed-grade-red.trx`.
Only the corrected run can establish grade-specific red evidence.

Corrected red monitor `mon_83FPB98RJC30BMTD` exited 1: two executed, two
failed, zero passed or skipped. Both rows reached the real diagnostic snapshot
and failed at `MonsterGrade`: expected 2, actual 0. Production was unchanged.
Artifact:
`%USERPROFILE%/AppData/Local/Temp/givre-observed-grade-20261105/results/observed-grade-red-corrected.trx`.

The exact command was:

```powershell
dotnet test "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/GivreClone.Tests.csproj" --artifacts-path "%USERPROFILE%/AppData/Local/Temp/givre-observed-grade-20261105" -p:RestoreSources=%USERPROFILE%/.nuget/packages -p:NuGetAudit=false -p:BuildInParallel=false -m:1 --tl:off --verbosity minimal --filter "FullyQualifiedName~ObservedMonsterGradeDispatchTests" --logger "trx;LogFileName=observed-grade-red-corrected.trx" --results-directory "%USERPROFILE%/AppData/Local/Temp/givre-observed-grade-20261105/results"
```

After this decisive red result, the single production argument was added:

```csharp
MonsterGrade: monster?.Grade ?? 0,
```

## Diagnostics

The new regression has no C# LSP errors. Before the adapter edit, the production
file's LSP already reported eight CS0121 duplicate-symbol ambiguities involving
planner methods. Those are recorded as baseline diagnostics, not silently
suppressed or treated as a clean language-server result.

After the edit, LSP reported CS1739 claiming that `PlannerFighter` has no named
`MonsterGrade` parameter, together with seven duplicate-symbol CS0121 reports.
The source record already declares `int MonsterGrade = 0`; the actual isolated
compiler/test command is used to settle whether the current project builds.
The language-server reports are retained explicitly rather than called clean.

The final authoring check `mon_Z078N4XC0YMSGV3F` exited 0 with
`check-test-junk: no findings`:

```powershell
bun "%USERPROFILE%/.omo/binary-runtime/5.1.57/plugin/skills/programming/scripts/typescript/check-test-junk.ts" "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/ObservedMonsterGradeDispatchTests.cs"
```

The new regression has 190 nonblank, noncomment lines and owns only observed
grade dispatch. It adds no production helper, parsing layer, logging, or type
suppression. The pre-existing large turn service is not refactored because the
authorized production edit is exactly one argument.

## Green validation command

The related integration run compiles the actual App project reference and
uses an isolated artifacts directory. Package resolution is restricted to the
local NuGet cache, with auditing disabled; no network, live session, or git
operation is used.

```powershell
dotnet test "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/GivreClone.Tests.csproj" --artifacts-path "%USERPROFILE%/AppData/Local/Temp/givre-observed-grade-20261105" -p:RestoreSources=%USERPROFILE%/.nuget/packages -p:NuGetAudit=false -p:BuildInParallel=false -m:1 --tl:off --verbosity minimal --filter "FullyQualifiedName~ObservedMonsterGradeDispatchTests|FullyQualifiedName~FightMonsterReaderTests|FullyQualifiedName~CombatAndStatDispatchRegressionTests|FullyQualifiedName~SummonOwnershipIntegrationTests|FullyQualifiedName~SummonDispatchIntegrationTests|FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~CombatPlannerResolverTests|FullyQualifiedName~CombatSummonCapacityTests" --logger "trx;LogFileName=observed-grade-green.trx" --results-directory "%USERPROFILE%/AppData/Local/Temp/givre-observed-grade-20261105/results"
```

This scope includes the new real dispatch adapter regression, the actual reader,
existing self-visibility dispatch, summon ownership and dispatch, planner
projection/resolution, and summon capacity. It is offline integration evidence,
not an end-to-end live-fight certificate.

The first related run `mon_4MH80RNJR1B2DJ1K` compiled the production adapter and
exited 1: 225 executed, 224 passed, one failed, zero skipped. Both new grade
rows passed. The failure was the existing visibility snapshot row
`companion: True, rawState: 987`: its captured log stopped before the snapshot.
The new fixture was running concurrently and restoring the shared global
`TransportLog.Enabled`, so another fixture could lose logging mid-turn. This
is a test-isolation bug, not a grade or visibility assertion mismatch.

The new test now belongs to a dedicated xUnit collection with
`DisableParallelization = true`, following existing repository conventions.
This keeps its global logger setup/restore out of every other collection's
execution without changing existing tests, weakening assertions, adding sleeps,
or rerunning for timing luck. The post-correction C# test diagnostics are clean.
The same related command is rerun with only its TRX filename changed to
`observed-grade-green-isolated.trx`; the failed first artifact is retained.

## Final result

### Independent parent verification

The parent read the complete test, both fixture corrections, the actual
one-argument production edit and this document. The first isolated parent
build/test, mon_KK76AM71AY71WTP7 / bash_83, compiled successfully but exited 1:
38 passed, one failed. The existing companion-invisible visibility case lost
its diagnostic before the grade assertion, matching the shared logger failure
described above. The parent assigned CombatAndStatDispatchRegressionTests to
the same nonparallel transport-logging collection; assertions were unchanged.

Fresh parent compilation and the identical filtered run then completed as
mon_56HXNM4WSGK5686D / bash_84 with exit 0: 39 passed, zero failed or skipped,
reported duration 959 ms. It used isolated artifacts
%USERPROFILE%/AppData/Local/Temp/givre-parent-observed-grade-20261105 and covered
ObservedMonsterGradeDispatchTests, CombatAndStatDispatchRegressionTests,
SummonDispatchIntegrationTests, SramLearnedDispatchIntegrationTests and
FightMonsterReaderTests. This is independent offline acceptance of the
adapter, not observed ordinal semantics, bomb simulation or live proof.

Final related monitor `mon_RQYNS0S58HHHY6WX` exited 0: 225 executed, 225 passed,
zero failed or skipped, reported duration 11 seconds. Both new player/controlled
grade rows and all ten existing self-visibility snapshot rows passed. Artifact:
`%USERPROFILE%/AppData/Local/Temp/givre-observed-grade-20261105/results/observed-grade-green-isolated.trx`.
The command above is exact for this final run with
`--logger "trx;LogFileName=observed-grade-green-isolated.trx"` replacing the first
run's logger argument. The successful test command compiled the actual production
App and test sources; the LSP's absent-parameter report did not reproduce in the
compiler. Existing repository compiler warnings remain visible and unsuppressed.

Final post-isolation authoring monitor `mon_AKJZMK52BCPWZX2W` exited 0 with
`check-test-junk: no findings`, using the same authoring command recorded above.
No validation remains pending.

Changed files, relative to
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/`:

- `GivreClone.App/Services/LaunchService.Combat.Turn.cs`: one grade argument,
  beside template, preserving the existing self-visibility correction.
- `GivreClone.Tests/ObservedMonsterGradeDispatchTests.cs`: actual decoder/store
  and turn-diagnostic regression, isolated from other global logger users.
- `docs/validation/observed-summon-grade-adapter-20261105.md`: contract,
  corrections, commands, monitor results, and limits.

The proven result is actor-keyed propagation of an already decoded grade scalar,
with zero remaining unknown. Decoded scalar does not prove ordinal semantics or
bomb lifecycle. No live, network, or git operation was performed.
