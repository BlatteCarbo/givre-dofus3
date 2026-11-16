# Sylvanbot miner live run

User requested keeping available accounts working while development and tests
continue. Parent connected only player.three / Sylvanbot to Mikhal in Socket mode,
using the explicit `Givre-live-parent-20261105` control pipe and account ID
`4bc58e7f9db345aaa2e7dfdaf9ae1726`. Other accounts were left offline; no
second account was connected to Dakal.

Observed character: Sylvanbot, level 157, life 2429/2429, miner level 148,
pods 5754/10356, initial kamas 121167. Parent read the entire existing generic
5400-character `Mineur 1-200 - Foragerbot.lua` script before running it unchanged.
Despite its historical filename, it has no character identity restriction.
It selects 12 eligible mine maps, banks at 85 percent pods, checks arrival
before gathering, and stops after a protector defeat. No webhook is present.

Console evidence on 2026-11-05 (UTC):

- 21:28:10: script started; miner level 148 and 12-map circuit observed.
- 21:28:14: zaap arrival confirmed, cost 10 kamas, seven transitions remaining.
- 21:28:17-21:28:43: successive border and door transitions logged.
- 21:28:44.095: arrival at map 188483076 confirmed.
- 21:28:44.102: Bronze gathering started at cell 240.

At this checkpoint the bot reports `scriptRunning=true`, status `gathering`,
map 188483076. This proves connection, script execution and arrival followed
by a gathering attempt, not a completed harvest, inventory gain, full circuit,
bank deposit or protector fight. Those acceptance scopes remain open.

Subsequent confirmed console receipts: 21:28:48, 11 Bronze; 21:28:56,
11 Silicate; 21:29:00, 16 Cuivre. Map 188484100 was reached between
the first and second harvests. These receipts establish actual harvesting,
not merely an attempted gather. Bank and full-circuit acceptance remain open.

## Additional requested account activities

Parent subsequently connected Shadowbot alone to Dakal in Socket mode, account
`264458b7d4924d36a9dd98ed9ba9e594`, level 183, life 2426/2426.
Enabled learned automatic spells and disabled automatic challenge constraints
for this diagnostic run. Started Plantala (subarea 942), 23 maps, groups 1-3,
three fights maximum, 20 minutes maximum, stop at 85 percent pods and after
death. This is only the first sector, not all-Pandala coverage.
First journal `20261105-223403-1073741827`: victory, 5.2 seconds, three turns,
six casts, seven moves, 89731 XP. A challenge failed; no challenge success
or post-fix binary deployment is claimed. Second fight started at 21:34:42 UTC.

Bastionbot connected to Mikhal in Socket mode, account
`c4a95aedecaf4a82803e6c64eb33ca38`. Observed jobs include level 200 woodcutter,
miner, alchemist, farmer, fisher and hunter. The server admitted this session;
the earlier reported temporary sanction's exact cause/duration remains unknown.
Started `scripts/bastionbot-high-end-harvest-20261105.lua`: 12 Frigost maps from
the existing local route, gathering and bank deposit only. The existing
multi-profession script was not run because it also sells stored resources.
21:34:30 UTC: Frigost zaap confirmed, cost 880 kamas; walking toward the
first harvest map observed. Harvest, bank and profitability still unverified.

Follow-up: Bastionbot reached map 54168355 at 21:35:29 UTC and harvested one
Frostiz at 21:35:34. A protector interrupted the next gather; the script
received `won=true` at 21:35:48 and began regeneration from 1757/3063 HP.
This establishes one harvest and a winning protector callback, not net profit
or completed bank operation.

Shadowbot's second fight stalled. Parent stopped its expedition and disconnected
the account; subsequent state confirmed offline, pid 0, no running script.
Transport `transport-2026-11-05_22-35-18.log` line 1025 records the existing
engine pause at turn 111. Reconstructed all 24 contiguous diagnostic fragments
for turn 99 (offsets 0 through 80500): enemy -1, template 6321 grade 2,
cell 453, 1/1802 HP, visible and targetable; Shadowbot cell 438, 11 AP, 1 MP.
Empty plan score: damage 0, finishes 0, incoming 148, position 489.821,
total 341.821; work 60000, budget exhausted. Several damage spells admit
enemy -1. This resembles the low-life overkill stall, but replay against the
corrected assembly is needed before attributing the whole incident to that
cause. The running application was not restarted with the corrected assembly.
Do not extend unattended Pandala farming from this failed acceptance run.

