# Farm par quantite - plan (agent bestiary-farm-seams, non verifie en jeu)

st_01a19382 [completed] model cursor/grok-4.7-xhigh-fast
I'll map the existing farming, stock, and bestiary seams in GivreClone so a quantity action can be designed from code that is already there.The first search hit a locked artifacts folder. I’ll narrow to the app, native, and script trees and search those in parallel.File reads are working. Next I’ll locate the bestiary, gather, stock, and native-menu seams and read the lines I will cite.<analysis>
**Literal Request**: Map a one-button bestiary action that farms a chosen quantity, with existing seams, a stop/cancel machine, tests, an ABI 8 menu verdict, and the live MITM clicks and message names.
**Actual Need**: An executable design that resolves one monster or resource, counts real owned stock, farms until that quantity, shows remaining, and stops on target or cancel, using seams that already exist.
**Success Looks Like**: File:line seams that were read, disjoint files to add or edit, a state machine, deterministic tests, and a capture list that uses only protocol names present in the code.
</analysis>

There is no bestiary screen and no quantity runner. The closest pieces are the expedition tab (monster id, drop table, generated fight script, route progress, stop button), the eco resource panel (item id, infinite gather script), unified stock (fail-closed owned counts), and the Lua host’s existing stop path. A native in-game context menu is not available from the ABI 8 mods that exist. The first surface is an app panel.

## Seams that already exist

**Monster identity.** A sub-area monster is `MonsterData.id` (`ExpMonster.Id`) at `GameDb.Expedition.cs:71-98`. On a map, that same id is `MonsterInGroupInformation.gid` inside `MapComplementaryInformationEvent` actors (`MapActorsReader.cs:96-105`). `SelectMonster` keeps a group only when its `MonsterEntry.GenericId` is in `FORCE_MONSTERS` and outside `FORBIDDEN_MONSTERS`, and the group size is inside `MIN_MONSTERS`/`MAX_MONSTERS` (`GivreLuaHost.Combat.cs:160-190`). Engagement sends `AttackMonsterRequest.monster_group_id`, the negative actor id (`MoveForge.cs:163-166`), from `FightOne` (`GivreLuaHost.Combat.cs:99-129`).

**Drop identity.** `MonsterDrops` reads `MonsterData.drops` objects with `objectId` and `percentDropForGrade1` only (`GameDb.Expedition.cs:101-118`; record at `ZoneValue.cs:7`). That percent is a grade-1 rate used for HDV value (`ZoneValue.cs:34-47`). It is not a quantity and not a kill quota. The expedition UI already loads those drops per monster card (`BotsViewModel.Expedition.cs:308-309`).

**Resource identity.** `ResourceMap.Resolve` accepts an item id or a name (`ResourceMap.cs:74-81`). `GatherScript.Build` writes `ITEMS_TO_GATHER = { itemId }` and then loops forever with `gatherAll()` (`GatherScript.cs:32-59`). Live matching uses that item gid through `GatheredItemGid`, and keeps it separate from `ELEMENTS_TO_GATHER` element-type ids (`GivreLuaHost.Gather.cs:145-160`). One gather step is `InteractiveUseRequest` with `element_id` and `skill_instance_uid` (`MoveForge.cs:142-145`; `LaunchService.Interactives.cs:163-175`).

**Owned stock.** `InventoryStore.CountByGid` sums every stack of that gid, equipped stacks included (`InventoryStore.cs:91-93`). A full bag snapshot is `InventoryContentEvent`, then `ObjectAddedEvent`, `ObjectQuantityEvent`, `ObjectDeletedEvent`, and the batch `ObjectsAddedEvent` / `ObjectsQuantityEvent` / `ObjectsDeletedEvent` (`InventoryReader.cs:8-15`, `27-88`). `LiveStockEvidence` publishes a bag only after `InventoryContentEvent` matches the post-store `HdvUpdated` signal, and a bank only after `StorageInventoryContentEvent` while storage is open and the session is not a chest or haven bag (`LiveStockEvidence.cs:10-16`, `187-201`, `232-258`). `UnifiedStockPlanner.Allocate` spends bag, then bank, then haven bag; a stale, partial, or missing bank/haven stays `UnknownStorage` and is not treated as zero (`UnifiedStockPlanner.cs:29-32`, `127-149`).

