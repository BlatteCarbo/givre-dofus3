# State-gated future damage in combat planning — 2026-11-06

A targeted regression reproduced the planner approaching to prepare damage whose required state was explicitly absent on the target. The future-damage potential calculation was corrected to honor positive and negative state conditions on both target and caster, consistent with the cast resolver's restrictions.

Validation at the snapshot boundary: 209 targeted planner/resolver tests passed; then the full source suite passed with 4,794 tests, zero failures, and two existing exclusions. The work tracker stood at 196/238. This was source-level validation only: the live build still predated the change, so live behavior was not validated or deployed.

Separately, account keyboard QA continued across account rows without enabling account commands. An isolated native check was launched using a QA profile with only a disabled dummy account and a distinct control pipe; existing Bastionbot and Sylvanbot instances were left running. The native readback result was not yet available at the snapshot boundary. Do not interpret this as completed accessibility or compact-layout QA.