## Stops and recovery

Later state inspection showed both gather scripts stopped, so the earlier
active checkpoints must not be read as proof of continuous unattended farming.
Sylvanbot journal `20261105-223029-1073741825`: protector defeat, nine turns,
zero casts/moves. It selected Statue and logged `sans bouger`; automatic spells
were enabled. Parent disabled AutoChallenge and ChallengeRules, then restarted
the same miner script. A successful protector after that change remains needed;
the challenge restriction is evidence, not a complete causal proof.

Bastionbot stopped because regenerate(90,180) timed out at 69 percent after
starting at 57 percent. Console showed about 30 HP recovered per 15 seconds,
so 180 seconds cannot reach 90 percent of 3063 HP from that starting point.
The new script now allows 900 seconds while retaining the confirmed 90-percent
health check; no zero-energy gate or food purchase was added. Restart accepted
and the console confirmed regeneration from 2451/3063 HP toward 90 percent.
Subsequent harvest resumption still needs observation.

Second Sylvanbot protector result supersedes the challenge-only hypothesis:
`20261105-224448-1073741825` was another defeat, nine turns, two casts and two
moves. Enemy HP fell from 3400 to 2834 on turn one, then stayed there. Turns
2-9 each logged ArgumentNullException, parameter `source`, before ending the
turn. Transport 22-35-18 lines 3809, 3851, 3883, 3914, 3945, 3981, 4011 and
4041 confirm this repeated exception. No stack trace is recorded by the current
catch in LaunchService.Combat.Turn.cs:1924. Therefore disabling Statue is not
a sufficient fix. Sylvanbot's script stopped after defeat and remains stopped;
do not relaunch until the null-source failure is diagnosed. Collection reads
on the pre-planner path are under investigation; no particular collection has
yet been proven null.

Recovery acceptance: at 21:44:08 UTC regeneration confirmed 90 percent
(2757/3063 HP), immediately followed by Frostiz gathering. At 21:44:12 one
Frostiz receipt was confirmed; the script then traveled four transitions and
reached map 54169377 at 21:44:27, starting another gather. This closes the
specific timeout/recovery/resumption check. Bank and profitability remain open.

Planner follow-up extended the existing last-enemy regression table with
1/1802 HP and expected incoming damage 150, preserving its 30/9013 HP case.
Planner/resolver suites passed 195/195, zero failed/skipped, exit 0, 10.8s;
logger `last-enemy-one-hp.trx` under the isolated
`%USERPROFILE%/AppData/Local/Temp/givre-last-enemy-20261105` artifacts root.
This simplified adjacent-target regression is not a replay of the complete
Shadowbot snapshot, whose work budget and class mechanics still need investigation.

## Logged-state replay result

`tools/CombatPlannerReplay` reconstructs contiguous player-empty fragments
from a transport log, reads a log concurrently with its writer, and runs the
production planner with the live 60000-work budget. It supports solo diagnostics
with empty team intents; nonempty intents are explicitly rejected. Logged
fields do not include live SpellLookup or the state-damage rule catalog.

Release run against pid 1073741827, turn 99, exited 0. Corrected planner chose
spell 12902 at cell 453 targeting -1, cost 3 AP, damage 1, finish reward 190,
incoming 0, total 191. Budget was exhausted at 60000 but the lethal action was
retained. The live logged empty plan scored 341.821 before the correction.
This is concrete reproduction against the logged board, not a server-confirmed
cast, a proof of every omitted callback, or deployment into the running app.

The first tool run failed opening the active log without shared-write access;
the second failed deserializing the internal-constructor TeamIntentView. The
final run handles these actual contracts without changing production types.

Two read-only children were requested with model `gemini-4-argon`. The first
task status independently reported effective model
`cursor/gemini-4-argon-medium`. Their steered scopes are restrictive expedition
arrival and Sram/Roublard mechanics; their findings are not yet accepted.

## Instrumented Kanojedo reproduction