**Fight loot.** `FightEndEvent` loot is `Object.object_gid` and `Object.quantity` under `FightLoot.objects` (`FightResultReader.cs:9-44`). The script sees that as `combat:lastFightResult().loot` (`GivreLuaHost.Combat.cs:417-455`).

**Progress UI.** The expedition script prints `[expedition-route] index/total map=...` (`ExpeditionScript.cs:345`). `ExpeditionRouteProgress.TryParse` accepts only that shape (`ExpeditionRouteProgress.cs:5-23`), and the expedition tab shows it (`BotsViewModel.Expedition.cs:408-413`). That counter is maps visited, not items owned.

**Start and stop.** Expedition’s one button builds Lua and calls `RunLua` (`BotsViewModel.Expedition.cs:447-514`). `ExpeditionStop` calls `StopLua` (`BotsViewModel.Expedition.cs:533-538`). `StopLua` calls `host.Stop()`, marks the character planning-paused, and clears script-running (`BotsViewModel.Lua.cs:449-461`). `move()` returning false is the script end used by `run_lua_script` (`BotsViewModel.Control.cs:166`, `320-327`). A stuck route calls `Stop("stuck")` and raises `StuckStopped`, which also pauses planning (`GivreLuaHost.Route.cs:105-119`, `BotsViewModel.Lua.cs:598-609`). Cancelling a run clears only that run’s pending map change, harvest, and attack (`LaunchService.ScriptArrivalActions.cs:24-46`). A manual action with a default token is left in place (`ScriptArrivalCancellationTests.cs:68-75`). Replacing a script waits until the previous run, including `stopped`, has finished (`LuaScriptExecutionTests.cs:14-27`; callback budget at `GivreLuaHost.LotE.cs:65-87`). If auto-fight is on, `ReleaseScriptCombat` can hand the open turn back to the engine (`LaunchService.Combat.Script.cs:285-322`). `combat:giveUp` is a separate `ContextQuitRequest` (`GivreLuaHost.LotE.cs:266-279`). The quantity button must use `StopLua`, not give-up.

`GivreLuaHost.cs` is 51218 bytes and could not be sliced here, so the body of `Stop(string)` is not cited. The call sites above are enough to drive cancel.

## App panel first

ABI 8’s only addition past v7 is `class_parent` (`native/dofus3-native-host/include/dofus_native_mod_api.h:274-277`, `357-366`). The host API is log, Unity reflection/invoke, and hooks. There is no context-menu or UI Toolkit call.

`native-tactical` is an ABI 8 mod and builds map-cell sprites (`native-tactical/src/lib.rs:1410-1478`). `interface-analyzer` dumps UI trees on F11/F12 and does not inject a menu (`mods/interface-analyzer/README.md:1-20`). `network-dumper` accepts only ABI 5 in `DNM_Query` (`network-dumper/src/lib.rs:972-977`), so it is not an ABI 8 menu. `MoveForge.cs` was read in full and contains no bestiary request.

A later in-game menu would be a new mod after a live UI dump. It is not a seam to build on now.

## Design

One button, two entry points, one planner.

- Expedition monster card: the owner types a quantity of one drop `objectId` of that `ExpMonster.Id`.
- Eco resource row: the owner types a quantity of the selected `ResourceInfo.ItemId`.

Admission fails closed when the name matches more than one resource (`ResourceMap.Find` is a contains-search at `ResourceMap.cs:67-71`; `Resolve` may still return a contains-hit at `ResourceMap.cs:80`). The button requires an exact id. A drop button requires the monster id on the card. Several monsters can share an `objectId`; the card’s monster is the only target.

Stock gate, before any script starts:

1. Read fresh observations from `LiveStockEvidence.Read`.
2. `UnifiedStockPlanner.Allocate` for `{ gid: quantity }`.
3. Bag count for the gate is stacks with `Position == 63` only (`InventoryStore.Item` at `InventoryStore.cs:17`; the inventory tool already treats 63 as the bag at `BotsViewModel.Control.cs:175`).
4. If bag-only owned is already `>= quantity`, show completed and do not start.
5. If bank or haven bag is `UnknownStorage`, show bag owned and “bank unknown”. v1 does not farm against an unconfirmed bank and does not deposit the target item. Pods full stops the run (existing `MAX_PODS` / `STOP_PODS` behavior). Depositing the target would drop the bag count and the script would never see the target.

