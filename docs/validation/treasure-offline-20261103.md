# Refresh allocation baseline - 2026-11-03

Release build and QA launch completed with exit 0 (PID 16488). A real
`get_app_status` response reported 95 refresh ticks, 5,832 allocated bytes
per tick and 2.843 ms per tick on average since process initialization.
`list_bots` confirmed five offline rows, all PID 0, no running scripts;
the application reported zero running supervisors and its process responded.

The existing 300 ms refresh now measures TickCore with Stopwatch timestamps
and GC.GetAllocatedBytesForCurrentThread; totals are exposed under `refresh`
in get_app_status. These are cumulative means, including initialization,
not percentiles or a steady-state-only sample. At the nominal frequency,
the allocation mean corresponds to about 19,440 bytes/second.

Scope: synchronous UI-thread allocations and elapsed refresh callback time.
This excludes allocations on worker threads, deferred layout/render work,
and the other timers. No live bots or combat traffic were exercised, and
these numbers do not prove loaded-session performance. No optimization or
performance threshold assertion is made. The C# language server remains
unavailable due to its existing access error under artifacts/kourial-tools;
the successful Release compilation supplies the compiler diagnostics.

# Log growth alert - 2026-11-03

TransportDiag now records each flushed batch in LogGrowthMonitor (one-minute
sliding window). The alert threshold equals one 25 MB segment per minute,
which would rotate the 20 retained sessions away in about 20 minutes.
get_app_status exposes transportLog.bytesPerMinute and growthAlert.

LogGrowthMonitorTests (threshold boundary, window expiry, normalization) and
the related filter: exit 0, 9 passed, zero failed or ignored.
The current Release build launched without Dofus (PID 20764) reported
bytesPerMinute=172 and growthAlert=false through the real control pipe.
No UI banner exists and no real burst was induced.

# Page opening time - 2026-11-03

`dotnet run --project tools/RecoveredUiHarness -c Release -- --page-timing`
constructs each compiled view six times in the real headless Avalonia app,
discards the first (cold) run and reports the other five (construct + jobs +
layout). Exit 0.

| View | Median | Max |
|---|---:|---:|
| BotsView | 474.1 ms | 660.5 ms |
| SettingsView | 203.6 ms | 220.8 ms |
| SmithMagicView | 131.4 ms | 139.7 ms |

BotsView is the outlier: its XAML is about 5,270 lines and every tab is
built eagerly. Headless figures exclude GPU rendering, data binding to live
bots and the real window; they are a relative baseline, not the on-screen
time. An attempt to time navigation in the live window through UI automation
exceeded its 120 s budget and produced no measurement. No optimization was
made here; lazy construction of inactive BotsView tabs is the follow-up.

# Treasure offline verification - 2026-11-03

Live client tests are paused at the user's request while the required Dofus
update is not available. No live hunt or server receipt is claimed here.

Command executed from the main worktree:

```text
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TreasureRunControlTests|FullyQualifiedName~TreasureHuntStopTests|FullyQualifiedName~TreasureScriptExecutionTests|FullyQualifiedName~TreasureNavigationRegressionTests|FullyQualifiedName~TreasureCluesTests" --nologo -v minimal
```

Result: exit 0, 50 passed, zero failed or ignored, test duration 6.7 seconds.
The first attempt could not copy dependencies while our QA application was
open (MSB3027/MSB3021); closing that owned window allowed the run to finish.

The existing tests exercise script-specific stopping without discarding the
decoded hunt, refusal/retry behavior, character switching, latest decoded
server step versus selected row, cancellation of a chest trip, and clue
parsing/matching. These are offline fixtures, not newly received server data.

Source inspection shows that TreasureViewModel.Refresh derives CurrentStepText
from the last decoded step and tracks finish/answer sequence numbers.
TreasureViewModel.Fouduglen requires a selected PID, a known map, a launch
callback, no competing script/trip/request, and valid count/budget/price.
Its launch refusal message groups rights, level and character availability;
this run does not prove each reason is explained separately in the UI.

TreasureClueService reads memory and disk cache before DofusDB. An uncached
network failure throws, whereas a successful response with no matching clue
reaches the explicit DofusDB-not-found message in BotsViewModel.TreasureMap.
Cache entries currently carry no freshness timestamp; this is a source
limitation, not evidence that a returned clue is current.

The four open treasure tasks remain open pending their full visible-surface
and server-observation requirements. Passing these tests alone does not
establish live availability, navigation, or stop/restart behavior.
