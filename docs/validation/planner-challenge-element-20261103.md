# Planner elemental challenge

The three-case planner reproduction ran under bash_829. Without the search
transition, both constrained cases selected 70 damage: expected 40 for a new
single-element chain and 30 for an existing Fire lock. The unconstrained
control permits both attacks. The run exited 1 with those two discrepancies.

CombatTurnSnapshot now carries the single-element challenge and existing
combat lock. PlannerSpell carries the element returned by the same
SpellElement method used by the live challenge guard. Each search node
inherits its parent's lock or establishes one on its first elemental cast.
An incompatible elemental cast cannot expand that branch. Non-damaging
spells with no element do not establish or violate the lock.

LaunchService copies the fight lock under the existing challenge-state lock
and supplies the active challenge flag. The existing execution-time guard
remains responsible for rechecking the actual combat state.

Validation under bash_830 exited 0: planner, challenge catalog and challenge
finishing policy tests passed. planner-challenge-element.trx records 100
executed, 100 passed, zero failed or not executed, including the three-case
reproduction. This is offline validation, not live combat verification.
Compilation emitted existing CS8826 partial-method signature warnings in
HdvStatsViewModel and HdvViewModel.Bulk; they were not suppressed. The language
server still crashes enumerating artifacts/kourial-tools/pypdf due to denied
access; compilation is the available diagnostic.

This change deliberately retains the live guard's existing single-element
classification. It does not establish correct challenge semantics for
multi-element spells, stat-dependent damage elements, indirect damage or
triggered spells, nor add branch-local Focus target transitions. Those need
separate evidence before the overall challenge-planning task can close.

## Dynamic element follow-up

bash_831 reproduced eight failures for actions 2822, 2828, 2832 and 2890:
the raw Neutral summary allowed incompatible casts and rejected compatible
Fire/Water casts. Each action has a compatible and incompatible lock case.
The test uses distinct Strength=50, Intelligence=100, Chance=0, Agility=75.

The planner now calculates these actions' elements from the current branch's
caster characteristics, not the initial summary. The live SpellElement guard
also reads the dynamic descriptor and current controlled-caster stats.
bash_832 exited 0. planner-dynamic-challenge.trx records 170 executed and
passed, zero failures or unexecuted tests, across the planner, challenge and
automatic rotation suites. This follow-up does not yet validate a buff-induced
element change or multi-element/indirect semantics.

The subsequent bash_833 run exited 0. planner-challenge-boost.trx records
two executed and passed, zero failures or unexecuted cases. With 100
Intelligence and a 200 Strength self boost, a dynamic best-element attack
can follow that boost under an Earth lock (30 damage, boost then attack).
Under a Fire lock the planner selects the unboosted Fire attack instead
(20 damage). This exercises branch-local characteristics rather than a
classification frozen at snapshot creation. It does not validate live
server execution or multi-element/indirect semantics.
