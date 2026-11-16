# Roublard: one resolved bomb data chain, 2026-11-05

## Result and scope

### November6 independent Gemini reconciliation

Read-only Gemini investigation st_01a1af4d rechecked the metadata chain and
current resolver. Parent independently read KnownAction, RowProblem and
CombatTurnPlanner.IsAdmitted. Actions406,1009 and141 are not modeled; action120
is modeled. Importantly this is NOT blanket rejection of the entire Detonateur
descriptor: IsAdmitted requires at least one immediate modeled effect and only
vetoes unsupported damage actions. It can therefore admit a partial AP-refund
simulation while retaining unsupported bomb effects as uncertainty. That is
not an implemented detonation or proof of a useful/safe live cast.

The static bomb-to-explosion and wall IDs were already established below.
The Gemini report does not add an observed explosion, scaling formula or
production implementation. Its suggested unsupported-action assertions would
record current limitations rather than prove new detonation behavior, so no
such test was added. The next meaningful behavior proof must cover the complete
effect sequence, bomb ownership/identity, removal, recursive explosion and
authoritative stats/combo input; treating the caster refund alone as a bomb
implementation would ignore side effects. No arbitrary damage or multiplier
was introduced. The broad Roublard task remains open.

The installed 3.7.13.17 assets resolve Explobombe end to end:
`SpellData 13444 -> SpellLevelData 41922 -> action 1008 -> MonsterData
3112 grade 1 -> SpellBombData 3112 -> explodSpellId 13455 -> SpellLevelData
41953 -> action 99 (fire), 9..10 base damage`.
This is a static data chain, not an observed explosion or an implemented bomb
simulator. No production/test files, builds, accounts or git operations were used.
Read the two class boundary documents first; their missing lifecycle conclusions
still hold, but the explosion and wall IDs are no longer unknown.

## Source identity and reproduction

All bundles below were decoded read-only from
`%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/StreamingAssets/Content/Data/`.
The sibling `StreamingAssets/version` was read in this investigation and says
`Version=3.7.13.17`, `BuildDate=2026-11-03T14:09:52+00:00`.
Existing `docs/cos-dofus-video/rapports-v2/N01/CLIENT_SOURCES.json` and
`N01/PREUVES.md` supply the version/i18n identity and learned-spell export.

| Bundle filename | Bytes | SHA-256 read in this investigation |
| --- | ---: | --- |
| data_assets_monstersdataroot.asset.bundle | 573502 | eabd45f82da8545908d43b86761ff53eac45551c51dfa8aaf69c67442c5fcf29 |
| data_assets_spellbombsdataroot.asset.bundle | 1555 | 90b97f3ed4c4f5f692ccbeb301b6282fa20f9170be684ce76e420f19717344bc |
| data_assets_spellbombwallsdataroot.asset.bundle | 1495 | ff623b3b4fe28c2fff46744c9bda2636a03b198408c68a65c6f94b251136fc79 |
| data_assets_spellsdataroot.asset.bundle | 320298 | 96c830cd1add89cb0af1c613886a7f193fd38706d1cd27e51556bfad9b330e26 |
| data_assets_spelllevelsdataroot.asset.bundle | 1097950 | 525048d9a4406720ba79fb6a23826b9ac5e40d4a87dcd3c452e1a0ccc27b2a30 |

Reader: existing UnityPy 1.25.4 under
`%USERPROFILE%/AppData/Local/Temp/senpi-unity-reader`, using the read-only
`read_bundle` function in `N01/extract_spells.py`, not that script's exporting
main. Unity fallback 6000.3.16f1 is a reader setting, not a game-version claim.
For each root the reader verifies `objectsById.m_keys` against managed-reference
`data.id`. Counts were 5135 monsters, 10 bomb definitions, 8 wall definitions,
17006 spells and 34318 spell levels. No separate SQLite dataset version was
assumed. The reverse-engineering skill was read before inspecting shipped data.

## Learned cast and summoned template

N01 `SPELLS_3.7.13.17.csv` identifies breed 13, spell 13444, name Explobombe,
levels `[41922,41923,41924]`. `EFFECTS_3.7.13.17.csv:579-590` supplies:

| Spell grade / level ID | Effect UID | Action | diceNum | diceSide | value | mask |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1 / 41922 | 211551 | 1008 | 3112 | 1 | 0 | a,A |
| 2 / 41923 | 211552 | 1008 | 3112 | 2 | 0 | a,A |
| 3 / 41924 | 211553 | 1008 | 3112 | 3 | 0 | a,A |

These summon rows have duration/delay 0, trigger I, point zone shape 80 (P),
param1=1, param2=0. The grade-1 cast base is 2 AP, range 1..6,
maxCastPerTurn=2, maxCastPerTarget=1 (N01 LEVELS and PREUVES). This does not
prove that cost stays 2 AP with existing owned bombs: startup data below contains
a cast-cost modifier. The other cast actions are 406 and 951/951, not damage.

MonsterData id 3112 has nameId=6172, race=220, m_flags=40706,
`spells=[]`, `spellGrades=[]`, summonCost=0, scaleGradeRef=3. Its three
grades are 1/2/3 at levels 1/66/132. All have:

