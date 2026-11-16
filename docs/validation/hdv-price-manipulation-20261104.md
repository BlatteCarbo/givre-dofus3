# HDV inflated lot price incident

User reported losing 200,000 kamas after the x100 market was emptied. Screenshot
183533 text extraction identifies Serviette de Plage13487: x1=147, x10=1746,
x100=4999999, x1000=399999, average unit621. Image cannot be directly inspected
by the current model. Exact historical tax debit has not been reconciled.

Reproduction bash1277 ran the existing bulk LotPrice owner with these quotes:
expected refusal(-1), actual4999999. The same-size quote branch had a minimum
price guard but no upper bound. Average-relative bounds applied only when that
size had no quote.

Implemented common sale/reprice boundary guard before relay injection:

- Price ceiling:3 times the lowest positive independent unit reference, from
  server average or other lot sizes; the requested size cannot justify itself.
- Only market-size quotes no older than90 seconds count; unknown independent
  references refuse the sale rather than infer a fair price.
- Estimated tax greater than10,000 kamas per operation refuses the sale.
  Unknown or invalid tax rate refuses it too. This is not a session tax budget.
- Manual, bulk and Lua sale/reprice all reach this boundary. Purchases were
  not changed: this incident reproduction concerns listing prices and taxes.
- Bulk LotsFor abandons the whole item on a suspect applicable market quote,
  rather than falling back to smaller lots. RunSellAsync marks the row skipped,
  attaches the refusal and logs it before continuing to the next item.
- Manual reprice and watcher report the guard reason before attempting send.

Verification bash1278:201 HDV tests passed,21.3s,exit0. After skip/report changes,
bash1279:201 passed,14.2s,exit0. A stronger LotsFor regression now checks both
inflated item(empty plan) and normal quote(one x100 lot); bash1280 exited0:
18 passed,0 failed,0 skipped,8.1s.
Threshold tests include exact price ceiling, one kama above, exact tax ceiling,
one kama above, missing independent reference and unknown tax.

Isolated application build bash1281 exited0,85.3s,227 existing warnings under
`%USERPROFILE%/AppData/Local/Temp/givre-hdv-safe-20261104`.
Process observation bash1282 found one app18872 from the older
givre-turbo-relay-20261104 build and one official Dofus23088. The user reports
the client already in game; no current map/actor observation is inferred from
that process list. No duplicate application or game was launched.

Graceful closure of the exact old app18872 is requested by bash1283, with a
15-second bounded exit observation and no forced termination or replacement if
it stays open. Closure confirmed OLD_APP_CLOSED,exit0. Launch bash1284 reported
SAFE_APP_STARTED pid29364,exit0 from the protected build. Follow-up process
observation found that exact executable responding but no main-window handle;
it did not show a Dofus process (PowerShell command exit1 when a requested process
name was missing). Thus game connection is not established. Bash1286 watches
for the first main window or early exit with a120-second bound. Do not launch
a duplicate while that observation is active. Bash1286 subsequently reported
SAFE_WINDOW_READY pid29364 hwnd1247720,exit0. Independent process observation
bash1287 found the same protected executable responding with that handle.
Computer Use could not resolve the handle or enumerate the window; taskbar AX
did expose exactly one Givre window. Invoking that taskbar button did not
make it enumerable. Asked the user to restore the existing window, not to
launch another instance. The protected process is running; mounted interface
verification and game reconnection are pending. Historical crash.log ends on
November2 and supplies no evidence of a current startup exception.

After the user restored the window, Computer Use enumerated the exact safe
process29364/HWND1247720 and read its AX tree successfully. Scoped dashboard
traversal showed0 tracked sessions and0 in-game sessions. Navigation through
the actual Bots button succeeded; the panel exposes Choisis un personnage.
No selected live profile is available for a mounted HDV refusal check. No
sale/reprice button was pressed. Thus protected build startup/navigation are
observed, while refusal rendering with a real inventory remains unverified.

Continuation evidence: bash1288 exited0,2 passed,0 failed,6.8s. Both incident
and normal control also plan the next independent item886 successfully. This
checks plan isolation, not execution of the asynchronous row loop in game.
Mounted AX exposed outdated RuleHint prose in account settings; updated French
resource to describe the actual guard and skip behavior. Build bash1289 exited0,
63s,226 warnings. This prose-only follow-up is not in running build29364.
Independent-reference cases now run in bash1290: requested x100 alone cannot
justify itself; x1 bounds x100 without a server average, including exact/above
ceiling controls. Bash1290 exited0:21 passed,0 failed,16.7s. Review found
that the requested-size-only test's original high price also exceeded the tax
cap; reduced that fixture to100 kamas so only the independent-reference gate
can explain refusal. Bash1291 exited0:21 passed,0 failed,3.9s. No production behavior
changed after the deployed guard.

Connection preparation remains observational: account search was set through
AX to the intended account and the displayed count changed to1/5. Launch
buttons were not exposed by subsequent scoped searches, so none was invoked.
Do not equate account filtering or historical notification rows with a running
game session. The existing protected application remains the only launched app.

Local verification task closed with the mounted-inventory limitation above
explicitly retained. IPC list_accounts on the safe app confirms all5 accounts
inactive with no PID. Read connect_bot before use: it delegates to
OrderConnectAsync -> LaunchInGameForTeamAsync, not the requested manual
server-selection launch. Therefore no IPC connect was issued. Manual UI launch
remains the appropriate route for the official client workflow.
No live listing/reprice was requested
to validate this fix and no additional tax was intentionally incurred. Existing
compiler/analyzer warnings and the C# LSP denied-artifact traversal remain.
