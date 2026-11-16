# Planner slice reconciliation

Read-only comparison st_01a1b172 found no slice-only production behavior in
the bounded planner comparison. Main already contains next-turn approach
scoring, per-spell allowed-target filtering, teleport-funded approach exclusion
and the slice displacement/threat tests, with additional state/visibility guards.
This is not proof that all requested class mechanics or live behavior are done.

Parent independently read the approach tests in both worktrees. The slice's
five-case theory verifies exact endpoints and no casts for aggressive, fleeing,
passive, NeverApproach and AllowApproach=false. Main's corresponding theory
previously covered only aggressive/fleeing, with a loose aggressive endpoint.
The existing main theory now retains all five cases, requires no casts and
checks Cell(3) or Cell(0) exactly. No production code was replaced and the slice
worktree was not modified.

Current planner/rotation/descriptor/damage validation is subscribed as bash_399,
receipt `planner-slice-approach-current.trx`. Its result is pending. This is a
coverage transfer, not a new production fix; historical slice test results do
not stand in for this run. The language server still fails on the unrelated
inaccessible artifacts/kourial-tools/pypdf directory.

The current run bash_399 completed with exit0:266 passed,0 failed,0 skipped
in `planner-slice-approach-current.trx`. All five endpoint/permission cases pass
against main's existing production planner. No live-combat acceptance is inferred.
