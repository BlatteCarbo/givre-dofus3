# Combat verification follow-up

## New bounded miner run, intermediate observation

Parent re-read the entire existing miner source and bounded wrapper before
running them unchanged on Sylvanbot. Fresh precondition:outside combat,script
stopped,2429/2429HP,pods3360/10356,kamas112240. run_lua_script returned accepted
with completionConfirmed=false. The wrapper stops after the first completed
fight or12 move callbacks; existing defeat and unconfirmed-travel stops remain.
Persistent monitor mon_NT5WF6QMY3X4QG21 / bash_225 follows its stop sentinels.

Subsequent get_bot_state confirms scriptRunning=true,outside combat,moving
on185862149(-2,-4),2429/2429HP,pods3935,kamas111560. Intermediate difference:
+575pods and-680kamas, not a sale receipt or net profit. No protector or
full1-200 completion is established by this observation.

At06:31:44.529 the wrapper emitted its exact12-passage stop sentinel. Fresh
get_bot_state confirms scriptRunning=false,outside combat,on104860169(-4,2),
2429/2429HP,pods3953/10356,kamas111380. Final difference from this run's baseline:
+593pods,-860kamas. The bounded stop worked. This run supplies no protector
acceptance, sale proceeds, mining XP/level progression or complete1-200 proof.
Duplicate notifications came from older standing stop monitors, not additional
runs. Retiring bash_225 returned EPERM while removing its monitor-state
directory; retirement is therefore unconfirmed. No alternate deletion or
permission workaround was attempted. The subsequent monitor event explicitly
confirmed watcher killed(exit1): the watcher stopped despite state-directory
cleanup failing. The failed cleanup directory was left untouched.

## Bastionbot protector at06:20

Parent fully read combat/2026-11-06/062016_pid1073741825_victoire.log from the
running old live profile. Victory:5.1s,3 turns,8 injected casts and3 confirmed
movements. Each turn casts:3,3,2. Confirmed movement latencies are31,16,15ms.
Enemy life at turn starts is4000,2247,841, followed by authoritative death at
+5.088s and victory at+5.095s. Server outcome grants703524XP and11112x1.
One challenge failed. This is useful live non-pass-only evidence, not proof
of every injected spell's damage, challenge compliance or visual Turbo speed.
The first own turn begins+3.095s; whole-fight duration should not be presented
as planner execution time. No current-source backend fix was deployed here.

Fresh get_bot_state after the event confirms Bastionbot outside combat with
scriptRunning=true,3063/3063HP,pods2088/15460 and3550641kamas. This confirms
resource-script continuation after the protector, not net profit acceptance.

The current isolated PacketBus test run (`bash_177`) completed with exit 0:
14 passed, zero failed or skipped, 743 ms. No bus source or tests were changed.
This reproduces the earlier isolated success, not the full-suite timeout.
The stress case schedules publish and cancellation on the worker pool and
awaits their completion with a three-second bound before inspecting the bus
outcome. A timeout there must be distinguished from the bus's expected timer
exception. The existing full-suite failure is not resolved or waived by this
isolated run; changing the production bus is not justified by this evidence.

A fresh independent compile/test of the preserved `combat-planner-slice1`
worktree is subscribed as `bash_178`. Its filter selects CombatTurnPlannerTests,
CombatMovementDecisionTests and SpellChoicePlannerTests. Artifacts are isolated
under `%TEMP%/givre-slice1-current-20261106`. No secondary-worktree file was
edited or merged. The result is pending.

Direct source inspection distinguishes this secondary planner from the current
main-worktree planner: its snapshot has no visibility, summon-owner or summon
capacity fields, and its score has no control/friendly-fire terms. Its tests
cover approach/cast/retreat, survival, teleport plus walking, displacement,
swap, obstruction, preserved target history and explicit movement-safety gates.
Passing that suite cannot prove the later main-worktree class, summon or
visibility contracts, nor justify replacing the newer planner wholesale.
The main worktree retains separate displacement and mobility-gate cases in
CombatTurnPlannerTests.cs around lines 1079-1272. No report of complete class
mechanics follows from this slice verification.

The secondary-worktree run bash_178 completed with exit 0: 68 passed, zero
failed or skipped, 831 ms reported test duration. This is a fresh Release
compilation and focused test result for its current preserved source, not a
live deployment or a full-suite result. Existing compiler warnings were not
suppressed. No secondary files were changed.

Read-only investigation `st_01a1ae98` is checking the actual monster and spell
data path needed to value a pure summon. The known living-capacity tests use
synthetic summon-plus-damage descriptors and cannot establish pure-summon
tactics. No arbitrary summon score bonus or new summon behavior was added.

Sylvanbot's existing bounded miner script was resumed after a fresh outside-combat,
full-life state check: 2429/2429 life, 1807 pods, 113026 kamas, script stopped.
The retained script and wrapper are unchanged: stop after the first fight or
twelve move callbacks, preserve bank confirmation and defeat stop. The RPC
accepted it; a subsequent state confirms scriptRunning=true, movement on map
191102978 and full life, with 112816 kamas. Acceptance alone was not counted as
execution. Completion/defeat/bank markers are subscribed as bash_179; no new
harvest, fight or profitable outcome is claimed yet.

The next bounded circuit ended after three callbacks and a server-confirmed
protector victory. Full journal read:
`combat/2026-11-06/030657_pid1073741827_victoire.log`: 57.3 seconds, seven
turns, 17 injected casts, seven movements all confirmed, 166086 XP and loot
7972x1. Every turn contains a cast. One challenge failed and tracked life fell
to 32%; this is not optimality or challenge acceptance. The speed is Fast,
not proof of Turbo. Tracked enemy HP reached zero at +48.095 seconds but
server death arrived only at +55.632; targeting correctly continued.

