# Sram invisibility: current integration gap

## Parent producer trace after managed full-suite restoration

The full managed suite now passes4740 tests with2 existing skips (4742 total,
zero failures,183.5s,exit0); this does not close invisibility tactics.
EnemyReply currently consumes ExpectedDamage/TurnAp and an aggregate per-cell
threat map. Its MonsterThreat producer, reached through LaunchService.LotF,
loads a four-number grade record [AP,MP,reach,turnDamage]. MonsterThreatCatalog
retains no spell identity, targeting requirement or area shape. Consequently
adding a visibility discount to EnemyReply alone would manufacture immunity
against unknown area attacks. No such discount or speculative field was added.
The useful next boundary is the threat-catalog producer retaining verified
spell targeting/area facts, not another admission-only change to action150.

## Correction: real spell is not pure action 150

The later installed-client investigation in sram-reveal-contract-20261105.md
identifies spell 12913 and levels 40916-40918. The parent independently read
the existing version-tagged export at N01/SORTS_3.7.13.17.json:5356 and the
current AutoSpellRotation gate: action128 is explicitly admitted. The child's
direct installed-bundle decoding further reports that this real invisibility
spell combines action150 with allied MP gain action128. Therefore the earlier
pure-action150 admission finding below is a synthetic boundary, not a
demonstrated reason this real Sram spell cannot enter the planner. Descriptor
conversion and the full learned-rotation path still require verification.
The parent has not independently rerun the child's bundle decoding. Defensive
targeted/AoE scoring, reveal rules and live evidence remain unresolved.

The parent also read the actual planner adapter in
LaunchService.Combat.Turn.cs:1038-1051. Its OnMe/TargetKind narrowing applies
only when profile.Spells explicitly contains the spell. A learned-only spell
does not take that branch. Consequently AutoSpellRotation.Build assigning an
Enemy default to the mixed action150/action128 descriptor is not by itself
proof that friendly beneficiaries are lost. The downstream routing regression
must establish a failure before changing that classification.

Parent source inspection distinguishes three contracts that must not be
reported as one completed class mechanic.

SpellResolver recognizes action 150 and updates the target's Visibility to
Invisible. CombatTurnPlanner uses simulated visibility to reject a later
targeted cast against a hidden target. The existing
Target_hidden_by_a_cast_is_not_available_to_a_later_targeted_cast regression
exercises that transition, using synthetic damage and a state requirement;
it explicitly does not reproduce the live Invisibilite spell.

AutoSpellRotation.Build can admit an immediate modeled invisibility descriptor,
but ShouldUsePlanner does not include action 150 in its utility-only gate.
Without a modeled damage spell or another recognized utility, the rotation does
not enter the planner. Simply adding action 150 to that gate is insufficient:
the planner's enemy reply scoring currently evaluates living allies without
consulting their simulated visibility. It therefore has no demonstrated
defensive value for a pure self-invisibility cast. Treating invisibility as
zero incoming damage would also invent protection against area effects.

Required next implementation evidence: distinguish targeted enemy attacks from
area/untargeted threats in the enemy reply model, establish the actual spell's
visibility and reveal rules from client data/server observations, and exercise
the learned-rotation-to-planner path with both a useful hiding case and a case
where hiding cannot avoid the enemy reply. Only then change admission if needed.

No code or tests were changed for this finding, and no live fight was run.
The Sram traps/invisibility and learned-utility todo items remain open. Existing
generic visibility transitions do not establish Sram tactical completeness.

Parent traced the live snapshot producer in LaunchService.Combat.Turn.cs:
enemy range and turn damage come from MonsterThreat or the largest observed
hit, with labeled conservative fallbacks. The snapshot does not retain an
enemy spell's targeting or area shape. It also explicitly supplies Visible for
the current self fighter rather than reading its visibility reducer. Thus
preserving observed self visibility in the live snapshot is another required
integration boundary; a resolver-only change cannot establish live invisibility
planning.

The bounded producer correction is now present: self visibility uses the same
reducer observation as other actors, while self-position certainty remains
Confirmed. The parent independently read the production expression, reducer,
full new regression, and decisive red TRX (eight visibility failures and two
visible controls passing). Independent related verification is running as
mon_XGWXBF1M0YWNAFFS / bash_67 in isolated
givre-parent-self-visibility-20261105 artifacts. Fresh LSP requests for the
two changed C# files timed out, so they do not establish clean diagnostics.
The independent parent run completed exit 0: 236 passed, zero failed or
skipped, reported test duration 8 s. This verifies the bounded producer
correction and related dispatch, controlled combat, visibility and planner
regressions. Defensive invisibility value, Sram spell admission and live
validation remain unproven.

Adjacent forced-movement validation completed independently with exit 0:
mon_Y2DWMD883VD56YSJ / bash_55, 59 passed, zero failed or skipped, 3.4 s.
The filter was CombatForcedMovesReplay and SpellResolverTests, using the R4
debug output without rebuilding. This confirms those existing offline tests,
not invisibility or complete Sram behavior. The resolver still explicitly marks
blocked action-5 push collision damage as unmodeled; the pushed position alone
must not be reported as a damage comparison against a direct attack.
