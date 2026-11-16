# Observed immunity producer contract, 2026-11-05

Parent raw-classification fields verification: mon_WBV6J183WJDZRAF7 /
bash_134, fresh isolated Release compilation/tests: exit 0, 88 passed,
zero failed/skipped, 400 ms. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-raw-state-fields-20261105.
Filter RawStateMessageReaderTests|FightVisibilityTests. LSP reader errors: none.
RawGdqa/RawGdqc remain nullable raw evidence, not an eligibility decision or
live state projection. Broader immunity task remains open.

Parent extended-reader verification: mon_6B9SGET8H3QMJAG3 / bash_126,
fresh isolated Release compilation/tests: exit 0, 74 passed, zero failed/skipped,
195 ms. Artifacts: %USERPROFILE%/AppData/Local/Temp/givre-parent-raw-state-variants-20261105.
Filter RawStateMessageReaderTests|FightVisibilityTests. Parent read updated
source and variant tests. LSP reported no errors on the reader. This accepts
jth parameter0 and jsq31/jrl request observations only, not execution of bulk
filtering, state activity, expiry or live planner integration.

Parent isolated reader verification: mon_ZED8V1NJ8RECFBS7 / bash_125,
fresh Release compilation and filtered RawStateMessageReaderTests plus
FightVisibilityTests: exit 0, 42 passed, zero failed/skipped, 448 ms.
Artifacts: %USERPROFILE%/AppData/Local/Temp/givre-parent-raw-state-20261105.
Parent read the entire source and test file. LSP freshness timed out.
Acceptance is limited to raw juh/juu observations, not active state knowledge,
live ingress, damage-rule projection, expiry, or automatic combat immunity.

## Result

There is no existing decoded active-state store to connect to the live
planner. The missing connection is not just `StateDamageRules`: it includes
version-grounded actor/state/effect identity and state removal/expiry semantics.
The current evidence does not support implementing that decoder correctly.
No production code, mapping, test, catalogue row, or state ID was changed.
This investigation ran no game, build, test, or git command.

## Exact current mapping evidence

These are the checked-in `data/matching_current.json` and `data/fields.json`
entries, not proof of the runtime streamed bundle or of field semantics.

| Code -> semantic message | Semantic field names -> wire numbers | Raw-code field names -> wire numbers |
| --- | --- | --- |
| `juh` -> `FightBoostAddEvent` | `fvvk=1` | `fxet=1` |
| `juu` -> `FightBoostRemoveEvent` | `fvuv=1`, `character_id=2` | `fxhi=1`, `fxhj=2`, `fxhk=3`, `fxhl=4` |
| `jth` -> `FightBoostUpdateEvent` | `fwiq=1`, `fwir=2`, `fwip=3` | `fwys=1`, `fwyt=2`, `fwyu=3` |
| `jun` -> `FightBoostUpdateStateEvent` | `fvtf=1`, `fvte=3` | `fxgf=1` |

`FightRemovableEffect` has anonymous fields `gcdm=1`, `gcdn=2`, `gcdt=3`,
`gcdw=4`, `gcdo=5`, `gcdp=6`, `gcdz=7`, then named `target_id=8` and
`dissipation_state=9`, followed by `gcdq=10`, `gcdu=11`, `gcdr=12`,
`gcds=13`, `gcea=14`, `gcdx=15`, `gcdy=16`.
`FightRemovableEffectExtendedInformation` names `effect=1`, `action_id=2`,
`source_id=3`, with anonymous `gcby=5`.
There is no confirmed named state ID, effect instance ID, duration, or
add/remove flag in those entries. `dissipation_state` is not established as
the fighter's gameplay state ID. The nesting of the add payload into those
effect types is not established by a field-number table alone.

The semantic/raw shape disagreements on `jun` and `juu` are material. Do not
combine names from one shape with numbers from another. `ProtocolCatalog`
resolves incoming codes through the selected matching and reads field numbers
through `FieldNumber`; that method deliberately does not infer beta-proto
numbers (`GivreClone.Protocol/ProtocolCatalog.cs:157`).

Prior artifact work in `combat-state-artifact-20261105.md` corroborates only
that `jth` contains two int32 values and one int64, and that the inspected
`jun` serializer writes a repeated collection at field 1. It does not assign
state, actor, duration, or removal meanings. Its traced internal
`EntityStateInfo(stateId, duration, status, isStateIcon)` construction has not
been connected to wire fields. This report does not promote that internal
model to a protocol contract.

## Decoded observation and lifecycle trace

`LaunchService.Packets.cs` handles fighter position, death, breed, visibility,
summons, roster, forced moves, marks, turn ownership, control, resources and
characteristics. No handler for the four boost messages or a gameplay state
add/remove/duration reducer is present in that file. Searches of the app,
protocol and engine C# sources found no alternative active-state decoder/store.
The engine's `GameState` also contains only identity/map/fight/turn knowledge,
not gameplay states (`GivreClone.Engine/GameState.cs`).

Existing actor-keyed stores are not substitutes:

- `_fighters` contains only `(Cell, ActorId)`; `_fighterStats` contains
  resources and characteristics. Neither supplies active gameplay state IDs.
- `FightVisibilityReducer` observes the separate `invisibility_state` contract;
  its positive/negative position knowledge is not general state knowledge.
- `_fightMarks` stores ground marks, not fighter states.
- `ApplySpellModifierEvent.actor_id` is decoded in
  `LaunchService.Packets.cs:1231`. It confirms a modifier event for that actor.
  `LaunchService.Combat.SpellMods.cs:54` then derives modifier effects from the
  last cast and stores them in `(Pid, Actor)` `SpellModifierSet`. Its
  `ExpireAt(turn)` uses the acting fighter's local turn count, called from
  `LaunchService.Combat.Turn.cs:178`. This is not an observed state list,
  cannot attribute an arbitrary boost to a state, and proves no state absence.
- `StateCriterion` parses spell eligibility expressions; the packet handler
  only renders their descriptions (`LaunchService.Packets.cs:1438`). It does
  not observe states.

The only executable state add/remove logic found is simulation:
`SpellResolver.cs:425` handles descriptor actions 950/951/952 by editing its
copied target `States` and `AbsentStates`. It does not persist them into
`LaunchService`. Its facts are conditional predicted outcomes, not confirmed
server observations. Descriptor duration or a successful cast send cannot
establish live application, expiry, or absence.

Therefore there is currently **no reliable production source of either
positive active gameplay-state knowledge or negative gameplay-state knowledge**
for this immunity connection. A catalogue row is reliable only as a property
of the selected catalogue: `GameDb.Spells.cs:15` returns nullable
`SpellStateDamage(stateId)` with `Invulnerable`, `InvulnerableMelee`,
`InvulnerableRange`, `CantDealDamage`. Missing table/row returns null, not a
known all-false rule. Catalogue/client version admission remains a separate
unresolved prerequisite identified in the prior artifact note.

## Real player and controlled-companion producer

Both use the same `RunFightTurn` adapter. At
`LaunchService.Combat.Turn.cs:988`, each observed actor becomes a
`PlannerFighter`; no `States`, `StatesKnown`, or `AbsentStates` arguments are
supplied. At line 1081, `CombatTurnSnapshot` is constructed without
`StateDamageRules`. `GameNameResolver` currently has no `SpellStateDamage`
lookup wrapper. Both data paths are missing.

`RunFightTurn` sets `_engineControlled.Value` from its explicit controlled
context (line 146). `SelfActorId` returns that context's actor first
(`LaunchService.Combat.cs:941`). Server-granted control is recorded by
`ObserveControlledFighter` and `ObserveServerFightTurn`, and
`RunAutomaticFightTurn` passes that context into `RunFightTurn`
(`LaunchService.Combat.Controlled.cs`). Thus a future state lookup must use
each `f.ActorId`, including the negative controlled actor, not `CharacterId`,
master ID, template, roster sign, or controller identity. Both snapshots use
the same full actor roster; only the caster identity changes.

`PlannerFighter.StatesKnown` is nullable, and the adapter interprets null as
`States is not null` (`CombatTurnPlanner.cs:202`). Supplying a partial or empty
set without explicitly supplying `StatesKnown: false` would falsely claim
completeness. `HasSameStateKnowledge` already compares presence, absence and
completeness, and the real walk-continuation check calls it at
`LaunchService.Combat.Turn.cs:1098`.

## Minimum correct connection, conditional on missing evidence

First establish a version-matched protocol contract identifying:

1. The add message's nesting, target actor, state ID, effect-instance ID and
   whether application is effective immediately or at a later hook.
2. Removal/update identity and whether removal ends one instance or the entire
   state; overlapping sources must not be erased by removing one instance.
3. Duration units, sentinel values, whose turns advance duration, exact
   beginning/end hooks, duration modifications, dispel handling and ordering.
   An actor-local automatic-turn counter cannot be assumed equivalent.
4. Whether any message is an authoritative complete state snapshot. Until
   proved, no message establishes `StatesKnown=true`; joining mid-fight,
   missing mappings and unobserved states remain unknown.

Only then add a protocol reader and a session/fight/actor-keyed lifecycle
reducer. Preserve confirmed presence separately from confirmed absence and
completeness. Removal/expiry may establish absence only when the contract
rules out another surviving source; losing freshness establishes unknown,
not absence. Clear the store on the existing fight/session purge boundary
(`LaunchService.Combat.cs:245`) and actor death, and handle identity reuse.

The producer edit itself should be small: at each existing planner decision,
copy reducer knowledge for each `f.ActorId` into `States`, explicit
`StatesKnown`, and `AbsentStates`; attach an immutable `StateDamageRules`
dictionary from the admitted catalogue. Include rules required by state
applications in reachable spell descriptors/lookup results as well as states
already present, or later simulated application cannot use its rule. Omit
unknown rows rather than fabricating all-false entries. Preserve unknown
knowledge when no observation exists. Do not copy simulated states back to
the observation store or enable `ForcedMovementSafe` from this work.

Current damage handling consumes only `Invulnerable` and `CantDealDamage`
(`SpellResolver.cs:160`, `CombatTurnPlanner.cs:301`). Melee/range flags are
carried but not implemented. Neither consumer treats an unobserved active
immunity as confirmed absent, although both may still predict damage in its
absence; this connection alone would not create a conservative unknown-state
damage policy. Classic/script rotation paths are also outside these planner
guards. Do not describe the proposed adapter as universal immunity safety.

## Regression boundary

Existing read tests establish catalogue null-vs-known distinctions
(`SpellStateCatalogTests`), synthetic active/absent immunity, shield and
life-steal behavior and effect ordering (`SpellResolverTests`), no useless
attack/approach (`CombatTurnPlannerTests`), and presence/absence/completeness
invalidation (`CombatPlannerResolverTests.Walk_continuation_requires_unchanged_state_knowledge`).
They do not establish server decoding, duration or a live producer connection.

`ObservedMonsterGradeDispatchTests.Automatic_turn_snapshot_preserves_actor_keyed_decoded_grades`
is an existing suitable producer-test pattern: it drives the real
`RunFightTurn` for player and controlled companion, sets zero resources to
obtain the existing serialized empty-plan snapshot, subscribes to its exact
log emission before invocation, and uses a bounded task timeout without
sleeping or running a game. Its grade-specific publisher is not a state
packet decoder; a new regression must traverse the actual admitted state
packet ingress and reducer before asserting the snapshot.

Required future cases are unknown initial knowledge; exact positive add;
explicit removal; overlapping/reapplied instances; duration update/expiry at
the proved actor hook; absent-to-present invalidation; distinct player,
companion and enemy actor IDs; death/end/session reset; missing mapping and
unknown catalogue row; and post-removal replanning. Assert both actor state
knowledge and the snapshot rule dictionary, then productive-versus-blocked
planner behavior. Do not pin this prose with a test.

No tests were run for this documentation-only investigation. The exact next
prerequisite is evidence resolving the four protocol contracts above, not a
guessed boost decoder or a snapshot with fabricated empty states.

## Follow-up: native serializer and effect-builder path recovered

The follow-up used the installed `reverse-engineer-anything` skill, the
existing address-annotated Cpp2IL extraction, its already compiled
`NativeAddressLookup.dll`, and ordinary Python Capstone/pefile analysis.
No target assembly was loaded or executed. No build, game, network account,
mapping edit or implementation was performed. These are ordinary static
analysis observations, not a new REA Evidence bundle. REA MCP tools were not
exposed in this child; the earlier managed representation-limit failure was
not repeated. A bounded ECMA-335 table reader recovered selected field and
method signatures directly in memory without editing or creating a tool.

Artifact identity:

- Native input: `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/GameAssembly.dll`,
  SHA-256 `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591`.
- Existing extraction root:
  `%USERPROFILE%/AppData/Local/Temp/givre-state-cpp2il-20261104/dll-addresses`.
- Its protocol DLL SHA-256 is
  `a41cf993a50418f4cb6aa64bf54eee641331685b029a5d2053ff146703af5d1e`;
  its Core.dll SHA-256 is
  `3979aa3e740a9821126379fd0c32f21422792c7f9096887526a9d27c965297c3`.
- The installation is the prior investigation's 3.7.13.17 reference. The
  inspected `.release.infos.json` itself names only game/release, not a version;
  this follow-up did not independently recover version provenance from it.

### Concrete wire nesting

The extracted **field type signatures**, not matching-name speculation, say
`juh.errz : lng` and `lng.fcwk : llg`. Recovered serializer bodies establish:

| Native type / body RVA | Object access and actual protobuf write | Conclusion |
| --- | --- | --- |
| `juh.chqn`, `0x7F20F0` | `0x7F210A` loads tag `0x0A`; `0x7F2114` loads `[rbx+0x18]`; `0x7F211E` calls nested-message writer | `juh` field 1 is its `lng` payload |
| `lng.cuuj`, `0xA60870` | tag `0x0A` at `0xA6088A`, load `[rbx+0x18]` at `0xA60894`, nested-message write at `0xA6089E` | `lng` field 1 is its `llg` effect |
| same | tag `0x10`, `[rbx+0x20]` at `0xA608B6` | `lng` field 2 is int32 action ID, consistent with the named field table |
| same | tag `0x18`, `[rbx+0x28]` at `0xA608D8` | `lng` field 3 is int64 source ID, consistent with the named field table |
| `llg.cucn`, `0xC62180` | tag `0x08` at `0xC621C6`, int32 `[rbx+0x18]` at `0xC621D0` | effect field 1 is the int32 value traced below into the state query |
| same | tag `0x28` at `0xC6224F`, int32 `[rbx+0x38]` at `0xC62259` | effect field 5 is the base buff-instance ID traced below |
| same | tag `0x40` at `0xC622DC`, int64 `[rbx+0x48]` at `0xC622E6` | effect field 8 is target ID, agreeing with the named field table |

