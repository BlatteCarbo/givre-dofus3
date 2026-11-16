# Round 6 Prompt 9 — local notifications

Implemented in GivreWork only. No original Mizan files/accounts, credential stores, live client, network, Git commit or push were used.

## Exact owned paths

- `GivreClone.App/Services/LocalNotificationPreferences.cs` (new): account-ID preferences, opt in, event mask, threshold, cooldown, source-generated JSON, atomic replacement; unreadable/invalid files are preserved and disable notification initialization.
- `GivreClone.App/Services/LocalBotNotifications.cs` (new): deterministic event decisions, per-account/per-event cooldown, full-pods edges, balance/session baselines, account-state cleanup.
- `GivreClone.App/Services/WindowsLocalToastDelivery.cs` (new): local Windows PowerShell/WinRT adapter, OS availability check, bounded process lifetime, explicit failure states.
- `GivreClone.App/ViewModels/BotsViewModel.LocalNotifications.cs` (new): event subscriptions, snapshot polling, Notifier history integration, delivery status, settings command.
- `GivreClone.App/ViewModels/BotsViewModel.Supervisors.cs`: only two added calls, initialization and polling after directory refresh. Existing singleton lifetime and supervisor timer are reused.
- `GivreClone.App/Views/LocalNotificationSettingsWindow.cs` (new): independent account settings window and explicit Windows availability check.
- `GivreClone.App/Views/BotsView.axaml`: one settings button bound to `ShowLocalNotificationSettingsCommand`.
- `tools/R6LocalNotificationsHarness/R6LocalNotificationsHarness.csproj`, `Program.cs`, `NuGet.Config` (new): package-free offline harness; NuGet sources cleared.
- `tools/R6LocalNotificationsHarness/windows-smoke.log`: actual Windows smoke results, one generic test toast submission.
- This document.

`MainViewModel.cs`, `MainWindow.axaml`, `AppConfig.cs`, `Notifier.cs`, `NotificationStore.cs` and `BotStatsStore.cs` were not edited by this work. Other workers' edits are preserved. The existing BotStatsViewModel already computes XP/h, kamas/h, resources/h and resource totals from BotStatsStore; no second statistics store/dashboard was introduced.

## Event wiring

| Event | Actual source | Behavior |
|---|---|---|
| Full pods | `LaunchService.PodsUpdated` | Known capacity, current >= maximum (100%), once until weight falls below maximum. |
| Death | `LaunchService.FighterKilled` | Nonzero dead actor equals `LaunchService.CharacterId(pid)`; another actor does not notify. |
| Win/loss | `LaunchService.FightOutcome` | Exactly one of won/lost must be true; unknown/ambiguous outcomes are ignored. |
| Disconnect | `LaunchService.GameLinkLost` plus account directory transition | No transport reason copied into a notification. Cooldown deduplicates the two sources. |
| Script end | account directory `ScriptRunning` true -> false | Label explicitly says stopped OR ended; manual cancellation is not reported as successful completion. |
| Kamas | account directory balance snapshots (~1 second) | Cumulative positive observed deltas, default threshold 1000. Unknown balances, changed PID/character or leaving the world reset the baseline. Spending is not a gain. |

Snapshots can miss an entire short script between timer ticks, or a disconnect/reconnect completed between ticks without a GameLinkLost signal. Kamas gains immediately spent between two samples cannot be recovered. No protocol field semantics were invented.

Preferences live in `MZP19LocalClone.DataRoot/local-notification-preferences.json`, keyed by the clone account's opaque model ID, not login or character name. Default off. Event cooldown defaults to 30 seconds and is independently configurable per account. Desktop text is generic and excludes account/character names, paths and raw error strings. Accepted notices pass through existing `Notifier.Push`, whose existing UI sink persists history via `NotificationStore`. History insertion is not desktop delivery evidence. The current settings window is for the selected running bot/account; offline-account access can be added later without changing the storage key.

## What Windows registration actually does

A user-clicked availability check, an enabled event, or the explicit harness option `--windows-smoke` invokes the adapter. It starts the system Windows PowerShell 5.1 executable hidden with `-NoProfile -NonInteractive -EncodedCommand`. No service/download/network is used. The script registers the application identity by creating/updating:

`HKCU\Software\Classes\AppUserModelId\GivreClone.Local`

