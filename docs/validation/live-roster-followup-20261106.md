# Live roster follow-up

## Current read-only roster recovery

Process inventory (bash_411,exit0) attributes PID18080 to the deployed
givre-challenge-user-only-20261106 executable; the other three processes
are separate stream/toggle/volume QA builds. Deployment source explicitly
assigns the live pipe Givre-live-parent-20261105, not the default pipe.
Reading that explicit pipe succeeds for list_accounts and list_bots.

Current roster shows Bastionbot,Shadowbot and Sylvanbot all in game, outside combat,
with scripts stopped. Bastionbot: map205520897,2763/2763HP. Shadowbot:
map73400322,2426/2426HP. Sylvanbot: map206307841,2429/2429HP and58kamas.
The wallet is sharply different from the historical observation below; no
cause is established and no trade, purchase, banking or script restart was
issued. A console follow-up failed in the resident evaluator before dispatch
because its helper name was undefined; no console result is inferred.
A corrected read-only retry of Sylvanbot's console (HDV/kamas/purchase/sale/script
filter, last20) returns only: HDV sale mode at12:06:38UTC, unknown kamas cost
and a10-kama zaap at16:32UTC, travel to HDV map206308355 and HDV sale mode at
16:34:37UTC. No purchase or sale line accounts for the wallet now reading58.
The16:32 activity postdates the parent's last account action, so another actor
using the shared application remains plausible. The cause is unestablished;
Sylvanbot is left untouched for economic activity until it is explained.
These snapshots are not fresh farming or combat acceptance. No connection,
disconnection or account setting changed during this recovery.

Parent read the deployed control roster and individual state without issuing
game orders. Bastionbot is online with scriptRunning=true on Pandala map205521927,
1429/2763HP. This is continued expedition activity, not a below-threshold
challenge observation.

Sylvanbot and Shadowbot are online but have no script running. Their consoles record
HDV sale-mode opening at12:06:38UTC and12:08:59UTC respectively. Sylvanbot's kamas
changed from826840 in the roster to822460 in the subsequent state read. The
cause is not established; another actor may be using the shared application.
No HDV action, script restart, setting change or disconnect was issued. A quiet
script flag alone does not establish that either account is idle for takeover.
Shadowbot remains the only connected Dakal character; no additional account was
connected. Bastionbot's active expedition was left running.

The saved-script inventory contains several historical miner presets, but no
Sylvanbot-specific saved script. Names alone do not prove suitable current job,
route or banking settings. No unrelated preset was substituted or overwritten.
These observations do not close miner, Pandala farming, economic or multi-account
acceptance tasks.

## Subsequent expedition stop

The next parent roster read observes Bastionbot's scriptRunning=false on
map205520897,2763/2763HP. Console cursors1482-1489 establish the stop:
three Floristile engagement attempts at12:58:34/46/58UTC each reached the
12second start timeout, followed at12:59:10UTC by
`Expédition : action=fight map=205520897 attempts=3 reason=fight_start_timeout`
and the generic success-level "script terminé" entry. The explicit error is
the authoritative outcome; the success-level wrapper is not successful farming.
The previous Bulbuisson fight on205520898 completed, followed by gathering
and a normal one-hop transition. No below-threshold abandonment entry was
found in this console read. The reason the server did not start these three
fights remains unproven; this is not a new claim about challenge constraints.
The parent did not restart the script or alter its filters in this read.

Transport correlation resolves the immediate failure stage. The transport uses
local13:58/13:59 for console12:58/12:59UTC. At13:58:34.193 and13:58:46.202,
AttackMonster scheduled walking239->482 then attacking group-20002; both
received MapMovementRefusedEvent about22ms later and reset self to239.
At13:58:58.221 the retry scheduled239->495, followed by the same refusal21ms
later. No accepted walk/arrival-to-attack is established in this interval.
Source LaunchService.Combat.PreFight.cs queues the attack after MoveTo returns
true and dispatches only on arrival; GivreLuaHost.Combat.cs then waits for
fight start. Thus fight_start_timeout is downstream of a refused approach,
not evidence that an injected AttackMonsterRequest itself was rejected.
Underlying server/path disagreement remains open. Evidence came from the
bounded engagement-only extraction of transport-2026-11-06_12-58-33.log.

Read-only independent investigation found an incoming ExchangeRequestedTradeEvent
at 13:58:19.188, before the three refused approaches. No exchange/dialog closure
was identified in the bounded interval. This is a possible server-busy cause,
not proof that an invitation blocks movement. Do not cancel dialogs based only
on this correlation. All three paths share the prefix
239>267>282>296>311>325>353>381>409>424>438>453, so selecting 495 instead of 482
did not exercise a different route prefix. The preceding harvest movement was
server-confirmed at 239 on this map, contradicting a stale-source explanation.
Exact deployed cell geometry and authoritative dialog state are still missing.

The new Lua regression initially failed before dispatch because its fixture did
not call RebuildWalk after loading cells (engagement-refusal-red.trx). That is
not causal red evidence. The corrected fixture is being tested separately in
engagement-refusal-ready-red.trx; production behavior has not yet changed.

The second fixture run failed on an uninitialized team snapshot, not the target
behavior. After initializing that snapshot, engagement-refusal-dispatch-red.trx
reached one real forged movement dispatch and the real refusal handler. It failed
at the intended assertion: expected engagement_refused, actual fight_start_timeout,
after 12.513 seconds. No fixed sleep was introduced in the test.

GivreLuaHost.Combat.cs now snapshots MoveRefusedCount before each approach
dispatch. Its existing fight-start wait also terminates on a changed refusal
generation; when not actually in a fight it returns engagement_refused and
applies the existing group exclusion/cooldown. Attempts with no refusal retain
fight_start_timeout. Pathfinding and dialog cancellation remain unchanged.
The diagnostic, navigation-refusal and engagement-cooldown tests are running
together with receipt engagement-refusal-green.trx. This source correction is
not yet a passing result, deployment or live-farming acceptance.

Validation completed: engagement-refusal-green.trx reports 23 passed, 0 failed,
0 skipped; command exit0. The new refusal case completed in 68ms versus
12.513seconds before the fix and asserts the machine-consumed reason plus
group exclusion, not a timing threshold. Application compilation retained
225 warnings and test compilation 30 warnings. C# language-server diagnostics
were unavailable because its directory scan crashes on the inaccessible
artifacts/kourial-tools/pypdf path; compiler validation succeeded independently.
The correction remains source/test validated, not deployed or live accepted.
