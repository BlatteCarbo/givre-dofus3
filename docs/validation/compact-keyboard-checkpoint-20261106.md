# Compact and keyboard QA checkpoint

## Resumed native Settings traversal

The expedition navigation gap was subsequently narrowed without repeating the
invocation. Source routes bots/3 through RequestedTab=3 and ShowBots; BotsView
intentionally hides selected-bot content when HasSelection is false. Fresh
native queries on the same QA window confirm `Aucun bot en jeu`, `Sélectionnez
un bot` and the enabled named `Ouvrir les comptes pour lancer un bot` action.
Focus-only on that action reports focused=true; a real Tab reaches the named
Back button. Thus absence of expedition controls here is consistent with the
observed no-bot prerequisite, not evidence of failed page navigation. Populated
expedition controls and visual acceptance remain unverified in this instance.
No bot was launched to satisfy this prerequisite.

The subsequent page-search invocation focuses the enabled native textfield
`Rechercher dans les pages locales` automatically. Element.type reports
`accessibility is unavailable on this backend`; no search text acceptance is
claimed and that failed input was not retried through another delivery method.
Fresh observation exposes the existing enabled `Bots › Expédition` result;
invoking that result dismisses the search field, but no expedition heading or
start control is then exposed by the targeted native queries. This is a
navigation acceptance gap, not proof that an expedition was started or that
the rendered page is blank. No account or script action occurred.

Fresh inventory identifies the existing isolated PID31792/window6097986 at
1460x920. Its actual Settings button opens the named volume slider and density
buttons. Focus-only on A · Grand followed by Tab reaches the enabled named Back
button with focused=true; the next Tab reaches the enabled named page/action
search button with focused=true. No density value or other preference changed.
This completes the previously pending forward exit segment, not reverse-tab
delivery or whole-application keyboard acceptance. A fresh screenshot was
captured but the interface explicitly omitted the image as unsupported, so no
visual compact verdict is inferred. Other Givre processes were untouched.

## Settings toggle-name follow-up

`settings-toggles-ready-red.trx` reproduces missing automation names with the
fixture's dynamic label resources supplied. The initial red run also exposed
missing resource text in the headless fixture and is not by itself proof for
those controls. SettingsView now shares each toggle's visible label with its
automation name; three previously literal labels use view-local resources.
The test focuses real controls without changing IsChecked. Developer cards are
made visible only in the fixture, without enabling packet capture.

The full UI suite is subscribed as bash_375 with receipt
`settings-toggles-green-full.trx`; its result is pending. The prior 224-pass
receipt predates this edit. Native inventory currently exposes windows2491222
(PID33552,1460x920) and8128494 (PID18080,1920x1032). Neither has yet been
established as a payload containing this correction. No native acceptance or
visual PASS is inferred from that inventory.

Process-path inventory distinguishes PID33552 as the older isolated
`givre-stream-ui-20261106` payload and PID18080 as
`givre-challenge-user-only-20261106`. A new Release payload is building in
`%USERPROFILE%/AppData/Local/Temp/givre-toggle-native-20261106` (bash_377).
It has not yet been launched. This avoids treating either older window as
evidence for the current toggle correction. Existing instances were not stopped.

The full UI run bash_375 completed with exit0:236 passed,0 failed,0 skipped,
receipt `settings-toggles-green-full.trx`. Its apparent stall was investigated:
testhost was awaiting its separate xUnit process, not blocked inside a Settings
control. The suite subsequently finished; no deadlock fix is claimed.

The inline PowerShell launcher failed parsing before launch because shell
interpolation removed variable references. The retained -File launcher avoids
that boundary and started PID8680. Independent native inventory confirms its
window10094754 at1460x920. Its Settings button was invoked. The corrected
phone-access toggle exposes its full visible label as a native Button name;
focus readback reports focused=true. One real Tab then reaches the named
local-network toggle, also enabled and focused=true. Neither was activated.
This establishes native forward-focus/name evidence for these two controls,
not complete page coverage or visual compact acceptance.

Native follow-up focuses `Sons d'interface` with its corrected name and reads
focused=true. The next actual Tab reaches an enabled, focused Slider with no
name. SettingsView.axaml identifies SfxVolumeSlider immediately following that
toggle; its visible label uses L.Settings.UiSoundsVolume and the slider has no
AutomationProperties.Name. No audio value was changed. This is the next
keyboard-accessibility defect, not evidence of complete Settings acceptance.

The real-control volume regression bash_390 completed with exit1:1 failed at
displayed-label/name equality (actual null); focus, visibility and enabled checks
passed first. SfxVolumeSlider now uses the same L.Settings.UiSoundsVolume
resource as its visible label. No value handler, range or preference changed.
The full UI suite is subscribed as bash_391 with receipt
`settings-volume-green-full.trx`. Native readback of this newest one-line
correction remains outstanding; PID8680 predates it.

Continuing the actual forward Tab sequence in PID8680 from the audio slider
reaches, in order, named enabled buttons `Musique d'ambiance`,
`Particules d'arrière-plan`, `Décor de saison` and `Injecter les mods natifs`.
Each transition has native focused=true readback. No toggle was invoked, so
this verifies the corrected names/focus segment without changing audio, visual
or injection preferences. It does not verify the later developer-only controls.

