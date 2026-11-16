# Combat report: no enemy classified — 2026-11-06

The captured fight report records a defeat after 53 turns, with 34 turns containing no spell casts and the character reaching 0 HP. In many turns the diagnostics reported one tracked fighter but zero classified enemies; the remaining fighter was explicitly logged as not allied, not confirmed enemy, and with team ID 0. The logger suggested a team-reading issue involving `FightTeamUpdateEvent` / `GameFightShowFighter`.

Treat this as strong incident evidence for investigating incomplete/ambiguous fighter-team attribution and no-target turn passes, not as a proven root cause or validated fix. The report's cast counts are injected requests, not proof of damage; its movement counts are attempted requests, not confirmed movement. Reproduce the roster/team-state transition and confirm an enemy is recognized before attributing the entire defeat to AI damage or survival strategy.

## Follow-up after wave transition

A later live trace on 2026-11-06 showed the same class of diagnostic after a wave transition: one tracked fighter, `allié=False`, `confEnn=False`, team ID 0, and zero enemies classified. The trace showed `FightNewWaveEvent` and `FightSynchronizeEvent`; combat proceeded with no recognized enemies and Shadowbot was defeated, followed by a second temporary suspension. The assistant stopped Shadowbot's script and disconnected the account pending a fix, to avoid further suspensions. Fighter/team packet handling was being investigated, but this transcript does not establish the root cause, build success, deployment, or post-fix validation. Keep the account stopped until a verified fix is deployed; do not infer that seeing wave/synchronize packets alone establishes correct roster state.
