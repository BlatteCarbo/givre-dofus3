$ErrorActionPreference = 'Stop'

function Require([bool]$condition, [string]$label) {
    if (-not $condition) { throw "FAIL MitmTurboContract $label" }
}

$root = Split-Path $PSScriptRoot -Parent
function Text([string]$rel) { [IO.File]::ReadAllText((Join-Path $root $rel)) }

$profile = Text 'GivreClone.App\Services\CombatProfile.cs'
$combat  = Text 'GivreClone.App\Services\LaunchService.Combat.cs'
$turn    = Text 'GivreClone.App\Services\LaunchService.Combat.Turn.cs'
$script  = Text 'GivreClone.App\Services\LaunchService.Combat.Script.cs'
$packets = Text 'GivreClone.App\Services\LaunchService.Packets.cs'
$runtime = Text 'GivreClone.App\Services\NativeTurboRuntime.cs'
$project = Text 'GivreClone.App\GivreClone.App.csproj'

Require ($profile.Contains('Instant = 0') -and $profile.Contains('Fast = 1') -and
         $profile.Contains('Slow = 2') -and $profile.Contains('Turbo = 3')) 'stable-enum-ordinals'

Require ($combat.Contains('profile.Speed == CombatSpeed.Turbo && !IsSocketPid(pid)')) 'mitm-only-native-preset'
Require ($combat.Contains('(profile.NoAnim      || turboMitm)')) 'turbo-enables-skipper'
Require ($combat.Contains('(profile.Collapse    || turboMitm)')) 'turbo-enables-collapse'
Require ($combat.Contains('(profile.InstantMove || turboMitm)')) 'turbo-enables-instantmove'
Require ($combat.Contains('LicenseService.Perm.NoAnim')) 'noanim-permission-gate'

Require ($combat.Contains('WaitTurboActionBarrier')) 'authoritative-barrier'
Require ($combat.Contains('ack.ActionId != seq.ActionId')) 'matching-action-id'
Require ($combat.Contains('seq.At < requestAt')) 'stale-sequence-rejected'
Require ($combat.Contains('_myTurnOpen.GetValueOrDefault(pid)')) 'turn-window-gate'
Require ($packets.Contains('p.ClientToServer && p.Name == "GameActionAcknowledgementRequest"')) 'visible-client-ack-observed'
Require ($packets.Contains('author == SelfActorId(p.Pid)')) 'self-sequence-only'
Require ($packets.Contains('PulseTurboBarrier(p.Pid)')) 'event-driven-barrier-pulse'

$engineCastWaits = ([regex]::Matches($turn, [regex]::Escape('await WaitAfterCast(castRequestAt)'))).Count
$engineTrackedCasts = ([regex]::Matches($turn, 'SendCast\(pid,.*out var castRequestAt')).Count
Require ($engineCastWaits -gt 0 -and $engineCastWaits -eq $engineTrackedCasts) 'all-engine-casts-barriered'
Require ($turn.Contains('!await WaitTurboActionBarrier(pid, moveRequestAt)')) 'engine-move-barriered'
Require ($turn.Contains('if (profile.Speed != CombatSpeed.Turbo || IsSocketPid(pid))')) 'engine-final-sleep-removed-only-mitm-turbo'

Require ($script.Contains('SendCast(pid, cell, spellId, out var turboRequestAt)')) 'lua-cast-tracked'
Require ($script.Contains('WaitTurboActionBarrier(pid, turboRequestAt).GetAwaiter().GetResult()')) 'lua-cast-barriered'
Require ($script.Contains('NoteTurboRequestTx(pid, $"script-move:{src}->{dest}")')) 'lua-move-tracked'
Require ($script.Contains('await WaitTurboActionBarrier(pid, turboRequestAt)')) 'lua-move-barriered'
Require ($script.Contains('if (speed != CombatSpeed.Turbo || IsSocketPid(pid)) await Task.Delay(delay)')) 'lua-final-sleep-removed-only-mitm-turbo'
Require ($script.Contains('CloseScriptTurnAfterTurboBarrierFailure')) 'lua-unknown-outcome-closes-turn'
Require ($script.Contains('aucun retry autor')) 'lua-unknown-outcome-no-retry'

Require ($runtime.Contains('RuntimeVersion = "v0.4.0-host+skipper-1.0.5"')) 'native-runtime-version'
Require ($runtime.Contains('BootstrapModuleName = "GivreTurboBootstrap.dll"')) 'unique-bootstrap-name'
Require ($runtime.Contains('WaitForLegacyModsAsync')) 'legacy-mod-bounded-priority-window'
Require ($runtime.Contains('NativeModules.IsLoaded(pid, "GivreMods.dll")')) 'legacy-mod-loaded-check'
Require ($runtime.Contains('"GivreClone", "NativeTurbo", pid.ToString')) 'per-pid-stage'
Require ($runtime.Contains('Path.Combine(root, "NativeMods", "control", pid.ToString')) 'per-pid-control'
Require ($runtime.Contains('"LOAD\t" + skipperTarget')) 'native-load-command'
Require ($runtime.Contains('"UNLOAD\t" + SkipperModId')) 'native-unload-command'
Require ($project.Contains('NativeTurboRuntime\DofusNativeBootstrap.dll')) 'runtime-packaged'
Require (-not $project.Contains('Link="version.dll"')) 'never-installs-version-dll'

$native = Join-Path $root 'native\GivreTurboRuntime\v0.4.0-skipper1.0.5'
$expected = @{
    'DofusNativeBootstrap.dll'       = '2C24FA8F9905427A5E637484402DF492D1942EA2DE1253A3E8B49AB7E6DFDA3D'
    'DofusNativeHost.dll'            = '22D2C6743C23D48F80BC0A9E114BA13BCFB07587470AEDB188A9FDBA8846EC1B'
    'DofusCombatAnimationSkipper.dll'= 'A71084FDD4A7EAF0085944280423C31A9D352FA1566A71331F1212F745C124A3'
}
foreach ($name in $expected.Keys) {
    $path = Join-Path $native $name
    Require (Test-Path -LiteralPath $path) "native-present-$name"
    Require ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $expected[$name]) "native-hash-$name"
}

$all = $combat + $turn + $script + $packets + $runtime
Require (-not $all.Contains('BuildGameActionAcknowledgement')) 'no-parallel-ack-forge'
Require (-not $all.Contains('Time.timeScale')) 'no-global-timescale'

Write-Output 'PASS MitmTurboContract eventBarrier=True engine=True lua=True nativeFallback=True hashPinned=True noAckForge=True noGlobalTimeScale=True'
