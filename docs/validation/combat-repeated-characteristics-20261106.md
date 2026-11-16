# Repeated combat characteristic refresh

Read-only investigation st_01a1aed3 found that FightStatsReader selected only
the first characteristic from FightRefreshCharacterStatsEvent. Parent read
the reader, dispatch and existing resource-confirmation tests independently.

The existing CombatUtilityDispatchIntegrationTests now sends confirmed PA and
PM in the same incoming packet, rather than two separate packets. Before the
fix, bash_192 failed both accepted utility cases (direct and child spell),
while both refused cases passed: four total, two failed, exit1. The second
PM update was not applied by the real decoded-packet handler.

ReadCharacteristicUpdates now yields every recognized characteristic in wire
order. Dispatch processes them all through its existing dictionary, point and
range update paths. Existing single-characteristic protocol tests use the
plural reader too. Entries without a recognized value message are skipped.
No duplicate singular compatibility method was retained because all located
callers were migrated. Focused validation bash_193 completed with exit0:
22 passed, zero failed or skipped,16.7 seconds. Both previously failing
accepted dispatch cases now pass. The build reported256 warnings, not
suppressed. Full-suite verification follows because all combat stat refreshes
share this reader. Full-suite bash_194 completed with exit1:4801 total,
4798 passed, one failed, two existing skips,250.8 seconds. The only failure
is MapInspectionTests.Inspection_stays_fast_with_thousands_of_entities_across_several_bots:
50 searches took9163ms. It does not use the combat refresh reader; no test
threshold or production map code was changed. Isolated MapInspectionTests
follow-up is subscribed as bash_195 to distinguish the full-run timing failure.
The full-suite result is not reported as green. Isolated bash_195 completed
with exit0: all20 MapInspectionTests passed, zero failed/skipped,2.8 seconds.
This distinguishes load-sensitive full-run timing from a reproducible isolated
failure; it does not establish that the performance issue is fixed. No map
source, test threshold or scheduling policy was modified.

This does not yet reconcile dedicated Life/MaxLife fields from absolute
characteristics. Their stored component values are not kept synchronized by
life-loss/gain handling, so recomputing HP from a partially refreshed dictionary
would risk using stale deltas. That requires a separate causal contract and
regression. It is not safe to present this repeated-entry fix as the cause or
complete repair of Sylvanbot's early-zero HP discrepancy. The server-confirmed
death requirement and zero-tracked-life targetability remain unchanged.

C# LSP still crashes while enumerating denied artifacts/kourial-tools/pypdf;
no permissions were changed. Compiler/test output supplies validation instead.
