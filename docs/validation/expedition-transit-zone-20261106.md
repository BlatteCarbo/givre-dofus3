# Expedition transit is not a hunting destination

User observed Boufmouth fights while Pandala was selected. Live journal and
route marker confirmed initial54169888 Frigost map was processed as step1.
BuildExpeditionProfile expands selected maps with TraversedMaps. The previous
generator fought/gathered on every expanded map. LoopStartIndex only controls
where the next circuit starts;it does not authorize activities.

Fix preserves the original selected-map snapshot as ExpeditionProfile.ActivityMaps.
Generated Lua travels/logs each route step,then yields immediately on nonselected
maps before gathering or fighting. This includes initial,intermediate and
closing transit maps. Explicit profiles without ActivityMaps retain their
existing route-as-activity contract. Death policy and monster filters unchanged.

Executable MoonSharp regression uses actual TraversedMaps and generated Lua,
starting54169888,approach2,selected10/20,intermediate/return11. It verifies
ten travel callbacks,two circuits,and activities only on10/20 in all four
gather/fight combinations. Parent removed only the new generated membership
guard for causal red:bash_274 failed3 activity cases,passed1 inactive case,
132ms,exit1. Guard restored;bash_275/mon_K49HB3ADTWZZQ48X passed126
Expedition tests,0 failures,in544ms,exit0. Receipt:expedition-transit-focused.trx.
Full suite bash_276/mon_B7TM763EZQXBSD71 exited1:
4814 passed,1 LocalControl console-wait timeout,2 existing skips,total4817,
in3m41s. Receipt:expedition-transit-full.trx. All expedition rows passed.

During full bash_276,LocalControlTests.Console_wait_observes_new_lines_without_blocking_other_reads
reported TimeoutException at the outer5s Task.WaitAsync.
Parent inspected ConsoleJournal:WaitAsync reads under the same lock used to
capture the next change signal,and Add completes the prior signal. No missed
wakeup is established from this fixture;its continuation uses asynchronous
thread-pool dispatch and both inner/outer deadlines are5s. Saturation is a
hypothesis,not a diagnosed fix. No timeout extension or production change made.
Keep full run result and isolate the unchanged test after it finishes before
deciding whether this is reproducible outside the full suite. Unchanged
LocalControlTests isolation subscribed as bash_277/mon_4ZDJ3GGR9097MTVG,
console-wait-isolated.trx completed exit0:6 passed,0 failed,in245ms.
The unchanged failure does not reproduce in this isolated run. Its full-suite
root cause remains unresolved;do not label the complete suite green.

No deployed/live acceptance yet. Existing running generated scripts need
regeneration to receive the selected activity-map set;rebuilding alone cannot
retroactively change already running Lua. Preserve the user's selected zone,
monster filters and settings when applying the verified build.

Deployment preparation:SaveExpedition persists area/subarea,group bounds,
mandatory/forbidden monsters,limits,bank/HDV,death policy,gather/fight and
resource selection in AppConfig.ExpeditionLast;RestoreExpedition reloads them.
Use that existing UI state when regenerating. The remote start_expedition
entry only accepts one subarea and fewer options;it must not replace a richer
user-selected expedition without preserving those settings. No restart executed.

Standing monitor subsequently observed route30/214 at09:39:25 with ongoing
Pandala wins. Two combat movement requests were explicitly refused at09:38
(342->328 in15ms and230->243 in32ms),then both fights still finished in victory.
Overflow tail was read in full. These are old-build live observations,not
acceptance of the new transit guard or proof server refusals are fixed.

At09:51:22 the old-build run stopped after route47/214. Control confirmed
scriptRunning=false,inFight=false,map209192199,1998/3063HP,pods4308/15460.
Console cursors907/923/939 report both source209192199 and target209191177
known to the world graph but no path. Cursor940 confirms three attempts,
reason=no_route. This is a distinct connectivity failure,not combat selection
or inventory capacity. Do not blindly restart the identical route expecting
the new activity membership guard to repair missing navigation connectivity.

Zone-policy build PID29684: Bastionbot was explicitly reselected by AX, the native
Expedition panel still showed 23 selected maps with groups 1-2, gather, bank
and HDV, and ExpeditionStartButton was enabled and on-screen (727,980,156,36).
Pressed only after confirming Bastionbot selection. Control state: scriptRunning
true, moving to map 207619076. Console: script started; first progress line
`[expedition-route] 1/33 map=73400322`. This proves regenerated route launch,
not yet transit no-fight or selected-map fight acceptance.

First live run console: route started at map73400322, then progressed through
205520903..205521410 (steps2-14/33). Fights occurred on steps2,4,6,7,8,9,
13 and later victories continued; first sampled outcome issue=2. Step1 had no
fight. Exact map-to-ActivityMaps membership for these fight maps is not yet
proven from the generated table, so transit acceptance remains pending.

Generated-code read (ExpeditionScript.cs:393-400): after travel and the route
print, `if not ACTIVITY_MAPS[MAPS[idx]] then return true end` runs before
gatherAll() and any fight call. Therefore a `selection.*` combat line after a
route step implies that map passed ACTIVITY_MAPS. Step1 map73400322 (start,
coords -16,3, outside Pandala) produced no selection line and no fight: first
live evidence of a transit map skipped. Steps 2-17 each produced selection
lines, so they were treated as activity maps. Still pending: a later transit
step inside the circuit and the closing return, to confirm no activity there.

Full first circuit read from console cursors 116-477: steps 2-33 all reached
selection, then the loop restarted at step 2 (LoopStartIndex), not at the start
map. Distinct maps across steps 2-33: 205520896-205520903 (8),
205521409-205521415 (7), 205521923-205521927 (5), 205522436-205522438 (3)
= 23, exactly the UI's 23 selected maps. Revisited steps (e.g. 205521412,
205521413, 205521924) are selected maps reused as connectors. The only
non-selected map in this route, start 73400322, had no selection and no fight,
and is not revisited. Live acceptance for this Pandala route: activity only on
selected maps, transit start skipped. Not covered: routes whose connectors leave
the selected set, dungeon mode, and the separate no_route connectivity defect.

no_route follow-up: source proved TraversedMaps kept a target when WorldPath was
empty, and the generated Lua stopped the whole expedition after 3 failed
travels. Fix: planning reports unreachable legs as launch warnings (targets kept,
zaap/havre-sac may still work); runtime marks a no_route map unreachable for the
run, warns once and continues; all activity maps unreachable stops with
reason=all_activity_maps_unreachable; other failures still stop. Causal red:
restoring stop-on-no_route made 3 of 130 Expedition tests fail. Full suite
4831 passed, 0 failed, 2 skipped (noroute-full.trx). Deployed PID30648, DLL
SHA256 8B9709CAB4E45EAD1193B3B0EAE9C9EF20478592D2903F5BE3AE25FA1C44B40E;
Bastionbot/Sylvanbot/Shadowbot reconnect requests accepted. Live no_route skip not yet
observed: the original 209192199->209191177 route is a different selection.