Thus the recovered add envelope is **`juh.#1 (lng).#1 (llg)`**. The target
path is **`#1.#1.#8`**; the state-value path is **`#1.#1.#1` for state-effect
actions**, not for every arbitrary removable effect; the base instance-ID
path is **`#1.#1.#5`**. `#1.#2` is action ID and `#1.#3` is source ID.
These are build-local facts. The earlier tables' anonymous names do not need
to be guessed or changed to express these coordinates.

### Actual semantic-use call sites

The previously unresolved getter is now attributed to `hq.nfk`, RVA
`0x26BF8B0`. Its recovered body dispatches to an underlying effect's virtual
value getter and unboxes an int32; it is **not itself a named state getter**.
The evidence that its value is used as a state ID is the independent native
query in `eak.vcx`: `0x27108AD` loads `[buff+0x38]`, `0x27108BC` calls
`hq.nfk`, and `0x27108C1..C4` reloads the saved requested state ID and compares
it to the returned value. This block was decoded from the known exception
fragment entry `0x27107E4`, not from a guessed instruction boundary.

The bridge to the wire value is a recovered **actual body**, not the name of
a purported handler:

- `eai.vbu`, RVA `0x270E040`, has extracted signature
  `hq (llg, ht, ActionId, int32, bool)`. At `0x270E11A` it loads int32
  `[llg+0x18]` (wire field 1), boxes it, and at `0x270E142` calls the
  underlying effect's virtual setter at class slot `0x1C8`.
- `hq.nfk` reads the corresponding virtual getter slot `0x1B8` at
  `0x26BF8C5..D3` and unboxes the value at `0x26BF912..17`.
  `eai.Update(llg, dzx, ref BuffChange)` independently corroborates the
  exact correspondence: `0x270BA21` calls `hq.nfk` and `0x270BA26` compares
  its result directly to `[llg+0x18]`.
- `eai.lsf`, RVA `0x270CB70`, has extracted signature
  `dzx (int64, ActionId, llg, bool)`. It receives `llg` in R8, preserves it
  in R14, loads field 5's int32 from `[r14+0x38]` at `0x270CBE3`, and
  constructs a packed scoped value: nonnegative ID occupies its low 32 bits,
  scope 1 its high 32 bits (`0x270CC3B..4D`); negative IDs take a static
  sentinel branch, whose meaning is not newly asserted here.
- At `0x270CD2B` it passes the same `llg` to `eai.vbu`, called at
  `0x270CD38`. At `0x270CC9B` it loads int64 target field 8 from
  `[r14+0x48]`. At `0x270CDDD` the packed instance value becomes constructor
  argument 1; at `0x270CDE5..F0` the target becomes argument 5. The call at
  `0x270CE07` is `dzx`'s constructor at `0x2705B80`, whose extracted
  signature begins `(BuffId, ht, hq, int64, int64, FightTurnWindow, ...)`.
  `BuffId` has named `baseId` and `scope` fields and constructor parameters;
  therefore field 5 is not a gameplay state ID or an actor ID.
- In that constructor, `0x2705C66` obtains the action from the `hq` wrapper;
  `0x2705C70` calls predicate `0x275A0D0`. The recovered predicate returns
  true exactly for action values 950, 951 and 952. On that branch,
  `0x2705C7E` calls `hq.nfk` and `0x2705C9C` passes its returned value to
  `hs`'s constructor. Together with the state query, this grounds use of
  effect field 1 as a state key for those state-effect actions. It does not
  prove that all three represent positive application.

There is also an actual wrapper-to-effect call:
`eai.Update(lng, dzx, ref BuffChange)` at `0x270BC30` loads
`[lng+0x18]` at `0x270BC39` and tail-jumps at `0x270BC44` to the `llg`
update body at `0x270B960`. This independently confirms the metadata nesting.

### Remaining admission boundary and stop

This materially new path succeeded: it recovered concrete add-envelope
nesting, state-value coordinates, target coordinates and scoped instance
coordinates using serializer and consumer bodies. The initial report's
blanket statement that no state-ID field is established is superseded by
this section for this exact artifact and state-effect action branch.

It has **not** recovered the `juh` dispatch handler calling the builder,
the positive-versus-removal action semantics, a duration/expiry contract, or
authoritative state completeness. No direct or tail-call edge into
`eai.lsf` was found by the bounded instruction-aligned scan; its native
address occurs as a pointer at RVA `0x690B850`. That is evidence of an
indirect-call boundary, not proof that the handler is missing or unavailable.
The recovered `eai.vbu` and serializer bodies are real machine code; naming
an unidentified `juh` handler from these results would still be speculation.

To make the positive producer implementable, the next independent evidence
must resolve `juh`'s registered consumer and its indirect dispatch to this
builder (including action handling), then the client's corresponding
instance-removal and duration-hook consumer bodies. An address-linked
IL2CPP method-registration/type-vtable view or focused native consumer trace
would resolve that boundary. The current evidence must not be turned into
`StatesKnown=true`, or used to persist state presence indefinitely. Per the
follow-up stop condition, this successful new evidence path ends here;
no further reverse-engineering branch or implementation was started.

## Registration follow-up: concrete juh consumer recovered

This bounded follow-up followed original IL2CPP registration and metadata,
not another direct/tail-call search. It recovered a concrete registered
consumer chain. The method-pointer entry at `0x690B850` is not a vtable call
site: it is an entry in Core.dll's codegen-module method-pointer array.
The actual recovered `juh` consumer calls a different builder entry directly.

### Inputs and identity

The same `GameAssembly.dll`, protocol DLL and Core.dll paths and SHA-256
values listed above were used. Additional shipped input:

`%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/Dofus_Data/il2cpp_data/Metadata/global-metadata.dat`

Its SHA-256 is
`ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`,
length 43,768,684 bytes, magic `AF 1B B1 FA`, version 39. Header sections
are offset/byte-count/row-count triples, not older offset/count pairs.
Original methods occupy offset 8,739,072, byte count 12,562,016, 392,563
32-byte rows. Original type definitions occupy offset 35,469,148,
3,646,130 bytes, 44,465 82-byte rows. Original strings start at 2,111,524.
These were read statically in memory. No new parser file or build was made.

### Original registration, not rebuilt assembly token order

The exact NUL-terminated native module name `Core.dll` is at RVA `0x5BA2CC8`.
The native codegen-module record at `0x5BA2C40` contains:

| Record offset | Observed value |
| --- | --- |
| `+0` | VA `0x185BA2CC8`, pointer to `Core.dll` |
| `+8` | method count `0x1BA63` |
| `+16` | VA `0x1868EAD30`, method-pointer array RVA `0x68EAD30` |

The original metadata method row 16,740 named `lsf` has original token
`0x06004165`. Array index `0x4165-1` selects RVA `0x690B850`, containing
VA `0x18270CB70`, exactly `eai.lsf`. Thus the pointer is grounded through
the module registration, not guessed from nearby pointer order.

Important qualification: the rebuilt extraction's ECMA MethodDef row for
`lsf` is 15,809, not the original row/token. Trying to index the native table
with rebuilt MethodDef tokens produced a mismatch and was rejected. Only
the shipped metadata's original tokens were used for the results here.

The extracted Core method signatures identify `gau.bizt(juh)` and
`gau.orc(juh)` as bool-returning instance consumers. Their original metadata
rows/tokens are 75,328 / `0x06012641` and 75,409 / `0x06012692`. Their
registered native slots are `0x697DF30` and `0x697E1B8`; both contain
VA `0x181E972B0`. This agrees with the address annotations but independently
grounds the body through shipped registration.

The actual registration code at `gau.biwz`, RVA `0x1E8D980`, constructs a
delegate using metadata-use slot `0x6B4BC78`. Its initial encoded value is
`0x60024C81`: method-definition usage, decoded original method index 75,328,
which is the `juh` consumer above. At `0x1E8E01C` that slot is loaded into
R8, RDX is the `gau` instance at `0x1E8E026`, and `0x1E8E02F` calls the
delegate constructor `0x38E14B0`. That constructor stores the method info
at `[delegate+0x28]` (`0x38E14D2`) and target at `[delegate+0x20]`
(`0x38E14D9`). At `0x1E8E052` the resulting delegate becomes RDX, and
`0x1E8E055` calls the generic registration method `0x345D1C0`.
The generic method context is still encoded at that call; its complete
instantiation was not reconstructed, so this report does not invent a
runtime registration key. The delegate method identity and actual registered
consumer body are grounded independently by the original metadata.

### Actual consumer call chain

Recovered `gau.bizt/orc` body at `0x1E972B0`:

```asm
01e972b9 mov rax, qword ptr [rdx + 0x18] ; juh.#1, lng
01e972c2 mov r9,  qword ptr [rax + 0x18] ; lng.#1, llg
01e972c6 mov r8,  qword ptr [rax + 0x28] ; lng.#3, source
01e972ca movzx edx, word ptr [rax + 0x20] ; lng.#2, ActionId
01e972d7 call 0x1e956b0
```

`gau.bizc` at `0x1E956B0` preserves that effect/source/action, then passes
effect in R8, action in EDX, source in RCX and `false` in R9 to
`eai.vbp`, called at `0x1E95701`, native body `0x270D930`. There is **no
950/951/952 branch in this forwarding consumer**. Unlike the earlier
investigated `eai.lsf`, this concrete consumer's builder is `eai.vbp`.

The recovered `vbp` body follows the same grounded network-effect conversion:

- `0x270D9A3` reads `[llg+0x38]`, wire field 5, for the scoped instance ID.
- `0x270DA5B` reads `[llg+0x48]`, wire field 8, for target ID.
- `0x270DAE2` loads the forwarded ActionId, `0x270DAEB` passes `llg` in
  RCX, and `0x270DAF8` calls `eai.vbu` at `0x270E040`. Its previously
  recovered field-1 state-value conversion therefore belongs to an actual
  `juh` consumer chain, not merely an orphan constructor path.

After the builder returns, `gau.bizc` allocates a concrete `dum` object at
`0x1E95719`, passes the built buff at `0x1E95724`, and calls its shared
constructor at `0x1E9572D` (`0x205D0A0`). The allocation's encoded type usage
is `0x20030315`, type index 98,698; original type row 2,979 named `dum` has
byval type index `0x1818A` = 98,698. This disambiguates the shared constructor
from four other types at the same address. It is then submitted through
`gau.bizj` at `0x1E9573B`. Subsequent `dum` bodies contain additional
interface calls; no unproven interface target is labelled a state insertion.

### State-action distinction: recovered facts and limit

Original metadata, not historical ID conventions, names these exact actions:

| Action field | Original field row | Default-data offset | Raw uint16 | Value |
| --- | --- | --- | --- | --- |
| `FightSetState` | 175,893 | 3,067,355 | `B6 03` | 950 |
| `FightUnsetState` | 175,894 | 3,067,357 | `B7 03` | 951 |
| `FightDisableState` | 175,895 | 3,067,359 | `B8 03` | 952 |

Fields are in the original field section at offset 30,085,240 (12-byte rows).
Default records are in the field-default section at 21,536,720; their data
base is 22,214,024. Reading these two-byte enum values as int32 would combine
adjacent defaults and was rejected. Names establish the declared distinction;
the following recovered body independently establishes different execution
paths, rather than assuming all three mean presence or immediate deletion.

In the actual state query `eak.vcx`, after matching the requested state ID,
`0x2710904` calls the effect action getter. The body then performs:

```asm
0271090c sub ecx, 0x3b6
02710912 je 0x2710aca     ; 950
02710918 sub ecx, 1
0271091b je 0x27109fa     ; 951
02710921 cmp ecx, 1
02710924 jne 0x2710b8d   ; 952 falls through at 0x271092a
```

The 950 branch tracks its own boolean/duration pair and contributes status
1 at `0x2710B79`; 951 tracks a separate pair and contributes status 3 at
`0x2710AC0`; 952 tracks another pair and contributes status 2 at
`0x27109F0`. These contributions call the same combining function at
`0x2710B83`; the final combined status is passed in R9D at `0x2710C0F`
to `EntityStateInfo(stateId, duration, status, isStateIcon)` at `0x2710C21`.
The metadata names and body agree that 950 is the set-state action, 951 the
unset-state action, and 952 a distinct disable-state action. **952 must not
be collapsed into 951 merely because both differ from set.**

This does not yet prove an ingress reducer should immediately add on 950
and destructively remove on 951/952. The query represents coexisting
set/unset/disable contributions as distinct statuses; it does not show an
instance-removal mutation for 951 or 952. Status combination, overlapping
instances, the final active-state predicate, duration/hook progression and
expiry remain unsupported. No negative knowledge or complete-state fact
follows from this work.

### Bounded conclusion

The requested consumer boundary is now grounded:
**original method registration -> registered `gau.bizt(juh)` delegate ->
body `0x1E972B0` -> `gau.bizc` -> `eai.vbp` -> `eai.vbu`**.
The earlier `lsf` pointer is resolved as a module method entry, not evidence
that the recovered handler calls `lsf`. The concrete state-action dispatch
and separate statuses are recorded above; immediate-removal semantics are
not claimed. This successful concrete chain meets the bounded stop condition.
Only this evidence document was appended; no mapping, code, game, account,
build or git action was performed.

## Active-state follow-up: status precedence and queried-active predicate

This bounded unit recovered the combiner, active predicate, and two direct
query consumers as actual instructions. It used the same installed native
input, original version-39 metadata and existing address-linked extraction
identified above. No direct/tail-call search for the old builder boundary
was repeated. The registered method-pointer array was used to bind each new
body to original Core method tokens. No code, mapping, game, build or git
action was performed; only this document was appended.

