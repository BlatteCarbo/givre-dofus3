# Closed language/confirmation keyboard investigation - 2026-11-05

## Current evidence and scope

The parent subsequently supplied native keyboard evidence from PID 21408,
window 5375934: a 30-step Tab trace reached closed confirmation buttons
Annuler/Confirmer, then closed language buttons Francais/English/Espanol after
onboarding and quick navigation had closed. This establishes keyboard traversal,
not merely accessibility-tree exposure. The trace also reached invisible
Shield/account/proxy fields. Those other modal defects are outside this unit and
must not be claimed solved by the language/confirmation gate.

The parent authorized a narrowed production-declaration/style headless regression
without constructing MainWindow. It loads the existing TestApp production
Controls styles. MSBuild copies the actual MainWindow.axaml to
`TestAssets/MainWindow.axaml` beside the test binary, including when
`--artifacts-path` points outside the repository. The test selects the named
Grid's attributes, omits its production descendants, and passes those attributes
unchanged to AvaloniaRuntimeXamlLoader. Avalonia interprets the real binding;
there are no binding-spelling assertions or handwritten binding parser.
Synthetic buttons exercise this gate with real Tab, Shift+Tab and Space events.
This is not full-shell handler, startup, native accessibility or visual proof. The parent owns native handler
verification of the rebuilt application; no native action is performed here.

## Earlier investigation result (superseded by native evidence)

The initial read-only pass stopped without a production change or test because
native accessibility exposure alone did not prove keyboard traversal and the
existing headless fixture could not safely construct/show MainWindow. The parent
then supplied the native Tab evidence above and explicitly authorized the narrow
style/gate fixture, superseding that stop. MainWindow startup remains excluded.

## References read

- `GivreClone.UiTests/TestApp.axaml` and `TestApp.axaml.cs`.
- `GivreClone.UiTests/NativeThemeTests.cs` and `SettingsKeyboardAccessTests.cs`.
- `docs/codex/HEADLESS_SETUP_20261030.md`.
- `docs/codex/DEVTOOLS_USAGE_20261030.md`.
- `docs/FLAKY_TESTS_20261024.md`.
- `docs/validation/bots-open-interaction-keyboard-20261105.md`.

The fixture loads production Controls.axaml and themes, but explicitly avoids
production startup, MainViewModel, account/configuration stores and transport.
Existing tests use actual compiled child views, real headless key events,
dispatcher jobs and bounded navigation, without sleeps.

## Why MainWindow is not a safe modal-only fixture

- `MainWindow.axaml.cs:45-46`: construction restores configuration-backed window
  size and installs a Closing handler that persists configuration.
- `MainWindow.axaml.cs:68`: construction calls
  `ItemCraftAccessService.AttachLiveStock(Stores.Launch)`.
- `Services/Stores.cs`: static initialization creates persisted account/proxy
  stores, LaunchService, world/statistics stores, private-message service and
  resource providers. This is not a modal-only object graph.
- `Services/LaunchService.cs:241-253`: the constructor creates local launch
  authority, launcher and Zaap objects and subscribes to mapping state. The local
  authority path allocates loopback ports; it does not start the main relay here.
- `MainWindow.axaml.cs:95-112`: the Opened handler starts partner-catalogue and
  professions loaders.
- `MainWindow.axaml.cs:234-334`: opening starts startup/animation timers, reads
  configuration, and may start map prewarming through the attached view model.
- `MainWindow.axaml.cs:1081-1086`: closing saves window state through AppConfig.

The shared style boundary alone can demonstrate general Avalonia modal-card
behavior, but a synthetic host cannot prove MainWindow's language/confirmation
closure handlers, focus state or activation effects. Copying their markup or
using an uninitialized production Window would not supply that missing proof.
No production test-only seam was introduced.

## Minimal production change

Before the fix, `MainWindow.axaml:558` and `:591` declared confirmation/language
grids with IsHitTestVisible false but without IsVisible or IsEnabled gates. Closing handlers
at `MainWindow.axaml.cs:519-524` and `:537-546` remove modalOpen and clear hit
testing. `Styles/Controls.axaml:860-887` implements the visual closure with
opacity/scale transitions, without a keyboard gate. The language click handler
does not reject activation while closed.

The only production change adds this self-binding to those two Grid declarations:

```xml
IsEnabled="{Binding IsHitTestVisible, RelativeSource={RelativeSource Self}}"
```

The existing open/close handlers already toggle IsHitTestVisible, so enabled
state follows that same synchronous gate. Disabled descendants cannot receive
keyboard focus or activate through normal keyboard input. Opacity/transform
transitions, visibility, handlers and the shared modal styles are unchanged.
Reopening restores enabled state through the same existing property change.

## Execution and artifacts

The initial regression ran before any production edit:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~ClosedModalKeyboardTests --results-directory Temp/closed-modal-keyboard-20261105/red --logger "trx;LogFileName=closed-modal-red.trx" --logger "console;verbosity=normal"
```

`Temp/closed-modal-keyboard-20261105/red/closed-modal-red.trx` records four
executed, zero passed, four failed, all at the initial closed Tab destination
assertion. This initial fixture used repository ancestor discovery and a manual
binding parser. Both were replaced after parent review; its result is historical
evidence, not proof of the final portable fixture.

Loader restore: `dotnet restore GivreClone.UiTests/GivreClone.UiTests.csproj
-m:1 --disable-parallel -v minimal`, monitor `mon_8CMJ5NXRBS0V2RXQ`, exit 0.

The corrected runtime-loader fixture was run against unchanged production:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~ClosedModalKeyboardTests --results-directory Temp/closed-modal-keyboard-20261105/portable-red --logger "trx;LogFileName=closed-modal-portable-red.trx" --logger "console;verbosity=normal" --tl:off
```

Monitor `mon_13KNZV89FFY3324H`, exit 1. Artifact
`Temp/closed-modal-keyboard-20261105/portable-red/closed-modal-portable-red.trx`:
4 executed, 0 passed, 4 failed at the initial closed Tab destination assertion.
This is the decisive red for the corrected fixture, before the production edit.

After the two self-bindings were added:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~ClosedModalKeyboardTests --results-directory Temp/closed-modal-keyboard-20261105/green --logger "trx;LogFileName=closed-modal-green.trx" --logger "console;verbosity=normal" --tl:off
```

Monitor `mon_XFFZVYSXES57878N`, exit 0. Artifact
`Temp/closed-modal-keyboard-20261105/green/closed-modal-green.trx`:
4 executed, 4 passed, 0 failed. Production XAML compiled successfully in this run.
The build reported existing CS8826 partial-method parameter-name differences in
HdvStatsViewModel (OnTotalSalesChanged/OnTotalTaxesChanged) and HdvViewModel
(OnBulkTotalChanged); no warnings were suppressed or unrelated source edited.

The regression's remaining assertions cover closed direct-focus refusal,
forward/reverse open traversal and Space activation, focus already inside when
closing, attempted Space after close without another Tab, closed traversal after
close, and usable reopening. It also requires production card transitions to
remain present. Dispatcher jobs, not sleeps, advance property/input processing.

No git or native-app operation was run; the parent's PID 21408 was not touched.
Parent native verification of the actual close handlers remains a separate step.

## Independent parent build and regression

The parent read the complete portable runtime-XAML fixture, copied-asset project
configuration and both actual production Grid gates. Fresh LSP diagnostics timed
out and are not reported clean. The isolated Release compile/test completed
under mon_W3KBR78CK4T1ZKZ4 / bash_88 with exit 0: six passed, zero failed or
skipped, duration 19 seconds. The filter was
FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests.
Artifacts were built outside the checkout at
%USERPROFILE%/AppData/Local/Temp/givre-parent-closed-modal-20261105.
The parent has begun native verification using that freshly built application;
Alt+F4 was sent to the previous test-only window before relaunch, but a later
window enumeration showed it remained open; closure was not confirmed.
The corrected build opened as PID 25572 / window 3213488, alongside the older
PID 21408 / window 5375934. A 28-Tab attempt addressed the corrected window,
but focusedElement reported only the window each time. Because actual input
delivery/focus was not established, this is not native pass or failure evidence.
The six passing headless cases remain the accepted bounded evidence; actual
shell verification remains open. No account operation was performed.

### Native follow-up rejected production acceptance

After observing the corrected window again, the parent explicitly focused its
quick-search button through AX before sending Tab. This established actual key
delivery, unlike the earlier attempt. The 25-step trace traversed chrome,
navigation and dashboard controls, then reached closed Annuler, Confirmer,
Francais, English, Espanol and a Shield text field. Thus the actual compiled
shell still exhibits the defect. No hidden action was activated.

The isolated six-case green proves only the extracted runtime-loaded Grid
binding, not effective gating in compiled MainWindow. Production acceptance is
withdrawn pending root-cause correction. The same child was instructed to pause
the adjacent account unit and resolve this discrepancy first; no inferred cause
is established yet. Both native windows remain test-only with no connected
accounts.

Correction to that interpretation: an independent Get-CimInstance probe of
ProcessId 25572 and 21408 returned the old executable path for BOTH processes:
%USERPROFILE%/AppData/Local/Temp/givre-parent-observed-grade-20261105/bin/GivreClone.App/debug/GivreClone.exe.
Thus the window attributed to the corrected Release build was actually running
the older Debug executable. The native trace remains genuine evidence of the
old defect but cannot reject the new binding. The redirection/relaunch cause
is not established. The parent informed the child immediately and started a
fresh-profile attempt; compiled-context checks and real-binary identification
remain prerequisites to any further production change.

### Correct Release binary: native bounded acceptance

Get-Process.Path identified PID 3160 as the independent corrected Release at
%USERPROFILE%/AppData/Local/Temp/givre-parent-closed-modal-20261105/bin/GivreClone.App/release/GivreClone.exe.
Native window enumeration mapped it to window 6031636. Before language choice,
AX reported the closed confirmation descendants disabled and the language
buttons enabled. The parent selected French, explicitly focused quick search,
and sent 24 Tabs, reading actual focusedElement after every key. The trace
traversed chrome/sidebar/dashboard controls, then went directly to the Shield
field and its buttons, followed by account fields. It did NOT traverse the
closed language or main confirmation controls, unlike the old Debug trace.

This accepts the two targeted gates in actual compiled production use and
confirms usable language selection. It does not prove reverse full-shell
traversal, reopening the main confirmation, visual appearance or the other
closed modals. Shield/account closure remains a reproduced adjacent defect.
No network, account-save or hidden confirmation action was invoked. The child
was told to retain the two working gates and resume the adjacent bounded fix.

### Isolated-output handoff

The child had already started the following fresh output build under monitor
`mon_4E7SD685303QTHBE` before learning that the parent independently started
isolated verification:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release -m:1 --artifacts-path %USERPROFILE%/AppData/Local/Temp/givre-closed-modal-child-20261105 --filter FullyQualifiedName~ClosedModalKeyboardTests --results-directory %USERPROFILE%/AppData/Local/Temp/givre-closed-modal-child-20261105/results --logger "trx;LogFileName=closed-modal-isolated.trx" --logger "console;verbosity=normal" --tl:off
```

The child stopped that redundant build (`bash_5`) at the parent's instruction to
leave isolated verification to the parent. No isolated TRX existed at that point;
there is no child isolated-output passing claim. Parent monitor
`mon_W3KBR78CK4T1ZKZ4` / `bash88` covers ClosedModalKeyboardTests and
SettingsKeyboardAccessTests from an independent isolated Release build. Its
result and actual native-handler follow-up are owned by the parent and were
pending at this handoff.

