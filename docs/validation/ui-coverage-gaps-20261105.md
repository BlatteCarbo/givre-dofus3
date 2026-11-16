# Remaining compact and keyboard QA

## Proxies route coverage extension

The compact matrix now includes the real ProxysView alongside the previous
15 routes, at 720x640 and 1200x800, dark and light-host themes. Its attach
callback requests exits on the existing relay client; SendJsonAsync throws
when disconnected rather than starting a connection. No relay session is
started by this inactive capture.

Fresh isolated Release run: 64/64 passed, zero failed/skipped, exit 0,
123.7 seconds test time. Artifacts root:
`%USERPROFILE%/AppData/Local/Temp/givre-proxys-qa-20261105`, logger
`compact-proxys.trx`. Build reported 232 warnings, including existing partial
method signature warnings; this run is not a warning-free build. C# LSP still
crashes on access denied at artifacts/kourial-tools/pypdf. Captures and geometry
checks are not pixel review; full compact visual acceptance remains open.

## Independent pixel-review capability check

Follow-up on the same isolated non-elevated window 5048408/PID26436 verified
the native F1 shortcut itself: the help text "Afficher ou masquer cette aide"
appeared after F1 and disappeared after Escape. This used real window key
delivery and independent AX queries, rather than invoking the help button.
No account action occurred. This verifies only help open/close, not full-shell
traversal. A fresh read of the compact Accounts PNG still returned "Current
model does not support images"; no visual verdict was produced.

Native follow-up used the existing non-elevated isolated QA window 5048408,
PID 26436; elevated window 2949868 was left untouched. AX button invocation
and quick-search submission closed the search field, but the expected active
Accounts search/import controls were not subsequently observed: the visible
AX tree still exposed dashboard actions and disabled hidden fields. This is
inconclusive navigation evidence, not an Accounts keyboard pass or a confirmed
product failure. No account import, connection, launch or profile edit occurred.
The existing historical headless keyboard suite remains the applicable
deterministic evidence; this native attempt does not extend its acceptance.

Parent located 694 existing PNG artifacts under
%USERPROFILE%/AppData/Local/Temp/givre-parent-reload-ui-20261105/bin/GivreClone.UiTests/release/TestResults.
This count includes keyboard traces and is not a route matrix coverage count.
Reading route-accounts-720x640-dark-inactive.png omitted pixels for the parent.
Independent visual-engineering task st_01a1ad28 read the same file and received
the same "Current model does not support images" response. It stopped at that
capability check, without edits or a visual verdict. No clipping, overlap,
blank-content or theme acceptance can be inferred from either attempt.

## Fresh isolated GUI probe

The new Release GUI PID7800/window6424172, profile
parent-auto-pipe-20261105, completed language selection and quick-search
navigation to Accounts. No import, connection or account action was invoked.
Opening quick search again, focusing its real search field and sending eight
native Tab events reached Dashboard, Bots, Accounts, Teams, Proxies, Network,
Settings and My Account result buttons in that order. None of these steps
focused a disabled closed-overlay field.

A corresponding eight-event native Shift+Tab probe returned exactly the same
forward order. This repeats the previously recorded native modifier-delivery
ambiguity; it is not evidence that product reverse traversal is correct or
broken. No navigation code was changed from this probe. Escape was sent at
the end; closure has not yet been independently observed. The native run
helper required awaiting desktop.window before using its window methods;
the first unawaited attempt failed before any input. Compact visual
inspection remains unavailable because screenshot pixels are omitted by the
current transport; frame geometry or accessibility text is not a visual pass.

Follow-up native observation confirmed quick search was closed: its search
field no longer appeared in the target window. Opening the header's keyboard
help button exposed the real "Afficher ou masquer cette aide" text; Escape
then removed that node from the next window query. This verifies both overlay
closures on the new isolated GUI without invoking any account action. It
does not resolve reverse modifier delivery or establish full keyboard and
compact visual acceptance.

## Current-source reconciliation