### Registration coordinates

These original tokens index Core.dll's method-pointer array at RVA
`0x68EAD30`, using `(token low24 - 1) * 8`. The selected slots contain
the corresponding image-base-plus-body VA:

| Original token | Native pointer slot RVA | Body RVA | Metadata attribution |
| --- | --- | --- | --- |
| `0x06004551` | `0x690D7B0` | `0x27514E0` | `ecc.Update(current, next)` |
| `0x06004542` | `0x690D738` | `0x273B620` | `EntityStateInfo.vsk() : bool` |
| `0x06004543` | `0x690D740` | `0x273B600` | `EntityStateInfo` constructor |
| `0x06004AAA` | `0x6910278` | `0x27D4AD0` | `egy.wtz(int32) : bool` |
| `0x06004168` | `0x690B868` | `0x27106D0` | `eak.vcx`, state-info query |
| `0x0600418D` | `0x690B990` | `0x2710D70` | `eak.vda` |
| `0x06004171` | `0x690B8B0` | `0x2710E30` | `eak.vdb` |
| `0x0600429C` | `0x690C208` | `0x271B390` | `ebd.vhp` |

The shipped metadata is multi-assembly; identical method tokens in other
images are not additional Core attributions. The existing extraction
independently attributes these exact RVAs. Status enum `ecb` declares
`dlmy=0`, `dlmz=1`, `dlna=2`, `dlnb=3`; its names are obfuscated, so no
semantic meaning is inferred from their spelling.

### Combiner: highest numeric status wins

The entire actual body called by `0x2710B83` is:

```asm
027514e0 cmp edx, ecx
027514e2 cmovg ecx, edx
027514e5 mov eax, ecx
027514e7 ret
```

It returns the signed maximum of current (ECX) and next (EDX). Query
`eak.vcx` initializes current status to zero at `0x2710733..3D`. For
matching eligible effects, the already recovered branches contribute
950 -> 1 (`0x2710B79`), 951 -> 3 (`0x2710AC0`), 952 -> 2
(`0x27109F0`), and call this combiner. Consequently the status precedence
is **unset (951) > disable (952) > set (950) > no contribution**. It is
order-independent for status; a later set contribution does not override
a retained unset or disable contribution.

This applies to the query's eligible contributors, not every observed buff
unconditionally. Before dispatch, `0x27108C4` compares the effect value to
the requested state ID; `0x27108D1` calls eligibility helper `0x2706D10`
and skips on false; `0x27108E8` calls `dzx` flag test `0x2706580` with mask
2 and skips on false. The latter helper's actual body reads `[buff+0x70]`,
ANDs it with the requested mask, and compares equality. This unit does not
rename those eligibility rules or equate them with a recovered expiry hook.

### Final active predicate

Constructor `0x273B600` establishes the value layout by instructions:
`[this+0]=stateId` at `0x273B608`, `[this+4]=duration` at `0x273B60A`,
`[this+8]=status` at `0x273B60E`, `[this+12]=isStateIcon` at `0x273B605`.
The active predicate's complete meaningful body is:

```asm
0273b620 cmp dword ptr [rcx], 0
0273b623 jbe 0x273b635
0273b625 cmp dword ptr [rcx + 8], 1
0273b629 jne 0x273b635
0273b62b mov ecx, dword ptr [rcx + 4]
0273b62e xor edx, edx
0273b630 jmp 0x27d4ad0
0273b635 xor al, al
0273b637 ret
```

Thus status must equal **1**, not merely be nonzero. The first guard is
literally an unsigned `stateId > 0` test; it must not be silently described
as a signed validity rule for arbitrary malformed IDs. No packet ID
validation is inferred here.

Duration-validity helper `0x27D4AD0` returns true for -1 at
`0x27D4AD8..DB`, otherwise calls `rw.pym` at `0x27D4ADF`. That helper
is `cmp ecx,0x3F; setge al; ret` (`0x1CD8C10..16`). If false, the caller
returns the signed test `duration > 0` (`0x27D4AE8..EA`); if true it
returns true. Therefore the observed boolean helper reduces to
`duration == -1 || duration > 0` for int32 inputs. The threshold branch
does not change this boolean result. This recovers an instantaneous
validity predicate only, **not duration units, decrement rules, sentinels'
lifetime meaning, or expiry progression**.

### A queried-active consumer, not a guessed property name

`eak.vda` body `0x2710D70` receives the state ID in EDX, preserves it,
copies its 32-byte query value, and passes the ID in R8D at `0x2710DE7`.
At `0x2710E02` it calls `eak.vcx` (`0x27106D0`) with a result buffer.
It copies the returned `EntityStateInfo` at `0x2710E0E..11`, calls the
predicate at `0x2710E16`, and returns AL unchanged. This is a concrete
boolean consumer of the final query, independent of the obfuscated method
name. `eak.vdb` similarly obtains the buff query value at `0x2710E7B`,
calls the same query at `0x2710EE6`, then the predicate at `0x2710EFA`,
and returns AL unchanged.

An independent change classifier `ebd.vhp`, `0x271B390`, compares old and
new `EntityStateInfo`. After equality handling, it evaluates the predicate
for old at `0x271B3D9` and new at `0x271B3E6`. When active booleans differ,
it evaluates new again at `0x271B404`, then returns 1 when new is active
or 3 when new is inactive (`0x271B409..18`). When both active booleans
agree but the information differs, it returns 2 (`0x271B3EF`). No enum
names are assigned to those classifier codes here. The actual tests prove
that the predicate is used to distinguish activation from deactivation,
not just icon visibility or presence of any status record.

### Instance-aware reducer contract supported by these bodies

For a state with an eligible set contribution of valid duration and no
eligible suppressor, queried-active is true. Retaining any eligible disable
contribution raises status to 2 and makes it false; retaining any eligible
unset contribution raises status to 3 and makes it false. Unset dominates
disable, although both are inactive under the recovered predicate. Multiple
set contributions remain status 1. The query selects the duration/icon pair
for the winning status (`0x2710BA6..BF8`) before constructing the result;
this unit does not establish how every contributor's duration is advanced.

An instance-aware model must therefore retain set, unset and disable
contributions under their own instance identities and combine eligible
contributions on query. **951/952 are suppressing contributions in this
query, not demonstrated destructive deletion of a stored 950 instance.**
Reapplication/order alone cannot clear a suppressor. If a separately proved
instance-removal event removes a suppressor, recomputation can reveal a
surviving eligible set; removing one set instance must not erase other set
instances. These are consequences of the recovered max/status-1 predicate,
not a newly invented lifecycle policy.

Only when the eligibility and validity of all relevant surviving
contributions are established may this be projected into live positive or
negative knowledge. Missing packets, unknown eligibility, unknown instance
removal, missing duration progression and incomplete snapshots still do not
establish absence. Expiry and completeness remain unsupported independently.
The combiner and queried-active consumer are now grounded, so this bounded
unit stops without implementing a reducer or starting another lifecycle trace.

## Registered instance removal through the actual store mutation

This bounded follow-up uses the same installed 3.7.13.17 artifact and hashes
recorded above. Addresses below are RVAs in `GameAssembly.dll` (image base
`0x180000000`). Original metadata tokens and the Core method-pointer table at
`0x68EAD30` establish registration; extraction names/signatures supplement
that linkage, rather than supplying guessed method-pointer indices. This is
static native/metadata evidence, not a live packet observation or REA bundle.

### Wire coordinates and registered handlers

`gau.bizu(juu)` is original method row 75437, token `0x060126AE`, native
pointer slot `0x697E298`, body `0x1E972F0`. Its encoded method-definition
usage is at `0x6B4BE88`, loaded by the registration body `gau.biwz` at
`0x1E8E069`. The parallel `jth` registration loads `0x6B4C098` at
`0x1E8E0B6`: `gau.bizv(jth)`, original row 75285, token `0x06012616`,
slot `0x697DDD8`, body `0x1E973B0`. `gau.drq(jth)` shares that body
(row 75433, token `0x060126AA`, slot `0x697E278`).

Raw serializers establish the following coordinates, without adopting a
semantic alias from a conflicting field map:

| Message | Wire field/type | Native member | Serializer tag/read RVAs |
|---|---|---|---|
| `juu` | #1 int32 | `+0x18` | `0xA837A9` / `0xA837B3` |
| `juu` | #2 int64 | `+0x20` | `0xA837CB` / `0xA837D5` |
| `juu` | #3 int64 | `+0x28` | `0xA837EE` / `0xA837F8` |
| `juu` | #4 nested message | `+0x30` | `0xA83811` / `0xA8381B` |
| `jth` | #1 int32 | `+0x18` | `0x91C3E9` / `0x91C3F3` |
| `jth` | #2 int32 | `+0x1C` | `0x91C40A` / `0x91C414` |
| `jth` | #3 int64 | `+0x20` | `0x91C42C` / `0x91C436` |

The serializers are `juu.chvu` at `0xA83790` and `jth.chff` at
`0x91C3D0`. In particular, raw `juu` has four fields; a two-field semantic
table is not a basis to relabel or discard #3/#4.

`bizu` reads only `juu.#1` (`0x1E9730E`) and `juu.#2`
(`0x1E97320`). It converts #1 with `ebi.vkb` at `0x1E9733D`, then
passes #2 as actor and the converted #1 as `BuffId` to the constructor at
`0x1E9736D`. The allocation usage at `0x6C689A0` contains `0x20030355`:
type index 98730, original type row 3015, `dvt`. This disambiguates the
shared constructor body `0x2675180` from the other type sharing that body.
It stores actor at `+0x48` (`0x26751B9`), BuffId at `+0x50`
(`0x26751A7`), and context at `+0x40` (`0x26751B0`); enqueue is
`0x1E9737B -> 0x1E96050`.

`ebi.vkb`, original token `0x06004303`, body `0x271CC70`, converts a
nonnegative int32 to `(baseId=input, scope=1)` at `0x271CCC7..D3`.
Negative input returns the static sentinel instead. Its initialization at
`0x27092F7..0x2709313` stores `(baseId=-1, scope=0)`. Neither path is
a demonstrated remove-all discriminator. The actual lookup compares both
32-bit components (`0x27093DB`, `0x27093F0`), not just the base ID.

### Exact interface dispatch, reducer callback, and destructive mutation

`dvt.uia`, original token `0x06003BC9`, slot `0x6908B70`, body
`0x267C600`, resolves service 0x13 at `0x267C67A`. Its interface usage
at `0x6C75110` contains `0x200304CF`: type index 98919, original type
row 3180, `eap`. Slot 11 is `veu(long, BuffId, out dzx) -> bool`.
The actor and BuffId are supplied at `0x267C6F1..FF`; slot selection is
explicit at `0x267C732`. Original `eao` type row 3179 has interface-offset
pair `(98919,4)` at original interface-offset row 3092. Its vtable entry
15 decodes to method row 16855, `eao.veu`, token `0x060041D8`, native
slot `0x690BBE8`, body `0x2715720`. This resolves the indirect target.

This slot is lookup, not removal: `0x27157A9..B2` supplies actor and
BuffId to `FightState.vtt` at `0x273D780`; `0x27157C4..C7` writes the
out buff and returns whether it is non-null. `vtt` selects store `+0x38`
when its bool argument is false (`0x273D7C8`), looks up the actor at
`0x273D7E2`, and calls `Buffs.uzy` at `0x273D7F7`. That method walks
instances and compares the full BuffId at `0x26F523B -> 0x2709390`.

The removal call is separately reached from `dvt.ujv` at
`0x267C8AC`, and from its callback `dvt.etd` at `0x267C23C`, through
`eap` slot 1 with actor and BuffId. The same original interface/vtable
link gives `eao.vek`, original row 16839, token `0x060041C8`, slot
`0x690BB68`, body `0x2714AB0`. It performs the same actor/ID lookup at
`0x2714B3A -> 0x273D780`. Only a found buff reaches
`0x2714B4D -> eao.vel(0x2714B70)`; absence returns without deleting
another instance.

`vel` passes the found buff to `ebb.vhr` at `0x2714C5E -> 0x271ACC0`,
with the removal callback from static field `+8` at `0x2714C5A`.
`eao` static initialization constructs that delegate from usage
`0x6C26670` (`0x2713891`), encoded method `0x600083CB`, original row
16869/token `0x060041E6`, `veg` body `0x270AFD0`. The dispatcher
selects a handler by the found buff's action ID (`0x271AD12`,
`0x271AD5F`), not a separate removal-packet source/type field, then
invokes `ecl<dzx>` slot 0 at `0x271AD92 -> 0x120480`.

The state-action specialization is not skipped in this trace: `ebb`
registers `ebd` for 950, 951, 952 at `0x271AEE5..0x271AF48`.
`ebd` derives from `ecj<dzx,EntityStateInfo>`, whose shared `vhv` body is
`0x3BBDAC0`; it invokes the supplied mutation delegate at `0x3BBDDA2`
between its state evaluations. The generic fallback `eci<dzx>.vhv`
(`0x3BBD9F0`) likewise invokes that delegate at `0x3BBDAA6`.

The delegate body `veg` jumps at `0x270AFE9` to `FightState.vtx`
(`0x273D9B0`). This uses the found buff's actor member `+0x20`
(`0x273D9DA`) to look up the actor in store `+0x38`
(`0x273D9F3`, `0x273DA12`), then calls `Buffs.vac` at `0x273DA3B`.
`vac` (`0x26F54E0`) reads that buff's BuffId at `0x26F5507`, scans
the collection, compares both components at `0x26F55A1 -> 0x2709410`,
and on the first equality calls `Buffs.vat` at `0x26F55C1` with the
matched index. No match returns false at `0x26F55DD`.

`vat` (`0x26F6380`) marks the matched object byte `+0x4C` at
`0x26F63EA`, then calls the indexed list mutator at `0x26F6404 ->
0x381CB80`. Its actual body bounds-checks the index at `0x381CC0D`,
**decrements the list count at `0x381CC19`**, and shifts the surviving
tail at `0x381CC3F`. This is an actual destructive instance-store
mutation, not merely an action allocation, interface call, or query flag.