The parent also located hit-test-only ShieldVisible/EditVisible grids in
AccountOverlays.axaml. They remain unchanged in this unit, along with the other
broader hidden-modal observations.

## Changed source paths

Reverse native check remains inconclusive: background shift+tab delivery on
window3409182 produced forward-looking header movement, while focusedElement
sometimes reported an unrelated textarea unlike the window AX focus. A longer
probe timed out. No application change was made from this ambiguous evidence.
Screenshot pixels were omitted by the model transport, so this session supplies
no visual/compact acceptance. Headless reverse cases remain the evidence for
the modal units; do not substitute them for the unfinished full native route.

Final parent native shell check: exact parent-reload-ui Release executable,
PID32336/window3409182. After French selection, 24 Tabs from quick search
traversed only shell/dashboard plus the genuinely OPEN map-preparation button,
then wrapped. Closed LevelCap buttons were absent. After reading CloseMapExtract
and pressing its real background button (no game/account/order action), a fresh
21-Tab trace traversed shell/dashboard and wrapped directly to quick search:
no closed modal controls remained in this bounded empty-dashboard forward path.
The open extraction action was correctly retained before dismissal and excluded
after dismissal. Other pages, reverse shell traversal, populated state and
compact visual appearance remain outside this native evidence.

Parent full UI suite after reload: mon_T9SW0NHR5WEMHRKC / bash_6,
fresh isolated Release compilation and unfiltered GivreClone.UiTests:
exit 0, 209 passed, zero failed/skipped, duration 6m50s.
Artifacts: %USERPROFILE%/AppData/Local/Temp/givre-parent-reload-ui-20261105;
TRX logger filename reload-ui.trx. This includes the final LevelCap fixture.
No dotnet process remained before this run; the interrupted pre-reload parent
result was not recovered or counted. Headless acceptance does not replace the
remaining native final-shell traversal or compact visual acceptance.

Independent parent map-preparation verification: mon_DAXC6Z4GG50N8947 /
bash_133 completed fresh isolated Release compilation/tests with exit 0:
71 passed, zero failed/skipped, 74 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-map-modal-20261105.
Filter: MapPreparationKeyboardTests plus the eleven accepted modal fixtures.
Parent read full test, actual gate, fixture-red correction and genuine closed
Tab red. Open busy and finished states remain focusable without starting work.
LSP freshness timed out; native follow-up still separate, LevelCap unchanged.

Native notification follow-up: exact parent-notification-r2 Release executable,
PID25428/window2949868, French selected then quick-search focused. Recorded
26 Tabs: shell/dashboard then Continuer en arriere-plan, Fermer, Voir les offres,
then wrap. Closed notification filters/search/detail actions were absent.
Bounded native forward closure accepted. Startup/offer controls remain separate
findings pending visibility attribution; none was activated. This is not a
whole-shell or visual pass.

Independent parent notifications verification: mon_5Z6DNPSR8SMDDPB1 / bash_129
completed fresh isolated Release compilation/tests with exit 0: 69 passed,
zero failed or skipped, duration 62 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-notification-r2-20261105.
Same eleven modal keyboard fixture filter as first parent attempt; new output
directory and UseSharedCompilation=false after the prior MSB3883 copy failure.
No source/assertion change was needed for the retry. LSP reported no errors on
NotificationOverlayKeyboardTests. Native follow-up remains separate.

Native HDV follow-up: matched PID32672/window1575232 to parent-sell-modal
Release. After French selection and quick-search focus, recorded 30 Tabs.
All previously attributed sell refresh/diagnostic/search/filter/select controls
were absent. After dashboard, traversal entered notifications filters/search,
Copier le detail, Fermer, Continuer en arriere-plan, Fermer, Voir les offres,
then wrapped to quick search. This accepts bounded native sell closure, not
whole-shell keyboard acceptance: notification/detail/startup/offer overlays
still require individual visibility attribution. No reached action was activated.

Independent parent HDV sell verification: mon_A051YC9YFNB49GCV / bash_120
completed fresh isolated Release compilation/tests with exit 0: 65 passed,
zero failed or skipped, duration 76 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-sell-modal-20261105.
Filter combines HdvSellKeyboardTests and the nine previously accepted modal
keyboard fixtures (without child SocketPickerUx/Settings extras).
Parent read the actual single root gate, full fixture and behavioral-red record.
No HDV command ran. LSP freshness timed out; native sell follow-up is separate.

Native planning/Lua follow-up: matched PID27012/window2558348 to the
parent-planning-modal Release executable, chose French, focused quick search,
and recorded 25 Tab destinations. After dashboard: Actualiser sac + banque,
Diagnostic UID, Fermer, textfield, two comboboxes, Cocher la liste.
Previously encountered closed planning Close/Connect tooltip and closed Lua
GoTo/Unsaved controls were absent. Bounded forward closure accepted; native
open/reopen remains covered only by compiled fixtures. Remaining sell/inventory
overlay controls were focused only, never activated. No account connection,
inventory refresh, diagnostic or sale command ran.

Independent parent planning verification: mon_N4QKBSYCSD08SNTD / bash_116
completed fresh isolated Release compilation/tests with exit 0: 63 passed,
zero failed or skipped, duration 63 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-planning-modal-20261105.
Filter combines PlanningOverlayKeyboardTests with the eight previously
accepted modal keyboard fixtures. Parent read the actual root gate and full
fixture: closed traversal before/after inner editor disable, all seven open
controls, focused close/reopen, no timer or connect/save command.
LSP freshness timed out; native planning/Lua follow-up remains outstanding.

Independent parent Lua verification: mon_D3VMES4DSVWEJDM3 / bash_115
completed fresh isolated Release compilation/tests with exit 0: 61 passed,
zero failed or skipped, duration 65 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-lua-modal-20261105.
Filter combines LuaOverlayKeyboardTests with CombatSimKeyboardTests,
RoadRecorderKeyboardTests, CombatModalKeyboardTests, SocketPickerKeyboardTests,
ProxyModalKeyboardTests, AccountModalKeyboardTests, ClosedModalKeyboardTests.
Parent read the fixture and narrowed production bindings: only GoTo/Unsaved
need new gates; four already-inner-hidden panels retain behavior checks but
no redundant gate. The outer-enabled assertion was rejected as an
implementation pin, not evidence of eight additional keyboard defects.
LSP freshness timed out; native follow-up and full visual QA remain open.

Native simulator/Road/Ask follow-up: verified parent-sim-modal Release PID19368,
window4458024, after French selection and quick-search focus, traversed 24 Tabs.
After shell/dashboard the sequence was Fermer, Fermer, the offline-planning
launch tooltip, Actualiser sac + banque, Diagnostic UID, Fermer. The old
simulator modes/fields and Road/Ask controls were absent. This accepts bounded
forward native closure of those targeted panels. Planning and sell/editor
overlays remain open findings; no launch, inventory refresh, diagnostic or
hidden confirmation was activated. Actual Enter-on-closed-Ask is covered only
by the accepted compiled regression, not by this native trace.

Independent parent simulator verification: mon_H6B1CNZQX8N1ABH3 / bash_111
completed fresh isolated Release compilation/tests with exit 0: 49 passed,
zero failed or skipped, duration 51 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-sim-modal-20261105.
Filter: FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests.
The parent read the whole compiled simulator fixture and actual root gate;
all modes/fields and close/reopen focus are covered without simulation actions.
LSP freshness timed out. Actual native follow-up remains separate; this does
not accept the still-uncorrected editor/planning panels or visual appearance.

Independent parent Road/Ask verification: mon_T7GDY9VT2PHA46XX / bash_110
completed fresh isolated Release compilation/tests with exit 0: 47 passed,
zero failed or skipped, duration 44 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-road-modal-20261105.
Filter: FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests.
The parent read the whole five-case compiled fixture, including default Enter
activation and its disconnected command, and the two actual enabled bindings.
No real script response, road action or command was invoked. LSP timed out;
native/full-shell verification and simulator/editor/planning gates remain open.

Native combat-panel follow-up: the parent matched executable path, PID 20212,
window 4393160 to the independently built parent-combat-modal Release. After
French selection and quick-search focus, 23 actual Tabs traversed chrome,
sidebar and dashboard, then Fermer, Placer un ennemi, Me placer, Gomme and a
text field. The earlier Essentiel / Placement & zones / spell configuration
sequence was gone. This accepts bounded forward closure of CombatOverlays in
the real shell; it does not validate visual layout or populated import. The
next controls belong to the still-uncorrected simulator scope, not the spell
configuration. No simulator mode, spell/profile or other hidden action was
activated. The current native test window remains separate from live PID14216.

Independent parent combat-panel verification: mon_5NPMPAWC8K4YKSSR / bash_106
completed fresh isolated Release compilation and tests with exit 0: 42 passed,
zero failed or skipped, duration 42 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-combat-modal-20261105.
Filter: FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests.
The parent read the full compiled CombatOverlays fixture and all three enabled
bindings. LSP freshness timed out. This proves bounded compiled-panel behavior,
not native shell/visual acceptance; import is exercised with no source profile.

Parent independent proxy validation: mon_5S4VEABRFBTKZE3B / bash_97 freshly
compiled Release outside the checkout and exited 0: 20 passed, zero failed or
skipped, duration 30 seconds. Filter:
FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests.
Artifacts: %USERPROFILE%/AppData/Local/Temp/givre-parent-proxy-modal-20261105.
The parent read the complete actual compiled proxy fixture and the one-line
EditVisible enabled gate before running. Fresh LSP diagnostics timed out;
compiler/test output, not a clean LSP claim, is the evidence. Native proxy
follow-up and the remaining unidentified Annuler control remain open.

- `GivreClone.App/Views/MainWindow.axaml`: two self-bindings only.
- `GivreClone.UiTests/ClosedModalKeyboardTests.cs`: four focused behavioral cases.
- `GivreClone.UiTests/GivreClone.UiTests.csproj`: production XAML content link
  and matching 12.1.1 runtime XAML loader package for the test fixture.
- `docs/validation/closed-modal-keyboard-20261105.md`: this evidence record.

No MainWindow code-behind or Controls.axaml change was needed. No other modal
was changed. The broader hidden Shield/account/proxy traversal remains open.

## Native proxy follow-up

After normal closure of the parent-owned QA windows, Get-Process confirmed only
the historical live PID 14216 remained; it was not closed. The parent launched
the proxy-corrected Release with run name parent-proxy-native-20261105 and
independently matched executable path, PID 7248 and window 6817850. After French
selection and explicit quick-search focus, 21 actual Tabs traversed chrome,
sidebar and dashboard controls, then Annuler, Fermer and Essentiel. No closed
proxy text fields, protocol selector or save button were traversed. This accepts
bounded native forward exclusion for the proxy correction, not all modals.
The remaining Annuler is being investigated in SocketServerPickOverlay; the
subsequent Fermer/Essentiel belong to another uncorrected scope. No hidden
button was activated. No account was connected.

## Adjacent Shield/account-edit unit

After accepting the independent isolated 6/6 language/confirmation and Settings
results, the parent authorized a separate unit for ONLY Shield and account edit.
The earlier native trace is the motivating observation. Proxy and launch-modal
behavior are not part of this unit. All preceding parent evidence is preserved.

`AccountOverlays` construction is startup-free: its code-behind constructor only
calls InitializeComponent. The new `AccountModalKeyboardTests` loads the actual
compiled view with the existing `AccountsFilterTests.CreateViewModel()` fixture.
That fixture bypasses the AccountsViewModel constructor, initializes its
ObservableCollections and account filter bookkeeping, and starts no services or
timers. ShieldVisible/EditVisible are observable state properties without
service side effects. The test changes these properties directly, not commands.

