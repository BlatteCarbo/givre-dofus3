# Sylvanbot protector defeat, 2026-11-06

The ongoing mining script stopped after a real protector loss. The parent read
the complete 150-line fight journal:
`userdata/combat/2026-11-06/000240_pid1073741825_défaite.log` in the
`givre-app-offline` test-run profile.

The fight lasted 74.4 seconds and nine turns, with 12 injected spells and 16
movement requests. Rounds 2, 3, 4 and 9 contained movement and no spell.
Enemy life fell from 3400/3400 to 2943/3355, then to 795/3145 at round 9.
This is not the unchanged-enemy-life pattern of Shadowbot's Ishigro fight. The
journal records player actor 46448378210 killed at +72.937 seconds and a
non-victory result at +74.438 seconds. No transport exception is required to
explain a tactical loss; the journal's zero technical issues is not acceptance.

The script reported `PROTECTEUR victoire=false defaites=1` at 00:03:55.874
and `ARRET protecteur perdu : ne pas relancer sans diagnostic` immediately
afterward. A parent RPC read confirmed no active fight and `scriptRunning=false`,
with 8426/10356 pods and 1681/2429 HP after passive recovery. It was not restarted.

A read-only investigation is comparing the transport/planner decisions with
the four no-spell rounds. Reachability, movement scoring, challenge constraints,
cast rejection and budget remain hypotheses until their actual decision traces
are read. The pre-existing discovery capture was enabled before this fight;
its usable coverage must still be verified, not assumed from the setting alone.

After identifying the completed fight, the parent disabled discovery capture
through the same process-35876 settings toggle and stopped its dedicated
subscription (`bash_122`). The separate harvest/result subscription remains
active. Capture was not left enabled while the read-only diagnosis runs.

The parent read the effective profile after defeat: AutoFight true, Aggressive,
LegacyByStyle, Standard movement with approach allowed, Closest target, Fast
speed, automatic spells/elements, MaxCastsPerTurn 99, AutoChallenge false and
ChallengeRules false. The failed challenge reported by the journal therefore
does not by itself establish an enabled challenge constraint. Profile state
after defeat is not proof of every setting at each earlier turn. No profile
change or farming restart was made during this inspection.

## Confirmed executor defect

Read-only investigation identified an exhausted-plan cache defect. The parent
independently read the turn executor and the round-4 transport interval
46820-46929. A bounded 60000-work plan selected five walking steps ending at
426. The server confirmed movement from 383 to 426, then the executor emitted
an empty-plan snapshot with AP 10 and MP 0, without another planner decision.

`LaunchService.Combat.Turn.cs` stores the tail after the movement prefix even
when that tail has no actions. Its reuse predicate admits a non-null empty
tail; the next loop iteration therefore skips `CombatTurnPlanner.Choose` and
breaks on the cached empty actions. The attached score/work belong to the
pre-move choice, not a fresh decision from the confirmed endpoint. The same
source defect applies to the reported movement-only rounds, but does not prove
that every endpoint offers a legal attack or that correcting it wins the fight.

The planned fix excludes exhausted tails from reuse. Non-empty move/cast tails
retain all existing observation checks. A fresh empty search still terminates;
the existing 32-decision turn bound and movement budget remain unchanged.
An isolated real-executor regression is being prepared before the owner change.

## Capture confirms an actual damaging action

The parent read transport lines 45050-45219. At 00:02:46.489, after the
confirmed spell-13518 cast, the raw `GameActionFightEvent [jsq]` has action
99, source 46448378210, and nested field 37 with target -1, loss 217 and
permanent damage 21. The adjacent packet also reports action 309 on actor -1.
This provides a concrete server damage receipt, rather than relying only on
the end-of-turn life summaries. It does not validate every life/state decoder
or explain the four movement-only decisions by itself.

## Regression and owner correction checkpoint

The parent independently ran the new real-executor test before changing the
owner (`bash_124`, `walk-tail-red.trx`): both rows failed, exit 1. The
movement-only row observed one planner decision instead of the required two,
which is the intended regression. The optional reachable-cast control also
failed with no injected cast; its exact cause remains to be checked separately.