### Scope, remove-one versus bulk, and remaining boundaries

The proved `juu` scope is **actor `#2` plus scoped instance ID from
`#1`**, followed by the found buff's actor/ID in the store mutation. It
does not delete by state ID alone, by source actor, or across actors.
`juu.#3/#4` are not consumed by this registered handler; assigning either
a source/type alias would exceed the evidence. State action/type is read
from the stored buff during dispatch. Removing one suppressing 951/952
instance therefore permits recomputation over surviving contributions;
it does not establish that every contribution for that state disappears.

A distinct bulk service operation exists: `eap` slot 3,
`eao.vem(long)` at `0x2714DB0`, obtains the actor's view at `0x2714DF8`,
walks backward (`0x2714F2D..30`), and calls the same per-instance
`vel` at `0x2714F28` only for entries whose member `+0x28` equals 1
(`0x2714F05..1D`). Thus even this bulk path is filtered, repeated
per-instance reduction, not an unqualified global store clear. The
meaning of that filter is not renamed here.

**Do not identify `jth` with that bulk path.** Its registered body passes
`#3` as actor, `#2` through the same BuffId conversion, and `#1` as an
additional int32 to `gau.bize` (`0x1E973B9..CE -> 0x1E95800`), which
allocates another action and calls its constructor at `0x1E95879 ->
0x2685B20`. Its downstream mutation is not traced in this bounded unit.
That proves a different input shape, not a remove-all semantic alias.

One registered removal handler now reaches the actual instance-store
mutation, so this unit stops here. Duration eligibility, duration
progression/expiry, and snapshot/stream completeness remain independently
unresolved. Static reachability does not prove live delivery, loss-free
history, or the lifecycle meaning of every sentinel. No production code,
field mapping, game process, build, or git action was changed or invoked.

## Lifetime follow-up: registered turn event, duration mutation, eligibility

Same installed 3.7.13.17 native image and original metadata as above;
addresses remain RVAs. This unit grounds an actual event -> duration/delay
mutation -> active-query chain. **It does not ground automatic expiry
deletion.** The concrete duration computation has a lower clamp of 1;
inventing a decrement-to-zero rule from it would retain or discard states
incorrectly. This distinction limits what the recovered chain authorizes.

### Query eligibility and mask 2, fully separated

`dzx.uyk` is original method row 16569/token `0x060040BA`, pointer slot
`0x690B2F8`, body `0x2706D10`. Its actual eligibility conditions are:

1. Instance byte `+0x4C` must be zero (`0x2706D16..1D`); the removal
   path in the preceding section sets this byte before deleting the entry.
2. Effect wrapper `+0x38` -> underlying effect `+0x10` -> **signed byte
   `+0x30`** is read by `0x26BF750` (`0x26BF75D`). The validity helper
   `0x27D4AD0` must accept it (`0x2706D33`). As previously grounded,
   that helper accepts -1 or a positive value.
3. The same underlying effect's **unsigned byte `+0x3D`** is read by
   `0x26BF980` (`0x26BF98D`). Helper `0x27D4230` must reject it
   (`0x2706D50..57`). This helper is true for positive int32 inputs:
   its >=63 branch is also positive. Thus the byte must be zero here.
4. Instance flags `+0x70` must not contain mask 4 (`0x2706D59..61`).
5. `0x26BF700`, tail-called at `0x2706D73`, requires underlying effect
   flag mask `0x10` at `+0x10` to be clear (`0x7C5130`) and its byte
   `+0x3D` to be zero (`0x26BF72E..32`). No semantic name is assigned
   to that raw effect flag in this unit.

The query calls this predicate at `0x27108D1` and skips false. Its
separate mask-2 test is at `0x27108E4..EF -> 0x2706580`: all requested
bits must be present. Mask 2 is a **state-action classification bit**, not
the remaining-duration counter. `dzx.uzb` clears it at `0x27072B3`,
reads the stored action ID, calls `0x275A0D0` at `0x27072D0`, and sets
it at `0x27072D9` only for actions 950, 951 or 952. The classifier's
complete comparison body is `0x275A0D0..EA`. Eligibility and classification
are therefore distinct gates. The final `EntityStateInfo` active predicate
and status precedence remain as recovered above.

### Wire lifetime coordinates are endpoints, not a guessed turn count

The already-grounded effect nesting is `juh.#1(lng).#1(llg)`. In the raw
`llg` serializer `0xC62180`, **#2** is nested member `+0x20`
(tag `0x12` at `0xC621E8`, read `0xC621F2`), and **#4** is nested
member `+0x30` (tag `0x22` at `0xC6222D`, read `0xC62237`).
`eai.vbp` reads those members at `0x270DAFD` and `0x270DB11` and
converts each with `dug.uhu` (`0x2668570`), then constructs a
`FightTurnWindow` at `0x270DB5C -> 0x26668D0`. The first value becomes
`start`, the second `end`; the window is stored at instance `+0x50`
and `+0x60` by the constructor (`0x2705C34..38`).

The exact converter signature is `FightTurn (lfz)`. `lfz.csfo` shares
serializer body `0x830A50`: **#1 int32** at `+0x18` (tag 8 at
`0x830A69`, read `0x830A73`); **#2 int32** at `+0x1C` (tag 0x10
at `0x830A8A`, read `0x830A94`). Conversion copies #2 to native
`FightTurn.round` (`0x2668588..8B`) and #1 to `FightTurn.order`
(`0x266858D..90`), with null additional ID. A null endpoint converts
to `(round=-1, order=-1, additionalId=null)` (`0x2668570..84`).
These are coordinates in round/order space, not milliseconds or elapsed
actor actions. No field-map alias is substituted for these raw coordinates.

### Registered turn event reaches the same stored effect

`fwp.bhsv(jvi)` is original row 72890/token `0x06011CBB`, pointer
slot `0x6979300`, body `0x1DC6C40`. Its method usage `0x60023975`
is at `0x6C92B50`; `fwp.pez` initializes it at `0x1DD17FE` and loads
it at `0x1DD1CE5` to construct the registered delegate
(`0x1DD1CF8`, subscription at `0x1DD1D1F`). Its raw packet fields use
the same serializer `0x830A50`: `jvi.#1` becomes round, `jvi.#2`
becomes order (`0x1DC6C4E..0x1DC6C6E -> 0x2666D30`).

It calls `fwp.bhre` at `0x1DC6C89 -> 0x1DC2900`, original row
72916/token `0x06011CD5`, slot `0x69793D0`. That method returns
without refresh if round/order are equal (`0x1DC29EE -> 0x2666D80`);
otherwise it stores the new current turn at `0x1DC2A25`, resolves
service 0x13, and calls `eap` slot 16 at `0x1DC2A52..5A`.

The original `eao` interface offset 4 maps slot 16 to vtable entry 20,
encoded method `0x6000839F`, original row 16847/token `0x060041D0`,
pointer slot `0x690BBA8`, `eao.vez` body `0x2715ED0`. It obtains
the current turn from `fws` slot 0 (`0x2716081..95`). Original `fwp`
interface-offset row 9331 maps `fws` to offset 8; slot 0 is original
row 72861/token `0x06011C9E`, `bhrd` at `0x11F1A00`, copying the
current turn stored at `+0x18`. Thus this indirect target is resolved.

`vez` calls `FightState.vuc` at `0x27160AF -> 0x273DF00`, original
row 17772/token `0x0600456D`, slot `0x690D890`. This enumerates the
values of the actor dictionary at store `+0x38`, then each buff. At
`0x273E0EF` it calls `dzx`'s shared turn updater `0x2706170` with
that current turn. It is a refresh over the stored contributions, not a
blanket deletion each actor turn. Changed contributions are collected
(`0x273E0F4..0x273E10F`) and notified by `vez` after refresh.

### Actual duration/delay mutation and sentinel behavior

Updater `0x2706170` reads old duration/delay, computes new values from
the instance's window and current turn, then writes the underlying effect:

- `0x27061E0 -> FightTurnWindow.uhw (0x2666900)`; result AL goes
  to **effect `+0x30` at `0x27061EA`**.
- `0x270620E -> FightTurnWindow.uhx (0x2666950)`; result AL goes
  to **effect `+0x3D` at `0x2706218`**.
- `0x270622B..0x270625D` compares new versus old values to report
  whether either changed. The add producer also calls this updater at
  `0x270DC8C`, so initial and subsequent refresh use the same calculation.

The following formulas transcribe those bodies, not a proposed model.
Let start be `(S,O_s)`, end `(E,O_e)`, current turn `(R,O)`:

```text
duration (0x2666900):
  if E <= 0:                         63
  else if S > 0 and R < S:            max(1, E - S)
  else:                              max(1, E + (O_e > O ? 1 : 0) - R)

delay (0x2666950):
  if S > 0 and R < S:                 S - R
  else if S > 0 and R == S and O_s > O: 1
  else:                              0
```

The writes store only the low byte; subsequent duration reads sign-extend
that byte, whereas delay reads zero-extend. No unobserved range-validation
or truncation policy is inferred. The units for these derived values are
**round differences adjusted at order boundaries**. They are recomputed,
not decremented blindly for every actor start/end message. A delayed
contribution is ineligible until its derived delay reaches zero; a turn
refresh can therefore change whether the active query considers it.

Sentinels must stay separate: a missing/nonpositive end produces duration
63, not zero (`0x2666941`). The validity helper accepts -1 and positive
values; another helper `0x27D4B50` treats -1 or >=63 as special. This
does not prove every such value's complete lifetime meaning. Most
critically, a positive end which is already passed still produces **1**
under `uhw`; it does not become 0 or set the removed byte.

### Stop boundary: refresh is proved; expiry admission is still blocked

The concrete event -> effect-duration/delay writes -> query eligibility
chain is now grounded, satisfying this unit's event/mutation/query stop
condition. It proves delayed eligibility and ongoing display-duration
progression. It does **not** prove that an actor turn automatically removes
or inactivates a state at the endpoint. Reaching or passing the end cannot
be used as a local expiry rule from this body alone. The separately
grounded registered `juu` event remains a real destructive-removal path;
this unit does not infer that it is the only expiry mechanism.

Consequently, the requested guarantee against stale immunity is **not yet
supported for a local lifetime reducer**: an authoritative expiry/update
producer or another concrete inactivation hook is still required. This
is a finding, not permission to retain immunity indefinitely or clear all
states every turn. Completeness remains unknown independently, and partial
state evidence must remain unknown. Only this evidence document was
appended; no code, mappings, game, builds, or git actions were used.

## jth follow-up: scoped parameter update, not a proved expiry mutation

This is the next bounded investigation, separate from the completed lifetime
unit. **Registered `jth` reaches a stored effect's parameter0 write and
single-instance collection replacement. It does not write duration, the
lifetime window, or the removed flag on the traced state-contribution path.**
Thus this packet is not evidence for automatic expiry or remove-all.

### Scope and artifact identity

Read-only static analysis used the same installed 3.7.13.17 artifacts:

- `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3/GameAssembly.dll`, SHA-256
  `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591`.
- Its `Dofus_Data/il2cpp_data/Metadata/global-metadata.dat`, SHA-256
  `ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`.
- Existing address-annotated extraction under
  `%USERPROFILE%/AppData/Local/Temp/givre-state-cpp2il-20261104/dll-addresses`:
  `Core.dll` SHA-256
  `3979aa3e740a9821126379fd0c32f21422792c7f9096887526a9d27c965297c3`,
  `Ankama.Dofus.Protocol.Game.dll` SHA-256
  `a41cf993a50418f4cb6aa64bf54eee641331685b029a5d2053ff146703af5d1e`,
  and newly consulted `Ankama.Dofus.Core.DataCenter.dll` SHA-256
  `e04c370b9409597a82b0249fe048a8a40afa0da615978d3e8b3141ae539a1b05`.

The already-built metadata reader was executed without compilation. Native
registration, original type uses and original vtable entries resolve the
indirect calls below; rebuilt extraction token ordering is not used to index
native method arrays. All addresses are RVAs; native image base is
`0x180000000`. Only this document was appended. No production code,
mapping, game, build, or git action occurred.

### Registered input through concrete dwm action

The previously recovered `gau.bizv(jth)`/`drq(jth)` body is `0x1E973B0`.
Its exact wire arguments are:

| Coordinate | Raw type/member | Use in this chain |
|---|---|---|
| `jth.#1` | int32 `+0x18` | replacement parameter0 value |
| `jth.#2` | int32 `+0x1C` | network instance ID converted to scoped BuffId |
| `jth.#3` | int64 `+0x20` | actor lookup key |

The serializer tag/read addresses remain in the removal section above.
The handler loads #1 into R9D at `0x1E973B9`, #2 into R8D at
`0x1E973BD`, #3 into RDX at `0x1E973C1`, then calls `gau.bize`
at `0x1E973CE -> 0x1E95800`. `bize` converts #2 through `ebi.vkb`
at `0x1E95844` using the same nonnegative `(baseId,scope=1)`/negative
sentinel behavior already established, not a bulk-operation discriminator.

Allocation usage slot `0x6C6AAE0`, loaded at `0x1E95849`, contains
`0x2003037D`: original type index 98750, original type row 3036,
**`dwm`**. It calls the constructor at `0x1E95879 -> 0x2685B20`.
Original constructor row 15425/token `0x06003C42`, native slot
`0x6908F38`, independently registers that body. Constructor stores:

- scoped BuffId at action `+0x50` (`0x2685B4C`);
- #1 int32 at `+0x58` (`0x2685B55`);
- context at `+0x40` (`0x2685B5D`);
- actor #3 at `+0x48` (`0x2685B61`).

Enqueue is the tail call `0x1E9589F -> 0x1E96050`. Allocation and
enqueue alone are not the mutation evidence.

### Resolved lookup and actual field mutation

