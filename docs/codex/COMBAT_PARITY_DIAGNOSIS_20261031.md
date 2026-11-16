# Diagnostic verrous, succes, placement (agent, hypotheses non confirmees en jeu)

st_01a19381 [completed] model cursor/grok-4.7-xhigh-fast
I'll trace fight locks, achievement acceptance, and near/far placement from the GivreClone source and the prior parity notes, starting with parallel searches across both trees.The parity note is in place. Next I’ll read it and search the source trees while skipping the locked artifacts folder.Fight-lock code is in `LaunchService.LotE.cs`. I’ll follow the UI, merge, and placement call sites, then the achievement and near/far paths.<analysis>
**Literal Request**: From current GivreClone source, explain why fight locks, automatic achievement acceptance, and near/far placement fail or are missing, with file:line paths, ranked causes, the smallest described fix, a test that fails today, and any live MITM capture.
**Actual Need**: A source-backed diagnosis the parent can implement or instrument without re-reading the three features. Known claims must be checked, not copied: lock merge/placement timing, an explicit achievement waiter accepting another reward id, missing independent placement, and per-spell approach not governing end of turn.
**Success Looks Like**: Each feature has a personally read execution path, a ranked cause, a small edit description, a deterministic failing test, and the exact in-game clicks plus message names still requiring a live capture. Unread oversized call sites are labeled, not treated as verified.
</analysis>

Spectator and team locks are implemented and can still send nothing. Automatic achievement claim only runs for a success finished during this session; the explicit claim waiter now filters by id. Independent near/far placement does not exist: placement is the combat style.

Lines below were read in this pass. `LaunchService.Packets.cs` (51160 bytes), `LaunchService.Combat.Turn.cs` (51196 bytes), `LaunchService.Combat.cs` (51247 bytes), and `BotsViewModel.cs` (51264 bytes) could not be sliced (offset reads return 0 lines). Their prior line cites in `docs/codex/GIVREACTIVE_PARITY_20261031.md` are not re-verified. `IsPlacementOpen`'s body was not read; callers and the `_placementOpen` test field were.

## 1. Fight locks (spectator + team, UI on, no effect)

**Path (read).** Checkboxes write the live profile and push it when the character is identified:

- `CombatLockSpectators` / `CombatLockParty` / `CombatLockJoin` — `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.CombatExtras.cs:18-20`
- `SaveAndPush` returns immediately if `_profileKey` is null; otherwise `SetCombatProfile(pid, _profile)` — `.../ViewModels/BotsViewModel.Combat.cs:409-416`. `SetCombatProfile`'s body was not read. `CombatProfile.Clone` does copy all three lock flags — `.../Services/CombatProfile.cs:177`.
- Script globals `FIGHT_LOCK_SPEC` / `FIGHT_LOCK_JOIN` / `FIGHT_LOCK_PARTY` become `SetScriptPolicy` — `.../Services/GivreLuaHost.LotE.cs:56-62`. Lua `combat:lock` calls `FightLock` directly — `GivreLuaHost.LotE.cs:136-141`.
- Merge is OR, not replace — `FightLocks.Merge` at `.../Services/CombatImportLogic.cs:131-137`. A script that does not set locks does not clear the profile.
- Automatic apply — `ApplyFightLocksAsync` at `.../Services/LaunchService.LotE.cs:106-135`. Comment at line 105 says the decoder starts this task. `OnLotEPacket` does not start it (`LotE.cs:239-254`). `RunPreFight` and `SnowEnginePreFightAsync` do not call it either. The invocation itself is in the unread `LaunchService.Packets.cs`. The 2026-10-31 note places it at `Packets.cs:855`; that line was not read here.
- Wire: option 0 secret/spectators, 1 party-only, 2 closed — `LotE.cs:44`. `ToggleFightOptionOn` — `LotE.cs:54-69`. Option 0 is an empty `FightOptionToggleRequest` (proto3 omits 0) — `GivreClone.Tests/LotETests.cs:66-74`. Inbound `FightOptionUpdateEvent` with omitted option/state/team is 0 / false / team 0 — `.../Protocol/LotEEventsReader.cs:39-45` and `LotETests.cs:167-175`.

