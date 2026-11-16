# Sram observed-mark catalog join

## Verified boundary

The parent read `MapFightReader.ReadMark` and `FightMarks.Observe` after the
read-only investigation. The current reader extracts mark identity, author,
type, cells and activation. The immutable observed registry retains only
those fields. Neither boundary currently establishes a source spell or grade.

The parent also checked `data/fields.json` at the FightMark entry. Current
semantic tags are identity 17, team 1, type 4, active 5, author 6 and cells 7.
Tags 3, 9, 11, 12, 13, 15 and 16 retain obfuscated names. Do not replace those
names with spell/grade based on a different protocol's tag positions.

The versioned effects export independently confirms these serialized
placement references:

| Source spell | Source grades | Placement action | Referenced spell | Referenced grades |
| --- | --- | --- | --- | --- |
| 12914, Piege Repulsif | 1, 2, 3 | 400 | 12928 | 1, 2, 3 |
| 12921, Piege Mortel | 1, 2 | 400 | 12927 | 1, 2 |

Source: `docs/cos-dofus-video/rapports-v2/N01/EFFECTS_3.7.13.17.csv`, rows
103-105 and 145-146 in the inspected file. These are catalog references,
not evidence identifying any unnamed live FightMark tag or executable
trap-trigger ordering. No planner behavior changed.

## Capture recovery result

New bounded recovery read both complete current live journals12-58-33
(78324 lines) and14-25-44 (47334 lines). Neither contains mark_cells or
unmark_cells. The former lists12921 among learned spells but supplies no cast
or placement frame; the latter's12927 substring is a kamas amount, not a spell.
These are not usable placement-to-activation captures. This search does not
exclude other archives. No simulated damage or live mark fields were inferred.

The next narrow observation target is12921/1 with payload12927/1, one entrant
pushed one cell onto one trap. Preserve complete wire tags, independently known
cast grade/owner, movement, activation, damage/state events and removal order.
Payload HP branches and delegated792 caster identity mean a synthetic expected
damage chosen before that observation would encode an assumption. Expiry,
overlaps, teleports and trap networks remain outside this target.

The historical recovery document records eight Huppermage rune placements
from spell 13686 on November 1 at 18:56. It is a report, not the raw fixture.
The parent searched November 1 transport filenames under the main worktree,
Downloads and `%LOCALAPPDATA%/GivreReplica`. Only
`TestRuns/ui-preview/userdata/logs/transport-2026-11-01_22-35-52.log` was found;
it contains no match for 13686, mark_cells or the placement log label.
This bounded search did not recover the documented placement frame. It does
not prove no other archive exists elsewhere.

## Next executable slice and acceptance

Use a complete version-identified raw placement payload, not the reduced
reader fixture. Preserve every mark field by wire tag before comparing it
against the complete spell-level catalog. A successful join must independently
identify both spell and grade; a coincidental numeric match is insufficient.
`tools/pktdump_extract.py` already provides a raw protobuf parser; its parser
does not itself assign semantic names. The existing reduced registry should
not gain guessed spell/grade values while this join is unresolved.

The child additionally reported installed hidden payload records 12928/1 and
12927/1, including action 792 references. The parent has not independently
replayed that bundle extraction, so those rows remain investigation leads,
not accepted trigger-simulation evidence. There is no live Sram-trap capture,
trap-chain execution, expiry model or new test result in this checkpoint.

## Separate simulated-placement provenance regression

Parent inspection found a narrower issue independent of the observed-wire
join: simulated placement stores `DiceNum` as the payload spell, but drops
`DiceSide`, while retaining only the source spell grade. The existing mark
provenance test now includes payload grades zero, one and three against source
grade two. `SpellMark.SpellGrade` defaults to zero so existing unknown marks
remain unknown; this does not infer a payload grade from the source grade.
The producer remains unchanged for the initial red run
`trap-payload-grade-red.trx`. A subsequent-cast control checks preservation of
an already-known payload grade without expiring or triggering the mark.
No trigger, score or live mark behavior is enabled by this model change.

The red run completed with two failures and two passes, exit 1, 13.4 seconds.
Payload grades one and three were both returned as zero; the unknown-grade
and existing-mark preservation controls passed. The production constructor
now copies the effect's DiceSide into SpellGrade. Related resolver, planner
and learned-Sram suites are running as `trap-payload-grade-green.trx`.
The C# language server still crashes during discovery of the inaccessible
pre-existing pypdf artifact directory; compiler/test output is the available
verification, and no permissions were altered.

Post-change verification completed (`bash_133`,
`trap-payload-grade-green.trx`): 252 passed, zero failed/skipped, 26.2 seconds,
exit 0. This covers the changed resolver provenance, subsequent-cast retention,
neighboring planner resolution and learned-Sram dispatch. It does not establish
trap execution, source identification from server marks, or live acceptance.
No account process was restarted or deployed for this data-retention change.
# Installed-data integration check