`dwm.uia`, original row 15433/token `0x06003C4A`, native slot
`0x6908F78`, body `0x26863C0`, resolves service 0x13 at `0x2686444`.
Its interface load at `0x2686464` resolves the same original `eap`
type-use slot `0x6C75110`. It calls slot 11 with actor and full BuffId
at `0x26864B5..C3`; the fast path explicitly adds 11 at `0x268660B`.
Original `eao` interface offset 4 maps this to vtable entry 15,
`eao.veu` (`0x2715720`), as grounded above. The actor-scoped
`FightState.vtt -> Buffs.uzy` lookup therefore resolves to a real stored
buff rather than a newly guessed instance.

Success obtains that buff at `0x2686622` and saves its reference at
action `+0x60` (`0x268662B`). A completion delegate is constructed
at `0x268664E`; when no additional work is needed it is invoked at
`0x268680C`. The delegate dispatch body `dwm.gli` at `0x2685B70`
ends at `0x2685BF8 -> dwm.ukm (0x26868B0)`. Other action callback
bodies repeat the same update; they are not interpreted as distinct
lifetime semantics.

`ukm`, original row 15429/token `0x06003C46`, native slot
`0x6908F58`, reads the found buff at `0x26868ED`, wrapper `+0x38`
at `0x26868FB`, then underlying effect `+0x10` at `0x2686908`.
It loads the packet #1 value from action `+0x58` at `0x2686911`,
boxes that int32 at `0x268691F`, and invokes the underlying effect's
virtual setter slot 5 at `0x268693D` (class callable offset `0x188`,
MethodInfo offset `0x190`).

The state producer `eai.vbu` allocates `EffectInstanceDice`; its original
type row is 36356, vtable starts at original vtable row 418023.
Entry 5 contains encoded method `0x600A40C7`, original method row
335971, token `0x06000599`, **`set_parameter0`**. The correct native
module is `Ankama.Dofus.Core.DataCenter.dll`: codegen-module record
`0x5B46480`, name pointer to `0x5B46508`, 1759 method pointers at
`0x6831060`. Original token `0x06000599` indexes native slot
`0x6833D20`, body **`0x7D6640`**. This body converts the boxed value
at `0x7D6686` and performs the actual **int32 write to effect `+0x80`
at `0x7D6690`**. It is not a duration setter. Getter slot 4 is original
row 335970/token `0x06000598`, native slot `0x6833D18`, body
`0x7D6560`, reading the same member at `0x7D6564`.

### Resolved update service and actual collection mutation

After that write, `ukm` calls `eap` slot 0 at `0x2686967..73`,
passing the found buff. Again this is not left as an unnamed interface:
original `eao` offset 4 maps slot 0 to vtable entry 4, original method
row 16838/token `0x060041C7`, **`eao.vej` at `0x2714880`**.
It calls `ebb.vhr` at `0x271496D -> 0x271ACC0` with static callback
field 0 (`0x271496A`). `eao` static initialization constructs this
callback from original method usage `0x600083C9` at `0x6C26458`,
original row 16868/token `0x060041E5`, **`vef` at `0x270AFA0`**.

For actions 950/951/952 the same previously grounded state-specific
`ebd -> ecj<dzx,EntityStateInfo>.vhv` wrapper invokes the supplied
callback at `0x3BBDDA2`. Here the callback is `vef`, not the removal
callback `veg`: `0x270AFC2 -> FightState.vtv (0x273D8D0)`.
`vtv` obtains that actor's ordinary buff collection via
`0x273D8DF -> 0x273D0E0` (bool false), then jumps at
`0x273D8F7 -> Buffs.vaa (0x26F52D0)`.

`vaa` searches by the full BuffId using inequality helper `0x2709410`
at `0x26F538E`. First equality passes the matching index to
`Buffs.var (0x26F60C0)` at `0x26F53C4`; no matching index passes -1
at `0x26F53CC`. The matched branch calls the actual list index setter
at **`0x26F617D -> 0x381E5C0`**. That generic body writes the element
into the indexed array slot at `0x381E688` and increments list version
at `0x381E6AF`; it does **not decrement list count**. Here the element
is the same found buff whose effect was updated. The no-index branch
adds at `0x26F61CF`; it is a generic upsert fallback, not evidence
that a missing packet target is constructed by this action. This action
only populates its saved buff reference after a successful lookup.

The state-action branch also calls `Buffs.vba` at `0x26F631B ->
0x26F6A60`, rebuilding its state view from stored action IDs
(`0x26F6BD2`) and state values (`0x26F6BEC -> 0x26BF8B0`).

### Query consequence, expiry finding, and exact unknowns

For a state contribution, the mutated value is **parameter0**, whereas
the state identity consumed by the active query is **parameter2**:
`hq.nfk` at `0x26BF8B0` calls getter slot 8 (`0x26BF8C5..D3`),
and query `0x27108BC..C6` compares that value with the requested state
ID. The parameter2 setter `0x7D6700` writes underlying effect `+0x78`
at `0x7D6750`; that is distinct from this packet's `+0x80` write.
The stored action ID, duration `+0x30`, delay `+0x3D`, window at buff
`+0x50/+0x60`, removed byte `+0x4C`, and eligibility flags are not
changed by this traced parameter update/replacement path. Rebuilding the
state view is not itself removal or expiry.

Therefore `jth.#1 == 0`, or any other parameter0 value, is **not a
proved instruction to expire a state, clear all its instances, alter the
state ID, or advance its duration**. With the same eligible contributions
and those query inputs unchanged, this path supplies no state-active
transition by itself. Its meaning for other effect action families is
outside this unit; no semantic alias for parameter0 is guessed.

This completes one actual stored mutation and its query consequence, so
the unit stops. Automatic lifetime-end removal/inactivation remains
unresolved, as do the complete set of expiry/update producers and
completeness of observed state history. No conclusion that server removal
is the sole expiry mechanism is supported. A lifetime reducer is still
not admitted by these findings, and partial state evidence remains unknown.

## Final bounded lifetime search: additional registered bulk-removal ingress

**An additional registered ingress reaches actual filtered actor-wide
instance removal: `jsq.#31(jrl)` -> `gau.biye` -> `dux` -> `eao.vem`.**
It is distinct from `juu` and from the parameter-only `jth` update. This
establishes another removal producer, not automatic expiry at a lifetime
endpoint. The successful additional mutation path is this unit's stop
condition; the inventory below is explicitly bounded, not a claim to have
enumerated every native writer or every expiry mechanism.

### Artifacts, method of analysis, and bounded inventory

The same installed 3.7.13.17 `GameAssembly.dll` and original version-39
`global-metadata.dat`, and existing annotated `Core.dll` and
`Ankama.Dofus.Protocol.Game.dll` extraction were used, with paths and
SHA-256 values recorded in the preceding section. No new artifact version,
live capture, game execution, build, mapping edit, production change, or git
action was used. Only this document was appended. Addresses are RVAs at
image base `0x180000000`. Original method tokens, module pointer slots,
encoded type uses and original vtables ground the indirect targets.

Instruction-aligned direct/tail-call scans found no direct/tail callers of
`eao.vek (0x2714AB0)` or `eao.vem (0x2714DB0)`: they have interface
callers, not missing bodies. Direct calls to `eao.vel (0x2714B70)` are:

| Call RVA | Enclosing operation |
|---|---|
| `0x2714B4D` | `vek` single actor/scoped-ID removal |
| `0x2714F28` | `vem` filtered actor-wide removal |
| `0x271514B` | `ven` actor/two-int operation; not traced to ingress here |
| `0x271532F` | `veo` actor/int operation; not traced to ingress here |
| `0x271696E` | `vfg` reconciliation against supplied contributions; ingress not traced here |

Exact `eap` type-slot references were examined for slot-3 dispatch. The
productive candidate was `dux.uia (0x266FC80)`. Other slot-3-immediate
candidates in enclosing bodies `0x1D83A70`, `0x1DD4280`, `0x1E3BC00`
and `0x2675922` are not claimed to be `eap.vem`: a function can reference
multiple interfaces, so an immediate 3 elsewhere in that function is not
enough to identify the target. They were not pursued after the successful
`dux` path. This does not establish exhaustion of indirect callers.

A bounded instruction inventory of the buff/lifecycle region
`0x26F0000 <= function start < 0x2707500` found literal true writes to
byte member `+0x4C` at `0x26F6369`, `0x26F63EA`, and `0x26F6559`.
The middle write is the grounded indexed removal body; the last is the
collection-wide marking body. The shared setter `0x899100` can also write
that byte from an argument, and `dzx` aliases `uyo/ljc/fgp` share the
literal-true body `0x2706270`; no direct/tail call to the latter was found.
These shared/indirect setters are not assigned an expiry ingress here.

Within the inspected `dzx` constructor/lifecycle range
`0x2705700..0x2707500`, flag writes include constructor/copied whole-word
assignments and whole-word masks, not only single-bit changes. The explicit
`or [instance+0x70],4` is at `0x2705F3E` in `0x2705E80`, reached
after its effect-derived classification branch (`0x2705F29`). Whole-word
assignments also include `uyq` at `0x2706E05`, whose supplied flags are
then reclassified. These are not evidence that a turn endpoint sets mask 4.
No universal expiry conclusion follows from this bounded writer inventory.

### Registered server-envelope dispatch to concrete removal action

The registered main handler is `gau.bixl(jsq)`, original method row 75290,
token `0x0601261B`, body `0x1E90220`. Its encoded method use
`0x60024C35` is at `0x6B4B018`; registration body `gau.biwz`
initializes it at `0x1E8DA9E`, loads it at `0x1E8DCC7`, constructs
the delegate at `0x1E8DCDA`, and subscribes it at `0x1E8DD01`.

`bixl` reads envelope discriminator `+0x30` at `0x1E90266`; jump-table
entry 31 at `0x1E907BC + 31*4` selects `0x1E9066C`. That branch
tail-jumps at `0x1E90684` to **`gau.biye(jsq)`**, original row
75394/token `0x06012683`, registered native pointer slot `0x697E140`,
body `0x1E91D30`. Thus absence of a standalone delegate use for `biye`
is explained by its concrete registered outer switch, not treated as an
inaccessible registration.

The raw envelope serializer `jsq.chah (0xA4B520)` tests discriminator
31 at `0xA4C055`, writes tag bytes `FA 01` at `0xA4C05E..66`,
and serializes the nested message at `0xA4C0B6`: **wire field 31,
length-delimited**. Getter `jsq.cgzp (0xA4B1A0)` checks discriminator
31 at `0xA4B1C7`, obtains object `+0x28`, and checks its exact type.
Type-use slot `0x6B68EE8` contains `0x2003EF6B`, original type index
128949/original type row 12276, **`jrl`**. No semantic message alias is
needed to establish this ingress.

### Raw payload scope and the proved bulk discriminator

`jrl.cglx (0xA5A300)` proves:

| Payload coordinate | Raw type/member | Instructions |
|---|---|---|
| #1 | oneof int32, discriminator 1, boxed object `+0x28` | tag 8 `0xA5A320`; value `0xA5A35A` |
| #2 | int64 actor at `+0x18` | tag 0x10 `0xA5A374`; read `0xA5A37E` |
| #3 | oneof int32, discriminator 3 | tag 0x18 `0xA5A396`; value `0xA5A3C8` |
| #4 | bool at `+0x20` | tag 0x20 `0xA5A3E0`; read `0xA5A3EA` |

`biye` obtains `jrl` at `0x1E91D91` and branches on its oneof
discriminator at `0x1E91DA4`. The case-3 branch goes to another action
(`0x1E91EB9 -> 0x2668860`), not traced here. For other cases, actor
#2 is loaded at `0x1E91DBC`. Helper `eai.vbs (0x270DDB0)` calls
`jrl.cgln` at `0x270DDBD -> 0xA41B80`: this reads #1 when oneof
case 1, otherwise returns zero. Nonnegative values convert to scoped
`(baseId,1)` at `0x270DE1A..26`; negative values select the static
BuffId sentinel at `0x270DDE7..0x270DE11`.

Therefore **an explicit negative #1 in case 1** is one concrete way to
reach the bulk sentinel. An absent #1/default oneof is not automatically
bulk: the getter returns 0, which converts to `(0,1)`. Payload #4 is not
read in the traced `biye` branch. Neither a source/type alias nor a
global-clear meaning is inferred from unused fields.

Allocation slot `0x6C65BE8`, loaded at `0x1E91DF5`, contains
`0x20030329`, type index 98708: concrete **`dux`**, original type row
2992. The constructor `0x266F990`, original row 15135/token
`0x06003B20`, is called at `0x1E91E1C` with actor, converted BuffId
and the already resolved `eap` service. It stores ID at action `+0x48`
(`0x266F9B7`), service at `+0x40` (`0x266F9C0`), actor at `+0x50`
(`0x266F9C9`), then the handler enqueues it at `0x1E91E43`.

`dux.uia`, original row 15137/token `0x06003B22`, body `0x266FC80`,
compares the full BuffId against the static sentinel at
`0x266FCD5 -> 0x2709320 -> 0x2709390`. A non-sentinel calls `eap`
slot 1 at `0x266FD07`, the known single-instance `eao.vek` path.
**A sentinel takes the bulk branch `0x266FD0E`**, reads the actor,
and dispatches `eap` slot 3 (`0x266FD54..6F`; fast-path add 3 at
`0x266FD95`). Its interface use is the original `eap` type slot
`0x6C75110`, not an inferred method name.

### Resolved bulk service to actual state-store mutation

Original `eao` interface offset 4 maps `eap` slot 3 to vtable entry 7,
encoded method `0x60008387`, original row 16835/token `0x060041C4`,
**`eao.vem (0x2714DB0)`**. As previously grounded, it gets only the
specified actor's buff view at `0x2714DF8`, walks backward, and calls
`eao.vel` at `0x2714F28` only for entries whose member `+0x28` equals
1 (`0x2714F05..1D`). This is filtered actor-wide removal, not an
unqualified all-actor clear or a remove-by-state-ID operation.

For each selected state contribution the resolved chain is the established
`vel -> ebb.vhr -> ebd/ecj state reducer callback -> veg ->
FightState.vtx -> Buffs.vac -> Buffs.vat`. It sets the found instance's
removed byte at `0x26F63EA`, calls list removal at `0x26F6404`, and
**decrements count at `0x381CC19`**. The active query then excludes that
instance via removed-byte guard `0x2706D16..1D`, and surviving
contributions recombine under the already proved status rules. No lifetime
window arithmetic or inferred flag-4 transition is needed for this removal.

