# Challenge notification is not a cast refusal

The parent read the full live Bastionbot journal
`044726_pid1073741825_victoire.log`. Its final cast was injected at+5.045s;
challenge notification188 followed at+5.069s and authoritative enemy death
at+5.071s. The notification was incorrectly recorded as cast_refused before
the accepted action's sequence confirmation. This is evidence of incorrect
classification, not proof that the cast failed or that the fight was lost.

`LaunchService.Combat.cs` already excluded companion notification597. The
existing `LiveCombatRegressionTests` challenge test was extended to cover188
with the observed parameters. Pre-fix bash_207 exited1:188 failed because the
rejection collection was not empty;597 passed. No timing sleep was introduced.

The production exclusion now covers188 and597. Actual refusal handling,
sequence admission, acknowledgment logic and challenge tracking are unchanged.
Focused post-fix validation bash_208 exited0:26 passed, zero failed or skipped,
59.9 seconds, with256 build warnings unsuppressed. This includes existing true
refusal cases and both challenge notifications. Full-suite validation is
subscribed as bash_212. No corrected application has been deployed. The change does not
implement challenge-aware planning or prove the separate early-zero HP cause.

During the still-running full suite, the zero-level row of
FmPreparationControlTests.Preparation_uses_local_skill_compatibility_and_observed_level
reported TimeoutException. Parent read the fixture: it awaits Lua RunCompletion
with a ten-second bound after starting a local, nontransactional preparation
check; it contains no sleep. This is not a failed admission assertion, and the
combat change does not modify that path. Root cause is not established. No
timeout, assertion or production FM behavior was changed. Final full-suite
totals were not produced: bash_212 was killed at its monitor deadline. The
incomplete full run is not green. Isolated follow-up bash_213 exited0: all four
FM preparation rows passed, zero failures or skips,3.4 seconds. Neither that
isolated pass nor the26 combat passes repairs or waives full-workload timing.
Read-only process/working-set measurement is subscribed as bash_214 to examine
concurrent load. No timing threshold was increased and no test was disabled.

Serialized full-suite bash_215 completed with exit0 after the identified Ghidra
process ended:4804 total,4802 passed, zero failed, two existing skips,
246.8 seconds. No source, timeout or test policy changed between the incomplete
concurrent run and this serialized validation. This establishes a clean full
run of the corrected build, not a diagnosis or fix of general test contention.
The application still has not been deployed; live observation preceded the fix.

The C# language server still crashes while enumerating the access-denied
artifacts/kourial-tools/pypdf directory. Permissions were not changed; compiler
and test results are the available validation boundary.