Parent inspection after the209-case UI acceptance found the historical map-Tab
concern below already addressed in current source: IsoMapControl.OnKeyDown has
no Tab action, and BotsKeyboardAccessTests.Tab_leaves_the_map_in_both_directions_even_with_an_inspector
covers both directions with/without an inspector. Its neighboring five key
cases retain arrow/Escape inspection behavior. Do not reimplement or duplicate
that fix based on the older chronological finding. Full native reverse-shell
and visual/compact acceptance remain separate unresolved scopes.

## Verified boundary

Current integrated UI suite: 101/101 passed, zero skipped, exit 0 (1 minute
30 seconds), after the deferred-focus correction and bidirectional traversal
tests. This supersedes the earlier 66-case integrated count below. Screenshot
generation and geometry checks do not constitute visual inspection or live
account validation; the remaining shell and overlay coverage gaps stay open.

Latest integrated focus correction run passed 66/66, zero skipped, exit 0
(1 minute 6 seconds). The compact matrix now enumerates both requested host
themes for every route/size pair (52 cases passed, exit 0, 1 minute 6 seconds), with distinct `dark` and
`light-host` capture prefixes. Light host means requested Fluent theme only;
hard-coded dark application resources are not silently claimed to become light.
BotsKeyboardAccessTests now sends real Tab key events through a complete
observed focus cycle in each corrected Bots panel, requiring all initially
visible/enabled text fields to be reached and fit the window horizontally.
This four-case run passed 3/4; Expedition tab traversal left a numeric field at
x=653, width 82 (15px clipped), despite direct-focus checks passing. The focus
handler now reapplies horizontal reveal at the dispatcher Loaded boundary only
if that field or its descendant still owns focus, after the control's own focus
handling. No fixed sleep or polling is used. Revalidation passed 4/4, exit 0
(28 seconds). The same cycle now runs in both directions (8 cases, pending),
with separate forward/reverse capture names. The bidirectional run passed 4/8:
all forward cases passed, while reverse cycles reached only their initial field
(1/6 Expedition, 1/2 Trajets, 1/5 Carte, 1/3 Inventaire). This is an unresolved
navigation finding, not a result to weaken into a forward-only claim. The next
run records actual focus identities before deciding whether the fixture leaves
the selected tab or the production UI traps reverse focus. The trace repeats
the predecessor control (ComboBox, RouteMapControl, IsoMapControl, or expander
header) on the second reverse key. This cross-control pattern may be a keyboard
fixture modifier-state issue. A follow-up explicitly presses/releases physical
LeftShift around Tab before changing production navigation. That follow-up
also failed the same four reverse cases (4/8 passed). A three-plain-TextBox
control case now checks two successive reverse transitions without BotsView;
this isolates keyboard injection behavior from any production handler. The
plain-field witness passed 1/1, exit 0. The previous reverse test started at
the first form field and expected the entire shell to wrap back through every
field; that cyclic navigation contract is not established. The reverse scenario
now starts at the last form field and still requires every enabled field to be
reached backwards. It stops only after reaching the complete target set, or
fails on a repeated focus/bounded traversal budget. This run passed 9/9,
zero skipped, exit 0 (32 seconds): four forward traversals, four backward
traversals, and the plain-field keyboard witness. This proves traversal between
the form endpoints, not cyclic shell navigation or escape from a focused map.
Separately,
IsoMapControl source consumes Tab even when its KeyAction callback does nothing;
that source observation is not yet a live-navigation diagnosis.
It uses inactive views and never activates an
action button. Reverse traversal and full-shell shortcuts remain separate gaps.

The integrated UI run after the expanded matrix passed 64/64, zero skipped,
exit 0 (38 seconds). A new geometry probe checks for horizontal extent beyond
viewport where horizontal scrolling is disabled in every captured state;
24/24 cases passed, exit 0. Reading a current compact craft capture again returned no
image content to this model, so visual inspection remains unverified.