The following native Tab destinations are named and focused in sequence:
`Vérifier les modules hors ligne`, enriched-item-tooltip toggle,
`Format du suffixe`, `Relance automatique`, then the sensitive-backup inclusion
checkbox. None was invoked or changed; no export was performed. This connects
the corrected sound/visual-settings segment to the already documented backup
and numeric-safety segment. Developer-only packet controls were not exposed
in this native profile, so their headless proof remains distinct.

The volume Release build bash_392 completed with exit0. Launcher bash_393
reported PID31792/window0; subsequent process inventory confirms that exact
new executable is alive/responding, but native window inventory does not yet
expose a window for it. Input-idle is therefore not accepted as native readiness.
No second launch was attempted. Its stack diagnostic is subscribed as bash_396.
The pending full UI run owns xUnit process35220; its diagnostic is bash_395.
Neither pending diagnostic is evidence of a deadlock or a passing test.

Both pending boundaries subsequently resolved. The UI stack showed actual
CompactRouteCaptureTests execution inside PNG frame saving, not a Settings
deadlock; bash_391 completed with exit0:237 passed,0 failed,0 skipped in
`settings-volume-green-full.trx`. The new native PID31792 exposed window6097986
at1460x920. Its real Settings button was invoked and the native slider is named
`Volume des sons`, enabled, with focused=true after focus only. No volume value
was changed. This closes the volume correction's headless and native-name
validation, not the complete compact visual or keyboard acceptance tasks.

On current PID31792/window6097986, the actual forward-Tab sequence after volume
was checked through music, particles, seasonal decoration, native injection,
offline-module verification, enriched tooltips, suffix format, automatic restart,
backup inclusion, export, restore, all four numeric safety inputs, stream mode,
discovery capture, exhaustive capture and archimonster Discord notification.
Every destination was enabled, named and reported focused=true. Capture options
were focusable in this native run, unlike the earlier profile observation; no
option was activated and no value, export or notification was sent. This also
provides current native evidence for both corrected capture-toggle names.

Next forward Tab reaches named Discord rich-presence, Join Discord and compact
density buttons. None was invoked, including the external link. A subsequent
shift+tab request unexpectedly advanced through Normal, Grand and Back instead
of reversing to Join Discord. This is a native-input delivery observation, not
proof of a reverse-tab product defect; no reverse acceptance is claimed from
those requests. The focused names are valid but direction needs an independent
key-delivery check before using this trace for reverse-navigation conclusions.

A separate delivery attempt used the array chord [shift,tab] after requesting
focus on the rich-presence toggle. Focus readback reached Join Discord, again
the forward neighbor. This does not establish Shift modifier delivery. Neither
background chord representation is accepted as native reverse-navigation proof;
the existing headless reverse test remains a separate type of evidence.

## Resumed native QA, independent payload PID36524

The old expedition QA launcher announced PID30840 ready but no process or
window survived the subsequent inventory. Its output directory lacks
Avalonia.Controls.dll. The launcher now rejects that incomplete payload before
Start-Process; validation produced the explicit missing-dependency error and
exit1 as expected. No live process was stopped.

The independently built threshold payload then exposed PID36524/window1509786
at1460x920 under parent-compact-20261105. Invoking its actual Réglages button
resolved both Format du suffixe and Relance automatique as enabled native
controls. Focusing the suffix combobox confirmed HasKeyboardFocus=true;
one Tab reached the named Relance automatique button, and another reached the
named checkbox controlling sensitive-data inclusion in backups. Neither was
activated and no export was requested. This is native forward-focus evidence
for this segment only; full keyboard and compact visual acceptance remain open.

Screenshot output remains omitted by the current model interface, so no visual
PASS is inferred from native bounds or accessibility alone.

The same forward-tab sequence continued through Exporter sur le Bureau and
Restaurer un zip without activating either. The next focused input was unnamed.
Native ancestry identified its actual owner as NumericUpDown with
AutomationId=PauseTurnsBox, also unnamed; the focused child is PART_TextBox,
enabled with HasKeyboardFocus=true. This is a confirmed accessible-name defect
for the pause-after-turns setting, distinct from the previously corrected
challenge threshold. SettingsView.axaml also exposes three neighboring numeric
controls (AbandonTurnsBox, FightPauseMinBox, FightPauseMaxBox) without explicit
accessible names; their focused native behavior has not yet been checked.
No numeric value was edited. Full keyboard acceptance remains open.

### Numeric settings accessible-name correction

settings-numeric-name-red.trx contains four failures at the intended equality
assertion: each focused real template TextBox exposes null instead of its
displayed label. Visibility, enabled state and actual focus assertions pass
before that assertion. The test compares shipped label/input equality, not
fixed translated prose, and never changes a numeric value.

SettingsView.axaml now shares each existing label through a view-local string
resource used by the visible text, parent NumericUpDown and internal TextBox.
The styles are scoped to the four named safety controls only. Receipt
settings-numeric-name-green.trx reports 6 passed, 0 failed, 0 skipped, exit0:
the four focused-name cases and both existing forward/reverse Tab cases.
Compilation retained 225 application and 7 UI-test warnings. Native acceptance
of this corrected payload remains pending; the running QA process predates it.
The compact/desktop light/dark Settings capture matrix is being regenerated
as settings-numeric-layout.trx; no visual PASS is claimed.

