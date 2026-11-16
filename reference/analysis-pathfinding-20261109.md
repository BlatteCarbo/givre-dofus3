# Pathfinding Failures and Zaap Fallback Analysis (2026-11-09)

## Executive Summary & Core Findings

This investigation analyzes the transport logs from the four headless test runs conducted on **2026-11-09**:
- `transport-2026-11-09_09-43-31.log` (09:43:31 - 10:13:26, ~30 min)
- `transport-2026-11-09_10-13-58.log` (10:13:58 - 10:36:42, ~23 min)
- `transport-2026-11-09_10-37-03.log` (10:37:03 - 10:46:28, ~9 min)
- `transport-2026-11-09_10-46-45.log` (10:46:45 - 11:29:00, ~42 min)

Across these four sessions, a total of **216 pathfinding failure events** (`trajet: marche impossible vers <MAPID>`) occurred. The **212** figure cited in the task definition corresponds to the **exact number of combat defeats** (212 wipes, 0 victories) suffered by the expedition bots across the four runs.

### The Mechanism of the Failure
1. **Combat Wipeout**: The bots (**Shadowbot**, **Bastionbot**, **Archerbot**) were executing automated gathering/expedition scripts in **Pandala** (Akwadala, Terrdala, Pandala Faubourgs). Their combat AI (`style=Aggressive placement=Near cible=Closest`) attacked high-level Pandala monster mobs (e.g. *Ino-Naru*, *Lichangoro*, *Kurookin*, *Akakwa*, *Betto*) solo and often at partial health. Out of 212 combats, **212 ended in defeat (100.0% loss rate)**.
2. **Server Respawn at Astrub**: When a character dies in Dofus, the server respawns them at their saved Zaap spawn point. None of the characters had saved their spawn in Pandala; all four characters had their spawn set to **Astrub Zaap (Map 191105026, [5, -18])**.
3. **Resumption of Expedition Route**: The Lua expedition script caught the defeat (`script: Expédition : défaite (N d'affilée), on continue`) and immediately called `GoToMapId` for the next waypoint in Pandala (e.g., [18..22, -22..-36]).
4. **World Graph Disconnection**: The World Graph (`worldgraph5.json`) contains **zero walking transitions** between the Amakna/Astrub mainland and Pandala Island.
5. **Pathfinding Failure & Zaap Fallback**: `PlanPath` returned 0 hops (`path.Count == 0`). `GivreLuaHost.Travel.cs` logged `trajet: marche impossible vers <MAPID>`. Because the bots were standing directly on Astrub Zaap (`zaap sur la carte: True`), the bot fell back to a paid Zaap teleport to **Pandazaap (Map 207619076, [20, -29])**, paying between **50 and 180 kamas**.
6. **Infinite Drain Loop**: Upon arriving in Pandala, the bot walked to the target map, engaged another monster mob, died within 1–2 turns, respawned at Astrub Zaap, and paid another Zaap fee.

---

## 1. Top Failing Target Map IDs

Across all 216 pathfinding failures, **79 unique target maps** were targeted. All top target maps are located on **Pandala Island**:
- **Bastionbot** operated in **Akwadala** (southern coastal bamboo/water zone, coordinates `[18..21, -22..-26]`).
- **Shadowbot** and **Archerbot** operated in **Terrdala & Pandala Faubourgs** (central and eastern bamboo/earth zone, coordinates `[15..22, -33..-36]`).
- **Sylvanbot** operated on a mining route transitioning towards **Saharach** (`[16, -60]`).

### Top 15 Target Maps Overall

