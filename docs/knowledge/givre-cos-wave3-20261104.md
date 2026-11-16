# CoS wave3 integration

2026-11-04: reports delivered under `docs/cos-dofus-video/rapports-v3/` in GivreWork/fix-f01-f16-final-20261024; INDEX.md read directly. Six missions cover incurable, buff timing, companion challenges, critical branches, Sram/Roublard transitions, and separate Turbo clocks. These are documentary/static candidates, not server validations.

P01 reports official SpellStateData76 with incurable=1 and state5920 sharing the icon but incurable=0. SpellResolver's central Heal previously ignored state76; six regression rows reproduced three failures (expected0,actual20), three controls passed. A central state76 guard was added; bash1243 combined resolver/planner/rotation suite passed260/260,0fail,0skip,exit0. This is modeled-state behavior, not live validation. No live heal result is claimed. P02 expiration convention remains historical, not established on current server. P03 Prudent companion11AP/6MP cause remains unknown.

Native visuals audit reports752/762 pinned signature mismatches on3.7.13.17. Several metadata-token/native mappings documented in docs/validation/combat-visuals-compositor-371317-20261104.md; field layout audit has no mismatch in nine sampled targets. No compatibility pins relaxed or hooks promoted.

User directed relaunch and Computer Use. Existing app15976 UI reconnect replaced game14252 with7016; process check confirmed only one game. IPC still connecting/map0 at last observation. Current model's desktop screenshots omit images; no coordinate guessing. No claim of ingame/tactical/Turbo visual validation.
- Tactical lease follow-up (2026-11-04): a managed regression now checks that an exception from a `PacketDecoded` subscriber cannot prevent authoritative map-transition handling/revocation. The fix and throwing-subscriber regression were being added at transcript end; no final test result, deployment, or in-game validation is established. Keep tactical interactive rendering unresolved.