Then `StartNamedScript` (`BotsViewModel.NamedScript.cs:9-21`) runs generated Lua. The script sets the globals `SelectMonster` actually reads, puts `ITEMS_TO_GATHER` only for a resource, and at the top of every `move()` compares `getInventoryItemCount(gid)` with the embedded target. At or above target, `move()` returns false. It must call the one-step gather path (`gather` on a route step, `GivreLuaHost.Route.cs:299-302`), not `gatherAll()`, because `GatherAll` drains the whole map before returning (`GivreLuaHost.Gather.cs:190-201`). Fights use `fight()` / `FightOne`, which returns only after the fight ends (`GivreLuaHost.Combat.cs:120-131`), so the last fight can overshoot by that fight’s loot. That overshoot is the stop, not another pull.

Progress line, same strictness as the expedition parser:

`[quantity-farm] gid=<id> owned=<n> target=<n> remaining=<n>`

`remaining` is `max(0, target - owned)` from the same count the script uses. The panel binds that parse the way `ExpRouteProgress` does. Cancel is the existing expedition stop button: `StopLua`.

### Files

New, disjoint:

- `GivreClone.App/Services/QuantityFarm/QuantityFarmPlanner.cs` — exact id, bag-only baseline, `UnifiedStockPlanner` result, start/refuse.
- `GivreClone.App/Services/QuantityFarm/QuantityFarmScript.cs` — Lua text only.
- `GivreClone.App/Services/QuantityFarm/QuantityFarmProgress.cs` — parser, same style as `ExpeditionRouteProgress`.
- `GivreClone.Tests/QuantityFarmPlannerTests.cs`
- `GivreClone.Tests/QuantityFarmScriptTests.cs`
- `GivreClone.Tests/QuantityFarmProgressTests.cs`

Edit, disjoint:

- `GivreClone.App/ViewModels/BotsViewModel.Expedition.cs` — quantity on the selected monster drop, command calls planner then `StartNamedScript`; stop stays `ExpeditionStop`.
- `GivreClone.App/ViewModels/EcoToolsViewModel.Resources.cs` — quantity beside `MakeGatherScript`; the new command starts the bounded script instead of the infinite `GatherScript.Build` loop.
- The existing expedition and eco views that already bind those view-models — one numeric box and one button each.

Leave `GivreLuaHost.cs`, `MoveForge.cs`, `LaunchService`, and `native/` untouched.

### Stop and cancel

| State | Enter | Exit |
| --- | --- | --- |
| Idle | no host run for this action | quantity button |
| Resolving | button | refuse, or `StartNamedScript` |
| Refused | ambiguous id, quantity outside `1..1_000_000_000`, no route, no `Perm.Gather` or `Perm.Fight`, follower lock, socket gate in `ExpeditionSocketSessionGate` (`ExpeditionScript.cs:500-528`), bag already at target, bank unknown and the user has not accepted bag-only | back to Idle, no packet |
| Running | host `Running` | progress lines each `move()` |
| Stopping | `StopLua` / `host.Stop`, license `StopAllScripts`, socket `Stop("socket")`, link `Stop("disconnected")`, `Stop("stuck")` | `CancelScriptArrivalActions` drops this run’s pending harvest, attack, and map change; `stopped(reason)` has 2s and then throws on blocking verbs |
| Completed | `move()` returns false because owned `>= target` | `RunFinished` clears running; panel remaining is 0 |
| Cancelled | stop reason other than target | planning paused so the scheduler does not restart it (`BotsViewModel.Lua.cs:455`) |
| Overshoot | fight still inside `FightOne` when the count crosses the target | after `CombatEnded`, the next `move()` returns false; no `ContextQuitRequest` |

Cancel does not abandon the current fight. If the combat profile has auto-fight, the engine may finish the open turn after `ReleaseScriptCombat`. A second start waits until the previous run and its `stopped` callback finish.

### Deterministic tests

