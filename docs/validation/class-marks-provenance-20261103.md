# Class marks: current boundary and provenance - 2026-11-03

The read-only class audit found generic primitives rather than complete Sram
or Roublard tactics. SpellResolver creates marks for actions 400/401/402/1091/
1165/2022, but does not activate them on movement, execute their child spell,
consume them, expire them or propagate a trap chain. Live ground-mark packets
are logged, not reduced into planner marks. Visibility tracking rejects
invisible or unconfirmed targets; it does not simulate Sram invisibility.

Bomb placement currently uses generic summon occupancy with synthetic 1/1
life, not monster-grade bomb statistics. Bomb age/combo progression, walls,
detonation propagation and once-per-chain history remain unimplemented.
Controlled-fighter permission handling is not bomb lifecycle support.

This increment preserves SourceSpellId, SourceGrade and the raw effect
Duration in SpellMark at creation, alongside the existing child SpellId,
owner and cells. A subsequent resolve preserves these fields without
decrementing Duration. Duration is retained data, not an asserted remaining
turn count or expiry rule. Delayed rows are already retained separately as
DeferredSpellEffect; they do not create immediate marks.

The existing summon/mark test now checks distinct parent and child spell IDs
and grade/duration. A separate preservation test starts with an existing
mark and resolves an attack, checking provenance and absence of a new mark
event. The Release --no-restore run filtered to SpellResolverTests,
CombatPlannerResolverTests and CombatTurnPlannerTests completed with exit 0:
97 passed, zero failed or ignored, duration 37.0 seconds. This change does
not enable trap activation, invisibility simulation, bomb chains or live admission.

## Observed mark activation flag

The current server-mapping/fields.json names FightMark.active as field 5,
but still supplies no named mark identity for pairing placement and removal.
MapFightReader.ReadMark now retains this boolean; LaunchService.Packets logs
it with type, author and cells. The protocol fixture covers both explicit
true and proto3 omitted false. No anonymous field was guessed as identity.

Release --no-restore filter CombatForcedMovesReplayTests and
FightVisibilityTests completed with exit 0: 22 passed, zero failed or
ignored, duration 11.7 seconds. These are synthetic protocol scenarios,
not a new Sram trap capture. The marks remain logging-only; active is not
treated as sufficient evidence to trigger or avoid a mark.

RequiredMapping now includes FightMark.active. The Release --no-restore
MappingContractTests run completed with exit 0: 16 passed, zero failed or
ignored, duration 4.1 seconds. This validates the shipped mapping contract,
not the unresolved placement/removal identity or live trap lifecycle.

## Static identity investigation: unresolved

A read-only worker investigated two avenues: versioned protocol sources and
installed client metadata. Neither independently established placement identity.
Historical rune counters at wire 17 remain candidate evidence only. The old
ExternalSourceBridge source directory is absent; broader artifact searches
were incomplete because of unrelated access-denied directories, so no global
absence claim follows.

The installed client still reports 3.7.12.12 (build 2026-10-19). The worker
reported SHA-256 values:

- GameAssembly.dll: 70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663
- global-metadata.dat: ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6

Metadata v39 contains Core.Features.Fight.Marks.FightMark (definition 3213,
fields 9973-9992) and FightMarkData (definition 8486, fields 47442-47443),
but obfuscated field names do not prove wire numbers or registry identity.
MarkCells/UnmarkCells generated-message strings prove existence only.

Missing proof: a version-matched serializer/descriptor plus the relationship
to UnmarkCells.mark_id, or a constructor/handler data-flow trace showing the
placement value and removal value address the same registry key. No client
was launched or attached, and no guessed mark_id was added to the mapping.
