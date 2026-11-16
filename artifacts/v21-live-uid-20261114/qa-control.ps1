param(
    [ValidateSet('status', 'diagnostics', 'disconnect', 'connect-socket')]
    [string]$Action = 'status'
)

$ErrorActionPreference = 'Stop'
$runName = 'v21-uid-qa-20261114'
$store = Join-Path $env:LOCALAPPDATA ('GivreReplica\TestRuns\' + $runName + '\userdata\accounts.json')
$accounts = ConvertFrom-Json ([IO.File]::ReadAllText($store))
if ($accounts.Length -ne 1 -or $accounts[0].AutoConnectType -ne 0 -or
    $accounts[0].AutoStartScript -or -not $accounts[0].Enabled) {
    throw 'QA account isolation violated.'
}

$sha = [Security.Cryptography.SHA256]::Create()
try {
    $identity = [Environment]::UserDomainName + '\' + [Environment]::UserName
    $identityHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)))).Replace('-', '').Substring(0, 16)
    $runHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($runName)))).Replace('-', '').Substring(0, 16)
    $pipeName = 'GivreWork-' + $identityHash + '-qa-' + $runHash
} finally { $sha.Dispose() }

function Invoke-QA([string]$tool, [object]$arguments) {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(5000)
        $json = ConvertTo-Json -InputObject @{ tool = $tool; arguments = $arguments } -Compress -Depth 12
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $header = [BitConverter]::GetBytes([int]$bytes.Length)
        $pipe.Write($header, 0, 4)
        $pipe.Write($bytes, 0, $bytes.Length)
        $pipe.Flush()
        $responseHeader = New-Object byte[] 4
        $offset = 0
        while ($offset -lt 4) {
            $n = $pipe.Read($responseHeader, $offset, 4 - $offset)
            if ($n -le 0) { throw 'Control bridge returned a partial header.' }
            $offset += $n
        }
        $length = [BitConverter]::ToInt32($responseHeader, 0)
        if ($length -lt 1 -or $length -gt 2000000) { throw 'Invalid control response size.' }
        $buffer = New-Object byte[] $length
        $offset = 0
        while ($offset -lt $length) {
            $n = $pipe.Read($buffer, $offset, $length - $offset)
            if ($n -le 0) { throw 'Control bridge returned a partial body.' }
            $offset += $n
        }
        return ConvertFrom-Json ([Text.Encoding]::UTF8.GetString($buffer))
    } finally { $pipe.Dispose() }
}

$list = Invoke-QA 'list_bots' @{}
if ($list.isError -or @($list.bots).Count -ne 1) { throw 'QA roster not unique or failed.' }
$bot = $list.bots[0]
if ([string]$bot.accountId -ne [string]$accounts[0].Id) { throw 'QA identity mismatch.' }
if ($bot.scriptRunning) { throw 'QA had an active script.' }

switch ($Action) {
    'status' {
        'QA_BOT_COUNT=1'
        'QA_MODE=' + $bot.mode
        'QA_STATUS=' + $bot.status
        'QA_PID_PRESENT=' + [bool]($bot.pid -gt 0)
        'QA_MAP=' + $bot.mapId
        'QA_IN_FIGHT=' + [bool]$bot.inFight
        'QA_SCRIPT_RUNNING=false'
    }
    'diagnostics' {
        $health = Invoke-QA 'get_bot_diagnostics' @{ bot = [string]$bot.accountId }
        if ($health.isError) { throw 'QA diagnostics were refused.' }
        'QA_VERDICT=' + $health.verdict
        'UID_STATUS=' + $health.socketEnvelopeUidV20.status
        'UID_REQUESTS=' + $health.socketEnvelopeUidV20.postFlushPositiveUidRequests
        'UID_MATCHED=' + $health.socketEnvelopeUidV20.matchedResponseUidEnvelopes
        'UID_UNMATCHED=' + $health.socketEnvelopeUidV20.unmatchedResponseUidEnvelopes
        'UID_AMBIGUOUS=' + $health.socketEnvelopeUidV20.ambiguousOrReusedUids
        'UID_CAN_AUTHORIZE=' + [bool]$health.socketEnvelopeUidV20.canAuthorizeGameplay
        'SOCKET_SOURCE=' + $health.socketAuthority.status
        'SOCKET_RUN_EPOCH=' + $health.socketAuthority.runEpoch
        'SOCKET_SERVER_FRAMES=' + $health.socketAuthority.serverFramedChunks
        'SOCKET_POSTFLUSH_WRITES=' + $health.socketAuthority.engineWritesAfterFlush
        'SOCKET_CAN_AUTHORIZE=' + [bool]$health.socketAuthority.canAuthorizeGameplay
        'OVERALL_MAY_ASSUME_GAMEPLAY=' + [bool]$health.mayAssumeGameplayConfirmed
    }
    'disconnect' {
        $result = Invoke-QA 'disconnect_bot' @{ bot = [string]$bot.accountId }
        if ($result.isError) { throw 'QA disconnect was refused.' }
        'QA_DISCONNECT_ACCEPTED=' + [bool]$result.ok
        'QA_DISCONNECT_COMPLETION_CONFIRMED=' + [bool]$result.completionConfirmed
    }
    'connect-socket' {
        if ($bot.pid -gt 0 -or $bot.status -ne 'offline') { throw 'QA is not offline.' }
        $result = Invoke-QA 'connect_bot' @{ bot = [string]$bot.accountId; mode = 'socket' }
        if ($result.isError) { throw 'QA connection was refused.' }
        'QA_CONNECT_ACCEPTED=' + [bool]$result.ok
        'QA_CONNECT_COMPLETION_CONFIRMED=' + [bool]$result.completionConfirmed
    }
}
