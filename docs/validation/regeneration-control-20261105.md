# Regeneration through local control, 2026-11-05

## Contract and scope

The local `get_bot_settings` / `set_bot_settings` boundary now includes the
existing per-character regeneration policy: `RegenerationEnabled`,
`RegenerationStartPercent`, `RegenerationStopPercent`, `RegenerationFoodGid`
and `RegenerationWaitForTeam`. Boolean inputs are strict JSON booleans;
percentages are integers from 1 through 100; the food identifier is a
nonnegative signed 32-bit integer. The resulting start threshold must not
exceed the resulting stop threshold, including when only one changes.

The existing validator stages every operation before changing settings or
calling persistence. Invalid mixed requests therefore cannot partially enable
recovery. The existing session/access guards remain in use. Persistence is
still best-effort: the response reports `applied_in_memory` and
`persistenceVerified=false`, not a verified disk write.

No new recovery engine, energy restriction, or account operation was added.
`BotsViewModel` already pushes these options to online sessions at its stats
refresh, and the Lua startup path already pushes them before starting scripts.

## Evidence

The existing `McpHarnessProductionTests` owner executes the compiled production
validator with an isolated persistence callback. Two new valid-policy cases
failed before the production change with the exact exception:

`Setting 'RegenerationEnabled' is unknown or not writable via MCP.`

The red run completed with exit code 1, two failures and zero passes. Its
receipt is `GivreClone.Tests/TestResults/regen-control-red.trx`; compiled
outputs are isolated under `%TEMP%/givre-regen-control-20261105`.

The expanded rejection table checks bounds, strict boolean types, reversed
thresholds and atomic rejection after an otherwise valid enable operation.
Valid-policy cases check typed readback and actual settings mutation.

The post-change run includes the complete `McpHarnessProductionTests`,
`RegenerationPolicyTests` and `ObservedLifeRegressionTests`: 63 passed,
zero failed or skipped, 17.6 seconds, exit code 0. Its receipt is
`GivreClone.Tests/TestResults/regen-control-green.trx`. The build retains
pre-existing warnings, including NU1903 for Microsoft.DotNet.ILCompiler,
CA2022 in ControlPipeServer, nullable diagnostics and partial-method parameter
name warnings; none were suppressed.
C# language-server diagnostics remain unavailable because workspace discovery
throws `UnauthorizedAccessException` on `artifacts/kourial-tools/pypdf`;
permissions were not changed. Compilation is the available diagnostic check.

The currently running harvest application has not been replaced with this
build. No claim is made that these new RPC fields are already available in
that process, or that regeneration has been live-validated through this change.