MainWindow constructor inspection confirms global Stores initialization via
ItemCraftAccessService.AttachLiveStock(Stores.Launch), bridge loads on Opened,
configuration restoration/persistence and license handlers. It is not mounted
as a supposedly isolated fixture. BotsView is covered by an existing detached
timer lifecycle test; the compact matrix now adds its unbound tabs (26 cases,
24 passed, 2 failed) without a session model. At 720px a field began at x=653
although its rightmost valid origin was 638; its tab/name needs identification.
At 1200px an inner viewport height was 1186 against an 800px window; an inner
viewport inside an outer vertical scroller is not itself proof of clipping.
The probe now respects a scrolling ancestor for that height check and emits
the field/tab identity for horizontal failures. Diagnostic run passed 25/26:
the sole remaining failure is Bots top-level tab index 3 (Expedition), a
NumericUpDown template PART_TextBox at x=653 with width 82 in a 720px window.
Its immediate template labels are empty; expanded ancestor-width diagnostics
are running to identify the actual owner before any production layout edit.
Timer teardown remains owned by window close. Neither failure is closed yet.

Follow-up: Expedition's fixed 300px selector column plus parameter widths
exceeded the remaining Bots detail width. Its content now has a 660px minimum
inside an enabled horizontal scroller. The next run reached tab 4 (Trajets),
proving the earlier Expedition field gate was passed, then failed a 382px field
at x=351 in the 720px window. Trajets reserved a fixed 390px settings column
beside the map and fixed 680px height without an outer scroller. It now has a
720px minimum inside a two-axis scroller. Revalidation of both changes is pending;
this is scroll accessibility, not a responsive reflow or a visual PASS.
The next run passed Expedition and Trajets and reached tab 5 (Carte), where a
numeric template field at x=695, width 42 exceeds 720px. Result 25/26, exit 1.
FitMapTab already changes the right-hand column to 250px below 1250px; source
inspection alone does not identify which nested control imposes the excess.
Scroll-container diagnostics show a 400px-wide ancestor with horizontal
scrolling disabled; the template field itself extends beyond its visible edge.
The map/inspector grid now has a 720px minimum inside an outer two-axis scroller,
preserving FitMapTab height/column behavior while allowing access at narrow
widths. Revalidation is pending; no visual PASS or live map-input claim is made.
That run reached tab 6 (Inventaire), passing the earlier map field gate, then
failed the inventory filter at x=720, width 200 in the 720px window (25/26).
The inventory's appearance card and fixed toolbar widths exceeded the detail
area without a horizontal path. Its grid now has a 1000px minimum inside a
two-axis scroller. This accessibility fix preserves the existing toolbar and
item actions; it is not a claim that the toolbar reflows. Current run pending.