The owner change requires a non-empty action list before reusing a cached
continuation. It does not change scoring, movement limits, spell admission or
the turn decision bound. Related post-change verification is running; neither
the regression nor live behavior is yet declared green. C# LSP discovery still
fails on the pre-existing inaccessible `artifacts/kourial-tools/pypdf` path;
no permissions were changed to bypass it.

The first post-change run (`walk-tail-green.trx`) passed 54 of 56 tests, but
both new rows still failed. Inspection found a fixture error: the synthetic
`MapMovementEvent` supplied individual varints for cells, while
`MapMovementReader` requires packed bytes. The test now echoes the movement
request's packed path and asserts the real fighter position immediately after
decoded ingress. The production guard was temporarily removed to rerun the
baseline with that valid confirmation (`walk-tail-packed-red.trx`). The earlier
two failures alone do not prove the exhausted-tail defect: they could stop at
movement confirmation before reaching continuation reuse.

The corrected baseline (`bash_126`, `walk-tail-packed-red.trx`) completed
with one pass and one failure, exit 1. The reachable-cast control now passes.
The exhausted-tail row confirms arrival but still observes one fresh planner
decision instead of two (expected 2, actual 1). This isolates continuation
reuse from the malformed-packet problem. The non-empty continuation guard
was restored, and the two rows plus the neighboring planner, walk-resource
and utility-dispatch suites are running as `walk-tail-packed-green.trx`.

Post-change targeted verification completed successfully (`bash_127`,
`walk-tail-packed-green.trx`): 56 passed, zero failed or skipped, 23.2 seconds,
exit 0. This includes the corrected decoded-movement regression and its
reachable-cast control. A single full Release test run on these built artifacts
is now running as `walk-tail-full.trx` (`bash_128`, no build/restore).
No deployment or live combat with the continuation guard has happened yet.

The full run completed with 4780 passed, one failed and two skipped, 202.3
seconds, exit 1. Its sole failure is
`MZP0PacketBusTests.Competing_publish_cancel_and_timeout_settle_once_under_load`,
an unhandled `TimeoutException`. That test starts publish and cancel through
`Task.Run` and bounds their combined completion at three seconds outside its
waiter-outcome catch. This is separate from the modified turn executor; the
failure does not establish a planner regression or a clean global suite.
The unchanged PacketBus group is being run separately for diagnosis as
`walk-tail-packetbus-diagnostic.trx`. No failing test was weakened or skipped.

The isolated unchanged PacketBus group completed (`bash_129`): fourteen
passed, zero failed/skipped, 2.8 seconds, exit 0. This identifies a
load-sensitive global-suite finding, not proof that the full run was green.
The three-second thread-pool completion bound remains unfixed in that
unrelated test. The continuation correction is source/test verified; live
acceptance remains owned by the separate live-combat validation task.

## Controlled deployment preparation

After the mark-grade verification, the parent observed Bastionbot outside combat
with its script active and Sylvanbot outside combat at full 2429/2429 life with
its script stopped. Bastionbot's script was stopped cooperatively; a subsequent
state read confirmed `scriptRunning=false` and `inFight=false`. Both accounts
were then disconnected through the local control pipe. A fresh list confirmed
all five rows offline with PID zero and no running script or fight. Shadowbot
remained offline throughout. Only the old process 35876 was asked to close
through its window button; unrelated application instances were not closed.
The new build has not yet been started at this checkpoint.

## Deployment and bounded acceptance started

The replacement process is 14940, launched from
`%TEMP%/givre-regen-control-20261105/bin/GivreClone.App/release/GivreClone.exe`.
Its transport journal is `transport-2026-11-06_01-25-52.log`. The launch
subscription timed out on its Windows pipe-path probe, but a direct framed
`list_bots` request succeeded; no duplicate replacement was launched.

