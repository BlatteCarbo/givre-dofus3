# Learned mobility admission

AutoSpellRotation.Build already adds admitted learned teleport descriptors, but
ShouldUsePlanner only recognized damage and selected support/control effects.
Consequently a learned teleport with no modeled attack never reached the
planner branch in LaunchService.Combat.Turn.cs, even though the resolver and
planner can simulate its movement and compare the enemy reply.

The new admission regression observed one failure and one pass before the fix:
without an unknown attack the gate returned false instead of true; with an
unmodeled conditional attack it correctly preserved the legacy fallback.
Original monitor mon_JRAWZPAC5V2MC61D, exit 1, 2 total, 1 failed, 1 passed,
zero skipped (28.2 s). No production source was mutated to generate this red.

The correction adds Teleport to the existing immediate-utility gate. It retains
IsAdmitted, IsImmediateModeled and the unmodeled-attack fallback. No new
movement scoring or synthetic threat discount is introduced. Two additional
integration cases build the learned rotation, pass the real selection gate and
ask the planner for a turn: teleport out of a stationary melee threat with no
walking MP, or retain the cooldown when no incoming damage can be avoided.
They assert the selected action, safe distance and zero modeled incoming damage,
not merely the gate's return value.

Fresh error diagnostics for both changed C# files returned no diagnostics.
Related AutoSpellRotation, CombatTurnPlanner and CombatPlannerResolver suites
are running as mon_J144K7T14S3SW8MZ, isolated artifacts
givre-mobility-admission-20261105. No green or live behavior is claimed yet.
This is generic immediate teleport admission, not completion of class-specific
mobility, Sram networks, Roublard combos or the live combat validation goal.

The related suites completed: **263 passed, zero failed or skipped**, exit 0
(23.3 s). Both actual retreat/no-benefit scenarios passed, as did the admission
case preserving fallback for an unmodeled attack. No live execution was added.