The regenerated matrix completed exit0: 4 passed, 0 failed, 0 skipped.
The first independent native build failed NETSDK1004 because --no-restore
was used with a new artifacts directory lacking project.assets.json. No
application launch was attempted from that output. A build with restoration
into the same isolated directory is now subscribed. The new guarded launcher
start-settings-numeric-qa-20261106.ps1 requires the complete payload and the
six-test receipt, and checks for a surviving window after input-idle. Close
the existing isolated PID36524 before reusing its QA profile; do not stop live
PID18080 or run two instances against parent-compact-20261105 concurrently.

The restored native build completed exit0. Parent closed only the observed
old QA window1509786 using its enabled chrome close button, then launched the
corrected payload. Its launcher reported no window immediately after input-idle,
but a fresh process/window inventory established PID34868/window5179122 alive
at1460x920. This is a premature readiness assertion, not an application crash;
no duplicate launch was attempted. The old launcher monitor later timed out,
which is not used as an application exit receipt.

Native acceptance on corrected PID34868 succeeded: the actual Réglages button
opens named Pause spinners. Focusing Pause après reaches its internal TextBox
with Name=Pause après (tours), enabled=true and focused=true. Three successive
Tabs reach named focused TextBoxes Abandon après (tours), Pause entre combats,
min (s), and Pause entre combats, max (s). No value was typed or spinner invoked.
Thus all four corrected focused-input names are accepted on the native surface.
Full compact visual and whole-application keyboard QA remain open.

After native acceptance, the complete UI suite is subscribed as bash_364 with
receipt settings-numeric-ui-full.trx. This broadens verification from the six
focused settings tests to the existing application UI contracts; result pending.
A later native tree exposes the protocol-loading overlay, so no further focus
sequence was inferred from the prior screen state and no account action issued.

Reopening the actual Réglages destination restored the expected named spinners.
Focusing the max-pause input and pressing Tab reaches an unnamed enabled button.
Its native attributes identify AutomationId=StreamToggle, ClassName=ToggleSwitch,
HasKeyboardFocus=true and empty Name. The source uses L.Settings.StreamMode
for its visible adjacent label but supplies no accessible name. It was never
activated. A focused-control regression is now subscribed as bash_365 with
settings-stream-name-red.trx; it injects a view-local resource sentinel and
checks the real focused toggle without changing IsChecked. This is a new
attributed defect, not a failure of the four numeric-name corrections.

The complete UI receipt settings-numeric-ui-full.trx records 223 passed,
0 failed, 0 skipped. However bash_364 did not exit before its monitor deadline
and was killed. This is not a clean command-level full-suite success; testhost
termination remains an unresolved harness finding. Concurrent Stream regression
bash_365 failed MSB3027/MSB3021 before testing because PID35800 held the shared
UI-test executable. Subsequent identity inspection found no surviving PID35800.
No account process was killed. Stream regression bash_367 now uses a separate
Release artifacts directory givre-stream-ui-20261106 to avoid the collision.
There is still no causal red result for StreamToggle at this checkpoint.

The isolated run subsequently completed exit1 with the intended assertion:
settings-stream-name-isolated-red.trx expects the injected localized resource
name and receives null from the actual focused StreamToggle. The one-line
production correction binds AutomationProperties.Name to L.Settings.StreamMode,
the same existing resource as its displayed label. All SettingsKeyboardAccessTests
are now subscribed as bash_368 with settings-stream-name-green.trx. No checked
state changed. Corrected native acceptance and a clean full-suite exit remain
pending; the prior 223-test receipt predates this new name correction.

settings-stream-name-green.trx completed exit0 with 7 passed, 0 failed,
0 skipped. An independent App build into givre-stream-ui-20261106 also
completed exit0. Parent closed only the old isolated QA window5179122 and
reused its profile; live PID18080 remained untouched. Updated launcher receipt
correctly reports STARTED rather than READY when MainWindowHandle is still0.
Fresh native inventory then establishes PID33552/window2491222 at1460x920.

Actual Réglages navigation exposes the enabled button Mode Recording / Stream.
Focusing it confirms AutomationId=StreamToggle, ClassName=ToggleSwitch,
Name=Mode Recording / Stream and HasKeyboardFocus=true. No invocation or
preference change occurred. This accepts the corrected name on the native
surface. Full isolated UI suite bash_370 remains subscribed with receipt
settings-stream-full.trx; no clean full-suite result is claimed yet.

The isolated full suite subsequently completed with exit0:
settings-stream-full.trx reports 224 passed, 0 failed, 0 skipped. This supersedes
the earlier command-termination uncertainty for this source state, without
claiming that its transient shared-output lock is a production defect.

Next native Tab after Stream reaches another unnamed focused ToggleSwitch,
AutomationId=PacketDumpToggle. No capture option was enabled. Source inspection
shows the same missing-name pattern on the remaining settings switches, so
one parameterized real-control test now covers their displayed-label equality.
It makes developer cards visible only in the isolated headless fixture and
focuses controls without changing IsChecked. Baseline is subscribed as bash_372,
settings-toggles-red.trx. Native whole-page coverage remains open.

## Latest deployed challenge settings, PID18080

Fresh native screenshot of window8128494 returned1920x1032 metadata but the
image was explicitly omitted by the model interface. Visual compact QA remains
open. Native selection had changed to Sylvanbot during the preceding work; it was
not silently switched back or used to start a script.

