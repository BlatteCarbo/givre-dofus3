# Backlog d'ameliorations (agent bot-improvement-proposals, non verifie en jeu)

st_01a19380 [completed] model cursor/grok-4.7-xhigh-fast
The first pass mapped the docs and service surface, but the seams are still too thin to rank. I’ll read the research notes and the workflow code that would actually host new one-button flows.The markdown reads came back empty, so I’m pulling the same paths again and pinning line numbers on the workflow seams.Reads work again. Next I’ll pin the craft, forge, soul, bank, and expedition seams so the fifteen items stay outside the already planned list.<analysis>
**Literal Request**: A ranked list of 15 GivreClone improvements beyond the owner's current plan, each with a workflow, user value, file:line seams, the gap, size, and whether a new MITM capture is required, plus a disjoint top-5 edit set.
**Actual Need**: An executable backlog whose buttons fit the existing one-button, live-progress, and Stop contract, without redoing activation, character or server switching, visible MITM, breeding, in-game FM advice, bestiary quantity farming, achievement claim, near/far placement, or tactical rendering.
**Success Looks Like**: Fifteen ranked workflows grounded in code that was actually read, with the first five able to start in parallel because they do not edit the same files.
</analysis>

Observed facts are marked as such. Anything about unwired behavior is marked as inference. No `Energy` type appeared in the files read. Craft, bank, and crush already stop on the gather license, not on an energy pool. None of these workflows may add an energy gate.

Already present, so not proposed again: expedition Start/Stop, mule send/cancel, treasure `StartAutoHunt` and `StartFouduglen` (they launch, but do not own Stop), `CraftAsync`, `HdvBuyRunner`, and `CrushAsync`.

## Ranked improvements

**1. Production run: craft a quantity, then optionally list it.** Size **L**. New MITM capture: **no** for the craft sequence already described at `LaunchService.Craft.cs:25` and `LaunchService.Craft.cs:41`. A capture is needed only if the job's workshop element and skill ids are not already in the local game data. That lookup was not read.

The user sets the item or set, the quantity, the maximum kamas, whether the bank may be used, and an optional sale floor, then presses Run. The bot withdraws stock, buys only allowed missing inputs, crafts, and reports each step until the quantity, the budget, or Stop. This removes the hand-off between an offline bill of materials and the workshop.

Observed: `ItemCraftPlanner` says the plan is never a receipt (`ItemCraftPlanner.cs:54`), always returns `Executed: false` (`ItemCraftPlanner.cs:298`), and labels sources and crafts as consultative (`ItemCraftPlanner.cs:402`, `ItemCraftPlanner.cs:462`). The planner screen is explicitly offline (`ItemCraftPlannerViewModel.cs:88`, `ItemCraftPlannerViewModel.cs:184`); `RefreshPlan` and `Simulate` only calculate (`ItemCraftPlannerViewModel.cs:484`, `ItemCraftPlannerViewModel.cs:534`, `ItemCraftPlannerViewModel.cs:649`, `ItemCraftPlannerViewModel.cs:678`). `CraftAdvisor` is pure (`CraftAdvisor.cs:29-31`) and `EcoService` does not send game actions (`EcoService.cs:10-11`). `CraftAsync` already opens a workshop, places ingredients, sets the count, validates, and closes.

Missing: a cancellable orchestrator, live progress, a budget stop, bank withdrawal, calls into the existing buyer, and the workshop ids. Do not change the pure planner.

**2. Gather circuit that banks and continues.** Size **M**. New MITM capture: **no**. Gathering and `BankDepositAllAsync` already exist (`LaunchService.Bank.cs:145-149`).

The user picks a resource and a pod limit, then presses Run. The bot walks the nearest rich maps, gathers, deposits at the nearest known bank when pods fill, and resumes until Stop.

