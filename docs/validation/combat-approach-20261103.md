# Approach scoring and permitted targets - 2026-11-03

The primary planner reproduced the same excluded-target regression as the
partial combat-planner-slice1 worktree. An aggressive fighter with three MP
and a range-one attack walked toward an enemy not in that spell's
AllowedTargets. The regression failed with moves to cells 22, 37 and 51,
instead of an empty plan.

Next-turn potential now evaluates each admitted attack separately, considering
only its permitted living targetable enemies. The spell must permit approach
and have a positive CastsLeft budget. Its damage and reach remain paired;
the score no longer borrows maximum damage from one spell and maximum range
from another. The existing teleport exclusion and flat post-cast potential
remain, preserving the hit-and-run objective.

Validation command:

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CombatTurnPlannerTests|FullyQualifiedName~AutoSpellRotationTests|FullyQualifiedName~SpellDescriptorTests|FullyQualifiedName~DamageEstimatorTests|FullyQualifiedName~SpellResolverTests" --nologo -v minimal
```

Result: exit 0, 115 passed, zero failed or ignored, duration 7.5 seconds.
The matching partial-worktree run passed 56 tests; its own evidence is in
docs/COMBAT_APPROACH_VALIDATION_20261103.md in that worktree.

This is a discounted positioning heuristic, not a promised next-turn hit.
It does not simulate the intervening enemy turn, future cooldowns, or future
line of sight. Current casts still pass through the existing legality and
effect resolver. No class-specific completeness or live result follows from
these tests. Client-dependent work remains paused while the game update is
unavailable, as requested by the user.

## Learned utility combo

The generated-rotation suite now drives a learned action-111 AP boost through
the planner and real effect resolver before a learned attack. With two initial
AP, the one-AP boost grants two AP and permits the three-AP attack: the plan
must contain spell IDs 70 then 71 and deal 10 damage. A disabled manual row
for the boost must instead produce no action and zero damage. No mock supplies
the AP gain or planner result.

The related AutoSpellRotationTests, CombatTurnPlannerTests and
SpellResolverTests completed with exit 0: 112 passed, zero failed or ignored,
duration 2.4 seconds. This adds behavioral coverage for an already implemented
immediate AP utility; it does not establish class-specific boost durations,
cooldown observation, live acceptance, or all learned utility spells.

The first elemental-boost fixture paired effectElement=2 (fire) with action
100 (neutral damage). The resolver correctly treated that action as neutral,
so Strength increased the damage and the fire-control expectation failed.
The new fixture now uses action 97 for earth and 99 for fire. No production
change was made in response to that inconsistent test data; the corrected
fixture's related suite completed with exit 0: 114 passed, zero failed or
ignored, duration 3.7 seconds. It verifies Strength then earth damage (20),
versus fire damage alone (10), using the same initial four-AP budget.

## Executed AP/MP gains

The automatic turn loop deducted a cast's AP but never credited a learned
immediate self gain (actions 111/120 AP, 128 MP). Its next decision takes
min(local, observed), so the stale local value could hide a server-granted
AP and end the turn before an affordable follow-up attack.

After an acknowledged cast, the loop now adds the gain computed by
CombatTurnPlanner.ExpectedSelfPointGain. Only effects on the caster count
(mask C, or a/self mask when the caster is the explicit target); an enemy
mask yields nothing. The observed-minimum check is unchanged, so a refused or
reduced gain cannot invent points. Five InlineData cases cover these rules.

AutoSpellRotationTests, CombatTurnPlannerTests, SpellResolverTests and
ControlledCombatTests completed with exit 0: 132 passed, zero failed or
ignored, duration 6.6 seconds; the build reported 255 warnings, none
suppressed. This tests the gain calculation, not the full live turn loop,
which remains unvalidated until the client update is available.
