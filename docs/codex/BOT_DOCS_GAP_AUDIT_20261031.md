# GivreClone compared with four Dofus bot docs

Date: 2026-10-31. Code root (read only): `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

Status words mean the following.

- **implemented**: a method or command body performs the action (injects a packet, travels, buys, forges, or returns live state). A matching name is not enough.
- **stub**: the name, setting, or planner exists, and the game action does not run from it.
- **missing**: no counterpart turned up in `GivreClone.App` or `GivreClone.Engine`.

## Fetch log

| URL | Result |
| --- | --- |
| https://doc.frigost.dev/llms.txt | HTTP content returned. Title index only. |
| https://doc.frigost.dev/llms-full.txt | Part 1 of 3 (pages 1–100 of 263). Next part advertised as https://doc.frigost.dev/llms-full.txt/1. Parts 2 and 3 were not fetched. |
| https://doc.ankabot.dev/llms.txt | HTTP content returned. Method index with one-line descriptions and a few page URLs. |
| https://doc.ankabot.dev/llms-full.txt | Part 1 of 8 (pages 1–100 of 734). Next part advertised as https://doc.ankabot.dev/llms-full.txt/1. Later parts were not fetched. |
| https://doc.docraft.dev/llms.txt | HTTP content returned. |
| https://doc.docraft.dev/llms-full.txt | Full dump returned (no pagination banner). |
| https://doc.exofast.dev/llms.txt | HTTP content returned. |
| https://doc.exofast.dev/llms-full.txt | Full dump returned (no pagination banner). |

No site was unreachable. There is no transport error to record.

Docraft and ExoFast prose below comes from their full dumps. Frigost prose comes from full-dump part 1; later Frigost titles (pixel, mouse, keyboard, sniffer, advanced combat, Wakfu) come from the index. AnkaBot prose for the product, scripts, merchant mode, and Koliseum comes from full-dump part 1; API rows after the character section come from the `llms.txt` one-liners.

## Doc facts and source evidence

Sentences in the “what it is” sections and the Capability column are documentation facts. File:line notes are from this tree. The ranked list at the end is inference.

---

## Frigost

**What it is.** Official docs at https://doc.frigost.dev/llms-full.txt (part 1) and https://doc.frigost.dev/llms.txt. Frigost is a Windows multi-account, comfort, and automation program for Dofus 3.0, Dofus Retro, and Wakfu. The introduction lists per-account hardware-id changes so several accounts can share one PC, comfort options (popup removal, permanent tactical view, speed change, animation disable), a line-of-sight calculator, a Count Harebourg confusion calculator, one-click group quest validation and group movement, combat automation, and Lua bot scripts (https://doc.frigost.dev/llms-full.txt, introduction and subscription sections). Subscriptions are split into Multi, Confort, and Bot. An internal proxy is documented; other proxy or VPN software is supposed to be off. Global features include a per-account window icon, a Chrome window on that account’s IP, and a per-account proxy or network.

Bot scripts use `move()` maps with `fight`, `gather`, `path` (`top`/`bottom`/`left`/`right`, cell, `zaap`, `zaapi`, `havenbag`), `door`, and `custom` / `lockedCustom`. Combat configuration covers automatic combat, kick, near/far placement, delays, and per-fighter spell rules, including turn comparisons and turn congruence (`Tour ≡ 7 [5]`). The index additionally lists keyboard, mouse, pixel color wait, IPC, a packet sniffer, advanced combat objects, team definition, Dofus Retro, and Wakfu.

| Capability (doc) | Givre status | Evidence |
| --- | --- | --- |
| Lua route: change map, gather, fight, NPC, bank | implemented | `GivreLuaHost.Bindings.cs` `changeMap` 82–87, `gatherAll` 92, `fight` 98, `talkNpc` 126–129, `bank["depositAll"]` 703 calling `LaunchService.Bank.cs` `BankDepositAllAsync` 149–165 |
| Zaap, zaapi, havenbag path keywords | implemented | `Bindings.cs` 81–87 and `zaap`/`zaapi` tables 765–782; `LaunchService.Interactives.cs` `EnterHavenBag` 179–189 builds `HavenBagEnterRequest` |
| Per-account proxy | implemented | `AccountsViewModel.Multi.cs` `AssignSelectedProxy` 93–102 writes `SelectedProxy` on selected rows that are not running |
| Per-account socket device id | implemented for socket login | `SocketProbe.cs` `DeviceId` 59–67 hashes the machine id with a per-account seed and documents that the license hardware id stays machine-wide |
| Official-client hardware-id change for every window | missing | The socket id above is the only per-account device value found. No official-client hardware-id writer turned up |
| Combat speed multiplier and tactical display | implemented | `NativeTurboRuntime.cs` 29–44 and `RequestState` 326 load a pinned speed module and a tactical flag; `BotsView.axaml` 964 binds `CombatTactical` |
| Combat profile: auto fight, placement, ready, challenges, spell gates | implemented | `CombatProfile.cs` `SpellConfig` 61–106 and `CombatProfile` 112–134; `LaunchService.Combat.cs` 818 refuses a spell before `MinTurn` |
| Spell rule “only turns congruent to N modulo K” | missing | `SpellConfig` has `MinTurn` (92) and no modulo or inequality field |
| One-click group invite of the bot team | implemented | `GivreLuaHost.LotG.cs` `TeamInviteAll` 687–696 calls `LaunchService.Team.cs` `PartyInvite` 38–51, which injects a party-invite frame |
| Start the whole team on the leader script | implemented | `BotsViewModel.Team.cs` `StartTeam` 591–606 |
| One shortcut that moves every official client window, with a delay | missing | No window-broadcast move command. Team follow is the socket path in `DrainTeamJoinLeader` 574–584 |
| One shortcut that talks to an NPC or validates a quest on every window | missing | Quest actions are per character in `GivreLuaHost.LotI.cs` 81–82 |
| Auto-accept of exchanges from own characters | partial | `Bindings.cs` `trade["autoAcceptTeam"]` 646–656 accepts team bots during a script. It does not accept every exchange, party invite, and dungeon teleport from comfort settings |
| FPS cap, hide entities, ungrouped taskbar, end-of-fight and level popups | missing | No matching settings in App services or bot views |
| Line-of-sight calculator and Harebourg confusion calculator | missing as tools | Fight geometry exists at `LaunchService.Combat.Script.cs` 501 (`HasLineOfSight`). No Harebourg helper |
| Pixel, mouse, and keyboard automation | missing | Frigost index lists them (https://doc.frigost.dev/llms.txt). No `pixel` or window mouse/keyboard Lua module |
| Packet subscribe | implemented | `GivreLuaHost.LotG.cs` `registerMessage` 404–435 |
| Arbitrary packet send from script | refused | `GivreLuaHost.SnowDev.cs` 16 and 187 refuse `sendMessage` |
| Discord webhook from script | implemented | `Bindings.cs` `sendWebhook` 152–160 |
| Dofus Retro and Wakfu products | missing | Docs name both games in the introduction. This tree targets Dofus 3 |

---

## AnkaBot

**What it is.** Official docs at https://doc.ankabot.dev/llms.txt and https://doc.ankabot.dev/llms-full.txt (part 1). AnkaBot is a Dofus bot, with separate AnkaBot, AnkaBotTouch, and AnkaBotRetro subscriptions. A connected character runs a Lua path. The subscription text says the package also includes AnkaChasse (treasure hunts), RoadCreator, and AnkaKoli (Koliseum), plus an Archi-Monsters notification channel (https://doc.ankabot.dev/llms-full.txt, introduction and subscription). Connection is documented as MITM (official client open) or socket (no client). Scripts use `move`, `bank`, and phoenix functions; map actions include gather, fight, doors, zaap, zaapi, and havenbag. Console commands include `/cmd`, `/joinBoss`, `/fight`, `/gather`, and `/goto`. Merchant mode takes a player-merchant spot when a watched merchant logs in (same file, “Bot marchand”). The Koliseum page describes win-rate rules and the AnkaKoli script. The index documents sale, craft, mount and paddock, quest, chat, developer packet, account-controller, and advanced fight APIs (https://doc.ankabot.dev/llms.txt).

| Capability (doc) | Givre status | Evidence |
| --- | --- | --- |
| Socket session without the official client | implemented | `SocketProbe.cs` `RunAsync` 81 and `SocketGameClient.cs` game loop |
| MITM session with the official client | implemented | `AccountsViewModel.Multi.cs` `SetSelectedMode` 106–111 switches Socket and MITM |
| Path movement, bank, gather, fight, craft | implemented | `Bindings.cs` `travelTo` 62–63, `gatherAll` 92, `fight` 98, `craftItem` 163–184; bank body in `LaunchService.Bank.cs` 149–165 |
| HDV sell, reprice, average price, buy by gid | implemented | `Bindings.cs` `BindSale` 261–316 forwards to `LaunchService.Hdv.cs` `HdvSellByGidAsync` 806–813, `HdvRepriceAllAsync` 848, `HdvBuyByGidAsync` 602–617, which calls `HdvBuyAsync` 412–433 |
| Buy one inspected offer (uid plus effects) | implemented in Lua | `LaunchService.Hdv.cs` `HdvBuySelectedOfferAsync` 627–637 re-reads the offer and checks effects before `HdvBuyAsync` |
| Quest list, step, objectives, validate | implemented | `GivreLuaHost.LotI.cs` 31–87. `initialize` is an alias of `loaded` because `QuestListRequest` is absent (comment at 34–36) |
| Quest objective text and quest-pack path | stub | `GivreLuaHost.SnowNpc.cs` 185–188 `UnsupportedLua`; `GivreLuaHost.ApiContract.cs` 36 `quest:plan` unsupported |
| Team join leader (`/joinBoss`) | implemented | `GivreLuaHost.LotB.cs` `JoinLeader` 158–165 enqueues; `BotsViewModel.Team.cs` `DrainTeamJoinLeader` 574–584 calls `HandleJoinBoss` |
| Chat and stat spending | implemented | `Bindings.cs` `sendPrivateMessage` 46–55; `GivreLuaHost.LotB.cs` `UpgradeStat` 140 |
| Planning hours that connect and run a script | implemented | `PlanningViewModel.cs` `AddSlot` 466–475 persists a weekly slot |
| Archimonster on the current map | implemented | `Bindings.cs` `map["getArchMonsters"]` 371–373 |
| Treasure hunt, fully automatic | partial | Step buttons in `TreasureViewModel.cs` `Flag` 146, `Dig` 148, `GoToStep` 187–216. One-button loop exists for Fouduglen only: `TreasureViewModel.Fouduglen.cs` `StartFouduglen` 34–41 |
| Mount status, riding, feed, paddock objects | stub | `GivreLuaHost.SnowNpc.cs` 230–243 fills `mount:*` with `UnsupportedLua` and empty tables |
| Paddock gauge planner | stub | `BreedingViewModel.cs` 26 and 45–51: default session has no executor, so Start stays disabled. `BreedingSession.cs` 45 |
| Player-merchant seat | stub | `GivreLuaHost.SnowEconomy.cs` 279–291 sets `player_merchant_mode_unavailable` and stubs `open`, `buyItem`, `priceItem`, `quantityItem`, `existItem` |
| Koliseum queue and AnkaKoli | missing | Docs: https://doc.ankabot.dev/llms-full.txt “Fonctionnement du Kolizéum” and the introduction’s AnkaKoli sentence. Source hits are comments only: `LaunchService.LotG.cs` 99, `SocketGameClient.cs` 1085 |
| Create a character from script, anti-moderator toggle, read the account password | refused | `GivreLuaHost.Snow.cs` 660–666 assigns `UnsupportedLua` for `createCharacter`, `activateAntiModerator`, and `getPassword` |
| Dofus Touch and Dofus Retro APIs | missing | Separate products in the AnkaBot index. This tree is Dofus 3 |

---

## Docraft

**What it is.** Official docs at https://doc.docraft.dev/llms.txt and https://doc.docraft.dev/llms-full.txt. Docraft is a Windows HDV bot for Dofus 3.0, Dofus Retro, and Dofus Touch. The introduction lists configurable sell quantity and minimum price, undercutting to the lowest price, a fast replace when a lot sells, buying craft ingredients, constrained buying, a sniper mode for mispriced lots, Discord notifications, archimonster soul stones, live kamas estimates, and the site scan.docraft.dev. “Associer” attaches the tool to an open official client. Buy mode is a grid of max price per lot, lot checkboxes, shortage and surplus, recipe explode (one item, or until every ingredient is non-craftable), a custom recipe, a percent change of all max prices, and a lot policy: 1000 then 100 then 10 then 1, or the cheaper unit price when it beats the other by more than a margin. Sell mode is minimum price, minimum number of lots, price refresh, percent cuts, import from inventory or current sales, and a choice to reprice all lots or only the cheapest or dearest N lots. Sniper, archimonster sale, and instant sale are separate video pages in the same dump.

| Capability (doc) | Givre status | Evidence |
| --- | --- | --- |
| Buy list with max price, allowed lots, budget, and a start button | implemented | `HdvViewModel.Trade.cs` `ToggleBuy` 373–431 loops rows through `HdvBuyRunner` and `HdvBuyByGidAsync` |
| Open the buy HDV, including travel | implemented | `EnsureBuyerModeAsync` 339–370 |
| Load a game recipe into the buy list | implemented | `LoadBuyRecipe` 603–619 reads `GameData.Recipe` |
| Explode one craftable row into its ingredients | implemented | `DecomposeBuyRow` 623–639 |
| Explode until every row is a raw material | missing | `DecomposeAllBuy` 642–662 comments “Un passage par clic” and decomposes the current rows once |
| Add arbitrary buy rows (custom ingredient list) | implemented | `AddBuyItem` 249–255. There is no saved object named “Docraft - Recette personnalisée” |
| Lot policy “largest lot first” | implemented | `HdvTradeLogic.cs` `Plan` 31–37 |
| Lot policy “cheaper unit price within a margin” | missing | `Plan` does not compare unit prices across lot sizes. Doc fact: https://doc.docraft.dev/llms-full.txt buy-mode row 12 |
| Sell with a floor price and a minimum number of lots | implemented | `TopUpRelistAsync` 547–573 sells missing lots at market minus undercut, never under `MinPrice` |
| Replace a lot as soon as it sells | implemented | `RelistAfterSaleAsync` 516–529 |
| Reprice every listing one kama under the market, without undercutting oneself | implemented | `LaunchService.Hdv.cs` `HdvRepriceAllAsync` 848, called from `Bindings.cs` `UpdateAllItems` 295. The same function’s comment says it does not raise a price |
| Reprice only the cheapest N or dearest N lots | missing | No branch of that policy in the HDV view-model |
| Sniper: buy when a lot is far under the usual price | stub | `HdvTradeSettings.cs` 66–68 stores `SniperPct` and wait bounds. `HdvViewModel.TradeSettings.cs` 218–220 only saves them. No buy loop reads them |
| Buy or sell one archimonster stone among clones of the same item | partial | Lua `HdvBuySelectedOfferAsync` 627–637 can buy a chosen uid after an effect check. The buy screen uses gid purchase (`LaunchBuyPort` 332–335), so the screen does not pick a clone |
| Discord message on buy, sell, reprice, full pods, or low kamas | partial | `ToggleBuy` 412 raises an in-app toast when `NotifyBuy` is set. Script `sendWebhook` exists (`Bindings.cs` 152–160). No HDV event posts those five cases to Discord by itself |
| Local craft-profit scan from cached prices | implemented | `HdvViewModel.Scan.cs` `RunLocalCraftScanAsync` 20–46 |
| Fresh online price check of one scan row | stub | `HdvViewModel.Trade.cs` `VerifyScanRow` 666–668 sets a status that fresh verification is unavailable and sends no request |
| scan.docraft.dev | missing | External site named in the Docraft dump. Not a screen in this tree |
| Attach Retro or Touch | missing | Docraft “Synchronisation” pages. This tree has Dofus 3 MITM and socket only |

---

## ExoFast

**What it is.** Official docs at https://doc.exofast.dev/llms.txt and https://doc.exofast.dev/llms-full.txt. ExoFast is a Windows maging bot for Dofus 3.0, Dofus Retro, and Dofus Touch. The introduction lists item and rune configuration, a live sink (“puit”), automatic rune buying, rune-use statistics, optional Lua, and speed. The dashboard picks an item, “no item”, or a script; an optional final exo; a PA/PM alternation checkbox; live tables; rune-by-rune versus all-runes validation (documented as no longer editable); a wait before each validation; and random pauses (5–15 seconds every 20–40 seconds). Each item has ordered steps of three kinds: targets (priority, stat, minimum, when to use Pa and Ra), conditions (interval to action, plus skip, finish, or `exofast_analyse()`), and conditions plus sink with AND or OR. Same priority uses the largest gap to the target. Rune purchase is a per-item pack: rune, quantity to hold, max unit price, price check, a multiplier, and a speed. The start-purchase button requires the HDV already open in buy mode. The queue imports items and selects the configuration whose name equals the item name. Statistics cover runes used, successes, success rate, and estimated kamas. Starting maging requires the workshop open, the item placed, and any rune validated once; ExoFast continues from there. Stopping is closing the workshop or choosing “no item”. Lua callbacks return -1, -2, or -3 to skip a condition, skip a step, or finish. Sound files play on success, failure, and alerts.

| Capability (doc) | Givre status | Evidence |
| --- | --- | --- |
| Run maging in the workshop from a target list, item queue, and stop button | implemented | `SmithMagicViewModel.cs` `Start` 238–243 and `ForgeAsync` call at 358. `LaunchService.Forge.cs` `ForgeAsync` 47–80 opens the workshop, moves the item, and sends one rune per step |
| Per-item queue | implemented | `AddToQueue` 119–128. Lines are kept per object uid in memory (`_linesByItem`) |
| Auto-buy missing runes, then return to the workshop | implemented | `Start` path 374–393 calls `BuyRunes`. `BotsViewModel.SmithBuy.cs` `BuyRunesCoreAsync` 24–39 walks to the nearest rune HDV, opens buy mode, and calls `HdvBuyByGidAsync` |
| Live sink and per-rune history | implemented | `SmithMagicViewModel.cs` `OnStep` 334–356 updates the pool text, success and fail counts, and line deltas |
| Target step: raise a stat, with Pa and Ra thresholds and priority | implemented | Session decision inside `RunAsync` (the `Decide` callback ending at 328) and `SmithMagicPlanner.Next` at 221. `ForgeEngine.DecideTargets` 30–39 encodes the same priority-then-gap rule and is a pure function |
| Condition steps and condition-plus-sink steps | stub | `ForgeEngine.cs` `DecideConditions` 43–47 and `Matches` 50–57. Call sites are that file and `GivreClone.Tests/ForgeAndLotTests.cs`. `Start` does not call `DecideConditions` |
| Skip-step, finish, and script actions inside a step | stub | `ForgeAction.Skip` and `Finish` exist (`ForgeEngine.cs` 10–14) and the session can return them (328). The editor does not store condition rows, AND/OR, or an `exofast_analyse` return of -1/-2/-3 |
| PA/PM exo alternation checkbox | missing | Exo lines can be added (`SmithMagicViewModel.Editor.cs` 51–60). No alternation flag |
| Buy a rune pack while the HDV is already open | missing as its own button | Purchase is tied to a forge session and always travels (`SmithBuy.cs` 33–39). ExoFast’s button assumes the buy HDV is already open (https://doc.exofast.dev/llms-full.txt, “Démarrer l'achat”) |
| Queue column that selects the configuration with the exact item name | missing | Queue rows store uid lines, not a named configuration library |
| Random pause during maging | missing on the forge session | HDV bulk has `BulkPauseEveryMin` (`HdvViewModel.Bulk.cs` 516). The forge loop in `SmithMagicViewModel.cs` has no 5–15 second pause |
| Success, fail, and alert sounds | missing | ExoFast “Alertes sonores” in the full dump. No wav player on forge events. `Notify` at 352 is a text notification |
| Retro and Touch | missing | Same as Docraft: sync pages exist only in the ExoFast dump |

`ForgeEngine`’s own comment at line 26 says the workshop is not connected. That comment is stale for target runs: `ForgeAsync` does run the workshop. It is accurate for condition steps, which the session never asks for.

---

## Ranked gaps

Inference, ordered by how large a one-button or settings-plus-button hole remains after the bodies above. Account creation, moderator detection, and official-client hardware-id changes are documented and absent or refused. They are left out of this list on purpose. This report does not describe how to add them.

1. **Sniper watch on the buy HDV.** Doc: https://doc.docraft.dev/llms-full.txt (“mode sniper” in the introduction and “[Achat] : Sniper”). Settings already stored: percent under the usual price, and a wait range (`HdvTradeSettings.cs` 66–68). Nothing reads them. Workflow: with those settings and a buy list, one button watches the open buy HDV and purchases a lot whose unit price is at least that percent under the reference, then waits a random time in the configured range, until stop or budget. Preconditions: character in game, HDV open in buy mode, at least one row with a max price, sniper percent greater than zero, enough kamas, HDV permission.

2. **Maging steps that test a stat interval and the sink.** Doc: https://doc.exofast.dev/llms-full.txt (“Type d'étape: Conditions” and “Conditions + puit”). The pure function exists (`ForgeEngine.DecideConditions` 43–47) and is not what Start runs. Workflow: edit ordered rows (stat range and/or sink range, AND or OR, action = rune, skip step, or finish), then press the existing Start button. Preconditions: character on a maging workshop map, forgeable item in the bag, runes for the chosen actions in the bag, workshop ids known, forge history readable. Start already refuses a bound item (`AddToQueue` 124).

3. **Join Koliseum and fight until stop.** Doc: https://doc.ankabot.dev/llms-full.txt introduction (AnkaKoli) and “Fonctionnement du Kolizéum”. No join method exists. Workflow: one button queues Koliseum and plays with the saved combat profile until the user stops it. Preconditions: character connected, Koliseum available for that character, a combat profile loaded (`CombatProfile.cs` 112–128), no other script holding movement, not already in a fight.

4. **Buy one chosen listing, including an archimonster stone.** Doc: https://doc.docraft.dev/llms-full.txt introduction (archimonster stones) and buy-mode tip that lists each clone’s price. Lua can already buy a chosen uid (`HdvBuySelectedOfferAsync` 627–637). The screen buys by gid. Workflow: after prices are loaded, select one offer (uid and effects) and press buy on that row. Preconditions: HDV open in buy mode, offers loaded for that gid, the fresh price still equal to the price shown, effects unchanged, kamas at least that price.

5. **Take a free player-merchant spot.** Doc: https://doc.ankabot.dev/llms-full.txt “Bot marchand” (lines describing the enable checkbox and the watched names). Lua merchant methods are explicit stubs (`GivreLuaHost.SnowEconomy.cs` 279–291). Workflow: on the current map, check the merchants to watch and press enable; the character takes the first watched spot that opens. Preconditions: map allows player-merchant mode, character not in a fight, goods to put on the stall.

6. **Buy a rune pack in the HDV the user already opened.** Doc: https://doc.exofast.dev/llms-full.txt “Achat des runes” and “Démarrer l'achat”. Current purchase always paths to the nearest rune HDV during a forge (`SmithBuy.cs` 33–39). Workflow: fill rune, target quantity, and max unit price, then one button buys until the bag holds those quantities. Preconditions: HDV already open in buy mode, rows filled, price check on or off as set, enough kamas. The forge Start button is a different workflow and stays as it is.

7. **Run paddock gauges from Start.** Doc: AnkaBot mount and paddock methods in https://doc.ankabot.dev/llms.txt (`feedMount`, `setXpRatio`, `paddockedMounts`, `putPaddockItem`). Local planner math exists; the button does not act. Workflow: load confirmed lot readings and a target, press Start to activate the planned gauges, press Stop to deactivate them. Preconditions: a verified paddock executor bound into `BreedingSession` (the default constructor has none, `BreedingViewModel.cs` 26 and `BreedingSession.cs` 45), fuel readings confirmed, same paddock id. Mount Lua remains `UnsupportedLua` (`GivreLuaHost.SnowNpc.cs` 230–243) until that executor exists.

8. **One button for an ordinary treasure hunt.** Doc: https://doc.ankabot.dev/llms-full.txt introduction (AnkaChasse included with the subscription). The treasure tab runs flag, travel, and dig as separate buttons (`TreasureViewModel.cs` 146–216). Workflow: one button asks for a hunt and repeats clue, travel, flag, and dig until the chest or a stop. Preconditions: character in game, current map known, no other script running, hunt NPC or current hunt available. Fouduglen already has its own button (`StartFouduglen` 34–41) and is not this gap.

9. **Choose the cheaper lot size when the unit price wins by a margin.** Doc: https://doc.docraft.dev/llms-full.txt buy-mode row 12 (1000→100→10→1 versus “le moins cher d’une marge”). `HdvTradeLogic.Plan` 31–37 always prefers the largest allowed lot. Workflow: a percent setting on the buy screen, then the existing buy button uses it. Preconditions: lot prices already fetched, lots checked, budget set, HDV in buy mode.

10. **Explode the buy list down to raw materials in one click.** Doc: https://doc.docraft.dev/llms-full.txt buy-mode button 7 (“décomposer la recette … jusqu’à ce que tous les ingrédients soient non-craftables”). `DecomposeAllBuy` 642–662 does one level per click. Workflow: one button repeats that decomposition until no remaining row has a recipe. Preconditions: local `dofus.sqlite` loaded, buy not already running, each row quantity greater than zero.

Frigost’s one-shortcut group move and group quest click (https://doc.frigost.dev/llms-full.txt, Confort shortcuts) stay outside this ten. Socket teams already start together and invite each other (`StartTeam` 591–606, `TeamInviteAll` 687–696). Those Frigost shortcuts act on official client windows, which this tree does not drive. Pixel and mouse tools from the Frigost index are also absent; they are a script substrate, not a single button.
