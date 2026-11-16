# Compact UI parent observation

Status: incomplete; no visual acceptance.

## Additional compact-review attempt

The read-only visual-engineering track st_01a1ab8c attempted a direct Read of
accounts-720x640-keyboard-forward-12-dark.png. Its model also received
"Current model does not support images". It produced no visual verdict and
stopped rather than treating OCR or metadata as visual evidence. Changing the
reviewer category did not resolve the missing-pixel capability.

Its directory inventory reports 688 PNGs, including 576 route captures and
all twelve current Accounts keyboard matrix captures. These counts and the
reported timestamp comparisons are child-reported inventory, not independent
parent visual acceptance. The child did not read the two capture test files;
its coverage reconstruction is therefore incomplete. No clipping, contrast,
overlap or focus-visibility PASS follows from that attempt. Full-shell and
actual light-theme checks remain separate missing evidence.

The parent extended the existing Bots keyboard traversal assertion to include
the first focused field and both viewport axes for every reached field. The
previous assertion checked only X and skipped the first field. This protects
against vertically offscreen focus without adding a duplicate fixture or any
production test seam. Fresh automatic LSP diagnostics timed out. The targeted
build/test is registered as mon_N4HHDGK761DBP99H / bash_73; its result is pending.

## Accounts keyboard continuation

AccountsKeyboardAccessTests now exercises the actual AccountsView at 720x640
with real Tab and Shift+Tab input, checks that every eligible TextBox is reached
and remains inside both viewport axes, and captures the end state. It attaches
no view model and activates no button, so account creation, stores and transport
commands are not exercised. The account-search/filter populated state is not
covered by this entry-form-only fixture.

The first run, mon_2VW7G02XWW284VYW / bash_58, exited 1 at compilation:
CS1061 on TextBox.TranslatePoint because the new test omitted `using Avalonia;`.
The earlier error-diagnostics request had returned no diagnostics and did not
detect this compilation error. This was a test-authoring error, not evidence
of a product keyboard defect. The missing import is now supplied, matching the
existing Settings keyboard test. The corrected run,
mon_SB2ASM1T4S9BPPWW / bash_59, passed 2/2 with zero failed or skipped,
exit 0 (27.6 s), using givre-ui-audit-20261105 artifacts.
This proves traversal in the inactive entry-form fixture, not visual acceptance.

The integrated UI suite then passed 122/122, zero failed or skipped, exit 0
(158.6 s), mon_GE9ES1FXV8QD8MQX / bash_60. This predates the populated
Accounts extension below.

The same keyboard theory now adds both directions with one offline AccountRow
and the existing AccountsFilterTests startup-free view-model fixture. It
explicitly requires the search field to be present, preventing an accidentally
hidden filter from yielding a vacuous traversal pass. No button is activated.
The shared fixture is now internal to the test assembly; production code is
unchanged. Fresh error diagnostics for both test files returned no diagnostics.
The four traversal cases and existing filter cases passed 22/22, zero failed
or skipped, exit 0 (16.1 s), mon_4XTM0217EZKXBKWK / bash_61.
This includes actual keyboard access to the populated search field; account
launch commands, full-shell focus and visual appearance remain unverified.

The expanded TextBox/ComboBox traversal passed the same 22-case combined filter,
zero failed or skipped, exit 0 (20.2 s), mon_F3905ZYPJRYNR3DE / bash_62.
A direct Read of accounts-720x640-keyboard-forward-populated.png again omitted
pixels because the current model does not support images; no visual verdict
follows. The theory now also includes eligible Buttons and CheckBoxes, keeping
navigation-only input: Enter, Space and clicks are never sent.

The resulting full-suite run failed 2/124 (122 passed, zero skipped), exit 1,
146.8 s, mon_8YEBMNVJ6HBAT98V / bash_63. Both populated Accounts traversals
focused a control below the viewport: Y=711 forward and Y=759 reverse, at
the unchanged vertical visibility assertion. The inactive fixture still passed.
AccountsView previously only initialized XAML; it now bubbles GotFocus to
Control.BringIntoView so the existing ScrollViewer handles visibility for
buttons and checkboxes as well as text inputs. No command is activated by this
handler. Error diagnostics returned none. The unchanged four-case regression
is running as mon_TACCFX8QKFDD7TFH / bash_64; correction is not yet proven.