Eight cases cover Shield/account-edit, forward/reverse traversal, and closure
while either a text field or button owns focus. The real production text fields
and buttons must be reachable while open; headless text input must reach the
first field and update ShieldCode/EditLogin. Closing must remove descendant
focus and reject direct focus as well as both Tab directions. Reopening must
allow every tested text field and button to focus, and the production modalCard
must retain transitions. No Space/Enter is sent to production buttons: Shield,
save, cancellation, network and persistence commands are never activated.
There are no sleeps or test-only production seams. This is compiled overlay and
binding behavior, not native/full-shell startup or visual proof.

### Parent executable-path correction during adjacent-unit work

The parent temporarily paused account production edits after observing closed
controls in what was thought to be the corrected Release process. A later
Get-CimInstance check established that both PID 25572 and PID 21408 actually ran
the older `Temp/givre-parent-observed-grade-20261105/bin/GivreClone.App/debug/`
executable. The parent withdrew that trace as evidence against the corrected
gate. The old trace remains evidence for the original defect, not failure of
the new Release assembly. Native follow-up now requires confirming executable
path as well as window identity. No child native-window action was performed.

The standalone language/confirmation diagnostic was strengthened to retain
MainWindow's inherited x:DataType and compiled-binding mode; this remains a
fragment test, not actual full-shell startup proof. The first diagnostic failed
because the temporary host still owned the Grid (fixture parenting error), not
because of binding behavior. The corrected diagnostic detaches that child before
hosting it. No binding production change was made on the withdrawn native claim.

Read-only startup checks found no direct Process.Start/restart path in Program.cs
or App.axaml.cs. This is not an exhaustive updater investigation.

The corrected compiled-context diagnostic completed with 4 executed, 4 passed,
0 failed, exit 0 (`mon_HB9Y1Y5RJG8S3SP3`):

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~ClosedModalKeyboardTests --results-directory Temp/closed-modal-keyboard-20261105/compiled-context-fixed-host --logger "trx;LogFileName=compiled-context-fixed-host.trx" --logger "console;verbosity=normal" --tl:off
```

Artifact: `Temp/closed-modal-keyboard-20261105/compiled-context-fixed-host/compiled-context-fixed-host.trx`.
This settles the fragment's inherited compiled-binding context, not full-shell
native behavior. Parent App assembly mtime was 2026-11-05 13:56:38 UTC, newer than
the MainWindow source edit at 13:53:46 UTC; that timestamp check alone could not
establish which executable owned the native windows, as the parent later proved.

The first account-overlay run is not accepted as a complete red: all 8 cases
failed, but 4 account-edit cases hit a fixture count error because a templated
button was included. The fixture now selects the two command-bound buttons.
It removes unrelated sibling modal grids from the loaded view so the actual
compiled target is tested without sibling keyboard defects masking its result.
No production command is invoked, and no target markup is copied.

### Account unit decisive red and minimal gate

With both production account gates still unchanged, the corrected compiled
fixture ran:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~AccountModalKeyboardTests --results-directory Temp/account-modal-keyboard-20261105/target-red --logger "trx;LogFileName=account-modal-target-red.trx" --logger "console;verbosity=normal" --tl:off
```

Monitor `mon_CZ1REYBZRW7BGW5D`, exit 1. Artifact
`Temp/account-modal-keyboard-20261105/target-red/account-modal-target-red.trx`:
8 executed, 0 passed, 8 failed. All eight failed at the first closed Tab
destination assertion, with an actual closed text field or button focused.

The production edit adds only `IsEnabled="{Binding ShieldVisible}"` to the
Shield grid and `IsEnabled="{Binding EditVisible}"` to the account-edit grid.
The existing modalOpen and IsHitTestVisible bindings are retained unchanged;
visibility state now gates keyboard eligibility as well as pointer input.
Shared opacity/scale animations, command bindings, launch-modal and proxy code
are untouched. MainWindow's two already-accepted gates were not modified.

### Independent parent account verification

The parent read the actual compiled-overlay fixture, including removal of
unrelated sibling modals from the test host, and the two production enabled
bindings. Independent fresh Release build/test completed under
mon_V3VR9EV1PV2X1WDM / bash_94 with exit 0: 24 passed, zero failed or skipped,
duration one minute 19 seconds. Artifacts:
%USERPROFILE%/AppData/Local/Temp/givre-parent-account-modal-20261105.
Filter: FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~AccountsKeyboardAccessTests.
This accepts compiled component keyboard behavior and the neighboring account
regressions. Native verification on this newly built account correction remains
open; the earlier verified Release shell predates the account gates. No Shield
confirmation, account-save or connected account action was used.

### Native account build follow-up

Get-Process.Path and native window enumeration independently matched PID 22828 /
window 2099312 to the parent-account-modal Release executable above. The parent
selected French, focused the real quick-search button, then sent 27 Tabs with
focusedElement read after each key. After chrome/sidebar/dashboard controls,
the next controls were an Annuler button, proxy-name field, protocol combobox,
host/port/user/password fields, Annuler and Enregistrer. The Shield code field
and Shield confirmation pair, account login/label/proxy-selection controls,
and closed language/main confirmation controls were no longer traversed.
This is bounded forward native acceptance of the account closure correction.
The remaining Annuler before proxy was not attributed to a specific modal and
is not claimed fixed. No production command was activated. Proxy controls in
this build predate their separate pending correction.

### Account unit green

After the two account visibility-bound enable gates were added, the combined
focused and related run was:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/account-modal-keyboard-20261105/green --logger "trx;LogFileName=account-modal-green.trx" --logger "console;verbosity=normal" --tl:off
```

Monitor `mon_5QPG9BS8P068HDWA` / `bash_14`, exit 0. Artifact
`Temp/account-modal-keyboard-20261105/green/account-modal-green.trx`:
14 executed, 14 passed, 0 failed, 0 skipped: all eight compiled account-overlay
cases, four strengthened compiled-context language/confirmation fragment cases,
and two Settings keyboard cases. Production AccountOverlays XAML compiled in
this run. Existing CS8826 partial-method signature warnings in HdvStatsViewModel
and HdvViewModel remained; nothing was suppressed or edited to hide them.

This proves the actual compiled Shield/account-edit bindings and descendants
are excluded when closed, text/button keyboard focus remains usable while open,
real text input updates the intended view-model field, closing clears text or
button focus, and reopening restores focus eligibility. It does not prove
native full-shell traversal or visual appearance after the account fix; those
remain the parent's independent follow-up. No network/save command, account
operation, native-window action or git operation was performed by the child.

### Paths changed during the adjacent unit

- `GivreClone.App/Views/AccountOverlays.axaml`: only the two IsEnabled bindings.
- `GivreClone.UiTests/AccountModalKeyboardTests.cs`: new eight-case regression
  on actual compiled account overlays with the existing startup-free fixture.
- `GivreClone.UiTests/ClosedModalKeyboardTests.cs`: retain inherited production
  data type and compiled-binding context, prompted by the now-resolved stale
  native executable discrepancy. No production MainWindow edit was made.
- `docs/validation/closed-modal-keyboard-20261105.md`: append-only evidence for
  this adjacent unit, preserving the parent's prior native and isolated results.

No csproj asset change was necessary for AccountOverlays because the real
compiled control is instantiated directly. Proxy and launch-modal gates remain
unchanged; no broad modal sweep was performed.

## Proxy-edit compiled-overlay unit

After reporting independent account validation (`mon_V3VR9EV1PV2X1WDM` /
`bash94`, exit 0, 24/24), the parent authorized only the previously observed
closed proxy-edit descendants. No MainWindow/account gate is changed in this
unit, and no native window is touched.

`ProxysOverlays` only calls InitializeComponent. Its actual compiled view and
the existing production TestApp styles are used. `ProxysViewModel` construction
is deliberately bypassed: its constructor subscribes to NetworkWatcher,
LicenseService and relay events, enumerates adapters, and reloads Stores.Proxies.
There is no existing proxy-specific startup-free fixture in UiTests. Following
the AccountsFilterTests pattern, the new fixture uses
RuntimeHelpers.GetUninitializedObject and initializes only the overlay's edit
properties and the Protocols auto-property backing field. No persisted proxy
entry, store, relay client, network watcher or constructor subscription is used.

`ProxyModalKeyboardTests` has eight cases: external/adapter edits, forward/reverse
Tab, and close while a text field/button owns focus. It uses the actual compiled
controls, requiring closed traversal and direct-focus exclusion; open traversal
of every visible text field, protocol selector and command button; real text
input updating EditName; focus removal on close; usable reopening; and retained
production modalCard transitions. Adapter mode must expose only the name field.
Save/Cancel/Test commands are never invoked, including through activation keys.
No sleeps, copied overlay markup, production test seam, csproj or dependency
change is introduced. This is bounded compiled-overlay behavior, not native
full-shell or visual acceptance.

### Proxy decisive red

Before any proxy production gate edit:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~ProxyModalKeyboardTests --results-directory Temp/proxy-modal-keyboard-20261105/red --logger "trx;LogFileName=proxy-modal-red.trx" --logger "console;verbosity=normal" --tl:off
```

Monitor `mon_DB6JNFZQ4TFCWKKN` / `bash_15`, exit 1. Artifact
`Temp/proxy-modal-keyboard-20261105/red/proxy-modal-red.trx`: 8 executed,
0 passed, 8 failed at the initial closed Tab destination assertion. Actual focus
was a production proxy text field for forward traversal or button for reverse
traversal, including adapter mode.

The minimal production fix adds only `IsEnabled="{Binding EditVisible}"` beside
the existing proxy-edit modalOpen/IsHitTestVisible bindings. No view-model,
command, animation, MainWindow or account-gate edit is included.

