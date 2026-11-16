Givre Replica Workbench (MZP6) - private Windows x64 build, 16/10/2026

Entry point: MZP3ReplicaWorkbench.exe
Runtime: .NET 10 Desktop Runtime x64; Avalonia Pro/Fluent build.
This is NOT the installed GivreClone v1.3.1, nor a complete gameplay bot.

Supported in this build:
- Dark Avalonia application with the original independent navigation.
- Opt-in local TEST profiles (never game credentials) and candidates.
- Sécurité TEST page: owner-only test recovery directory, real local
  MZP0 journal, health/uncertain status and structural launcher preflight.
- Paquets page: MZP3 neutral offline wire inspection and MZP6 GameMessage
  panel. The MZP6 panel REQUIRES an independently supplied, SHA-pinned
  3.7.10.10 catalogue + source scope; without that it is disabled.
- Non-operational views explicitly say so; no fake map, combat or ACK.

IMPORTANT:
- Does not connect to Ankama, GivreClone or Dofus.
- Does not read or modify installed GivreClone settings, sessions, tokens or
  accounts. Does not send packets, inject DLLs or claim server ACK.
- TEST storage is separate at %LOCALAPPDATA%/GivreReplica/TestRuns and is
  not opened until the operator explicitly clicks its Open button.
- Do not input account secrets, session credentials or real private frames.

Original full GivreClone.App cannot yet build: true GivreCore and
GivreLauncher.Core provider projects are missing. Offline verification is not
equivalent to gameplay validation. Source and reproducibility instructions:
GivreShareWork/docs/MZP6_PRIME_HANDOFF_20261016.md