Labels, so "team" is not one bit: `LockSpectators` = spectators, `LockParty` = group only (option 1), `LockJoin` = close the fight (option 2) — `CombatImportLogic.cs:61-63`, `CombatProfile.cs:160-162`.

**What the apply method actually does.** After merge, it returns with no packet if there is no lock, no `Perm.Fight`, or `IsTeamFollower(pid)` (`LotE.cs:114-115`; follower flag `LaunchService.Team.cs:137-142`). It then waits 400–900 ms (`LotE.cs:117`). Every toggle requires `Still()`, which is `IsCurrentTeamPlacement`: not manual, `IsPlacementOpen`, same fight start (`LaunchService.Combat.PreFight.cs:342-343`). Spectators, then party, then join (`LotE.cs:119-131`). Join waits for followers (`untilJoined: true`) and **returns from the whole method** if that wait fails (`LotE.cs:128`). That return is after spectator and party were already attempted, so it does not by itself explain both locks doing nothing. Option 2 is also refused before any inject while a follower is still expected — `LotE.cs:56` and `StrictTeamWaitTests.cs:102-107`. A toggle already marked on, or already recorded in `_fightOptSent` this fight, is not sent again (`LotE.cs:57-59`). An inbound option event is ignored for another fight id or the other team; an unknown own team keeps the event (`LotE.cs:249-253`).

The checkboxes are not gated by `CanFight`. `CombatAutoFight` is (`BotsViewModel.Combat.cs:108`). The pre-fight tooltip states the MITM relay does not block the Dofus client's own ready (`BotsViewModel.Combat.cs:227-228`). `_placementOpen` removed means placement is over and no further placement action is authorized (`StrictTeamWaitTests.cs:129-135`).

**Ranked causes.**

1. Placement closes during the 400–900 ms delay, so `Still()` is false and no option is injected. Client auto-ready, or `RunPreFight` reaching `SendReady` (`PreFight.cs:259`), both close that window. This hits spectator and party together. Whether that race happened in the reported fight is live-only.
2. The character is a team follower. `ApplyFightLocksAsync` returns at `LotE.cs:115` for every lock. The UI still shows the boxes checked. Live-only whether that character is the mule.
3. `Perm.Fight` is false. Same early return. The boxes still save.
4. Profile never reached `_combat`: character not identified (`SaveAndPush` at `BotsViewModel.Combat.cs:411`), or `SetCombatProfile` drops the flags (body unread). `Clone` would keep them if that method uses it.
5. `FightOptionOn` is already true from a `FightOptionUpdateEvent` attributed to this team, including when own team is still unknown (`LotE.cs:251-253`), or from option 0 being the omitted default. The bot then skips the send. Whether the server emitted that event is live-only.
6. The decoder never starts `ApplyFightLocksAsync`. Not confirmed; the method comment says it does, and the prefight orchestrators do not.
7. Empty option-0 payload rejected by the live server. Does not explain option 1, whose payload is non-empty (`LotETests.cs:71-73`). Live-only.
8. `_fightOptSent` remembers a toggle the server ignored, until `ResetFightOptions` (`LotE.cs:364`). Call sites of that reset were not read.

**Smallest edit.** In `ApplyFightLocksAsync`, send spectator (0) and party (1) at the start of the task, while placement is open, before the 400–900 ms delay. Keep the follower wait and the single-send guard only on option 2. If `Still()` is already false, log one line with the reason (placement closed, follower, no fight permission) and do not inject. Do not resend an option already in `_fightOptSent`. Do not change 0/1/2. Do not re-port the old lock sender. On the follower checkbox row, say locks are leader-only; `IsTeamFollower` already decides that.

