# Navigation review and Dofus memory evidence

The retained dump contains the loaded GameAssembly module, not the process heap or a full game-state snapshot. PE raw section offsets were compared with their mapped RVAs in the dump. The executable `.text` (6,654,612 bytes) and `il2cpp` (86,934,883 bytes) ranges match the installed DLL byte for byte. Exact digests are in `DOFUS_LOADED_CODE_20261017.json`. This observation does not establish method semantics, gameplay equivalence, or the contents of unrelated memory.

REA successfully selected the installed x64 PE with SHA-256 `e08da74cfcac126a4d0929bf76d79b7c4f45b902ece082041b55d2715db9bf13`. Ghidra `binary_overview` exceeded the MCP 300-second deadline. The session was closed. No usable native pseudocode or function-level Evidence record was returned; do not describe the changes below as recovered Dofus source.

Source review identified two pathfinding defects. The first three regression tests reproduced two failures before the fix: a blocked destination could be encoded, and a sector callback could allow diagonal movement between two non-walkable corners. The shared pathfinder now requires a walkable destination and combines corner walkability with sector permission. Leaving a blocked starting cell remains supported.

The new read-only MCP tool `probe_navigation(bot, target)` reports map/geometry/position readiness and a local route without sending it. It requires all 560 unique cell IDs and a confirmed position for the current map. It respects fight-only obstacles, map sectors and custom script obstacles. Dynamic actor occupancy is explicitly excluded; results do not guarantee live server acceptance. No account or game action was performed during verification.

Validation before the final application build: 37 focused navigation and existing LotB/ScriptCombat tests passed, with zero failures or skips. MCP compiled with zero warnings/errors. The stdio smoke verified initialization, exact discovery of 28 tools, navigation argument schema/read-only hint, and the absent-app error path. The app bridge and actual in-game navigation still require runtime validation; smoke discovery alone does not establish those behaviors.

Commands:

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj --no-restore --artifacts-path artifacts/McpValidation --disable-build-servers -p:UseSharedCompilation=false --filter 'FullyQualifiedName~NavigationSafetyTests|FullyQualifiedName~NavigationProbeTests|FullyQualifiedName~LotBTests|FullyQualifiedName~ScriptCombatTests' -v quiet
dotnet build GivreMcp/GivreMcp.csproj --no-restore -m:1 --disable-build-servers -p:UseSharedCompilation=false -v quiet
pwsh -NoProfile -File GivreMcp/Smoke.ps1
```

Final integration build: dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore --artifacts-path artifacts/McpValidation -m:1 --disable-build-servers -p:UseSharedCompilation=false -v quiet succeeded with 0 errors and 175 warnings. The application was not launched. Build log: artifacts/navigation-app-build.log (local, not committed).
