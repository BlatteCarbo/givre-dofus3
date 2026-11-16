# Installed client class metadata - November 3, 2026

Read-only inspection of the standard installed client, no game launch or injection.
Metadata SHA-256 was recomputed:
`ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6`.
The existing `native/GivreTurboRuntime/TacticalAutostart/verify-map-root.mjs`
also completed with exit code 0 against the installed GameAssembly and metadata.

## Class identity

Type definition 38951 is `Core.Enums.BreedEnum`, with 31 fields including
`value__`. Its first field index is 186749. Field definitions have 12-byte
rows at 0x1cb8ba0; field defaults have 12-byte rows at decimal 21547432,
and their data begins at decimal 22224688.
The defaults below use single-byte compressed signed integers: even bytes
decode to byte / 2; odd bytes decode to -(byte / 2 rounded down) - 1.

| Enum member | Value |
|---|---:|
| Undefined | 0 |
| Feca | 1 |
| Osamodas | 2 |
| Enutrof | 3 |
| Sram | 4 |
| Xelor | 5 |
| Ecaflip | 6 |
| Eniripsa | 7 |
| Iop | 8 |
| Cra | 9 |
| Sadida | 10 |
| Sacrieur | 11 |
| Pandawa | 12 |
| Roublard | 13 |
| Zobal | 14 |
| Steamer | 15 |
| Eliotrope | 16 |
| Huppermage | 17 |
| Ouginak | 18 |
| Forgelance | 20 |
| Bellaphone | 21 |
| Summoned | -1 |
| Monster | -2 |
| MonsterGroup | -3 |
| NPC | -4 |
| TaxCollector | -5 |
| Mutant | -6 |
| MutantInDungeon | -7 |
| MountOutside | -8 |
| Prism | -9 |

There is no enum constant 19 in this type. Enum membership does not prove
that a class is playable, available to the account, or modeled by the bot.
In particular, do not infer Forgelance = 19 from its position in a UI list.
The existing `ClassIconProvider.BreedId` already maps UI index 18 to 20;
no correction to that production mapping was necessary.
The creation-screen index to server BreedData.id relationship still needs
its own observed UI/protocol evidence; this table is the client enum only.

Type definition 36402 is `Core.DataCenter.Metadata.Breed.BreedData`, not
the distinct empty type 38918 `Data.BoneDataManager.BreedData`.
Its 25 fields start at index 176741. Verified named fields include `id`
(type index 78866, Int32 as independently resolved in the existing map
analysis), `breedSpellsId`, `breedRoles`, `complexity`, `sortIndex`, the six
`statsPointsFor*` fields, and cached spell variants and spell IDs.
This establishes the schema, not populated data values or learned spells.

## Existing loader and map evidence

The verifier reconfirmed field storage and offsets for MapController and epf,
the LoadAssetAsync<GameObject> and Release<UnityEngine.Object> AOT specs,
and the throwing base MakeGenericMethod implementation. Detailed return-type
and cell-field analysis remains in `METADATA-3.7.12.12.md` beside that verifier.
This execution alone does not independently re-decode every cell field or
the full generic return chain. No new runtime or visual claim follows.

### Direct follow-up decode on November 3