`SpellModifierRealDataTests.Installed_Sram_traps_retain_a_resolvable_payload_grade`
now exercises the real installed-data loader and resolver for Repulsive Trap
12914 grade 3 and Lethal Trap 12921 grade 2. It checks the resulting mark's
payload ID and grade, then resolves that payload through the strict catalog.
This covers the loader-to-resolver join that synthetic effect fixtures cannot
establish; it does not execute a trap trigger or infer network mark fields.
The targeted run is subscribed as `sram-realdata.trx` in the separate
`%TEMP%/givre-sram-realdata-20261106` artifacts directory.
The C# language server still fails while enumerating the inaccessible existing
`artifacts/kourial-tools/pypdf` directory; no permissions were changed.

A bounded consumer search across the eighteen combat-service source files
found `SpellMark` construction, copy and event projection only in
`SpellResolver.cs`. No mark consumer was found in `CombatTurnPlanner.cs`.
Consequently this check cannot establish trap-trigger execution, trap-network
damage valuation, movement through marks, or expiry. Preserving a resolvable
payload is a prerequisite, not a completed Sram planning mechanic.

The run completed with exit 0: seven passed, zero failed or ignored, reported
test duration 81.6 seconds and total build/test duration 219.9 seconds.
The installed client's Data directory was independently confirmed to contain
real asset bundles, so the fixture's missing-client early return did not apply.
Both trap cases passed through the real loader, resolver and strict payload
lookup. Build output reports 259 warnings; this is not warning-free validation.
No production code was changed during this integration increment, and no
trap-trigger execution or live network/catalog join is claimed.

## Hidden-payload extraction follow-up

A new read-only investigation identified the smallest candidate as one Lethal
Trap 12921 -> 12927 entered by a one-cell push, excluding overlapping marks,
teleports and payload movement. Placement alone is insufficient: payload row
792 can delegate the caster, and consume-before-payload and chain ordering are
not established by the inspected references. No trigger simulation was added.

`tools/SpellCatalogInspect` now requests exact spell/grade pairs through the
existing installed-data loader and prints the complete normalized descriptor.
It does not guess fields or recurse into children. The first extraction is
subscribed as `bash_165`, requesting 12927 grades 1/2 and 12928 grades 1/3 in
a separate artifacts directory. Its build and output remain pending. These
records can establish payload contents, but cannot alone prove server removal
timing, entrant selection or trap-centered push semantics.

The extractor compiled and ran successfully (`bash_165`, exit 0). Its four
complete normalized outputs were independently read via a multiline rerun
(`bash_166`, exit 0), avoiding terminal line truncation:

- 12927/1 has two earth-damage action-97 rows: 31-35 with mask `a,A,v50`
  and 39-44 with `a,A,V50`. Grade 2 has 39-43 and 49-54 respectively.
  Both grades then delegate action 792 to 12968/1 with mask `C`.
- 12928/1 and /3 first push two cells (action 5, shape Q, size 1,
  mask `a,A`), then deal air damage (action 98, cross size 1, mask `A`):
  9-11 at grade 1, 14-17 at grade 3. Both delegate 792 to 12968/1.
- 12968/1 was fully read (`bash_167`, exit 0). It conditionally delegates
  792 to 12974/1 on `C,*E3766`, 12972/1 on `C,*E3765`, 12971/1 on
  `C,*E3763`, 14319/1 on `C,*E3762`, and 19724/2 on `a,P,E611,*E3764`.
  All inspected rows use immediate trigger `I`, zero delay and empty Condition.

These executable payload bodies are no longer merely unverified leads. They
also show why flattening every trap to direct damage would lose state-gated
side effects. The extractor now prints the selected content path and SHA256
of the actual spell-level bundles to stderr. A rebuilt run for all five child
branches is pending as `bash_168`; version provenance is not inferred from
the earlier CSV name. No consumption or chain-order behavior is enabled.

The provenance-enabled build and all five branch lookups succeeded (`bash_168`,
exit 0). Selected data is under
`%LOCALAPPDATA%/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/Content`.
The spell-level bundle SHA256 is
`525048D9A4406720BA79FB6A23826B9AC5E40D4A87DCD3C452E1A0CCC27B2A30`.
This identifies the inspected artifact without claiming an independently
verified release version.

The branch action/mask/value fields were read from the completed output:
12974/1 contains action 1036 referencing 12940, value 1, duration -1, mask C.
12972/1 contains action 285 referencing 12949 plus state 950/951 rows involving
3777 through 3780. 12971/1 contains action 293 referencing 12932, value 8,
plus state rows involving 3767 through 3771. 14319/1 consists of state rows
involving 1407 through 1410. 19724/2 includes action 666 rows, action 293
references to 19725 and 12936, and state rows involving 3772 through 3776.
The numeric references in 1036/285/293 are not assigned executable semantics
here merely because they resemble spell IDs.

The next simulation boundary must explicitly handle or reject these modifiers
and state transitions. Ignoring them while valuing trap damage would not be
full Sram behavior. In particular, sequentially re-evaluating each state mask
after earlier rows mutate states may differ from server selection chronology;
the extracted rows do not prove which evaluation model the server uses.
No new production behavior or broad Sram-completion claim follows from this
catalog inspection. The read-only inspection tool itself is now build- and
runtime-verified against ten distinct requested spell/grade pairs.

