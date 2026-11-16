# Expedition map skipping: investigation boundary

Parent read ExpeditionScript.Build and the existing executed-Lua regressions
in ExpeditionRouteRegressionTests before proposing a route change.

The generated move function advances idx before travelTo. A false travel
result increments travelFailures, returns to the host, and the next invocation
tries the next map. A successful arrival resets that failure counter. Two
complete rounds without any success stop execution. The existing
Temporary_travel_failure_does_not_permanently_exclude_a_map explicitly expects
a failed map to be retried on subsequent rounds, not permanently blacklisted.

Consequently, changing every false travel result into a same-map retry would
change the existing route behavior rather than independently prove the reported
restrictive-expedition defect. No such change has been made. The generator
does not itself expose a strict-route flag in this inspected branch. The
remaining investigation must trace what the user's restrictive setting actually
constrains in travelTo/path selection and correlate a skipped-map trace with
its arrival result. The source inspection alone does not establish the cause.

The existing regressions were then independently executed in the parent's
current isolated Debug output: mon_EX5YJZ1MGM5GJYZ6 / bash_70 exited 0,
46 passed, zero failed or skipped, reported duration 586 ms. Filter:
ExpeditionLoopRegressionTests, ExpeditionRouteRegressionTests and
ExpeditionCompletionRegressionTests. The no-build command used
givre-parent-self-visibility-20261105 artifacts. No new test or live
expedition was run; the restrictive-map-skipping todo remains open.

Additional source tracing: the Lua travelTo callback delegates to
TravelToArgs in GivreLuaHost.Travel.cs:715, which resolves numeric ids,
numeric coordinate pairs and coordinate strings, then calls GoToMapId.
The route-step code in GivreLuaHost.Route.cs:405 deliberately permits
world-graph/zaap travel for non-adjacent waypoints. Literal searches found
no separately named restrictive/strict-route setting in this App branch;
this is not proof none exists under another name. Mandatory/forbidden
monster filters in the generated script constrain groups, not travel.
Changing route retries or globally banning zaaps would therefore lack a
reproduced contract failure at this point.