Bastionbot reconnected on Mikhal as PID 1073741825, at full 3063/3063 life.
The exact existing Frigost script was submitted through `run_lua_script`;
a subsequent state read confirmed movement and `scriptRunning=true`.
Sylvanbot reconnected on Mikhal as PID 1073741827 at 2429/2429 life. PID attribution
is therefore reversed relative to the previous application process.

Sylvanbot's unchanged miner script was combined with
`scripts/sylvanbot-miner-acceptance-20261106.lua`. This wrapper stops at the next
move callback after the first protector result, win or loss, or after twelve
move callbacks without a protector. It retains the original bank and defeat
handling. The control service accepted its syntax and start; a fresh state
read confirmed an active script at map 191105026, 2429/2429 life, 8426/10356
pods and 115704 kamas. This is a bounded run in progress, not combat or bank
acceptance. A subscription on the new transport journal was armed before
account reconnection. Shadowbot remains offline and no Dakal account was added.

The deployed application DLL SHA256 is
`66F93D1138E35F59A6D3F57C6328FFBD9B33DE46204B9985FC0E3390E909820E`.
At 01:33:40 Sylvanbot logged bank entry at 85 percent pods and 115704 kamas.
At 01:34:04 it logged `BANQUE depot confirme pods=85->1`. A subsequent
control read confirmed 165/10356 pods, 115076 kamas, full 2429/2429 life,
no fight and a running script. This validates one real bank-deposit/recovery
cycle, not resource sale, net profit, profession level 200 or protector success.
The 628-kama difference includes activity since the preceding state read;
it is not attributed entirely to the bank fee.

At 01:39:31 the acceptance wrapper stopped at its twelve-callback bound.
The transport contains twelve completed mining-map passages, including four
with no harvest, and no protector callback in this interval. A fresh control
read confirmed `scriptRunning=false`, `inFight=false`, full 2429/2429 life,
312/10356 pods, 114606 kamas and map 104860169. This proves bounded stopping,
route traversal, harvesting and the bank cycle on the deployed build, but it
does not exercise the movement-continuation correction in combat. The dedicated
bounded-run subscription was stopped after confirming the final state; the
general new-build harvest/combat subscription remains active.

After rereading the stopped, full-life state, the parent submitted the same
unchanged miner script plus the same twelve-callback/first-fight wrapper for
a second bounded circuit. The existing general result subscription was already
active. This extends the opportunity to observe a protector without silently
switching back to an unbounded farming run. Its result is pending.

## First observed corrected-build protector: Bastionbot

The parent read the complete `014413_pid1073741825_victoire.log` journal:
10.2 seconds, seven turns, ten injected casts and eleven movement requests,
703524 XP and loot 11112x1. Two turns had no cast and life fell to 1388/2876
(48 percent); one challenge failed. Victory is not proof of optimal tactics.

Crucially, transport lines 9311-9332 show the corrected exhausted-tail path:
at 01:44:18.931 turn three selects only two walking steps; arrival at cell 160
is server-confirmed at 01:44:18.954. At 01:44:19.023 a second planner decision
reports its own 53.1 ms / 60000-work search and an empty result. The following
snapshot has AP 10, MP 4 and self cell 160. This directly confirms fresh
planning after an exhausted movement-only tail in the deployed build; unlike
the earlier defect, this empty outcome was actually searched. It does not
establish that the bounded search found every legal attack or an optimal route.

Other turns confirm move/cast continuation and an actual place exchange
after spell 12733. Enemy life falls across rounds, and the server death event
is recorded at +9.579 seconds before the winning result. The local life tracker
already read zero before that death event; this remaining tracking discrepancy
must not be mistaken for an earlier confirmed kill. Exact packet-level damage
attribution and Sylvanbot-specific protector acceptance remain separate scopes.

The existing `tools/CombatPlannerReplay` was independently read before use.
A replay of Bastionbot PID 1073741825 turn three is subscribed as `bash_143`
in separate `givre-bastionbot-replay-20261106` artifacts. It reconstructs exact
offset fragments, rejects duplicates/gaps and calls the production planner
with the live 60000-work budget. It deliberately does not restore the omitted
live spell lookup or state-damage catalog, so any reproduced result applies
only to the logged-field boundary. No scoring or search-budget change has
been made on the basis of this one empty turn. Replay results are pending.