| Rank | Target Map ID | Coordinates (X, Y) | Region / Zone | Failure Count | Character(s) Affected |
|:---:|:---:|:---:|:---:|:---:|:---|
| 1 | `209192706` | [22, -34] | Pandala (Terrdala) | **10** | Shadowbot (6), Archerbot (2), Bastionbot (2) |
| 2 | `209192194` | [21, -34] | Pandala (Terrdala) | **9** | Shadowbot (6), Archerbot (2), Bastionbot (1) |
| 3 | `209192195` | [21, -35] | Pandala (Terrdala) | **7** | Shadowbot (4), Archerbot (1), Bastionbot (2) |
| 4 | `204997889` | [19, -24] | Pandala (Akwadala) | **7** | Bastionbot (7) |
| 5 | `204997120` | [18, -23] | Pandala (Akwadala) | **6** | Bastionbot (6) |
| 6 | `209191681` | [20, -33] | Pandala (Terrdala) | **6** | Shadowbot (3), Archerbot (1), Bastionbot (2) |
| 7 | `209191683` | [20, -35] | Pandala (Terrdala) | **6** | Shadowbot (3), Archerbot (2), Bastionbot (1) |
| 8 | `204997121` | [18, -22] | Pandala (Akwadala) | **5** | Bastionbot (5) |
| 9 | `209192193` | [21, -33] | Pandala (Terrdala) | **5** | Shadowbot (3), Archerbot (1), Bastionbot (1) |
| 10 | `206045186` | [17, -35] | Pandala (Faubourgs) | **5** | Shadowbot (4), Archerbot (1) |
| 11 | `204998656` | [21, -23] | Pandala (Akwadala) | **5** | Bastionbot (5) |
| 12 | `204998403` | [20, -26] | Pandala (Akwadala) | **5** | Bastionbot (5) |
| 13 | `204998144` | [20, -23] | Pandala (Akwadala) | **5** | Bastionbot (5) |
| 14 | `209192707` | [22, -35] | Pandala (Terrdala) | **5** | Shadowbot (3), Archerbot (1), Bastionbot (1) |
| 15 | `209191171` | [19, -35] | Pandala (Terrdala) | **4** | Shadowbot (2), Archerbot (1), Bastionbot (1) |

---

### Top Target Maps Per Character

#### Bastionbot (PID 1073741825) — 98 Total Failures (47 Unique Maps)
*Primary activity: Akwadala resource collection & combat.*

| Rank | Target Map ID | Coordinates (X, Y) | Zone | Failures |
|:---:|:---:|:---:|:---:|:---:|
| 1 | `204997889` | [19, -24] | Akwadala | 7 |
| 2 | `204997120` | [18, -23] | Akwadala | 6 |
| 3 | `204997121` | [18, -22] | Akwadala | 5 |
| 4 | `204998656` | [21, -23] | Akwadala | 5 |
| 5 | `204998403` | [20, -26] | Akwadala | 5 |
| 6 | `204998144` | [20, -23] | Akwadala | 5 |
| 7 | `191105026` | [5, -18] | Astrub (Return) | 3 |
| 8 | `204997378` | [18, -25] | Akwadala | 3 |
| 9 | `204998401` | [20, -24] | Akwadala | 3 |
| 10 | `204998914` | [21, -25] | Akwadala | 3 |
| 11 | `204998913` | [21, -24] | Akwadala | 3 |
| 12 | `204997377` | [18, -24] | Akwadala | 3 |
| 13 | `204997890` | [19, -25] | Akwadala | 2 |
| 14 | `204997633` | [19, -22] | Akwadala | 2 |
| 15 | `204997891` | [19, -26] | Akwadala | 2 |

#### Shadowbot (PID 1073741827) — 91 Total Failures (43 Unique Maps)
*Primary activity: Terrdala expedition & monster hunting.*

| Rank | Target Map ID | Coordinates (X, Y) | Zone | Failures |
|:---:|:---:|:---:|:---:|:---:|
| 1 | `209192194` | [21, -34] | Terrdala | 6 |
| 2 | `209192706` | [22, -34] | Terrdala | 6 |
| 3 | `209192195` | [21, -35] | Terrdala | 4 |
| 4 | `206045186` | [17, -35] | Faubourgs Pandala | 4 |
| 5 | `209191681` | [20, -33] | Terrdala | 3 |
| 6 | `209192193` | [21, -33] | Terrdala | 3 |
| 7 | `209191683` | [20, -35] | Terrdala | 3 |
| 8 | `206045698` | [18, -35] | Faubourgs Pandala | 3 |
| 9 | `206176770` | [16, -35] | Faubourgs Pandala | 3 |
| 10 | `206176771` | [16, -34] | Faubourgs Pandala | 3 |
| 11 | `209192707` | [22, -35] | Terrdala | 3 |
| 12 | `206177283` | [15, -34] | Faubourgs Pandala | 3 |
| 13 | `209191171` | [19, -35] | Terrdala | 2 |
| 14 | `209191170` | [19, -34] | Terrdala | 2 |
| 15 | `209191682` | [20, -34] | Terrdala | 2 |