Opened Combat through its actual tab. The localized HP-threshold spinner was
enabled, on-screen at1555,980,124,36, with persisted value25 for Sylvanbot. No value
was changed. Focusing the spinner transfers native keyboard focus to its inner
PART_TextBox (bounds1555,980,56,36, enabled, focused, Name empty). One Tab
reached the named XP radio button. A subsequent shift+tab reached Butin, not the
preceding text input; this reproduces the previously unresolved native modifier
delivery issue and is not evidence that reverse app tab order is broken. No
radio button was invoked. This is a narrow native focus segment, not full
keyboard or visual acceptance. Parent spinner has a localized name; the inner
focused text box still exposes an empty native name.

### Focused threshold input regression

`BotsKeyboardAccessTests.Challenge_threshold_focused_input_exposes_its_accessible_name`
now mounts the actual BotsView with an identified character profile and active
challenge rules. A view-local resource sentinel supplies the accessible label;
the test focuses the real template TextBox and asserts that input's name.
It neither pins translated prose nor changes a live profile.

The early receipts `threshold-name-red.trx` and `threshold-name-green.trx`
failed to find the control because the fixture selected Expedition, not Combat.
`threshold-name-real-red.trx` and `threshold-name-enabled-red.trx` then failed
before the name assertion because the isolated profile was not ready. These
are fixture failures, not causal accessibility evidence.

With the correct Combat tab, an identified profile key, and ChallengeRules=true,
`threshold-name-ready-red.trx` fails at the intended name assertion: expected
the injected resource value, actual null. Visibility, enabled state and actual
keyboard focus assertions all pass first. The production fix applies the same
localized resource as the parent spinner to its internal template TextBox,
scoped only to the challengeHpThreshold class. Full UI validation bash_328
completed with exit0:219passed,0failed,0skipped in239.5seconds;
receipt `GivreClone.UiTests/TestResults/threshold-name-full.trx`.
Native acceptance and deployment remain pending; this test does not close
the broad compact or keyboard QA tasks. The compiler emitted225 application
warnings and7UI-test warnings; no warning was suppressed for this change.

The isolated native QA launch did not reach a usable window. The first launcher
returned an input-idle receipt but the process disappeared. A persistent launch
reported CLR exit -532462766. Redirected stderr on bash_338 establishes the
actual error: FileNotFoundException for Avalonia.Controls12.1.1 at Program.Main.
The application directory was a project-reference output of the UI-test build,
not an independently built application payload. Input-idle alone was therefore
not a successful QA-start criterion. No live process was replaced or stopped.

A direct build of GivreClone.App into the separate
`%USERPROFILE%/AppData/Local/Temp/givre-threshold-native-20261106` directory is
subscribed as bash_339. Build result and native acceptance remain pending.

The direct application build bash_339 completed with exit0. The corrected
payload launched as PID8300/window3345174 under bash_340 and exposed the real
dashboard at1460x920. Native Bots navigation showed zero sessions and
"Aucun bot en jeu"; the isolated profile cannot expose an editable combat
threshold without an identified session. No live account data was copied,
no account was connected and no fake game state was injected to claim native
acceptance. Parent closed only this QA window using its observed native close
button. The startup dependency issue is resolved for the independently built
payload, but the focused field's native acceptance remains open. Headless
219-test coverage is the established evidence for that corrected field.
The launcher then exited1 because its Process handle exposed a null ExitCode
after the observed UI close. That is an unavailable launcher receipt, not
evidence of another application crash or a confirmed clean exit code. Its
diagnostic now explicitly distinguishes null from a nonzero process code.

## Fresh window inventory after replay validation

### Auto-relaunch accessible-name correction

Full updated UI suite bash_264 completed exit0:213 passed,0 failed,0 skipped,
in3m47s. Receipt:
GivreClone.UiTests/TestResults/settings-accessibility-full-20261106.trx.
This validates the existing automated UI contracts after the accessible-name
change. It does not close the native classic-shell discrepancy or substitute
for inspection of rendered captures across all routes.

SettingsKeyboardAccessTests bash_263 completed exit0:2 passed,0 failed,in3s.
Receipt:GivreClone.UiTests/TestResults/settings-keyboard-20261106.trx.
At720x640,forward and reverse Tab reach every visible enabled tab-stop Button
without invoking it;each reached button fits client bounds after layout.
This is deterministic headless keyboard/scroll evidence for Settings only,
not acceptance of all pages or proof of native Shift delivery.

Updated Settings capture matrix bash_262 completed exit0:4 passed,0 failed,
2s,covering720x640/1200x800 in light/dark. Receipt:
GivreClone.UiTests/TestResults/settings-route-20261106.trx.
Parent read the assertions:root matches client bounds,scroll viewports fit
their scrolling ancestors,horizontal overflow with disabled scrolling is
rejected,and scroll-to-end offsets are checked. The test constructs SettingsView
directly,so it does not validate classic-shell navigation. Images remain
unexamined by the current model;these are geometry assertions,not visual PASS.

Native acceptance completed on the same binary after changing only the closed
QA profile's UiSkin from classic to default. bash_260 confirmed PID13428 exit;
bash_261 launched PID35456/window4917764 at1460x920. Settings invocation now
exposes Relance automatique. Native focus and attributes confirm
AutomationId=AutoRelaunchToggle,Name=Relance automatique,IsEnabled=true,
HasKeyboardFocus=true. The toggle was focused but never activated. This accepts
the localized accessible-name fix on the standard shell. Classic-shell
navigation remains a separate unresolved finding;no navigation code changed.