Fresh post-stop state confirms outside combat, scriptRunning=false, map
188482052, 1244/2429 life, 2137 pods, 112851 kamas. Relative to circuit start:
+330 pods and -175 kamas; neither is a net-profit measurement. No automatic
restart was requested at this reduced life.

Parent independently checked the summon-investigation conclusion against
SpellResolver.cs:310-323, GameNameResolver.ReadMonsters and planner scoring:
new summons have occupancy and owner/template/grade metadata, not initialized
statistics or autonomous turns. GameNameResolver currently loads monster
id/name/gfx only. A positive pure-summon score would therefore be invented.
The next necessary contract is exact template/grade initialization, startup
SpellLevelData reference resolution, owner scaling and activation timing.
The installed bomb startup/explosion descriptor check is running as bash_180;
it cannot by itself establish any of those lifecycle rules.

## Bastionbot protector follow-up at 03:40

The parent read `combat/2026-11-06/034028_pid1073741825_victoire.log`:
4.9 seconds, four turns, ten injected casts, seven movement requests and
server-confirmed victory, 703524 XP and loot 16528x1. Each turn includes casts.
The third turn contains a server-observed position swap after spell12733,
followed by movement and another turn; this is execution evidence, not proof
that the planner models every Sacrieur placement combination.

At +4.798 seconds the tracked enemy life reaches zero without a death event;
the enemy remains targetable, another cast follows, and server death arrives
at +4.868 seconds. One challenge failed. Socket Turbo is reported by the
journal, not evidence of equivalent visible MITM animation speed.

Fresh control readback after the victory confirms Bastionbot outside combat,
3063/3063 life, scriptRunning=true, 12678/15460 pods and 3560690 kamas.
No script or account setting was changed for this observation. The broader
class, challenge, resource-profit and multi-account validation tasks remain open.

## Next bounded Sylvanbot circuit

A fresh account read now shows Sylvanbot recovered to2429/2429 life, outside combat
with script stopped, 2137 pods and112851 kamas. The unchanged miner plus
acceptance wrapper was resumed: stop after the first completed fight or twelve
move callbacks, preserving its bank and defeat handling. RPC acceptance was
followed by a separate state read confirming scriptRunning=true and gathering
on188483076 at full life. Existing watch bash_179 remains running; no duplicate
watch was created. No completed harvest, victory or net profit is claimed yet.

The circuit then stopped after three callbacks and another protector victory.
Parent read the entire040606_pid1073741827_victoire.log:66.8 seconds, eight
turns,19 injected casts and eight confirmed movements. Every turn casts.
Tracked life falls to20% in turn8 and challenge971 fails. Fast speed remains
configured, so this run says nothing about Turbo visual equivalence.

Tracked enemy HP reaches zero at+50.362 seconds, but it takes two more turns
before server death at+64.407. Continued targeting avoids another empty-turn
stall; the underlying HP discrepancy remains unresolved and now has a second
protector reproduction. Read-only investigation st_01a1aed3 is tracing that
specific life-event path without changing the death-confirmation rule.

Post-stop state confirms scriptRunning=false, outside combat,1236/2429 life,
2767 pods and112890 kamas. The bounded circuit therefore changes observed
stock by+630 pods and cash by+39 kamas, not a resource valuation or net profit.
No restart was requested at this reduced life.

Bastionbot's later script log reports a confirmed bank deposit13152->151 pods.
A separate fresh control read confirms151/15460 pods,3063/3063 life, outside
combat with scriptRunning=true and movement resumed on54171949. This verifies
one deposit-and-resume observation, not bank behavior under network failure
or general expedition inventory coordination. The reported cash3556861 is
not attributed to this deposit as profit or cost without transaction receipts.
# Further bounded miner observation

At04:48:46.638 the explicit stop message reported passages=12. Fresh state
confirms scriptRunning=false, outside combat, map104860169 (-4,2),2429/2429
life,3360 pods and112240 kamas. Across this bounded run the recorded delta is
+593 pods and-650 kamas. The twelve-callback stop is proven; full miner1-200,
all-mine access, profession XP and net profitability are not established.

Bastionbot's full journal044726_pid1073741825_victoire.log was read:5.1 seconds,
four turns, eight injected casts and seven confirmed movements, victory with
703524XP and16528x1, one failed challenge. All four turns cast. Tracked enemy
life reached zero at+4.617s, before authoritative death at+5.071s. The final
cast at+5.045s caused challenge text188 at+5.069s, which was logged as
cast_refused despite the death following2ms later. Current source excludes
companion challenge message597 but not188. The existing rejection regression
was extended with188 and its observed parameters; causal pre-fix run is
bash_207. No production classification fix has yet been applied.

Parent restarted Sylvanbot only after fresh state confirmed2429/2429 life,
outside combat and no running script. The existing miner source and its
first-fight-or-twelve-callback wrapper were reused unchanged. Acceptance of
the launch was followed by confirmed gathering on188483076 at full life.

A later state reports map147851011 (-11,-18), script running, outside combat,
2429/2429 life,3319 pods and112560 kamas. Compared with the restart baseline,
that is552 more pods and330 fewer kamas; neither is a net-profit estimate.
Parent re-read the actual source: its level-gated multi-mine route includes
149948416 (-11,-16), so leaving Astrub is consistent with the configured
circuit. This snapshot does not prove arrival at that mine or full-route
completion. No additional restart was sent. Monitor bash_200 remains armed
for the wrapper's explicit stop message. The broad miner task remains open.

