# Expedition panel overflow

User clarified the broken UI is the Expedition panel extending beyond the
screen. This is distinct from the unresolved classic Settings navigation.

Parent inspected shipped BotsView.axaml:outer Expedition ScrollViewer permits
horizontal scrolling but disables vertical scrolling. Its Grid has columns300,*
and MinWidth660. The left column is an unscrolled StackPanel including zone
selectors and expanded route preview. Only the right column owns a vertical
ScrollViewer. Thus extra left-column content can exceed available height
without a vertical path to its controls. Existing inactive compact route tests
do not populate these controls and cannot establish this state is accessible.

Assigned bounded fix/regression to st_01a1aff7,restricted to Expedition XAML
region and ExpeditionLayoutTests.cs. Required populated long labels,expanded
route and reachable actions at720x640/1200x800,using shipped controls without
starting services. Agent completion and parent execution pending. No visual
PASS claimed:the model interface has omitted native screenshot images.

Delivery reviewed by parent:vertical scrolling enabled,horizontal disabled,
minimum width removed;container query stacks columns below640px and keeps
300px settings alongside details above that width. Start/Stop now wrap.
Populated shipped-view regression passed2/2 at720x640 and1200x800,
exit0,including expanded route,nonoverlap,horizontal bounds and vertical
reachability of controls. Build reported233 warnings;not a warning-free claim.
Updated CompactRouteCaptureTests to stop requiring horizontal scrolling for
the now-responsive Expedition tab;other tabs retain their existing checks.
Full UI suite started after that change,result pending. No native acceptance
or causal-red run yet;no account starts or live deployment from these tests.

Full UI suite completed:218 passed,0 failed,0 skipped,exit0,301.1seconds;
build7 warnings. Receipt expedition-layout-full.trx. This covers the existing
compact/keyboard routes as well as the new populated expedition checks,not
dungeon-mode or Snow-skin populated acceptance. Preparing a separate native
QA profile;live PID7132 remains untouched.

Native QA PID3680/window4653826 started using the separate empty profile.
Map extraction completed17623/17623,child exit0. Selected French and opened
quick search through AX;the Bots > Expedition command was available and
invoked. With no account row,the page exposes Open Accounts rather than
the bot-specific Expedition controls. Therefore this run is startup/navigation
evidence only,not native populated-panel acceptance. Closed only this QA
window via its enabled chrome Close button. No account imported or connected.
Combined main suite started against the same artifact root,result pending.

Combined validation completed4815 passed,0 failed,2 skipped. Guarded live
deployment replaced7132 with15304/window6882008,using the preserved profile.
Application DLL SHA256=36A9F4EAB4AB292A7E5BEE168D0660591F19CB072B655DF1B0BEAA01B5A6F368.
Bastionbot and Sylvanbot reconnected on Mikhal. Runtime pseudo-PIDs reversed again:
Bastionbot1073741825,Sylvanbot1073741827;always address by account ID.
Native AX selection of Bastionbot and Expedition exposes restored Ile de Pandala,
23 selected maps,groups1-2,gather,bank and HDV settings. Start action not yet
reached;no expedition regenerated. Native screenshot omitted by interface.
Therefore live acceptance remains incomplete despite passing headless bounds.

Native AX subsequently reached ExpeditionStartButton via ancestor traversal
and ScrollIntoView. Runtime attributes:enabled,true;IsOffscreen,false;
bounds727,980,156,36 within1920x1032window. Start/Stop both exist. This
establishes native desktop reachability,not screenshot-based visual quality.
Before invoking Start,a fresh read showed selection had changed to Shadowbot183
and roster now included Shadowbot in game. Parent did not connect that account or
press Start. Do not launch against a stale selected-character assumption;
external UI/session activity has changed the selected bot during inspection.
