# Restrictive expedition selection diagnostic

## Scope

The historical skipped visit in `expedition-restrictive-readiness-20261105.md`
remains unproven. This change adds evidence for a future reproduction; it does
not fix or claim to reproduce that visit. Selection order, mandatory-any matching,
amounts applying only to present species, unknown-geometry fallback, settling,
attack retries, and delays retain their existing behavior.

## Emission and fields

`GivreLuaHost.Combat.cs` emits `selection.no_target` at the initial FightOne
no-target return and at its post-pause, regeneration, and team-wait reselections.
The selector itself emits nothing. Planner/helper calls do not request a
diagnostic. Failed attack retries retain their existing failure diagnostics.
Readiness/permission returns before selection are not covered by this event.

One existing settle boundary needs care when interpreting this event:
`SelectMonsterSettled` can return null because a fight starts during its
post-fight wait, without doing its final selection. In that case the diagnostic
still describes the earlier failed selection before settling, not a fresh
selection at return, and must not be treated as proof that the settled map was
empty or filtered. This concurrent-fight boundary has not been reproduced by
these tests; no combat or settle behavior was changed for it.

Fields are whitespace-delimited `key=value` tokens:

- `reason`: `empty`, `excluded`, `filters`, or `unreachable`.
- `map`, `worldMap`, `selfMap`, `selfCell`, `selfReady`, `cellsMap`,
  `geometryKnown`, `preferredCell`: selection-time readiness and origin.
  `selfReady` is readiness for `worldMap`; compare `map` separately for agreement.
- `observed`: monster groups before exclusion; `excluded`: groups removed by
  the supplied cooldown/camp block set; `eligible`: groups passing script filters.
- `rejectedSize`, `rejectedForbidden`, `rejectedMandatory`, `rejectedAmount`:
  first-rejection counts, in that order. A group belongs to only one category.
  `rejectedReachability` equals `eligible` on a no-target result. These rejection
  counts plus `excluded` partition `observed`.
- `min`, `max`: effective group-size limits, including defaults.
- `forbiddenIds`, `mandatoryIds`: sorted numeric filter IDs, capped at eight
  each; `forbiddenIdCount`, `mandatoryIdCount` expose their full counts.
- `forbiddenNameCount`, `mandatoryNameCount`: active name-filter counts. Names
  are intentionally omitted because arbitrary script strings are not safe log
  payloads. Rejection classification still reports their effect.
- `amounts`: up to eight sorted `gid:min:max` entries; `amountCount` exposes
  the full number. Lists use brackets and commas, never whitespace.

Only the immediately preceding diagnostic string is retained. Identical polling
states emit once; a changed summary emits again. An initial successful selection
clears suppression, so a later no-target episode can emit the same summary.
Memory is constant, independent of visits and polls. List output is bounded;
there is no actor/species inventory dump, actor identity, account identity,
credential, or arbitrary name in the added payload. Existing logging routing
and its transport envelope are unchanged. Counts describe a best-effort mutable
world observation, not an atomic server snapshot or proof of actor publication.
Large filters are explicitly truncated; the line cannot recover omitted IDs or
name strings. It explains rejection classes, not every rejected group.

## Verification

`ExpeditionSelectionDiagnosticTests` drives actual private FightOne through the
same offline reflection convention as adjacent host lifecycle tests. Real world
state, Lua configuration, selector, reachability, and the normal log event run;
no selector fake or new production test seam is introduced. The fixture has a
ready self/geometry map, no client process or network, and disables script-file
opening. It checks parsed machine fields rather than prose. The separate helper
case verifies silent selection and the unknown-geometry candidate behavior.

Programming guidance and both testing references were read before test changes.
Language-server diagnostics were requested for both changed C# files; each
timed out waiting for fresh diagnostics after 3000 ms.

Owned offline build and test command (run from `%USERPROFILE%/Downloads`):

```text
dotnet test "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/GivreClone.Tests.csproj" --no-restore --filter "FullyQualifiedName~ExpeditionSelectionDiagnosticTests|FullyQualifiedName~MonsterGroupReachabilityTests|FullyQualifiedName~ExpeditionFilterResetTests|FullyQualifiedName~ExpeditionLoopRegressionTests|FullyQualifiedName~ExpeditionRouteRegressionTests|FullyQualifiedName~ExpeditionCompletionRegressionTests|FullyQualifiedName~PostFightRoleplayTests" --verbosity minimal
```

Terminal monitor/session handle `mon_SCTV9AT9GAV3TH9W` completed with exit code
0. Its rendered terminal summary reported 64 total, 64 passed, zero failed,
zero skipped; test duration 15.1 seconds. The build succeeded with 255 warnings
in 68.4 seconds. The warnings include existing partial-method signatures,
nullability/platform diagnostics, Avalonia obsolete properties, and xUnit
analyzer findings in other files; they were not suppressed or repaired here.
This run freshly built the source in normal repository outputs, not isolated
artifacts. The terminal API returned the `mon_` handle directly; it did not
expose a separate underlying `bash_` handle for this run.

The first invocation of the same command, session `mon_4R1VDZG2HY41VR0D`,
exited 1 at compilation because the new fixture omitted BotWorldStore's launch
constructor argument (CS7036). That fixture was corrected to `new World(launch)`
before the successful run. No tests were removed or weakened.

The owned whitespace check completed with exit code 0 in session `bash_7`:

```text
git -C "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024" diff --check -- GivreClone.App/Services/GivreLuaHost.Combat.cs GivreClone.Tests/ExpeditionSelectionDiagnosticTests.cs docs/validation/expedition-selection-diagnostic-20261105.md
```

No mutation run was performed. No live app, account action, packet replay,
network operation, git commit, or full test-suite run was performed. Parent
independent validation (`mon_MQQK4T7G6B2WPG8M` / `bash_71`) is separate evidence
and is not claimed as an owned result here.

Exact owned changed paths, relative to the worktree:

- `GivreClone.App/Services/GivreLuaHost.Combat.cs`
- `GivreClone.Tests/ExpeditionSelectionDiagnosticTests.cs`
- `docs/validation/expedition-selection-diagnostic-20261105.md`

Pre-existing edits in Combat.cs and the shared worktree were retained.

## Independent parent verification

The parent read both changed C# files and this document, including the complete
long diagnostic expression without terminal truncation. Fresh test-file LSP
diagnostics returned no errors. The first isolated build, bash_71, exited 1
because the concurrently added SummonDispatchIntegrationTests fixture had
incorrect type aliases; it did not execute tests. That fixture was corrected
without excluding it from compilation.

The next isolated build and test run, mon_179H2HHVPCT3TXKY / bash_72, exited 0:
77 passed, zero failed, zero skipped, reported test duration one second. It used
dotnet test with --artifacts-path
%USERPROFILE%/AppData/Local/Temp/givre-parent-expedition-selection-20261105,
--verbosity minimal -p:BuildInParallel=false -m:1 --tl:off and the filter:

```text
FullyQualifiedName~ExpeditionSelectionDiagnosticTests|FullyQualifiedName~ExpeditionLoopRegressionTests|FullyQualifiedName~ExpeditionRouteRegressionTests|FullyQualifiedName~ExpeditionCompletionRegressionTests|FullyQualifiedName~MonsterGroupReachabilityTests|FullyQualifiedName~ExpeditionFilterResetTests|FullyQualifiedName~SummonDispatchIntegrationTests|FullyQualifiedName~SummonOwnershipIntegrationTests|FullyQualifiedName~CombatSummonCapacityTests
```

This verifies bounded offline diagnostics and adjacent behavior, not the
historical skipped-map incident or a live expedition.