### Admission boundary: native evidence versus proposed policy

**Native evidence:** there are now at least two grounded removal ingresses,
the actor/scoped-ID `juu` path and this `jsq.#31(jrl)` single/bulk path.
The latter proves a negative-ID sentinel's bulk meaning in this exact
consumer; it must not be generalized to every negative-ID field or message.
The display-duration refresh is still not an expiry decision. No finding
establishes the complete set of expiry producers or makes server removal
the sole possible inactivation mechanism.

**Proposed conservative policy, not recovered native behavior:** event-sourced
partial positive contributions may be retained as observed records until
confirmed scoped removal, but that does not authorize retaining a live
immunity boolean until such a message arrives. A positive result is usable
only while its identity, eligibility and temporal validity remain supported;
unobserved suppressors, gaps, reordering and missing lifecycle evidence can
still defeat an authoritative positive or negative claim. Once a known
lifetime endpoint is overrun without a grounded continuation/expiry event,
that contribution's live status should become **unknown**, not assumed
active and not declared absent. Keep the historical instance record separate
from whether it presently supports immunity. Confirmed removal can retire
only its proved instance/actor/filter scope; it cannot establish completeness
or global state absence.

Thus this evidence supports bounded maintenance of partial event records and
confirmed removals, with uncertainty at unsupported temporal boundaries.
It does not yet admit a complete native-equivalent lifetime reducer or
`StatesKnown=true`. No implementation or fallback clearing policy was added.
One additional registered bulk path now reaches actual mutation, so this
final bounded search stops without pursuing the remaining indirect edges.

## Source-grounded integration design: observed records, not authoritative states

**Decision:** the next implementable unit is an engine-only, normalized
instance-contribution ledger for actions 950/951/952, confirmed scoped removal,
and the confirmed actor-local bulk filter. Direct live projection into planner
positive states is **blocked**. This section is a design, not an implementation
or a new native finding. Only application source and existing catalog data were
read for it; no further native search, mapping change, build, test, git, or game
action was performed.

### Actual ingress and reset seams

- `GivreClone.App/Services/CaptureDecoder.cs:10` defines `DecodedPacket` with
  PID, direction, code/name, parsed payload, and raw frame, but no connection or
  session generation, ordered observation sequence, decode-health signal, or
  artifact binding. `Feed` buffers by PID/direction and emits `Packet` outside
  its buffer lock (`:32-77`); that lock alone does not serialize downstream
  callbacks from concurrent feeds. Buffer overflow clears at `:46`; missing
  payload/fallback decoding and ignored parse exceptions at `:57-72` do not
  publish a loss/gap event. `Forget` (`:81`) clears buffers, not consumer epochs.
- Both transport paths reach that decoder: socket
  `LaunchService.Packets.cs:136` (`FeedSocketFrame`) and MITM
  `LaunchService.cs:653-659` (`_mitm.OnData`). `EnsureDecoder` subscribes
  `Packet += OnDecoded` at `LaunchService.Packets.cs:121-122`.
  `OnDecoded` (`:183`) invokes `PacketDecoded` at `:213` before most combat
  materialization. A future internal fold must run in an ordered ingress step
  before publication and before turn scheduling, not as a subscriber that can
  expose the previous snapshot. Accept server-to-client observations only.
- Allocate a fresh local fight generation at `FightStartingEvent`
  (`LaunchService.Packets.cs:840-871`), not merely by server fight ID. Clear the
  ledger in `LaunchService.Combat.cs:245` (`PurgeFightState`), shared by
  `EndFight` (`:210`) and `LaunchService.GameLink.cs:440` (`ForgetSession`).
  `EndFight` deliberately raises `FightEnded` before purge (`:230-231`): retain
  that callback ordering, while closing observation admission at fight end.
- Full PID teardown needs its own ledger reset in `LaunchService.cs:469`
  (`Forget`); do not assume it passes through `PurgeFightState`.
  The same-PID character-selection branch at `LaunchService.Packets.cs:1542`
  clears other character caches but does not supply a state-ledger reset.
  Invalidate on character switch/loading, connection replacement/loss, and
  reconnect before accepting new observations; reject late old-generation
  frames. `AdoptCharacterId` (`LaunchService.Character.cs:58-62`) only fills an
  unknown identity and is not the character-change boundary.
- Confirmed death removes the actor from the roster/visibility at
  `LaunchService.Packets.cs:985-995`: forget that actor's planning observations
  there, without claiming this proves native buff destruction. A disposition
  roster refresh (`:970`) or fighter-show/reconnect adoption (`:1305`) does not
  prove complete buff/state knowledge and must not promote completeness.

### Smallest engine unit and its admission boundary

Proposed new engine source: `GivreClone.Engine/ObservedStateContributions.cs`.
Keep protobuf/catalog/application dependencies outside it. Normalize verified
inputs to an epoch, target actor, full scoped instance ID, source/type identity
where established, action ID, state ID, bulk-filter classification, and explicit
eligibility/temporal evidence. Preserve missing/unsupported fields as unknown;
an unknown classifier or eligibility bit must not default to a permissive value.
Use one atomic ledger/snapshot update, following immutable snapshots in
`GivreClone.Protocol/FightVisibility.cs:37-58`, rather than publishing
independently mutable actor sets.

1. Record 950/951/952 as separate instance contributions with native statuses
   1/3/2, respectively. Derive the maximum status over eligible observed
   instances; do not implement 951/952 as deletion of a 950. Keep the stored
   records separate from the derived **observed-subset** result. Unsupported
   eligibility or time evidence prevents a definite live result for that subset.
2. Scoped remove deletes only the identified actor/instance record. Recompute
   from surviving contributions: deleting a suppressor may reveal a surviving
   set in the observed subset. Removal of an unknown key is harmless and never
   establishes absence. Do not key by state ID, source, or action alone.
3. Bulk remove applies only the established actor-local predicate on stored
   classification (`buff+0x28 == 1`), not all actor records or all states. An
   unknown classification on a possible survivor invalidates its live certainty
   rather than assuming inclusion/exclusion. Preserve the native distinction
   between explicit negative sentinel and absent instance ID defaulting to zero;
   the separate case-3 path remains unsupported.
4. Retain history separately from live eligibility. Invalid/unknown flags,
   delayed activation, unsupported endpoints/turn position, endpoint overrun,
   or an observation gap produce unknown live knowledge, not automatic expiry,
   perpetual activity, or a per-turn clear. The prior clamp-to-1 finding is not
   an expiry contract. Do not reuse the arithmetic in
   `GivreClone.Engine/SpellModifiers.cs:42` for these records. `jth` changes
   parameter0 only under the accepted evidence; it is not a lifetime refresh or
   a new state-ID observation.

**Partial contributors cannot establish an authoritative positive.** An observed
950 can coexist with an unseen eligible 951/952 whose higher status suppresses
it. Even a definitely eligible 950 and a gap-free interval after joining do not
prove those suppressors were absent before joining. Therefore expose records,
observed-subset status, and uncertainty reasons, but do not export that subset's
status-1 result as `PlannerFighter.States`. The first engine unit is useful and
testable without any live planner positive/negative export. A future export
requires a separate per-state proof excluding unseen eligible suppressors (for
example, a verified baseline and coverage contract); none is established here.
It need not imply global completeness, but it cannot be invented from this log.
No explicit `AbsentStates` should be emitted by this first slice either.

### Planner and continuation contract

`CombatTurnPlanner.cs:18,24` makes `StatesKnown` nullable and infers completeness
from non-null `States`; the root `SpellBoard` repeats that inference at `:202`.
Any eventual partial projection must pass `StatesKnown:false` explicitly.
That flag **does not make listed positives uncertain**: masks consume present
states as true (`SpellResolver.cs:271-279`), and damage rules inspect membership
(`:163`). Do not populate `States` from an observed 950 merely with that flag.
Keep the existing unknown projection until the independent positive-admission
proof above is available. Unknown state absence remains unknown as well.

The future live snapshot seam is the `PlannerFighter` construction at
`LaunchService.Combat.Turn.cs:988-1009`, inside the decision loop at `:950`.
Read a frozen admitted projection there on every decision. Continuation reuse
already checks `HasSameStateKnowledge` at `:1097`, comparing positive and absent
sets plus completeness: withdrawal of a formerly admitted positive must reach
that comparison, not remain cached in `expectedFighters`. Unsupported temporal
transitions must be processed before the turn-start scheduling paths at
`LaunchService.Packets.cs:1167-1225`; current actor/round observations alone do
not implement native round/order progression. A gap/reset must also cancel
in-flight work or fail a generation/revision check before the next send, not
only before the next planning iteration. Equal unknown projections need no
state-set invalidation unless an in-flight admission proof was revoked.

Do not feed `SpellResolver.cs:429-437` back into the observed ledger: its
950-add/951-or-952-remove plus explicit absence is a speculative cast model,
not the native instance combiner. Do not use the boolean
`GivreClone.Protocol/StateCriterion.cs` helper as a tri-state adapter, or
`GivreClone.Engine/GameState.Apply` as this app's ingress: the actual combat
model is populated in `LaunchService.OnDecoded`.

There is a separate immunity-consumption gap: the live snapshot at
`LaunchService.Combat.Turn.cs:1081-1088` does not supply `StateDamageRules`.
The planner/resolver already accept those rules; the selected game catalog can
query them via `GameDb.Spells.cs:15` (`SpellStateDamage`). Catalog properties
alone are not observed state membership. Wiring rules cannot repair the missing
positive-admission proof and is not part of the ledger unit.

### Concrete live-ingress blockers still to resolve

- **Version-bound reader schema, not guessed aliases.** Current
  `data/matching_current.json:745,757,763,770` names raw jsq/jth/juh/juu as
  `GameActionFightEvent`/`FightBoostUpdateEvent`/`FightBoostAddEvent`/
  `FightBoostRemoveEvent`. But `data/fields.json:2623-2633` still has opaque
  semantic-member names, and its semantic remove entry has only fields 1/2,
  unlike raw juu's four fields at `:13597`. The raw entries for juh, lng, llg,
  lfz, juu, jrl and jsq exist; their existence is not a verified semantic reader.
  Bind the recovered installed-3.7.13.17 schema to the decoder's actual artifact
  identity, exact envelope/variant and wire types, signed values, nested-message
  presence and scalar defaults. Verify actor versus source, full scopedID,
  action950/951/952 and state parameter2, plus scoped-remove branch scope and
  jsq.#31/jrl sentinel selection. No old clear-name/tag fallback is admitted.
- **Eligibility construction.** The native predicates are known, but the app
  lacks a normalized wire-to-stored-field adapter for removed byte, eligibility
  mask4, effect flag0x10, valid signed duration and delay. Establish each
  field/default or constructor-derived value, including unsupported variants;
  do not infer it from action ID or spell data.
- **Temporal support.** Wire endpoint containers are grounded at
  juh.#1(lng).#1(llg), with llg.#2/#4 start/end lfz round/order. The app still
  lacks verified normalization of endpoint presence/defaults, nonpositive/end
  sentinels, native delay and signed duration, and the current actor turn/order
  context (including the additional-ID behavior). Neither local turn number
  nor wall time supplies that proof. No automatic expiry or indefinite-active
  sentinel interpretation is admitted; unsupported transitions remain unknown.
- **Bulk classification and update identity.** Locate/validate how the incoming
  instance obtains the stored +0x28 classifier before live bulk filtering.
  Define duplicate delivery, replacement/upsert and ID reuse under an ordered
  epoch; unsupported collisions must invalidate rather than overwrite by a
  guessed identity. Preserve confirmed jth parameter0 behavior without treating
  it as state/lifetime refresh. Unknown variants must not silently bypass a
  possible state invalidation.
- **Observation integrity.** Add explicit sequence/connection-generation and
  decode-gap signals covering overflow, parse failure, empty fallback payload,
  missed transport chunks, reconnect and superseded connection closure.
  Bind a packet's epoch at capture, not by reading current PID state after it
  arrives. Serialize folds/publication, reject stale epochs, and revoke pending
  planner admission on gaps. Existing exception logs and `CaptureDecoder.Forget`
  do not provide this contract.
- **Coverage and authoritative knowledge.** There is no verified complete
  baseline, per-state suppressor-coverage proof, or loss-free lifetime guarantee
  in the inspected path. Fight start, roster/show packets, reconnect, removal,
  and observing only additions cannot supply it. This blocks live authoritative
  positives independently of decoding/time work; global `StatesKnown=true`
  remains disallowed. Keep unknown damage rules distinct from absent states.

### Precise tests owed by a later implementation

Place normalized reducer tests in proposed
`GivreClone.Tests/ObservedStateContributionTests.cs`; the test project already
references Engine and Protocol (`GivreClone.Tests.csproj:30-31`). Cover two
950 instances with one removed; independent actors/epochs and full scoped IDs;
950+952+951 precedence regardless of arrival order; removing the highest
suppressor revealing only the observed-subset result; unknown removal never
proving absence; exact bulk classification retaining nonmatches and making
unknown classifications uncertain; absent-zero versus explicit negative
sentinel; unsupported eligibility/delay/endpoints/overrun and gap becoming
unknown without deleting history; duplicate/replacement policy; and epoch reset
rejecting late observations. Include the decisive counterexample: identical
partial observed950 history is compatible with an unseen951/952, so neither
history may produce an authoritative planner positive or negative.

Reader tests, when the version-bound adapter is authorized, should follow
`FightVisibilityTests.cs` fixture shape but exercise raw recovered envelopes,
signed actors/IDs, malformed wire types, omitted versus explicit scalar values,
unknown variants, the semantic/raw catalog mismatch, and wrong artifact version.
Do not make a test pass by substituting semantic tags from a different schema.

