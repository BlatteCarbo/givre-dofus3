$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $repo 'native\GivreTurboRuntime\v0.4.0-skipper1.0.5-speedhack1.0.3'
$rust = Get-Content -Raw (Join-Path $runtimeDir 'GivreCombatSpeedHack.rs')
$native = Get-Content -Raw (Join-Path $repo 'GivreClone.App\Services\NativeTurboRuntime.cs')
$combat = Get-Content -Raw (Join-Path $repo 'GivreClone.App\Services\LaunchService.Combat.cs')
$launch = Get-Content -Raw (Join-Path $repo 'GivreClone.App\Services\LaunchService.cs')
$project = Get-Content -Raw (Join-Path $repo 'GivreClone.App\GivreClone.App.csproj')

function Require-Text([string]$haystack, [string]$needle, [string]$label) {
    if (-not $haystack.Contains($needle)) { throw "missing contract: $label" }
}

Require-Text $combat 'profile.Speed == CombatSpeed.Turbo && !IsSocketPid(pid)' 'Turbo is MITM-only'
Require-Text $combat 'NativeTurboRuntime.RequestState((int)pid, nativeFallback, turboMitm)' 'Turbo requests native speedhack'
Require-Text $native 'SpeedHackModId = "givre.dofus3.combat-speedhack"' 'speedhack mod id'
Require-Text $native 'if (combatSpeedHack && !status.Contains(SpeedHackModId' 'speedhack load is requested only for Turbo'
Require-Text $native 'else if (!combatSpeedHack && status.Contains(SpeedHackModId' 'speedhack unload on Turbo exit'
Require-Text $native 'EnsureUnloadedAsync(pid, desired, control, SpeedHackModId, "speedhack")' 'speedhack unload on profile teardown'
Require-Text $native 'LegacyModsTookOwnership' 'legacy no-animation ownership handoff'
Require-Text $native 'GivreMods reprend le no-animation, skipper fallback retiré' 'legacy handoff unloads only skipper fallback'
Require-Text $native 'await DisableAsync(pid, desired).ConfigureAwait(false)' 'Turbo exit restores speedhack even when GivreMods owns no-animation'
Require-Text $launch 'NativeTurboRuntime.LegacyModsTookOwnership(pid)' 'launcher preserves Turbo speedhack when GivreMods loads'
Require-Text $project 'DofusCombatSpeedHack.dll' 'speedhack copied to app output'
Require-Text $native 'ProcessStartTicks' 'owned process identity'
Require-Text $native 'Generation' 'desired state generation'
Require-Text $native 'EnsureCurrent(pid, desired)' 'stale requests cancelled'
Require-Text $native 'process.Handle' 'owned process handle prevents PID reuse during injection'
Require-Text $native 'InjectBootstrapOwned' 'bootstrap injection is identity-bound'
Require-Text $native 'EnsureUnloadedAsync' 'strict unload reconciliation'
Require-Text $native 'combat-speedhack.lease' 'managed speedhack lease'
Require-Text $native 'combat-speedhack.restore-request' 'managed restore token request'
Require-Text $native 'combat-speedhack.restored' 'managed restore token ack'
Require-Text $native 'RestoreAckMatches' 'generation-matched restore ack'

Require-Text $rust 'const COMBAT_MULTIPLIER: f32 = 8.0' 'x8 multiplier'
Require-Text $rust 'InputChecker' 'visible fight witness'
if ($rust.Contains('FightEntitiesService') -or $rust.Contains('find_fight_state_field')) { throw 'heuristic fight-state resolver must stay removed' }
Require-Text $rust 'UnityEngine.CoreModule.dll' 'Unity Time owner assembly'
Require-Text $rust 'get_timeScale' 'timeScale baseline getter'
Require-Text $rust 'set_timeScale' 'timeScale setter'
Require-Text $rust 'UnityEngine.Time::set_timeScale(System.Single)' 'exact direct Unity timeScale icall fallback'
Require-Text $rust 'write_time_scale_direct' 'direct baseline restore fallback'
Require-Text $rust 'refusing to arm x8 without a direct restore fallback' 'fail closed when direct restore fallback is unavailable'
Require-Text $rust 'LEASE_MAX_AGE' 'native lease expiry'
Require-Text $rust 'combat-speedhack.lease' 'native lease file'
Require-Text $rust 'combat-speedhack.restore-request' 'native restore token request'
Require-Text $rust 'combat-speedhack.restored' 'native restore token ack'
Require-Text $rust 'last_restored_token' 'native generation-aware restore ack'
Require-Text $rust 'restore_baseline(state, &mut speed, "fight inactive")' 'restore after fight'
Require-Text $rust 'restore_baseline(state, &mut speed, "mod unload")' 'restore on unload'
Require-Text $rust 'speed.baseline = Some(baseline)' 'preserve exact pre-fight baseline'

$speedDll = Join-Path $runtimeDir 'DofusCombatSpeedHack.dll'
$actualHash = (Get-FileHash -LiteralPath $speedDll -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedHash = '895a195a6551efb4a1085e500b745676ce8bf44a0967426de3e14abd5d1a0a24'
if ($actualHash -ne $expectedHash) { throw "speedhack SHA mismatch: $actualHash" }

Write-Host "PASS: MITM Turbo speedhack contract + fight-only x8 + baseline restore + SHA $actualHash"