1. Exact resource id `303` resolves; `"frene"` with a second contains-hit refuses.
2. Drop `objectId` on monster `149` is that monster only, even if another monster has the same `objectId`.
3. Bag position 63 has 4, an equipped stack of the same gid has 1, target 10: owned for the gate is 4, remaining 6.
4. Bag 12, target 10: planner returns completed and the script builder is not called.
5. Fresh bag 3, stale bank 50, target 10: `UnknownStorage` is 7; start is refused.
6. Same stack uid in bag and bank counts once (same rule as `UnifiedStockPlannerTests.cs:82-95`).
7. Quantity `0`, negative, and `1_000_000_001` throw, matching `UnifiedStockPlanner.cs:49-51`.
8. Generated resource script contains `ITEMS_TO_GATHER`, the gid, the target, a `[quantity-farm]` line, and `return false`; it does not contain `gatherAll()`.
9. Generated monster script sets `FORCE_MONSTERS` to that monster id and `MIN_MONSTERS`/`MAX_MONSTERS` from the expedition sliders.
10. Parser accepts `[quantity-farm] gid=885 owned=4 target=10 remaining=6` and rejects a missing field, `owned > target` with a non-zero remaining, and a line longer than the expedition parser’s spirit (keep a small max length).
11. Two `FightEndSummary` loots of gid 885, quantities 1 then 2, against remaining 2: progress owned becomes baseline+3 and the next `move()` returns false. A different gid in the same loot does not count.
12. Harvest quantities 4 then 3 against remaining 6: stop after the second, no third `InteractiveUseRequest` in the script trace.
13. Cancel of run A removes `_pendingHarvest` / `_pendingAttack` / `_pendingMapChange` for A and leaves run B and manual actions (`ScriptArrivalCancellationTests` already covers the launch service; the new test only checks the planner does not clear a different pid).
14. Empty `GatherScript.Route` refuses start.
15. `Perm.Gather` false refuses a resource; `Perm.Fight` false refuses a monster. No script text is produced.

### Live capture

Passive MITM only, official client clicks, no forge. Clear names below are the ones in the readers and `MoveForge`. Three-letter aliases in comments (`iwo`, `iwi`, `itn`, `iwq`) are not capture keys.

1. **Bag baseline.** Character entry already carries `InventoryContentEvent`. Read `objects` → `ObjectItemInventory.position`, `ObjectItem.gid`, `ObjectItem.quantity`, `ObjectItem.uid` (`InventoryReader.cs:280-290`). Later changes: `ObjectAddedEvent`, `ObjectQuantityEvent`, `ObjectDeletedEvent`, `ObjectsAddedEvent`, `ObjectsQuantityEvent`, `ObjectsDeletedEvent`.

2. **One resource click** (a resource whose item id is already in `ResourceMap`, such as wheat). Outgoing: `InteractiveUseRequest` with `element_id` and `skill_instance_uid`. Incoming to confirm before trusting the waiter comments at `GivreLuaHost.Gather.cs:57-66`: the catalog clear name that matches `InteractiveUseEnded` for that element, `InteractiveUseError` on refusal, and `ObjectHarvested` for the gained quantity. Also record `InteractiveElementUpdatedEvent` (`interactive_element`, `enabled_skills` / `disabled_skills`) and `StatedElementUpdatedEvent` (`stated_element`) from `MapInteractivesReader.cs:88-133`. The capture has to show which of those packets actually carries the quantity. The waiter comment says `ObjectHarvested` can arrive after `InteractiveUseEnded`.

3. **One monster-group click** for a drop whose `objectId` is known. The map packet is `MapComplementaryInformationEvent`, actor field `actors`, monster id field `MonsterInGroupInformation.gid` (`MapActorsReader.cs:51-105`). Outgoing: `AttackMonsterRequest.monster_group_id`. Incoming end: `FightEndEvent.results` → `FightResultListEntry.rewards` → `FightReward.rewards` → `FightLoot.objects` → `Object.object_gid` and `Object.quantity` (`FightResultReader.cs:29-44`). Cross-check the same gid on `ObjectAddedEvent` or `ObjectQuantityEvent`.

4. **Bank, only if a later version should count it.** Open the banker (`NpcGenericActionRequest`), capture `StorageInventoryContentEvent` while the bank dialog is open, then `DialogLeaveRequest`. A haven-bag or chest `Storage*` packet must not be stored as bank (`LiveStockEvidence.cs:194-201`).

5. **Bestiary click.** Open the in-game bestiary and click one drop and one monster. Record every new clear name. `MoveForge` has no bestiary message, so if this click emits nothing, it is local UI and cannot start the bot. Pair that with an interface-analyzer F11/F12 dump of the open bestiary if a native menu is explored later.

