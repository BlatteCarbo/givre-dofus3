# Combat admission and movement evidence

- On 2026-11-02, AutoSpellRotation's RequireDescriptors path was aligned with CombatTurnPlanner.IsAdmitted instead of strict descriptor.CanPlan; classic calls retain the strict policy. Regression baseline: eight failures; combined rotation/planner/resolver selection: 88 passed. This enables candidates, not complete class support.
- Lua/manual shared ScriptCombatMoveAsync and CombatReachable now use a terrain-composed per-step NeverApproach predicate. Destination, refreshed geometry/PM, reachability accounting and packet path share it. Reverse destination distance field tests forward edges in reverse. Final targeted selection: 70 passed.
- Full earlier suite: 4045 passed, 1 performance failure (MapInspection 12029 ms against 12000 ms), 2 existing skips; unchanged test passed alone in about 1 second. Do not erase failed suite evidence.
- Authoritative recovery details and open live gates: %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/docs/codex/RECOVERY_COMBAT_20261102.md. Tactical map lifecycle and live fights remain unverified; unavailable-user question does not authorize input on Bastionbot.
- No current trap activation, Roublard bomb chains/walls or full class-state/template semantics established by generic resolver support.
