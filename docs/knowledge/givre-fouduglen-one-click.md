# Fouduglen one-click hunt scope

On 2026-10-31, the user requested a one-action flow that completes two Fouduglen hunts: buy missing map fragments, assemble the map, travel and fight Fouduglen, then repeat. They invited suggestions for omitted steps. This is a requested scope, not evidence that implementation or live tests are complete.

Operational requirements inferred from the request and discussed in the session:
- Reuse suitable fragments already owned; buy only missing quantities and verify the selected market items before purchasing.
- Make assembly, travel, hunt progression and the boss fight part of the same guided/automated flow, with visible progress and recoverable stop reasons.
- Do not abandon an in-progress legendary hunt; count a run only after server-confirmed victory.
- Record costs, rewards and evidence for both runs. Establish an explicit spending cap before purchases if the user has not supplied one.
- Existing hunting APIs did not include a discovered legendary assembly/launch flow in the inspected code at the time; implementation remained outstanding.