The 60000-work replay completed with exit 0 and reproduced empty actions:
damage/healing/control/incoming zero, position 40.69166666666666,
work 60000 and budget exhausted. This confirms the logged-field reproduction,
not that every server-legal attack was unavailable. The replay CLI now accepts
an optional bounded work limit; a diagnostic comparison at the planner's
existing maximum 180000 is running as `bash_144`. The live 60000-work limit
and production scoring are unchanged.

The maximum-budget replay completed with exit 0. It found walks to 175, 188
and 202 followed by spell 12755 centered at 283, with estimated damage 488.3,
incoming 178, total score 550.587, work 180000 and budget exhausted. This is
a planner opportunity on logged fields, not a server-confirmed cast.

One candidate-order experiment is now running at the unchanged 60000 budget:
retain eligible occupied centers before appending all area/free-cell centers,
instead of sorting the merged list solely by numeric cell ID. No candidate
is removed and no scoring or budget is changed. This source edit is provisional
until replay and regression checks establish its effect; it is not deployed.

The occupied-center experiment completed exit 0 but still returned an empty
plan at the live budget. It was reverted exactly. A different bounded
experiment now reuses the initial `Casts(root)` results when the beam later
visits the identical root origin, instead of resolving those same casts twice.
Only the untouched root's empty action history selects that cache; moved or
cast branches retain fresh generation. Replay `bash_146` is pending. This is
not an increased budget, a speculative scoring bonus or a deployed correction.

Reusing root casts also completed with an empty result and was reverted. The
next experiment changes only origin visitation: nearest living-enemy distance,
then action count and cell ID, rather than breadth-first distance from the
caster. All reachable origins and their existing scoring remain available;
the 60000 limit is unchanged. `bash_147` is running this replay before any
regression acceptance or live promotion. This ordering may be unsuitable for
some support/ranged scenarios, so one replay alone cannot justify retaining it.

The origin-order replay completed exit 0 and now selects movement followed by
a cast at 60000 work. The terminal receipt truncates the JSON; exact damage
and full path are not inferred from that partial line. Neighboring planner,
resolver, movement-continuation and Sram dispatch verification completed as
`firing-origin-order.trx`: 199 passed, zero failed/skipped, 19.9 seconds, exit 0.
That compiled run predates the new minimal origin-budget regression below.

The new regression uses eight short-range area spells on the existing seven-cell
board, a 300-work bound and Fleeing style, asserting an actual damaging cast
within budget. Its first compile/run is subscribed as `firing-origin-fixture.trx`.
It has not yet earned acceptance or mutation proof. An initially written
nonexistent style name was corrected to the real enum before this run.

The initial minimal fixture returned no cast because its enemy reply could
outweigh the hit. To isolate the budget claim, its enemy now has zero expected
damage; no production score was weakened. That isolated case passes with the
origin ordering (`bash_151`, one passed, 3.1 seconds, exit 0). The original
breadth-first origin expression is temporarily restored for the decisive
baseline run `firing-origin-red.trx` (`bash_152`); no live build is affected.

The complete clean replay receipt (`bash_150`) confirms the modified search
at 60000 chooses cells 175, 188, 202, 216 then spell 12755 at 297. Estimated
damage is 488.3, incoming 178 and total 550.606. It still exhausts the budget;
this improves candidate discovery in the observed case, not search completeness.

The decisive baseline completed (`bash_152`, `firing-origin-red.trx`): one
failed, zero passed, exit 1. The original breadth-first origin order cannot
find the fixture's damaging cast within 300 work, while the corrected order
passes the same isolated fixture. The nearest-origin ordering was restored.
Combined final verification, including that regression and the real-data Sram
cases, is subscribed as `bash_153`, `firing-origin-final-green.trx`, with an
explicit results directory under the separate test artifacts root. Until this
run completes, no final green or deployment of the origin-order change is claimed.