Focused native attributes identified the previously unnamed button as
AutomationId=AutoRelaunchToggle,ClassName=ToggleSwitch,Name empty. The control
is crash auto-relaunch,not network reconnection. SettingsView.axaml now binds
AutomationProperties.Name to its existing localized L.Settings.AutoRelaunch.
bash_256 compiled successfully,exit0. No preference value was toggled.

The first inline QA launch failed PowerShell parsing before execution.
Tracked start-relaunch-name-qa-20261106.ps1 avoided shell expansion and
guards against duplicate executables. bash_258 launched PID13428 with isolated
profile parent-compact-20261105 and distinct pipe;bash_259 observed window2228866.
This retained profile currently exposes the classic sidebar. Invoking its
named Reglages button left a content subtree containing Ouvrir les Comptes;
neither suffix nor native-module controls were discoverable. Native acceptance
of the new label is therefore pending,not inferred from successful compilation.
Source navigation binds ShowSettingsCommand to NavigateTo(SettingsVM,"settings");
the discrepancy still needs attribution. Live PID7132 was not restarted.

ClassicPageResolutionTests now exercises the shipped ClassicShellBody host
without MainViewModel or account services. Production ViewLocator is scoped to
the test window because TestApp does not register it. bash_265 completed exit0:
2 passed,0 failed,in3s;receipt classic-page-resolution-20261106.trx.
With keyed row templates retained or removed,the actual content presenter
resolves SettingsView with the supplied SettingsViewModel and positive visible
bounds. Thus the tested host/locator path works. The test directly sets Content,
disables cross-fade,and does not load ClassicControls:it does not establish
that production commands,bindings,styles or animation are correct. No runtime
navigation fix follows from these results.

The reproduction was extended with the shipped ClassicPalette and
ClassicControls scoped to the window,without changing application globals.
bash_266 completed exit0:3 passed,0 failed,in3s;receipt
classic-page-styles-20261106.trx. The styled host still resolves a visible
SettingsView. This narrows the unresolved native discrepancy to factors not
exercised here,including command/binding state and transitions;it is not
evidence that the full classic navigation works.

Further source attribution:the classic content host is outside the team/solo
item templates and has no local DataContext override. Application.DataTemplates
registers the shared ViewLocator;MainWindow defines no Settings-specific local
template. The profile diag.log contains startup/shutdown markers only through
the two QA launches,no exception identifying failed Settings construction.
These checks rule out the inspected explicit overrides,not runtime binding
or template resolution failures. No speculative routing change was made.

Native inventory no longer contains isolated QA window6947718. Only live
PID7132/window6751846 is present,1920x1032. No duplicate QA process was launched.
The live Settings navigation is reachable. Its Format du suffixe combobox is
enabled and explicitly named. Focus then one Tab reached an enabled unnamed
button with nested groups;its function remains unidentified and it was not
activated. This is an attribution finding,not yet an application defect.
Screenshot capture again returned metadata but the model interface omitted
the image. No visual compact acceptance is claimed. Account settings were
not changed and no connection or launch action was invoked.

## Teams empty-state native navigation

On the same isolated window6947718, native invocation of the named Equipe
navigation button exposed the enabled empty-state instruction to launch at
least two accounts, the enabled Nouvelle equipe action and team console title.
The navigation button retained native focus. No team was created and no
account was connected. The first global textfield query returned disabled
controls from hidden panels; those are not evidence of active team fields.
This confirms destination reachability and its empty-state text only, not
team editing tab order, screenshot fit or full compact acceptance.

## Native quick navigation to professions

Forward focus inside the opened Coach subsequently passed from
`Préférence entre temps et kamas` through `Nom de ressource du stock manuel`,
`Quantité en inventaire`, `+ Ajouter`, and `Filtrer les recettes chargées`.
Each step used one Tab followed by native focused-element readback with
focused=true. No text value or preference changed and Ajouter was not invoked.
This verifies named sequential reachability for that segment, not reverse-tab
delivery, all panel fields, or screenshot-based focus visibility.
The named `Fermer le Coach métiers` action was then invoked natively and its
button disappeared from the same bounded query, confirming this exit path.

On isolated updated window6947718, invoking the named header search button
focused the enabled field `Rechercher dans les pages locales`. Window-targeted
typing of unaccented `metiers` exposed the `Coach métiers` result. Enter opened
the destination, confirmed by enabled Coach title and named close action.
Element.type itself reported accessibility unavailable on this backend; window
typing was used after that capability error, not after a permission denial.
No recipe, acquisition or account operation was invoked.

Escape left this destination open. Parent inspected the window key handler:
Escape closes help, notification layers, advanced-tools menu and quick search,
not ordinary profession destinations. This observation is not labelled a
regression against an unestablished page-close contract. Full native keyboard
and visual coverage remain open; this proves only search focus, accent-free
destination lookup and Enter activation on the actual corrected QA instance.

## Updated-build help and Escape acceptance

### Notifications follow-up

On the same isolated updated instance, invoking the header Notifications button
exposed enabled titles `Notifications` and `Aucune notification pour ces filtres.`.
After native Escape, the empty-state text remained queryable but became disabled.
Its native IsOffscreen still reported false with retained bounds. This differs
from the help title disappearing entirely: a global AX search alone cannot be
used as a reliable panel-visibility assertion. No notification was created,
acknowledged, exported or deleted. Full screenshot acceptance remains open.