The single related green run was started asynchronously under
`mon_D9DRA5CJXX262Y4C` / `bash_16` with:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/proxy-modal-keyboard-20261105/green --logger "trx;LogFileName=proxy-modal-green.trx" --logger "console;verbosity=normal" --tl:off
```

Definitive result: exit 0; artifact
`Temp/proxy-modal-keyboard-20261105/green/proxy-modal-green.trx` records
22 executed, 22 passed, 0 failed, 0 skipped. This includes all eight actual
compiled proxy-edit cases, eight compiled account-modal cases, four
compiled-context language/confirmation fragment cases and two Settings cases.
The production proxy XAML compiled successfully. Existing CS8826 warnings in
HdvStatsViewModel/HdvViewModel remain; none were suppressed or edited away.

The new eight passing cases prove closed eligibility, open keyboard traversal
and text binding, text/button focus removal on close, and reopening in both
adapter/external modes and traversal directions. Native full-shell traversal,
visual appearance and actual proxy operations are not claimed by this run.
Parent independently inspects/runs and owns native follow-up.

### Proxy unit changed paths

- `GivreClone.App/Views/ProxysOverlays.axaml`: one IsEnabled binding only.
- `GivreClone.UiTests/ProxyModalKeyboardTests.cs`: eight actual compiled-view
  cases, including adapter and external-proxy edit modes.
- `docs/validation/closed-modal-keyboard-20261105.md`: appended proxy evidence;
  all preceding parent native and independent-test records are preserved.

No csproj, dependency, MainWindow, account gate or proxy view-model change was
needed. The view-model constructor was not executed and no command activated.

## Socket server/character picker unit

Native follow-up: Get-Process.Path matched PID 20764, window 1770242, to
the independent parent-socket-picker Release output. With French selected and
quick search explicitly focused, a 21-Tab trace traversed shell/dashboard then
went directly to Fermer, Essentiel and Placement & zones. The formerly preceding
Annuler was absent. This corroborates the picker attribution and accepts its
bounded forward exclusion in the actual shell. The subsequent spell controls
remain a known separate defect being corrected; no hidden command was invoked.

Independent parent build/test: mon_NNF7QNCG8CVMBAGN / bash_102 exited 0 with
38 passed, zero failed or skipped, duration 36 seconds. Fresh isolated Release
artifacts: %USERPROFILE%/AppData/Local/Temp/givre-parent-socket-picker-20261105.
Filter: FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests.
The parent read the entire compiled-view fixture and actual one-line gate.
LSP freshness timed out; no clean diagnostic claim is made. Native follow-up
remains separate and no server/character command was activated by these tests.

The parent accepted the proxy fix natively on verified Release PID 7248/window
6817850. Its Tab trace then reached Annuler followed by closed spell controls
Fermer/Essentiel; those spell controls are explicitly outside this unit.

Source and actual compiled tree identify a picker footer Button whose Command
is CancelServerPickCommand. The regression selects that exact command identity,
not the localized Annuler label. This attributes the tested picker control
without claiming every native Annuler belongs to it.

SocketServerPickOverlay's constructor only loads XAML. The fixture reuses
AccountsFilterTests.CreateViewModel, bypassing account/service startup and
initializing observable collections. Synthetic server/character cards are plain
view-model data; no account, transport, game data or icons are loaded. The test
changes ServerPickVisible and ServerPickStep directly. The latter's callback
only publishes state notifications. It never invokes selection, cancel,
creation, deletion or network commands.

Sixteen actual compiled-view cases cover server, character, creation and
deletion steps; both Tab directions; and close while a field or the identified
cancel button owns focus. They require open text/command-button traversal and
real text binding, closed Tab/direct-focus exclusion, clearing descendant focus
on close, and reopened state/text preservation. Creation mode is exercised
without selecting game-backed breed/sex choices or executing creation.
Production modalCard transitions must remain present. No sleeps, copied markup,
test-only seam, csproj/dependency or unrelated gate change is introduced.

The initial picker run (`Temp/socket-picker-keyboard-20261105/red/socket-picker-red.trx`)
is rejected as regression evidence: 16 cases failed during fixture setup because
setting ServerPickSearch called RebuildPickLists without _pickSource. The test
now leaves search untouched (focus is still tested) and sends text input only
to creation/deletion fields, whose setters do not rebuild connection-backed
lists. No search callback was disabled and no production seam was introduced.

### Picker decisive red and minimal fix

With corrected fixture and production unchanged:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~SocketPickerKeyboardTests --results-directory Temp/socket-picker-keyboard-20261105/target-red --logger "trx;LogFileName=socket-picker-target-red.trx" --logger "console;verbosity=normal" --tl:off
```

Monitor `mon_YW6GATGVBNZR18XT` / `bash_18`, exit 1. Artifact
`Temp/socket-picker-keyboard-20261105/target-red/socket-picker-target-red.trx`:
16 executed, 0 passed, 16 failed at the initial closed Tab destination assertion.

The sole production edit adds `IsEnabled="{Binding ServerPickVisible}"` to the
picker's existing modalOpen/IsHitTestVisible Grid. All step, creation, deletion,
command and animation bindings remain unchanged. No spell-modal control is
edited, and no broad modal sweep is included.

The related green run was submitted once under `mon_51K8JZ03K75GWRYA` / `bash_19`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/socket-picker-keyboard-20261105/green --logger "trx;LogFileName=socket-picker-green.trx" --logger "console;verbosity=normal" --tl:off
```

Definitive green: exit 0; artifact
`Temp/socket-picker-keyboard-20261105/green/socket-picker-green.trx` records
40 executed, 40 passed, 0 failed, 0 skipped. This includes 16 new picker state
cases, 2 existing SocketPickerUxTests (named deletion confirmation and offline
character selection/window-state behavior), and 22 prior modal keyboard cases.
No duplicate green command was started.

The new fixture path is `GivreClone.UiTests/SocketPickerKeyboardTests.cs`,
using the existing `AccountsFilterTests.CreateViewModel()` startup-free fixture
and actual compiled `SocketServerPickOverlay`. No csproj asset copy or runtime
fragment loader is used for this overlay. The production XAML compiled in the
passing run. All four steps preserve their existing state across close/reopen;
the creation/deletion text bindings remain usable. This does not prove populated
game-backed creation options or execute creation/deletion/selection commands in
the new keyboard fixture. The older UX tests' offline commands are their
pre-existing bounded contract, not live account/network actions.

Changed paths for this unit:
- `GivreClone.App/Views/SocketServerPickOverlay.axaml`: one IsEnabled binding.
- `GivreClone.UiTests/SocketPickerKeyboardTests.cs`: focused compiled regression.
- `docs/validation/closed-modal-keyboard-20261105.md`: appended evidence only.

No MainWindow/account/proxy/spell gate, dependency, native window or git state was
changed by this unit. C# language-server diagnostics are unavailable in this
environment; successful Release compilation and the TRX are the compiler/test
evidence. Native full-shell attribution/acceptance and visual appearance remain
the parent's independent follow-up; Fermer/Essentiel are still out of scope.

## CombatOverlays three-panel unit

The parent subsequently authorized only the spell configuration, challenge list
and combat import panels in this one component, motivated by native traversal
of closed Fermer/Essentiel. All preceding accepted gates remain untouched.

CombatOverlays only loads XAML in its constructor. Its scrim handlers execute
commands, so the test never clicks scrims or activates buttons. The offline
Bots fixture follows BotsKeyboardAccessTests: bypass constructor and initialize
ObservableCollection fields, with no bot, timers, stores or transport startup.
SpellConfigVisible, ChallengeListVisible and CombatImportVisible have no setter
side effects. A plain in-memory SpellRowViewModel and ChallengeChoiceRow count
any data edits, which must remain zero. The bound spell properties were checked
not to access its store-backed variant getters. No live profile is attached.

CombatImportFrom and merge/replace changes rebuild from CombatProfileStore, so
the fixture preserves the real no-source import state instead of faking a
profile-backed source. Import header/cancel controls are usable there; source
selection, populated import contents and confirmation are not claimed tested.

Six cases (three panels by two directions) use actual compiled controls, removing
unrelated sibling grids only from the test host to isolate each gate. They test
closed traversal and focus refusal, open visible content/command-button reach,
closure while a content control or button owns focus, reopening and retained
modalCard transitions. Spell content includes NumericUpDown text fields and the
Essentiel radio; challenge content includes a populated checkbox. No activation
keys or data edits are sent. Both closure owners are checked within each case
rather than multiplying a large matrix. No sleep or production test seam exists.

### Combat decisive red and minimal gates

Before combat production edits:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~CombatModalKeyboardTests --results-directory Temp/combat-modal-keyboard-20261105/red --logger "trx;LogFileName=combat-modal-red.trx" --logger "console;verbosity=normal" --tl:off
```

Monitor `mon_22TQ5W53X8XN21Z7` / `bash_20`, exit 1. Artifact
`Temp/combat-modal-keyboard-20261105/red/combat-modal-red.trx`: 6 executed,
0 passed, 6 failed at the initial closed Tab destination assertion. Every panel
failed in both directions with an actual closed command button focused.

The only production edits add IsEnabled bindings to SpellConfigVisible,
ChallengeListVisible and CombatImportVisible beside their existing hit-test
bindings. Logic, profile, save, commands and opacity/scale styles are unchanged.

The single related green command, monitor `mon_9DNSVBBWCGNK5RJX` / `bash_21`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/combat-modal-keyboard-20261105/green --logger "trx;LogFileName=combat-modal-green.trx" --logger "console;verbosity=normal" --tl:off
```

### Combat definitive result and bounded handoff

The existing green run completed with **exit 0: 46 executed, 46 passed,
0 failed, 0 skipped**. No duplicate run was started. Artifact:
`Temp/combat-modal-keyboard-20261105/green/combat-modal-green.trx`.
Class totals are CombatModalKeyboardTests 6, SocketPickerKeyboardTests 16,
SocketPickerUxTests 2, ProxyModalKeyboardTests 8, AccountModalKeyboardTests 8,
ClosedModalKeyboardTests 4 and SettingsKeyboardAccessTests 2, all passed.

The actual test method is
`CombatModalKeyboardTests.Compiled_combat_panel_excludes_closed_controls_and_reopens`.
These are the six cases in both TRX artifacts, not inferred coverage:

| panelIndex | Panel | reverse | Pre-gate red | Post-gate green |
| --- | --- | --- | --- | --- |
| 0 | Spell configuration | False | Failed | Passed |
| 0 | Spell configuration | True | Failed | Passed |
| 1 | Challenge list | False | Failed | Passed |
| 1 | Challenge list | True | Failed | Passed |
| 2 | Combat import, no source | False | Failed | Passed |
| 2 | Combat import, no source | True | Failed | Passed |

Only these paths are claimed for this combat unit:

- `GivreClone.App/Views/CombatOverlays.axaml`: three visibility-bound enable
  gates at lines 10-11, 231-232 and 309-310.
- `GivreClone.UiTests/CombatModalKeyboardTests.cs`: the six-case actual compiled
  regression, with closed/open traversal, focus removal/refusal and reopening.
- `docs/validation/closed-modal-keyboard-20261105.md`: appended combat evidence.

Retained transitions are asserted present, not visually timed. No native UI,
profile loading, network, spell save, challenge reset or import command ran.
Populated import remains outside the fixture because its source setter loads
profiles; this result claims only the real empty-source controls for import.
Existing CS8826 partial-method parameter-name warnings in HdvStatsViewModel
and HdvViewModel remain untouched. Successful compilation and the TRX are the
diagnostic evidence; no C# language-server diagnostics were available.
Parent native picker acceptance and prior production units were not changed.
This unit stops here for parent audit; no further modal scope was taken.

## RoadRecorderOverlays bounded two-panel unit

Parent reported independent combat acceptance: 42/42, exit 0,
`mon_5NPMPAWC8K4YKSSR`. This subsequent unit is restricted to MapPlanVisible
and AskVisible in RoadRecorderOverlays, one focused test file and this append.
The view constructor only initializes compiled XAML. The Bots fixture bypasses
the constructor, including the RoadRecorder field initializer that uses Stores,
and initializes collection fields following the existing offline Bots pattern.
Visibility, gather/fight, answer and question-mode setters have no service
effects. No recorder/script entrypoint, command or TaskCompletionSource is used.

Four traversal cases cover both panels in both Tab directions. Each checks
closed skip, all open input/check/command controls, closure from a content
control and a button, refusal of closed direct focus, and reopen usability.
The Ask fixture uses text-input mode. A fifth case exercises actual compiled
IsDefault Enter routing. Before Enter, it verifies command identity then
replaces only the test-host confirm binding with a null-command binding and
counts handled Click events. This preserves the actual default-button key
route without executing AskConfirm or completing a script task. Open and
reopened Enter must produce clicks, preventing a vacuous closed assertion.

Initial monitor `mon_QQ6Z5A3W7MYQQPHG` / `bash_22` exited 1 at test compilation:
the helper named Key shadowed the key enum, and headless calls required physical
key arguments. This was a fixture error, not behavioral red. The helper was
renamed SendKey and explicit Tab/Enter physical keys supplied before retrying.

### Recorder genuine red and two enable gates

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~RoadRecorderKeyboardTests --results-directory Temp/road-recorder-keyboard-20261105/red --logger "trx;LogFileName=road-recorder-red.trx" --logger "console;verbosity=normal" --tl:off
```

