# Future attack state-mask regression

The speculative NextTurnPotential calculation summed damage rows without
checking E/e state requirements, although immediate resolution already checked
them. A deterministic planner fixture puts an unthreatening enemy beyond this
turn's reach, with the required state either explicitly present or absent.
The pre-fix run bash_181 fails the absent-state case at Assert.Empty because
the planner walks toward damage that cannot affect the target; the present
control remains useful. This is a new regression, not a changed old assertion.

CombatTurnPlanner now excludes a row's future damage when its E/e condition is
false or unobserved. Star-prefixed conditions use the caster, ordinary ones
use the target, following SpellResolver.Mask. No speculative state application
or expiry is assumed. The table also checks negative state requirements and
caster conditions. The current planner/resolver verification is subscribed as
bash_183; results are pending. The preceding bash_182 stopped at compilation
with CS0136 because a mask-token local reused the damage local's name. That
name collision was corrected without changing the regression's expectation.
No live deployment has occurred.

This bounded fix does not claim full mask-aware future damage, trap triggers,
state expiry, summon initialization or multi-turn enemy simulation. Compiler
verification is required because the C# language server still fails while
enumerating the inaccessible artifacts/kourial-tools/pypdf directory; permissions
were not changed.

The corrected focused run bash_183 completed with exit 0: 209 passed, zero
failed or skipped, 698 ms. This includes all eight presence/absence and
caster/target table rows plus the existing planner and resolver tests.
Full managed-suite verification is subscribed as bash_184 against the same
compiled Release artifact, with --no-build --no-restore and no filter. Its
result is pending. Passing focused cases is not claimed as live validation.

Full managed-suite run bash_184 completed with exit 0: 4794 passed, zero
failed, two existing skips, 4796 total, 3 minutes 15 seconds. The PacketBus
stress case also passed in this run; this is not a claimed fix for its earlier
intermittent timeout. The narrow state-mask correction is source/test verified.
The running bot still uses the previous artifact, so live deployment and
attributable in-game validation remain under the broader combat task.

## Secondary worktree reconciliation

Read-only investigation st_01a1b0d6 compared the approach-cast-retreat candidate
in `combat-planner-slice1` with the main worktree. The secondary regression at
`CombatTurnPlannerTests.cs:35-47` is already present in main at lines851-863:
a seven-cell corridor,3AP,4MP,range3 attack and enemy at cell5 require movement
before damage, then retreat to cell0. Main already searches walking plus casts
and post-cast movement. This candidate needs no additional port or duplicate
regression. The investigation ran no new tests and made no changes; historical
test receipts above do not establish live kiting or complete learned utilities.

Parent independently read the current automatic rotation adapter in
`LaunchService.Combat.Turn.cs:484-501`: descriptor-backed discovery is wired
for non-script turns, while script-driven rotations intentionally omit those
descriptors. The existing utility beneficiary and global-center evidence was
also reviewed. No new utility bug or full secondary-worktree parity is claimed
from this bounded comparison.
