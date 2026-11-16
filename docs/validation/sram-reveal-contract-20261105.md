# Sram invisibility and reveal contract: installed 3.7.13.17

## November 6 cooldown routing check

Read-only investigation st_01a1af12 identified the legacy Lua compatibility
alias GameActionFightSpellCooldownVariationMessage -> GameActionFightEvent.
Parent independently checked that alias and searched current fields.json:
no named spell-cooldown payload field was found. The alias is routing, not
a decoded current-server reset. The current last-cast history is local; the
investigation found no server variation consumer. No reset was implemented
from generic death or from the legacy numeric message ID6219.

An authoritative current payload establishing caster, spell and reset value
or delta is still needed before modifying spell12913 history. The installed
description remains evidence of intended behavior, not wire ownership or
ordering. No cooldown or kill-attribution behavior changed in this check.

## Result and boundary

The installed spell is **12913**, not a synthetic pure action-150 spell.
It costs 2 AP, makes its target invisible, and immediately grants an allied
target MP. The current utility gate already recognizes that MP action (128).
Adding action 150 to that gate alone is therefore not an evidence-based fix
for this real spell. No blanket reveal-on-cast or reveal-on-damage rule was
established. Do not implement one from this report.

This investigation used two bounded search waves: current resolver/descriptor/
rotation and local data discovery, followed by exact installed spell/effect
decoding and focused source searches for reveal behavior. No production/test
edits, builds, git commands, live accounts, or game execution were used.

## Provenance and reproduction

Client root:
`%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3`.
`Dofus_Data/StreamingAssets/version` was read directly and says
`Version=3.7.13.17`, `BuildDate=2026-11-03T14:09:52+00:00`.

Existing extraction sources were read at
`docs/cos-dofus-video/rapports-v2/N01/extract_spells.py`,
`N01/CLIENT_SOURCES.json`, and `N01/SORTS_3.7.13.17.json`.
The script's `read_bundle` and `i18n_names` functions were then invoked in a
read-only Python eval without invoking `main` or writing output files.
`sys.dont_write_bytecode=True`; the already installed UnityPy reader was used
from `%USERPROFILE%/AppData/Local/Temp/senpi-unity-reader`.

Directly decoded installed files beneath
`Dofus_Data/StreamingAssets/Content/Data/`:

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `data_assets_spelllevelsdataroot.asset.bundle` | 1097950 | `525048d9a4406720ba79fb6a23826b9ac5e40d4a87dcd3c452e1a0ccc27b2a30` |
| `data_assets_effectsdataroot.asset.bundle` | 13347 | `84d7a2d65759418462a0fbc9c4e32c1d0c6ea75584690e8a01ee364728a1c75e` |

The managed-reference roots were `SpellLevelsDataRoot` (34318 records) and
`EffectsDataRoot` (884 records), classes `SpellLevelData` and `EffectData`
under `Core.DataCenter.Metadata.Spell` and `.Effect`, assembly
`Ankama.Dofus.Core.DataCenter`. The parser checks that each dictionary key
matches its referenced record's `id`. UnityPy warned that no valid Unity
version was embedded and used the existing extractor's fallback `6000.3.16f1`.
These are serialized-data observations, not server execution evidence or REA
Evidence records; REA tools were not exposed in this child session.

## Exact spell and effect rows

The existing current export at `N01/SORTS_3.7.13.17.json:5356` identifies
spell 12913, French name `Invisibilite` (accented in source), nameId 797777,
descriptionId 1118933, spellBreed 4. Direct installed decoding confirmed all
three level records:

| Grade | Level record | Player level | AP cost | Range | Invisible effectUid | MP effectUid | MP gain |
| ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: |
| 1 | 40916 | 5 | 2 | 0-4 | 208157 | 208233 | 1 |
| 2 | 40917 | 72 | 2 | 0-5 | 208158 | 208185 | 2 |
| 3 | 40918 | 139 | 2 | 0-6 | 208197 | 208198 | 2 |

All grades have `minCastInterval=3`, `initialCooldown=0`,
`globalCooldown=1`, `statesCriterion=""`, `m_flags=512`,
`criticalHitProbability=0`, and empty `criticalEffect` arrays. The bit meaning
of 512 is not asserted here.

Each grade has exactly two normal effect rows, in this order:

1. `actionId=150`, `targetMask="a,A"`, `duration=1`, `diceNum=0`,
   `diceSide=0`, `value=0`.
2. `actionId=128`, `targetMask="a"`, `duration=2`, `diceNum=1/2/2`
   by grade, `diceSide=0`, `value=0`.

Both rows have `delay=0`, `triggers="I"`, `random=0`, `dispellable=1`,
`effectTriggerDuration=0`, `baseEffectId=0`, `spellId=12913`.
Both use `zoneDescr.shape=80` (ASCII P), `param1=1`, `param2=0`,
`includeCarried=1`. Duration values are serialized turn-duration fields;
the precise decrement/expiry boundary was not observed on the server.
There is no action-950 spell-state application in these rows, no AP-gain row,
and no explicit reveal trigger row. Do not invent a numeric invisibility
spell-state ID from action 150 or its characteristic 24.