Correction to the interpretation above: bash_64 again failed both populated
cases. Its detailed range was 0..680 and the assertion is line 67, the X check,
not Y. The failing positions 711 and 759 are horizontal. The generic
BringIntoView handler did not fix this and was removed. The account action
row has fixed columns totaling 752 px inside the compact card; its outer
ScrollViewer disables horizontal scrolling. The fixed row is now inside its
own horizontally scrollable viewport. AccountsView uses the same horizontal
focus-offset calculation already used by BotsView to reveal the focused
control. This preserves fixed action widths and does not activate commands.
Fresh code-behind error diagnostics returned none; the unchanged four cases
passed 4/4, zero failed or skipped, exit 0 (23.7 s),
mon_438A8ANJ6177M410 / bash_65. This closes the reproduced horizontal
keyboard-access failure in the real AccountsView fixture, not full-shell or
visual acceptance. Full UI integration passed 124/124, zero failed or skipped,
exit 0 (133.0 s), mon_PCGBQA08EQW9B87G / bash_66 against the same built
artifacts. No further UI rerun is required for this unchanged increment.

The Accounts theory was subsequently expanded to 0, 1 and 12 offline cards,
both tab directions and both requested host themes at 720x640. Its 12 cases
passed with zero failed or skipped, exit 0 (73.2 s),
mon_DD6RG3E6MSYGAAT2 / bash_68. This exercises combined vertical list scrolling
and per-card horizontal action scrolling, with every eligible input reached
and checked against both viewport axes. No command is activated. These cases
supersede the prior four-case theory; the earlier 124-case integrated run
predates this test-only matrix extension. Visual appearance remains unverified.

## Current real-window boundary

A fresh Windows desktop capability check reports capture, AX and input granted,
global stop armed, screen unlocked. Window enumeration again finds PID 14216,
GivreClone v1.3.3.11, window 329234, at 1920x1032. Fresh AX observation reaches
the navigation and numerous retained modal trees. Their presence does not prove
visibility; no modal confirmation or account command was invoked. This remains
the earlier running binary and does not validate the newly built Accounts fix.
No additional image-based acceptance is inferred from accessibility text.

## Real-window observation after journal R3

Computer capabilities on this Windows host report capture/input/AX granted,
global stop available and screen unlocked. The existing process PID 14216 was
observed, not relaunched: GivreClone v1.3.3.11, 1920x1032. It runs the earlier
givre-journal-final-20261105 output, not the newly compiled R3 binary. No UI
change in R3 is claimed from this observation.

The window AX tree exposes navigation, dashboard counters, and many retained
modal control trees. Thus the presence of a modal's AX nodes alone does not
prove it is visible. A screenshot request returned frame-2 at 1920x1032 but
explicitly omitted pixels because the model does not support images. This is
still no visual acceptance. Activating the search navigation control was
attempted once through AX; the subsequent AX read did not establish an opened
search field. No setting, account, network command or modal confirmation was
activated; no blind retry or guessed coordinate click followed.

The live dashboard reports zero tracked sessions and an available navigation
surface. Populated-state, compact-window, contrast and modal-focus acceptance
remain open. The previous offline 120-case suite remains the keyboard/layout
evidence; this full-size AX observation does not close the compact QA task.

The current capture and keyboard harness was read before running it. It mounts
production controls with shipped theme resources, Fluent control styles and
offline fixtures. TestApp never starts production App, MainViewModel, account
stores or transport services. The route matrix enumerates 13 routes, two sizes
(720x640 and 1200x800) and two requested host themes, with recursive tab and
scroll-end captures. This covers 52 route cases, not the full live shell.

Fresh captures and keyboard traversal tests are running under monitor
`mon_26FZZHHR9MP49589`, artifacts `givre-ui-audit-20261105` in local Temp.
Generated images must be inspected before claiming contrast, overlap or visual
focus correctness. Requested light host does not imply the app's fixed dark
resources become a fully light application theme.

That fresh run completed: 61 passed, zero failed or skipped, exit 0 (162.2 s).
This is the 52-case capture matrix plus the existing nine keyboard cases, not
the entire UI suite or visual acceptance.

## Map keyboard finding

IsoMapControl.OnKeyDown consumes Tab and Shift+Tab as next/previous inspection
actions even when no inspector exists; the Bots handler returns immediately
when there is no selected bot. With an inspector, both directions still cycle
entities instead of leaving the map. Arrow keys already provide next/previous
inspection. A new four-case production-control UI regression checks Tab leaves
the focused map to the neighboring field in each direction, with and without
an inspector subscription. It asserts focus and absence of an inspection action,
not source text or UI prose. The pre-fix run is monitored as
`mon_D4G5NQC9W5898VWX`. No production keyboard behavior has changed yet.