Corrected-fixture monitor `mon_22Q4PSVN7P1JTHRC` / `bash_23`: exit 1,
5 executed, 0 passed, 5 failed. Actual artifact:
`Temp/road-recorder-keyboard-20261105/red/road-recorder-red.trx`.
`Compiled_recorder_panel_skips_closed_controls_and_reopens` failed at closed
traversal for all (ask, reverse) pairs: (False, False), (False, True),
(True, False), (True, True). Focus entered a closed checkbox, text field or
button. `Closed_ask_default_Enter_is_inert_but_open_and_reopened_Enter_reaches_confirm`
failed with expected 0 clicks, actual 1 on the initial closed Enter. Its
production command had already been disconnected, so no Ask completion ran.

Only production changes: IsEnabled bound to MapPlanVisible and AskVisible on
their respective panel Grids. Existing hit-test gates, IsDefault, styles,
animations, field bindings and all commands are retained.

Single async related green command, `mon_HPFT8G0D5MYBWB05` / `bash_24`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/road-recorder-keyboard-20261105/green --logger "trx;LogFileName=road-recorder-green.trx" --logger "console;verbosity=normal" --tl:off
```

### Recorder definitive green and parent handoff

The existing related run completed **exit 0: 51 executed, 51 passed, 0 failed,
0 skipped**. No duplicate green run was started. Definitive artifact:
`Temp/road-recorder-keyboard-20261105/green/road-recorder-green.trx`.
The five actual recorder cases match the genuine red artifact above:

| Test in RoadRecorderKeyboardTests | Arguments | Red | Green |
| --- | --- | --- | --- |
| Compiled_recorder_panel_skips_closed_controls_and_reopens | ask: False, reverse: False | Failed | Passed |
| Compiled_recorder_panel_skips_closed_controls_and_reopens | ask: False, reverse: True | Failed | Passed |
| Compiled_recorder_panel_skips_closed_controls_and_reopens | ask: True, reverse: False | Failed | Passed |
| Compiled_recorder_panel_skips_closed_controls_and_reopens | ask: True, reverse: True | Failed | Passed |
| Closed_ask_default_Enter_is_inert_but_open_and_reopened_Enter_reaches_confirm | none | Failed | Passed |

Related passed totals: RoadRecorderKeyboardTests 5, CombatModalKeyboardTests 6,
SocketPickerKeyboardTests 16, SocketPickerUxTests 2, ProxyModalKeyboardTests 8,
AccountModalKeyboardTests 8, ClosedModalKeyboardTests 4 and
SettingsKeyboardAccessTests 2. Both commands above run from the worktree root
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

Claimed changed paths for this unit only:

- `GivreClone.App/Views/RoadRecorderOverlays.axaml`: MapPlan gate at lines 9-10
  and Ask gate at lines 45-46; the default confirm at line 65 remains IsDefault.
- `GivreClone.UiTests/RoadRecorderKeyboardTests.cs`: four traversal cases and
  one bounded default-Enter case on the actual compiled overlay.
- `docs/validation/closed-modal-keyboard-20261105.md`: this appended evidence.

The open MapPlan checks and two command buttons, and open Ask text field and
two command buttons, are traversed in both directions without activation.
Closing with a content control and command button focused clears focus;
closed direct focus fails, and reopening restores it. The Enter test counts
0 clicks closed, 1 open, unchanged at 1 after closing, and 2 after reopening;
AskVisible remains true on open activation because no production command runs.
No real script, task completion, timers, stores, native UI or Road/Ask command
was used. Question-mode alternate labels are not a separate fixture case.
Transitions remain present, not visually timed. Compilation and passing tests
provide diagnostics; no C# language-server diagnostics were available.
Existing unrelated CS8826 warnings remain untouched. No editor/planning,
previous modal production code or parent acceptance evidence was changed.
Parent retains independent source/test audit and native/build verification.
This bounded unit stops with definitive results; no other modal scope taken.

## CombatSimOverlay bounded keyboard unit

Parent reported independent Road/Ask acceptance: 47/47, exit 0,
`mon_T7GDY9VT2PHA46XX`. This next unit only concerns CombatSimVisible in the
compiled CombatSimOverlay after native Release traversal reached its closed
close button, three mode radios and text field. Prior unit files are untouched.

Callback review: Bots.CombatSimVisible has no callback. OpenCombatSim resolves
live spells/profile via Stores and calls Setup, so it is never called. The
standalone CombatSimViewModel constructor creates an inactive DispatcherTimer,
local default board and localized HUD. Only Play starts that timer; Run calls
Play, and MultiTurn/DefaultEnemyHp setters reset state. No such command/setter
is used. Mode changes only notify mode booleans and cursor, so direct Mode
changes safely exercise each selected radio's normal Tab eligibility.

The startup-free Bots fixture initializes collections, supplies a real
standalone simulator through its existing auto-property backing field, and
loads actual compiled CombatSimOverlay. It never calls Setup, Run, Play,
Cell, SetMode or close commands. Two cases cover both directions, each checking
closed skip, all enabled open buttons/checks/text controls plus the selected
radio for each of the three modes, closing with text/button/radio focus,
closed focus refusal and reopening. Six numeric text fields are required.
The inactive timer, no result/playback/log, unchanged AP/MP and retained card
transitions are asserted. No pointer, Enter, Space, data edit or sleep is sent.

Pre-gate command, monitor `mon_D37SXZFHKH6TCRYV` / `bash_25`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~CombatSimKeyboardTests --results-directory Temp/combat-sim-keyboard-20261105/red --logger "trx;LogFileName=combat-sim-red.trx" --logger "console;verbosity=normal" --tl:off
```

Genuine red: **exit 1, 2 executed, 0 passed, 2 failed**, both
`CombatSimKeyboardTests.Compiled_simulator_skips_closed_controls_and_reopens`
cases (`reverse: False` and `reverse: True`) failed the initial closed Tab
destination assertion with a real closed button focused. Artifact:
`Temp/combat-sim-keyboard-20261105/red/combat-sim-red.trx`.
An xUnit2031 warning in the new selected-radio assertion was corrected using
Assert.Single's predicate overload; its condition was not changed.

Only production edit: IsEnabled bound to CombatSimVisible on the root Grid,
beside its existing hit-test binding. Commands, simulator logic and animation
styles remain unchanged. Single async related green command,
`mon_Z9N70PXP24612XAJ` / `bash_26`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/combat-sim-keyboard-20261105/green --logger "trx;LogFileName=combat-sim-green.trx" --logger "console;verbosity=normal" --tl:off
```

Claimed changed paths for this simulator unit only:

- `GivreClone.App/Views/CombatSimOverlay.axaml:8-9`: root visibility enable gate.
- `GivreClone.UiTests/CombatSimKeyboardTests.cs`: two cases of
  `Compiled_simulator_skips_closed_controls_and_reopens(reverse: False/True)`.
- `docs/validation/closed-modal-keyboard-20261105.md`: appended simulator evidence.

Commands run from `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.
Coverage is the initial no-result simulator state, not playback or simulation.
Each radio is reached by Tab while selected through its real property binding;
unselected radios are also checked for closed focus refusal and reopen focus.
No command activation is required to prove keyboard eligibility. Animation
transitions are retained and asserted present, not visually timed. Parent owns
native/build acceptance; prior production files and shared simulator logic
remain outside this unit. Final related-run counters follow on completion.

### Simulator definitive result

Existing monitor `mon_Z9N70PXP24612XAJ` / `bash_26` completed **exit 0:
53 executed, 53 passed, 0 failed, 0 skipped**. No new run was started.
Definitive artifact:
`Temp/combat-sim-keyboard-20261105/green/combat-sim-green.trx`.

| Actual CombatSimKeyboardTests case | Pre-gate red | Post-gate green |
| --- | --- | --- |
| Compiled_simulator_skips_closed_controls_and_reopens(reverse: False) | Failed | Passed |
| Compiled_simulator_skips_closed_controls_and_reopens(reverse: True) | Failed | Passed |

The two cases passed open traversal of each selected mode radio and six numeric
text fields, plus enabled buttons/checks, then focus removal/refusal and reopen
for text, close button and radio owners. Their inactive timer, no result or
playback, empty log, unchanged AP/MP and retained transition assertions passed.
Related passed totals are simulator 2, recorder 5, combat panels 6, picker 16,
picker UX 2, proxy 8, account 8, closed main modals 4 and settings 2.
Compilation and passing tests are the diagnostic evidence; no C# language-server
diagnostics were available. Existing unrelated CS8826 warnings remain untouched.
Only the new test's xUnit2031 warning was fixed as recorded above. Parent keeps
independent source/test review and native/build acceptance. No prior unit was
edited, no simulation/profile command executed, and no additional modal scope
was taken. This unit stops at these definitive results and evidence.

## LuaEditorOverlays six-panel bounded unit

Parent reported independent simulator acceptance 49/49, exit 0,
`mon_H6B1CNZQX8N1ABH3`. Its existing child-side result was already final:
2/2 new cases and 53/53 related cases, exit 0; it was not rerun separately.

LuaEditorOverlays initializes compiled XAML, subscribes to Bots property changes
and watches SelectedBot. There is no Loaded handler. Fullscreen property change
creates a real LuaCodeEditor, attaches its shared document and posts focus;
closing removes it and detaches its document. The VM fullscreen callback only
requests CloseAssist. GoTo/API visibility changes post focus to their fields.
The other visibility setters are plain. No open/close command is required.

The offline fixture bypasses Bots startup, initializes its collections and
supplies a real ephemeral TextDocument plus empty diagnostics/declarations.
It does not call LuaScript's setter: that hooks changes, warms analysis and
starts its debounce timer. No document edits are sent, because LuaCodeEditor
document changes start a folding timer. Attaching an unchanged document computes
foldings synchronously. API query/category setters load the catalog, so the
fixture leaves the legitimate empty API state unchanged. Recent rows are plain
fixture records, not read from disk. Templates use their built-in list without
application/insertion. No save/discard/file/template/script command is invoked.

Twelve cases (six panels by two directions) keep the matrix limited to the
required panel/direction pairs, testing closure owners within each case.
Four panels (fullscreen/templates/recent/API) already hide an inner Grid when
closed. Their initial closed Tab skip can pass without the new enable gate;
the regression separately asserts the requested outer disabled state. Only
GoTo/Unsaved have visible closed content and can demonstrate an initial Tab
leak. Gate-assertion failures will not be reported as behavioral Tab failures.

First monitor `mon_PHFQDBXZPP9564Z6` / `bash_27` exited 1 at compilation:
missing Views namespace and a Visual-only extension called on InputElement.
These fixture errors were corrected; they are not behavioral red.
Corrected pre-gate run: `mon_TF0D6SGH1KNB4H2W` / `bash_28`.

That retry exposed an unavailable GetVisualRoot API in this Avalonia version
and also stopped at compilation (exit 1). Replaced it with the supported
GetVisualAncestors window membership query; next pre-gate monitor is
`mon_Z0DN1Q6C2FMRFT1F` / `bash_29`, using the same command below. Neither
compile-only run is counted as a regression result.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~LuaOverlayKeyboardTests --results-directory Temp/lua-overlay-keyboard-20261105/red --logger "trx;LogFileName=lua-overlay-red.trx" --logger "console;verbosity=normal" --tl:off
```

### Lua actual red and minimal six gates

Monitor `mon_Z0DN1Q6C2FMRFT1F` / `bash_29` completed **exit 1: 12 executed,
0 passed, 12 failed**. Artifact:
`Temp/lua-overlay-keyboard-20261105/red/lua-overlay-red.trx`.
Four cases (GoTo and Unsaved, both directions) failed the initial closed Tab
destination assertion with a closed textbox/button focused. The other eight
cases (fullscreen/templates/recent/API, both directions) passed closed Tab then
failed Assert.False(overlay.IsEnabled). Those eight are gate-state regressions,
not demonstrated Tab leaks, because their existing inner visibility protects
closed traversal already. No test removes or overrides that visibility.

The only production edits add respective IsEnabled bindings to the six outer
Grids. Inner visibility, dynamic fullscreen editor creation, focus posts,
commands, document handling and animation styles remain intact.

Single related green, `mon_SH9PTXGYQH0XNMGH` / `bash_30`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~LuaOverlayKeyboardTests|FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/lua-overlay-keyboard-20261105/green --logger "trx;LogFileName=lua-overlay-green.trx" --logger "console;verbosity=normal" --tl:off
```