The corrected matrix passed 26/26, exit 0 (40 seconds). Right-edge captures
exist for Bots tab paths 0-3, 0-4, 0-5 and 0-6. A follow-up explicitly requires
each of those states to exercise horizontal movement at 720px, rather than
merely accepting an enabled scrollbar. Inventory's outer vertical scrolling
is disabled so its existing grid/list viewport remains height-constrained;
only horizontal access is added there. The full UI regression run after this
adjustment passed 66/66, zero skipped, exit 0 (54 seconds). Visual inspection and populated live Bots states remain
unverified; the full compact task stays open.
The next probe calls BringIntoView on each field inside a horizontal scrolling
ancestor and then requires its actual bounds to fit the window. This is stronger
than accepting any enabled horizontal ancestor. It passed 25/26 and found that
Trajets' 382px field cannot fit a 330px outer viewport: BringIntoView left it at
x=339, ending at 721px. An enabled scroller alone did not establish access.
The settings column is reduced from 390px to 300px and total minimum from
720px to 630px, retaining 318px for the map. Revalidation passed the Trajets
field gate then failed a Carte field at x=849, width 242 (25/26, exit 1).
Before another production edit, the probe now forces UpdateLayout after the
BringIntoView request to distinguish unapplied geometry from inaccessible
content. Synchronous layout still failed the same Carte field (25/26, exit 1).
The next diagnostic records actual scroll offsets and ancestor Expander states
to distinguish an unreachable visible field from a retained collapsed template.
No collapsed-control exclusion was retained without that evidence.
The field has no Expander ancestor. BringIntoView left the outer horizontal
offset at 22 of 452, while the field remained at x=849. Automatic focus/bring
scrolling is therefore not proven. A separate manual-scroll accessibility probe
now computes the horizontal offset from the field's bounds and verifies the
resulting actual on-window bounds. This does not claim to fix BringIntoView;
its automatic behavior remains a keyboard QA finding. Manual probe passed
26/26, exit 0 (42 seconds): fields can be brought within actual window bounds
by explicit horizontal offsets. A separate reproduction now focuses enabled
Bots fields before any manual scrolling and requires on-window bounds. Its
result is pending; this distinguishes real focus behavior from an explicit
BringIntoView call. It reproduced a clipped focused Carte field at x=871,
width 242 (25/26 passed, exit 1). BotsView now handles the bubbling focus event
and adjusts only horizontally scrollable ancestors to expose the focused
control, retaining their vertical offset. Verification passed 26/26, exit 0
(40 seconds), including focus-before-manual-scroll assertions. The focused-field
defect is closed at that UI boundary; full UI integration is running. An initial
GotFocusEventArgs type name was rejected by diagnostics and replaced with the
actual routed-event argument contract before running the verification.

The full GivreClone.UiTests suite passed 40/40 before the latest keyboard
extensions. The extended TreasureReadinessTests passed 2/2 after the last edit,
at 1000x1000 and 720x640. It mounts the production TreasureView and checks
readiness, displayed resolver refusal without travel, latest decoded step,
simulated script-host stop/restart, Tab, Shift+Tab, Space to start, and Space
to stop. Start, stop, current-step and prerequisite bounds fit the window.

Captures reside in
`%LOCALAPPDATA%/Temp/givre-provider-ui-20261105/bin/GivreClone.UiTests/debug/TestResults/`.
The names are `treasure-{1000x1000,720x640}-{running,keyboard}.png`.
Their image content could not be inspected by the current model. Bounds checks
do not establish contrast, clipping, overlap or visible keyboard focus.

## Existing coverage is not full navigation coverage

- CompactRouteCaptureTests mounts inactive TeamsView, SocialView and
  AdaptersView at 720x640 and 1200x800 without data contexts or account sessions.
  The first run plus the hunt panel passed 8/8, exit 0. Route dimensions and
  visible scroll viewports fit the window. Captures use
  `route-{teams,social,adapters}-{720x640,1200x800}-inactive.png` in the same
  TestResults directory. These empty fixtures do not prove populated rows or
  live commands. A follow-up enumerating currently mounted tab controls is
  passed 6/6. The recursive follow-up also passed 6/6 and captures lazy nested
  Social profile/equipment tabs separately. Capture names now encode tab index
  paths rather than visual-tree depth; older depth-named captures are stale.
  The harnais also captures the end of actually scrollable visible regions.
  A subsequent fixture populates SocialViewModel with 40 profile rows and
  40 chat lines; its run passed 6/6, exit 0. No injection callback or transport
  is set. A follow-up asserts realization and on-window vertical bounds of the
  final list item after scrolling passed 6/6, exit 0. An additional coverage
  assertion requires two distinct social lists to execute that check, avoiding
  a conditional assertion passing without exercising either populated list;
  this last run passed 6/6, exit 0. Both distinct populated social lists exercised
  the final-item realization and vertical-bounds assertions at both sizes.
  Atlas and item-composition panels are now included as offline empty states;
  the expanded 10-case run passed 10/10, exit 0. Their constructors were inspected:
  they initialize local view models without account sessions or catalog queries.
  A subsequent run supplies 40 unverified fixture maps and 40 unverified fixture
  catalog entries to exercise populated states. It passed 8/10 and failed both
  Atlas sizes because the social-specific last-container assertion also matched
  an Atlas list with 40 items. This is insufficient evidence of a product bug:
  the Atlas list is nested inside another scroll container and virtualization
  need not realize its last item when that inner scroll is moved. The assertion
  is now explicitly scoped to Social; its two-list coverage requirement remains.
  Atlas scrolling and capture checks remain active. The corrected run passed
  10/10, exit 0. A further check moves enabled horizontal scroll containers to
  their right edge and requires actual horizontal movement for Atlas at 720px;
  that run passed 10/10, exit 0, with right-edge captures alongside other states.
  The matrix now includes ProfessionPlannerPanel with 40 unverified fixture
  recipes and MarketInsightsPanel without a data context. The expanded 14-case
  run passed 14/14, exit 0. No catalog provider, purchase or game transport is invoked.
  The next matrix adds CharacterCreationTabView, SoulSyncView and MyAccountView
  without data contexts (20 cases total, passed 20/20, exit 0). Their constructors only
  initialize XAML. These fixtures do not create a character, connect an account,
  synchronize files or exercise account-specific loaded states.
  AccountsView and PlanningEditor are added without data contexts in the next
  24-case run passed 24/24, exit 0. Constructor inspection shows XAML initialization and,
  for PlanningEditor, key/focus handler registration only. No script rename,
  import, schedule or account launch is triggered.
  Atlas
  declares MinWidth=825 inside an explicitly horizontally scrollable container,
  so 720px capture is a scrollable surface, not a claim of reflow to 720px.
