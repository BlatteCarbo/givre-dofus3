# Treasure panel reconstruction

This is new source implementing a missing panel against GivreWork's existing decoded server snapshots and guarded send methods. It is not recovered original native source.

Comparison input: `Downloads/GivreClone/analysis/GivreCloneBot.strings.ascii.jsonl` exposes the names `TreasureViewModel`, `Status`, `Hunting`, `CheckPointText`, `RetriesText`, `HasSteps`, `Steps`, `SessionText`, `Running` and step-row display names. Names establish a UI surface, not the original algorithm. GivreWork already contains the transport/parser in `LaunchService.Treasure.cs`; the reconstruction uses that implementation instead of introducing another protocol decoder.

Implemented: selected-character snapshot display, known directional arrows with explicit unknown values, POI/NPC identifiers, raw flag states and map identifiers, manual flag/remove/dig/give-up commands using existing permissions, local pending-send state, matching newer receipt handling, 15-second receipt timeout, and clearing state on bot/hunt boundaries. A successful local send is never displayed as a successful server action. Result codes are displayed without guessing enum meanings.

Not recovered: automatic clue resolver, navigation strategy, original numeric enum meanings, legendary-hunt/fragment selection workflow, or original session automation. No endpoint or algorithm was fabricated for these gaps.

Offline validation: `dotnet run --project tools/TreasureSmoke/TreasureSmoke.csproj`. The harness links the production view-model and tracker sources, replacing only the transport service with deterministic fixtures. It checks selection isolation, stale/other-kind receipts, failed versus accepted sends, pending timeout, unknown directions, clearing completed hunts, and newer snapshots after completion. It sends no packets and does not launch a game client. An app build additionally validates Avalonia bindings.