Extend `CombatPlannerResolverTests.cs:108` only when projection is implemented:
explicit false with non-null partial sets; admitted positive withdrawal and
absence/completeness changes reject continuation; equal knowledge permits it;
an observed950 alone never enters planner positives. Preserve the speculative
`SpellResolverTests.cs:332-384` contract rather than rewriting it as native
evidence. Future app integration tests must drive both decoded ingress paths,
assert fold-before-publication/turn scheduling, all reset boundaries above,
reconnect/gaps/late epochs, and cancellation or revision rejection before send.
Use deterministic event barriers, not sleeps. Immunity behavior also needs a
separate rule-fed snapshot test; no rule-backed behavior is claimed by this
document-only change.

This makes the next decision concrete: authorize the normalized engine ledger
and its tests independently, or first supply the listed ingress/admission
contracts. Do not enable a live state producer by projecting partial records.
The source-grounded design is complete; no implementation was performed.

### Adapter invariant: confirmed query knowledge only

Source confirmation: `GivreClone.App/Services/Combat/SpellResolver.cs:159-166`
(`Damage`) reads target `States.Any(...Invulnerable)` and caster
`States.Any(...CantDealDamage)` directly, with **no `StatesKnown` guard**.
`Heal` (`:146-157`) likewise treats present state76 as incurable; for a positive
effective heal on an incomplete fighter it requires explicit `AbsentStates`
membership for76, otherwise reporting unmodeled healing. Unknown76 must not be
silently converted to absent76 to admit healing.

The proposed adapter invariant, for each actor/state in one ordered epoch and
snapshot revision, is:

- Membership in exported `States` implies a **confirmed active native-query
  result**, not merely a 950 record or status1 over the observed subset. Its
  proof must cover eligible higher-status suppressors, relevant history, and
  current eligibility/time. If any of those are uncovered, export neither
  presence nor absence for that state, regardless of observed contributors.
- Membership in exported `AbsentStates` requires an independent confirmed
  inactive-query proof. No observed950, scoped/bulk removal, empty observed set,
  or unsupported endpoint supplies that proof by itself. In particular, none
  supplies absent76. Positive and absent sets must be disjoint.
- Keep contribution records and observed-subset aggregation in a separate
  evidence snapshot. Keep `StatesKnown:false` explicitly on any partial planner
  projection, including an empty one; never use that flag as a substitute for
  either membership proof. A gap, epoch change, unsupported temporal transition,
  or lost suppressor/history coverage revokes affected admitted memberships and
  reaches continuation/in-flight invalidation before further use.

No such live membership proof is established by this first slice. Its ledger
may retain partial contribution evidence, but its adapter must retain unknown
query knowledge rather than project raw contributors into planner `States`.
These are proposal constraints and tests owed, not speculative production edits.

## Isolated prerequisite implemented: raw juh/juu observation reader

`GivreClone.Protocol/RawStateMessageReader.cs` is an isolated reader, not a
ledger or live producer. It takes a caller-supplied schema version and catalog;
`MZP0ProtocolAdmission.Supports37Family` gates the version label (3.7.x.y, not
one patch fingerprint or 3.8). This family gate is not proof of matching schema,
catalog origin, connection identity, or live admission.

Supported raw codes are `juh` and `juu` only. All consumed tags resolve through
the existing raw catalog members, without semantic aliases or fallback tags:
juh.fxet -> lng.gdzl -> llg; lng.gdzm/gdzn are action/source;
llg.gdps/gdpw/gdpz are state/network-instance/actor; llg.gdpt/gdpv are
start/end lfz, whose gcra/gcqz are round/order. Add accepts only actions
950/951/952. Remove reads juu.fxhi/fxhj as network instance and actor; it
does not assign semantics to unused #3/#4 or call a negative ID remove-all.
Nonnegative supplied IDs expose `(baseId,scope=1)` and negative supplied IDs
the grounded `(-1,0)` sentinel. An omitted ID has no derived scoped identity.

Nullable signed scalars preserve omission versus explicit zero. Their protobuf
effective default is zero, but the reader does not turn omission into a proved
identity/state. Nullable endpoints distinguish absent endpoint from a present
empty endpoint with nullable coordinates. Signed int32 range, varint wire type,
nested-message bytes, duplicate consumed fields and required raw mappings are
checked; malformed/unsupported input returns no observation. Unknown unused
fields are not interpreted. The decoder's tentative nested Message view is not
used as schema evidence when original bytes are available: proven message
fields are reparsed, including empty messages and malformed nested bytes.

The outputs contain raw observations only: no status, eligibility, duration
arithmetic, query-active result, positive/absent planner set or completeness.
`jth` parameter update and `jsq.#31(jrl)` single/bulk discrimination are
explicitly unsupported in this first coherent add/scoped-remove slice. Their
accepted native evidence remains above for a separately scoped extension.
No App/CaptureDecoder/planner wiring, mapping edits, dependencies, game actions
or commits were added.

`GivreClone.Tests/RawStateMessageReaderTests.cs` drives actual raw protobuf
bytes through RawProtobuf and the reader using the existing copied catalog.
Fixtures distinguish actor/source and state/instance; cover all three actions,
signed actors/IDs, endpoints, omission/zero/empty messages, sentinel scope,
family gating, unsupported codes/aliases, semantic-only catalog rejection,
overflow, malformed nesting/wire types and duplicate consumed fields. These
tests establish the reader boundary only, not native eligibility or live truth.

Verification completed: the existing explicit `dotnet test
GivreClone.Tests/GivreClone.Tests.csproj --filter
"FullyQualifiedName~RawStateMessageReaderTests|FullyQualifiedName~FightVisibilityTests"
--verbosity normal` run exited 0: **42 passed, 0 failed, 0 skipped** (18.5s
reported test duration; 28.4s build/run). Protocol and Tests compiled successfully.
The earlier no-restore command exited 0 without a discovery/result summary and
is not counted as test evidence. Fresh LSP diagnostics timed out for both new
files; compiler/test success is available, but fresh LSP results are not claimed.

Public entry signatures are
`RawStateMessageReader(string? schemaVersion, ProtocolCatalog catalog)` and
`RawStateObservation? Read(string rawCode, IReadOnlyList<ProtoField> payload)`.
The constructor-supplied version is checked on every Read with
`MZP0ProtocolAdmission.Supports37Family(string? version)`. Tests include accepted
3.7.0.0 and 3.7.99.123 candidates, rejected 3.8/3.6, incomplete/malformed labels
and null, while using the supplied raw catalog rather than patch acceptance.
No family gate attests schema equivalence or grants live production admission.

Changed files for this unit are exactly the new Protocol reader, the new
`GivreClone.Tests/RawStateMessageReaderTests.cs`, and this evidence document.
The reader owns raw state-message parsing (84 nonblank/noncomment source lines);
its focused test file owns that wire boundary (104 such lines). Parsing returns
typed nullable wire observations, uses a raw-code switch with unsupported
default, and catches only boundary FormatException. No new dependencies or
test-only production seams were introduced. Tests use deterministic encoded
wire fixtures, not mock echoes; they distinguish wrong coordinates, signed
values, missing/default presence, malformed input, aliases and family admission.
No deliberate production mutation run was performed; the requested related
filtered test run is the executed behavioral evidence.

Evidence artifact:
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/docs/validation/observed-immunity-producer-contract-20261105.md`.
Compiled test artifact:
`GivreClone.Tests/bin/Debug/net10.0/GivreClone.Tests.dll` in that worktree.

## Bounded reader completion: parameter update and removal request variants

The same `RawStateMessageReader` now also parses `jth` and `jsq.#31(jrl)`;
the earlier first-slice statements leaving both unsupported are superseded only
for these raw observation branches. Constructor/Read signatures and the existing
caller-supplied 3.7 family admission policy are unchanged.

