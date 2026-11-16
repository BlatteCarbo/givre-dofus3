# Absolute combat life reconciliation

The repeated-characteristic correction does not synchronize dedicated Life
and MaxLife. A separate decoded-packet regression now starts an enemy at
tracked0/3060 with old stored components. A complete incoming refresh sends
delta97=-2500, vitality11=-400 and base0=3400, in that order. The required
dedicated result is500/3000. The partial control sends only delta97 and must
retain the dedicated0/3060 rather than combining it with stale components.
Both cases require the new characteristic value to be stored and PA/PM to
remain unchanged. The fixture is reconstructed, not a captured live packet.

Pre-fix bash_196 exited1: the complete case failed, while the partial control
passed. The dispatch now buffers the packet's decoded characteristics and
reconciles dedicated life only when base0, vitality11 and delta97 all occur
for the same actor in that packet. Values are bounded to0..MaxLife, and the
existing upsert notification is reused. Missing components do not borrow
old dictionary values. Existing point and range updates remain intact.

Focused validation bash_197 completed with exit0:53 passed, zero failed or
skipped,16.9 seconds, covering dispatch, life policy and protocol cases.
Both complete and partial refresh cases pass. The build reported256 warnings,
not suppressed. No guessed missing delta or death inference was added,
and this does not establish the source of Sylvanbot's early-zero drift. The
changed application has not been deployed live.

Full-suite bash_198 completed with exit1:4803 total,4800 passed, one failed,
two existing skips,223.7 seconds. The sole failure is a five-second timeout
in HdvLiveUiTests.Price_progress_advances_only_after_each_row_and_counts_missing_quotes.
Parent read the test: it subscribes explicit completion sources before starting
the operation, with no fixed sleep. No HDV source or test timeout was changed.
The combat reader and reconciliation regressions passed in that run; the
full suite is not reported as green. Isolated HDV follow-up bash_199 completed
with exit0:21 passed, zero failed or skipped,2.4 seconds. This does not fix
or waive the timeout seen under the full workload. The earlier full-run map
performance failure did not recur, but no performance fix is claimed either.
