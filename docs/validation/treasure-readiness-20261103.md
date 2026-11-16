# Treasure launch prerequisites

The automatic-hunt button previously exposed only a disabled state. The
view-model now derives both CanStartAutoHunt and its visible explanation
from AutoHuntReadyState: no character, no observed map, ongoing travel,
missing launcher, running script, or ready to request a launch. Ready does
not promise permission or character-level admission; those checks remain at
the actual start boundary. Existing launch and stop behavior is unchanged.

The explanation uses the existing wrapping TextBlock treatment immediately
above the automatic-hunt controls. No new container, color or icon is added.
Existing step, status, level, retry and Fouduglen controls remain intact.

Release --no-restore tests filtered to TreasureRunControlTests,
TreasureNavigationRegressionTests and TreasureHuntStopTests passed 23 cases,
zero failed or ignored, exit 0, duration 14.7 seconds. Six table rows assert
the typed reason and the actual command gate, without pinning prose. The
previous unmodified test set passed 17 cases in 1.7 seconds. The new reason
table subsequently detected a deliberate mutation admitting an unknown map
as Ready: expected NoMap, actual Ready; six cases, one failed and five
passed, exit 1, duration 23.0 seconds. The original NoMap branch was restored
immediately afterwards; the restoration run passed all 23 cases, zero
failed or ignored, exit 0, duration 5.1 seconds.

An isolated current-build application was opened without accounts or a game
client (PID 6428, window 6621008). AX identified the language chooser,
background extraction control and local navigation search. French and
background extraction were selected, then the search opened. Typing into
its focused field returned `accessibility is unavailable on this backend`.
The owned process tree was stopped using bash_659. The changed treasure
panel was not reached. No render, accessibility, contrast or compact-layout
PASS is claimed; real-surface verification and the todo remain open.

C# language-server diagnostics remain unavailable because workspace scanning
throws UnauthorizedAccessException in artifacts/kourial-tools/pypdf. The
successful test run compiled the current C# and XAML bindings instead.

## Stale server step during clue resolution

The existing character-switch regression was extended with a new server
hunt sequence arriving while the real view-model awaited its resolver.
The resolver deliberately ignores cancellation and returns an old target.
Before correction that case started one trip instead of zero; two cases,
one failed, exit 1, duration 3.4 seconds. Trip ownership now also requires
the current decoded hunt sequence to equal the sequence that was resolved.
This rejects stale departure and stale completion/status updates without
changing the server step or launching a replacement trip automatically.
The combined hunt regression run passed 24 cases, zero failed or ignored,
exit 0, duration 7.6 seconds. This does not claim cancellation
of a route already executing when a later server step arrives.

The navigation table was then extended to four cases: character/sequence
replacement during resolution/travel. Both sequence-replacement cases lacked
the cancellation signal (expected true, actual false); two failed and two
passed, exit 1, duration 3.0 seconds. Step trips now retain their hunt
sequence until cleanup. Refresh cancels such a trip on a new sequence;
character clearing removes that sequence, and a chest trip explicitly has
none. This supplies cooperative cancellation to the traveler, while the
sequence guard still rejects results from a traveler ignoring cancellation.
The expanded combined regression run passed 26 cases, zero failed or ignored,
exit 0, duration 7.3 seconds. Physical stopping still
depends on the travel implementation honoring its cancellation token.

The chest-trip integration fixture now additionally delivers a server hunt
update while travel is suspended. It requires the chest token to remain
uncancelled, the script to start on arrival and the new server clue to stay
displayed. This distinguishes chest navigation from stale clue navigation;
the combined run passed 27 cases, zero failed or ignored, exit 0, duration
2.8 seconds.

The real binding in BotsViewModel.cs forwards the same token to
GivreLuaHost.TravelToMapAsync and disposes the transient host afterwards.
TravelToMapAsync passes it through Task.Run and RunTravelAction, which links
it into ScriptArrivalScope and cancels owned arrival actions in finally.
GoToMapIdCore checks ThrowIfStopping before map lookup or route planning;
ChangeMap and GoToCell also check it at their action boundaries. This is
source evidence for cooperative cancellation, not an observed physical stop
or proof that every transport implementation cancels a command already sent.

## Chest completion preserves a newer server clue

When a hunt already existed before chest travel, successful completion
unconditionally restored the captured old step. A suspended-trip fixture
received a newer direction step and reproduced old `Bosquet` replacing it:
two cases, one failed, exit 1, duration 2.4 seconds. Restoration now requires
the captured hunt sequence still to be current on both success and failure.
The table covers all four combinations of new server step and arrival
success/failure. The combined regression run passed 30 cases, zero failed
or ignored, exit 0, duration 8.2 seconds.

## A newly running script owns movement

The navigation race table also covers a script taking control during clue
resolution or travel. Both takeover cases initially lacked cancellation:
six cases, two failed and four passed, exit 1, duration 2.9 seconds.
Refresh now cancels an active step trip when ScriptRunning becomes true.
The continuation ownership check independently rechecks that no script is
running before starting travel or reporting its result. Chest travel is not
classified as a step trip. The combined regression run passed 32 cases,
zero failed or ignored, exit 0, duration 7.2 seconds.