with string `DisplayName=GivreClone`, then calls WinRT `ToastNotificationManager.CreateToastNotifier("GivreClone.Local")`. This is registry registration only: no Start Menu shortcut, COM activation handler, MSIX identity or click activation is installed. The actual Windows smoke below confirms availability and successful submission on this installation; it does not establish visible display or compatibility with other Windows installations.

The adapter reads `ToastNotifier.Setting` before any `Show`. A nonzero setting returns `Suppressed` and preserves its numeric value; the UI displays `Setting=N`. The explicit check returns `Available` only for Setting=0 and never constructs/sends a toast. Setting=0 does not prove that Focus Assist, session policy or the desktop actually displays a later toast.

On send, text is XML-escaped and transported into PowerShell as base64 data. `Show` returning normally maps to `Submitted`, never a displayed/seen acknowledgement. WinRT/registry/PowerShell failures map to `Failed`; OS rejection maps to `Suppressed`; execution is limited to eight seconds (`TimedOut`). Only one delivery process per adapter runs at once; bursts can produce `Busy` and drop a desktop toast while retaining its internal history. No fake balloon, UI success or external fallback is substituted.

## Final validation and status

Verified by this worker: the initial 24/24 behavioral checks pass, followed by 30/30 checks after adding the smoke gate tests. These include zero delivery calls after Suppressed, Failed, Unsupported, TimedOut or Busy availability, and exactly one delivery call after Available, preserving a later OS refusal. The independent window compiles against existing local Avalonia DLLs; targeted `git diff --check` passes. The earlier 24-check output is retained in `artifacts/R6LocalNotifications-validation.txt`; the later 30-check result is in the task execution output. Generated PowerShell fixtures in `artifacts/R6LocalNotifications/` were syntax-parsed only, never executed.

Run the isolated harness (does not build or start the app):

```powershell
dotnet run --project tools/R6LocalNotificationsHarness/R6LocalNotificationsHarness.csproj -p:RestoreConfigFile=%USERPROFILE%\Videos\GIVRE\GivreWork\tools\R6LocalNotificationsHarness\NuGet.Config -p:NuGetAudit=false -p:ValidateAvaloniaUi=true
```

`ValidateAvaloniaUi=true` additionally compiles the independent settings window against the existing local `GivreClone.App/bin/Debug/net10.0/Avalonia.Base.dll` and `Avalonia.Controls.dll`. This checks its C# API usage, without rendering it or compiling the full application. Omit that property when those prebuilt DLLs are unavailable.

### Actual Windows adapter smoke, 2026-10-18 UTC

Executed once, after compiling only the isolated harness:

```powershell
dotnet run --project tools/R6LocalNotificationsHarness/R6LocalNotificationsHarness.csproj --no-build --no-restore -- --windows-smoke
```

Exact retained evidence from `tools/R6LocalNotificationsHarness/windows-smoke.log`:

```text
2026-10-18T10:24:17.6262183+00:00 Availability=Available Setting=n/a
2026-10-18T10:24:17.6282861+00:00 SendCalls=1; sending one generic GivreWork test toast.
2026-10-18T10:24:19.2863219+00:00 Delivery=Submitted Setting=n/a; displayed=unverified
WINDOWS_SMOKE SendCalls=1; displayed=unverified; no account/game access.
```

The actual adapter registered its identity, completed the availability probe and returned normally from one generic GivreWork toast submission. `Setting=n/a` is the exact logged result: the adapter does not return the numeric setting on Available/Submitted. No actual Suppressed/Failed result was observed on this installation during this smoke. Refusal-path coverage comes from the offline gate tests and the adapter's pre-Show setting check; Windows settings were not changed to force a refusal. Visible display remains unverified.

### Integrator validation

The coordinating agent exercised the actual settings window through `tools/R6IntegrationUiHarness`: save, reload and account isolation pass (`artifacts/R6-ui-validation.log`). Its serialized full application build has zero errors (`artifacts/R6-app-debug-build.log`). These are executed tool results, not a report of human visual validation.

Status: implementation complete for this Prompt9 scope; deterministic tests pass, real window behavior and zero-error app build are reported by the integrator, and the Windows adapter is verified through Available -> Submitted for one generic test. Desktop visibility, game-triggered end-to-end behavior and compatibility on other Windows installations are not claimed. No account/game/network action, commit or push was performed by this worker. No further MainViewModel/MainWindow integration is required by this implementation. LSP remains unavailable and was not installed.

This report is finalized for commit preparation. No code changes follow this documentation update.