On isolated corrected PID12692/window6947718, native invocation of the header
`Raccourcis clavier` button exposes an enabled static title `Raccourcis clavier`.
Native Escape then removes that title from the same targeted query. This closes
the earlier ambiguous help/escape observation for this updated standard-shell
session. Parent read the actual IsVisible/IsHitTestVisible binding and Escape
handler before the check. No settings, accounts or external links were changed.
This does not validate the old live binary or full visual fit of the help panel.

## Corrected-build native suffix-name acceptance

## Corrected-build native account-row labels

Actual focus of the row connection combobox subsequently moved its rectangle
from y944..984 to y808..848, inside the window's y116..916 extent. Tab then
focused the proxy combobox at y808..848. This directly confirms focus-driven
scrolling for those two controls; the earlier unfocused bounds do not establish
a clipping bug. No popup opened or selection changed. This remains geometric
and focus evidence, not a screenshot-based visual verdict or a full traversal.

A following Shift+Tab request read back focus on the MITM launch button,
which is forward of Proxy, not on Connection. The same modifier-delivery
ambiguity was previously observed on the header. No launch key was sent and
the button was not invoked. This observation is not labelled an application
reverse-tab regression without independent modifier-delivery evidence.

The isolated pipe list_bots confirmed the seeded no-credentials account is
loaded and offline; its absence from initial global AX queries was not an
account-store loss. Tab from the sort field reaches that row's selection
checkbox. Walking the actual parent/child subtree locates the same row's
connection/proxy controls even though the global ComboBox search omitted them.
Native attribute readback confirms the updated names
`Type de connexion: où arriver au lancement` and `Assigner un proxy`, both
enabled and keyboard-focusable. Thus both row-name fixes are now accepted on
the updated artifact. No selection, connection, proxy assignment or launch
was invoked. The bounds extend below the viewport and IsOffscreen remains
false, so these attributes still do not establish compact visual fit.

On the same corrected QA instance, Accounts navigation exposes its named
search field, state filter, server filter and sort combobox. Focusing the
search then sending three separate Tab keys reaches the state filter, server
filter and sort in that order; each native focused-element readback confirms
its explicit name and focused=true. No selection or account launch occurred.
The seeded account row was not found in bounded native queries; account-row
connection/proxy label acceptance is therefore still outstanding, not inferred
from the named page filters. Source inspection confirmed the local store path
and schema but did not establish the missing row's cause. No account fixture
or production account-loading logic was changed during that investigation.

The retained no-credentials QA profile was changed from classic to default
shell and1200x800. Bash_216 launched only that isolated profile and separate
control pipe as PID12692; bash_217 confirmed responding=true and window6947718.
The first window inventory preceded creation; no duplicate launch was sent.
Native Settings invocation succeeded on this standard shell. A native ComboBox
query now finds `Format du suffixe`; focus and attribute readback confirm
Name=Format du suffixe, IsEnabled=true and HasKeyboardFocus=true. This accepts
the explicit name fix on the updated artifact, not just source or headless tests.
No selection, preference save or account connection was triggered.

The reported ComboBox bounds are y874..916 while IsOffscreen=false; that native
property still does not prove visual fit. Full compact and keyboard traversals
remain open. Live account process36248 was not restarted or deployed.

## Further native keyboard observation

On process36248/window7080826, the parent observed the actual sidebar ancestry
and invoked Settings. It focused `Vérifier les modules hors ligne` without
activating it. Native Tab reached `Infobulles enrichies : identifiants et
données locales`; the next Tab reached the suffix combobox, with focused=true
and an empty accessible name. No toggle, selection, module verification or
saved preference was changed. This proves forward keyboard reachability on
the old live artifact, not acceptance of the new accessible-name fix. The
initial button bounds reached y916 in the1200x800 window; IsOffscreen=false
alone is therefore not proof of visible fit. Compact visual QA remains open.

This is an incomplete traversal, not acceptance of the compact or keyboard
tasks. The parent inspected live process 35876, window 3409622, without
restarting the application or changing account scripts. It is the existing
live build, not the newly compiled movement-continuation build.

The custom maximize/restore button was invoked through Windows accessibility.
The window changed from 1920x1032 at (0,0) to 1200x800 at (360,116).
The restored accessibility tree exposes the header search, help, notifications,
window controls, primary navigation and a scrolling Settings page.

The Settings page exposes language, appearance, interface, remote control,
audio, effects, native mods, long-combat limits, privacy, capture, Discord,
density and About sections. This proves accessible presence, not absence of
visual overlap or clipping. No setting value was changed in this checkpoint.

The parent invoked the help button, then F1, then the help button again,
observing between actions. Searches still exposed only the header help button,
not the expected help title. The enabled generic Close result was ambiguous
with window chrome and hidden modal controls. Escape was sent afterward.
These observations do not establish whether help is absent, AX omits the
overlay, or background input is not reaching the intended handler. No UI bug
or successful help/escape behavior is claimed from them.

The screenshot call produced a 1920x1032 frame, but its image was unavailable
to the current model interface. Visual layout quality therefore remains
unverified. Further QA must inspect rendered evidence and finish all page,
scroll and keyboard states before either traversal task can be closed.

## Accounts and navigation traversal

At the same 1200x800 size, the parent opened Accounts through its accessible
navigation button. The page exposed a separately named account-search field.
Setting only that filter to `zzqaaucuncompte` produced the accessible empty
state `Aucun compte ne correspond aux filtres.` Clearing the filter removed
that state. No account credentials, connection or launch state was edited.

