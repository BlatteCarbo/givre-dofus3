# Learned MP utility: execution gap - 2026-11-03

Source inspection of the current primary worktree found a second movement
budget bound not covered by the previous ExpectedSelfPointGain tests.

LaunchService.Combat.Turn.cs initializes `mpStart = mp`. Its local MoveBudget
calls FinishingMovePolicy.RemainingMp(local, start, steps, observed), which
returns max(0, min(local, start - steps, observed)). After an acknowledged
utility cast, the planner branch adds ExpectedSelfPointGain.Mp to `mp`, but
does not update `mpStart`. Thus the opening-turn ceiling still applies even
when both local and observed PM have increased.

Concrete arithmetic: opening PM 3, two confirmed steps, gain 2 PM; local PM
and server PM are now 3, but RemainingMp(3, 3, 2, 3) returns 1. With zero
opening PM it remains zero despite a positive observed gain. This is a
source-derived execution gap, not a reproduced server incident.

The existing AutoSpellRotationTests prove immediate self-gain arithmetic,
AP-boost-to-attack planning, and generated support/teleport configuration.
CombatFinishingMoveTests protect the initial ceiling against reusing already
spent movement. Neither suite currently drives a learned MP boost through
the real asynchronous automatic-turn loop. Changing the shared ceiling
without preserving that prior invariant would risk re-enabling spent PM.

## Reproduction and correction

The five new CombatFinishingMoveTests cases were run against the unchanged
calculation (the optional gainedMp argument existed but was not yet used).
Exit 1: three failed, two passed. Failures were expected 3 / actual 1,
expected 2 / actual 0, and expected 2 / actual 0. These reproduce the ceiling
error independently of game timing; the local/server lower-bound controls
already passed.

RemainingMp now calculates startMp + gainedMp - stepsUsed before taking the
minimum with local and server balances. The automatic planner execution
accumulates the existing immediate self-gain estimate only after the
acknowledged-cast gates, and passes that total to MoveBudget. The initial PM
and confirmed step count are not reset. NoMove and OneMove remain applied
after this calculation, unchanged; their live execution is not proven here.

Validation command (Release, --no-restore):

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CombatFinishingMoveTests|FullyQualifiedName~AutoSpellRotationTests|FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~ControlledCombatTests|FullyQualifiedName~SpellResolverTests" --nologo -v minimal
```

Exit 0: 149 passed, zero failed or ignored, duration 8.9 seconds. Compilation
reported 255 warnings (226 application, 29 tests); none were suppressed.
The new cases now pass alongside the existing no-gain movement cases.
This proves the shared budget calculation, not the full asynchronous turn
loop or server acceptance. The real-client test remains paused at the
user's request pending the game update.

## Generated-rotation MP combo

AutoSpellRotationTests.Learned_mp_boost_opens_a_walk_then_attack_from_zero_mp
starts with four AP and zero MP on a four-cell corridor. The generated
rotation admits a one-AP self boost (action 128, two MP) and a three-AP
range-one attack. The real planner and resolver must choose boost 70,
walk through the two intermediate cells, and attack 71 for ten damage.
A disabled profile entry for boost 70 must instead yield no casts, no
movement and zero damage. No mock grants the points or chooses the plan.

Release command with --no-restore and filter AutoSpellRotationTests,
CombatTurnPlannerTests, CombatFinishingMoveTests completed with exit 0:
116 passed, zero failed or ignored, duration 4.8 seconds. This covers
offline generated-rotation planning, not acknowledged execution in game.

## Recursive point effects - 2026-11-05

Independent reproduction with the original continuation helper failed as
intended: a child spell granting two MP resolved correctly on the board, but
ExpectedSelfPointGain returned (0, 0) instead of (0, 2). Monitor
mon_RH7VYZR4ZNKNM9BN exited 1; one test failed in 35.2 seconds. Outputs:
`%USERPROFILE%/AppData/Local/Temp/givre-subcast-red-parent-20261105`;
TRX: `GivreClone.Tests/TestResults/subcast-red.trx`.

The acknowledged-cast branch now supplies its pre-cast CombatTurnSnapshot
to the helper. Planning and continuation share the existing observed-board
mapping through ObservedBoard. The full resolver receives the spell lookup,
caster level, observed state/breed knowledge and passable cells. Only point
events addressed to the original actor contribute; resource losses are
included before clamping the net gain. Cast rejection and sequence-start
confirmation gates remain before this accounting. A preceding walk returns
to the observation loop before casting, so its old position is not reused.

The first green run, mon_33K9H0ZK3C4H4ASF, passed 291 tests with no failures
or skips in 79.8 seconds, exit 0. Its TRX contains the original single-case
regression, not the subsequent seven-row theory; it does not prove those
later edits. A fresh run of the current source is registered as
mon_AN41QT48902G5A4T, with isolated outputs
`%USERPROFILE%/AppData/Local/Temp/givre-subcast-current-20261105` and logger
`subcast-current.trx`. This run completed with exit 0: 322 passed, no failures
or skips, 52.9 seconds. The parent inspected the TRX and confirmed all seven
rows of Executed_subcast_point_gain_respects_recipient_and_immediate_resolution
are present, unlike the earlier single-case run. Filters included
AutoSpellRotationTests, CombatTurnPlanner, SpellResolver, FinishingMove,
ControlledCombat and SummonDispatch.

Current-source command (run from the primary worktree):

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --artifacts-path %USERPROFILE%/AppData/Local/Temp/givre-subcast-current-20261105 -p:UseSharedCompilation=false --filter "FullyQualifiedName~AutoSpellRotationTests|FullyQualifiedName~CombatTurnPlanner|FullyQualifiedName~SpellResolver|FullyQualifiedName~FinishingMove|FullyQualifiedName~ControlledCombat|FullyQualifiedName~SummonDispatch" --logger "trx;LogFileName=subcast-current.trx"
```