**Test that fails today.** Reflection setup as in `StrictTeamWaitTests.Service`: profile `LockSpectators` and `LockParty` true, not a follower, `_placementOpen` true, fight permission granted. Clear `_placementOpen` before invoking `ApplyFightLocksAsync`. Assert a `FightOptionToggleRequest` for options 0 and 1 was built. It fails: both toggles are checked only after `Task.Delay(400..900)` via `Still()`. A second assert, `IsTeamFollower` true, expects zero toggles and passes today; that documents cause 2, it is not the race.

**Live capture (Discovery).** Developer license is required for the field dump: `DumpDiscoveryPacket` returns unless `LicenseService.IsDeveloper` (`LaunchService.Combat.Debug.cs:107`). In a fight it includes already-mapped messages (`Combat.Debug.cs:92-95`) when discovery/packet dump is on. Without that license, the transport lines still exist.

Clicks: be the group leader or solo, not a mule; character identified; spectator and group-only (and close-fight if that is the checked "team" box) on; Dofus automatic ready off; start a monster fight; do not click ready; stay on the placement screen at least 2 seconds.

Look for C->S `FightOptionToggleRequest` (empty body = secret/option 0, `option` = 1 party, `option` = 2 closed) and S->C `FightOptionUpdateEvent` (`option`, `state`, `fight_id`, `team`). Also note whether `FightReadyRequest` appears before any toggle. Log text: `[combat] pid N : FightOptionToggleRequest option=`.

## 2. Automatic achievement acceptance

**Path (read).**

- Checkbox default on: `BotSettings.AchievementAutoClaim` — `.../Services/BotSettings.cs:61-64`. The settings VM only assigns that field and saves the store — `BotSettingsViewModel.cs:118`, `:167`, `:199`. It does not call `SetAutoClaimSetting`. That push is the unread `BotsViewModel.cs` (the parity note says line 566).
- Engine: `SetAutoClaimSetting` — `LaunchService.LotM.cs:226`. Effective flag is script `LotMState.AutoClaim` AND the setting, default true if unset — `LotM.cs:229`. Lua `achievement:setAutoClaim` writes only `St.AutoClaim` — `LotMGame.cs:59`, `LotMLuaModule.cs:119-124`.
- `AchievementsEvent` only seeds finished ids — `LotM.cs:90-91`, `AchievementLedger.Seed` at `LotM/LotMLogic.cs:155`. Comment at `LotMLogic.cs:145-146`: rewards already waiting at login are not on a named mapped message. Seeded ids are done and not rewardable — `LotMLogicTests.cs:187-196`. A later finish of a seeded id does not become pending — `LotMLogic.cs:158-165`, `LotMLogicTests.cs:209-216`.
- A new `AchievementFinishedEvent` calls `OnFinished` and, only if that returns true, `ScheduleAutoClaim` — `LotM.cs:92-97`.
- Auto send is non-blocking `AchievementRewardRequest`, `Perm.Gather`, no waiter — `LotM.cs:233-240`. The loop waits 3–6 s (`LotMLogic.cs:189`), postpones while in fight (`LotM.cs:253`), sends each `AutoClaimable()` id, and polls `HasReward` up to 5 s (`LotM.cs:242-261`). Refused ids stay pending but are excluded from auto — `LotMLogic.cs:174-184`, `LotMLogicTests.cs:218-227`.
- Any `AchievementRewardResultEvent` with non-zero id updates that id — `LotM.cs:129-134`. Omitted `success` is false — `LotMReader.cs:46-49`. Readers are explicitly not validated on a real frame — `LotMReader.cs:16`.
- Explicit `achievement:claimReward` — `LotMLuaModule.cs:102-106` → `LotMGame.ClaimAchievementAsync` (`LotMGame.cs:58`) → `AchievementClaimAsync` (`LotM.cs:210-221`). The waiter predicate is `RewardResult.Id == id` (`LotM.cs:214-216`). `claimAllRewards` walks `Rewardable()`, which includes refused ids (`LotMLuaModule.cs:107-117`).