- `jth.fwys` (#1 signed int32) becomes nullable `Parameter0`, `fwyt` (#2
  signed int32) the nullable network instance ID, and `fwyu` (#3 signed int64)
  the nullable actor. `RawStateParameterUpdateObservation` carries those fields
  only: it neither refreshes lifetime nor changes the observed state ID.
- `jsq.fwvz` (#31 nested) must be the sole outer envelope field for this
  supported slice. Mixed/duplicate/other envelopes are rejected rather than
  guessing oneof order or meanings for ungrounded metadata. `jrl.fwov` (#1
  signed int32) and `fwot` (#2 signed int64) provide nullable ID and actor.
  `RawStateRemovalRequestObservation.RequestKind` is `ActorFilteredBulk` only
  for an explicitly present negative case1 ID; absent/default oneof and explicit
  zero are `SingleInstance`. The raw ID stays null when omitted, while
  `EffectiveScopedInstance` exposes the grounded absent-getter default `(0,1)`.
  Explicit negatives retain their wire value and convert to sentinel `(-1,0)`.
- `jrl.fwox` (#3/case3) remains unsupported, including zero. Both case1+case3
  orders are rejected, as are duplicate consumed fields, unknown request fields,
  bad wire types, malformed nesting and int32 overflow. `fwou` (#4 bool) is
  checked as absent/0/1 but does not select scope/filter: the grounded handler
  does not consume it. All tags resolve through the inspected raw catalog names;
  no numeric fallback or semantic alias was introduced.

The bulk classification describes the request only. No stored +0x28 classifier,
actor instance store, cleared states, filter execution, eligibility, active
projection or completeness is implemented or inferred. In particular, this
does not promote the scoped `juu` sentinel into a bulk request.

The same focused test file now includes real encoded protobuf fixtures for
signed/default/missing parameter updates, explicit-negative versus absent/zero
case1, empty present envelopes, ignored boolean classification, case3/unknown
variants, competing oneof fields in both orders, duplicate envelopes/scalars,
malformed nested/wire/range cases, and shared 3.7 admission for the new branches.
Only the existing Protocol source, neighboring test and this document changed;
no ledger, live routing, mapping changes, dependencies, commits or game actions.

## Add-constructor eligibility provenance: bounded juh chain

**Conclusion: the current raw observation must still carry unknown overall
eligibility.** The registered add constructor does ground the bulk classifier
and initial instance mask4 from two additional raw llg fields. Removed=false
is an allocation default, not a wire flag or explicit constructor write. The
query's effect flag0x10 cannot yet be certified on the final wrapped effect:
the directly reached virtual setters and virtual clone remain precise unresolved
edges. No active-state admission follows from these partial construction facts.

This append used only the previously loaded native image, original metadata
and annotated extraction identified above: installed reference 3.7.13.17,
`GameAssembly.dll` SHA-256
`ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591`,
and the existing `givre-state-cpp2il-20261104/dll-addresses` extraction.
Addresses below are RVAs, image base `0x180000000`. Inspection was restricted
to the accepted registered `gau.bizt -> gau.bizc -> eai.vbp(0x270D930)` chain,
its `dzx` constructor and directly reached effect/flag bodies. No new global
caller/writer scan or end/expiry investigation was performed. Only this document
was edited; no production, mapping, tests, game or git actions occurred.

### Argument provenance and final instance fields

The previously grounded registered forwarder supplies source, action, llg and
false to `eai.vbp`. In vbp, R14 is llg and EDI is explicitly zero at
`0x270DA19`. The fresh instance is allocated by `0x270DB61..68 -> 0x59BBC0`,
then passed to `dzx..ctor`, original row16600/token `0x060040D9`, actual call
`0x270DBC7 -> 0x2705B80`. The constructor saves one register and reserves
0x20 bytes: its stack argument loads below therefore refer to caller stack
offset plus0x30. This grounds the argument correspondence rather than assuming
field names from an extraction.

| Requested field | Actual construction and query use | Admission consequence |
|---|---|---|
| Instance removed byte +0x4C | No +0x4C write in constructor `0x2705B80..0x2705CC4`; its base call `0x2705C01 -> 0x65DFD0` is `ret 0`. Fresh allocation precedes the constructor. Eligibility explicitly requires zero at `0x2706D16..1D`. | False is the managed fresh-object zero-initialization default, not a serialized flag. The allocator body itself was not proved here; no explicit native clear instruction is claimed. This is an initial-construction fact only, not the current value after updates/removal or an upsert of an existing instance. |
| Instance flags +0x70, mask4 | vbp reads llg+0x58 at `0x270DA53`, selects EBP via jump table `0x270DA73`, and passes it at caller stack+0x38 (`0x270DB96`). Constructor reads callee stack+0x68 at `0x2705C30`, assigns +0x70 at `0x2705C43` when changed, then calls `0x2707280`; it tail-calls that same classifier at `0x2705CC4`. Query tests mask4 at `0x2706D59..61`. | The initializer's mask4 is wire-derived, not a universal false default. The actual constructor's classifier preserves bit4; see the exact table below. |
| Bulk classifier instance +0x28 | vbp reads llg+0x50 at `0x270DAA9`, maps it into R15D, writes caller stack+0x40 at `0x270DB8E`. Constructor reads callee stack+0x70 at `0x2705C4E`, stores +0x28 at `0x2705C52`. Bulk service compares this stored member to1 at `0x2714F05..1D`. | llg.#9=0 maps to stored1, including the protobuf effective default when omitted. It is not the action ID, state ID, source, or jrl bool. The current raw reader does not expose this field. |
| Underlying effect flags +0x10, mask0x10 | Query follows instance+0x38 -> wrapper+0x10, then `0x26BF700 -> 0x7C5130`; the latter shifts bit4 at `0x7C5134`. The final wrapper is made in `vbu` at `0x270E348 -> 0x26BEAD0`, whose `0x26BEB69..77` virtual call clones/converts the provisional effect and stores the returned object at wrapper+0x10 (`0x26BEB87`). | No wire bool directly establishing this final bit was proved. Provisional-effect defaults and four flag setters alone do not prove that all virtual parameter setters and the final virtual clone preserve bit0x10. Keep this gate unknown. |

### Exact wire fields and initialization tables

The already accepted llg serializer `0xC62180` ties the two newly traced
members to raw catalog coordinates without assigning semantic aliases:

- **llg.#9 (`gdqa`), enum/int32 member +0x50:** serializer loads tag0x48
  at `0xC622FE`, value at `0xC62308`. vbp maps wire0 -> stored classifier1,
  1 ->2, 2 ->3, 3 ->4, every other value ->0 (`0x270DAA9..DADB`).
  Omitted scalar has effective wire0; the table does not establish any gameplay
  name for the classifications. Actual filter execution still requires stored
  instances and remains outside the isolated request reader.
- **llg.#11 (`gdqc`), enum/int32 member +0x58:** serializer loads tag0x58
  at `0xC62340`, value at `0xC6234A`. The eight jump-table entries at
  `0x270DCE4` resolve to these full initial flags:

  | Wire value | Initial flags |
  |---|---|
  | 0 or1 | 0x41 |
  | 2 | 0x42 |
  | 3 | 0x20 |
  | 4 | 0x50 |
  | 5 | 0 |
  | 6 | 0x40 |
  | 7 | 4 |
  | Outside0..7, including negative | 0 |

  Therefore initial mask4 is set exactly for wire7 in this builder. Omission
  defaults to wire0/0x41, whose mask4 is clear. The actual constructor calls
  **`0x2707280`, not `0x2705E80`**. Its instructions clear/recompute bits8,
  2,0x200 and0x180 (`0x2707286`, `0x27072B3`, `0x27072DD`,
  `0x2707314`), optionally set0x80/0x100 (`0x2707337`,`0x2707344`),
  and leave mask4 intact. The previously recorded explicit `or ...,4` at
  `0x2705F3E` belongs to a different method and cannot be imported into this
  constructor path. Action950/951/952 still sets classification bit2 via
  `0x27072D0..D9`; that does not override the mask4 gate.

These are build-local native conversion rules, not authorization to trust a
different supplied catalog merely because its version label belongs to3.7.
The existing normalized reader's fields do not yet include llg.#9/#11.

### Provisional effect versus final wrapped effect: precise remaining edges

`eai.vbu(0x270E040)` allocates a provisional effect at `0x270E087..9B`
and calls constructor `0x7C61D0`. That constructor writes byte1 to its
flags member+0x10 at `0x7C61D2` and tail-calls the no-op base constructor.
With fresh-object zero initialization, this provisional flag word initially
has mask0x10 clear. It is not yet the final effect read by the query.

The grounded vbu body then invokes virtual setters on this concrete object's
class: slot0x188 with llg+0x54/field10 (`0x270E0E4`), slot0x1A8 with
llg+0x60/field13 (`0x270E113`), and slot0x1C8 with llg+0x18/state
field1 (`0x270E142`). The existing state-value evidence grounds the last
setter's parameter role, not all its flag side effects. When llg.#11 is7
and the supplied bool is false, `0x270E1BE -> 0x270E370` reaches additional
parameter setters after the predicate call `0x270E39A -> 0x7D4360`.
The exact concrete virtual setter targets and their complete flag writes were
not resolved in this bounded unit. Allocation type-use slot `0x6BAEA18`
contains original encoded type use `0x20023113`; this is the precise original
type/vtable linkage available for a later targeted resolution, not a guessed
extraction token or permission for a general reverse scan.

vbu's directly reached lookup `0x270E1D0 -> 0x26CB3A0` chooses a list
from supplied context+0x88/+0x90 according to llg.#3 bool and matches the
entry's underlying effect+0x14 against llg.#6, returning null when absent
(`0x26CB48E..91`, `0x26CB4E1..E3`). On null it passes false to four
effect flag setters (`0x270E1E9/1F6/203/210`); on a match it copies the
corresponding flags from the matched effect (`0x270E245/267/289/2AB`).
The concrete setters `0x7D5950`, `0x7D59B0`, `0x7D5890`, `0x7D58F0`
operate on masks1,8,2,4 respectively, through generic bit mutation
`0x7ADF30` (byte branch `0x7AE088..A0`). **None of those four masks is
0x10.** Thus the matched-context flags do not by themselves explain the
query's bit0x10; copying arbitrary eligibility from spell data would be wrong.

Finally wrapper constructor `0x26BEAD0` invokes the provisional effect's
virtual slot0x218 with method-info slot0x220 (`0x26BEB69..77`) and stores
the returned effect at+0x10. Its concrete original vtable target, copy semantics
and flag preservation are the final exact unresolved edge for certifying the
queried bit0x10. This unit stops there rather than presenting provisional
allocation/default as proof of the final wrapped instance's eligibility.

### Admission result and stop

A future raw record can carry the additional #9/#11 wire evidence and the
constructor-local classifier/mask4 derivation when its schema is verified.
It may describe removed=false as a fresh managed construction default, explicitly
separate from current stored-instance history. It **cannot yet carry verified
overall eligibility**: final effect bit0x10 preservation has the unresolved
virtual edges above, and this task deliberately does not reopen the separate
duration/delay/time-support proof or contribution coverage. A future add may
also reconcile with an existing instance; fresh-constructor defaults do not
prove that instance's later fields. Keep eligibility explicitly unknown and
keep raw evidence out of authoritative planner positives. No reader or reducer
code was changed. All four requested fields now have a bounded constructor/query
provenance or a precise unresolved edge, which is this task's stop condition.

## Exact virtual-edge resolution: final constructed effect preserves clear flag0x10

**The named virtual edges are now resolved.** On this registered fresh-add
construction path, the three concrete parameter setters do not modify flags,
the special7 branch uses two of those same setters, and the concrete clone
explicitly copies source flag0x10 to the returned effect. Thus the provisional
clear flag0x10 survives into the final wrapper's queried effect. This supersedes
only the preceding section's unresolved-final-effect-bit conclusion; it does
not establish complete eligibility, current stored-instance state, time support,
or observed-contribution coverage.

Only the exact named type/vtable edges and their reached bodies were inspected
in the already loaded installed-reference3.7.13.17 image/original metadata.
Artifact paths and hashes are unchanged from the preceding section. This was
read-only artifact analysis plus this append; no broad scan, expiry work,
production or reader edit, build, test, game or git action occurred. All native
addresses below are RVAs at image base `0x180000000`.

### Concrete original type and module, not extraction-token guesses

Native usage slot **0x6BAEA18** contains **0x20023113**. Its encoded payload
decodes to original type index `(0x20023113 & 0x1FFFFFFF) >> 1 = 71817`
(`0x11889`), **not** index0x23113. Original type row **36356**, metadata
record offset38450340 (`type-section35469148 + 36356*82`), has that byval
index and names
`Core.DataCenter.Metadata.Effect.Instance.EffectInstanceDice`.

Its original vtable starts at row **418023**, metadata offset **0x211FD08**;
there are16 entries. The relevant native virtual offsets map to entries
5/7/9/14 (the same class layout independently agrees with the previously
grounded parameter0 setter). Original encoded method entries decode to original
method rows, whose original tokens index the **DataCenter** module, not Core's
method array. The established codegen-module record is **0x5B46480**, name at
**0x5B46508**, method-pointer array **0x6831060**, count1759.

| Native virtual code offset | Original vtable entry / encoded method | Original row / token / method | DataCenter pointer slot -> actual body |
|---|---|---|---|
| 0x188, method-info0x190 | 5 / 0x600A40C7 | 335971 / 0x06000599 / set_parameter0 | 0x6833D20 -> 0x7D6640 |
| 0x1A8, method-info0x1B0 | 7 / 0x600A40CB | 335973 / 0x0600059B / set_parameter1 | 0x6833D30 -> 0x7D66A0 |
| 0x1C8, method-info0x1D0 | 9 / 0x600A40CF | 335975 / 0x0600059D / set_parameter2 | 0x6833D40 -> 0x7D6700 |
| 0x218, method-info0x220 | 14 / 0x600A40C3 | 335969 / 0x06000597 / Clone | 0x6833D10 -> 0x7D6280 |

### Actual setter writes and special7 branch

The complete setter bodies convert the supplied boxed value using
`0x41B4660`, then write only their concrete parameter member:

- `set_parameter0`: conversion call **0x7D6686**, int32 write
  **0x7D6690 -> effect+0x80**, return0x7D669B.
- `set_parameter1`: conversion call **0x7D66E6**, int32 write
  **0x7D66F0 -> effect+0x84**, return0x7D66FB.
- `set_parameter2`: conversion call **0x7D6746**, int32 write
  **0x7D6750 -> effect+0x78**, return0x7D6758.

The conversion receives the boxed argument, not the effect pointer. None of
these bodies writes effect+0x10 or calls another effect mutator. Accordingly
the `vbu` virtual calls at0x270E0E4/0x270E113/0x270E142 are now concrete
parameter writes, not remaining eligibility-flag edges.

When llg.#11 is7 and the supplied bool isfalse, the already grounded
`0x270E1BE -> 0x270E370` helper calls predicate **0x7D4360** at
**0x270E39A**. Its inspected body0x7D4360..0x7D4503 initializes/reads
the effect's cache+0x70 and action+0x1A and returns a cached/catalog-derived
predicate value; it does not write effect flags+0x10. The exact truth value
is immaterial to flag preservation because both branches preserve that bit.
On false, helper returns. On true, it computes two parameter values, boxes
them, and invokes **0x188 at0x270E3E1** and **0x1A8 at0x270E40E**.
Those resolve to the same concrete parameter0/parameter1 setters above.
No special7 branch sets, clears or aliases flag0x10.

### Clone copies the actual queried flag, not just a provisional default

`hq` wrapper constructor invokes virtual0x218 at **0x26BEB77** and stores
the returned object at **wrapper+0x10, 0x26BEB87**. That virtual call now
resolves to `EffectInstanceDice.Clone` **0x7D6280..0x7D6559**. Its own
allocation uses **the same type-use slot0x6BAEA18** at0x7D62B3 and calls
allocator0x59BBC0 at0x7D62BA. It initializes destination flags byte+0x10
to1 at **0x7D62C7**, then copies effect data and individual flag bits.

Crucially, this is an explicit copy of the relevant source bit:

```text
0x7D646E  movzx ebp, byte ptr [rbx+0x10]  ; source effect flags
0x7D6472  shr   bpl,4
0x7D6476  and   bpl,1                   ; source flag0x10 as bool
0x7D64A2  movzx r8d,bpl                 ; supplied bool
0x7D64A6  mov   byte ptr [rsp+0x30],0x10 ; exact mask
0x7D64AB  lea   rcx,[rdi+0x10]           ; destination effect flags
0x7D64AF  call  0x7ADF30                ; bounded bit replacement
```

The previously inspected byte implementation at **0x7AE088..0x7AE0A0**
implements `(old & ~mask) | (-bool & mask)`. This copy therefore clears
destination flag0x10 if the source isclear and sets it if the source isset;
it is preservation, not a universal forced-clear operation. The rest of the
clone copies separate masks0x40/0x80, then returns the destination inRAX
at **0x7D6547**, returning at0x7D6559. It also copies duration byte+0x30
and delay byte+0x3D at0x7D6300/0x7D6307; no new lifetime conclusion is
drawn from those copies. The wrapper stores the clone and populates its own
other members; its remaining body contains no rewrite of the clone's flags.

### Final construction consequence and boundary

The provisional effect constructor **0x7C61D2** starts flags byte+0x10
at1. The resolved parameter setters, special7 branch and the previously
grounded flag setters for masks1/8/2/4 do not change mask0x10. The resolved
Clone then copies that clear mask0x10 into the actual object stored at
wrapper+0x10. The query **0x26BF700 -> 0x7C5130** therefore sees this
bit clear for the successfully constructed fresh-add effect on this chain.
There is no longer an inaccessible virtual target among the edges requested
in this unit, and the conclusion rests on concrete final-effect copy semantics,
not solely on an allocation-default assumption.

This is a constructor-local bit provenance result, not a promise that later
mutations cannot change flags or that an add/upsert replaces all old instance
history. Removed/current history, duration validity, delay/time support, mask4
and unseen suppressors remain separate gates. No eligibility boolean, ledger,
reader-field additions or planner projection was implemented. The requested
concrete targets and relevant writes/copy are grounded, so this unit stops.

## Raw add evidence extension: nullable gdqa and gdqc only

`RawStateAddObservation` now carries `int? RawGdqa` and `int? RawGdqc`,
read through existing catalog members `llg.gdqa` (#9) and `llg.gdqc` (#11).
They preserve wire omission as null and explicit zero as zero, retain signed
unknown enum values, and use the existing signed-int32/wire-type checks.
Neither field is a computed classifier, instance flags value, eligibility
boolean or active-state claim. Constructor derivation tables above remain
documentation only; no production derivation or consumer was introduced.

The existing encoded-protobuf test file adds explicit0/1/3/7, negative unknown,
independently missing versus zero, wrong-wire and both-sided int32-overflow
fixtures for these fields. Existing observation assertions now explicitly
expect null for omitted new fields. The same caller-supplied 3.7 family gate
and raw catalog lookup remain in force without aliases or fallback tags.

Only `GivreClone.Protocol/RawStateMessageReader.cs`,
`GivreClone.Tests/RawStateMessageReaderTests.cs` and this document changed
for this unit. No ledger, live wiring, planner projection, dependencies,
mapping edits or further native work was added. Fresh LSP diagnostics timed
out for both touched code files; no fresh diagnostic success is claimed.
The single related Release test run is monitor `mon_BVX4HGAMTS63HM22`,
session `bash_66`; its definitive result is pending at this append.

## Temporal storage follow-up: eligibility reads the truncated bytes

The parent re-read the retained duration/delay and eligibility evidence above
after the read-only follow-up. The updater stores AL at `0x27061EA` and
`0x2706218`; getters at `0x26BF75D` and `0x26BF98D` respectively sign-extend
duration and zero-extend delay. Consequently the necessary temporal gates are:

```text
storedDuration = signed8(low8(computedDuration))
storedDelay = unsigned8(low8(computedDelay))
(storedDuration == -1 || storedDuration > 0) && storedDelay == 0
```

Testing the untruncated formula results instead is not equivalent: computed
duration 128 reads as -128, while computed delay 256 reads as zero. This is
literal retained native storage behavior, not evidence that these distances
occur in valid game input. No upstream range bound has been established.
Unsupported temporal ranges must not be silently saturated or used to infer
confirmed state membership. Endpoint expiry remains unproved because the
grounded positive-end formula clamps to 1 rather than removing the instance.
No normalizer, live ledger or planner state projection was implemented here;
the follow-up did not independently redisassemble the installed image.
