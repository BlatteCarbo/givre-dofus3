# Smithmagic reconstruction checks

Run `dotnet run --project tools/RecoveredSmithMagicHarness` from the repository root.
This dependency-free harness compiles the real planner source and checks tier
thresholds, rune stock/effect matching, rune caps, budget overflow and fallback.

Evidence: `Downloads/GivreClone/analysis/GivreCloneBot.strings.ascii.jsonl`
contains the recovered SmithMagicViewModel binding names at RVAs
`0x22c6f8a`–`0x22cb82d`. They include queue, rune inventory, custom targets,
PaFrom/RaFrom, perfect roll, maximum runes and kamas, smaller-rune fallback.
The UTF16 companion contains missing-rune and missing-workshop stop messages
at `0x5e778cc` and `0x5e77af4` and a no-item condition at `0x5e779bc`.

These strings establish feature surface, not the original algorithm or defaults.
The planner and ViewModel are newly authored compatible local behavior. Rune
IDs, effect IDs, thresholds, stocks and prices must be supplied by a verified
caller. Rune thresholds choose a tier from the current stat; no fuse outcome,
weights, success probability or workshop navigation is inferred. Default local
limits are design choices. MaxKamas=0 permits zero-price observations only.
The ViewModel deliberately reports unavailable gameplay transport and never
reports a successful fuse, moves an item, buys a rune or starts a game script.
Snapshot acquisition, transport and persisted character settings remain missing.
