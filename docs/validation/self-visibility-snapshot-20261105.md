# Observed self visibility at the planner snapshot boundary

## Contract and bounded change

`GivreClone.App/Services/LaunchService.Combat.Turn.cs:998` now reads
`FightVisibilityReducer.Get(actor).Visibility` for the active actor as it already
did for other fighters. It no longer substitutes `Visible` for self. Missing
observations remain `Unknown`. The existing confirmed self-position override
is retained: knowing the controlled actor's cell does not make that actor visible.

The packet producer in `LaunchService.Packets.cs:1000-1012` feeds initial and
changed observations for all actors, with no self exclusion. The reducer in
`GivreClone.Protocol/FightVisibility.cs` maps raw protocol states 0, 1, and 2
to Invisible, Detected, and Visible; unrecognized or missing observations are
Unknown. Position confirmation never reveals an invisible actor. The turn's
`SelfActorId` resolves the current `EngineControlled` context before the account
character, so the same correction applies to a server-controlled companion.
`CombatTurnPlannerTests.Invisible_caster_can_still_target_its_own_cell` already
establishes that invisibility does not prevent a caster using its own cell.

Only the snapshot visibility expression changes in production. Planner scoring,
utility admission, spell rules, enemy threats, and UI are outside this change.
This is not full Sram invisibility support or live combat validation.

## Regression boundary

`GivreClone.Tests/CombatAndStatDispatchRegressionTests.cs:230-333` extends the
existing real dispatch fixture. It invokes the compiled production
`RunFightTurn`, using its existing empty-plan JSON diagnostic to observe the
actual snapshot. Both account and server-controlled companion turns exercise
invisible, detected, visible, unknown raw state, and missing observation cases.
Zero observed AP and MP make the planner empty without casts, moves, confirmation
waits, or sleeps; the ordinary finish-turn frame is the sole dispatch. Logging
is subscribed before execution and restored afterward. No test-only production
seam was added. The synthetic damage descriptor only admits this producer path;
it does not model a Sram spell.

Reducer and synthetic planner tests could pass while this producer overwrote
self visibility; this boundary regression specifically catches that overwrite.
The programming skill and its `references/testing/README.md` and
`references/testing/junk-classes.md` were read before test authoring.

## Commands and evidence

Run from the worktree root. All test commands use the actual test project and
build the production App reference, with no restore or network request.

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --no-restore --filter "FullyQualifiedName~Automatic_turn_snapshot_preserves_active_actor_visibility" --logger "console;verbosity=normal" --logger "trx;LogFileName=self-visibility-red.trx" --results-directory GivreClone.Tests/TestResults/self-visibility-20261105 -p:BuildInParallel=false -m:1 --tl:off
```

Corrected red run: `mon_443QGJXXXKBH02FQ`, real session `bash_3`, exit 1.
Ten executed, eight failed, two passed, zero skipped. All eight failures are the
visibility assertion: expected Invisible (2), Detected (3), or Unknown (0), but
actual Visible (1). Both Visible controls passed. Artifact:
`GivreClone.Tests/TestResults/self-visibility-20261105/self-visibility-red.trx`.
An earlier preliminary run (`mon_W1146B553ARE2E4W`) also exposed an incorrect test
expectation of no finish-turn frame; that assertion was corrected before the
decisive red run and before editing production.

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --no-restore --filter "FullyQualifiedName~CombatAndStatDispatchRegressionTests|FullyQualifiedName~ControlledCombatTests|FullyQualifiedName~FightVisibilityTests|FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~CombatPlannerResolverTests" --logger "console;verbosity=normal" --logger "trx;LogFileName=self-visibility-green.trx" --results-directory GivreClone.Tests/TestResults/self-visibility-20261105 -p:BuildInParallel=false -m:1 --tl:off
```

Green/related run: `mon_35J180VEYAJZ3Q1W`, real session `bash_5`.
Result pending at document creation; final evidence is recorded below after exit.

## Independent parent verification

The parent read every changed artifact, the reducer, and the complete decisive
red TRX. Its isolated run mon_XGWXBF1M0YWNAFFS / bash_67 completed exit 0:
236 passed, zero failed or skipped, reported test duration 8 s. Command:

```powershell
dotnet test "%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/GivreClone.Tests.csproj" --artifacts-path "%USERPROFILE%/AppData/Local/Temp/givre-parent-self-visibility-20261105" --filter "FullyQualifiedName~CombatAndStatDispatchRegressionTests|FullyQualifiedName~ControlledCombatTests|FullyQualifiedName~FightVisibilityTests|FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~CombatPlannerResolverTests" --verbosity minimal -p:BuildInParallel=false -m:1 --tl:off
```

Fresh parent LSP requests for both modified C# files timed out; no clean LSP
result is inferred. The successful command compiled the current production
and test sources. This is offline integration evidence, not a live fight.

```powershell
bun %USERPROFILE%/.bun/install/global/node_modules/omo-ai/plugin/skills/programming/scripts/typescript/check-test-junk.ts GivreClone.Tests/CombatAndStatDispatchRegressionTests.cs
```

Final test authoring diagnostic `mon_W9Y7H3F0BET0HQFW`: exit 0,
`check-test-junk: no findings`.

The existing large turn service and dispatch fixture were not structurally
refactored because that would exceed the explicitly bounded edit scope. The
production change adds no helper, parsing layer, variant switch, or logging;
the regression reuses the established reflection fixture and real reducer.