Claimed paths for this unit only:

- `GivreClone.App/Views/LuaEditorOverlays.axaml`: six respective enable gates.
- `GivreClone.UiTests/LuaOverlayKeyboardTests.cs`: one theory,
  `Compiled_lua_panel_excludes_closed_controls_and_reopens`, with panel indices
  0 fullscreen, 1 templates, 2 recent, 3 API, 4 GoTo, 5 unsaved; each has
  reverse False and True. Closure owners are exercised inside each case.
- `docs/validation/closed-modal-keyboard-20261105.md`: Lua evidence append.

Commands run at `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.
No native or separate build run was started. The related run above is the only
post-gate run; exact final counters and any runtime fixture failures will be
recorded after it completes, not inferred from the pre-gate result.

The first post-gate related run `mon_SH9PTXGYQH0XNMGH` / `bash_30` completed
exit 1: 65 executed, 63 passed, 2 failed. All 53 prior related cases and ten
Lua cases passed. Both fullscreen cases failed an unlabelled Assert.True focus
assertion, without an exception or source line in the Release trace. This is
not counted as green or yet attributed to production. Artifact:
`Temp/lua-overlay-keyboard-20261105/green/lua-overlay-green.trx`.
Assertion messages were added without weakening conditions; focused diagnostic
monitor `mon_QT22CNNK46TM5S4A` / `bash_31` runs the Lua theory only, same test
command options, results directory `Temp/lua-overlay-keyboard-20261105/focus-diagnostic`,
TRX `lua-focus-diagnostic.trx`. No related-suite duplicate or extra production
change was started.

Focused diagnostic `mon_QT22CNNK46TM5S4A` / `bash_31` completed exit 1:
10 passed, 2 failed, both at "Initial attached fullscreen TextArea focus".
Source inspection found TestApp.axaml includes FluentTheme/Controls but omits
the AvaloniaEdit theme that production App.axaml line 22 includes. Upstream
TextArea implementation confirms it is a templated input control (source:
https://raw.githubusercontent.com/AvaloniaUI/AvaloniaEdit/master/src/AvaloniaEdit/Editing/TextArea.cs).
The fixture now includes that exact production theme locally on its view,
without altering shared test startup or weakening any focus assertion.
This is a runtime fixture correction, not an additional production defect.
Targeted verification `mon_S9WCVBANE1AJM017` / `bash_32` uses the same target
filter and flags, directory `Temp/lua-overlay-keyboard-20261105/theme-green`,
TRX `lua-theme-green.trx`. All six production changes remain enable gates only.

### Parent scope correction: retain only behavioral fixes

Parent rejected outer-state assertion pins as justification for four gates.
The theme run `mon_S9WCVBANE1AJM017` / `bash_32` completed exit 1, 8 passed,
4 failed: fullscreen/templates traversal reached only one side of their real
AvaloniaEdit input. With the proper theme present, Tab is consumed as editor
indentation (upstream EditingCommandHandler.OnTab/OnShiftTab), not a focus-exit
key. Source: https://raw.githubusercontent.com/AvaloniaUI/AvaloniaEdit/master/src/AvaloniaEdit/Editing/EditingCommandHandler.cs.
The fixture now approaches from both boundaries and stops before sending Tab
inside TextArea, preserving real focus assertions without triggering document
edits or folding timers. No editor keyboard behavior is overridden.

Removed the outer disabled-state assertion and removed the fullscreen,
templates, recent and API enable bindings. Their existing inner IsVisible
encloses all interactive content; header controls are inside it, and outside
scrim/card Borders are non-focusable. They have no demonstrated need for an
additional gate. The only retained production bindings are LuaGoToOpen and
LuaUnsavedOpen: each leaked closed Tab in both directions in the genuine red
artifact. All twelve behavioral closed/open/reopen cases remain.

New targeted behavioral verification `mon_2CXRZ3P82MNFGTHT` / `bash_33`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~LuaOverlayKeyboardTests --results-directory Temp/lua-overlay-keyboard-20261105/behavioral-green --logger "trx;LogFileName=lua-behavioral-green.trx" --logger "console;verbosity=normal" --tl:off
```

Earlier references to six production gates describe an abandoned intermediate
version, not the final scope. The requested final scope is two necessary gates
and six-panel behavioral coverage. No related run was duplicated while pending.

The narrowed behavioral run `mon_2CXRZ3P82MNFGTHT` / `bash_33` completed
**exit 0, 12 executed, 12 passed, 0 failed**. Artifact:
`Temp/lua-overlay-keyboard-20261105/behavioral-green/lua-behavioral-green.trx`.
This proves the other four panels' existing visibility protection with their
outer gates removed, and retains real fullscreen TextArea focus and attachment
checks. No outer enabled-state assertion remains.

After this completed, parent requested the single final related validation of
the narrowed revision. Monitor `mon_9TV0CBEAE8TWQYK0` / `bash_34`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~LuaOverlayKeyboardTests|FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/lua-overlay-keyboard-20261105/final-related --logger "trx;LogFileName=lua-final-related.trx" --logger "console;verbosity=normal" --tl:off
```

### Lua definitive narrowed result and necessity of retained gates

Final monitor `mon_9TV0CBEAE8TWQYK0` / `bash_34` completed **exit 0:
65 executed, 65 passed, 0 failed, 0 skipped**. Artifact:
`Temp/lua-overlay-keyboard-20261105/final-related/lua-final-related.trx`.
All 12 Lua cases and all 53 prior keyboard/UX cases passed. No run remains
pending and no duplicate run was launched while another was active.

| Panel | Current-source pre-gate evidence | Final both directions | Production gate retained |
| --- | --- | --- | --- |
| Fullscreen | Closed Tab already skipped; state-only assertion rejected | Passed | No |
| Templates | Closed Tab already skipped; state-only assertion rejected | Passed | No |
| Recent | Closed Tab already skipped; state-only assertion rejected | Passed | No |
| API reference | Closed Tab already skipped; state-only assertion rejected | Passed | No |
| GoTo | Actual closed textbox/button Tab leak in both directions | Passed | LuaGoToOpen |
| Unsaved | Actual closed button Tab leak in both directions | Passed | LuaUnsavedOpen |

Final production diff is two bindings in LuaEditorOverlays.axaml: GoTo at line
433 and Unsaved at line 454. Both are necessary because their content is not
hidden by an inner visibility gate, unlike the other four. No behavior-based
justification was found for retaining the other four; they were removed.
The final test has no outer-enabled-state assertion. All six panels retain
closed Tab, open control traversal and close/reopen focus checks. Fullscreen
retains real TextArea focus, shared document identity and recreated-editor
reopen checks. It does not claim Tab exits a code editor: indentation is the
editor's actual behavior, so traversal approaches from both boundaries.

Final claimed files: `GivreClone.App/Views/LuaEditorOverlays.axaml`,
`GivreClone.UiTests/LuaOverlayKeyboardTests.cs`, and this evidence append.
Only the test view locally includes the production AvaloniaEdit theme. The
ephemeral document is unchanged, no analysis timer is created, and no editing,
save/discard/file/template/script command or persistence is exercised. API is
tested in its safe empty-catalog state; recent uses plain synthetic rows.
Animation definitions remain unchanged and card transitions are asserted
present, not visually timed. C# compilation and test results are the diagnostic
evidence; language-server diagnostics were unavailable. Pre-existing unrelated
CS8826 warnings remain outside scope. Parent owns native/independent acceptance.
Planning was read for preparation only; no planning or prior-unit file changed.
This narrowed Lua unit stops at definitive results and evidence.

## PlanningOverlays authorized root-gate unit

Parent reported independent narrowed Lua acceptance: 61/61, exit 0,
`mon_D3VMES4DSVWEJDM3`. Child-side Lua was already final at 12/12 target and
65/65 related, exit 0; no result was pending. This new unit only changes the
planning root visibility enable gate, one compiled test file and this append.

PlanningOverlays and PlanningEditor constructors initialize compiled XAML;
PlanningEditor registers rename key/lost-focus handlers but performs no load.
PlanningViewModel has no explicit constructor and its initializers only create
a detached CharPlanning and empty local collections. Its bound getters are
safe in the unloaded state. Enabled/AutoConnect/JitterMin changes save to
PlanningStore, and row edits persist, so no data mutation or populated script/
slot rows are used. ShowEditorActions=false is a plain display property.
The unchanged jitter value is only focused, never edited. No Load, Apply,
save, file picker, connection, close command or scheduler/tick is called.

The offline Bots fixture bypasses its constructor, initializes collection
fields, supplies the real unloaded PlanningViewModel and a plain synthetic
PlannedOfflineRow with CanConnect=true. It directly sets visibility and
OfflineEditorEnabled, without the CloseOfflinePlanning command that commits
renames and starts a delayed 250 ms editor disable. Thus two cases can test
both directions immediately closed with OfflineEditorEnabled still true,
as well as the settled editor-disabled closed state and header/footer buttons.
Open traversal requires the jitter field, enable toggle, two close buttons,
connect button and safe unloaded editor actions. Closing from field/close/
connect focus and reopening are tested inside each case; no activation keys,
pointer events, data edits, sleeps or timer triggering are used.

Pre-gate monitor `mon_5A00KACZWNV6CX7A` / `bash_35`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~PlanningOverlayKeyboardTests --results-directory Temp/planning-overlay-keyboard-20261105/red --logger "trx;LogFileName=planning-overlay-red.trx" --logger "console;verbosity=normal" --tl:off
```

Genuine pre-gate result `mon_5A00KACZWNV6CX7A` / `bash_35`: **exit 1,
2 executed, 0 passed, 2 failed**. Both
`PlanningOverlayKeyboardTests.Compiled_planning_excludes_closed_controls_even_before_editor_disable_and_reopens`
cases (`reverse: False/True`) failed the initial closed Tab destination
assertion with actual closed header/footer buttons focused. Artifact:
`Temp/planning-overlay-keyboard-20261105/red/planning-overlay-red.trx`.
This is a behavioral failure, not a disabled-state implementation assertion.

