# Parent audit of combat, journal and suspension DAG

Run: `dag_90403bff-6f50-485f-99a2-f56e94709778`.
Status: **reopened for cross-turn journal attribution; not final acceptance**.

The original four node prompts were recovered from the persisted native DAG
definition. Completed node status is not evidence that its obligations hold.
The shared working tree includes earlier work; a diff against HEAD alone cannot
attribute every changed line to a particular child.

## Independently observed baseline

The parent read the journal, suspension and verification reports. The required
`combat-empty-turn.md` was absent. The previous verification report explicitly
failed the combat lane: missing report and two failing challenge planner cases.
Those planner cases were subsequently corrected by the parent; that correction
must not be attributed to the child.

The parent inspected `CombatJournal.cs`, `CombatJournalService.cs`,
`CombatJournalTests.cs`, `BanGate.cs`, `BanGateTests.cs`, the suspension branch
and launch guards in `AccountsViewModel.Socket.cs`, the account ban properties,
and the three localization entries. Read-only Git comparisons confirmed the
relevant changes, but do not establish ownership of unrelated earlier edits.

Independent command:

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --artifacts-path %USERPROFILE%/AppData/Local/Temp/givre-parent-audit-20261105 --filter "FullyQualifiedName~CombatJournal|FullyQualifiedName~Ban" --verbosity minimal
```

Result: exit 0; total 137, failed 0, passed 137, skipped 0, duration 28.4 s.
The recorded journal red run was also inspected: 8 failed, 10 passed, total 18.
It shows 56 casts instead of 30 and companion HP/actions replacing self values.
Historical red output is recorded evidence, not a new parent execution.

The parent also opened both original saved fight logs rather than relying on
the test samples. The victory header reports 56 casts and 47 moves; its rows
show zero self HP from round 4 despite subsequent actions. The defeat log's
round 3 shows 13 AP, a forced challenge move and an end-turn request, without
a player cast between those boundaries. These observations establish the
accounting/empty-turn symptoms, not a maximum-turn ban cause.

## Corrections requested on the same DAG

### combat-empty-turn

The only full empty-plan snapshot found at initial audit was
`[combat-companion-empty]` in `LaunchService.Combat.Turn.cs`. The self-player
diagnostic and required report were still owed. Send and retry were refused
because this node/run was completed; amending this same run reopened this node
and invalidated its dependent verification, without creating a replacement DAG.
The correction is limited to the owed diagnostic/report and original checks;
the child's prompt explicitly preserves the parent's later planner changes.

The parent subsequently read the changed branch: empty plans now choose
`combat-player-empty` or `combat-companion-empty` and serialize the same snapshot
in 3500-character chunks. At that inspection the report was still missing and
the node had again returned completed with its test command pending. It was
revived on the same node to collect that result and finish the actual report.

### journal-attribution

`TrackTurn` counted Cast/Move with `Actor.None` as self. A delayed movement
outcome after a companion finish could therefore increase self totals without
belonging to any self round. Refused and timed-out movement outcomes had no
behavioral attribution coverage. The formatter described confirmations although
the counter includes refused/time-out outcomes. The same node was revived with
these concrete corrections and deterministic behavioral test requirements.

### ban-auto-resume

A manual wake completed the ban wait early, then unconditionally called
`BanGate.Clear`, even if the suspension had not expired. The timed suspension
branch also lacked the ordinary backoff branch's check for auto-relaunch being
disabled during the wait. The same node was revived to preserve an unexpired
mark, respect the current relaunch setting, and test the real decision without
sleeps or source-string assertions.

### verify

The existing verifier was sent these findings and told not to accept the lanes
before corrective deliveries. Its original Release build and combined filtered
test obligations remain. The parent still owes independent inspection of final
corrective code, required commands, scope and completeness.

Independent original combat-filter execution is running with isolated artifacts
`givre-parent-audit-combat-20261105`, monitor `mon_N6HSN9TWHYS4GA28`.
The independent Release app build is running with isolated artifacts
`givre-parent-audit-release-20261105`, monitor `mon_453V2R0ZJHS11NR2`.
Neither command is claimed successful before its exit and summary arrive.

Both commands have now completed independently: combat filter 259 passed,
zero failed or skipped, exit 0 (76.4 s); Release app build exit 0 with 226
warnings (123.6 s). Warnings were not suppressed. The required combat report
now exists and accurately distinguishes its diagnostic edit from parent-owned
planner fixes and historical red evidence.

The parent inspected the subsequent suspension correction: CompleteResumeWait
is used by the real socket branch, preserves an unexpired mark on manual wake,
and rejects a timed resume when auto-relaunch has been disabled. Four injected
time tests cover the decision without sleeping. This does not prove the async
session lifecycle itself. The journal now captures movement-request ownership
and leaves outcomes outside a turn unassigned when no request proves ownership;
its new tests exercise confirmed/refused/timeout for both actors and a delayed
companion result before the next self turn.

The original combined verification filter is running independently after those
code corrections, with artifacts `givre-parent-audit-final-20261105`, monitor
`mon_EWSDFC6VF3C62RMG`. Its result is pending. The journal correction report
was still stale when inspected; acceptance remains open until the final report,
the verifier's findings and current-state checks have all been reconciled.

The combined parent run completed with exit 1: 449 total, 447 passed, two
failed, zero skipped (70.4 s). Both failures are the new journal regressions:
the unassigned-outcome case expected 0 but got 1, and the late companion-result
case expected 1 but got 0. Immediate source inspection found that the previously
reviewed correction was no longer present: lineActor was assigned directly
from _actor rather than _moveOwner, and the self counter used an unconditional
else rather than an Actor.Self check. This exactly explains the two failures.
The parent notified the same journal node and verifier, requested restoration
and stable-source confirmation, and prohibited further shared-source mutation
during parent verification. Whether this was a deliberate red mutation remains
unconfirmed; it is not assumed to be an unrelated or pre-existing failure.
The final combined validation therefore does not pass and must be rebuilt after
the actual owner has been restored. No test assertion was weakened.

On the next inspection the self-round guard was also absent. The parent took
source ownership back and instructed the journal child to stop all source edits
and mutations and work only on its report. The parent's restoration patch then
failed because a concurrent restoration had already landed. The required
recovery read confirmed all three corrected conditions present: request-owner
selection for movement results, explicit Actor.Self counting, and a self-only
round guard. The failed patch changed nothing. A rebuilt combined run (not
`--no-build`) is now monitored as `mon_VX6B9NQXNAEVGPGY`, session `bash_26`,
using the same final artifacts directory. Acceptance awaits its result.

That repeat also failed the same two tests (447/449, exit 1, 25.4 s), despite
the subsequent source read showing all three corrected conditions. The test
project explicitly compiles CombatJournal.cs as a linked source, independently
of its aliased app reference. This raises an incremental-output inconsistency;
source inspection alone cannot establish the cause. A materially different run
uses a new artifacts directory `givre-parent-audit-clean-20261105`, disables
shared compilation and requests Rebuild, monitor `mon_4E8K473PWXB4JVNX`.
Its build completion must still be checked for actual test execution, not just
an exit-zero build. The journal child remains report-only. Its transcript
confirms it attempted multiple red variants and then found an unexplained
failed green; no mutation timeline has yet been established.

The direct dotnet-test/Rebuild invocation was rejected before execution with
MSB1008 (only one project can be specified). It ran no tests. The corrected
command explicitly runs `dotnet build ... -t:Rebuild` first, then `dotnet test
... --no-build` joined with `&&` against that same fresh artifacts directory.
This avoids reusing the suspect output while requiring a successful rebuild
before executing tests. Active monitor: `mon_J73AQYHYBQBMGWR2`, bash_28.

The clean build completed successfully with 256 warnings in 105.0 s; tests
from that newly built output are still pending. The journal report has now
been updated and explicitly acknowledges shared-source red mutations. Its
timeline uses filesystem timestamps, not exact mutation receipts; the overlap
with the parent build is therefore qualified rather than asserted as proven.
The report also retracts its unproven stale-build diagnosis and identifies
overlapping earlier test runs. Its supported delayed-owner case is explicitly
limited to results arriving before the next actor's turn starts.

The verifier's v4 report still records FAIL for the same two journal tests.
That historical result is retained. The verifier has been notified that stable
source hashes do not establish that an incremental binary reflects restored
source; final acceptance requires the clean test result and reconciliation of
the actual corrected source and updated reports.

## Clean combined result

The parent clean build and subsequent tests completed with exit 0. Build:
256 warnings, 105.0 s. Original combined DAG test filter: **449 passed,
zero failed, zero skipped, 40.6 s**. This run uses the new artifacts directory,
explicit Rebuild with shared compilation disabled, and tests that same fresh
output only after a successful build. It supersedes the failed acceptance
attempts without erasing their results or claiming their precise cause proven.

The journal correction and deterministic suspension decision are now verified
in the requested combined scope. The parent notified both original nodes and
the verifier of this independently observed result. Final report reconciliation
remains, along with the explicitly separate live/UI and cross-next-turn
movement-attribution limitations; this does not complete the larger Givre goal.

## Evidence limits

No game, account or application was launched in this audit interval. Scheduling
helper tests do not establish live reconnection, app-close cancellation or UI
appearance. No commit or push was made. All four audit tasks remain open until
the corrections and independent verification have landed.

## Reaudit following repeated completion notification

The parent reread the persisted original node prompts and the verifier's final
reconciliation. Original journal scope requires movement counts to belong only
to the requesting actor. Its documented cross-next-turn fallback violates that
requirement; calling it an open limit is not acceptance. Earlier independent
449/449 proves only the covered cases. No extra test run is claimed here.

Send to the original journal node returned node_not_continuable; retry returned
node_not_retryable. The existing definition was amended on the same run (now
generation 5), retaining the original prompts and reopening only journal and
its dependent verifier. No replacement DAG was created. The correction requires
actual producer-order/correlation evidence and behavioral cases in both actor
directions, without guessing ambiguous outcomes. Shared-source mutation for
red evidence is explicitly forbidden. Parent independent inspection and checks
will follow the correction; the prior clean results remain historical evidence.
Two follow-up audit tasks track this remaining work without undoing the four
completed initial audit tasks.

The parent inspected the cross-turn correction and all five added behavioral
cases directly. Pending ownership now retains from/to, actor and requesting
round across turn headers; matching outcomes consume that request and update
the original self round without prolonging its duration. Unmatched outcomes
in request-bearing logs remain unassigned. Historical logs without request
lines retain the old in-turn fallback.

Parent producer inspection confirms RunFightTurn takes _turnRunning by pid
before installing the controlled actor and releases it in finally. The local
move logs request before dispatch and outcome before returning. The verifier
was asked to check the complete serialization/cancellation boundary rather
than infer it solely from a local await; matching endpoints alone are not
unique request ids. Independent parent journal execution is now monitored as
mon_QRY5QDCQYJB2AYSY, with normal build in the parent-owned
givre-mobility-admission-20261105 directory. Result remains pending.

That independent parent journal run completed **31/31 passed**, zero failed or
skipped, exit 0 (2.2 s). The cross-turn regressions pass on compiled current
source. Final acceptance still awaits the verifier's serialization finding and
the combined delivery check, not another repetition of this filtered result.

The original eight-family combined filter is now running independently under
mon_VMYDBP0H9Q2MCST7 against current source, using parent artifacts
givre-mobility-admission-20261105 and a normal build. Expected additional
coverage versus the earlier 449 cases comprises the five journal regressions
and four parent mobility cases. Actual totals and exit status must come from
the execution result; none is inferred from adding those counts.

The parent combined run completed **458 passed, zero failed or skipped**, exit 0
(21.6 s). This is independently executed current-source evidence, including
the cross-turn journal and mobility additions. Serialization review and final
report/scope reconciliation remain separate acceptance obligations.

## Generation 6: producer-owned movement results

The verifier found uncovered defects despite the 458 passing cases: non-Turbo
and scripted movements did not emit a recognized result; a same-round result
after the finish request could extend EndSec; ownership inferred from headers
could be wrong if a stale producer was resumed after the next actor's header.
Normal pid serialization alone does not make those headers atomic with dispatch.
The generation-5 completion status is therefore not acceptance.

The parent reconstructed all four prompts again. Sending to the completed
journal node and retrying were refused; the same DAG was amended to generation
6, changing only journal and its dependent verification prompt. The journal
child owns its original parser, tests and report, and must preserve old saved
logs. The parent separately owns the necessary producer changes:

- Turn.cs emits an unconditional combat move_result after dispatch and its
  existing result wait, carrying the invocation's self/companion identity and
  captured round. The existing Turbo metric is retained with journal=canonical
  to prevent double counting. A changed fight-start timestamp suppresses the
  old invocation's result line.
- Packets.cs and Controlled.cs pass the self header's server round through the
  automatic wrapper rather than substituting a mutable current-round lookup.
  Paths without a server round retain their invocation's turn counter fallback.
- Script.cs captures its script round before awaiting and emits the same result
  independently of speed. The refusal baseline is now captured before dispatch
  so a synchronous response is not lost.
- CombatAndStatDispatchRegressionTests exercises actual ScriptCombatMoveAsync
  dispatch with a synchronous position confirmation and opposite-actor header,
  feeding real emitted transport lines into the recorder in Instant and Turbo.

These are parent-authorized integration fixes, not extra changes attributed to
the child. They do not change combat policy, turn serialization, energy rules,
or class mechanics. Canceled automatic waits still unwind without a completed
result line. Legacy streams lacking producer identity cannot prove arbitrary
overlapping same-endpoint request identity.

Parent integration run: mon_E8A4KY6Y44YNZX8F, artifacts
givre-parent-move-journal-20261105; result pending. Initial Turn.cs language
server freshness requests timed out; Script.cs, Controlled.cs, Packets.cs and
the new test file subsequently returned no error diagnostics. A build result
is still required; no live result is claimed.

The first parent integration run completed **9/9 passed**, zero failed or
skipped, exit 0 (42.8 s). Parent inspection then found a transport boundary
weakness: the new canonical lines relied on ambient PID scope while
CombatJournalService routes by the emitted PID. Both new producers now pass
pid explicitly to TransportLog.Line, and the integration observer filters by
that actual PID. This prevents a synthetic all-lines observer from hiding a
wrong-bot routing regression.

The strengthened current source is being checked by parent Release build
mon_BFJV240SKT7DV2SZ and parent combined tests mon_ATAW8EX3TJ8F1A3M. The test
filter includes every original required family plus dispatch, controlled-combat
and live-regression harnesses affected by the producer boundary. These are
offline harnesses despite the LiveCombatRegression class name. Results are
pending; intermediate project build lines are not final build success.

The strengthened parent Release build completed with exit 0 in 99.4 s:
`Generer a reussi avec 226 avertissement(s)` (original output is accented).
Warnings were not suppressed. The combined tests remain pending. Parent read
the actual canonical parser branch and all eight added case executions: explicit
ownership bypasses the legacy pending request, marked Turbo metrics classify as
Info, and a late self result uses `_actor != Actor.Self` to preserve EndSec even
before the next header. Both journal source/test files returned clean error
diagnostics. This inspection does not replace the pending test execution.

The strengthened parent combined run has now completed: **503 passed, 0 failed,
0 skipped**, exit 0; tests 91.6 s, total build/test 211.9 s, 255 warnings not
suppressed. This includes actual PID-filtered script movement integration in
Instant and Turbo, all eight original required families, and the additional
controlled-combat and live-regression offline harnesses. Attribution correction
is independently green; final DAG report reconciliation remains open.

Parent opened the R3 journal report and its actual isolated red log: 4 of 38
failed on the pre-R3 recorder, including EndSec expected 2 versus actual 9 and
missing canonical counts. The child real-project log/report says 39/39 green;
the independent parent 503-case run covers that filter on current source.
No live combat proof or blanket reset-concurrency guarantee follows from this.

## R4: original cast attribution obligation remains open

Parent read the final R3 verifier report directly. Its Release build and
466-case original filter were green, but it left cast ownership dependent on
header order. This is part of the original journal contract, not an optional
extension. The report also incorrectly stated that no producer integration
tests exist; parent opened CombatAndStatDispatchRegressionTests.cs and confirmed
the actual PID-filtered ScriptCombatMoveAsync cases for Instant and Turbo.

Send and retry on the completed verifier were refused. Generation 7 amends the
same DAG, reopening only journal-attribution and its dependent verifier.
The child owns only the parser, journal tests and original report. The parent
owns the producer correction in Turn.cs and the dispatch integration tests.

SendCast now captures explicit owner and round before Inject and appends them
to its existing cast line; it emits with explicit pid and suppresses that line
if the fight-start identity changed during dispatch. RunFightTurn passes its
invocation's controlled flag and captured round. Script movement now prefers
the observed server round over the internal cooldown counter. Tests distinguish
server round 7 from internal counter 3 and exercise both cast header-switch
directions using the real injection/log/recorder boundary.

All three parent-edited C# files returned no error diagnostics. The dispatch
integration run is pending as mon_2SPECKR294XA6SP4, bash_52. Parser red/green and
the new verifier are pending. The source changes are not yet accepted as green;
the earlier 503-case result belongs to R3, not this R4 source state. No live
deployment, accounts, commits or pushes were performed for this correction.

The parent dispatch integration run completed with exit 0: **11 passed, zero
failed or skipped**, 36.2 s. Parent then traced the script claim/resume producers:
they logged the internal cooldown counter and could overwrite the recorder's
server round. Those two lines now use the observed server round with the same
internal-counter fallback and explicit PID; cooldown state is unchanged.
Fresh Script.cs diagnostics returned no errors.

Parent opened the R4 parser branch and six added behavioral tests: both actor
header switches, late closed-round duration, current self cast, unknown explicit
owner, and legacy suffix-free lines. Explicit cast attribution bypasses header
order and late casts do not extend the closed round. The broadened current-state
parent check is now running as mon_TK97BTZ454MZERVY, bash_53, with fresh artifacts
givre-parent-journal-r4-final-20261105. Its result, final child report and final
verifier inspection remain pending; no final R4 acceptance is claimed.

The broader parent R4 run completed with exit 0: **511 passed, zero failed or
skipped**, 130.4 s (mon_TK97BTZ454MZERVY / bash_53). Parent also opened the
actual isolated R4 red log (4 failed, 40 passed) and real-project green-r4b.log
(45 passed). This corroborates the six new parser regressions and actual
producer integration, rather than accepting the child's summary alone.

The next verifier delivery still omitted its mandatory current-state commands.
Parent rejected completion, tried send then retry, and amended only verify in
the same DAG (generation 8). Its latest source reading exposed an unclaimed
script-turn header without a round. Parent corrected the delegation header in
Packets.cs to include the already observed server round with explicit PID;
fresh error diagnostics were clear. This one-line change happened after bash_53
started and is not claimed covered by its 511-case result. The verifier now owes
fresh original Release and eight-family commands on the stabilized source,
correction of its obsolete claim that no producer integration tests exist, and
final per-lane verdicts. The audit todo remains open until those results are
independently reconciled. No live proof is claimed.

## Final verifier delivery and parent checks

Parent read the complete final R5 verify.md, including all nine residual
findings. It now reports its required Release build and 472-case original
filter, plus the four producer cases. Its extra no-op Release rerun is disclosed;
zero warnings on that no-op do not replace the warnings from a fresh build.
The no-producer-tests assertion is explicitly retracted. The untested socket
wait lifecycle and old-log heuristic are still limits, not live acceptance.

Parent's own fresh Release build on the final delegated-header source completed
with exit 0: 226 warnings, 138.0 s, mon_CGVF1S6F1N6S7PFQ / bash_56. The broader
parent test command on this same source is running as mon_6XPWH5ZMKNHZ1Q19 /
bash_57. Acceptance still awaits that final test exit, not the child report.

Parent re-read BanGate.cs and the actual timed-wait branch in
AccountsViewModel.Socket.cs: the wait links cancellation to the session token,
clears only its own wake slot in finally, handles explicit cancel before the
decision, and rechecks AutoRelaunch through CompleteResumeWait. This is source
inspection with deterministic decision tests, not an executed socket lifecycle.
The redundant SetBanResume branches and report history were left as-is; neither
changes the requested behavior. Attribution of unrelated shared-tree changes
cannot be inferred from a diff against HEAD, so those changes were preserved.

The final parent test command completed with exit 0: **511 passed, zero failed
or skipped**, 127.7 s (mon_6XPWH5ZMKNHZ1Q19 / bash_57). Together with the fresh
Release build above, this covers the final source including the delegated-round
header. Parent acceptance of this bounded offline DAG audit is complete.

This accepts the requested combat fallback, journal accounting and timed-ban
decision corrections, not the broader goal. Diagnostic emission, some producer
paths and the live socket wait lifecycle retain the documented coverage gaps.
No live combat, real temporary-ban reconnection, complete class mechanics,
Turbo speed parity or visual QA is established by these commands. The broader
tasks remain open; no commits or pushes were made.