Observed: `GatherScript.Build` only emits `travelTo` and `gatherAll`, and its own comment says the bank must be added by hand (`GatherScript.cs:9`, `GatherScript.cs:31-38`, `GatherScript.cs:48-59`). The resource tab only saves that script (`EcoToolsViewModel.Resources.cs:59-74`). The eco notes say the same (`docs/ATELIER_ECO.md:72-73`). `NearestBank` already resolves a bank door (`BotsViewModel.Expedition.cs:228-246`).

Missing: the pod-to-bank-to-resume loop, progress, and Stop. Call `NearestBank` and `BankDepositAllAsync`; do not edit those methods.

**3. Supply missing dungeon keys, then start the dungeon.** Size **M**. New MITM capture: **no**, provided the existing buyer search can see the key. `HdvBuyRunner` already refreshes a lot price and buys under a ceiling (`HdvTradeLogic.cs:107-125`).

The user sets the dungeon, the number of runs, and a key budget, then presses one button. The bot reads the chest, buys only the missing keys under that budget, and then starts the existing dungeon script. Stop cancels the buy before a run begins.

Observed: the budget model can subtract both bag and chest (`ExpeditionBudget.cs:12`, `ExpeditionBudget.cs:59`) and explicitly buys nothing (`ExpeditionBudget.cs:25-29`). The expedition screen always passes `0` for the chest and says the chest was not consulted and the prices were not used (`BotsViewModel.Expedition.cs:151-154`, `BotsViewModel.Expedition.cs:170-171`). `ExpeditionStart` already launches the run (`BotsViewModel.Expedition.cs:449`).

Missing: open the nearest chest, fill `InBank`, buy the missing quantity through `HdvBuyRunner`, show progress, and only then call the existing start.

**4. Buy one observed underpriced lot.** Size **M**. New MITM capture: **no**. Buyer offers and lot prices are already stored (`BidHouseStore.cs:37-40`, `BidHouseStore.cs:74-82`).

The user sets the maximum unit price, the quantity, and the budget on a deal row, then presses Buy. The bot buys lot by lot until the quantity, the budget, a refusal, or Stop.

Observed: deal finding is pure and does not query the market (`MarketDeals.cs:13-15`). The deals panel only refreshes, copies the name, or opens the craft calculator (`EcoToolsViewModel.Deals.cs:23-24`, `EcoToolsViewModel.Deals.cs:44`, `EcoToolsViewModel.Deals.cs:49-58`). `LotPlanner` can choose a lot size (`LotPlanner.cs:16-20`) but nothing in the deals panel calls it or `HdvBuyRunner`.

Missing: the row command, progress, and Stop. Leave `HdvTradeLogic.cs` unchanged.

**5. Synchronize soul quantities to Metamob.** Size **M**. New MITM capture: **yes**, unless an existing item-effect list already carries the monsters inside a stone. `SoulCapture` has no such field, and no contents decoder was found in the files read.

The user saves an API key, chooses the quest and whether the bag, the chest, or both count, then presses Synchronize. The bot sends the changed absolute quantities and shows updated, skipped, and failed rows. Stop aborts the next batch. It must not change quest settings or mark messages read.

Observed: the research note ranks this as a single sync button (`docs/codex/DOFUS_GITHUB_ADDITIONAL_20261031.md:10`, `docs/codex/DOFUS_GITHUB_ADDITIONAL_20261031.md:42`, `docs/codex/DOFUS_GITHUB_ADDITIONAL_20261031.md:78`). `SoulStone` is only id, power, and name (`SoulCapture.cs:8`). `SoulScript` generates a fight script (`SoulScript.cs:8-12`). `ArchMonsterLog` stores sightings, not owned quantities (`ArchMonsterLog.cs:28-29`).

Missing: stable monster counts from complete selected storage, the diff, the batch POST, and the progress UI. This is not the planned bestiary quantity farm.

**6. Job-XP run, after item 1.** Size **M** once the production runner exists; **L** if built alone. New MITM capture: **no**.

