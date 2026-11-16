# State packet artifact investigation

REA 6.8.0 static managed inspection was run against the existing Cpp2IL
address-annotated extraction; no target code was loaded or executed.

Assembly-CSharp.dll is only 4096 bytes with ten auxiliary types; its metadata
does not contain the combat protocol. Its SHA-256 is
fdcdbf0b04393e6c2010cd2a394e85d813f6ac42f56d49dce1897691aea42f5c.

Ankama.Dofus.Protocol.Game.dll has SHA-256
a41cf993a50418f4cb6aa64bf54eee641331685b029a5d2053ff146703af5d1e.
REA recognized jth, jun, llg and lng, with tokens 020003b4, 020003c0,
020005c5 and 020005d4 respectively, but hit its 67108864-byte metadata
representation limit before returning fields or methods. Semantic full names
and semantic short names were unmatched. These partial results do not prove
field meaning or native handler behavior.

The existing NativeAddressLookup metadata reader now accepts `type:exact-name`
in addition to its original RVA lookup. It enumerates fields, int32 constants,
properties and annotated method addresses without loading the target assembly.
The first compiled read of jth completed with exit 0: constants erhm=1,
erho=2, erhq=3; properties fwyp through fwyu. This confirms field numbers only,
not state ID, actor ID, duration, or removal semantics. Native address extraction
completed with exit 0: jth.WriteTo RVA 0x7EB780, jth.chff 0x91C3D0,
jth.MergeFrom(input) 0x7EB700, getters chez/chfb/chfd at 0x7AE760,
0x74BB10 and 0x68D2D0. The current matching catalogue maps jth to
FightBoostUpdateEvent and jun to FightBoostUpdateStateEvent. Neither this alias
nor an address proves field semantics; jun member extraction is pending.
No production mapping or combat reducer changed.

jun extraction completed exit 0: one int32 field-number constant erua=1,
getter chtj RVA 0x68D2C0, serializer chtk RVA 0xA80A20. Bounded Capstone
inspection of the installed GameAssembly.dll (ordinary local analysis, not REA
Evidence) shows jth getters reading int32 at offsets 0x18/0x1c and int64 at
0x20. The jun serializer reads its collection from offset 0x18 and calls the
repeated-field writer at 0x3914490. This corroborates the earlier shape
investigation but still does not establish semantic state/actor attribution.

Core metadata identifies EntityStateInfo(stateId, duration, status, isStateIcon),
constructor RVA 0x273B600. Executable-section relative-call candidates were
checked against exception-table function bounds and decoded instruction starts;
one confirmed call occurs at 0x2710C21. The enclosing exception fragment starts
at 0x27107E4, which has no direct annotated method match (lookup exit 1).
Its UNW_FLAG_CHAININFO names the actual function start 0x27106D0; lookup there
succeeds and identifies eak.vcx(a,b), exit 0. At construction EDX comes from
ESI (initial RCX), duration R8D from EAX, status R9D from EDI. Thus the state ID
is already a method argument, not proven to be any jth wire field. Following
the chain prevents misattributing an internal fragment as a separate handler.

ABI qualification: callers at 0x27103FF and 0x27104DF pass a stack result
buffer in RCX and a copied 32-byte value in RDX. eak.vcx returns its result via
that buffer. Therefore entry RCX must not be labelled a stateId argument merely
because it later reaches ESI; the constructor region may reuse that register.
Tracing only the last register move is insufficient. These callers obtain the
copied value from 0x2707250 and 0x26F5E80 respectively. Twelve direct calls to
vcx were instruction-boundary verified, including these two. No wire-to-state
attribution follows until the value fields and constructor data flow agree.
Further decoding confirms ESI is reassigned from the saved state-id argument
at 0x27108C1 (and corresponding alternate branches), correcting the earlier
last-move interpretation. The factory enumerates wrapped buffs, reads an effect
object from [buff+0x38], calls getter 0x26BF8B0, and compares its result to that
saved state ID. Getter attribution is pending. dzx.uza constructs a generic
32-byte wrapper containing the supplied object at offset 8, not a wire reader;
it therefore supplies no protobuf attribution itself.

The DataCenter assembly exposes the exact named model
Core.DataCenter.Metadata.Spell.SpellStateData. Metadata inspection completed
exit 0 and identifies explicit fields `cantDealDamage`, `invulnerable`,
`invulnerableMelee`, `invulnerableRange`, `incurable`, `cantBeMoved`,
`cantBePushed`, `cantSwitchPosition`, `cantTackle`, `cantBeTackled`,
`preventsSpellCast` and `preventsFight`, alongside id/nameId/descriptionId.
GetSpellStateById is annotated RVA 0x7C62A0. These are schema properties,
not observed state rows or proof that a fighter currently owns such a state.
The examined GameDb.Spells.cs supplies SpellLevelData rules only; it does not
yet import these state properties. No numeric immunity IDs were inferred.

The application's actual read-only catalogue was located at
GivreReplica/TestRuns/givre-app-offline/userdata/gamedata/dofus.sqlite.
Its adjacent version.json identifies ledouxm/dofus-sqlite tag v6.0_3.7.12.12,
updated 2026-10-29, not the installed 3.7.13.17 reference. SQLite inspection
confirms SpellStateData contains the same named immunity columns. Example
rows: 56 has invulnerable=1; 375 invulnerableRange=1; 376
invulnerableMelee=1; 218 and 257 cantDealDamage=1. These are observed rows in
that catalogue only, not current-client equivalence or active fighter states.
No table or row was modified. Future integration should carry the catalogue
properties rather than hard-code these IDs, and must keep unknown active
states distinct from confirmed absence.

