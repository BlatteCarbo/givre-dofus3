# Givre MCP stdio server

Standalone .NET 10 executable using the official `ModelContextProtocol` SDK 2.2.0 and `Microsoft.Extensions.Hosting` 10.0.10, pinned from https://api.nuget.org/v3/index.json. No global installation is needed. Only JSON-RPC is written to stdout; host logs go to stderr.

Build from `GivreWork` with `dotnet build GivreMcp/GivreMcp.csproj -m:1 --source https://api.nuget.org/v3/index.json`. **Parent: serialize this build with app/shared-project builds.** The project references `../GivreControl/GivreControl.csproj` and calls `GivreControl.ControlPipeClient.CallAsync(string tool, object arguments, CancellationToken ct)` for every tool. The shared client owns length-prefixed IPC and returns raw JSON; this server does not duplicate the wire protocol. Its default pipe is `ControlPipeClient.DefaultPipeName`; the shared client honors optional `GIVRE_CONTROL_PIPE`.

Use `client-config.toml` for Codex or `client-config.json` for clients accepting `mcpServers`. These are examples only; no user configuration is changed. Build first and use the executable directly so build messages never enter the stdio protocol. For Release builds, replace `Debug` with `Release` in the example paths. An optional JSON entry `"env": { "GIVRE_CONTROL_PIPE": "your-local-pipe-name" }` selects another local pipe.

## Exact tool API

Parameters are JSON strings unless marked otherwise. Parentheses show optional defaults. CancellationToken is injected by the SDK, never part of the public schema.

```text
get_app_status()
get_scripting_guide()
search_lua_api(query, max:integer=20)
get_inventory(bot, query?:string, equippedOnly:boolean=false, includeEffects:boolean=false)
get_map_state(bot)
list_bots()
get_bot_state(bot)
get_character_info(bot)
get_bank(bot, offset:integer=0, limit:integer=100, includeEffects:boolean=false)
search_items(query, offset:integer=0, limit:integer=20)
get_item_info(gid:integer)
find_places(bot, query, limit:integer=10)
get_bot_stats(bot, period="day")
get_anomalies(bot)
get_craft_advice(query?:string, gid:integer=0, quantity:integer=1, bot?:string)
get_market_deals(bot, minDiscountPercent:integer=25, minLotGain:integer=1000, maxAgeMinutes:integer=180, limit:integer=30)
get_subscriptions()
get_wealth(bot)
list_dungeons(query="", offset:integer=0, limit:integer=25)
get_bot_settings(bot)
get_bot_diagnostics(bot)
set_bot_settings(bot, changes:object)
start_dungeon(bot, dungeonId:integer, runs:integer=1, maxMinutes:integer=0, elixirRank:integer=0, elixirMaxPrice:integer=0, elixirStock:integer=2)
exit_dungeon(bot)
use_item(bot, uid:integer)
equip_item(bot, uid:integer, position:integer=-1)
unequip_item(bot, uid:integer)
read_bot_console(bot, since:integer=0, limit:integer=100, pattern="", level="")
wait_console(bot, pattern, since:integer=0, timeout:integer=30)
validate_lua_script(code)
list_saved_scripts()
read_saved_script(name)
save_script(name, code, overwrite:boolean=false)
patch_script(name, oldText, newText)
run_saved_script(bot, name)
run_lua_script(bot, code)
stop_script(bot)
connect_bot(bot, character="", server="")
disconnect_bot(bot)
reconnect_bot(bot)
create_character(bot, server, breed, sex, name="", head=0, body=0, colors={}, leaveTutorial=true)   # Socket ; compte hors ligne accepté
delete_character(bot, server, character, answer, confirm)   # IRRÉVERSIBLE ; Socket, compte avec code secret ; confirm=true seulement sur demande explicite
travel_to(bot, x, y | mapId)   # trajet vers une carte (mini-script travelTo)
find_treasure_clue(x, y, direction, clue="")   # chasse au trésor (DofusDB, cache)
plan_job_xp(job, fromLevel, toLevel, mode="cheapest", bot="")   # plan d'XP hors ligne
find_resource_maps(resource="", search="", job=0, bot="", x, y, limit=10)   # où récolter
make_gather_script(resource, bot="", maps=6, save=false)   # script de récolte
find_capturable_monsters(search="")   # âmes capturables
make_soul_script(monsters=[ids], save=false)   # script de capture d'âmes (voir docs/CAPTURE_AMES.md)
list_supervisors()
run_supervisor(name, code="")
stop_supervisor(name)
get_supervisor_template(id)
list_teams()
manage_team(action, name, members:string[]|null=null, leader="", script="", enabled:boolean=true, timeout:integer=30)
probe_navigation(bot, target:integer)
list_accounts()
read_log(file="", tail:integer=200, pattern="")
fight_metrics(file="", last:integer=20)
get_combat_profile(bot)
set_combat_profile(bot, changes:object)
list_expedition_zones(area="", subArea="")
start_expedition(bot, subArea:integer, mandatory:integer[]|null=null, forbidden:integer[]|null=null, minMonsters:integer=1, maxMonsters:integer=8, maxFights:integer=0, maxMinutes:integer=0, stopPods:integer=95)
get_settings()
emergency_stop()
```