- `lifePoints=0`, `vitality=10`, AP=0, MP=0; offensive base characteristics
  and base damage bonuses are 0.
- Percent reductions neutral/earth/water/air=10, fire=30; flat reductions=0.
- `startingSpellId=42004`.
- `bonusCharacteristics.lifePoints=90`; strength/intelligence/chance/agility/
  wisdom=100; damageBonus/percentDamageBonus and all five elemental
  damageBonus fields=100. Other inspected bonus fields are 0.
- `characRatios` values are `[0,0], [10,0.125], [11,10], [12,0.125],
  [13,0.125], [14,0.125], [15,0.125], [19,1], [23,1], [25,0.125]`.

These are raw scaling declarations, NOT final combat statistics. In particular,
`GameDb.Monsters.cs:14-35` reads top-level lifePoints only: reusing it as bomb
life would produce zero, not 90 or a proven owner-scaled HP. The interpretation
and application order of bonusCharacteristics, characRatios and owner stats are
not established by this investigation. Do not substitute grade level for final
bomb level or apply generic summon capacity merely from summonCost=0.

Important join: 42004 is absent as a SpellData ID but present as a
SpellLevelData ID: `id=42004, spellId=13468, grade=1`. Do not look it up as
spell 42004. Its first row UID 258887, action1160, mask C,F3112,
diceNum=20509,diceSide=1 links to spell20509 grade1, level54119,
UID258899 action296, mask h,P, duration=-1, trigger I,
diceNum=13444,diceSide=0,value=1. This is concrete cost-modifier data; when it
starts/stops contributing still requires lifecycle semantics.

## Dedicated bomb and wall definitions

The crucial non-class table is `SpellBombsDataRoot`, class
`Core.DataCenter.Metadata.Spell.SpellBombData`:

```json
{"id":3112,"chainReactionSpellId":13457,"explodSpellId":13455,"wallId":2,"instantSpellId":13456,"comboCoeff":0}
```

The matching `SpellBombWallsDataRoot` / `SpellBombWallData` row is:

```json
{"id":2,"spellId":13458,"color":16711680,"linear":1,"minHop":1,"maxHop":7}
```

Thus neither spell13499 nor the summon action's spellId is the normal
Explobombe explosion. Spell13499 has its own triggered damage path but is not
the `explodSpellId` for template3112. `comboCoeff=0` is a serialized value,
not proof that combo has no effect.

## Resolvable explosion damage versus unsupported activation

Spell13455 levels41953/41954/41955 (grades1/2/3) contain immediate action99,
effectElement2, trigger I, duration/delay0, diceNum/diceSide=9/10,13/14,17/19.
The paired damage UIDs are 211601/211691, 211602/211692, 211603/211693.
Their masks are respectively `A` and `a,f3112,f3113,f3114,f5161,e92`.
Zone is shape67 (C), param1=2,param2=1, includeCarried=0,
onlyAffectIfInSightLine=0, damageDecreaseStepPercent=10,
maxDamageDecreaseApplyCount=4. Retain the inner radius; do not silently turn
the explosion descriptor into a radius-2 disk including its center.

The same grade1 descriptor also has action1017 UID280633 to spell23515 grade1
with value999 and mask h,P; action141 UID211649 on C (diceNum1); state removals
951 for 641/665; action792 UID295413 to spell23537 grade1 with value999; and
action1009 UID295685 on owned bomb masks. Actions141/1009 and the lifecycle
meaning of value999 are not supported simply by knowing damage99.

Spell13456 (`instantSpellId`, levels41956..41958) has the same damage ranges
but zone C2 with param2=0, masks A and g,e92, plus counter/1009/child effects.
It is a distinct path, not interchangeable with the placed bomb explosion.

Spell13457 grade1 level41959 (`chainReactionSpellId`) has actions406/1009:
UID259913/211694, masks a,P,F3112,F3113,F3114,F5161, zone C2 inner1;
UID259914/211695, mask a,P,F3112, zone shape81 (Q),param1=7,param2=0.
This establishes candidate propagation geometry and template filters, not an
executable once-per-chain queue or order/removal policy.

Existing `GameNameResolver.cs:556-705` reads all SpellLevelData by spellId and
grade and retains raw actions, masks, dice, trigger/delay and zones; normal
explosion damage descriptors therefore need no invented class damage profile.
`SpellResolver.cs:74-111,310-345` recognizes damage99, states, several child
casts and summon1008, but not actions1009/141/406/296/1027/1060/1078. Deferred
trigger rows are stored rather than executed. The ordinary summon path creates
1/1 occupancy with MonsterTemplate and SummonerId; it does not start the bomb
table lifecycle. Static row support is not whole-spell support.

## Wall and age/combo evidence

Spell13458 (Explomur) levels41965/41966/41967 has six action99 rows per grade,
point zone P1, duration/delay0, trigger I. State2483 selects two damage bands:

| Grade | Masks with *E2483 | Masks with *e2483 |
| --- | --- | --- |
| 1 | 21..24 | 11..12 |
| 2 | 24..27 | 12..14 |
| 3 | 30..33 | 15..17 |

Each band has three masks: `A,...`, `a,p,...,e92`,
`a,P,f3112,...,e92`. Grade1 UIDs are211607/259272/299199 and
211759/259275/299200. These point damage rows are not the geometry or trigger
policy for constructing a wall segment. The wall table provides linear/minHop/
maxHop; obstacle handling, distinct-owner linking and activation quotas still
need interpreter or replay evidence.

Concrete counter path: spell20497 level54099 (`Compteur Up`) uses state950/
951 transitions. UID258796 grants2484 when none of the listed counter states
is present; UID258797 grants2485 with mask C,E2484,f5162,f5163; UID258798
action792 calls spell20500 grade1, then UID258800 removes2484. Higher rows
continue through2486..2493,2529..2533,2751..2753. Spell20500 grade1 level54102
contains action1027 diceNum20 and action1060 diceNum5; grade2 level54103
contains40/10. Do not reduce this to a guessed damage multiplier.

Monster startup13468 level42004 also has UID295468 action1017, mask h,P,
diceNum20577,diceSide2,value999. Spell20577 grade2 level63177 UID295432
action792 calls20497 grade1 with value999 on
`a,P,F3112,F3113,F3114,F5161,*e3442`; the other four rows grant action1078
diceNum40 to template/state-filtered bombs. This is not proof of +2 every
owner turn: subscription and value999 meanings are unresolved.

Related explicit clock data: spell13500 level42047 has action792 UID260127
trigger TB ->20683 grade1, UID260128 trigger TE ->20683 grade2,
UID260270 trigger X ->20683 grade2; UID211979 trigger X ->13499 grade1.
Spell20683 grade1 level54432 UID260124 applies state2483 on h,P for duration1;
grade2 level54433 UID260125 removes it. These explain the wall's state filter
without proving which live events TB/TE/X denote or that13500 is automatically
installed on every new bomb. No such automatic installation is claimed.

## Smallest truthful integration decision

### Installed descriptor recheck on November 6

Parent ran SpellCatalogInspect for 13468/1, 20509/1, 13455/1, 13456/1 and
13457/1 (`bash_180`, exit 0) and read their complete descriptor output.
The installed spell-level bundle SHA256 is
`525048D9A4406720BA79FB6A23826B9AC5E40D4A87DCD3C452E1A0CCC27B2A30`.
Startup 13468 routes 1160 to 20509 with grades 1/2/3/4 depending on template;
20509/1 retains unsupported action296, duration -1, reference13444, value1.
Explosion13455/1 retains C2 inner1 damage99, action141 and action1009;
instant13456/1 retains C2 inner0 damage99 plus its distinct counter path.
Chain13457/1 retains action406/1009 and C2 inner1 / Q7 geometry. All five
descriptors report CanPlan=false. No grade, trigger subscription, scaling,
activation or removal semantics were inferred from that flag or static data.
This rechecks spell descriptors, not the raw monster/startup-level join or a
server explosion. No native game or account action was performed by the tool.

The useful next unit is version-bound **bomb metadata lookup and preservation**,
not autonomous explosion scoring: retain summon template/grade, resolve
SpellBombData by template and its wall row, expose exact explosion/instant/
chain/wall IDs, and preserve raw scaling and startup-level references. Existing
all-spell descriptor lookup can supply damage99 once the lifecycle explicitly
requests a known spell/grade with an authoritative damage caster and stats.
Do not execute explosion damage while resolving the placement spell.

For an observed or explicitly supplied explosion event, the ordinary damage
descriptor is reusable as a bounded component. Full spell13455 is not currently
modeled; projecting only its damage rows must be labeled a damage projection,
not a successful whole explosion or safe live detonation tactic.

Required runtime inputs are unique bomb fighter ID, observed cell/life/maxlife,
template and exact grade, authoritative owner, owner/bomb characteristic source,
current counter and relevant state presence/absence, creation event/owner-turn
clock, buff expiry and damage modifier state, detonation cause, queued/processed
chain membership, and confirmed removal. Live snapshot construction in
`LaunchService.Combat.Turn.cs:990-1003` already passes observed characteristics,
MonsterTemplate and tracked SummonerId; these are useful, but not a birth age,
counter history, startup subscription, damage inheritance formula or chain
execution history. An absent tracked owner must not become the acting Roublard
by default. Observed grade/scaling must not be reconstructed from synthetic HP1.

No numeric final HP, combo multiplier, owner-stat inheritance, wall entry damage,
detonation order or +2 age progression is admitted from this static chain alone.
The closed result is the concrete versioned data contract and exact integration
boundary, not a generic profile relabeled complete.

## Verification performed

Read-only decoder assertions passed: learned level41922 first summon diceNum
is3112; SpellBombData3112 explodSpellId is13455; level41953 belongs to13455
and its first action is99. All dedicated table joins above were inspected in
the same decoder session and matched bundle hashes. No game was launched,
attached or cast; no tests/builds were run for this documentation-only task.