GameDb.Spells now exposes nullable SpellStateDamage(stateId) from the selected
catalogue, with all four damage-related flags. Missing table/row and invalid
IDs return null, distinct from a known row whose flags are false. Synthetic
tests use IDs 9001..9005 to catch hard-coded historical IDs and swapped flags;
the GameDb-related suite is running. This is only the catalogue boundary;
the resolver and live planner do not yet consume this lookup.

Catalogue suite passed 21/21, zero skipped, exit 0. The resolver now accepts
explicit StateDamageRules on SpellBoard and blocks damage for a present target
state marked Invulnerable or caster state marked CantDealDamage. The rules are
preserved across resolved boards; synthetic tests cover active/absent states,
shield preservation and no life-steal healing from a blocked hit. Resolver and
catalogue integration passed 83/83, zero skipped, exit 0 (7 seconds).
CombatTurnSnapshot now propagates the same explicit rules to SpellBoard and
future approach scoring. Tests require no immediate cast or speculative
approach against a harmless invulnerable enemy. This combined run is pending.
The combined run returned 220 passed and two failed (222 total, zero skipped,
exit 1). Both new immunity planner cases passed. The failures are existing
Reachable_challenge_end_cell_does_not_cancel_available_attacks rows for 3/6 AP;
their snapshots supply no StateDamageRules. The current Choose contract keeps
a reachable challenge endpoint binding, while those assertions require an
attack even when casting consumes the AP needed to escape. This discrepancy
requires baseline/provenance investigation, not weakening either assertion to
claim an immunity pass. Isolated replay reproduced both failures (0/2, exit 1).
Application/removal ordering controls then passed with the resolver/catalogue
suite: 85/85, zero skipped, exit 0 (10 seconds). They verify state application
blocks the following hit and state removal enables it without mutating input.
Inspection of LaunchService.Combat.Turn.cs confirms the live fighter adapter
still supplies no States/StatesKnown/AbsentStates values at construction. It
therefore cannot yet support live immunity admission; wiring a catalogue alone
would not repair that missing observation.
The attempted earlier-binary baseline could not execute: vstest reported a
missing assembly from the dependency manifest, despite its wrapper returning
exit 0. No test assertions ran, so it is not evidence that the two challenge
failures predate this increment. The failures remain unexplained. An additional
resolver control now verifies that merely having a CantDealDamage catalogue row
without its active state does not block damage; its focused run is pending.
That resolver run passed 86/86, exit 0 (17 seconds). Challenge fixture analysis
then found Cell(-1) lies outside the isometric board; the enemy is now on a
valid adjacent cell, with an explicit bounds assertion. Existing attack/damage
assertions were retained. Replay passed the 6-AP case but still failed the
3-AP case: the planner spent all AP on a compliant tackle escape and never hit.
Choose now retains productive compliant plans, but compares an unconstrained
plan when the compliant turn has no damage, finish, healing, shielding or
control; an available damaging plan wins that otherwise empty turn. This
implements the existing regression contract rather than editing it to pass.
The combined planner/resolver/catalogue run is pending.
The first repeat passed 224/225: broad fallback also bypassed the existing
Swap_preserves_end_cell_and_mobility_gates constraint. The fallback is now
limited to an observed positive AP tackle cost at the caster's starting cell.
All 225 combined tests then passed, zero skipped, exit 0 (7 seconds), including
the unchanged swap assertion and both corrected-fixture challenge cases.
The complete managed suite passed 4571 tests, zero failed, two existing
legacy skips (4573 total), exit 0 (2 minutes 9 seconds).
No live snapshot producer supplies these rules yet. Melee/range immunity,
unknown active-state handling and live catalogue version admission remain open.

## November 6 live wiring recheck

Parent read the current fighter construction at LaunchService.Combat.Turn.cs:
it now carries observed breed, visibility, summon owner and monster grade, but
still supplies no States/StatesKnown/AbsentStates or snapshot StateDamageRules.
The existing SpellStateCatalogTests establish data interpretation only, not
live state admission. Thus the newer breed/visibility coverage must not be
described as immunity-aware combat.

RawStateMessageReader was also read. Its own contract explicitly distinguishes
wire observations from active-state eligibility and lifetime. It retains null
scalars, scope and raw timing fields; removal requests do not prove executed
bulk removal. Connecting its add observations directly as active states would
discard that distinction. No such shortcut or guessed state ID was added.
The next implementation prerequisite is the established activation/lifetime
reducer for these observations, followed by the existing live snapshot boundary.

Unrelated resumed expedition verification: the old --no-build artifacts could
not start testhost because AssetRipper.TextureDecoder 1.3.0 was missing. A fresh
isolated reconstruction then passed 43/43 ExpeditionScriptTests,
ExpeditionLoopRegressionTests and ExpeditionRouteRegressionTests, zero skipped,
exit 0 (1 second). This validates the returned agent integration in controlled
Lua fixtures, not actual gathering/combat on the official client.

Evidence bundles are existing local files under Temp: givre-state-members-20261105,
givre-state-protocol-members-20261105, givre-state-protocol-short-20261105,
and givre-state-obfuscated-20261105. The latter three carry partial coverage.
