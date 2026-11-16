# Planner decisions retained in combat replay

Investigation of Bastionbot's reverse swap could not identify the execution
branch from the combat journal: both CombatJournalService.OnLine and
CombatJournalRecorder.Feed rejected combat-planner messages. Existing transport
test was extended with a planner execute message and exact unchanged zero
cast/move counts. No new duplicate test or production-only test seam added.

bash_252 exited0 with no test output or TRX in the newly selected artifacts
directory; it is not test evidence. Re-run with restoration bash_253 failed
one test on Assert.Contains:the planner decision was missing,337ms,exit1.

Both production filters now accept combat-planner messages. Recorder retains
the distinct prefix and classifies it Info before action classification.
This preserves planned decision payloads without claiming execution.
bash_254 completed exit0:45 journal/transport tests passed,158ms.
Full suite bash_255/mon_70ERPH44SX3F1MY4 completed exit0:
4809 passed,0 failed,2 existing skips,total4811,in3m41s.
Receipt:GivreClone.Tests/TestResults/planner-replay-full.trx.
Separate artifacts root givre-planner-replay-20261106,
not the executable currently running as PID7132. No live acceptance yet.
