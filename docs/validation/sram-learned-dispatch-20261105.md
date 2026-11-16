# Sram learned dispatch integration

## Independent parent execution

The parent read the complete added producer test and this document, then ran
dotnet vstest against the freshly compiled isolated assembly at
%USERPROFILE%/AppData/Local/Temp/givre-sram-dispatch-20261105/bin/GivreClone.Tests/debug/GivreClone.Tests.dll
with filter SramLearnedDispatchIntegrationTests, SummonDispatchIntegrationTests
and CombatAndStatDispatchRegressionTests. Monitor mon_6RM4W6NKTCS2ZGMA /
bash_80 completed with exit code 0: 32 passed, zero failed or skipped,
reported test duration one second. This is an independent execution, not a
second compilation or a live combat result. No production mutation was run.

## Scope

`SramLearnedDispatchIntegrationTests` invokes the actual private
`LaunchService.RunFightTurn` producer with a real profile, learned spell catalog,
map reader cache, observed resources and visibility reducer. It captures socket
frames through the existing injector, rejecting the first cast to avoid server
acknowledgment waits, and parses protobuf request code, spell ID and target cell.
It neither reproduces `AllowedTargets` nor introduces a production seam.

The descriptor for 12913 preserves the documented three grades: AP 2, range 0-4/5/6,
flags 512, interval 3, zero caps and initial cooldown, action 150 Unknown with `a,A`
mask and duration 1, and action 128 Buff with `a` mask, duration 2 and MP 1/2/2.
Both effects retain point shape 80, size 1, minSize 0, element -1 and trigger `I`.
`Uncertain=true` represents the converter's nonzero globalCooldown result; the
descriptor contract does not retain that raw field. This is a hand-constructed
descriptor based on the export, not execution of the Unity parser.

The tactical board and attack 42 are explicitly synthetic. A three-cell corridor
places the caster at cell 0, an enemy at cell 2, and starts the caster with AP 5,
MP 0. The synthetic attack costs AP 3, deals 10 damage and has range 1. Even grade 1's
MP 1 permits movement to cell 1 followed by the attack. No arbitrary planner scores
are supplied. The actual first emitted cast must be 12913 targeting cell 0 (self).
The rejected socket write means the later move and attack are not sent or
acknowledged by this fixture.

Each grade has two controls: a manual 12913 entry with default Enemy target
restriction must emit only finish-turn, and removing the following attack must
also emit only finish-turn. The controls distinguish productive MP search from
an unconditional invisibility cast. These assertions cover self routing, not
nonself ally choice or ally MP valuation. Defensive invisibility valuation,
enemy invisibility choice, server acceptance, subsequent execution, reveal,
expiry, cooldown reset and complete Sram tactical behavior remain outside scope.

## Sensitivity proposal (not applied)

At `LaunchService.Combat.Turn.cs`, change only
`if (configured && (cfg.OnMe ? f.ActorId != selfId`
to `if ((cfg.OnMe ? f.ActorId != selfId`, retaining the remainder of the ternary
and closing parentheses with one fewer `)`. This deliberately applies manual
target narrowing to generated entries as well. The learned-only successful rows
should then emit no 12913 self cast and fail the parsed frame assertions; manual
controls should retain their outcome. Only the coordinating parent may run this
temporary production mutation after ensuring no concurrent compile uses it.
No mutation or production change has been made by this task.

## Verification

The first isolated compile exited 1 because the new file omitted the
`GivreCore.Protobuf` import (three CS0103 errors for RawProtobuf). This was fixed;
no tests ran in that attempt. The initial diagnostics request timed out after
3000 ms; a fresh request after the import correction and unused-import removal
returned `No diagnostics found`.

The corrected isolated compile and related producer test run exited 0:
**32 executed, 32 passed, 0 failed, 0 skipped**. The nine new Sram rows all passed,
including the three learned-only self-target cast rows and both controls per
grade. Their TRX durations range from 0.0252991 to 0.6959280 seconds; no five-second
fixture timeout occurred and the timeout was not widened. Existing unrelated
source/analyzer warnings remain (30 warnings in the incremental successful build).

Monitor: `mon_X4YQTEBE6KWSGF8F` (completed, exit 0).
TRX, read directly:
`%USERPROFILE%/AppData/Local/Temp/givre-sram-dispatch-20261105/results/sram-dispatch.trx`.
Fresh assembly:
`%USERPROFILE%/AppData/Local/Temp/givre-sram-dispatch-20261105/bin/GivreClone.Tests/debug/GivreClone.Tests.dll`.

Command, run from the worktree:

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --artifacts-path %USERPROFILE%/AppData/Local/Temp/givre-sram-dispatch-20261105 --filter "FullyQualifiedName~SramLearnedDispatchIntegrationTests|FullyQualifiedName~SummonDispatchIntegrationTests|FullyQualifiedName~CombatAndStatDispatchRegressionTests" --logger "trx;LogFileName=sram-dispatch.trx" --results-directory %USERPROFILE%/AppData/Local/Temp/givre-sram-dispatch-20261105/results -m:1 -v:minimal
```

Mutation sensitivity remains a proposal, not observed red/green evidence. The
parent explicitly prohibited production mutation during concurrent UI work.