**The "waiter accepts another id" claim is stale in the claim method.** `SendAndWaitForCodeAsync` stores `Accept` and refuses to replace another pid's waiter (`LaunchService.Scripting.cs:116-138`). `AcceptCodePacket` returns false when the predicate fails (`Scripting.cs:141-146`). The comment says the waiter stays armed on unrelated packets (`Scripting.cs:112-114`). The `OnDecoded` site that must call `AcceptCodePacket` was not read (same oversized packet file). If that site matches only the event name, a different reward's `AchievementRewardResultEvent` still completes the waiter, and `LotM.cs:218-219` then writes that other id. That residual is live-only until `OnDecoded` is read. The parity note's description of `LotM.cs:214-220` does not match the method read here.

**Ranked causes for "auto accept does nothing".**

1. The reward was already finished at connect. `Seed` never enqueues it, and a repeat `AchievementFinishedEvent` does not schedule a claim. This is the current code, covered by tests that pass.
2. `Perm.Gather` is false. Both auto send and explicit claim return false (`LotM.cs:212`, `:235`). `CanAct` is the same permission (`LotMGame.cs:25`).
3. The character is in a fight for the whole claim window. The loop postpones, then only the current wave continues (`LotM.cs:253`).
4. The result omits `achievement_id` (proto3 zero). `OnLotMPacket` ignores `rid == 0` (`LotM.cs:133`), so `HasReward` stays true, the 5 s poll ends, and that wave does not clear the row. Whether the live frame omits the id is not established (`LotMReader.cs:16`).
5. A server refusal marks the id refused, so auto never retries it (`LotMLogic.cs:184`). Manual `claimAllRewards` still will.
6. Checkbox off vs engine: if the unread push never calls `SetAutoClaimSetting(false)`, the engine stays default true. That explains claims continuing while the box is off, not claims missing while the box is on.
7. Cross-id waiter, only if `OnDecoded` ignores `Accept`. The claim method itself no longer does.

**Smallest edit.** Keep the id predicate. In the unread decoder, complete `_codeWait` only through `AcceptCodePacket`, and still pass a non-matching `AchievementRewardResultEvent` to `OnLotMPacket`. Do not treat `AchievementsEvent` as an unclaimed-reward list. If login backlog must be claimed, that needs a real unclaimed-reward message from a capture; do not invent one. Do not turn auto-claim off.

**Test that fails today** if the requirement is "claim rewards already finished at login":

```csharp
var l = new AchievementLedger();
l.Seed(new[] { 5 });
Assert.Contains(5, l.AutoClaimable());
```

`LotMLogicTests.Already_seeded_achievements_never_become_rewardable_again` asserts the opposite and passes. A predicate-only test of `AcceptCodePacket(p => RewardResult(p).Id == A, packetForB)` returns false and passes today. An end-to-end "B's success completes a wait for A" test belongs on `OnDecoded`, which was not read, so it is not claimed to fail.

**Live capture.** Finish a new achievement out of combat, Succès checkbox on, do not click the reward, wait 6 seconds. Messages: S->C `AchievementFinishedEvent`, C->S `AchievementRewardRequest`, S->C `AchievementRewardResultEvent` (`achievement_id`, `success`). Separately, on connect, dump `AchievementsEvent` and any other achievement message to see whether unclaimed rewards exist on an unmapped name. To probe the waiter: call `achievement:claimReward(A)` while reward B completes, and see which result the script treats as A's answer.

## 3. Near/far placement, and per-spell approach

**Placement path (read).** There is no `PlacementMode`, none/keep, or near/far field on `CombatProfile`. `CombatStyle` is the only selector (`CombatProfile.cs:11-16`, `:113`, `:133`).

