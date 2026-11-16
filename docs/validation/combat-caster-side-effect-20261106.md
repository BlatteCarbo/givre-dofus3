# Caster side effect on an authorized occupied center

Read-only investigation st_01a1af38 identified a mismatch between the two
affected-fighter gates in CombatTurnPlanner.CastOn. The first deliberately
exempts caster side effects from configured target restrictions. The second
required an eligible affected fighter even when the actual occupied center
was independently eligible and a useful effect remained on the caster.

Parent independently read CastOn, Eligible, candidate-center filtering and the
neighboring empty-center regression. A synthetic point spell heals the caster
with mask C and another ally with mask g. The ally is an authorized center at
range one. State76 suppresses its healing, leaving only the caster healed.
This establishes modeled behavior only, not a particular installed spell.

## Causal reproduction

Monitor mon_YWVBHX4MFP6SVANE / bash_219 compiled the added three-row theory
against unchanged production. Exit1: one failed, two passed, zero skipped,
721ms reported test duration. Only incurable=true, allowCaster=false failed:
Assert.Single saw an empty action list. The healthy-ally and caster-allowed
controls passed, including exact selected cell and healing score assertions.

## Bounded correction

The affected-beneficiary gate now also admits an eligible occupied center when
the caster is affected. Other affected-fighter restrictions, HP policy,
target caps, cast constraints, and subsequent scoring are unchanged. An empty
center does not satisfy this exception. The existing empty-center allowed-
beneficiary theory remains unchanged and is included in focused verification.

Focused verification mon_RRG0GX2JY3T9JKPJ / bash_220 exited1 before tests:
MSB3027/MSB3021 could not overwrite GivreClone.exe because isolated QA PID12692
was using that artifacts directory. This is a build-output lock, not a failed
behavioral assertion. No process was stopped. Verification was restarted as
mon_R9R5ATC3YJQVHJV5 / bash_221 with a distinct artifacts directory:
%USERPROFILE%/AppData/Local/Temp/givre-caster-side-effect-20261106. It covers
CombatTurnPlannerTests, CombatUtilityDispatchIntegrationTests and
AutoSpellRotationTests. The isolated run completed exit0:253 passed, zero failed
or skipped,1s reported test duration. This includes all three causal cases and
the unchanged empty-center beneficiary controls. The complete managed suite
is now subscribed as mon_XNMC9BTXKKM9K8QN / bash_222 on that same build with
--no-build --no-restore and caster-side-effect-full.trx. No full-suite result,
live acceptance or deployment is claimed yet.

The full run reported a TimeoutException in the unrelated
MZP0PacketBusTests.Competing_publish_cancel_and_timeout_settle_once_under_load
while still running. Parent read that test: it schedules Publish and Cancel
with Task.Run in each of150 iterations and bounds Task.WhenAll by3s, separately
from the20ms bus timeout. The short failure trace does not identify which
WaitAsync expired, so pool starvation is only a hypothesis, not an established
cause. No PacketBus code, timeout, assertion or test scheduling was changed.
The full run completed exit1:4807 total,4803 passed,2 failed,2 existing
skips,4m34s reported duration. The second failure was
MapInspectionTests.Inspection_stays_fast_with_thousands_of_entities_across_several_bots:
50 searches took8677ms against its8000ms bound. No threshold was changed.
Read-only process snapshot bash_223 listed testhost29888 using2245378048 bytes
working set and no java process; its exit1 reflects absent named processes in
Get-Process, not a test result. This snapshot does not establish the failures'
cause. mon_SEVJNZ4GREAVBVQE / bash_224 waits for that exact testhost to exit,
then runs MZP0PacketBusTests and MapInspectionTests on the same build without
concurrent full-suite work. That isolated reproduction completed exit0:34
passed, zero failed or skipped,1s reported duration, with unchanged production,
timeouts and assertions. The two full-run failures did not reproduce in this
bounded check; this does not identify their cause or turn the failed full run
green. The previously green
full suite predates this correction, so it cannot certify this build.

Subsequent full validation after the incomplete-utility admission correction
passed on the combined current source: bash_228 / mon_6XPZYQS6VA5NHTB3,
exit0,4809 total,4807 passed,zero failed,2 existing skips,3m43s. Receipt:
partial-refund-full.trx. This establishes combined managed regression success;
it does not determine the cause of the earlier two timing failures.

The bounded planner correction is accepted with253 focused passes and the
causal pre-fix failure. Remaining limitations: failed full-run timing/concurrency
checks as recorded above, no specific installed mixed spell or official-server
acceptance, and no live deployment. Broader learned-utility work stays open.

C# LSP initialization still crashes while enumerating the unrelated denied
artifacts/kourial-tools/pypdf directory. No ACL changes or access workaround
were attempted. Compilation and tests provide the available diagnostics.