Only production change adds IsEnabled bound to OfflinePlanningVisible on the
overlay Grid. The inner OfflineEditorEnabled binding, delayed close behavior,
all commands, editor persistence logic and animation definitions are retained.
Single async related green `mon_WD1Q82P067SMPBP9` / `bash_36`:

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~PlanningOverlayKeyboardTests|FullyQualifiedName~LuaOverlayKeyboardTests|FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/planning-overlay-keyboard-20261105/green --logger "trx;LogFileName=planning-overlay-green.trx" --logger "console;verbosity=normal" --tl:off
```

### Planning definitive green and bounded handoff

Existing monitor `mon_WD1Q82P067SMPBP9` / `bash_36` completed **exit 0:
67 executed, 67 passed, 0 failed, 0 skipped**. No duplicate run was started.
Artifact: `Temp/planning-overlay-keyboard-20261105/green/planning-overlay-green.trx`.
Both actual planning cases (`reverse: False`, `reverse: True`) passed;
65 prior keyboard/UX cases also passed. The genuine red artifact above contains
both corresponding pre-gate behavioral failures, not state-only assertions.

Each planning case traverses seven enabled open controls: header/footer close
buttons, ConnectPlannedNow, jitter textbox, enable toggle, load-file button and
AddSlot. Controls are focused only, not activated. Both initial closed states
(OfflineEditorEnabled true and false) skip the overlay. Closing from jitter,
close and connect focus removes focus immediately while OfflineEditorEnabled
stays true; all seven reject closed direct focus, skip Tab and recover on reopen.
Unchanged jitter/enable/autoconnect values and empty slots/scripts are asserted.

Final claimed paths for this unit only:

- `GivreClone.App/Views/PlanningOverlays.axaml:11-12`: one root IsEnabled
  binding to OfflinePlanningVisible. The delayed editor gate is untouched.
- `GivreClone.UiTests/PlanningOverlayKeyboardTests.cs`: two cases of
  `Compiled_planning_excludes_closed_controls_even_before_editor_disable_and_reopens`.
- `docs/validation/closed-modal-keyboard-20261105.md`: this planning append.

Both commands run from
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.
Coverage is the real unloaded planning state, not populated store-backed rows.
No Load/Apply, script/file, save, close, connection or scheduler operation was
invoked. No bound value was edited, and no sleep or timer was triggered.
Animations are unchanged; retained card transitions are asserted present, not
visually timed. Compilation and tests provide diagnostics; no C# language-server
diagnostics were available. Pre-existing unrelated CS8826 warnings were left
untouched. Parent owns native/build and independent acceptance. No prior-unit
file was edited or subsequent production scope taken. This unit stops here.

## HdvSellOverlay bounded SellModalVisible unit

Source attribution: HdvSellOverlay.axaml owns the complete reported sequence:
bank refresh at line 55 (L.Bots.Hdv.BankRefresh, French "Actualiser sac + banque"),
Diagnostic UID at line 61, header close at lines 72-75, search/filter/sort at
lines 80-82, and CheckVisible at lines 83-88 (L.Bots.Hdv.Sell.CheckVisible,
French "Cocher la liste"). The resource values are in Lang/fr.axaml at 2896
and 1129 respectively. No reported control in this sequence remains unowned.
The separate HdvUidOperatorWindow is only the diagnostic destination; it is
not opened or changed. The sell panel also contains further settings/footer
controls, which are enclosed by the same root gate; no inventory/modal scope
was inferred beyond this actual source.

HdvViewModel's constructor and field initializers access Stores.Launch and
subscribe to live events, so the fixture bypasses both HDV and Bots constructors.
It uses real compiled new HdvSellOverlay(), real HdvViewModel, empty observable
collections and a constructor-bypassed LaunchService with only _hdvOpen,
_hdvMaxSlots and _hdvTaxPct empty dictionaries for read-only presentation getters.
No populated or selected inventory rows are required. SellModalVisible has no
changed callback and is the only exercised visibility setter. Numeric templates
may normalize initial values; the existing _loading guard is set before binding
so PriceStep/FloorPercent/BulkMaxLots callbacks cannot save. Empty preview
recalculation uses only those isolated dictionaries. No production seam added.
OpenSellModal, CloseSellModal, refresh, diagnostic, check/list selection, sell,
network, store or launch commands are never executed. No activation key, pointer
event or data edit is injected.

Pre-gate monitor mon_K2JSY51XYK4J68JV / bash_37 completed exit 1:
2 executed, 0 passed, 2 failed. Both reverse False/True cases fail the initial
closed Tab destination Assert.Same with an actual Button focused instead of
the boundary Button. Compilation, construction, showing/layout and boundary
Focus succeeded; these are genuine traversal failures, not fixture exceptions
or assertions pinning the enabled property. Artifact:
`Temp/hdv-sell-keyboard-20261105/red/hdv-sell-red.trx`.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~HdvSellKeyboardTests --results-directory Temp/hdv-sell-keyboard-20261105/red --logger "trx;LogFileName=hdv-sell-red.trx" --logger "console;verbosity=normal" --tl:off
```

Only production change: root Grid IsEnabled bound to Hdv.SellModalVisible,
alongside the existing class and hit-test bindings. No animation or command
definition changes. Single related green mon_F6ZXCG7CE1H53CV7 / bash_38
completed exit 0: 69 executed, 69 passed, 0 failed, 0 skipped. Both actual
HdvSellKeyboardTests cases (reverse False/True) and the 67 prior cases passed.
Artifact: `Temp/hdv-sell-keyboard-20261105/green/hdv-sell-green.trx`.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~HdvSellKeyboardTests|FullyQualifiedName~PlanningOverlayKeyboardTests|FullyQualifiedName~LuaOverlayKeyboardTests|FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/hdv-sell-keyboard-20261105/green --logger "trx;LogFileName=hdv-sell-green.trx" --logger "console;verbosity=normal" --tl:off
```

Both commands run from
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

### Sell definitive bounded handoff

Claimed paths for this unit only:

- `GivreClone.App/Views/HdvSellOverlay.axaml:36-37`: one visibility-bound
  root enable gate. No logic/profile/save/command change.
- `GivreClone.UiTests/HdvSellKeyboardTests.cs`: one two-case compiled
  regression, Compiled_sell_skips_closed_controls_and_reopens_without_commands.
- This evidence append, preserving the prior parent/child records.

The passing cases require initial closed Tab/Shift+Tab to reach the opposite
boundary. Open traversal reaches the actual enabled Buttons, search TextBox,
three ComboBoxes (two toolbar plus lot settings) and eligible numeric/template
fields. RefreshBankStock, Diagnostic UID and CheckVisible command identities
are asserted for attribution but never invoked. Closing with search, refresh
and diagnostic focused removes keyboard focus; every collected control refuses
closed direct focus, Tab skips the panel, and all recover direct focus on reopen.
Empty SellRows/SellRowsView, null selection, no bank loading and no bulk run are
asserted. No enabled-property value is pinned. Both cases assert retained card
transitions; animations are preserved, not natively or temporally measured.

No fixture failure or correction was needed: genuine pre-gate traversal red
was followed by the minimal binding and this one related green run. Compilation
and executed headless keyboard behavior provide diagnostics; no C# language
server diagnostics were available. Existing unrelated CS8826 partial-method
parameter-name warnings remain, including HDV, planning and settings methods;
none were suppressed or edited. Coverage is the real empty offline sell state,
not populated inventory, active selling, diagnostic windows or live profiles.
All controls named by the parent's sequence are owned by this overlay; no
unowned remainder from that sequence was found. Parent native/build and
independent acceptance remain separate. No native UI or external operation
was performed. This unit stops at these definitive results.

## MainWindow notification list and detail bounded unit

Parent reports HDV independent 65/65 green and native exact-path PID 32672,
window 1575232, 30 Tab steps with sell controls absent. This next unit is only
the notification list and detail grids, one test and this append.

Source attribution before editing: MainWindow.axaml:656 uses Notifications.IsOpen
for the list panel. Its lines 684-686 own Non lus, Incidents and Gravite d'abord;
693-699 own text/source filters. MainWindow.axaml:760 uses
Notifications.DetailVisible; 778-786 own CopyDetails and CloseDetail buttons.
All controls in the reported native sequence are owned by these two panels.
The list item template also has copy/dismiss actions, but the safe empty-list
fixture does not instantiate notification rows or invoke any list action.

MainWindow constructor restores/persists window geometry, attaches live stock
and providers, and loads catalogs on Opened. NotificationCenterViewModel loads
NotificationStore and wires Notifier.Sink; MainViewModel starts the wider shell.
The test therefore bypasses both VM constructors, initializes an empty Items
collection and supplies a detached real NotifRow/NotifItem as Detail. Visibility
and Detail setters have no changed callbacks. No Open command is called (it
would acknowledge and persist). No copy, acknowledgement, clear, dismiss, close
or other command is executed, and no filter value is edited.

NotificationOverlayKeyboardTests uses the already-linked TestAssets/MainWindow.axaml
in the existing csproj. AvaloniaRuntimeXamlLoader interprets each actual complete
grid, preserving MainViewModel compiled-binding context, descendants, command
bindings, styles and templates. Only the list scrim PointerPressed handler is
removed because no shell instance is created. No asset entry, dependency or
production seam is added. Four cases are list/detail times forward/reverse.
There is no IsEnabled property assertion. Neither panel declares a default or
cancel button. Shell Escape at MainWindow.axaml.cs:797-807 invokes close commands;
it is not loaded or exercised in this command-free panel fixture. No shortcut
coverage is claimed, and no activation keys are injected.

Genuine red mon_M5KE6HA9J3SZ56VR / bash_39: exit 1, 4 executed, 0 passed,
4 failed. All four reach the initial closed Tab destination assertion. List
forward actually focuses CheckBox; list reverse TextBox; both detail cases
focus Button instead of the boundary Button. Runtime loading/show/layout and
initial focus succeeded, so these are behavioral defects, not fixture errors.
Artifact: `Temp/notification-keyboard-20261105/red/notification-red.trx`.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~NotificationOverlayKeyboardTests --results-directory Temp/notification-keyboard-20261105/red --logger "trx;LogFileName=notification-red.trx" --logger "console;verbosity=normal" --tl:off
```