The instrumented Release app built successfully in
`Temp/givre-turn-error-20261105` (bash_94, exit 0, 226 warnings).
The previous live process 8212 closed normally after all requested accounts
were confirmed offline. Process 22616 uses the same run profile and explicit
`Givre-live-parent-20261105` control pipe. It includes the last-enemy scoring
correction. Loading this binary is not live acceptance of that correction.

Bastionbot reconnected on Mikhal and resumed the inspected Frigost script.
Server-confirmed Frostiz receipts at 22:12:25 and 22:12:36 UTC establish actual
harvesting after the restart, not uninterrupted operation or profitability.

Sylvanbot reconnected on Mikhal, traveled to 88212247, and entered 99090957 via
the door at cell 315. One `fightCell(325)` script targeted a level-50
Kanojedo punching bag; no mining loop was resumed. Transport
`transport-2026-11-05_23-05-54.log` lines 1670-1677 capture the first complete
error stack at local 23:13:24.189, pid 1073741827, epoch 3:
`SpellResolver.Resolve` local `Cast` -> planner `CastOn` -> `ChooseConstrained`
-> `LaunchService.RunFightTurn`. The same exception recurred rapidly.
The script was stopped, AutoFight disabled, then Sylvanbot disconnected and
confirmed offline. Stopping the script alone did not stop the independent
automatic turn engine. No completed healthy fight is claimed.

Newly simulated summons initialized `States` but not `AbsentStates`.
Later state effects in the same Resolve call invoked `ToHashSet()` on that
null collection. Existing summon tests resolved the state change in a second
call, whose input-copy step normalizes both sets and therefore hid this path.

`SpellResolverTests.Summon_accepts_state_changes_later_in_the_same_cast`
executes summon creation and state action 950, 951 or 952 in one production
resolver call, then checks present/absent state membership on the new summon.
All three rows failed before the fix with the same ArgumentNullException
from `Enumerable.ToHashSet` (bash_107, exit 1, `summon-state-red.trx`).
The correction initializes `AbsentStates` at summon creation; it does not
catch and ignore the exception or omit the spell side effects.

Post-correction focused verification passed 286 tests, zero failures or
skips, 15.4 seconds, exit 0 (bash_108, `summon-state-green.trx`). It includes
the resolver, planner and automatic-spell rotation suites using the isolated
`Temp/givre-last-enemy-20261105` artifact root. Full-suite verification and
live revalidation remain pending at this entry.
The C# language server still fails on the pre-existing inaccessible
`artifacts/kourial-tools/pypdf` directory; compiler results are used instead.
The scoring-review child st_01a1ada1 failed with zero tool calls after provider
idle timeouts. It supplied no review evidence.

## Corrected build follow-up

Full `GivreClone.Tests` verification passed 4770 tests, zero failures and
two existing skips, 271.1 seconds, exit 0 (bash_109, `summon-state-full.trx`).
The test summary reports 4772 total. Process 35876 now runs the corrected
app from the same `Temp/givre-last-enemy-20261105` build output.

Sylvanbot rejoined the existing level-50 punching-bag fight with AutoFight still
off, then resumed with AutoFight on and Speed Fast. Transport
`transport-2026-11-05_23-19-57.log` shows three productive local engine turns:
enemy HP 1500 -> 1044 -> 608, followed by a server death notification and
FightEndEvent at local 23:22:44.828, outcome 2. No `combat-turn-error` or
ArgumentNullException occurred in that resumed session. The diagnostic
endTurn script returned false after the fight had already finished; it is
not credited with resuming or winning the fight.

Journal `20261105-232110-1073741825` reports 107 turns and 7 casts because its
rounds retain the server history of the interrupted fight (productive rounds
105-107). It is not a fresh three-turn journal and does not prove that the
earlier empty rounds disappeared. A new single-fight test against the same
level-50 bag was started at local 23:23:23 to validate a clean fight lifecycle.

That fresh fight won: journal `20261105-232323-1073741825`, 20.1 seconds,
3 turns, 9 casts, zero moves, zero reported problems, longest gap 2.5 seconds.
The server sent FightEndEvent at 23:23:43.791; the one-fight script returned
true at 23:23:44.436. The same transport file has no ArgumentNullException
or `combat-turn-error` entries. Sylvanbot returned to 99090957 at full 2429 HP,
script stopped. This validates the resolver-crash correction on a fresh live
fight, not mobile-enemy behavior, the complete mining loop, or visual Turbo.
