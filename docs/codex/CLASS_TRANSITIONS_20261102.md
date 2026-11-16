# Shared combat transitions and class coverage

This is an implementation ledger, not a claim that generic profiles implement
all classes. User scope explicitly includes Xelor Telefrags and Eliotrope
portals, as well as Sram, Roublard and other classes.

## Observed implementation boundary

GivreClone combat-worktree tests cover immediate point damage, selected
heals, percentage maximum-life healing, selected flat shielding, walking and
simple teleportation. Resistance dictionaries and exact spell-grade lookup
now feed this model. These changes are now integrated into the primary
worktree; they are not deployed into the running application or live-certified.

Forced push/pull/swap transitions have scenario tests and parent review.
Runtime admission remains disabled by ForcedMovementSafe=false because the
required carry/state/mark/portal observations are not established.

| Shared transition | Necessary facts beyond spell names | Class examples; coverage status |
|---|---|---|
| Occupancy-changing movement | Current cells, walkability, blockers, immovable states, event confirmation | Many classes; simple caster teleport covered, forced movement pending |
| Historical movement | Ordered per-actor history, turn-start position, push/pop semantics | Xelor; not integrated |
| State add/remove and hooks | Actor/state IDs, origin spell/grade, duration, trigger order, per-cast and per-turn history | Xelor, Zobal, Ouginak and others; not integrated |
| Ground marks | Owner, cell geometry, mark kind, active/used flags, placement order, lifetime | Sram, Feca, Eliotrope and others; not integrated |
| Network routing | Owned marks, usable entries, chain order, projected aim, exit origin, LOS/range rules | Eliotrope; not integrated |
| Summon lifecycle | Owner, template, capacity cost, turn/control eligibility, creation/removal events | Osamodas, Sadida, Steamer, Roublard and others; control lifecycle partly tested, class summon model incomplete |
| Linked effects and carrying | Carry relation, damage redirection/sharing, trigger recursion, immunity rules | Pandawa, Sacrier and others; not integrated |
| Bomb chains | Bomb type/owner, age/combo states, walls, propagation and one-explosion-per-chain history | Roublard; not integrated |

Every named class above is an example of a dependency, not an exhaustive class
inventory. Exact learned-grade data and server-observed facts must determine
admission. Unsupported state criteria remain rejected rather than assumed true.

## Xelor reference evidence

JondoEmu revision `683ab49c73f65b7c4ab7c1134c448f7fa850f6bc`:

- `Jondo.Unity.Server/Managers/MonsterTactics.Planner.cs:879-907` checks
  alive/pinned/valid/walkable landing, moves into an empty cell or swaps with
  an occupant, records both participants and limits Telefrag generation to
  one per spell simulation. Position history is preserved except for explicit
  undo movement.
- `:789-822` scopes the Telefrag map to the current spell and restores the
  prior map after simulation. `:909-954` maintains state-change hooks,
  including hooks carried by already active spells.
- `Jondo.Unity.Tests/Combat/XelorParityTests.cs:70-131` compares simulated
  positions, states and AP with the same emulator engine across learned
  spell pairs, distances and third-party occupancy. It requires over 150
  compared cases, but explicitly tolerates spell 13292 as a known gap.
- `Jondo.Unity.Tests/Combat/PreviousCellHistoryTests.cs` distinguishes walking
  history, repeated returns and restoration after speculative replay.

This is evidence for required state and history structure, not proof that
GivreClone can generate or consume a Telefrag merely by swapping cells.
AP grants must follow the actual child effects, criteria and cast history.

## Eliotrope reference evidence

- `Jondo.Unity.World/Fights/Portals.cs:20-56` retains owner, placement spell/
  grade, dice/value, used status and neutralising actor.
- `:60-85,92-150` records a four-per-owner network and occupancy-based
  activation. Several rules are explicitly labelled inferred in the source.
- `:154-161` resets use at turn start and neutralisation for its owning
  actor's turn; `:169-196` follows the nearest usable same-owner portal,
  breaking equal-distance ties by newer portal ID.
- `:203-211` projects caster-to-entry vector from the exit and rejects
  off-board projection. `:214-232` sums link distances and calculates a bonus
  explicitly labelled inferred from effect text, not measured A/B damage.
- `MonsterTactics.Planner.cs:797-816` changes the simulated cast origin/aim
  through the network, marks portal context and restores that context after
  evaluation.

Portal discovery, identity, ordering and active-state events are prerequisites
for using these rules against current client data. A guessed empty-cell
teleport is not equivalent to routing a spell through portals.

## Actual laboratory execution

Parent ran the unmodified pinned source in an isolated scratch checkout with
the delivered 249,536,512-byte world.db and tracked JSON data.

- 133 selected Sram/bomb/displacement/Xelor/portal/history tests: passed.
- 117 additional class-state/planner/passive tests: passed.
- No second Dofus client or emulator server was started.
- Some upstream tests can return early when catalogue data is absent.
  These runs supplied the data, but the totals are not full coverage proofs.
- Emulator-engine parity tests are not independent official-server oracles.

Primary source base:
https://github.com/Keka-Bron/JondoEmu/tree/683ab49c73f65b7c4ab7c1134c448f7fa850f6bc

Path correction when following the separate feasibility audit's older links:
the inspected effect engine is `Jondo.Unity.Server/Managers/EffectEngine.cs`,
and ground glyph types are in `Jondo.Unity.World/Fights/Glyphs.cs`, not the
Handlers/Managers locations stated by some of its citations.