The full managed suite is now running against those same compiled outputs,
without rebuilding: mon_XVCKAKBFTVK1R04Q, logger `subcast-full.trx`.
It exited 1: 4,749 total, 4,746 passed, one failed and two existing skips,
174.5 seconds. The failure is
MZP0RecoveryJournalTests.Explicit_reconciliation_is_bound_to_original_uncertain_generation
(DefinitelyNotApplied): IOException "Impossible de supprimer le fichier à remplacer."
from FileSystem.ReplaceFile through MZP0RecoveryJournal.WriteAtomic/Activate.
The parent read the failure stack and fixture: the test uses a GUID-scoped
temporary directory. Neither the recovery journal nor its test was changed
by this correction. The environmental or journal cause is not established;
this run is not a clean full-suite validation and no test was weakened or
retried to conceal the failure. All seven new continuation rows passed.

A separate diagnostic run of MZP0RecoveryJournalTests on the same binaries
(mon_FTH021JXB2WFJ4Q4, logger `subcast-recovery-diagnostic.trx`) passed all
21 tests in 3.5 seconds, exit 0. This establishes that the journal failure
did not reproduce in that isolated run; it neither explains its cause nor
turns the failed full run into a green result. No journal code or assertions
were changed. The full-suite failure remains a recorded reliability finding.

Source inspection after that diagnostic confirmed the temporary writer is
disposed before File.Replace, the persistent lease targets a separate .lck
file, and the test disposes its first journal before opening the resumed
instance. Independent read-only investigation reached the same conclusion:
no demonstrated handle leak or shared fixture justifies a source fix.
Attribution needs the exception HResult, absolute failed path and concurrent
file-open trace. No sleeps, retry policy or atomic-write fallback was added.

The expanded theory distinguishes original-caster versus delegated-ally
gains, missing child lookup, delayed/random/unknown-mask effects, and an
ordered MP removal after a gain. These are offline continuation calculations,
not a wire-level acknowledged boost-to-walk scenario or official-client
acceptance. Fractional accumulation and live class-specific spell behavior
remain unverified by these cases. Read-only independent source review found
no concrete correctness defect; that review does not replace execution evidence.

## Acknowledged dispatch integration

CombatUtilityDispatchIntegrationTests adds four synthetic offline scenarios
through the actual RunFightTurn loop: direct versus child-spell MP gain,
each with decoded acceptance or decoded rejection. Initial resources are
four AP and zero MP. The injector records the actual cast, then the fixture
sends SequenceStartEvent, real FightRefreshCharacterStatsEvent updates and
SequenceEndEvent for acceptance, or TextInformationEvent170 for rejection.
The accepted cases require a MapMovementRequest covering exactly the two
available steps; rejected cases require no movement. Movement injection
itself returns false, so this proves dispatch eligibility, not completed
movement or attack after movement. No server or official client is involved.

The parent read the complete new file and the real SendCast/WaitAfterCast
gates, inspected the child's TRX (four passed), then independently executed
the focused class against those binaries. Monitor mon_6ZW8GSNCKSEKNJVQ:
four passed, zero failed/skipped, 3.1 seconds, exit 0; logger
`mp-dispatch-parent.trx`, output root
`%USERPROFILE%/AppData/Local/Temp/givre-mp-dispatch-20261105`.

A sensitivity run is currently registered as mon_521MBTVW0M9YMCJV,
logger `mp-dispatch-sensitivity.trx`, separate output root
`%USERPROFILE%/AppData/Local/Temp/givre-mp-dispatch-sensitivity-20261105`.
Only the acknowledged adapter call was temporarily returned to its old
descriptor-only helper; the parent confirmed that source line before the
run. The accepted child-spell row must fail for missing movement. Result
The sensitivity run exited 1: three passed, only the accepted child-spell
row failed, because Assert.Single found no movement request. Duration
20.9 seconds. The parent restored the snapshot-aware adapter call exactly;
no temporary mutation remains. Fresh restored-source validation including
combat, resolver, summon dispatch and local-control/security tests is active
as mon_WS0C0FHGHFKBRJNK, logger `mp-dispatch-restored.trx` in the original
dispatch output root. It completed with exit 0: 342 passed, zero failures
or skips, 50.0 seconds. This includes all four dispatch scenarios after
restoring the adapter, the seven helper controls and the local-control
isolation/security cases. The full suite's earlier atomic journal failure
remains separately recorded; this targeted result does not erase it.

Final full managed run on these restored binaries completed:
mon_8B3SWSM14490KTVX / logger `mp-pipe-full.trx`, exit 0,
4,754 total, 4,752 passed, no failures and the same two existing GameCodec
skips, 182.3 seconds. Command: dotnet test GivreClone.Tests/GivreClone.Tests.csproj
-c Release --no-build --no-restore --artifacts-path
%USERPROFILE%/AppData/Local/Temp/givre-mp-dispatch-20261105
--logger "trx;LogFileName=mp-pipe-full.trx".
This is current combined regression evidence for the continuation correction,
four dispatch cases and profile-isolated control bridge. The earlier journal
replacement failure remains unexplained rather than fixed by this passing run.
