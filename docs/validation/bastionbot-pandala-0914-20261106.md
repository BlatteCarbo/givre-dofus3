# Bastionbot Pandala defeat at09:14

Read the complete278-line091423_pid1073741827_defaite journal(original filename
uses the accented French outcome). Live PID7132 still runs the older verified
build;new recursive-refund and replay-retention changes are not deployed.

Outcome0 after6.223s,three own turns,eight injected casts,two canonical walks.
Pétartifoux/Rouquette/Crachefoux group,four enemy fighters. HP2875/3045 then
1900/2707 then790/1995;self death authoritative at+5.938s. No enemy removal.
Numerous enemy marks and forced movements are recorded. This is not a pass-only
failure and the journal does not prove one specific tactical action caused death.
Expedition continuation after defeat is observed,not assumed to be a bug.

Concrete synchronization finding:12733 injected at+3.811s;message1376 at+3.833s
was treated as cast_refused. The following decoded text at+3.837s explicitly
says challenge485 failed,and the swap actually executes at+3.840s. Thus this
challenge announcement prematurely ends the cast wait. Existing188/597
challenge exclusions do not cover1376;production source contains no1376 entry.
Follow-up needs a regression through the actual message handler before changing
classification. Do not blacklist12733 or attribute defeat solely to the swap.

Extended existing Challenge_failure_does_not_reject_the_pending_cast theory
with1376 and actual parameter485. bash_271 failed only1376;188/597 passed,
125ms,exit1. Production guard now also excludes1376. bash_272 passed27
live-combat/Turbo policy tests,0 failures,in54s,exit0. Full validation is
completed as bash_273/mon_SAMKCRY56FHV81MH,exit0:4811 passed,0 failed,
2 existing skips,total4813,in3m44s. Receipt:challenge1376-full.trx.

User subsequently confirmed the intended expedition is Pandala,not Frigost.
BuildExpeditionProfile expands selected maps with TraversedMaps before passing
them to Build. LoopStartIndex prevents repeating the initial approach but
does not prevent combat there. Build fights on every route index. This explains
why starting map54169888 was eligible despite being outside the selected zone.
Separate root fix must preserve selected-map membership,including intermediate
transit between hunting maps. Work delegated;not yet accepted or deployed.
