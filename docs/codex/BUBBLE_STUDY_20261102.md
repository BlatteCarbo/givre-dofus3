# Bubble combat reference, November 2, 2026

Read-only source inspection at commit
`4cb70b04ad69d7850c76f91f7df108101670d9fc`. GitHub recursive tree reports
`truncated=false`. No upstream binaries were executed or code copied.

## Useful mechanisms

- `BubbleBot.Cli/Services/Fight/SpellWrapper.cs:31-63,197-224,288-320`
  retains a selected spell grade, boostable range, geometry constraints,
  costs and cooldowns rather than reducing a spell to its name.
- `DamageCalculationTranslator.cs:72-109,128-145` translates individual
  effects and critical effects using the requested grade, preserving triggers,
  target masks and zone descriptions. The default immediate trigger is `I`,
  consistent with the installed-data finding in GivreClone. Defaults and
  IDs still require validation against the current installed client.
- `FightInfo.cs:758-774` creates a simulation context with cloned fighters,
  excluding invisible/dead actors. At `1336-1385`, candidate evaluation sets
  the cast origin, computes effects and resets temporary simulation state.
  `528-605` advances a staged action sequence through boosts, attacks,
  movement and post-movement attacks. This is not evidence of optimal
  multi-action search or a safe final position.

## Important missing integration

- `AiEnvironment.cs:11-310,336-342` filters a large explicit spell denylist.
  It excludes Sram invisibility and numerous traps, and many Roublard
  placement/detonation tools. Having their IDs in the repository is not
  automatic AI support.
- `DamageCalculationTranslator.cs:30-62` returns the caster for summon
  creation and no spell for bomb explosions/walls. The lower-level damage
  library is therefore not proof that the application models bomb chains.
- `FightSimulation.cs:24-28,58-66,102-110` includes incomplete adapter methods:
  portal exit lookup returns no exit, mark creation and fighter removal
  do nothing. Generic library mechanics must be traced through these adapters
  before treating them as usable.
- No path in the complete tree matched tactical/native or C++/Rust source
  names. This is a filename-level finding only; it does not establish that
  all source contents lack rendering references. It supplies no evidence for
  the current Unity terrain root or its placement frame.

## Consequences for GivreClone

Keep the per-grade effect descriptor and immutable observed facts. Add
displacement/state/mark transitions only with actual data and end-to-end
adapter support; do not import the denylist as a class implementation.
The new planner budget split and preserved fighter characteristics remain
local unintegrated work. Neither this source study nor upstream simulation
code validates them in live combat.

Before adopting a simulation package, test actor/mark creation, expiration,
movement and removal through its real adapter, then compare predicted
transitions with server events. Sram and Roublard remain explicit unfinished
scope.

## Primary sources

All paths/line numbers above refer to this immutable revision:

- https://github.com/AlpaGit/Bubble.D3.Bot/tree/4cb70b04ad69d7850c76f91f7df108101670d9fc
- https://github.com/AlpaGit/Bubble.D3.Bot/blob/4cb70b04ad69d7850c76f91f7df108101670d9fc/BubbleBot.Cli/Services/Fight/SpellWrapper.cs
- https://github.com/AlpaGit/Bubble.D3.Bot/blob/4cb70b04ad69d7850c76f91f7df108101670d9fc/BubbleBot.Cli/Services/Fight/DamageCalculationTranslator.cs
- https://github.com/AlpaGit/Bubble.D3.Bot/blob/4cb70b04ad69d7850c76f91f7df108101670d9fc/BubbleBot.Cli/Services/Fight/AiEnvironment.cs
- https://github.com/AlpaGit/Bubble.D3.Bot/blob/4cb70b04ad69d7850c76f91f7df108101670d9fc/BubbleBot.Cli/Services/Fight/FightSimulation.cs
- https://github.com/AlpaGit/Bubble.D3.Bot/blob/4cb70b04ad69d7850c76f91f7df108101670d9fc/BubbleBot.Cli/Services/Fight/FightInfo.cs
