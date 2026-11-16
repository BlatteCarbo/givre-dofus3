# Class mechanics: observed versus simulated

Read-only source audit on 2026-11-04. This is not live gameplay validation.

## Sram and Roublard

`GivreClone.Protocol/FightMarks.cs` keeps server-confirmed mark identity,
author, type, cells and activation. Placement replaces the same identity;
unmark removes it. Activation of an unknown identity does not invent a mark.
A trigger alone does not imply removal.

`GivreClone.App/Services/Combat/SpellResolver.cs` records placement actions
400, 401, 402, 1091, 1165 and 2022 as simulated marks. Effects with a delay or
non-immediate trigger are recorded as deferred effects rather than executed.
Summon actions 181, 1011 and 1008 create occupancy with placeholder life of one;
the source explicitly does not claim summon combat statistics without grade data.
These records are insufficient to predict a bomb explosion or a trap network.

The live snapshot construction in `LaunchService.Combat.Turn.cs` supplies
fighters, spells, geometry, challenges and team information. It does not pass
the server mark registry into the planner's initial board. Its
`ForcedMovementSafe` argument remains at the default false. The planner rejects
non-teleport resolved displacement when that prerequisite is false.

Consequently, the following remain unsupported by the inspected live path:

- Triggering and chaining a Sram trap network from observed server marks.
- Valuing invisibility as a class-specific future-turn tactic. Observed fighter
  visibility and position certainty gate targeting, but do not simulate that tactic.
- Bomb grade statistics, combo growth, walls, detonation ordering and chained
  explosion damage for Roublard.
- Certifying live swap/pull/push chains through traps, portals or other movement
  triggers. Synthetic planner cases with explicit safety are a narrower contract.

Connecting mark cells alone would not close these gaps: the observed registry
does not contain the source spell/grade and executable trigger semantics needed
to assign damage, expiration, ownership interactions and removal behavior.
Do not infer those values from the mark type or icon.

## Existing verification

Current-worktree comparison with the interrupted combat-planner-slice1 file:
read both complete CombatTurnPlanner.cs files (current647 lines,sibling443).
The sibling retains the earlier inline damage/heal/displacement simulation;
current code uses SpellResolver and additionally carries visibility, partial
states, team intents and explicit forced-movement admission. Public plan now
includes intent/unmodeled results and search accepts an explicit work budget.
Do not copy the old sibling over current source: that would remove these guards.
Current CastOn retains the resolved board, including visibility, between
simulated actions. Live walk continuation checks observed visibility and
position certainty against its expected snapshot before reuse. This read is
static evidence only. Current movement/planner/resolver-continuation suites
ran as bash1292,planner-slice-current-20261104.trx:exit0,188 passed,
0 failed,0 skipped,12.6s. This verifies the current synthetic contracts,
not live kite behavior or executable trap/bomb networks.

Immediate invisibility transition under validation: the client export
`rapports-v2/N01/EFFECTS_3.7.13.17.csv:97-102` identifies spell12913 action150,
mask a,A, duration1, delay0, trigger I, separately from ally MP action128.
Baseline bash1273 failed2 of3 cases: unsupported action150 and an illegal
second targeted cast after the synthetic hiding setup; the no-hide control
passed. SpellFighter now carries visibility, action150 changes it to Invisible
and emits VisibilityResolved, and planner target gates consult the current
node board as well as initial confirmed-position eligibility. Next-turn setup
and follow-up ranking use the same visibility restriction. Incoming threat
is unchanged. Related verification bash1274 exited0:288 passed,0 failed,
0 skipped,43.3s. Follow-up self-targeting regression bash1275 reproduced the
fault: invisible caster produced no healing action; visible control passed
(1 failed,1 passed). Eligible now exempts only the acting fighter from the
visibility/position-certainty gate; spell range, masks, allowed targets, AP,
HP and per-target limits still apply. The enemy-hidden regression remains
the negative control. Verification bash1276 exited0:290 passed,0 failed,
0 skipped,45.4s. These are compiled offline tests, not game observations.

This immediate transition does not implement expiry, reveal-on-action,
enemy blind casts, defensive invisibility valuation or trap triggers. The
planner regression combines damage/state setup with the real visibility row
only to exercise a productive two-action branch; live Invisibilite is not
claimed to deal that synthetic damage. Not deployed or live-validated.

The current compiled planner/resolver/walk suite passed 170 cases (bash1265).
After removing temporary walk-continuation diagnostic serialization, the
planner/resolver/auto-rotation/walk suite passed 247 cases, zero failures or
skips (bash1269, `combat-no-debug-green.trx`). These receipts cover their
synthetic scenarios, not the missing class mechanics listed above.

Invocation baseline on the same compiled code: bash1272 exited0 with21 passed,
zero failures or skips in3.0s. The parent checked
`GivreClone.Tests/TestResults/summon-class-baseline.trx` counters. The suites
cover shared living capacity, confirmed death removal, current caster ownership
and packet ownership decoding. This does not establish whether each Roublard
bomb uses that generic limit, nor supply bomb grade statistics or explosion
semantics. Those class-specific rules require separate evidence before reuse.