Each method forwards the exact tool name and camelCase argument names, including defaults, to the shared client. The application owns validation, allowed team actions, script-store paths, execution and connection behavior. Status/list/read/wait/validation/template tools have read-only hints. Mutation tools retain conservative SDK hints. There is no generic dispatch or evaluation tool exposed through MCP.

The character/game tools operate on locally observed data. Their nine reads omit credentials, and unknown values appear as null or with an explicit availability indicator. Bank and catalogue pages have bounded sizes. Item actions require the inventory UID of the selected bot, rather than an item catalogue GID. The additional read-only get_bot_diagnostics tool reports local readiness checks and clearly marks unverified protocol and server ACK evidence.

The six new writes honor McpAllowActions and the protected-account list. set_bot_settings accepts only validated movement, watchdog, achievement and anomaly-notification settings (never API keys, webhooks, endpoints or arbitrary properties). start_dungeon generates a Lua script using locally known maps and keys and delegates the run to AppBotOrders; exit_dungeon checks the current dungeon before requesting an exit. Item sends and script starts return local acceptance or refusal; no server acknowledgement is claimed.

SDK dispatch supports concurrent requests and propagates cancellation to the shared client; this server adds no global lock. Safe shared-client InvalidOperationException messages become SDK McpException tool errors (`isError`); transport/JSON/unexpected exceptions use fixed messages without stack traces, payloads or inner exception details. Successful raw JSON is serialized by the SDK into the tool result.

## Smoke test without the application

After building, run `pwsh -NoProfile -File GivreMcp/Smoke.ps1` from `GivreWork`. It starts only the MCP executable, sends the messages below, verifies the exact 66 tools, navigation probe schema, console defaults and read-only annotation, then calls `get_app_status` against a fresh deliberately nonexistent pipe name to verify a meaningful `isError` response. It never launches or contacts the application. This verifies MCP discovery and the disconnected error path, not live app behavior.

`probe_navigation(bot, target)` diagnoses a local route without sending a movement. It reports stale or incomplete geometry, stale/unconfirmed position, invalid or blocked target, unreachable target, an existing position, or a path with its step count. Fight-only and custom obstacles are respected. Live actor occupancy is excluded, so a calculated route does not establish server acceptance. For example, call it with the identifier returned by `list_bots` and an observed destination cell; inspect `status`, `cells`, and `sent: false`.

Run/stop responses report local acceptance or refusal; they do not confirm game-server outcomes. Caller cancellation is rethrown with its original token, while bridge-originated cancellation becomes a safe tool error. The SDK handles concurrent dispatch and cancellation notifications.

For a manual test, launch the built executable with redirected stdin/stdout, send one JSON object per line, and wait for response 1 before sending the last two messages:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"manual-smoke","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
```

Close stdin to end the process. Responses are newline-delimited JSON, not the shared control pipe's binary length-prefixed frames.
