[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$messagesPath = Join-Path $root 'GivreClone.App\Services\Transport\Socket\ConnectionMessages.cs'
$clientPath = Join-Path $root 'GivreClone.App\Services\Transport\Socket\SocketConnectionClient.cs'
$testsPath = Join-Path $root 'GivreClone.Tests\ConnectionMessages361216Tests.cs'

function Require([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL SocketConnection361216Contract $Name" }
}

$messages = [IO.File]::ReadAllText($messagesPath)
$client = [IO.File]::ReadAllText($clientPath)
$tests = [IO.File]::ReadAllText($testsPath)

Require ($messages.Contains('case 6 when wire == 2: DecodeCharacterListResponse')) 'character-list-response-field6'
Require ($messages.Contains('internal static List<ConnServer> MergeServerCharacters')) 'server-character-merge-helper'
Require ($messages.Contains('!byId.TryGetValue(bucket.Id, out var target)')) 'unknown-character-server-fail-closed'
Require ($messages.Contains('character.ServerId != bucket.Id')) 'character-server-id-correlation'

Require ($client.Contains('IReadOnlyList<ConnServer>? serverMetadata = null;')) 'server-metadata-latch'
Require ($client.Contains('IReadOnlyList<ConnServer>? characterBuckets = null;')) 'character-list-latch'
Require ($client.Contains('!selecting && serverMetadata is not null && characterBuckets is not null')) 'selection-waits-for-both-responses'
Require ($client.Contains('ConnectionMessages.MergeServerCharacters(serverMetadata, characterBuckets)')) 'selection-uses-authoritative-merge'
Require ($client.Contains('servers.Any(s => s.Id == preferred && s.Characters.Count > 0)')) 'preferred-server-must-own-character'
Require ($client.Contains('string.Equals(c.Name, chosen.Character, StringComparison.Ordinal)')) 'interactive-character-must-belong-to-chosen-server'
Require ($client.Contains('serveur/perso incoh')) 'interactive-character-mismatch-fails-closed'

$identStart = $client.IndexOf('case ConnDecoded.Kind.IdentificationSuccess:', [StringComparison]::Ordinal)
$characterStart = $client.IndexOf('case ConnDecoded.Kind.CharacterList:', $identStart, [StringComparison]::Ordinal)
Require ($identStart -ge 0 -and $characterStart -gt $identStart) 'identification-case-range'
$identBody = $client.Substring($identStart, $characterStart - $identStart)
Require (-not $identBody.Contains('BuildSelectServer')) 'no-premature-select-on-identification'
Require (-not $identBody.Contains('PickServer(')) 'no-premature-auto-pick-on-identification'

foreach ($secretLogPattern in @(
    'Short(p.DeviceId)',
    'Short(p.Token)',
    'Short(p.CertificateHash)',
    'Short(d.GameToken)',
    'compte « {d.AccountNickname} »',
    'personnage « {cc} »'
)) {
    Require (-not $client.Contains($secretLogPattern)) ("no-sensitive-log:" + $secretLogPattern)
}
Require ($client.Contains('tokenPresent=')) 'game-token-presence-only-log'
Require ($client.Contains('devicePresent=')) 'device-presence-only-log'
Require ($client.Contains('personnage explicite')) 'character-choice-nonidentifying-log'
Require (-not $client.Contains('SocketLog.Hex(frameBody)')) 'no-raw-connection-response-hex-log'

Require ($tests.Contains('Merge_server_characters_preserves_authoritative_metadata_and_drops_unknown_servers')) 'merge-unit-test-present'

Write-Output 'PASS SocketConnection361216Contract mergeAfterBoth=true unknownServerFailClosed=true preferredRequiresCharacter=true sensitiveLogsRedacted=true'