The user sets the job, the current level, the target level, the cheapest or fastest mode, and the budget, then presses Run. The bot crafts the planned steps until the level, the budget, or Stop.

Observed: `JobXp.Plan` is an offline formula (`JobXp.cs:16-18`). `EcoService.JobPlan` is also offline (`EcoService.cs:77-84`). The screen calculates and copies a shopping list (`EcoToolsViewModel.JobXp.cs:18`, `EcoToolsViewModel.JobXp.cs:40-59`).

Missing: the execution button. It should call the item-1 runner rather than grow a second workshop machine.

**7. FM expense journal fed by confirmed events.** Size **S**. New MITM capture: **not yet**. `LaunchService.SmithSpending.cs` exists, but its body was not read. Attach to those events before asking for a capture. This is not the planned in-game advice overlay, and it does not press forge.

The user toggles Track this session. Purchases and rune uses that the server confirms append themselves; the panel shows spent kamas, stock, and cost per use. Stop ends the session.

Observed: `FmLedger` is a pure summary (`FmLedger.cs:25-26`). The screen starts a session and then requires typed purchases and uses (`EcoToolsViewModel.Fm.cs:190-216`). `ForgeEngine` still says the workshop connection remains to be done (`ForgeEngine.cs:25-26`); leave that engine to the planned advice work.

Missing: deduplicated event append, and no hand entry.

**8. Confirmed-sale journal.** Size **S**. New MITM capture: **only if** the event that reaches `AddSale` lacks gid, lot size, and net price. That caller was not read.

The user presses nothing extra during play. A Sale journal button shows item, lot, net kamas, and time for sales the server confirmed, and Stop is not required because this is a log, not a loop. Asking prices stay separate.

Observed: `BidHouseStore.AddSale` keeps a session kamas total and a count (`BidHouseStore.cs:126-136`, `BidHouseStore.cs:158`). `HdvSellEvidence` says an event class alone is not a completed transaction (`HdvSellEvidence.cs:5-6`, `HdvSellEvidence.cs:16-19`).

Missing: per-sale identity and a durable row. Do not treat a listing update as a sale.

**9. Treasure Stop and live progress on the same panel.** Size **S**. New MITM capture: **no**. Hunt state is already decoded (`TreasureViewModel.cs:68-116`).

The user sets the level and the failure cap, presses the existing start, and sees the current step on the treasure panel. Stop halts that script. The same contract covers Fouduglen.

Observed: `StartAutoHunt` and `StartFouduglen` both say to watch the Lua console (`TreasureViewModel.cs:227-233`, `TreasureViewModel.Fouduglen.cs:35-39`). `StopGo` exists only for the single-step trip (`TreasureViewModel.cs:218`). `TreasureScript` already encodes the loop (`TreasureScript.cs:9-18`).

Missing: a Stop bound to that host, and step text on the panel rather than only in the console.

**10. Capture the archmonster that was just seen, once.** Size **M**. New MITM capture: **no** for travel plus the existing soul script. The capture duration and cooldown are still marked unconfirmed in `docs/CAPTURE_AMES.md:8` and `docs/CAPTURE_AMES.md:26`; that is a gameplay check, not a new packet family. This is not the planned bestiary quantity target.

The user presses Capture on a sighting row. The bot travels to that map, fights groups that contain that monster, and stops after one capture attempt or on Stop.

Observed: a sighting already has map, coordinates, and cell (`ArchMonsterLog.cs:8-18`), and the phone alert is documentation only (`docs/ALERTES_TELEPHONE.md:3-4`). `SoulScript.Build` fights whatever eligible group is already on the current map (`SoulScript.cs:29-33`); it has no target map.

Missing: travel to `MapId`, one-shot completion, progress, and Stop.

**11. Optional buy when a price alert hits.** Size **S**. New MITM capture: **no**. Call the existing `HdvBuyRunner`.

