# Bots map tab: keyboard reach of an OPEN interaction window at 720x640 (2026-11-05)

## Scenario

`BotsKeyboardAccessTests.Tab_reaches_every_open_interaction_field_inside_the_map`
(4 cases: bank and trade, forward and Shift+Tab). The window is 720x640, the map tab is
index 5, and the overlay is the real `InteractionWindowViewModel` border in `BotsView.axaml`.

## Fixture: real state publication

- Uses a synthetic, never-launched PID `2000000011` on the shared `Stores.Launch`, which
  is cleaned up with `launch.Forget(pid)` in `finally`.
- Calls the production `LaunchService.BeginRoleplayMap(pid, 191104002)` (private, via
  reflection). It writes `_lastMap`, which is the map that `StorageOpen` and `TradeOpen`
  compare against.
- Seeds `Inventory` with 12 bag stacks through the public `Inventory.Seed`.
- Calls the private production decoder entry `OnDecoded` with a server-to-client
  `StorageInventoryContentEvent` or `ExchangeStartedWithPodsEvent`. That runs
  `ApplyStorageContent` or `ApplyTradeStarted`, which write `_storageOpenMap` or
  `_tradeOpen` for the current map. The test asserts that `CurrentExchange(pid)` is Bank
  or Trade before any UI work.
- Calls `bot.Interaction.Poll()` once, explicitly. Poll reads `CurrentExchange` and the
  stores, so `Kind` and `IsVisible` come from the published state.
- No `IsVisible` is overridden and no command is executed.
- Poll timing: the offline `BotsViewModel` is created without running its constructor,
  so the 300 ms `_timer`, whose `Tick` is the only production caller of
  `Interaction.Poll()`, never exists. No other Poll can run during the test. A later
  Poll would read the same published state anyway, because the PID is nonzero.
- There are no sleeps. Each Tab press is followed by `Dispatcher.UIThread.RunJobs()` and
  `UpdateLayout()`.

## Assertion

For every enabled, visible, focusable `TextBox` in the overlay (filter, quantity
`NumericUpDown`, kamas `NumericUpDown`), Tab or Shift+Tab must reach the field. After each
focus, the field's window-space rectangle must lie inside both of these:

- `MapTabBorder` intersected with the window client area, recomputed for every field,
  because outer ScrollViewers move the map when focus changes.
- The intersection of every `ClipToBounds` visual ancestor.

## Evidence (artifacts `%USERPROFILE%/AppData/Local/Temp/givre-open-interaction-20261105*`)

1. `-red.log` is invalid as evidence. The map rectangle was captured once, before the
   first focus. Result: 2 passed, 2 failed (reverse cases, field at 447,240 against a
   stale map rect at 339,495). This was a false red from a moved origin.
2. `-run2.log` is the decisive red. Bounds were recomputed per field, with production
   unchanged. Result: 4 of 4 failed. In every case the failing field is the kamas
   `NumericUpDown` text box: bank at y=599, trade at y=563, height 36. The map at that
   moment is 339,176 381x360, so its bottom edge is 536. The clip intersection is empty,
   so the field is physically clipped. The cause is that the overlay
   (`VerticalAlignment=Bottom`, about 338 px tall content plus margins) has no scroller of
   its own. Its kamas row sits below the bottom edge of the fixed-height map border, and
   no ancestor ScrollViewer can bring it into view.
3. `-run3.log` (`open-interaction-run3.trx`) tried a rejected variant: a star-width Grid
   column with MaxWidth 500 plus an inner ScrollViewer. Result: 4 of 4 failed. The field
   measured 738 px wide, wider than the overlay, so the outer horizontal reveal scrolled
   the map off-screen. That variant was replaced.
4. `-run4.log` (`open-interaction-run4.trx`) is green. Result: 4 passed, 0 failed, EXIT=0.
5. Full UI suite (`-full.log`, `GivreClone.UiTests/TestResults/open-interaction-full.trx`):
   136 total, 136 passed (the earlier 132 plus these 4), EXIT=0, duration 4.38 min.

## Production change (`GivreClone.App/Views/BotsView.axaml`, overlay only)

Inside the existing interaction `Border`, the content `StackPanel` is now wrapped in
`<ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">`.
The outer `Border` stays `Width="500"`, bottom-left, inside the map `Grid`, so the map
border's fixed height bounds the overlay and its content scrolls on its own. The
existing two-axis `RevealFocusedField` in `BotsView.Focus.cs`, which was not modified,
scrolls this new ScrollViewer to the focused field. The map height is unchanged, and no
assertion was removed or loosened.

## Parent audit

The parent read the changed overlay XAML, complete new theory, and this
document. The real state publication and cleanup were checked against
InteractionWindowViewModel.Poll and the synthetic PID contract. The initial
stale rectangle finding was rejected; the parent read the decisive run2
failures showing the kamas field outside all visible clipping bounds.
The actual full log was read and reports 136 passes and EXIT=0. Independent
execution of BotsKeyboardAccessTests completed as
mon_GB3BJR9RHKK4EPQT / bash_81 with exit code 0: 22 passed, zero failed,
zero skipped, duration 1 min 10 s. This accepts the bounded offline keyboard
fix, not the remaining visual or other interaction scenarios.

## Remaining visual and scenario limits

- PNG captures `bots-map-{bank,trade}-720x640-keyboard-{forward,reverse}.png` under
  `.../bin/GivreClone.UiTests/debug/TestResults` were written but not inspected by eye.
  The image viewer was unavailable in this session. There is no visual PASS.
- Only the shop and teleport overlays share the new scroller. Their keyboard paths were
  not exercised.
- No live account, client or network was used.
