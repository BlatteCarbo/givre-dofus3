# GivreClone class coverage and combat oracle

The user's ongoing requirement is genuine class-specific combat behavior across all classes (including Xelor Telefrags and Eliotrope portals), plus shared transitions. Generic profiles or isolated unit tests do not establish complete integration or live parity. Validate the mechanics in GivreClone and distinguish tested behavior from unsupported mechanics and live evidence; see [human](human.md) and [givre-combat-v2-20261102](givre-combat-v2-20261102.md).

## JondoEmu audit (2026-11-02)

Pinned reference: JondoEmu revision `683ab49c73f65b7c4ab7c1134c448f7fa850f6bc`. It is useful as a semantic/test oracle, not copied source. In the isolated lab, two targeted test batches passed: 42 tests and 117 tests (250 cases reported across both). These are reference-project results, not GivreClone integration or live-game validation. The GivreClone full suite was still running and reported a known Lua inventory failure; do not claim it passed.

JondoEmu's implemented mechanics provide concrete investigation targets: Xelor Telefrag swaps can grant 2 AP once per spell, per-spell swaps are capped, and pinned fighters prevent swaps; Eliotrope portals route spells via a portal chain, project the cast vector from the exit, apply a distance-based bonus, and track used/neutralized state. Treat these as source observations requiring confirmation against the target game/version, not unquestioned parity.

An earlier feasibility audit found tiered test prerequisites: pure displacement tests do not require game data at runtime, while spell-driven Sram/bomb tests need `world.db` and bomb data. A missing DB can cause early-return tests to appear green, so validate data presence and test execution evidence. The source targets Windows/.NET 10; the audit's tested client version was incompatible with the installed game at that time. Recheck current compatibility before any live-client use.

No claim that GivreClone has integrated these class systems follows from the reference tests. Keep the shared transition ledger and per-class implementation gaps in the project documentation, and verify each against current code/tests before proceeding.

## Installed class metadata (2026-11-03)

A read-only inspection of the pinned installed Dofus client found `Core.Enums.BreedEnum` assigns Forgelance value **20**, Bellaphone **21**, with no constant 19. Do not infer the character-creation index-to-server-ID mapping from this enum alone; that mapping remains unverified. Evidence and parsing limits are in the Givre worktree's `docs/codex/CLIENT_CLASSES_20261103.md`. This is static metadata evidence, not live-game validation.