The user adds an optional lot size, budget, and ceiling to an alert, then arms Buy when hit. A fresh market observation at or below the target buys one planned lot and stops. Discord-only alerts stay the default.

Observed: alerts only decide whether to notify (`PriceAlerts.cs:6`, `PriceAlerts.cs:41-45`). The screen saves the alert and says Discord (`EcoToolsViewModel.Alerts.cs:26`, `EcoToolsViewModel.Alerts.cs:49-60`).

Missing: the armed-buy flag, one purchase, progress, and Stop. Do not buy a hand-typed price; `PriceAlerts.IsFreshObservation` already requires a recent market source (`PriceAlerts.cs:44-45`).

**12. Crush one item, show the yield, then continue the rest.** Size **S**. New MITM capture: **no**. The recycle open and result events are already required (`LaunchService.Crush.cs:10-12`).

The user selects one stack and presses Crush. The bot breaks one item, shows the confirmed rune yield and known value, and continues the remainder only after that result is visible. Stop ends the queue.

Observed: `CrushTrialPolicy` refuses to release the rest until one item was confirmed and the user has seen the yield (`CrushTrialPolicy.cs:11-12`, `CrushTrialPolicy.cs:29-30`, `CrushTrialPolicy.cs:50-56`). `CrushAsync` instead moves every selected stack immediately (`LaunchService.Crush.cs:17`, `LaunchService.Crush.cs:43-55`). The bag picker only queues rows (`SmithMagicViewModel.GoalsRun.cs:22-39`); the command that calls `CrushAsync` was not in that file.

Missing: the runner must consult the policy. Do not edit `SmithMagicViewModel.Advice.cs`; that belongs to the planned advice work.

**13. Refresh wealth from the chest.** Size **S**. New MITM capture: **no**. `BankOpenAsync` already opens the chest (`LaunchService.Bank.cs:230-250`).

The user presses Refresh wealth. The bot opens the nearest known chest if this session has none, values the bag and the chest, closes the chest, and shows the total. Stop cancels the trip or the open.

Observed: `Wealth.Compute` is pure (`Wealth.cs:16-20`). The eco notes say the chest counts only after it has been opened once (`docs/ATELIER_ECO.md:23-24`). The bank store stays empty until that open (`LaunchService.Bank.cs:24-26`).

Missing: the button that opens, snapshots, and closes. No wealth view-model was in the files read, so this needs a new partial.

**14. Travel to a notebook position.** Size **S**. New MITM capture: **no**. Treasure and mule already travel by map.

The user selects a saved position and presses Go. The bot travels there; Stop cancels the trip. Coordinates stay in the notebook.

Observed: the notebook comment says nothing is sent to the game (`EcoToolsViewModel.Notebook.cs:48-49`). The only position action copies coordinates (`EcoToolsViewModel.Notebook.cs:74`). The eco notes say the same (`docs/ATELIER_ECO.md:38-39`).

Missing: a coordinate-to-map resolution and the existing travel call, plus progress and Stop.

**15. Optional upload of observed HDV prices.** Size **M**. New MITM capture: **no**. Prices are already decoded into memory (`BidHouseStore.cs:8-13`, `BidHouseStore.cs:50-54`).

The user supplies an operator-issued key, chooses the servers, and presses Send observations. The bot uploads the current deduplicated snapshot and shows the last accepted count. Disabling it or pressing Stop sends nothing further. No bundled frontend key.

Observed: the research note limits this to a provisioned batch API (`docs/codex/DOFUS_GITHUB_ADDITIONAL_20261031.md:13`, `docs/codex/DOFUS_GITHUB_ADDITIONAL_20261031.md:81`). `BidHouseStore.Clear` drops the session (`BidHouseStore.cs:165`). No upload or disk journal was in that file.

Missing: the toggle, the batch sender, and the progress line. A button can read the current snapshot, so this does not need to edit `BidHouseStore.cs`.

