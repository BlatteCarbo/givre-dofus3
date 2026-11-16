# WorldIdOverlay prerequisites — Round 6 prompts 1/3

Status: **BLOCKED_STRUCTURAL_PROOF**. This directory is a design/evidence boundary, not a native mod. No Cargo manifest, DLL, hook, F7 registration or runtime integration is provided.

See `docs/codex/R6_WORLD_STRUCTURAL_EVIDENCE.md` and the offline-only `tools/R6-WorldMetadataProbe.ps1`. The report contains identifier occurrences, never resolved methods. `structuralProof`, `semanticProof` and `hookAuthorized` remain false.

Before implementation, require an attributed 3.7.11.11 inventory with assembly, namespace, declaring class, inheritance, exact parameter/return types, instance/static flags, field ownership and offsets, plus native method RVA and generic/shared-entry information from a documented mapping. A metadata string file offset is never an RVA. Refuse missing, ambiguous or incomplete inventories.

HUD requirements: prove map identity and its lifecycle; distinguish the hovered cell from the selected cell and the player's cell; distinguish entity instance identity from template/type IDs; establish hover enter/leave and map-change invalidation. One event-driven update per proven event, no scene/UI scan. F7 is only a proposed key and must be checked when integrating.

CombatInstant requirements: prove the movement entry, time representation, completion/cancellation path, combat-state witness, and scene/object lifetime. Animator playback acceleration alone does not establish movement completion. No guessed native signature, first-candidate selection, or copied historical offset is acceptable.
