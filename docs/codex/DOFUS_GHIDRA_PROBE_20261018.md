# Dofus native analysis probe — 2026-10-18

Target: installed GameAssembly.dll, SHA-256 e08da74cfcac126a4d0929bf76d79b7c4f45b902ece082041b55d2715db9bf13. Existing project: artifacts/ghidra-dofus-long-20261018/project/Dofus371111.

The first analysis retained 308,283 function definitions but reached its 3,600-second analysis budget. Its status is partial, not complete.

A read-only probe reused that project with automatic analysis disabled. Three bounded sample functions at 0x180001060, 0x1800010e0 and 0x180001140 all produced pseudocode with a 90-second per-function ceiling. These samples were selected only to test the decompiler; none is established as a navigation function. A successful sample does not establish global function-boundary or type accuracy.

The bounded literal search found zero matching top-level defined strings for pathfind, mappoint, lineofsight or walkable in the current Ghidra listing. This does not establish absence from the binary or absence of navigation logic. Separately, a literal metadata scan found 18 candidate identifiers, including MapPoint, WorldPathfinding and PathFindingData. These are names at file offsets, not restored native method addresses or proven semantics.

Artifacts retained locally: navigation-probe.txt, navigation-metadata-seeds.json, probe-console.log and probe-state.json. The initial script compilation error was corrected before the successful probe. No game action, client patch, injection or Mizan inspection occurred.

A continuation was launched on the existing project with -process GameAssembly.dll, two analysis CPUs and a 14,400-second analysis limit. No reimport is requested. Results use separate continuation-state.json, continuation-summary.json and continuation-ghidra.log files. At report creation the continuation is running; do not treat the larger time budget as completion evidence. Additional analysis may improve references but does not guarantee IL2CPP name recovery. No new GivreWork gameplay feature is claimed from this probe.
