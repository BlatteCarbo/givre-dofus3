# Measured session economics (backlog 78/79/80)

This read-only report measures wallet cashflow, not a profitable strategy. It sends
no game actions, performs no travel or market queries, waits for nothing, and writes
no files. Two endpoint reads and one fixed-size output line bound each lifecycle.
There is no ranking when evidence cannot support one.

## Existing measurement inventory

- `GivreLuaHost.Bindings.cs`: `getKamas()` reads the current session wallet cache;
  `-1` means unknown. `getCharacterName()` and `getServerName()` identify its scope.
  Cache freshness is not exposed, so two reads are not two confirmed transactions.
- `GivreLuaHost.LotB.cs`: `stats:summary(period)` returns persisted aggregate
  `kamas` and `bottingSec`, but no wallet sample count, receipt attribution, fees,
  or cost basis. Session counters have a different scope. The new report does not
  reset counters or reinterpret the daily aggregate as the current measurement.
- `BotStatsStore.cs`: `KamasGained` is last minus first wallet sample; it is not
  business net income. Underlying sample rows are not exposed to Lua.
- `GivreLuaHost.Snow.cs`: `global:elapsedTime()` uses integer division into
  whole hours since the host's first start, not exact measurement seconds.
  The report gives conservative duration bounds when available, never an exact
  rate from that clock. Lower bound is inclusive; upper bound is exclusive.
- `HdvStatsStore.cs` already records realized sales and taxes, including offline
  sales with deduplication. These journal entries are not exposed by current Lua
  bindings. Reimplementing that journal in Lua would duplicate existing machinery.
- `GivreLuaHost.Eco.cs`: price/craft/deal/wealth values are estimates, not receipts;
  `eco:wealth()` also clamps unknown wallet balances. Neither is an income basis.
- The existing `Readonly-Inventory-Audit-20261101.lua` and
  `Readonly-Craft-Cost-20261101.lua` remain separate estimate diagnostics.

## Invocation

Install `scripts/lib/Measured-Session-Economics-20261101.lua` under the active
application data root's `scripts/lib` directory. The normal default root is
`%APPDATA%/GivreClone`; use the active clone's root when configured differently.
The host's supported `require` loads modules there, not relative to the preset.
Load `scripts/Readonly-Session-Economics-20261101.lua` through the normal script
surface and run once. It starts at load, finishes on the first `move()`, returns
`false`, and tolerates the host's subsequent `stopped(reason)` callback.
This is a smoke measurement, normally nearly instantaneous, not a useful earning
sample. It does not initiate any economic activity.

For an existing observation session in the same Lua VM, use the module directly:

```lua
local economics = require("Measured-Session-Economics-20261101")
economics.start()
-- Existing independently authorized session runs here.
-- At the session's actual finish:
local report = economics.finish()
```

Call `start` once and `finish` once; repeated `finish` returns the same result
without more reads or output. Finish may also be called from an existing
`stopped` hook because all accessed bindings are reads. Do not load another script
or restart the host between endpoints; state is deliberately confined to one VM.

An operator with independently observed receipt evidence may pass **subtotals**:

```lua
local report = economics.finish({
    knownFees = 25,
    knownOtherCosts = 100,
    source = "operator receipt reference"
})
```

These inputs are not collected automatically and must not be populated from
average prices or assumed values. Omit unknown fields; explicit zero means an
observed zero subtotal only. The source is mandatory, 1..160 bytes, and amounts
must be nonnegative safe integers. These subtotals never establish ledger
completeness and are not subtracted from wallet delta: a fee paid from the wallet
is already in that delta. Even supplied zero subtotals leave total fees unknown.

## Machine-readable result and limits

`READONLY_SESSION_ECONOMICS_RESULT` and the returned Lua table contain:

- `schemaVersion`, `sampleCount` (2 endpoint reads), `knownWalletSamples`,
  `first`/`last` snapshots, identity continuity, wallet source and freshness.
- Signed `walletDelta` only if both balances are known and the nonempty character
  and server match. A negative delta is cash outflow, not proof of economic loss.
  An unchanged cached balance is zero observed cashflow, not zero unknown costs.
- Optional duration bounds from host hours; `durationStatus` remains `"unknown"`.
  A missing or reversed clock omits both bounds.
- Optional `knownFees` and `knownOtherCosts` with caller evidence provenance.
  `feesStatus`, `otherCostsStatus`, `costBasisStatus`, `realizedReceiptsStatus`,
  and `netProfitabilityStatus` remain `"unknown"`.
- `missingEvidence` and `rankingStatus = "insufficient-evidence"`.

Unknown numeric fields are absent (`nil`), never fabricated zeroes. There is no
`netIncome`, exact `durationSeconds`, or per-hour income field. Transfers,
withdrawals, gifts, stock consumed from earlier purchases, and delayed sale
receipts cannot be attributed by endpoint balance differences. This is the
missing evidence seam, not a claim that any of these costs is zero.

## Regression coverage and handoff

`GivreClone.Tests/MeasuredSessionEconomics20261101Tests.cs` uses the existing
MoonSharp soft-sandbox harness with deterministic endpoint values, no sleeps,
and only the actual read-binding shapes. It covers signed/zero cashflow, unknown
balances, identity changes, coarse/missing/reversed time, receipt subtotals,
invalid evidence, lifecycle bounds, and the preset stop callback.

Parent validation command:

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --filter FullyQualifiedName~MeasuredSessionEconomics20261101Tests
```

Tests and real application surface were not run by this child: its scope forbids
build/test processes and reserves the live surface for the parent. In particular,
real wallet cache freshness and journal completeness remain unverified.
No child temporary files, environments, caches, services, or processes were
created; the reserved child temp directory therefore needs no cleanup.
