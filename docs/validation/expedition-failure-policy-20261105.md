# Confirmed expedition failure policy

## Scope and contract

This implements the user's subsequently confirmed policy. It supersedes the
old next-circuit retry expectation described in
`expedition-route-failure-boundary-20261105.md`; it does not establish the cause
of the historical skipped-map incident.

Generated expeditions attempt a travel or fight action at most three times
before stopping. Travel retries retain the same intended MAPS entry. A failed
fight retries on its original observed map; a changed map is refused rather
than engaging on the replacement map. A genuinely empty map or groups rejected
by the existing monster filters return `no_matching_group`, ending that visit
without retrying. A single-map expedition retains its existing respawn poll.
Successful fights reset the action budget for the next group. Existing limits,
bank yield, defeat policy and cancellation remain in force.

`fight()` retains the boolean first return and adds a reason and original map.
Generated `fight(true, origin)` opts into strict readiness and bypasses the
ordinary failed-engagement cooldown: otherwise an immediately retried refusal
could become an apparently empty map. Ordinary fight/camp/route callers keep
their existing cooldown and unknown-geometry behavior. The actual mandatory,
forbidden, size, amount and reachability selection rules are unchanged.
Classification is produced directly from selection and action decisions, not
from diagnostic text. Cooldown exclusions and unreachable eligible groups are
not classified as no matching group. Readiness is checked before selection and
again after waits that can change the map snapshot. A fight beginning during
post-fight settling is not mistaken for an empty selection.

`travelTo()` retains its boolean first return and adds a stable failure code
and the existing human travel explanation. The generated stop message includes
action, intended map, attempts, reason and available travel detail. It never
branches on human explanation text. Existing routing, shortcuts, internal
movement/attack retries and production readiness waits are retained; the
three-attempt budget counts host action calls, not individual packets. No new
production retry sleep or test sleep was introduced.

## Red evidence

Before production edits, the executed real Lua route suite failed six of 19
tests (13 passed) under monitor `mon_SAEVZ08HCZ9T01ZF` / `bash_2`, exit 1.
Persistent travel/fight errors returned true instead of stopping, and temporary
errors produced zero wins instead of retrying the same action to success.
The actual host selection-status suite then failed eight of 11 tests (three
passed) against the pre-fix host under `mon_M84YQWVFTFNKT8X8` / `bash_5`, exit 1:
the explicit status field did not yet exist. Existing selector/filter behavior
ran before those assertions.

An initial `--no-restore` run with a nonexistent isolated artifact tree exited
without test output and is not test evidence. The red run restored local
dependencies into the isolated tree. A duplicate build was immediately stopped
before production edits to avoid concurrent artifact writes.

## Verification

Independent parent verification: the parent opened all eight claimed C# files,
read the generated retry loop, selector classification, bindings, travel status
assignments and actual host false-return termination. A fresh isolated compile
and the same related test filter completed under mon_5W0KH4CKK82KKPFR / bash_85
with exit 0: 134 passed, zero failed or skipped, reported duration one second.
Artifacts: %USERPROFILE%/AppData/Local/Temp/givre-parent-expedition-policy-20261105.
The parent command used the project and filter below, omitted --no-restore for
the new artifact tree and added -p:BuildInParallel=false -m:1. No connected
account or historical incident was validated by this run.

Fresh LSP diagnostics returned no errors on all four changed production files
and all four changed test files. An intermediate targeted build/run passed
122 tests, zero failed or skipped, exit 0, under
`mon_FRFQT8JGRFM4WJ0P` / `bash_6`. The next build/run passed 125 tests, zero
failed or skipped, exit 0, duration 924 ms, under
`mon_1DAXWK500Y79F6XZ` / `bash_7`, including the real Lua binding and origin guard.
The host loop was also read at `GivreLuaHost.cs:631-637`: imperative false
breaks the loop and ends the run. An executed generated-exhaustion regression
now drives the actual Host.Start/RunCompletion boundary and limits travel calls
to exactly three, all to map 101. Final verification includes that regression
and the existing McpLuaCompletionControlTests completion/cancellation coverage.

The first host-loop integration run (`mon_HVTY42NS7752TDQE` / `bash_8`)
passed 133 tests and failed the new fixture's expected Completed outcome.
Captured host logs confirmed the exact three-attempt stop and ScriptEnded.
The fixture had added a stopped() callback; existing InvokeStopped deliberately
cancels its CTS before invoking that callback, yielding Stopped even for natural
completion. The unnecessary callback was removed, without changing production
termination or weakening the three-attempt stop assertions. The follow-up
single-case diagnostic runs (`bash_9`, `bash_10`) are failure evidence only.

Final build and test run `mon_SXB8DK22534RDPNS` / `bash_11` completed with exit
code 0: 134 passed, zero failed, zero skipped, reported test duration 532 ms.
This includes the actual generated-exhaustion host-loop case, real binding
status/origin guard, selector classification, no-match route progression,
temporary/persistent action failures, configured defeat continuation, banking,
post-fight roleplay, and existing completion/cancellation cases.

Command (absolute project path under the main worktree):

```text
dotnet test "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/GivreClone.Tests.csproj" --no-restore --artifacts-path "%USERPROFILE%/AppData/Local/Temp/givre-expedition-policy-20261105" --filter "FullyQualifiedName~Expedition|FullyQualifiedName~MonsterGroupReachabilityTests|FullyQualifiedName~PostFightRoleplayTests|FullyQualifiedName~ScriptCombatCancellationTests|FullyQualifiedName~McpLuaCompletionControlTests" --logger "console;verbosity=minimal" --tl:off
```

No UI, resolver or planner implementation, account, client, live network action,
git command or commit was used. Live incident resolution is unverified. Existing
build warnings in unrelated files were not suppressed or repaired.

Changed paths:

- `GivreClone.App/Services/ExpeditionScript.cs`
- `GivreClone.App/Services/GivreLuaHost.Combat.cs`
- `GivreClone.App/Services/GivreLuaHost.Bindings.cs`
- `GivreClone.App/Services/GivreLuaHost.Travel.cs`
- `GivreClone.Tests/ExpeditionRouteRegressionTests.cs`
- `GivreClone.Tests/ExpeditionLoopRegressionTests.cs`
- `GivreClone.Tests/ExpeditionCompletionRegressionTests.cs`
- `GivreClone.Tests/ExpeditionSelectionDiagnosticTests.cs`
- `docs/validation/expedition-failure-policy-20261105.md`

Cross-boundaries: previously generated/saved Lua is not rewritten; regenerate
an expedition to obtain the new loop. Travel's internal rerouting budget and
combat's existing two internal reattempts remain separate from the outer
three-call budget. Live transport refusal/readiness timing was not exercised;
the offline tests drive actual host state/status and executed Lua policy, not
a connected account. A persistent technical failure ends the script normally
with its precise error line, rather than changing LuaRunOutcome to Failed.