The parent then opened the global dashboard. Its accessible controls included
local indicator refresh and the notifications destination. Invoking the header
Back button returned to Accounts: the named account-search field reappeared
and the dashboard-refresh control disappeared. This proves these AX-driven
navigation outcomes, not visual fit, keyboard-only reachability, or full page
coverage. The filter was left empty after the check.

The header search command was then invoked on Accounts. A widened AX query
returned all eighteen text fields (including hidden disabled modal fields),
but none for QuickNavSearch; only the four Accounts fields were enabled.
This excludes the earlier seven-result query limit as the explanation for
the missing palette field. A read-only source investigation was started to
check event routing and live-build provenance before altering any UI code.

## Build provenance and advanced-tools control

The parent independently read the source guard and overlay bindings after the
read-only investigation. Search is gated by blocking/loading/notification
states; the header help command directly toggles its own visibility. No
unconditional immediate close is established from those source paths.

Process 35876 resolves to
`%TEMP%/givre-last-enemy-20261105/bin/GivreClone.App/release/GivreClone.exe`.
Its adjacent DLL is 35244544 bytes, modified November 5 at 23:18:02, SHA256
`D46E36CCAE25482AEBCECAD627A69F5A3B54011B0807280324AA3B0983753DAB`.
The first provenance command failed because shell expansion removed its
PowerShell variable; the corrected variable-free command exited zero.
This identifies the tested artifact, not source equality with current files.

As a control, invoking the header advanced-tools button exposed the object
workshop, economy workshop and world-atlas destinations. Opening the atlas
exposed its named map-ID search, offline-atlas title and explicit Close Atlas
button. The parent closed it through that button afterward. Thus at least
one header overlay and its destination work through AX in this live build;
the missing quick-navigation/help observation is not explained merely by all
overlays being absent from accessibility. Visual fit and keyboard-only
operation remain unverified, and neither QA task is complete.

## Native keyboard delivery checkpoint

The parent explicitly focused the header Help button through accessibility,
then sent Space. Focus readback remained on Help and the expected help title
was still absent from the queried accessible nodes. Tab then moved focus to
Advanced Tools. Sending Shift+Tab next instead read back Notifications, the
following header control, not Help. This sequence proves focus can move under
background keyboard delivery, but does not establish reverse-tab correctness:
modifier delivery and application routing have not been distinguished.
Do not label the latter observation an application focus-order bug without
independent input-delivery evidence. No settings or accounts were changed.

## Deployed-build checkpoint

The parent resumed this traversal on replacement process 14940, window
9569860, at the persisted 1200x800 size. Its application DLL SHA256 is
`66F93D1138E35F59A6D3F57C6328FFBD9B33DE46204B9985FC0E3390E909820E`.
An AX invocation of the enabled header help button again did not expose a
matching shortcuts title or close button in the subsequent targeted queries.
The screenshot was again unavailable to the current model interface.

The full AX tree contains the protocol-loading explanation, but presence in
that tree alone does not establish that `MappingLoadVisible` is true: modal
content uses opacity/hit-testing bindings and other hidden content is also
represented. Source `MainWindow.axaml.cs:749-753` guards quick navigation on
that property, whereas header help directly uses its toggle command. No
speculative change was made to either guard. Both traversal tasks remain open.

## Origin-order build keyboard checkpoint

On process 36248, window 7080826, the current 1200x800 capture again reaches
the tool but its image is omitted by the model interface. Visual fit remains
unverified. The exact enabled header button `Raccourcis clavier · F1` was
invoked through AX. Subsequent queries still expose only that header button,
not an enabled help-specific close action or shortcuts title. Source inspection
confirms a direct toggle and an IsVisible binding; it does not establish a
runtime binding defect. No UI source was changed on that evidence.

After Escape, focusing Help and sending Tab moves focus to Advanced Tools.
Sending Shift+Tab from there reads back Notifications instead of Help, matching
the earlier build observation. Input modifier delivery remains unproven, so
this is not classified as an application reverse-tab defect.

The Accounts navigation button opens the page with its enabled named
`Rechercher des comptes` field. Focusing that field and sending Tab moves
focus to `Filtrer les comptes par état`, an enabled combo box. This is a fresh
keyboard transition on the current build. No filter value, account, script or
setting was changed. Windows exposes the search role as `textfield`, not
`textbox`. Both complete QA tasks remain open; these observations cover only
the stated transitions and not all page states or rendered layout.

## Continued account-filter keyboard traversal

On the same process/window, fresh accessibility inspection confirms the state
filter is focused. Three successive Tab actions, each followed by focused-element
readback, reach `Filtrer les comptes par serveur`,
`Trier les comptes sans modifier leur sélection`, then the first account's
`Sélectionner ce compte` checkbox. All are enabled and report focused=true.
No selection, sorting value, server filter or account action was activated.
This extends the verified forward sequence from account search through the
three filters to account selection. Reverse traversal and the rest of the page
remain unverified. A fresh 1200x800 screenshot was again omitted by the model
interface, so it adds no visual-layout acceptance evidence.

## Account-row traversal and unnamed connection controls

Continuing twelve Tab transitions from the first row selection reaches its
copy-login, stop, settings and delete buttons, then the next row selection,
copy-login, ban-marker action, two combo boxes, MITM launch, Socket launch
and settings. None of those actions was activated. The two combo boxes report
enabled=true but no accessible title. Source inspection identifies them as
connection type and assigned proxy; neither had AutomationProperties.Name.
AccountsView.axaml now gives them the existing translated ConnType and
AssignProxy resources as explicit accessible names. No binding, choice or
connection behavior changed. Isolated compilation is subscribed as bash_174.
This change has not been deployed or rechecked on the real accessibility tree;
the broad keyboard and compact tasks remain open.