<results>
<files>
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LotM/GameDb.Expedition.cs - monster id, sub-area lists, MonsterData.drops objectId and percentDropForGrade1
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Expedition/ZoneValue.cs - MonsterDrop is gid plus grade-1 percent, not a farm quota
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.Expedition.cs - monster cards, drop load, one-button start, stop, route progress
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/ExpeditionScript.cs - generated fight loop, getInventoryItemCount, return false on limits
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/ExpeditionRouteProgress.cs - strict progress-line parser to copy
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Protocol/MapActorsReader.cs - live group generic id from MapComplementaryInformationEvent
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/GivreLuaHost.Combat.cs - SelectMonster filters, FightOne, AttackMonster call, lastFightResult loot
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Protocol/MoveForge.cs - InteractiveUseRequest, AttackMonsterRequest, inventory and fight message names
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Protocol/FightResultReader.cs - FightEndEvent loot fields
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Resources/ResourceMap.cs - resource item id and name resolve
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Resources/GatherScript.cs - infinite gather script, no quantity
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Resources.cs - resource picker and MakeGatherScript
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/GivreLuaHost.Gather.cs - one-step gather, InteractiveUse waiter comments, GatherAll
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.Interactives.cs - SendInteractiveUse
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Protocol/MapInteractivesReader.cs - interactive update message names
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Protocol/InventoryReader.cs - bag snapshot and quantity events
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/InventoryStore.cs - CountByGid includes every position
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/UnifiedStock/LiveStockEvidence.cs - fresh bag and open-bank snapshots
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/UnifiedStock/UnifiedStockPlanner.cs - allocation and unknown storage
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.Lua.cs - RunLua, StopLua, planning pause, default script filters
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.NamedScript.cs - StartNamedScript
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/GivreLuaHost.Route.cs - one gather per tick, StuckStop
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.ScriptArrivalActions.cs - cancel pending harvest and attack for the owning run
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.Combat.Script.cs - release script combat without quitting
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/GivreLuaHost.LotE.cs - stopped callback and ContextQuitRequest
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/ScriptArrivalCancellationTests.cs - cancel isolation
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/LuaScriptExecutionTests.cs - replacement waits for cleanup
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/UnifiedStockPlannerTests.cs - stock cases to extend
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/native/dofus3-native-host/include/dofus_native_mod_api.h - ABI 8 is class_parent plus existing reflection and hooks
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/native/dofus3-native-host/mods/native-tactical/src/lib.rs - ABI 8 map overlay, no menu
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/native/dofus3-native-host/mods/interface-analyzer/README.md - UI dump only
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/native/dofus3-native-host/mods/network-dumper/src/lib.rs - ABI 5 packet dump, not an ABI 8 menu
</files>

<answer>
Build an app-side quantity action on the expedition monster card and the eco resource row. Resolve the monster as MonsterData.id / MonsterInGroupInformation.gid, or the resource as ResourceInfo.ItemId. Count bag position 63 plus a fresh UnifiedStockPlanner snapshot. Refuse when the bank is unknown or the target is already met. Start one generated script through StartNamedScript that rechecks getInventoryItemCount every move(), prints [quantity-farm] gid owned target remaining, and returns false at the target. Cancel with the existing StopLua path. Do not call gatherAll(), do not deposit the target item, and do not send ContextQuitRequest. A native bestiary context menu is not available: ABI 8 adds only class_parent, and the current mods dump UI, draw tactical cells, or capture packets. They do not inject a menu, and MoveForge has no bestiary message.

Live capture, passive official-client clicks: InventoryContentEvent and the ObjectAdded/ObjectQuantity family for the bag; one resource click for InteractiveUseRequest then the packets whose clear names are InteractiveUseEnded, InteractiveUseError, and ObjectHarvested, plus InteractiveElementUpdatedEvent and StatedElementUpdatedEvent; one group click for AttackMonsterRequest.monster_group_id and FightEndEvent loot Object.object_gid/quantity; banker only if bank stock is added later (StorageInventoryContentEvent while the bank dialog is open). Opening the in-game bestiary must be captured separately because no bestiary message name exists in this tree.
</answer>

<next_steps>
Add the three QuantityFarm types and their tests first, with no host or native edits. Wire the two existing panels only after the planner tests refuse unknown bank, ambiguous names, and an already-met bag. Run the MITM click list before treating InteractiveUseEnded or ObjectHarvested as anything beyond the gather-waiter comments.
</next_steps>
</results>