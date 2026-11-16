# Root causes & fixes for Shadowbot 5-min bans (Terrdala Teapot PA0 + 46-turn Geoglyph Turbo), 2s 1-turn defeat (50% HP entry + IdCurLifeDelta 97), and 50% regen freeze (2026-11-07)

---
description: Root causes & fixes for Shadowbot 5-min bans (Terrdala Teapot PA0 + 46-turn Geoglyph Turbo), 2s 1-turn defeat (50% HP entry + IdCurLifeDelta 97), and 50% regen freeze (2026-11-07)
---
# Shadowbot live incidents & fixes — 2026-11-06 / 2026-11-07

## 1. Two 5-minute temporary bans at `FightEndEvent`
- **23:38:30 (`ban_end=22:43:30Z`)**: Terrdala fight against 2 Parashukouï + 1 Lolojiki (`205391873`). Parashukouï casts `Transpompoko` (swap + `-100 PA` Teapot -> `char 1 = -89`, `PA0`); Lolojiki's damage every 3rd turn breaks Teapot, resetting consecutive `_idleTurns` (`2, 0, 2, 0... < 6`), so 10/15 turns sent `FightTurnFinishRequest` in 2–10 ms over 18.3s -> server closed socket at `FightEndEvent` with a 5-min ban.
  - **Fix**: Added cumulative `_totalIdleTurnsInFight` (`TotalIdleTurnsBeforeGiveUp = 8`) in `LaunchService.Combat.Turn.cs` + reset in `LaunchService.Packets.cs`.
- **00:11:40 (`ban_end=23:16:40Z`)**: Terrdala fight (`205259264`) reached **46 turns in ~45s** (`seq fin=3370`, 18,382 frames/min in Turbo) because Tanuki Geoglyphs grant invulnerability until displaced off the glyph; planner spammed non-displacing attacks for 45 turns until `12909` displaced/killed on turn 46 -> server closed socket at `FightEndEvent` with a 5-min ban.
  - **Operational rule**: Solo Sram 110/120 must avoid Terrdala Tanukis (Geoglyph invulnerability + Parashukouï `-100 PA`); farm Plantala / Akwadala 1–2 monsters instead.

## 2. "Combat perdu en 2 secondes, 1 tour" (`23:49:40`)
- Not a surrender: Shadowbot cast 3 spells (`12917`, `12917`, `12902` = 11 AP) on turn 1.
- Entered fight against 3 Akwadala monsters (10,000 total HP) with **1221/2431 HP (50%)** because `RegenerationEnabled` was `false`; took exactly 1221 damage across monster turns in 2.8s (Turbo) before turn 2.
- **Bug found & fixed (`FightStatsReader.cs:100`)**: `ReadShowFighter` set `Life = maxLife` (`2431`), ignoring `IdCurLifeDelta = 97` (`-1210` in wire characteristics -> `1221`). Now clamps `maxLife + m.GetValueOrDefault(IdCurLifeDelta)` to `[1, maxLife]`.

## 3. "Il se croit toujours à 50%" regeneration bug (`00:34:52`)
- Log showed: `vie: régénération: 50 % (2431/2431 PV)` -> `délai dépassé (15 s) à 50 %` -> `reason=regeneration_not_ready` -> script stopped.
- Root cause: Dofus 3 Unity does NOT emit `UpdateLifePointsEvent` during passive out-of-combat regeneration (`LifePointsRegenBeginEvent` 500 ms/HP), nor on standing up once already at 2431/2431. `GivreLuaHost.Regeneration.cs` and `GivreLuaHost.LotF.cs` gated on `AuthoritativeLifePercent` (frozen at 50%) while `SelfLife` was `2431/2431` (100%).
- **Fix**: When `AuthoritativeLifePercent` is non-null (a post-fight server HP packet WAS received, not stale), `PrepareOperatorRegeneration` and `Regenerate`'s `Pct()` use `SelfLifePercent` (authoritative base + server-announced regen rate), while food healing still requires `LifeSeq` + `SelfLifeAuthoritative` increase. Also wired `launch.SetRegenerationOptions` in `BotsViewModel.ControlGame.cs` `set_bot_settings`.

## Follow-up deployment and live evidence — 2026-11-07

- The assistant reported that the fixes were published and the live app restarted (new process PID 11380), with Sylvanbot, Bastionbot, and Shadowbot reconnected. Reported automated results were 534/534 main tests and 100/100 UI tests. These are transcript-reported results, not independently rechecked in this memory run.
- The new live log reportedly showed Bastionbot regenerating at 69% (2138/3073 HP) and Shadowbot at 63% (1539/2431 HP), demonstrating estimated passive regeneration progressing past the previously frozen 50% reading on the real client. Later snapshots reportedly showed them at 93% and 92%, respectively. This supports the regen correction specifically; it does not establish full combat/planner correctness or long-run stability.
- Sylvanbot's `Mineur-1-200.lua` reportedly hit its 180-second regeneration wait limit at 64% and then continued, after which the script was found stopped. Root cause and any corrective action were not established in the supplied transcript. Review the miner's wait/stop behavior separately; avoid assuming that a timeout message means the script safely continued.
- Transcript continuation proposed further Wave 3 work (streamer-privacy masking and combat mechanics) but ended before implementation or verification; treat those as open proposals, not completed work.
