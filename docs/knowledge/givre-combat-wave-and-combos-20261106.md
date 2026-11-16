# Combat wave team sync fix, Sacrieur Transposition+Attirance chain, Sram Invisibilite/Peur action 783, and true kiting reply-map fix (2026-11-06)

---
description: Combat wave team sync fix, Sacrieur Transposition+Attirance chain, Sram Invisibilite/Peur action 783, and true kiting reply-map fix (2026-11-06)
---
# Combat fixes & class mechanics landed 2026-11-06 (unit-verified, pending live deploy)

Worktree: `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024` (uncommitted per user rule).

1. **Wave transition `0 ennemi classé` fix (`LaunchService.Packets.cs:1320-1326`)**:
   - Root cause: `FightSynchronizeEvent` skipped `ReadFighterTeam` behind `known` (already in `_fighters`), leaving stale `team = 0` after wave sync.
   - Fix: refresh `_fighterTeam` from `SpawnInformation` before `known` / cell guards.
   - Verified by `WaveTeamSynchronizationTests` (7/7 passed). Live combat post-deploy still required before calling live-validated.

2. **Sacrieur `Transposition (12736, action 8)` -> `Attirance (12735, action 6)` -> melee (`CombatTurnPlanner.cs`)**:
   - Admits `HS!number` / `HS=number` (`&` / `|`) state criteria (`Transposition` has `HS!7`), allows `Attirance` pull to stop cleanly at melee distance (cell distance 1 from caster) while keeping wall/fighter obstruction rejection, and recursively scores follow-ups up to depth 3 within `MaxWork`.
   - Verified by `SacrieurPlacementChainTests` (15/15) and `CombatTurnPlannerTests` (192/192).

3. **Learned utility admission, displaced-enemy reply maps (true kiting), mark avoidance, and Sram `Invisibilité (12913)` + `Peur (12908, action 783)`**:
   - `AutoSpellRotation.ShouldUsePlanner` admits modeled `Push`, `Pull`, `Swap`, and action `150` invisibility; `Build` marks `150` as support (`SelfBuff=true`).
   - `CombatTurnPlanner` caches enemy reply maps by `(enemy, actualCell)` after displacement so push-to-escape earns its real exposure reduction, and excludes known/simulated mark cells when walking.
   - `SpellDescriptor` + `SpellResolver` + `CombatTurnPlanner` deterministically model action `783` (`Peur 12908`, `SpellLevelData 40901`, `NeedFreeCell + CastInLine`, range 2..6: pushes the fighter adjacent to caster along cardinal line to the targeted empty cell, rejecting obstructed paths).
   - Verified by `ClassMechanicsAndKitingPlannerTests` + planner suites (296/296 passed).
   - Still unsupported (reported honestly): push collision damage, trap trigger/chain execution (action 400 payload), Roublard bomb action 406 / bomb walls / combo chains (`Explobombe 13444`, `Tornabombe 13435`), portals, carry/throw chains, >3-cast chains.