Final targeted verification completed (`bash_153`,
`firing-origin-final-green.trx`): 207 passed, zero failed, zero skipped,
80.3 seconds, exit 0. This includes the budget regression, existing planner
and resolver cases, movement continuation dispatch, Sram learned dispatch and
installed-client spell modifier cases. Build output reports 257 warnings;
this is not a warning-free build. The new Sram mark access now asserts
non-null explicitly. The live process still uses the previous verified build;
origin ordering has replay and regression evidence, not live acceptance yet.

The complete suite on the same binaries (`bash_154`,
`firing-origin-full.trx`) reports 4785 passed, one failed, two skipped,
204.5 seconds, exit 1. The sole failure remains
`MZP0PacketBusTests.Competing_publish_cancel_and_timeout_settle_once_under_load`,
with `System.TimeoutException` at the worker completion wait. This is the same
pre-existing full-suite failure observed before origin ordering; its source
and timeout have not been changed to make this run green. A separate live
artifact build is running under `%TEMP%/givre-origin-live-20261106` before
any cooperative replacement of the active application.

While the isolated build runs, the previous live build won another Bastionbot
protector fight: `2026-11-06/020831_pid1073741825_victoire.log`, 6.1 seconds,
eight turns, 13 injected casts and 11 movement requests, 703524 XP and
27721 x1 loot. Two turns contained no cast and one challenge failed. A fresh
control snapshot after completion confirms outside combat, script still
running, life 1565/3063 and pods 11418/15460. This is previous-build evidence,
not acceptance of the new origin ordering. Sylvanbot remains connected, full
life 2429/2429, outside combat with the bounded mining script stopped.

The isolated application build completed (`bash_156`, exit 0, 226 warnings).
Bastionbot was stopped cooperatively; fresh control state confirmed script false
and outside combat before disconnecting Bastionbot and Sylvanbot. All five account
rows were then verified offline with pid 0. Only window 9569860 of process
14940 was closed through its enabled accessible Close button; process exit
was independently confirmed. The replacement executable was launched once
as process 36248 from `%TEMP%/givre-origin-live-20261106`, retaining run
`givre-app-offline` and pipe `Givre-live-parent-20261105`.
Its application DLL SHA256 is
`4F3688B39C88D872A2CF5E65C422E3D3051E34187C7EB5AB3BD8385D1B7B0FD9`.
Control readiness is subscribed before reconnecting accounts. Launch alone
does not establish live combat acceptance.

The new pipe returned the expected five account rows. A single initial
ENOENT immediately after the connect-only readiness probe did not persist;
process 36248 remained alive and a subsequent framed request succeeded.
Bastionbot reconnected as pid 1073741825 on Mikhal and resumed the exact prior
Frigost script. Sylvanbot's first control connection request exceeded the
15-second client deadline without creating a session; after confirming that
state, the client deadline was extended to 60 seconds and reconnect succeeded.
Fresh state confirms Sylvanbot pid 1073741827 on Mikhal, 2429/2429 life,
811/10356 pods, script stopped before the bounded script start.

The original miner plus the unchanged twelve-passage/first-fight wrapper was
accepted for Sylvanbot. The new transport log is
`transport-2026-11-06_02-11-09.log`; general live events are subscribed via
`bash_162` and the explicit acceptance stop marker via `bash_163`.
Bastionbot's script-running state is confirmed; Sylvanbot's script start acceptance
still needs observed progression and any protector fight needs its own
journal analysis. Neither is inferred from command acceptance alone.

## First protector acceptance on the origin-order build

Read the complete journal `2026-11-06/021359_pid1073741827_victoire.log`:
Sylvanbot won in 63.9 seconds, seven turns, 17 injected casts and seven movement
requests, with a cast on every turn. All seven recorded movement results are
confirmed. Server death is recorded at +61.503 seconds and the final server
result grants 166086 XP and 7971 x1. Speed is Fast, not Turbo; this is not
Turbo acceptance or a controlled timing comparison with Bastionbot.

