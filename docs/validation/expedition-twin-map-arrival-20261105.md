# Coordinate fallback must confirm the destination map ID

## Reproduced defect

`GivreLuaHost.Travel.cs`, `WalkByCoordinates`, checked exact map ID first
but then returned success for equal coordinates on different map IDs. Two
maps can share coordinates and a world-map layer without being interchangeable.
When graph navigation falls back to coordinate walking, this could report a
visit to the requested map without leaving its twin. This is a source-derived
expedition failure path, not a proven explanation of the historical live incident.

The parent independently read the navigation branch and existing offline host
fixture after the read-only investigation. The new two-row theory in
`ExpeditionSelectionDiagnosticTests` calls the actual host method with map IDs
10 and 11 at the same coordinate and layer. No movement, sleeps, network or
substituted travel function is involved. Exact ID must succeed; its twin must
fail without emitting travel logs.

## Evidence

- Before correction: mon_BN8ZF2NW9CH4WM56 / bash_65,
  `twin-map-red.trx`: two tests, one passed and one failed, exit 1, 17.6 seconds.
  Target 11 returned true when false was expected; exact target 10 passed.
- Correction: only the equal-coordinate/different-ID return changes to false.
  Exact-ID arrival, stale-ID normalization and expedition retry policy remain
  unchanged.
- Related verification mon_QF27XZMPXRC815MQ / bash_66 passed 47/47, zero
  failed/skipped, 14.8 seconds, exit 0, `twin-map-green.trx`: selection
  diagnostics, expedition route regressions, zaap refusal regressions and
  coordinate walking tests. Existing compiler/analyzer warnings remain.

Compiler/test results provide validation because the existing C# language
server crashes while scanning the inaccessible artifacts/kourial-tools/pypdf
directory. No permissions were changed. Official-client acceptance remains open.