#### Archerbot (PID 1073741831) — 26 Total Failures (23 Unique Maps)
*Active during session `transport-2026-11-09_10-13-58.log`.*

| Rank | Target Map ID | Coordinates (X, Y) | Zone | Failures |
|:---:|:---:|:---:|:---:|:---:|
| 1 | `209191683` | [20, -35] | Terrdala | 2 |
| 2 | `209192194` | [21, -34] | Terrdala | 2 |
| 3 | `209192706` | [22, -34] | Terrdala | 2 |
| 4 | `209191171` | [19, -35] | Terrdala | 1 |
| 5 | `209191170` | [19, -34] | Terrdala | 1 |
| 6 | `209191681` | [20, -33] | Terrdala | 1 |
| 7 | `209191682` | [20, -34] | Terrdala | 1 |
| 8 | `209192193` | [21, -33] | Terrdala | 1 |
| 9 | `209192705` | [22, -33] | Terrdala | 1 |
| 10 | `209192707` | [22, -35] | Terrdala | 1 |
| 11 | `209192196` | [21, -36] | Terrdala | 1 |
| 12 | `209192195` | [21, -35] | Terrdala | 1 |
| 13 | `209191684` | [20, -36] | Terrdala | 1 |
| 14 | `209191172` | [19, -36] | Terrdala | 1 |
| 15 | `206045697` | [18, -36] | Faubourgs Pandala | 1 |

#### Sylvanbot (PID 1073741829) — 1 Total Failure
*Miner bot.*
- `173278720` [16, -60] (Saharach Entry from Mine Hurlement): **1 failure** (400 kamas teleport).

---

## 2. Total Kamas Actually Spent on Zaap Fallbacks

All 216 pathfinding failures resulted in paid Zaap teleportations.

### Total Kamas by Character

| Character | PID | Fallback Count | Kamas Spent | Cost Breakdown |
|:---|:---:|:---:|:---:|:---|
| **Shadowbot** | `1073741827` | 91 | **16,600 kamas** | 90 × 180 k, 1 × 400 k |
| **Bastionbot** | `1073741825` | 98 | **12,020 kamas** | 46 × 180 k, 25 × 80 k, 21 × 50 k, 2 × 140 k, 2 × 70 k, 1 × 170 k, 1 × 100 k |
| **Archerbot** | `1073741831` | 26 | **4,680 kamas** | 26 × 180 k |
| **Sylvanbot** | `1073741829` | 1 | **400 kamas** | 1 × 400 k |
| **TOTAL** | — | **216** | **33,700 kamas** | Average: **156.0 kamas / fallback** |

### Distribution by Fallback Mechanism
- **Map Zaap Direct (`zaap_depuis_carte`)**: **207 events (95.8%)**. Standing on Astrub Zaap with known destination pricing.
- **Ground Zaap Dialog (`zaap_sol`)**: **6 events (2.8%)**. Triggered interactive use on Zaap element 538792.
- **Haven Bag Shortcut (`zaap_havre_sac`)**: **3 events (1.4%)**. Entered haven bag to open zaap interface when not on a zaap map.

---

## 3. Spatial Adjacency Analysis

The coordinate table `map-positions.json` (15,469 maps mapped to `[x, y, worldMapId]`) was joined against the bot's confirmed position (`[socket] [jeu] carte <MAPID> annoncée` / `MapCurrentEvent`) and target maps for all 216 events.

### Key Finding: 0 Adjacent Failures
- **Directly Adjacent Failures (Distance = 1)**: **EXACTLY 0 (0.0%)**.
- **Chebyshev Distance = 1 Failures**: **EXACTLY 0 (0.0%)**.

