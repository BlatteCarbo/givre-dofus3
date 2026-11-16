param(
    [string] $Server = (Join-Path $PSScriptRoot 'bin\Debug\net10.0\GivreMcp.exe'),
    [string] $ReviewCacheRoot = '',
    [string] $ReviewSummarySha256 = ''
)
$ErrorActionPreference = 'Stop'
$start = [System.Diagnostics.ProcessStartInfo]::new($Server)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['GIVRE_CONTROL_PIPE'] = 'GivreMcpSmoke-absent-' + [Guid]::NewGuid().ToString('N')
foreach ($key in @('GIVRE_DOFUSDB_MCP_CACHE_ROOT', 'GIVRE_DOFUSDB_MCP_SUMMARY_SHA256',
    'GIVRE_DOFUSDB_MCP_REVIEW_ONLY', 'GIVRE_DOFUSDB_MCP_OPERATOR_SCOPE')) {
    [void]$start.Environment.Remove($key)
}
$reviewEnabled = $false
if ($ReviewCacheRoot -ne '' -or $ReviewSummarySha256 -ne '') {
    if (-not [System.IO.Path]::IsPathFullyQualified($ReviewCacheRoot) -or
        $ReviewCacheRoot.StartsWith('\\') -or
        $ReviewSummarySha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Pass an absolute local ReviewCacheRoot and a 64-hex independently pinned ReviewSummarySha256.'
    }
    $manifest = Join-Path $ReviewCacheRoot 'review-summary.json'
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf) -or
        (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $ReviewSummarySha256.ToUpperInvariant()) {
        throw 'The local DofusDB review manifest does not match the caller-supplied SHA-256.'
    }
    $start.Environment['GIVRE_DOFUSDB_MCP_CACHE_ROOT'] = $ReviewCacheRoot
    $start.Environment['GIVRE_DOFUSDB_MCP_SUMMARY_SHA256'] = $ReviewSummarySha256
    $start.Environment['GIVRE_DOFUSDB_MCP_REVIEW_ONLY'] = '1'
    $start.Environment['GIVRE_DOFUSDB_MCP_OPERATOR_SCOPE'] = 'api_ai_mcp'
    $reviewEnabled = $true
}
$process = [System.Diagnostics.Process]::Start($start)
$stderr = $process.StandardError.ReadToEndAsync()
function Send-Mcp($message) {
    $process.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 30 -Compress))
    $process.StandardInput.Flush()
}
function Receive-Mcp($id) {
    while ($true) {
        $read = $process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(60000)) { throw "Timed out waiting for MCP response $id" }
        $line = $read.Result
        if ($null -eq $line) { throw 'Server closed stdout unexpectedly.' }
        $reply = $line | ConvertFrom-Json -Depth 50
        if ($reply.id -eq $id) {
            if ($null -ne $reply.error) { throw ($reply.error | ConvertTo-Json -Compress) }
            return $reply.result
        }
    }
}
try {
    Send-Mcp @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'givre-smoke'; version = '1.0.0' } } }
    $initialized = Receive-Mcp 1
    if (-not $initialized.serverInfo.name) { throw 'Missing serverInfo.' }
    Send-Mcp @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
    Send-Mcp @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} }
    $listed = Receive-Mcp 2
    $expected = @('get_app_status','list_bots','get_bot_state','read_bot_console','wait_console','validate_lua_script','list_saved_scripts','read_saved_script','save_script','patch_script','run_saved_script','run_lua_script','stop_script','connect_bot','disconnect_bot','reconnect_bot','list_supervisors','run_supervisor','stop_supervisor','get_supervisor_template','list_teams','manage_team','emergency_stop')
    $actual = @($listed.tools.name)
    $expected += @('get_scripting_guide', 'search_lua_api', 'get_inventory', 'get_map_state', 'probe_navigation',
        'list_accounts', 'read_log', 'fight_metrics', 'get_combat_profile', 'set_combat_profile', 'list_expedition_zones',
        'start_expedition', 'get_settings', 'create_character', 'delete_character', 'find_treasure_clue', 'plan_job_xp', 'find_resource_maps', 'make_gather_script', 'find_capturable_monsters', 'make_soul_script', 'travel_to', 'list_combat_journals', 'get_combat_journal', 'make_treasure_script', 'get_events', 'read_chat', 'send_private_message', 'plan_production_chain', 'recommend_zones', 'estimate_spell_damage')
    $reads = @('get_character_info','get_bank','search_items','get_item_info','find_places','get_bot_stats',
        'get_anomalies','list_dungeons','get_bot_settings','get_bot_diagnostics','get_craft_advice','get_market_deals','get_subscriptions','get_wealth','query_world_atlas','query_profession_plan','query_item_craft')
    $dofusDbReads = @('dofusdb_catalog_capabilities','dofusdb_catalog_status','dofusdb_search_items',
        'dofusdb_get_item','dofusdb_get_recipe','dofusdb_get_set','dofusdb_list_sets')
    $reads += $dofusDbReads
    $writes = @('set_bot_settings','start_dungeon','exit_dungeon','use_item','equip_item','unequip_item')
    $expected += $reads + $writes
    if (@(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)).Count -ne 0) { throw 'Tool names differ from the required contract.' }
    $console = $listed.tools | Where-Object name -eq 'read_bot_console'
    if ($console.inputSchema.properties.since.default -ne 0 -or $console.inputSchema.properties.limit.default -ne 100) { throw 'Console defaults are incorrect.' }
    if (-not $console.annotations.readOnlyHint) { throw 'Missing read-only hint.' }
    foreach ($tool in $listed.tools) {
        if ($tool.inputSchema.properties.PSObject.Properties.Name -contains 'ct') { throw 'Cancellation token leaked into tool schema.' }
    }
    Send-Mcp @{ jsonrpc = '2.0'; id = 3; method = 'tools/call'; params = @{ name = 'get_app_status'; arguments = @{} } }
    $unavailable = Receive-Mcp 3
    if ($unavailable.isError -ne $true) { throw 'Expected isError for absent application pipe.' }
    if (($unavailable.content.text -join ' ') -notmatch 'not reachable|unavailable|timed out') { throw 'Expected meaningful unavailable-application error.' }
    $probe = $listed.tools | Where-Object name -eq 'probe_navigation'
    if (-not $probe.annotations.readOnlyHint -or $probe.inputSchema.properties.target.type -ne 'integer') { throw 'Navigation probe contract is incorrect.' }
    foreach ($name in $reads) {
        $schema = $listed.tools | Where-Object name -eq $name
        if ($null -eq $schema -or -not $schema.annotations.readOnlyHint) { throw "Read-only tool missing or incorrectly annotated: $name" }
    }
    $getItem = $listed.tools | Where-Object name -eq 'dofusdb_get_item'
    $findItems = $listed.tools | Where-Object name -eq 'dofusdb_search_items'
    if ($getItem.inputSchema.properties.itemId.type -ne 'integer' -or
        $findItems.inputSchema.properties.limit.default -ne 20 -or
        $findItems.inputSchema.properties.offset.default -ne 0) {
        throw 'DofusDB read-only schemas/pagination do not match contract.'
    }
    Send-Mcp @{ jsonrpc = '2.0'; id = 4; method = 'tools/call'; params = @{
        name = 'dofusdb_catalog_status'; arguments = @{} } }
    $blocked = Receive-Mcp 4
    if ($blocked.isError -eq $true) { throw 'DofusDB must return a safely bounded status.' }
    if ($reviewEnabled) {
        if (($blocked.content.text -join ' ') -notmatch 'REVIEW_ONLY') {
            throw 'Explicit local review source was not admitted by SHA-pinned read-only reader.'
        }
        Send-Mcp @{ jsonrpc = '2.0'; id = 5; method = 'tools/call'; params = @{
            name = 'dofusdb_search_items'; arguments = @{ query = 'bois'; offset = 0; limit = 5 } } }
        $items = Receive-Mcp 5
        if ($items.isError -eq $true -or
            ($items.content.text -join ' ') -notmatch 'sourceGameVersionVerified') {
            throw 'Expected local source-checked items response without version promotion.'
        }
    } elseif (($blocked.content.text -join ' ') -notmatch 'BLOCK') {
        throw 'DofusDB local read must fail closed without explicit operator gate.'
    }
    foreach ($name in $writes) {
        $schema = $listed.tools | Where-Object name -eq $name
        if ($null -eq $schema -or $schema.annotations.readOnlyHint) { throw "Write tool missing or annotated read-only: $name" }
    }
    $bank = $listed.tools | Where-Object name -eq 'get_bank'
    if ($bank.inputSchema.properties.offset.default -ne 0 -or $bank.inputSchema.properties.limit.default -ne 100) { throw 'Bank pagination defaults are incorrect.' }
    $settings = $listed.tools | Where-Object name -eq 'set_bot_settings'
    if ($settings.inputSchema.properties.changes.type -ne 'object') { throw 'Bot settings changes must be a JSON object.' }
    $item = $listed.tools | Where-Object name -eq 'use_item'
    if ($item.inputSchema.properties.uid.type -ne 'integer') { throw 'Item action must use an integer UID.' }
    Write-Output "PASS: initialize ($($initialized.protocolVersion)), tools/list, $($expected.Count) exact tools, new read/write schemas, pagination, navigation probe, console defaults, cancellation injection, absent-app isError."
} finally {
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