The isolated application build bash_174 completed with exit 0 and reports
`La génération a réussi.` Existing compiler warnings remain, including CS8826
on HdvListingRow.OnFetchErrorChanged; none was suppressed. Both resource keys
exist in the French, English and Spanish dictionaries. Existing AccountsFilter,
AccountsKeyboardAccess and AccountModalKeyboard tests are running as bash_175
against this artifact directory. Their result is pending and will not establish
native live accessibility names without deployment and a fresh AX readback.

The existing UI regression run bash_175 completed with exit 0: 38 passed,
zero failed, zero skipped, 59 seconds. This covers the selected account filter,
keyboard-access and modal-keyboard fixtures against the corrected compiled
XAML. It does not replace the outstanding native accessibility readback or
visual traversal. No account process was restarted for this verification.

Full UI verification on the same compiled artifact is subscribed as bash_176,
using --no-build --no-restore without a test filter. This includes the existing
16-route x two-size x two-theme compact matrix, tab and scroll assertions,
and the remaining keyboard fixtures. Results are pending; rendered visual
review remains distinct from these machine assertions.

A fresh control readback during the run confirms Bastionbot remains in game,
outside combat with her script running and 3063/3063 life. Sylvanbot remains in
game, outside combat, with the bounded mining script stopped and 2429/2429
life. These are state observations only; no account action was requested.

The full UI run bash_176 completed with exit 0: 213 passed, zero failed,
zero skipped, 4 minutes 3 seconds. This is the complete existing UI suite on
the corrected compiled artifact, including compact route, tab, scroll and
keyboard fixtures. Native accessibility readback of the changed controls and
human-visible layout acceptance remain outstanding; neither broad QA task is
closed by the headless suite alone.

Further native forward traversal on the unchanged live build reaches the
second row delete action, another row's selection/copy/stop/settings/delete,
then the following row selection/copy and its two unnamed combo boxes. None
was activated. This confirms the missing names recur across offline rows,
consistent with the shared row template corrected in source; it does not
verify that correction in the older live artifact.

## Isolated native-readback instance

The corrected Release artifact from givre-packetbus-evidence-20261106 was
launched as PID10960/window3671766 (`bash_185`, exit 0), with run name
givre-label-qa-20261106 and explicit separate pipe Givre-label-qa-20261106.
Its only seeded account is disabled, contains no credentials and has script
autostart disabled. The live PID36248/window7080826 remains separate. Initial
AX inspection confirms the QA window exists at 1460x920; the targeted Accounts
navigation query is pending. No corrected native-name acceptance is claimed yet.

The first Accounts query timed out; a bounded AX read then revealed first-run
language selection and map preparation. Selecting French disabled the language
overlay, and the map child later exited code0 with 17623/17623 reported. The
Accounts button was reached through its actual ancestor subtree. AX press and
then a native click both left Accounts search absent and only disabled hidden
combo boxes exposed. This is not acceptance of the new labels or proof of a
binding defect: protocol-loading content was still represented, and its actual
visibility was not independently established. No repeated blind clicks followed.
The owned QA instance was closed with Alt+F4 and absence of PID10960's window
was confirmed; live PID36248 and the three pre-existing instances remain.
The no-credentials profile is retained as local evidence. A future readback
needs a ready native session or a diagnosed navigation-delivery path, not another
claim based only on source or headless tests.

## Teams page and console keyboard checkpoint

On live PID36248/window7080826 at 1200x800, Accounts navigation again exposes
its enabled account-search field. Invoking Team exposes the Team heading,
subtitle, New Team action and shared console heading. Invoking the header
Back action returns to Accounts with its enabled search field; Team was then
opened again. No team was created or modified.

The New Team button was located through the actual ancestors of its text,
then focused without invocation. Two Tab transitions, each followed by native
focused-element readback, reach enabled `Copier` then `Vider` buttons in the
team console. Neither action was activated. This verifies that forward-focus
segment, not populated team cards or their collapsed controls.

A fresh screenshot returned a 1200x800 frame, but the image remains omitted
by the model interface. AX also reports the protocol-loading text as enabled
and onscreen even though modal visibility uses opacity/class bindings.
That property alone is not accepted as evidence of visible loading or overlap.
Visual compact acceptance and the complete keyboard task remain open.

## Settings suffix-format accessible name

Native inspection of live PID36248 found an enabled ComboBox with an empty
Name and HelpText. Its actual parent contains the native-mod health section,
the enriched-item-tooltip toggle and the adjacent `Format du suffixe` text.
Source inspection identifies SettingsView.ItemOverlay.cs as its creator.
The control now receives that existing visible label as its explicit automation
name. No selection, save handler or preference value was changed.

This is a confirmed accessible-name defect, not a clipping finding: the AX
rectangle is below the window viewport while IsOffscreen still reports false.
No visual conclusion is drawn from that inconsistent native property.
The Settings keyboard compilation/test bash_189 completed with exit0: two
passed, zero failed or skipped, 20.8 seconds reported test duration. The
build reported 231 warnings, not suppressed. These existing forward/reverse
button-traversal cases establish no new navigation regression; they do not
assert the ComboBox name. Updated native-name readback requires the new
artifact and remains outstanding.