`ChoosePlacementCell` (`LaunchService.Combat.PreFight.cs:166-179`): occupied cells excluded; no known enemies returns `available[0]`; `Aggressive` minimizes distance to the nearest enemy; every other style maximizes it. Both callers pass `profile.Style` only:

- `RunPreFight` — `PreFight.cs:205`, after an enemy wait of up to about 1.6 s (`:195-204`), then `FightPlacementPositionRequest` via `SendPlacement` (`:88-94`) if the cell is not the current one.
- `SnowEnginePreFightAsync` — `LaunchService.SnowPlacement.cs:61`, same rule. `SnowStartEnginePreFight` (`:23-32`) requires `Perm.Fight`, placement open, and `AutoPreFight`. `place: false` means the script already placed itself and the engine must not place again (`SnowPlacement.cs:19-22`, `:53`).

`QaCombatHuntBudgetPlacementTests.cs:66-103` covers occupied cells, own cell, and no enemies, for all three styles. It does not cover an independent policy, because the method has no such parameter. Aggressive end-of-turn cell choice among challenge candidates is also style-only and does not read `AllowApproach` — `LaunchService.Combat.Challenges.cs:380-397`.

**Ranked placement causes.** This is missing behavior, not a broken sender. Far-while-aggressive and keep-while-still-automating challenge/ready cannot be requested. `AutoPreFight` off skips placement entirely (`PreFight.cs:190`). A script `onPlacement` / `prefightManagement` that already placed sets `place` false. No enemies falls through to the first free offered cell (`PreFight.cs:174`), which is neither near nor far.

**Smallest placement edit.** Add persisted `PlacementMode` { `LegacyByStyle`, `Keep`, `Near`, `Far` }, default `LegacyByStyle`, on `CombatProfile`, copied by `Clone` and by the import table. `ChoosePlacementCell` takes that mode: `Near` = current aggressive score, `Far` = current fleeing score, `Keep` = current cell if it is in the free offers, otherwise do not send. Use it at `PreFight.cs:205` and `SnowPlacement.cs:61`. Do not duplicate the distance loop. If the script already placed, keep `place: false`.

**Placement test that fails today:**

```csharp
int aggressive = (int)Call(service, "ChoosePlacementCell", 1u, new[] { 100, 400 }, Style.Aggressive);
int fleeing = (int)Call(service, "ChoosePlacementCell", 1u, new[] { 100, 400 }, Style.Fleeing);
Assert.Equal(fleeing, aggressive);
```

On the existing harness (enemy at cell 200), those two calls differ, so the assert fails. There is no parameter that asks for the far cell while style stays `Aggressive`. `typeof(CombatProfile).GetProperty("PlacementMode")` is also null.

**Approach (the paired known fact).** `SpellConfig.AllowApproach` defaults true and means "move to get this spell in range; false = cast in place only" — `CombatProfile.cs:104`, `SpellRowViewModel.cs:219-223`, checkbox `Views/CombatOverlays.axaml:133`. `fightBasic:addSpell` forces `AllowApproach = true` — `GivreLuaHost.SnowMap.cs:466`. `fightBasic:setApproachDistance` writes only `ScriptCombatOverrides.KiteMin` (0–20) — `SnowMap.cs:432-437`. `ApplyTo` copies that to `KiteMinDistance` and leaves `KiteMaxDistance` alone — `ScriptCombatOverrides.cs:43-44`. `KiteMaxDistance` 0 means unlimited flee — `CombatProfile.cs:122`. `playTurn` tactics are separate: 0 passive, 1 aggressive, 2 fleeing, 3 end the turn — `SnowMap.cs:478-487`. The style comment says aggressive still approaches again at end of turn — `CombatProfile.cs:13`.

