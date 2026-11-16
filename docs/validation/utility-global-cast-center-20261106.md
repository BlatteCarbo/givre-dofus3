# Global caster utility with a distant cast center

Read-only investigation st_01a1aeb9 identified a candidate-enumeration mismatch.
The parent independently read the planner and resolver before changing them.
The resolver's exact uppercase `C` mask selects the caster outside the effect
zone, but the planner previously considered only eligible fighter positions
for a point effect without mandatory free-cell targeting. Positive minimum
range excludes the caster's position, omitting a legal empty center.

The existing beneficiary theory now covers mandatory empty centers and optional
empty centers at minimum range1, with allowed and disallowed caster cases.
Before the production change, bash_186 exited1: the allowed optional-center
case failed Assert.Single because the plan was empty. This extends the
existing behavior owner rather than adding a duplicate resolver-only test.

The planner now includes board centers for exact global-caster masks with
positive minimum range when occupancy is not mandatory. Existing range,
line-of-sight, allowed-cell, occupancy and beneficiary checks still decide
whether each candidate can produce a legal useful cast. No score bonus or
new effect semantics were added. The focused compilation/test run bash_187
completed with exit0: 299 passed, zero failed or skipped, 9.7 seconds reported
test duration. It includes planner, resolver and AutoSpellRotation tests.
The build reported 256 warnings; they were not suppressed. Full-suite follow-up
bash_188 completed with exit0: 4798 total, 4796 passed, zero failed and two
existing skips, 206.4 seconds. This covers the current compiled source and
does not substitute for live utility acceptance.

The C# language server still crashes while enumerating the inaccessible
artifacts/kourial-tools/pypdf directory. No permissions were changed.
Compiler/test results, not that failed LSP request, must establish validation.

This is a descriptor-contract regression, not evidence that an installed
learned spell has this exact geometry/mask/range combination. No live build
was replaced, and no live utility acceptance is claimed.