Direct `EffectData` decoding and French localization confirmed:

| Action key (`EffectData.id`) | DescriptionId | French meaning | Additional fields |
| ---: | ---: | --- | --- |
| 128 | 1066457 | `#1{{~1~2 a }}#2 PM` (accented in source) | characteristic 23, operator +, oppositeId 169 |
| 150 | 1106193 | Makes the target invisible | characteristic 24, hideValueInTooltip 1 |
| 202 | 1098804 | Reveals invisible entities | characteristic 0, hideValueInTooltip 1 |

`EffectData.effectId` is a different field: it is 3 for key128 and -1 for
keys150/202. Do not substitute that field for the spell row's actionId.

Installed `Content/I18n/fr.bin`, descriptionId **1118933**, reads:

> Rend la cible invisible.
> Augmente egalement ses PM si c'est un allie.
>
> Le temps de relance est reinitialise lorsque le lanceur acheve un combattant.

Accents are transliterated above. This says the cooldown resets when the
caster finishes a combatant. It does not restrict that sentence to enemies,
nor specify direct versus trap/poison/summon attribution. This is current
client-facing contract text, not proof of the reset event's server ordering.

## Reveal evidence and what it does not prove

No available current source established whether casting an attack ends
invisibility, merely exposes the cast origin, or depends on spell-specific
flags, weapon use, actual damage, trigger source, or hit success.
The current 12913 description does not answer that question.

A local emulator source offers a useful distinction, not authority:
`%USERPROFILE%/AppData/Local/Temp/givre-jondo-lab-20261102/source/`
`JondoEmu-683ab49c73f65b7c4ab7c1134c448f7fa850f6bc/`
`Jondo.Unity.Server/Handlers/FightHandler.cs:4709` updates
`caster.LastSeenCell = caster.CellId` when an invisible caster casts.
It does **not** remove action-150 rows or make the fighter Visible there.
Its `VisibleOtraVezAsync` at line6389 waits until no active visibility row
remains, citing frames316-317 of `sram-invisibilidad` for expiry. Those capture
bytes/version/actor provenance were not inspected here; the comments are
secondary evidence only. `Managers/EffectEngine.cs:4030` separately removes
visibility rows for its `DesvelaLosInvisibles` action. Installed action202's
label corroborates an explicit reveal category, not the emulator's exact
removal algorithm or a universal cast hook.

Legacy VB packet classes under
`%USERPROFILE%/Downloads/givre-GivreActive/Videos/GIVRE/GivreActive/`
`EasyBot/Packets/` expose visibility message/state storage; the bounded search
found no governing cast/attack rule there. They do not establish a current
3.7 server contract.

`N07/SOURCE_COMPARAISON.json` records no spell/level/effect differences for its
four-class set between exported 3.7.12.12 and 3.7.13.17. This pre-existing
comparison was read, not rerun. It is not evidence that old Dofus 2 emulator
reveal rules apply, and does not establish runtime behavior.

## Narrow implementation candidates and unresolved inputs

The already modeled immediate portion can be evaluated with a faithful
12913 descriptor: hide the selected fighter, and give only allied targets
1/2/2 MP. `SpellResolver.Resolve` already implements action150 and action128;
`AutoSpellRotation.ShouldUsePlanner` already includes action128 for self/ally
targets. The effect's `a,A` mask means this is not exclusively a self buff.
`SpellDescriptor.Classify(150)` returns Unknown, but the immediate resolver
uses action IDs rather than requiring that classification. A data-faithful
rotation-to-resolver regression is a narrower next candidate than adding a
universal reveal hook or a pure-invisibility admission exception. First inspect
the selected catalog's actual descriptor conversion and installed data version;
that conversion was not traced to completion by this bounded investigation.

A distinct real gap is the described **kill-triggered reset of spell12913's
cooldown**. A narrow implementation could target that cooldown only, once the
authoritative kill event attribution and cooldown owner are traced. The
generic descriptor currently carries only `MinCastInterval`/`InitialCooldown`,
and the immediate resolver has no cooldown-reset event. Do not implement a
global cooldown reset, or assume that only direct enemy kills count.

Remaining required inputs before a reveal/expiry implementation:

- Version-attributed server observations or a current authoritative rule for
  full reveal versus origin detection on damaging spells, utility spells,
  weapons, misses/zero damage, indirect damage and triggered casts.
- Any relevant spell flag/property and its verified decoder; action150 alone
  does not specify cast-breaking policy.
- Duration decrement owner and exact expiry ordering, independently for
  action150 duration1 and action128 duration2, including overlapping buffs
  and dispels.
- Kill attribution and reset ordering for the description's cooldown rule.

The defensive targeted/AoE scoring gap remains separate and is not solved by
these findings. No production implementation is justified for reveal-on-cast
from the evidence collected here. The installed data contract above is
defensible; the runtime reveal contract remains explicitly unresolved.