The pre-fix run failed all four cases: focus remained on IsoMapControl rather
than moving to the neighboring TextBox, exit 1. The control now leaves Tab
and Shift+Tab to normal focus navigation; arrows retain entity inspection and
Escape retains cancellation. The adjacent BotsView comment was corrected to
describe arrows rather than Tab. Five control cases assert each existing
arrow/Escape action is delivered once while focus remains on the map.
The expanded keyboard suite is running under `mon_BPBPN18JKQ1QYZ30`.
Fresh LSP error diagnostics for the control, its Bots handler and the test file
returned no diagnostics after the initial automatic diagnostic requests timed
out. No warning was suppressed.

The expanded suite first failed to compile: two CS7036 errors in the new
arrow/Escape control cases because the headless key APIs require physicalKey
and keySymbol arguments. The test calls now provide those arguments explicitly,
matching the neighboring tests. This was a test-authoring error, not a product
failure. Fresh LSP error diagnostics remained clear; execution is now monitored
as `mon_Y0CFYWCN9YK4M28N`. No passing result is claimed from the failed build.

The corrected keyboard suite passed 18/18, zero failed or skipped, exit 0
(83.6 s). This includes four Tab escape cases, five arrow/Escape action cases,
the plain-field witness and eight forward/reverse compact Bots traversals.
The complete UI suite is now running as `mon_CYWM9TKZC1Z07YB0` after the last
source edit, regenerating capture evidence. There are 576 PNG artifacts in its
TestResults directory at enumeration time; this includes additional existing
UI suites, not 576 independently reviewed states.

The parent attempted a direct Read of the freshly generated compact Bots map
capture `route-bots-720x640-dark-tabs-0-5.png`. Read explicitly returned
`Current model does not support images` and omitted the image. Visual contrast,
overlap and focus appearance remain unverified; no extracted-text substitute
was used to claim a visual PASS. The runtime keyboard focus assertions above
remain valid independently of that visual evidence limitation.

The complete UI suite after the final keyboard edit passed **110/110**, zero
failed or skipped, exit 0 (198.3 s). This supersedes the earlier integrated
101-case result, retaining its existing coverage and adding nine map keyboard
cases. It closes the reproduced map Tab trap at the actual control boundary,
not the full-shell traversal, visual inspection or live populated-state gaps.

## Additional route coverage

The existing compact-route matrix now includes the shipped DashboardView at
both viewport sizes and both host theme variants (four additional cases).
Its constructor only initializes XAML; no dashboard model or session is
attached. This deliberately exercises the inactive dashboard layout, not its
populated monitoring state. The expanded matrix is running under
`mon_PBZZF7X4704QV99W`; fresh error diagnostics returned no diagnostics.

ProxysView is not added to this offline matrix: its visual-tree attachment
calls the relay's RequestExitsAsync, so simply mounting it is not a local-only
fixture. MainWindow also attaches production stores and startup services.
Those shell/proxy gaps require an isolated real-surface setup rather than
claiming that standalone panel captures cover them. SettingsView additionally
reads the configured remote token into a text field, so indiscriminate capture
of production preferences would expose secrets. These findings are not
evidence that those layouts pass or fail.

SettingsView can be mounted without activating its settings commands: its
constructor guards initialization handlers with `_ready`, and attachment only
subscribes to license permission changes. The route fixture now includes it,
clearing RemoteTokenBox and RemoteAddresses before showing the window or
capturing any frame. No toggle or command is activated; production preferences
are not edited. This removes the capture-secret prerequisite without claiming
to validate remote configuration. Fresh error diagnostics passed. The already
running dashboard matrix predates this edit; it cannot verify these four new
settings cases. A fresh execution is required after that run finishes.

The dashboard-expanded matrix completed 56/56, zero failed or skipped, exit 0
(98.9 s). The subsequent 60-case matrix including the expurgated settings view
is now monitored as `mon_7J1X0BXJPM1EN787`. No production input or account
operation is triggered by these fixtures.

The settings-expanded matrix completed 60/60, zero failed or skipped, exit 0
(82.9 s). All 15 inactive routes now pass both viewport sizes and both host
theme variants. This adds eight route cases to the previously green 110-case
full suite; the other 58 cases were not rerun after this fixture-only extension.
Full-shell modal/proxy navigation and populated live states remain open.

## Settings keyboard traversal

Added two real-input cases in SettingsKeyboardAccessTests: mount the shipped
SettingsView at 720x640 with remote token/address cleared before display, collect
its visible enabled focusable buttons, and traverse using Tab or Shift+Tab.
Each newly reached button must be fully inside the window on both axes after
layout; traversal ends at the first repeated focus element or a control-count
bound, never a timer or sleep. Buttons are not pressed, so preference and
external-link commands are not invoked. This can detect premature focus cycles
and missing focus-driven scrolling, unlike direct Focus calls on every button.
Fresh error diagnostics returned none. Execution is monitored as
`mon_102S36NQMDX72BM3`; result is pending.

