# Account resumption on the corrected build

## Subsequent overnight checkpoint

On November 6, the standing transport watch confirmed Bastionbot harvesting
Ginseng twice (nine each at 00:24:16 and 00:24:21), then Frostiz (one each at
00:26:20, 00:26:52 and 00:27:17), under PID 1073741827. These are actual
harvest receipts, not bank-deposit, sales, net-profit or combat acceptance.
Sylvanbot's later protector defeat and script stop are recorded in
`sylvanbot-protector-defeat-20261106.md`; the earlier victory below does not
establish continued safe mining after that loss.

Bastionbot subsequently won a harvest protector at 00:29:30 under the same PID:
server bilan `issue=2`, 703524 XP, loot `16528x1`, followed by the script's
`PROTECTEUR victoire=true defaites=0`. Actual harvesting resumed at 00:29:36:
nine Pin, fifteen Tremble, fifteen Ginseng and three Frostiz were confirmed
through 00:32:37. This validates one protector victory and post-combat script
resumption on the existing live build. It does not validate the pending
movement-continuation patch, bank completion or net profit.

The parent then inspected the complete journal summary for this protector:
`2026-11-06/002922_pid1073741827_victoire.log`, 8.2 seconds, twelve turns,
ten injected casts, twelve attempted moves, zero recorded technical problems.
Eight turns had no cast, and observed player life reached 170/2710 (6%) in
round twelve. Therefore the win is not evidence of safe or efficient repeated
protector farming. The cast/move totals do not by themselves prove accepted
damage/movement; challenges were not validated by this fight either.

The standing watch subsequently confirmed another Frostiz at 00:37:54 and
ten Ginseng at 00:40:46 under PID 1073741827. Harvesting therefore continued
after the inspected protector victory. These receipts add no bank, sale or
new combat result.

Another Bastionbot protector ended in victory at 00:54:09, 703524 XP and loot
`11113x1`, followed by `PROTECTEUR victoire=true defaites=0`. Parent inspection
of `2026-11-06/005404_pid1073741827_victoire.log` records 5.7 seconds, four
turns, ten injected casts and ten attempted moves, with no cast-free turn or
recorded technical problem. Final reported player life was 1945/2944 (66%).
The starting nearest enemy was two cells away, compared with fifteen in the
earlier twelve-turn fight; these are different encounter geometries, not a
before/after correction comparison. A challenge failed. Harvest resumed with
nine Ginseng at 00:56:15 and nine Pin at 00:56:20. The continuation patch is
still not deployed, and bank/sales remain unvalidated.

The next inspected protector, `2026-11-06/011205_pid1073741827_victoire.log`,
ended at 01:12:12 with another 703524 XP and loot `16528x1`; the script
confirmed victory with zero defeats. It lasted 7.2 seconds and nine turns,
with ten injected casts and twelve attempted moves. Five turns had no cast;
final reported life was 988/2828 (35%), with one failed challenge and zero
recorded technical problems. This additional win does not resolve the
movement-only decisions seen on the still-old live build. No subsequent
harvest receipt was present in this result event alone.

The parent resumed the requested accounts using process 35876 and control pipe
`Givre-live-parent-20261105`. This is Socket-mode evidence, not visual Turbo
acceptance. Transport: `transport-2026-11-05_23-19-57.log`.

## Sylvanbot, Mikhal

The inspected `Mineur 1-200 - Foragerbot.lua` resumed at 23:24:12 with mining
level 148. Several initial maps had no harvest. At 23:28:50.120 the console
confirmed seven Bauxite harvested. A subsequent state read showed the script
still running, full 2429 HP and 6494/10356 pods. The fresh corrected Poutch
fight is documented separately in `sylvanbot-miner-live-20261105.md`. No mining
protector victory on this resumed route has been observed yet; level 200,
bank completion and profitability are not validated.

Further confirmed harvests: 17 Fer at 23:30:38, ten Manganese at 23:32:39
and nine Argent at 23:32:43. Seven Edelweiss at 23:32:35 are a different
profession's resource, not mining XP evidence. The inspected script explicitly
uses unfiltered `gatherAll()` for all accessible professions along its mine
route. No filter was silently added and the mining level remains 148 in the
observed route messages.

The resumed mining route subsequently won a real harvest protector:
`20261105-233707-1073741825`, victory at 23:38:19.999, 72.7 seconds,
eight turns, eighteen casts, thirteen moves, zero technical problems,
166086 XP. The script confirmed `PROTECTEUR victoire=true defaites=0` and
continued harvesting Cuivre (18), Etain (5), Kobalte (5), Fer (13) and Argent
(9). This extends the Poutch evidence to a mobile harvest protector and actual
post-combat route resumption. The journal also reports a low of 18% HP and a
failed challenge, so this is not evidence of a safe full-HP combat or challenge
compliance. A later read confirmed the script active at 1782/2429 HP and
7631/10356 pods. Bank completion and level 200 remain unvalidated.

## Shadowbot, Dakal

Only the assigned Shadowbot account was connected on Dakal. A bounded Plantala
expedition used 23 cleaned maps, groups of 1-3 monsters, three fights maximum,
20 minutes maximum, 85% pods stop and no continuation after death.

All three observed fights won:

| Journal | Seconds | Turns | Casts | Moves | XP |
| --- | ---: | ---: | ---: | ---: | ---: |
| 20261105-232538-1073741829 | 5.7 | 2 | 5 | 3 | 170929 |
| 20261105-232607-1073741829 | 7.7 | 8 | 20 | 4 | 692965 |
| 20261105-232642-1073741829 | 11.4 | 9 | 25 | 14 | 271468 |

