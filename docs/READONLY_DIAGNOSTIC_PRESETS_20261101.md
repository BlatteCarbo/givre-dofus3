# Read-only inventory and craft diagnostics

## Run once

Load either file as the selected character's Lua script, then start it:

- `scripts/Readonly-Inventory-Audit-20261101.lua`
- `scripts/Readonly-Craft-Cost-20261101.lua`

Both define `move()`, produce one report, and return `false` to stop. Loading
alone only defines the script; a Lua evaluation surface must also call `move()`.
For a surface supporting `loadfile`, use the absolute path, for example:

```lua
assert(loadfile("%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/scripts/Readonly-Inventory-Audit-20261101.lua"))()
move()
```

Change only the local `CONFIG` at the top before loading. Audit defaults to
40 rows. Craft defaults to GID `16520`, quantity `1`, and 40 detail rows, matching
the target used by the existing `scripts/check-craft-plan-readonly.lua`. The
target is a concrete diagnostic example, not a recommendation. If it is absent
from the installed catalog, the script emits `CRAFT_UNAVAILABLE` with the
binding's reason; choose a catalog GID you intend to inspect.

Craft quantities are desired totals, limited to 1..1000. The planner consumes
observed owned stock first, including target items already owned, then recursively
expands recipes into missing leaf materials. It does not force additional crafts.
`maxRows` accepts integers 1..100. Changing a local config requires reloading.

## Interpret the output

Inventory groups unequipped stacks (`pos == 63`) by GID, sorts numerically, and
reports stack count and quantity. Equipment is counted separately, never valued
as bag stock. Bank contents are excluded. Default output is at most 42 lines:
header, 40 detail rows, summary. Totals include rows omitted from printed detail.

Craft emits missing leaf quantities, cached average unit values, estimated missing
resource costs, craft step counts, and insufficient-job-level flags. Default
output is at most 42 lines, sharing one detail budget between materials and craft
steps. The report is not an executable crafting plan. A noncraftable target can
appear as a missing leaf itself.

Craft stock is the binding's **session inventory plus known bank snapshot**.
Inventory counting in this planner includes equipped items. Bank data may be stale
or unobserved; it is not refreshed, opened, or verified by this script. Neither
script proves inventory freshness. Start after the character's inventory has
loaded, and treat observations as a snapshot rather than guaranteed availability.

Both report the current server name, `cached-server-average` provenance, and
unknown price age. Prices originate in `ObjectAveragePricesEvent`, scoped to the
bot's server (or its session before server identity is known); they are not live
HDV offers. When the server name is unavailable, the presets deliberately leave
all prices unknown and make no price calls. A nonpositive price means unknown,
never zero cost. `knownSubtotal` is only the sum of known rows; a total is absent
and printed as `unknown` when any row lacks a price. A genuinely empty missing
list has cost zero. Estimates exclude fees, sell prices, time, craft feasibility,
and profitability.

Each run also leaves `READONLY_DIAGNOSTIC_RESULT` as a Lua table for inspection
and deterministic checks. Its `rows` include all diagnostic rows, even omitted
printed rows. Unknown row values and unknown totals are absent (`nil`). Craft
sets `status = "ok"` or `"unavailable"` and retains the unavailable `reason`.
Running `move()` again without reloading produces no more output.

## Supported API evidence

Only these Givre reads and the existing `print` logger are invoked:

| Call | Binding/source contract |
| --- | --- |
| `getServerName()` | `GivreClone.App/Services/GivreLuaHost.LotB.cs`, current session server name |
| `inventory:items()` | `GivreLuaHost.Bindings.cs`, rows with `gid`, `qty`, `name`, `pos`; `pos != 63` is equipped |
| `inventory:getAveragePrice(gid)` | `GivreLuaHost.SnowEconomy.cs`; calls `LaunchService.Snow.cs` `AveragePrice`, 0 if unknown |
| `craft:plan(gid, quantity)` | `Services/LotM/LotMLuaModule.Craft.cs`; returns plan or nil/reason |

`Services/LotM/CraftPlanner.cs` supplies bounded recursive expansion (10,000
visits, depth 64). `Services/LotMGame.cs` supplies inventory plus known-bank
counts; `Services/InventoryStore.cs` confirms counts include all positions.
No buying, selling, travel, crafting, inventory mutation, stat spending, event
registration, delays, network calls, or gameplay verbs occur in these presets.

The pre-existing craft smoke script already lists missing materials. New
capabilities here are the reusable bag audit and missing-resource cost accounting
with explicit unknowns, server provenance, job-level diagnostics, and bounded
output; this does not establish completion of unrelated backlog candidates.

## Deterministic checks

The dedicated test file is
`GivreClone.Tests/ReadonlyDiagnosticPresets20261101Tests.cs`. It loads the real
Lua scripts into the repository's existing MoonSharp fixture pattern with only
read APIs present. It checks grouped stock, equipment exclusion, unknown prices,
the output cap without losing totals, unknown server identity, partial craft cost,
already-owned targets, unavailable data, and one-shot termination.

After the parent builds the test assembly, run from the repository root:

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~ReadonlyDiagnosticPresets20261101Tests
```

Binary success: exit code 0, six tests passed. This child does not build the
assembly or claim live game execution. The parent should run both scripts on
the actual Lua surface: `move()` must return `false`, emit one header plus
summary (or `CRAFT_UNAVAILABLE`), and a second call must emit nothing. No game
action should be sent.

Child-owned temporary verification root:
`%USERPROFILE%/AppData/Local/Temp/ulw-givre-100-20261101/presets`.
It contains `Verify-Readonly-Presets.ps1`, a no-build fixture runner using the
already-installed MoonSharp 2.0.0 assembly. Exact invocation:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File %USERPROFILE%/AppData/Local/Temp/ulw-givre-100-20261101/presets/Verify-Readonly-Presets.ps1
```

Binary success: exit code 0 and `READONLY_PRESETS_PASS cases=6`. The temporary
runner is owned by this preset task and may be deleted with that directory after
parent verification; it is not a shipped dependency.

## Observed live run — November 1, 2026

The parent executed each complete script through `run_lua_script` on Bastionbot,
Mikhal, in Socket mode. Both ended normally with `scriptRunning=false`.
The inventory report observed 213 unequipped GIDs and 16 equipped stacks;
40 detail rows were printed. Ten missing prices kept the estimated total
unknown, rather than treating them as free.

The craft report for GID 16520 observed six missing resource GIDs (25 units),
one craft step and no insufficient-job-level step. Cached average prices
produced an indicative missing-resource subtotal of 3788 kamas. This is
neither a current purchase quote nor a net-profit measurement.

Before and after both scripts, map ID was 54165297 and the wallet was
1254757 kamas. The diagnostic session was then disconnected and confirmed
offline. The six C# regression cases also passed in the integrated suite
(3881 passed, two pre-existing skips). Repeated-call silence is proven by
fixtures, not claimed as a separate live action.
