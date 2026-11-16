# Combat AI pass — 2026-10-18

This source-based pass improves combat placement and enemy-targeted area safety. It does not establish recovered Dofus combat semantics, best-in-class AI, or live performance. The Ghidra investigation is independent and has not supplied a validated combat method mapping for these changes.

## Changes

- Casting-position search now uses one orthogonal reachable-cell exploration per request, bounded by movement points and the supplied sector/occupation predicates. Among legal firing cells it keeps the minimum real step count and the previous stable cell-ID tie order. Range, alignment and line-of-sight checks remain the existing engine rules. The deterministic test bounds walk-predicate calls to 2,240 on a 560-cell grid; this is an algorithmic work bound, not a latency benchmark.
- Both offensive fallback loops now reject spells configured for allies or self. They previously skipped only self-targeted spells, so an ally-targeted support spell could enter enemy selection.
- Known-area enemy spells reject zones intersecting allies or the caster, including normal casts, recasts after approach and the emergency attack loop. Minimum AoE hits applies to the actual selected area instead of the maximum over unrelated candidates. Existing challenge checks remain required.
- If the current casting position is unsafe, the main rotation can search a reachable, geometrically valid and zone-safe firing cell. For hypothetical placement, the caster's old cell is excluded from ally occupation and its candidate cell is included. Unsafe targets remain available as repositioning candidates, but cannot bypass the cast-time check.

## Verification

57 focused tests passed, zero failures or skips. They cover stable minimal-PM placement, disconnected sectors, one-pass work bounds, invalid/no-movement inputs, fallback role separation, ally damage avoidance, per-area hit thresholds, and a real CombatZones/CombatPositionPlanner scenario leaving the caster's damage area in one step. Existing ScriptCombat, CombatImportLogic, CombatForcedMovesReplay, NavigationSafety and ZoneRouting tests were included.

Local log: artifacts/combat-decision-green.log. No application/game launch, account action or live combat occurred. The application build result is recorded below when available.

## Limits

The new friendly-area policy applies to enemy-targeted AI spells with known nonzero-size zone data. Self-targeted or ally-targeted rotations, unknown/unsupported effect masks, mixed beneficial/damaging effects and inaccurate zone data need further work. Unknown or missing zone data retains previous behavior; this is not a universal friendly-fire guarantee. Server-confirmed AP/MP costs, resistances, tackle, enemy future turns and full-turn lookahead are not newly modeled. Existing movement/turn/challenge restrictions still apply. Offline tests do not prove protocol semantics or in-game outcomes.

Release integration build succeeded: 0 errors, 175 warnings. Command: dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore --artifacts-path artifacts/McpValidation -m:1 --disable-build-servers -p:UseSharedCompilation=false -v quiet. Log: artifacts/combat-ai-app-build.log. Output: artifacts/McpValidation/bin/GivreClone.App/release/GivreClone.exe. NativeAOT publish and in-game validation were not run.
