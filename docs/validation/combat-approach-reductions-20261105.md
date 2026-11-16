# Approach scoring and observed damage reductions

The future-attack positioning score used raw effect dice even when an observed
target reduction absorbed the complete hit. In a seven-cell corridor with three
MP and a range-one attack, the planner approached that target without a useful
future attack. The initial two-case regression passed the no-reduction control
and failed the fully reduced case (`Assert.Empty`, movement actions returned).

`CombatTurnPlanner.NextTurnPotential` now estimates each permitted visible
living target separately using the simulated caster characteristics and target
resistances/reductions. Action-defined elements and proportional-life damage
follow the existing SpellResolver calculation. The future score remains a
discounted heuristic, not a promise of a legal next-turn cast.

The first related run passed 131/132 tests. The remaining existing regression
showed a stat boost receiving speculative credit for a dynamic-element attack
excluded by the current single-element challenge. Future scoring now checks
that challenge lock against the simulated characteristics as well.

Final focused run: CombatTurnPlannerTests and DamageEstimatorTests, 136 passed,
zero failed or skipped, exit 0, 285 ms. The reduction regression covers neutral,
earth, fire, water and air, plus an unreduced movement control. This is isolated
planner evidence; no official-client combat or deployment was performed.
Three additional table cases cover current-life damage with/without a complete
reduction and missing-life damage at full health (no useful approach).
The expanded planner/estimator run passed 139/139, zero skipped, exit 0 (367 ms).

A subsequent regression reproduced approach for an attack configured with
TargetHpMax=50 against a full-health target: one failed test, exit 1 (81 ms).
Future-attack scoring now applies the existing HpGate contract to both caster
and target using the simulated node life values. The related suite passed
140/140, zero failed or skipped, exit 0 (286 ms).
The subsequent complete managed suite passed 4545 tests, zero failed,
two existing skipped legacy cases, total 4547, exit 0 (2 minutes 11 seconds).

Separately, the interrupted combat-planner-slice1 worktree passed its existing
planner/rotation/descriptor/damage suite: 56 passed, zero skipped, exit 0.
That worktree was not overwritten by the newer principal implementation.

Not covered by this correction: immunity states, live observed damage learning,
next-turn enemy decisions, or complete class mechanics. Broader combat
integration verification passed: 724 tests selected by Combat, Spell,
DamageEstimator and Challenge names, zero failed or skipped, exit 0
(1 minute 49 seconds). This does not extend the evidence to live combat.