The cast-reposition skip and the end-of-turn move are in unread `LaunchService.Combat.Turn.cs`. The parity note cites `:1089-1095`, `:1380-1419`, and `:1436-1471`. Those lines were not read here, so they are not confirmed. What is confirmed without that file: the per-spell flag's own contract is cast range only, the style contract still approaches at end of turn, `PreferEndCell` ignores `AllowApproach`, and `setApproachDistance` is not an exact distance.

**Smallest approach edit.** Do not rename `AllowApproach`. If "never approach" is the request, add one profile movement policy and gate both the cast reposition and the end-of-turn approach in `Turn.cs`. If old `setApproachDistance(d)` must hold a distance, set `KiteMin` and `KiteMax` to `d` only inside that Lua callback (`SnowMap.cs:435`), not in the UI kite fields.

**Approach test that fails today:**

```csharp
var p = new ScriptCombatOverrides { KiteMin = 3 }.ApplyTo(new CombatProfile(), canFight: true);
Assert.Equal(3, p.KiteMinDistance);
Assert.Equal(3, p.KiteMaxDistance);
```

The second assert fails: max stays 0. A test that `AllowApproach == false` suppresses the aggressive end-of-turn cell cannot be written until `Turn.cs` is readable; `PreferEndCell` has no spell argument.

**Live capture.** Placement: start a fight and stay in placement. S->C `FightPlacementPossiblePositionsEvent`, then C->S `FightPlacementPositionRequest` (`cell_id`, `actor_id` — `PreFight.cs:91`). Compare that cell with the nearest and farthest offered cell. There is no control that changes the choice without changing style. Approach: uncheck approach on every spell, style aggressive, play one turn. Record any C->S movement after the casts and before `FightTurnFinishRequest`. The movement message name was not re-read in `Turn.cs`; do not assume it. Discovery field dumps during the fight require the developer license (`Combat.Debug.cs:107`).

<results>
<files>
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.LotE.cs - lock apply, toggle dedupe, option-update filter
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.Combat.PreFight.cs - placement scorer, ready, placement-open gate
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.SnowPlacement.cs - second prefight path, same style coupling
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.LotM.cs - auto-claim schedule and explicit claim predicate
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LotM/LotMLogic.cs - seed does not enqueue rewards
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.Scripting.cs - waiter predicate contract; OnDecoded caller unread
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/CombatProfile.cs - style, AllowApproach, lock fields, kite max 0
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/GivreLuaHost.SnowMap.cs - setApproachDistance sets only KiteMin
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.Combat.Challenges.cs - end-cell preference ignores AllowApproach
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.CombatExtras.cs - lock checkboxes
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.Combat.cs - SaveAndPush and client auto-ready warning
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotSettingsViewModel.cs - achievement checkbox saves settings only
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/StrictTeamWaitTests.cs - option 2 blocked while a follower is missing; placement end blocks actions
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/LotMLogicTests.cs - seeded achievements are not rewardable
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/docs/codex/GIVREACTIVE_PARITY_20261031.md - prior line cites for unread Packets.cs and Turn.cs; achievement waiter description is stale
</files>

<answer>
Spectator and team locks are real sends, but `ApplyFightLocksAsync` waits 400–900 ms and then sends nothing unless placement is still open, the character is not a follower, and fight permission is present. That common gate explains both boxes doing nothing. Close-fight (option 2) is a third flag and is additionally held until mules join. Automatic achievement claim runs only for a success finished during the session; connect backlog is intentionally not pending. The explicit claim waiter now requires the reward id; the decoder that must honor that predicate was not read. Near/far placement is missing: both prefight paths place near only for Aggressive and far for every other style. Per-spell `AllowApproach` is cast-in-place only; `setApproachDistance` sets a kite minimum, and max stays unlimited.
</answer>

<next_steps>
Implement the three described edits and the failing tests above. Before editing the lock sender or the achievement decoder, capture one placement with client auto-ready off and one newly finished achievement, using the message names in each section. Do not treat Packets.cs or Turn.cs line numbers from the 2026-10-31 note as verified until those files are actually read.
</next_steps>
</results>