Production change is exactly two Grid attributes: IsEnabled bound respectively
to Notifications.IsOpen and Notifications.DetailVisible. Existing MainWindow
gates, animation definitions, commands, startup and offers remain untouched.
Single related green mon_ASCBC28BS050X7HA / bash_40 completed exit 0:
73 executed, 73 passed, 0 failed, 0 skipped. All four notification cases and
69 previous keyboard/UX cases passed. Artifact:
`Temp/notification-keyboard-20261105/green/notification-green.trx`.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~NotificationOverlayKeyboardTests|FullyQualifiedName~HdvSellKeyboardTests|FullyQualifiedName~PlanningOverlayKeyboardTests|FullyQualifiedName~LuaOverlayKeyboardTests|FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/notification-keyboard-20261105/green --logger "trx;LogFileName=notification-green.trx" --logger "console;verbosity=normal" --tl:off
```

Both commands run from
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

### Notification definitive bounded handoff

Final claimed paths are MainWindow.axaml:656 and :760 (only the two root
enable bindings), GivreClone.UiTests/NotificationOverlayKeyboardTests.cs
(four runtime actual-XAML cases), and this evidence append. The csproj and
existing linked assets are unchanged. Prior MainWindow gates are untouched.

Both directions skip the closed list/detail. Open list traversal reaches all
three checkboxes and both text filters; open detail traversal reaches actual
copy/close buttons with real command bindings. Closing from either filter or
either detail button removes focus, refuses every collected closed control,
skips closed traversal and restores direct focus on reopen. Detached detail
and model remain unread; Items remains empty and filter values remain unchanged.
All controls are focused only, never activated. The four cases assert retained
card transitions; no native animation timing or shell startup/shortcut behavior
is claimed. No fixture failure or adjustment intervened between genuine red
and the two minimal gates followed by the single green run.

Compilation and runtime tests provide diagnostics; no C# language-server
diagnostics were available. Existing unrelated partial-method CS8826 warnings
remain outside scope and are not suppressed. Safe coverage is empty notification
history plus a detached detail, not persisted or acknowledged notification rows.
No store, clipboard, notification command, external action or native UI ran.
All reported sequence controls are attributed; no unowned remainder was found.
Parent owns independent build/native acceptance. This unit stops here.

### Parent notification verification status update

Parent reports its isolated run (monitor prefix `mon_NC58...`, full identifier
not supplied here) failed before tests with MSB3883 / File.Copy /
DirectoryNotFound in its own Temp obj path. This is a parent build/artifact
failure, not a notification behavioral test result. Parent is diagnosing the
exact path and disk state; independent notification acceptance remains pending.
The existing child result above remains 73/73 passed, exit 0, from
mon_ASCBC28BS050X7HA / bash_40. No parent artifact was inspected or modified,
no duplicate parent run was started, and no further unit was opened. This
follow-up changes only this documentation.

## Final MainWindow map-preparation attribution unit

Parent subsequently reports notification Release retry 69/69 passed, exit 0,
and native exact PID 25428 / window 2949868 with filters/detail absent. This
supersedes the pending parent notification status above. No parent artifacts
were inspected or changed by this unit.

Exact attribution: MainWindow.axaml:877-905 MapExtractOverlay owns
CloseMapExtractCommand and L.MapExtract.Background (French resource line 114,
"Continuer en arriere-plan"). MapExtractVisible drives modalOpen and hit tests;
there is no IsVisible binding on that root or its action. Controls.axaml:860-888
sets closed card/scrim Opacity=0 and open Opacity=1, not collapsed visibility.
MainViewModel.cs:95 defaults MapExtractVisible=false. StartMapPrewarmAsync at
218-264 shows it only after detecting available map data and pending work,
then hides after completion; CloseMapExtract also hides it but can publish a
notification while busy. None of these methods is invoked by the fixture.
A real MapExtractVisible=true panel is intentionally open, including the
finished interval before it closes; that state must remain focusable. Native
Tab reachability alone does not establish the running shell's property value.
The test explicitly distinguishes both visibility states; no claim is made
that the parent's live startup property was observed through this fixture.

"Fermer / Voir les offres" are not MainWindow grid descendants: MainWindow.axaml
934 hosts LevelCapOverlay, whose own inner Grid binds LevelCapModalVisible.
Its buttons bind CloseLevelCapModalCommand and OpenLevelCapOffersCommand;
BotsViewModel.LevelCap.cs:16 defaults false, ShowLevelCapModal sets true, and
the offer command calls Links.OpenPlans then hides it. The component likewise
uses opacity-only modal styles. It is attributed but unchanged, with no native
live-state claim or behavior result for it. Parent explicitly owns the separate
offer unit after native verification. No broadened MainWindow host gate is used.

MapPreparationKeyboardTests contains two directional actual-XAML runtime cases
using the existing linked MainWindow asset/dependencies. It loads the complete
MapExtractOverlay with inherited MainViewModel compiled-binding context and
unchanged descendants/command binding. The Main VM constructor is bypassed;
only plain presentation setters are used. No extraction, background, close,
offer, navigation, store or external action is activated; no timer or startup
method runs. Both busy-open and finished-open states are covered, alongside
initial closed traversal, closing focused action and reopen usability.

Initial run mon_S2E4MCB0F9458VC7 / bash_41 executed two failures before
traversal: the test asserted command identity before the overlay was attached
to its DataContext-bearing Window, so actual Command was null. This is a
fixture failure, NOT behavioral red and NOT justification for a gate. Artifact:
`Temp/map-preparation-keyboard-20261105/red/map-preparation-red.trx`.
The command-identity assertion was moved after Show/layout, with no production
edit. Corrected pre-gate run mon_Y2F984VBH5AJ4MF4 / bash_42 uses the separate
behavioral-red results directory so the initial evidence is retained.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~MapPreparationKeyboardTests --results-directory Temp/map-preparation-keyboard-20261105/red --logger "trx;LogFileName=map-preparation-red.trx" --logger "console;verbosity=normal" --tl:off
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~MapPreparationKeyboardTests --results-directory Temp/map-preparation-keyboard-20261105/behavioral-red --logger "trx;LogFileName=map-preparation-behavioral-red.trx" --logger "console;verbosity=normal" --tl:off
```

Corrected pre-gate mon_Y2F984VBH5AJ4MF4 / bash_42 completed exit 1:
2 executed, 0 passed, 2 failed. Both cases passed attached command identity
and MapExtractVisible=false assertions, then failed initial closed Tab
destination Assert.Same with the map action Button reached. This is genuine
behavioral red; the earlier null-command fixture failure is not counted.
Artifact: `Temp/map-preparation-keyboard-20261105/behavioral-red/map-preparation-behavioral-red.trx`.

Only production edit: MainWindow.axaml:878 MapExtractOverlay adds IsEnabled
bound to MapExtractVisible. This excludes only the false state; the true state
still opens/focuses normally and all existing startup transitions/timers and
completion/background behavior remain unchanged. MappingLoadOverlay has no
button and no gate was added there. LevelCapOverlay and its MainWindow host
are unchanged. Previous accepted MainWindow gates remain intact.

Single related green mon_GNGAPZHSF14ZKFTB / bash_43 completed exit 0:
75 executed, 75 passed, 0 failed, 0 skipped. Both map cases (reverse True/False)
and 73 prior keyboard/UX cases passed. Artifact:
`Temp/map-preparation-keyboard-20261105/green/map-preparation-green.trx`.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~MapPreparationKeyboardTests|FullyQualifiedName~NotificationOverlayKeyboardTests|FullyQualifiedName~HdvSellKeyboardTests|FullyQualifiedName~PlanningOverlayKeyboardTests|FullyQualifiedName~LuaOverlayKeyboardTests|FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/map-preparation-keyboard-20261105/green --logger "trx;LogFileName=map-preparation-green.trx" --logger "console;verbosity=normal" --tl:off
```

All commands in this map unit run from
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

### Final map-preparation bounded handoff

Claimed paths: MainWindow.axaml:878 (one MapExtractVisible enable binding),
GivreClone.UiTests/MapPreparationKeyboardTests.cs (two actual-XAML directional
cases), and this evidence append. Existing asset/csproj/dependencies and prior
gates are unchanged. No other MainWindow grid or offer component was edited.

Passing behavior: MapExtractVisible=false skips the real action in both Tab
directions and rejects direct focus. True/busy reaches the actual action and
then the opposite boundary. Closing with action focused clears focus and
refuses closed focus while busy stays true. Reopening in finished-but-visible
state again reaches/focuses the action. Thus genuinely open preparation is
not suppressed and no startup decision or completion delay was changed.
Progress counters stay zero; no prewarm, command, timer or external side effect
is triggered. Card transitions remain present, not visually timed. There is no
enabled-property assertion or simulated substitute action.

The parent's live MapExtractVisible value was not observed and is not inferred
from Tab alone. This unit proves the false-state defect and true-state usability
in actual production markup, not live startup state. Fermer/Voir les offres
remain attributed to LevelCapOverlay/LevelCapModalVisible and deliberately
unchanged for the parent's separate unit. Parent owns native and independent
acceptance. Compilation/runtime checks provide diagnostics; no C# language
server diagnostics were available. Existing unrelated CS8826 warnings remain
untouched. The initial fixture error and corrected genuine red are separately
preserved above. This final MainWindow unit stops on the definitive 75/75 result.

## Authorized final LevelCapOverlay compiled unit

Parent reports map-preparation Release 71/71 passed, exit 0, accepted and
documented. The separately attributed LevelCapOverlay is now explicitly
authorized; parent owns native verification after the final combined result.

LevelCapOverlay.axaml:9 contains the modal Grid bound to LevelCapModalVisible.
Its two buttons bind CloseLevelCapModalCommand and OpenLevelCapOffersCommand.
The code-behind constructor only initializes compiled XAML. BotsViewModel's
LevelCapModalVisible is a plain observable property without a changed callback;
using it does not write stores or alter licensing. OpenLevelCapOffers calls
Links.OpenPlans and must never execute in the fixture. The close command,
ShowLevelCapModal, cap enforcement and licensing callbacks are likewise not run.

LevelCapKeyboardTests constructs the actual compiled LevelCapOverlay with the
existing offline Bots constructor-bypass/empty-collections pattern. Two cases
cover forward/reverse boundary traversal, closed direct focus refusal, open
both-button order, real command identity, closing from each focused button and
reopen focus. Only Tab/Shift+Tab are injected; no activation, offer navigation,
external action, game, timer or store operation is used. There is no enabled
property assertion and no production seam or dependency change.

Pre-gate mon_N6S2NEC3VX5GYW2H / bash_44 completed exit 1: 2 executed,
0 passed, 2 failed. Actual compiled construction, command identities and
boundary focus succeeded; both initial closed Tab destination assertions
failed with an overlay Button focused. Reverse reaches the primary offers
button; forward reaches close. These are genuine behavioral failures, not
fixture errors or enabled-state assertions. Artifact:
`Temp/level-cap-keyboard-20261105/red/level-cap-red.trx`.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter FullyQualifiedName~LevelCapKeyboardTests --results-directory Temp/level-cap-keyboard-20261105/red --logger "trx;LogFileName=level-cap-red.trx" --logger "console;verbosity=normal" --tl:off
```

Only production change: LevelCapOverlay.axaml:9-10 adds IsEnabled bound to
LevelCapModalVisible on the existing modal Grid. No licensing, offer, enforcement,
command, MainWindow host or animation definition changes. Open state remains
enabled by the same visibility property. Single async related green
mon_EH1MCZ4N0BN5S5K4 / bash_45 completed exit 0: 77 executed, 77 passed,
0 failed, 0 skipped. Both compiled LevelCap cases (reverse False/True) and
75 prior keyboard/UX cases passed. Artifact:
`Temp/level-cap-keyboard-20261105/green/level-cap-green.trx`.

```powershell
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release --no-restore -m:1 --filter "FullyQualifiedName~LevelCapKeyboardTests|FullyQualifiedName~MapPreparationKeyboardTests|FullyQualifiedName~NotificationOverlayKeyboardTests|FullyQualifiedName~HdvSellKeyboardTests|FullyQualifiedName~PlanningOverlayKeyboardTests|FullyQualifiedName~LuaOverlayKeyboardTests|FullyQualifiedName~CombatSimKeyboardTests|FullyQualifiedName~RoadRecorderKeyboardTests|FullyQualifiedName~CombatModalKeyboardTests|FullyQualifiedName~SocketPickerKeyboardTests|FullyQualifiedName~SocketPickerUxTests|FullyQualifiedName~ProxyModalKeyboardTests|FullyQualifiedName~AccountModalKeyboardTests|FullyQualifiedName~ClosedModalKeyboardTests|FullyQualifiedName~SettingsKeyboardAccessTests" --results-directory Temp/level-cap-keyboard-20261105/green --logger "trx;LogFileName=level-cap-green.trx" --logger "console;verbosity=normal" --tl:off
```

Both commands run from
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

### LevelCap definitive final handoff

Claimed paths for this unit: GivreClone.App/Views/LevelCapOverlay.axaml:9-10,
GivreClone.UiTests/LevelCapKeyboardTests.cs and this evidence append only.
Both directions skip closed buttons and both reject direct focus. Open state
traverses close/offers in the appropriate order, then the opposite boundary.
Both command identities match the actual VM commands. Closing from each focused
button removes focus, refuses both buttons and skips closed traversal; reopening
restores direct focus to both. No button is activated. No enabled-property
assertion, fixture correction or extra green run was used. Retained card
transitions are asserted present; native or timed animation behavior is not
claimed. Prior gates and assets are untouched.

All licensing and cap enforcement logic is unchanged. No Links.OpenPlans,
licensing, navigation, store, game or startup operation ran. Only the safe
LevelCapModalVisible setter was exercised. Compilation/runtime results provide
diagnostics; no C# language server diagnostics were available. Existing unrelated
CS8826 partial-method warnings remain outside scope, unsuppressed. Parent owns
independent combined/native verification. This authorized final overlay unit
stops at the definitive 77/77 result and evidence above.
