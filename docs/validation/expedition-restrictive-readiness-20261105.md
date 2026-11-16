# Restrictive expedition: arrival and combat readiness boundary

## Result

No production change. This bounded investigation did not reproduce a premature
skip with a valid, eligible, reachable group. A recorded arrival followed by
departure without an attack exists, but neither the group's eligibility nor the
script's active filters can be recovered from that log excerpt. Treating it as a
readiness defect would be speculative.

The preceding boundary is
`docs/validation/expedition-route-failure-boundary-20261105.md`. Its executed
46-test result already establishes circuit revisits after empty maps and
temporary travel failures; route retry and zaap policy were not changed here.

## Actual call and state path

- `GivreClone.App/Services/ExpeditionScript.cs:379-410`: after successful
  `travelTo`, the generated multi-map loop repeatedly calls `fight()` until it
  returns false or a configured limit is reached. False ends this visit, not all
  future visits. A one-map script instead yields after an empty poll.
- `GivreLuaHost.Bindings.cs:98`: global `fight` invokes `FightOne(ct, -1)`.
  There is no separate `FightOnce` adapter on this path.
- `GivreLuaHost.Travel.cs:919-936,1249-1271`: travel can return true based on
  the current map, including the already-on-target case. Normal hops observe a
  changed current map and perform the existing settle delay. Travel success alone
  is not a proof that actors and geometry have been decoded.
- `GivreLuaHost.Combat.cs:53-67` and `GivreLuaHost.cs:733-744`: combat
  separately waits for self position on the current map, then geometry. These
  are bounded, best-effort gates. `FightOne` does not use the boolean return of
  `WaitSelfReady`; absence of a self cell returns false, and a null target also
  returns false. Before the initial attack it additionally checks current-map
  agreement and `SelfReadyFor`.
- `LaunchService.Packets.cs:1590-1627`: complementary map information publishes
  the map transition, schedules geometry loading, decodes the actors, and
  publishes `MapActorsDecoded` synchronously. `BotWorldStore.cs:172-219`
  invalidates self position on a real map transition, replaces the actor list,
  then stamps the matched self cell for that map. In the normal full-map event,
  actor replacement precedes the self-ready stamp: a self-ready observation is
  not by itself evidence that full-map actors are still awaiting publication.
  Actor-added and movement events are separate updates; the code is not an
  immutable atomic snapshot, so this is a path trace, not a concurrency proof.
- `GivreLuaHost.Combat.cs:154-209`: post-fight settling is conditional on
  another previously eligible group being expected. Selection first excludes
  cooldown/blocked actors, checks group size, forbidden entries, at least one
  mandatory entry when configured, and configured quantities for species that
  are present. It then delegates reachability selection to
  `Services/Expedition/MonsterGroupReachability.cs`. Known geometry tries the
  reachable candidates in distance order; unknown geometry retains an attack
  candidate rather than concluding that there is no path. These semantics were
  inspected, not changed.

The bounded self/geometry wait, later actor additions, and conditional
post-fight settling are possible trace boundaries, not demonstrated causes.
Adding waits for arbitrary monster presence would also delay genuinely empty
or fully filtered maps and lacks a demonstrated completion signal here.

## Recorded local evidence

Read-only source:
`%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/logs/transport-2026-11-05_00-41-44.log`.
Only process IDs, map IDs, timestamps, and public monster names are reproduced;
no account identity or authentication material is included.

For process `1073741825`:

| Original line | Time | Observation |
| --- | --- | --- |
| 2730-2731 | 00:54:47.934 | Current-map event announces map `206045703`. |
| 2738 | 00:54:48.245 | Complementary map information is received, 458 bytes; actor contents are not logged. |
| 2741-2742 | 00:54:48.819 | Travel reports arrival and route visit `1/29`, map `206045703`. |
| 2752 | 00:54:48.937 | Show-actors event arrives, 214 bytes; actor contents are not logged. |
| 2753-2757 | 00:54:49.073-074 | The next journey starts toward `206045191`, using source cell `419`; no attack appears between arrival and departure. |
| 2825-2826 | 00:54:53.512-520 | Route visit `2/29`, followed by attack on Rouquette+Boumbardier+Crachefoux. |
| 4024-4025 | 00:55:04.001-004 | Fight ends and another Rouquette group is attacked on the same map. |
| 5239-5240 | 00:55:11.493 | Another fight ends and a third group is attacked on that map. |

The first map is visited without combat again at lines 8461-8465 and
20717-20721. That repetition does not prove incorrect filtering. The nearby
show-actors event is not proof that it contained an eligible monster: it can
carry other actors, and its decoded fields are absent. The successful later
map also shows that this recording does not support a universal one-fight-per-map
failure.

The log directory contained 21 transport logs dated November 3-5. Literal
expedition-marker searches found four files with such markers. No live app,
account connection, packet injection, or network operation was used.

## Checks executed

The existing adjacent tests were read before running them:
`ExpeditionLoopRegressionTests`, `ExpeditionFilterResetTests`,
`MonsterGroupReachabilityTests`, and `ScriptCombatTests`. The latter protects
combat primitives, not the real host's arrival-to-selection lifecycle.
`BotReadinessTests` concerns diagnostics and is not a combat-arrival integration
test. Generated-Lua regressions stub `fight` and `travelTo`, so they cannot
establish readiness inside `GivreLuaHost`.

Offline command, using the parent's already-built isolated Debug assembly:

```text
dotnet vstest "%USERPROFILE%/AppData/Local/Temp/givre-parent-self-visibility-20261105/bin/GivreClone.Tests/debug/GivreClone.Tests.dll" --TestCaseFilter:"FullyQualifiedName~ExpeditionLoopRegressionTests|FullyQualifiedName~ExpeditionRouteRegressionTests|FullyQualifiedName~ExpeditionCompletionRegressionTests|FullyQualifiedName~ExpeditionFilterResetTests|FullyQualifiedName~MonsterGroupReachabilityTests"
```

Monitor `mon_976G7Z75JYNR4PPZ` completed with exit code 0: 51 passed,
zero failed, zero skipped, reported duration 180 ms. This is existing-assembly
evidence, not a fresh source build or a baseline-red reproduction. No source or
test edit was justified, and no build was needed for this documentation change.
The monitor was completed before handoff.

Programming guidance and both testing references were read from
`%USERPROFILE%/.omo/binary-runtime/5.1.57/plugin/skills/programming/`.

## Exact missing witness

To prove a readiness/filtering cause, one failed visit needs the effective
MIN/MAX, mandatory, forbidden, and amount filters; the decoded actor list
(actor ID, species IDs/counts and cells); current map, self-cell map and geometry
map at selection; the selected/excluded actor and reachability outcome; and the
reason `FightOne` returned false, correlated with arrival and later actor
publication. A packet replay or existing host harness carrying those same facts
must fail on the baseline despite a valid eligible group before changing combat
code. None of the inspected recording or existing generated-Lua tests supplies
that witness. This is the precise unproven boundary; no strict-route setting or
new mandatory-filter semantics has been inferred.