The bounded wrapper reported combat completion and stopped after one passage.
Fresh control state confirms outside combat and script false, life 1238/2429,
pods 1236/10356, kamas 113676, map 188483076. This establishes a complete
protector victory with the deployed change, not general optimality or causal
proof that ordering alone produced the win. Causality for bounded discovery
comes from the separate old-order-red/new-order-green fixture and replay.

Residual findings remain: one failed challenge; tracked life reached 31%
during combat; tracked enemy life became zero at +52.438 seconds but the
server did not announce death until +61.503. Targeting correctly continued
until that death. No new source fix or claim of resolved HP accounting follows
from this acceptance. Sylvanbot's broader mining validation remains open for
subsequent safe harvesting and recovery; the initial acceptance run is complete.

Before resuming Sylvanbot, a fresh control state confirmed complete recovery to
2429/2429 with no running script or combat. A second bounded circuit was then
started with the same original miner and unchanged acceptance wrapper. Fresh
state now confirms actual harvesting on map 188484100 [3,-17], script true,
full life, and pods increased from 1236 to 1396 without a new travel debit
(113676 kamas). This establishes recovery followed by harvesting, not a
completed second circuit. Its stop/fight marker is subscribed as `bash_164`.

The second circuit ended with the explicit twelve-passage stop at 02:32:10.
Fresh state confirms script false, outside combat, full life 2429/2429,
map 104860169, pods 1807/10356 and kamas 113026. No protector was reported
in this circuit. Across this bounded recovery/harvest run, pods increased
from 1236 to 1807 and kamas decreased by 650, so this is not evidence of net
profit or a measured XP rate. The prior protector victory remains the only
new-build combat acceptance for Sylvanbot in this checkpoint.

## Bastionbot new-build follow-up

The new build also won `023414_pid1073741825_victoire.log`: 5.1 seconds,
five turns, ten injected casts and ten movement requests, 703524 XP and
16528 x1. The first turn still contains zero casts; turns two through five
contain 3, 3, 2 and 2. One challenge failed. Fresh control state confirms
outside combat, script true, 1935/3063 life and pods 11710/15460.

The transport trace at lines 8880-8886 independently shows the first turn
starts with AP12/MP5, enemy cell 311, and returns an empty bounded plan after
51.2 ms and 60000 work, score zero. Thus this victory does not establish that
all budget starvation is fixed. A separate replay of this newly observed
snapshot is subscribed as `bash_169`, with unchanged live behavior.

Both 60000 (`bash_169`) and 180000 (`bash_170`) replay budgets return empty
plans with zero score. Disabling casts in the diagnostic (`bash_172`) also
returns empty, but uses only 19220 work. Therefore root-cast budget starvation
does not explain the empty walking result. The reconstructed snapshot has
560 cells, visible/confirmed enemy and caster, and admitted damaging spells.
A BFS on the logged adjacency finds 50 cells within five steps: cells 189
and 217 are five steps away and reduce enemy distance from 14 to 9. This does
not alone prove approaching is safe or should win the score.

The replay tool now accepts an optional diagnostic end cell, clearly separate
from the unmodified snapshot's plan. A constrained walk-only diagnostic for
cell 189 is subscribed as `bash_173` to measure exposure and future potential.
No combat behavior has changed during this investigation.

The constrained diagnostic completed (`bash_173`, exit 0): cell 189 is reached
via 135, 149, 162, 176, 189, but incoming damage is 173 and future-position
value only 61.0325, for total -111.9675. Standing still scores zero because
the enemy cannot reach it in the current reply map. This establishes that
the walking result is a safety preference, not a missed reachable endpoint
from budget exhaustion. No change to force an unsafe approach is justified by
this trace. The spell search does exhaust its budget, but increasing it did
not improve the result and does not establish a useful attack was missed.
Any future improvement must compare multi-turn progress and enemy movement
against that safety cost, not simply reward forward motion unconditionally.

The second bounded Sylvanbot circuit also stopped without a fight. A control read
confirmed full 2429/2429 life, 811/10356 pods, 113746 kamas, no active fight
and `scriptRunning=false` at map 104860169. No protector acceptance is claimed.