Each journal reported zero technical problems. The second fight reached 4%
HP and the third 25%, so these wins do not establish safe unrestricted farming
of larger groups. Challenges were disabled; reported failed challenges are not
evidence of challenge-aware planning. The exact previous one-HP stall has not
been reproduced live after the fix. Other exterior Pandala sectors remain
unvalidated.

The bounded expedition stopped normally with `Expédition : 3 combats atteints`
and `script terminé.` Shadowbot was outside combat at map 205520901. Before moving
to Akwadala (941), the parent started `regenerate(95, 900)` and armed a watch
for `SHADOWBOT_PANDALA_PREP_READY=`. Observed recovery progressed from 1599/2426
to 1661/2426 HP; the next expedition must not replace this running preparation
until its result and current state have been checked. The next sector will use
single-monster groups rather than the previous maximum of three.

At 23:35:59.580 the server-observed recovery reached 2305/2426 HP (95%);
`SHADOWBOT_PANDALA_PREP_READY=true` followed. The one-shot preparation then reported
`le script doit définir une fonction move.` This is a missing callback in the
diagnostic script, not a failed regeneration. At 23:36:06 the parent started
Akwadala with `maxFights=1`, `minMonsters=maxMonsters=1`, 20 minutes, 85% pods
and stop after death. The accepted route contains 33 cleaned maps; actual
travel from Plantala was observed. No Akwadala combat result is recorded yet.

Akwadala subsequently won against Betto on map 204998915: journal
`20261105-233633-1073741829`, 7.1 seconds, three turns, nine casts, zero moves,
zero technical problems, 147029 XP. The expedition stopped at its one-fight
limit and the script ended. A state read showed 2253/2426 HP (about 93%).
This validates one Betto fight, not every Akwadala species or its complete route.
The parent then started Terrdala (944), 24 cleaned maps, with the same one-fight,
single-monster, 20-minute bounds and stop after death. Its result remains pending.

Terrdala exposed a different live failure. Shadowbot engaged Ishigro Pake on map
205258753 at 23:37:14.976. At round 33, the trace still showed the enemy at
2500/2500 HP while the planner valued spells 12902 and 12917 as 603.4 damage.
The server confirmed AP consumption and forced movement after these casts;
packet injection alone therefore cannot establish damage. Round 59 still
contained repeated casts and the character's maximum life had fallen sharply.
This is not the previous one-HP empty-plan case. The precise immunity,
resistance or other monster mechanic has not yet been established.

The parent disabled AutoFight, stopped the script (which can hold an override),
and disconnected only Shadowbot. A subsequent state read confirmed `offline`, PID
zero, no script and no active fight in the local session. This does not prove
the server fight ended. No Terrdala victory or safe farming acceptance is
claimed. Sylvanbot and Bastionbot were left running. Extension to further sectors is
deferred until this ineffective-attack case is diagnosed.

The transport later recorded Shadowbot's result at 23:38:10.341 as
`issue=0 xp=0 butin=[]`. The parent subsequently read lines 26172-26202:
actor 39467549025 was killed at 23:38:10.128, FightEndEvent arrived at
23:38:10.332, and the authoritative character sheet returned 1/2426 HP.
Together these establish defeat and a completed server fight before connection
closure; that conclusion does not rely on the numeric result alone.

The read-only investigation reconstructed 73 planner snapshots and identified
Ishigro Pake as template 6220, grade 1. Its HP remained 2500/2500, but shield
values changed, including 0, 366 and 122. The local 3.7.12.12 catalog supplies a
specific Jardin Zen invulnerability pathway through state 3505, not ordinary
100% elemental resistance. This is a candidate, not proof of active state
membership during the fight. Retained packet summaries lack raw state and
damage payloads, so damage prevention, healing and life-tracking errors cannot
yet be distinguished. No species blacklist or inferred live immunity was added.

The parent subsequently enabled the existing discovery capture through the
settings UI of process 35876. Persisted settings confirmed `PacketDump=true`
and `PacketDumpAll=false`: full unrelated traffic capture was explicitly
disabled. The existing 15-minute automatic cutoff remains in force. A log
subscription was registered before activation for raw state/action receipts
and the cutoff. No new Ishigro fight was launched; Shadowbot was confirmed offline.
This capture can supply comparison evidence from the ongoing harvests, not
retroactively prove which effects were active in the lost Terrdala fight.

## Bastionbot, Mikhal

The high-end Frigost script remained active without an unnecessary restart.
It harvested seven Tremble at 23:25:32, won a protector at 23:25:45 (journal
`20261105-232538-1073741827`: 7.6 seconds, six turns, nine casts, ten moves,
703524 XP), reported `PROTECTEUR victoire=true defaites=0`, and then harvested
seven Tremble at 23:26:08 and eleven Ginseng at 23:26:12. The protector journal
also reported two turns without a spell and one failed challenge. A subsequent
state read showed full 3063 HP, 9366/15460 pods and the script still running.
Bank completion, sales and net profit have not been validated.

A later profile read corrected the assumed challenge state for Bastionbot:
`AutoChallenge` and `ChallengeRules` were both true, and placement was `Far`.
The journal explicitly applied LigneDeMire on turn 1 and started 15 cells away
from the protector. Thus the two empty casting turns cannot be attributed to
the resolver exception, and the script's challenge setting was not proof that
the engine profile was disabled. The parent set both profile flags false and
placement to `LegacyByStyle`; the control response confirmed these applied
values but did not verify persistence. A subsequent protector is still needed
to evaluate the placement change. No production code change was made here.

## Parallel review availability

All three requested Argon children ended with provider idle timeouts and zero
tool calls. They produced no accepted implementation or independent review.
The parent performed the resolver diagnosis, regression tests and live checks.
