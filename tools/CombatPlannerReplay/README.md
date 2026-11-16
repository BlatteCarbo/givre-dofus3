# Combat planner diagnostic replay

Run from the repository root:

```powershell
dotnet run --project tools/CombatPlannerReplay -c Release --artifacts-path "$env:TEMP/givre-planner-replay" -- "path/to/transport.log" 1073741827 99
```

Arguments are transport log, player pid and turn number. The tool rebuilds
`combat-player-empty` fragments, rejects gaps or duplicate offsets and calls
the current production planner with the live 60000-work budget. Output is the
selected actions, score, work and budget-exhaustion flag. An optional fourth
argument overrides the work budget, from 1 through the production planner's
maximum, for bounded diagnostic comparisons. It does not change the live
executor's budget. The log may remain
open in the application. Run this against one captured occurrence per pid/turn;
duplicate turn captures are rejected rather than merged.

Diagnostics on stderr identify the deserialized policy, fighters and spell
admission. An optional fifth argument requests a separate walk-only plan
constrained to that walkable end cell. It leaves the main replay result
unchanged and prints its own score, including incoming damage. This is a
counterfactual score comparison, not proof that the live bot should walk there.

Only solo diagnostics with empty team intents are supported. Missing
SpellLookup and state-damage catalogs are not reconstructed. A successful run
proves a decision on the logged fields, not a legal server cast, live damage,
complete class mechanics or deployment into a running application.
