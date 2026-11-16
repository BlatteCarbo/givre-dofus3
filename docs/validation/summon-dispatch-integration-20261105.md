# Summon capacity through actual turn dispatch

## Scope and evidence

SummonDispatchIntegrationTests.cs adds two offline cases: the main character
and a controlled companion. Both invoke the real RunFightTurn producer and
parse captured protobuf frames. A socket injector rejects sends immediately;
there is no network operation or arbitrary sleep. The bounded task timeout is
five seconds. Death is delivered through OnDecoded with the catalog's real
GameActionFightEvent death field, rather than directly adjusting a count.

The assertions cover finish-only output at the active actor's living cap,
finish-only output after another owner's summon dies, and a cast followed by
finish after an active-owned summon dies. The cast spell ID and candidate cell
are parsed and checked. The fixture deliberately gives player base resources
and the other actor different capacities, so using their capacity accidentally
does not satisfy the replacement assertion. Both actors' summons are registered
with explicit ownership before the turn.

The descriptor combines summon and area damage and is explicitly synthetic.
Its damage admits it into search. This is not evidence of real pure-summon
spell utility, class-specific summon tactics, server acceptance, or live combat.
No production change was needed for these cases.

## Parent verification

The parent read the entire new test and requested fresh LSP diagnostics, which
returned no errors after the child's type-alias correction. An earlier parent
build, bash_71, failed at those aliases and did not run tests. They were fixed
using explicit F06App aliases and the combat CharIds namespace, without
excluding or weakening the cases.

Independent fresh compilation and tests completed in
mon_179H2HHVPCT3TXKY / bash_72 with exit code 0: 77 passed, zero failed,
zero skipped, reported test duration one second. The command used dotnet test
on GivreClone.Tests.csproj, --artifacts-path
%USERPROFILE%/AppData/Local/Temp/givre-parent-expedition-selection-20261105,
--verbosity minimal -p:BuildInParallel=false -m:1 --tl:off and this filter:

```text
FullyQualifiedName~ExpeditionSelectionDiagnosticTests|FullyQualifiedName~ExpeditionLoopRegressionTests|FullyQualifiedName~ExpeditionRouteRegressionTests|FullyQualifiedName~ExpeditionCompletionRegressionTests|FullyQualifiedName~MonsterGroupReachabilityTests|FullyQualifiedName~ExpeditionFilterResetTests|FullyQualifiedName~SummonDispatchIntegrationTests|FullyQualifiedName~SummonOwnershipIntegrationTests|FullyQualifiedName~CombatSummonCapacityTests
```

The wider living-summon goal remains open pending real spell and live coverage.

## Current-build verification after the local regeneration-control change

The parent re-read the production capacity gates, living owner/roster counter,
planner tests and decoded-death dispatch tests. A fresh focused run on the
current isolated Release build passed 23/23, zero failed or skipped, 4.5 seconds,
exit code 0 (`mon_S41P6CHG0QVVBAV2`, `bash_123`). Filter:
`CombatSummonCapacityTests|SummonDispatchIntegrationTests|SummonOwnershipIntegrationTests`.
Receipt: `GivreClone.Tests/TestResults/summon-capacity-current.trx`.
The dispatch table now includes the classical configured-spell case in addition
to the player and controlled-companion planner cases. This does not close the
pure-summon utility or official-server acceptance gaps, so the broad task stays
open. No production change was made for this verification.