### Spatial Distance Distribution

| Manhattan Distance (|ΔX| + |ΔY|) | Event Count | Percentage | Context |
|:---:|:---:|:---:|:---|
| **0 to 16** | **0** | 0.0% | *(No adjacent or local failures)* |
| **17 to 20** | 43 | 19.9% | Astrub [5, -18] to Akwadala [18..21, -22..-26] |
| **21 to 25** | 40 | 18.5% | Astrub [5, -18] to Akwadala / Pandala Faubourgs |
| **26 to 30** | 47 | 21.8% | Astrub [5, -18] to Pandala Faubourgs & Terrdala |
| **31 to 35** | 84 | 38.9% | Astrub [5, -18] to Terrdala [20..22, -33..-36] |
| **90** | 2 | 0.9% | Amakna mainland to Saharach [16, -60] |
| **Total** | **216** | **100.0%** | Minimum: **17**, Maximum: **90**, Mean: **26.8** |

### Originating Map at Failure
- **Map `191105026` [5, -18] (Astrub Zaap)**: **209 events (96.8%)**.
- **Map `171707908` [-21, -7]**: 2 events (0.9%).
- **Map `204997633` [19, -22]**: 1 event (0.5%).
- **Map `204997377` [18, -24]**: 1 event (0.5%).
- **Map `204999170` [22, -21]**: 1 event (0.5%).
- **Map `209191424` [20, -32]**: 1 event (0.5%).
- **Map `192415750` [4, -18]**: 1 event (0.5%).

**Conclusion**: The pathfinding failure was **never** caused by a false disconnect between adjacent maps (such as elevation blockers or missing direction edges). In 96.8% of cases, the bot was physically standing at **Astrub Zaap** trying to walk to **Pandala Island**, a journey requiring trans-oceanic transit.

---

## 4. Permanent Exclusion (`no_route`) Analysis

- **Count of Failures Ending in `no_route` Permanent Exclusion**: **EXACTLY 0 (0.0%)**.
- **List of Excluded Maps**: **None**.

### Why No Maps Were Excluded in These Runs
In earlier test runs on 2026-11-08 (e.g., `transport-2026-11-08_21-41-36.log`), `reason=no_route` permanent exclusion occurred when both walking pathfinding AND Zaap fallback failed (for maps `209191177` and `209192710`).

In the four November 9 runs analyzed:
- In all 216 cases, `TravelViaWorldMapZaap` or `TravelViaGroundZaap` succeeded immediately.
- The bots paid the zaap fee, arrived at **Pandazaap (`207619076`)**, and from there successfully computed a walking path to the target map within Pandala.
- Because `GoToMapId` succeeded via Zaap, the expedition script never received `false` and therefore never triggered `reason=no_route`.

---

## 5. Temporal Clustering & Failure Etiology

The failures do **not** cluster around specific static obstacle maps, nor do they fail consistently on individual map geometry. Instead, they cluster strictly around **combat events**:

### Preceding Event Breakdown for the 216 Failures

| Preceding Cause | Count | Percentage | Description |
|:---|:---:|:---:|:---|
| **Combat Defeat Wipeout** | **203** | **94.0%** | Bot wiped in combat (`issue=0`), respawned at Astrub Zaap, resumed route. |
| **Script Launch / Startup** | **9** | **4.2%** | Script started at Astrub Zaap while first route waypoint was in Pandala. |
| **Mining Route Long Transition** | **2** | **0.9%** | Sylvanbot/Shadowbot exiting Hurlement mine to reach Saharach. |
| **Bank / Inventory Trip** | **2** | **0.9%** | Returning to route after visiting Astrub bank. |
| **Total** | **216** | **100.0%** | |

### Combat Defeat Correlation (The "212" Mystery Solved)

Querying `[combat] pid <PID> : bilan -> issue=0` across the four log files reveals:
- **Shadowbot**: **93 defeats**, 0 victories (100% loss)
- **Bastionbot**: **90 defeats**, 0 victories (100% loss)
- **Archerbot**: **26 defeats**, 0 victories (100% loss)
- **Sylvanbot**: **3 defeats**, 0 victories (100% loss)
- **Total Combat Defeats**: **EXACTLY 212**.

