# Bots compact-panel keyboard traversal, vertical focus (2026-11-05)

## Defect

`BotsKeyboardAccessTests.Tab_can_reach_every_enabled_field_in_the_selected_compact_panel`
had already been strengthened to assert that the initial field and every reached field
lie inside the 720x640 window on both axes. After `RevealFocusedField` in
`BotsView.Focus.cs` was extended to both axes, 2 of 18 cases still failed: tab index 5
(map tab), forward and reverse.

## Root cause: invalid fixture, not production geometry

The failing field was `PART_TextBox` of the kamas `NumericUpDown`. It sits inside the
map-overlay interaction window (`Border DataContext="{Binding SelectedBot.Interaction}"
IsVisible="{Binding IsVisible}"`). In that case it was at Y=678 with height 36, inside
`MapTabBorder`/`MapTabGrid` (fixed height from `FitMapTab`, viewport 330x372, extent
782x372). No ScrollViewer could bring it into view.

The ancestor diagnostic added to the failure message (`dc=null` on every ancestor)
showed that the test created `new BotsView()` without a DataContext. With null
bindings, `IsVisible="{Binding SelectedBot, Converter=IsNotNull}"` and
`IsVisible="{Binding IsVisible}"` cannot resolve, so the controls keep their default
`IsVisible=true`. That exposed the closed interaction window, which grew past the
fixed map height. In production BotsView is always bound to a `BotsViewModel`, and the
detail/tab area itself is gated by `IsVisible="{Binding HasSelection}"`. The overlay is
visible only when `InteractionWindowViewModel.IsVisible` (an open exchange or a pending
request) is true.

Production XAML and layout were not changed.

## Fix (test fixture only)

`GivreClone.UiTests/BotsKeyboardAccessTests.cs`:

- Binds an offline `BotsViewModel`, built the same way as
  `AccountsFilterTests.CreateViewModel`: an uninitialized object with its
  ObservableCollection fields populated. It runs no constructor, so no services,
  timers, packets or launches.
- Selects a real `BotRowViewModel` built from an `AccountRow` (pid 0, no live
  account). The test writes the generated backing field directly, so
  `OnSelectedBotChanged`, which touches services, is not invoked.
- Asserts `vm.SelectedBot` is that bot and that `bot.Interaction.IsVisible` is false.
- Leaves all existing on-screen assertions in place. The offscreen failure message now
  also lists each ancestor's `IsVisible` and DataContext type.

The parent's two-axis `RevealFocusedField` change in `BotsView.Focus.cs` is kept as is.

## Evidence

Artifacts: `%USERPROFILE%/AppData/Local/Temp/givre-ui-audit-20261105` (+ `-red.log`,
`-green1.log`, `-green2.log`, `-full.log`).

1. Red, null DataContext: tab 5 forward and reverse fail with `Offscreen PART_TextBox
   Y=678, height=36`, every ancestor `dc=null`.
2. Intermediate, VM bound but nothing selected: all 8 compact-panel cases fail at
   `Assert.NotEmpty` (tabs hidden by `HasSelection`). This confirms the fixture must
   select a bot.
3. Green: `dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj --artifacts-path
   %USERPROFILE%/AppData/Local/Temp/givre-ui-audit-20261105 --filter
   "FullyQualifiedName~BotsKeyboardAccessTests" --verbosity minimal
   -p:BuildInParallel=false -m:1 --tl:off` passes 18/18 with 0 failed and 0 skipped.
4. Full UI suite: the parent read the actual
   %USERPROFILE%/AppData/Local/Temp/givre-ui-audit-20261105-full.log. It reports
   132 passed, zero failed, zero skipped, duration 3 min 24 s. This run used
   the corrected selected-bot fixture and the two-axis focus implementation.

## Parent countercheck in progress

The original six failing cases used a null view model, so they do not establish
that production needs the vertical offset change with a valid selected bot.
The child's proposed shell-copy baseline was cancelled before mutation. The
parent instead used apply_patch to set only dy to zero and started the same
18 keyboard cases as mon_271VKXR7ANP94989 / bash_78. This exited 1 with
two failures and sixteen passes, zero skipped, duration 37 seconds. Both
tabIndex3 cases failed the initial-field Y assertion: actual 863, allowed
0..604. Unlike the original null-model failures, this controlled result uses
the valid selected-bot fixture and proves the vertical offset is necessary.
The parent restored the exact two-axis expression through apply_patch; no
experimental dy-zero source remains. Confirmation of the restored source is
completed as mon_WYPQTT6P67BMPBD0 / bash_79: 18 passed, zero failed or
skipped, reported test duration 31 seconds. The earlier 132-case full run used
this same restored production expression and fixture. This establishes the
bounded red/green keyboard regression with a valid offline model; it does not
close full-shell visual QA or the open-interaction-window scenario.

## Not validated

- The PNG captures (`bots-tab-{3..6}-720x640-keyboard-*-cycle.png` under the artifacts
  `TestResults`) were not inspected by eye in this pass.
- The keyboard path with an open interaction window (bank/trade) was not exercised. In
  that state the overlay can still exceed the fixed `MapTabBorder` height on a
  640px-tall window, and its kamas row may not be reachable on screen. This is a real
  layout question for a separate change.
