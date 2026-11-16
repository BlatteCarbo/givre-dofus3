# Native breeding seam

This is executable domain planning plus a bounded action runner, not complete
breeding automation. Mount Lua is unavailable; no numeric action identifiers,
packet fields, gauge observations or success receipts have been invented.
There are no background timers, another capture stack, Expo or Electron.

## Parent wiring

Construct `BreedingViewModel(new BreedingSession(execute, clock))`.
`execute` is a `PaddockActionExecutor` that must bind both ActivateGauges and
DeactivateGauges to existing, verified session actions, await their correlated
receipts with a bounded timeout, and return evidence text. A successful write
alone is not confirmation. Admission must recheck account/session generation,
paddock identity, active lot, selected gauges and ownership immediately before
each real action. The cancellation contract is on the delegate: settle any
in-flight activation and return a positive receipt if it actually activated,
even after cancellation, so the runner can send a separate deactivation.
Failure/cancellation exceptions must leave no active gauges. Stop failures
remain busy and retryable; they never claim the game stopped.

Bind `NextStageCommand`, `StartCommand`, `StopCommand`, `Status`, `Progress`,
`Remaining`, `Readings` and `PredictedReadings`. Default construction enables
planning but disables Start. Call `Load(lot, target)` for telemetry or explicit
manual corrections; every field carries Observed, Manual, Predicted or Unknown.
Call `Refresh(now)` from existing host events or its UI clock only while busy.
Run all viewmodel/session calls on the owner UI context. Do not create a new
global polling loop. The viewmodel owns no static subscription or timer.

Remaining real protocol requirements are lot count/family and identity,
highest serenity for non-unified lots, verified unification, current three
characteristics, gained XP since the chosen starting level, all selected gauge
fuel readings, and action receipts for gauge activation/deactivation. No
protocol adapter is supplied until these mappings are captured and validated.
After the estimated end the runner waits for observation; it does not auto-
complete or chain another stage. Stop sends the verified deactivation action.
Load fresh observations/manual corrections before starting the next stage;
forecasts never replace confirmed readings. A level-based UI must provide a
verified XP table conversion; this API accepts explicit required gained XP.

## Reference assumptions

Pinned Monture Minute commit:
`1def87be5cbb46a8ff4fe7c399d7dd7ef464ebd5`.
MIT notice is retained in Engine/Breeding/THIRD-PARTY-NOTICES.md.
It models one homogeneous batch, not per-mount genetics/fertility, and uses
the same rules for dragoturkeys, muldos and volkornes.

* Serenity -5000..5000; non-unified batches move to -5000 using their highest
  serenity. A batch at -5000 is inherently unified.
* Characteristics cap at 20000. Endurance needs serenity below zero,
  maturity includes both -2000 and +2000, love needs serenity above zero.
  Move targets sit 500 points inside the selected zone.
* At most two gauges; prioritize two available unfinished characteristics,
  otherwise one characteristic plus XP, movement plus XP, or XP alone.
* Ten-second cycles; fuel 0..200000. Per-cycle 10/20/30/40 points, strictly
  above 80000/140000/180000 for the higher tiers. Consumption stops when a
  gauge has supplied its need. As in the reference, stage cycles use the
  longest primary gauge; exhausted/shorter gauges stop independently.
* Prudent duration is rounded cycle time multiplied by 1.1; prediction uses
  actual cycles, not the conservative margin. It is an estimate, not proof.
* Unlike the reference's UI defaults, missing fuel is never filled in from a
  habit and future fuel refills are never assumed. Explicit empty fuel stays
  zero; missing, stale-as-Unknown or predicted readings block execution.

Tests are in GivreClone.Tests/BreedingTests.cs and use the existing F06App
project alias. Parent owns all central builds/tests. No existing project file,
view, protocol mapping or shared service is changed by this unit.