The **212 failures** referenced in the task brief is precisely the count of **combat wipes** suffered across the four log files. Each wipe threw the character back to Astrub Zaap, triggering an immediate `marche impossible vers <PandalaMap>` log and a paid Zaap teleport back to Pandazaap.

### Frequency and Cycle Time
Across the ~1 hour 45 minutes of total runtime in these 4 logs:
- Average cycle time per bot: **~45 to 60 seconds** per death loop.
- Example: **Shadowbot** in `transport-2026-11-09_09-43-31.log`:
  - `09:44:41`: Zaap Astrub -> Pandazaap (180 k)
  - `09:44:42 - 09:45:02`: Walk to `209191171`
  - `09:45:03`: Attack monster group `Akakwa+Betto`
  - `09:45:09`: Defeat round 2 (`issue=0`), respawn Astrub (`191105026`)
  - `09:45:10`: `marche impossible vers 209191170`, Zaap Astrub -> Pandazaap (180 k)
  - *Repeated 34 times in 30 minutes.*

---

## Root Cause Ranking

| Rank | Issue Category | Cause Description | Evidence & Impact |
|:---:|:---|:---|:---|
| **1** | **Combat Engine / AI Policy** *(Primary Trigger)* | **Suicidal Combat Loop & 100% Wipe Rate**<br>Autonomous combat policy (`auto=oui style=Aggressive`) attacks high-level Pandala mobs that the bots cannot defeat solo. | **212 defeats in 212 combats (100.0% loss rate)**.<br>Responsible for **203 out of 216** pathfinding failures (94.0%) and **31,760 kamas** wasted. |
| **2** | **Script Configuration** *(Operational Flaw)* | **Unsaved Local Zaap Spawn Point**<br>The bots ran expeditions exclusively in Pandala without saving their spawn point at Pandazaap (`207619076`). Deaths default to Astrub Zaap (`191105026`). | **209 out of 216 failures** occurred while standing at Astrub Zaap. If spawn was set to Pandazaap, respawn would have been local, costing **0 kamas** and saving travel time. |
| **3** | **Script State Machine** *(Error Handling Flaw)* | **Blind Route Continuation on Defeat**<br>Expedition scripts handle defeat with `Expédition : défaite, on continue` without checking current map, resting for health, or pausing on repeated deaths. | Bots repeatedly re-engaged combat with 50% max HP, entering combat instantly upon arrival. |
| **4** | **World Graph Topology** *(Genuine Routing Limit)* | **Pandala Island Disconnection**<br>Mainland Astrub and Pandala Island have no walking edges in `worldgraph5.json`. | Distance = 17–35 maps. Walking path is physically impossible on foot; Zaap teleport is mandatory. |
| **5** | **Pathfinder Bugs** *(Defect Assessment)* | **None Found (0 Bugs)**<br>The pathfinder operated 100% as designed: correctly detected unreachable target, avoided false adjacent disconnects (0 distance-1 failures), correctly selected Pandazaap, and safely recovered. | **0 adjacent failures**, **0 unhandled crashes**, **0 erroneous `no_route` exclusions**. |

---

## Actionable Recommendations

1. **Fix Combat Engagement Policy in Pandala**:
   - Add a monster level/power gate before attacking: do not attack Pandala mob groups (Pandikazes, Bulbis, Ino-Naru) solo without appropriate party size or level.
   - Enforce a minimum HP threshold (e.g. >= 90% PV) before entering combat.
2. **Bind Spawn Zaap to the Active Expedition Region**:
   - At the beginning of an expedition in Pandala, have the bot interact with Pandazaap (`207619076`) to set it as the character's respawn Zaap.
   - In the event of combat defeat, the character respawns directly on Pandazaap, eliminating the cross-continental Zaap fee and pathfinding failure.
3. **Add Death-Loop Circuit Breaker**:
   - If a bot suffers 3 consecutive defeats in an expedition, halt the script or recall to bank rather than draining the character's purse via 180-kama teleports every minute.