## Top 5 to implement first

These five do not share files. Job XP waits until the production runner exists. Later eco buttons must not edit `EcoToolsPanel.axaml` while the gather agent owns it, and dungeon work must be the only edit to `BotsView.axaml`. Do not edit `SmithMagicViewModel.Advice.cs`, `LaunchService.Craft.cs`, `LaunchService.Bank.cs`, or `HdvTradeLogic.cs`; call them.

**1. Production run**
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/ItemCraftPlannerViewModel.cs`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Views/ItemCraftPlannerPanel.axaml`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/ItemCraftPlanner/ItemCraftProductionRun.cs` (new)
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/ItemCraftPlanner/WorkshopLocator.cs` (new)

**2. Gather, bank, resume**
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Resources/GatherScript.cs`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Resources.cs`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Views/EcoToolsPanel.axaml`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Resources/GatherBankRun.cs` (new)

**3. Dungeon keys, then start**
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.Expedition.cs`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Views/BotsView.axaml`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Expedition/DungeonKeySupply.cs` (new)

**4. Buy an observed deal**
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Deals.cs`
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Eco/ObservedLotBuy.cs` (new)
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Views/ObservedLotBuyButton.axaml` (new; do not edit `EcoToolsPanel.axaml`)

**5. Metamob soul sync**
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Metamob/MetamobSync.cs` (new)
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Combat/SoulContents.cs` (new)
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/SoulSyncViewModel.cs` (new)
- `/%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Views/SoulSyncView.axaml` (new)

<results>
<files>
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/ItemCraftPlannerViewModel.cs - offline plan and simulate only
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/ItemCraftPlanner/ItemCraftPlanner.cs - plan is explicitly not a receipt
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.Craft.cs - existing workshop craft sequence
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Resources/GatherScript.cs - route script with no bank
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Resources.cs - saves the gather script
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/BotsViewModel.Expedition.cs - dungeon budget ignores the chest
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Expedition/ExpeditionBudget.cs - pure key budget with bag and chest slots
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Deals.cs - deals are display and copy only
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/HdvTradeLogic.cs - existing budgeted buy runner
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Combat/SoulCapture.cs - stone power only, no contents
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.JobXp.cs - offline job plan and copy
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Fm.cs - manual FM session entry
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/BidHouseStore.cs - in-memory prices and sale totals
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/TreasureViewModel.cs - auto-hunt starts without panel Stop
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/TreasureViewModel.Fouduglen.cs - Fouduglen likewise defers Stop to the Lua console
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/ArchMonsterLog.cs - sightings with map and cell
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Alerts.cs - Discord alerts, no buy
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/LaunchService.Crush.cs - crushes the whole selection
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Eco/CrushTrialPolicy.cs - one-item trial before the rest
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/Services/Eco/Wealth.cs - pure valuation
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/GivreClone.App/ViewModels/EcoToolsViewModel.Notebook.cs - copy coordinates only
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/docs/codex/DOFUS_GITHUB_ADDITIONAL_20261031.md - Metamob, production, and price-upload references
- /%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/docs/ATELIER_ECO.md - eco tools are read-only and the gather script has no bank
</files>

<answer>
The highest-value missing workflows are the ones whose calculation already exists and whose game action also already exists, but which have no Run/Stop button between them. Build the production runner, the banking gather loop, dungeon key supply, the deal buyer, and Metamob sync first, on the disjoint file sets above. Keep job XP behind the production runner. Do not add an energy gate, and do not reopen in-game FM advice, breeding, bestiary quantity farming, or the other items already on the owner's list.
</answer>

<next_steps>
Start the five parallel agents on the file sets above. After the production runner has a public Run/Stop API, implement job XP by editing only EcoToolsViewModel.JobXp.cs. Queue the remaining panel buttons so EcoToolsPanel.axaml and BotsView.axaml each have one editor at a time.
</next_steps>
</results>