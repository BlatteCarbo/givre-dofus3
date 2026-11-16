# GivreActive reuse study

Read-only source comparison, November 2, 2026. No measured performance or
profitability improvement is claimed.

Old root:
`%USERPROFILE%/Downloads/givre-GivreActive/Videos/GIVRE/GivreActive`.
Current paths below are relative to GivreClone.

## Ranked opportunities

1. **Preflight repricing tax before dispatch.**
   Old `EasyBot/CommerceIntelligence/DocraftSellPolicy.vb:386-419`
   admits the proposed tax against both per-action and remaining cumulative
   ceilings. Current `ViewModels/HdvViewModel.cs:1006-1020` checks already-spent
   tax before sending the next action. Include the next action's tax in the
   preflight and retain receipt-based accounting. Preserve current zero means
   unlimited. An asking-price reduction is not a realized trading loss.

2. **Explicit missing-reference policy for anti-bait repricing.**
   Old `DocraftSellPolicy.vb:300-304,330-348` rejects missing independent
   references and targets below its configurable percentage.
   Current `ViewModels/HdvViewModel.Bulk.cs:405-419` already has an independent
   average-price floor, fixed at 50%, with a one-kama fallback if unknown.
   Add an explicit strict mode rather than claiming anti-bait is absent.
   Keep competitor ask and independent reference distinct.

3. **Use the newer yield-aware planner for Lua crafting.**
   Old `EasyBot/CommerceIntelligence/DocraftRecipePlanner.vb:88-124`
   accounts for output yield and carries shared-intermediate surplus.
   Current `Services/LotM/CraftPlanner.cs:25-44` and `GameDb.cs:12`
   lack output yield; Lua exposes this path through
   `LotMLuaModule.Craft.cs:19`.
   Current `Services/ItemCraftPlanner/ItemCraftPlanner.cs:411-475`
   already handles verified yields and reusable surplus. Adapt the Lua path
   onto that implementation instead of duplicating the VB algorithm.
   Preserve the distinction between additional production and a target stock.

4. **Optional rune-count-based forge ordering.**
   Old `EasyBot/ForgeIntelligence/ExoFastForgePlanner.vb:163-190`
   breaks priority ties by estimated rune count and supports tier windows.
   Current `Services/Eco/ForgeEngine.cs:35-45` uses raw gap, while
   `ViewModels/SmithMagicViewModel.cs:360-369` uses proportional deficit.
   Share one explicit ordering policy between advice and execution.
   Preserve `Services/Eco/RuneCatalog.cs:60-67` no-overshoot behavior.
   This heuristic is not a success-probability or optimal EXO model.

5. **Acknowledged PA/PM EXO alternation.**
   Old `ExoFastQueuePlanner.vb:93-124` advances only after a confirmed rune.
   Current `SmithMagicViewModel.cs:300-315` already has an item queue;
   `Services/LaunchService.Forge.cs:112-134` validates forge results.
   Add an opt-in policy scoped to session and exact item UID, advancing from
   the acknowledged result path, never on send or timeout.
   No improved EXO success rate is established by the old offline planner.

6. **Explicit rune stock packs.**
   Old `ExoFastRunePackPlanner.vb:23-58` models stock multiplier, individual
   ceilings, existing stock, reserve and surplus policy.
   Current `Services/Eco/RunePurchasePlan.cs:15,46-80` supports 100/10/1 lots
   and doubles the minimum rune count.
   Make the multiplier and reserve explicit while retaining current purchase
   receipts. Do not enable 1,000-unit lots without current market evidence.
   A provisioning multiplier is not an estimate of random failure rates.

7. **Treasure-provider provenance and conflict handling.**
   Old
   `EasyBot/PacketAnalyser/Function/MOVE/ScriptLUA/PluginsScript/treasure.vb:330-395`
   queries two providers and reconciles results, choosing the nearer answer
   on conflict.
   Current `Services/Treasure/TreasureClueService.cs:16-54`
   uses one cached provider without stored source/time provenance.
   Keep cache-first behavior; retain source, observation timestamp and conflict
   status if adding a separately verified secondary provider.
   Old endpoints/HTML contracts are unverified; nearer does not mean correct.

8. **Bounded typed combat telemetry and action vectors.**
   Old
   `EasyBot/PacketAnalyser/Function/Combat/CombatTelemetryFeed.vb:33-50,101-110`
   uses a bounded 96-event ring with action identity, resource state, outcome
   and latency. Old `Function/Mapviewer/DrawMapDofus.vb:3122-3137` renders
   recent action vectors.
   Current `Controls/IsoMapControl.cs:503-512,580-590` already renders marks,
   movement and fighters. `Services/Combat/CombatJournalService.cs:34-43`
   ingests formatted transport messages.
   Add typed requested/confirmed events and monotonic dispatch-to-confirmation
   measurements, feeding optional numbered vectors and diagnostics.
   Reuse the data contract, not WinForms drawing or old packet sequences.

## Tactical and class limits

Old `tools/MapRenderer/extract.py:44-50` and
`EasyBot/PacketAnalyser/Mapalgo/D2Pparsing.vb:109` corroborate asset names
`map_<id>`. They do not prove the current runtime scene object's name.

The separate class audit found no complete reusable Sram, Roublard or Zobal AI.
The old target-mask evaluator in
`EasyBot/PacketAnalyser/Function/Combat/IA-BAsic/PacketsFight.vb:333`
is useful, particularly its self/ally/summon distinctions and explicit unknown
cases. Legacy state conversion is incomplete and must not be treated as
authoritative current protocol decoding.

## Version boundary

The old `AGENTS.md:3` identifies client 3.7.7.6. Current tactical metadata
targets 3.7.12.12. Reuse pure planning ideas and independently validated data,
not old wire IDs, native offsets, waits or provider contracts.

This study did not change application behavior, run the old application,
purchase anything or access credentials.