The first build failed on a missing Avalonia namespace import for TranslatePoint;
that test-only import was added. Actual execution then failed both directions:
32 of 39 collected buttons were reached, zero passed, exit 1 (14.8 s).
This is not yet classified as a product defect: the visual tree also includes
internal template buttons that may intentionally not be tab stops. The assertion
now reports missing button types, names, tab-stop flags and template owners;
the unchanged traversal is rerunning as `mon_QY903W7WZBT5282S` to determine the
correct control boundary. No production settings behavior has been changed.

The diagnostic rerun identified all seven missing controls as ButtonSpinner
RepeatButton arrows with IsTabStop=false. The failure therefore came from
including non-tab-stop template parts in the expected set, not from a proven
navigation defect. The fixture now includes only controls whose existing
KeyboardNavigation.IsTabStop is true; traversal and viewport assertions are
unchanged. This is a correction of the newly authored test's contract, not a
weakening of an existing product regression. Actual focus still must reach
every eligible button in both directions. Fresh error diagnostics are clear;
the corrected contract is executing as `mon_JAM0K6JPJ55K5RDF`.

The corrected settings traversal passed both directions: 2/2, zero failed or
skipped, exit 0 (24.6 s). Every eligible button was reachable and within the
viewport when reached. This validates the inactive SettingsView traversal only;
it does not validate full-shell modal focus trapping or visible focus contrast.

## Integrated run and disk recovery

The fresh-directory integrated run mon_3P7K45XYBHJ1ZWS6 failed before tests:
CS0041/CS0016/MSB3491 explicitly reported insufficient disk space while writing
GivreClone.Engine debug output. Exit 127 also included a closed TextWriter
exception. This is not a test result or a product regression.

The parent measured and removed only five inactive parent-created Temp build
directories: givre-parent-audit-20261105 (180979125 bytes),
givre-parent-audit-final-20261105 (1400897567),
givre-parent-audit-combat-20261105 (786415461),
givre-parent-audit-release-20261105 (108036909), and the failed
givre-ui-audit-final-20261105 (3383553). Total measured removal: 2479712615
bytes. Reports, capture directory, clean parent acceptance output and every
givre-mulw-* directory were retained. No source or user data was removed.

The integrated UI run now uses the existing givre-ui-audit-20261105 artifacts
directory under mon_W64KCXR0WXZSXJA8, avoiding another duplicate dependency tree.
It performs the normal build before tests, not a no-build reuse of older tests.

The integrated run completed **120/120 passed**, zero failed or skipped, exit 0
(182.7 s). This includes all 60 compact-route cases, the corrected map keyboard
cases and both settings traversal directions together with the existing UI
suite. It supersedes the earlier 110-case integrated result for automated UI
coverage. Full-shell modal/proxy traversal, visible focus contrast and live
populated states are still not covered by this result.

## Pixel inspection track

Because parent Read omits image pixels, a separate read-only visual worker
st_01a1ab07 is examining eleven named existing captures of compact/desktop
settings, settings keyboard endpoints, dashboard horizontal access and the
Bots map. It must use actual Read pixels and report image incapability rather
than substitute text extraction. No file edit, app input or build is delegated.
Any eventual findings will be child-observed visual evidence, not a claim that
the parent saw the images or that every route and full shell were inspected.

## Existing native window

The native win32 backend reports capture, AX and input permissions granted.
The parent enumerated existing windows and found GivreClone v1.3.3.11,
pid 14216, window 329234, 1920x1032. No duplicate application or game was
launched. A read-only window screenshot was captured as frame-1, but the tool
explicitly returned `tool image omitted: model does not support images`.
The screenshot therefore supplies no visual judgment to this parent.

An AX read at depth 12 returned shell navigation, settings, account and overlay
nodes, with truncation after 419 nodes. It also returned language-choice,
Shield-code, notification and map-extraction nodes. Their presence in the AX
tree does not prove these overlays are currently visible or mutually active.
No account launch, Shield submission, confirmation, deletion or configuration
change was performed. A settings-button lookup succeeded; it was not activated.

## Open evidence

The full shell/overlay matrix, populated live account states, precise screenshot
inspection and complete keyboard return navigation remain open. Existing
geometry assertions and the new test execution cannot close those gaps alone.
The compact QA task remains pending until actual visible evidence can be read
and its full scope has been traversed.