- DashboardUxTests mounts a dashboard subscription card at two window sizes.
- NativeThemeTests checks the production equipment card and captures themes.
- SmithAdaptiveSelectionTests covers workshop and inventory tab states.
- SmithMagicViewTests and SmithCrushingPickerTests cover smithing surfaces.
- SocketPickerUxTests covers the socket picker.
- CombatVisualStatusTests checks the production comfort-card bindings.
- NavigationShortcutsTests checks history and localized shortcut declarations,
  not key dispatch in MainWindow.
- MZP0OperatorNavigationTests checks navigation logic and XAML structure without
  constructing MainWindow, whose constructor starts license/bootstrap services.

## Routes still needing a complete traversal

Enumerate shell navigation, scroll positions and open/closed overlays before
calling compact or keyboard QA complete. Production view inventory includes:

- MainWindow and Classic/ClassicShellBody.
- AccountsView, BotsView, TeamsView, ProxysView, DashboardView, SettingsView,
  MyAccountView, SocialView, SoulSyncView and AdaptersView.
- TreasureView, SmithMagicView, VisualModsPanel and WorldAtlasPanel.
- EcoToolsPanel, MarketInsightsPanel, ItemCraftPlannerPanel and
  ProfessionPlannerPanel.
- CharacterCreationTabView and SocketServerPickOverlay.
- AccountOverlays, CombatOverlays, CombatSimOverlay and LevelCapOverlay.
- HdvSellOverlay and HdvUidOperatorWindow.
- LuaEditorOverlays and NoCodeWizardOverlay.
- PlanningEditor, PlanningOverlays and PlanningDisconnectOverlay.
- ProxysOverlays and RoadRecorderOverlays.

This is a source-file inventory, not a claim that every file is an independently
reachable route. For each reachable state, establish its owner and entry action,
capture the rendered surface, then check forward/reverse focus, disabled-control
skipping, activation, Escape, scrolling and return navigation. Use inactive
fixtures; constructing the live shell must not implicitly launch accounts.

## Resolver boundary follow-up

The later Treasure suite passed 96/96. Production now calls
TreasureClueResolution, whose tests exercise HTTP failure, non-user timeout,
invalid response, missing local coordinates, successful resolution and caller
cancellation using controlled task outcomes. This closes the earlier branch
coverage gap, not real HTTP or live-game verification. The mounted UI still
proves display plus absence of travel separately.
