# Bank/trade interaction keyboard accessibility

On 2026-11-05, compact-window QA reproduced clipped kamas fields in four bank/trade interaction paths. The fix adds vertical scrolling within the interaction window without enlarging the map. Independent keyboard-path rerun passed 22/22; the integrated UI suite passed 136/136 (exit code 0). This validates keyboard access and tested geometry, not visual appearance, shop, or teleport overlays. Evidence was recorded in `docs/validation/bots-open-interaction-keyboard-20261105.md`.