A separate in-memory read of the installed metadata and PE resolved method
row 0x1445cac, return index at byte 6: 51812. The types-table entry at
0x1860c6690 + 51812 * 8 points to 0x186063560 (kind 21). Its generic-class
pointer is 0x186d00cf8, whose definition type is at 0x186f9d288 and resolves
to definition 20028, `System.Threading.Tasks.Task`1`. This independently
reconfirms Task, not UniTask. The open generic argument is not claimed to
be GameObject from this chain alone; the closed AOT specialization is
separately checked by the verifier described above.

The same direct decode read all 15 ClientCellData and 24 ClientMapData
field definitions and resolved their type indices through the PE types table:

| Field | Metadata row | Type index | IL2CPP kind / type |
|---|---|---:|---|
| ClientMapData.id | 0x1ee70b0 | 78866 | 8 / Int32 |
| ClientMapData.cellsData | 0x1ee7170 | 40226 | 21 / generic instance |
| ClientCellData.cellNumber | 0x1ee6f78 | 93602 | 9 / UInt32 |
| ClientCellData.mov | 0x1ee6fb4 | 65961 | 2 / Boolean |
| ClientCellData.los | 0x1ee6fc0 | 65961 | 2 / Boolean |
| ClientCellData.visible | 0x1ee6fd8 | 65961 | 2 / Boolean |
| ClientCellData.roleplayMonstersMovementBlocked | 0x1ee6ff0 | 65961 | 2 / Boolean |

This establishes field identity and representation, not current walkability,
combat occupancy, hidden traps, or server acceptance of a movement. No
source change or client memory mutation was required for these two checks.

## Spell and effect schema follow-up

Installed definitions 35982 (`SpellData`) and 35984 (`SpellLevelData`)
contain respectively `spellLevels` and the `spellId`/`grade` relationship.
SpellLevelData exposes both `effects` and `criticalEffect`, per-target and
per-turn limits, shared limits, cooldowns and `statesCriterion`. Definition
36349 (`EffectInstanceData`) carries `actionId`, `targetMask`, `duration`,
`delay`, `triggers`, `random`, `effectElement` and `zoneDescr`; its dice subtype
36357 adds `diceNum`, `diceSide`, `displayZero`. Schema presence alone does
not establish that every condition is simulated by the planner.

The enum `Core.DataCenter.Metadata.Enums.ElementId` (definition 36322)
decodes to Undefined=-1, Neutral=0, Earth=1, Fire=2, Water=3, Air=4, None=5.
This differs from the historical effectElement convention used by
`DamageEstimator.ElementOf` (Earth=5, Neutral=0/1). The serialized
`effectElement` field has type index 87830, not this enum's definition;
the enum is therefore not sufficient proof to change that conversion.
An existing bundle probe was extended to emit actionId, effectElement and
dice together. Its first run failed NETSDK1004 because its temporary obj
directory had been cleaned. A run with dependency restoration was started;
its result is pending. This mapping task remains open.

The restored probe subsequently completed with code 0. A bounded pass over
grade-1 normal effects in the installed spell-level bundle found these
distinct action/element pairs (one witness per pair):

| Spell ID | Damage action | Serialized effectElement |
|---:|---:|---:|
| 201 | 98 | 4 (Air) |
| 202 | 99 | 2 (Fire) |
| 204 | 100 | 0 (Neutral) |
| 206 | 97 | 1 (Earth) |
| 212 | 96 | 3 (Water) |

Thus the installed serialized damage effects agree with ElementId, and the
historical Earth=5 conversion is incorrect for this client. The production
correction and regression validation are in progress. Utility examples
12735/action6, 12736/action8 and 32443/action5 have effectElement=-1;
32443/action99 has element2 and dice14..16. These are actual bundle values,
not inferred from spell names. Delays and random fields were zero and
triggers were I for those sampled utility effects; no generalization to
all spells is implied.

Installed spell-level bundle SHA-256:
`c4ac393701d0c075a758628c684f04d434fe0472fa3bba498cfe8cbb16181cec`.

The old conversion was reproduced by the corrected owner test: 1 failure
out of 5 on the unchanged estimator. Both worktrees now map Earth to 1,
Neutral to 0, and render element 5 as unknown rather than Earth. The
regression also checks an Earth resistance and Earth flat bonus together:
100 base + 20 bonus at 50 percent resistance yields 60.
The primary worktree passed 92 estimator, rotation and turn-planner tests,
zero failures or skips, exit 0. The combat worktree's first no-restore
invocation only generated successfully without reporting tests; it is not
counted as a passing test run. An explicit project run with restoration is
complete: 50 tests passed, zero failures or skips, exit 0. No live battle
was run for this correction. These counts differ because the worktrees
contain different test inventories; they are not a combined coverage claim.

## Profession schemas identified, version linkage pending

The same pinned metadata contains JobData (36190), RecipeData (36193), and
SkillData (36196). RecipeData exposes resultId, ingredientIds, quantities,
jobId and skillId. SkillData exposes parentJobId, isForgemagus,
modifiableItemTypeIds and levelMin. These schemas do not certify the version
of the bot's SQLite recipe catalog or its populated values. That separate
verification remains open.

### Installed profession bundle version check

The installed `Dofus_Data/StreamingAssets/version` was read directly:
Version=3.7.12.12, BuildDate=2026-10-19T15:11:45+00:00. The installation's
manifest.json lists the same bundle names and sizes below. SHA-256 values
were independently computed from the actual installed files, not copied
from the manifest's differently formatted hash fields.

| Bundle suffix under Content/Data/data_assets_ | Bytes | SHA-256 |
|---|---:|---|
| jobsdataroot.asset.bundle | 1572 | ab2c210a9422e0985755a0990faf87d713c83827f46cde36001a7b914df02c19 |
| recipesdataroot.asset.bundle | 81834 | 9babb80fee7374345f1e1439c933e4852c57980c47f46ae7bc5082da5fa084c6 |
| skillsdataroot.asset.bundle | 13281 | 48a765e282eadb5972ee09d89d6d912f9e7fe35741999e7a7092ac69e7d228a0 |

This associates the observed files with the local installation reporting
3.7.12.12; it is not a remote publisher attestation. The historical SQLite
export documented in tools/local-recipe-catalog/README.md still has UNKNOWN
version, and its preview cache is absent in this worktree. It was neither
relabelled nor promoted to a verified recipe source. ProfessionCatalogReader
rejects a declared version differing from its supplied expectedGameVersion;
the optional argument alone does not prove callers supply the installed
version. No recipe execution, XP value, or SQLite-to-bundle equality is
claimed by this check.
