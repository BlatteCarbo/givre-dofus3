# Givre MITM findings — Dofus 3.6.12.16

Date: 2026-10-05

This note records live findings from Givre's current direct visible-client MITM path and the parts that are relevant to GivreClone.

## Live transport proof

- Client: Dofus `3.6.12.16`.
- Givre launches the visible Dofus client with a process-scoped direct redirect and no Frida.
- The visible client connects to Givre's local MITM listener (`visible-loopback-adopted`) while Givre keeps the authoritative upstream Ankama GameService connection.
- Natural server/character selection was retained: Orukam -> Xyla-Taz.
- In repeated live fights, Givre continued to receive authoritative fight lifecycle/state and to dispatch sequence ACKs while the visible Dofus process remained bound and alive.
- No pcap was needed for this proof.

## Animation bypass result

Givre currently has a strict presentation-only suppression hook for `PlaySpellScriptEvent` (`jnm`). The rule is deliberately fight-scoped and never suppresses authoritative damage/AP/MP/sequence/fight-end/state events.

Important live result: across repeated Ravage/Supplice fights on 3.6.12.16, **no `jnm` frame was emitted**. Therefore `jnm` suppression cannot be claimed as a working no-animation mechanism for those player spells.

Do **not** broaden the drop list to generic animation-looking wires without independent proof. Player spell animation is coupled to authoritative fight action/sequence traffic closely enough that blindly dropping a cast/sequence event risks desynchronizing the visible client.

The correct direction for a real no-animation feature is the same architectural class used by Givre: a client-side, per-process animation skipper/hook that leaves network state authoritative.

## Sequence/ACK behavior

- Sequence start/end remains authoritative.
- ACK is dispatched immediately after the relevant owned sequence closes; Givre does not add synthetic human delays in turbo mode.
- Several live fights completed with repeated ACK dispatches and normal fight-end state reset.
- The visible UI may still be animating while the headless engine is already synchronized; this is exactly why presentation acceleration should happen in the client renderer/animation layer rather than by deleting state packets.

## Reconnect lesson worth sharing

A concrete bug was found in Givre's script-requested reconnect path: automatic reconnect already preserved the owned visible GameService client, but `ReconnexionautoForScript()` reset the upstream/world state without first suspending/preserving that visible binding. Result: the Unity window could disappear while the process stayed alive.

The fix is to perform the same visible-client preservation step **before** invalidating world state / stopping the old upstream pipeline, then buffer inbound replacement-upstream frames and resume visible forwarding only at an authoritative fresh snapshot.

In pseudocode:

```text
begin reconnect generation
preserve/suspend exact owned visible client (PID + transport generation)
invalidate transient world state
replace upstream connection
arm bounded reconnect catch-up buffer for new upstream generation
reselect exact prior character
on authoritative fresh world snapshot:
    replay buffered raw frames to the preserved visible client
    revalidate upstream + visible generations
    resume forwarding
```

Guards that matter:

- exact owned PID / transport generation;
- replacement upstream generation must still be current;
- bounded frame/byte buffer with fail-closed overflow;
- no reacquisition of another Dofus client during preservation;
- resume only after authoritative world/fight state is actually rebuilt.

## What Givre should import from Givre

The GivreShare code confirms several ideas that are directly useful to Givre:

1. **Native/client-side no-animation control.** `ModControl` drives per-PID native mods; the actual no-animation implementation is intentionally outside packet transport. This validates moving Givre's real animation bypass to a client hook rather than widening MITM packet suppression.
2. **Sequence barriers before movement.** Givre waits for the previous combat sequence to clear, then re-reads the current cell and target occupancy before forging a move. This avoids stale-target/stale-cell rejects in fast combat.
3. **No fixed move delay in Instant mode.** Givre removed the old fixed 400 ms wait once server-derived position confirmation was authoritative. Givre already follows the same principle and should keep it.
4. **Progress-based anti-stall.** Givre distinguishes useful actions from keepalives/dialog refreshes and escalates recovery in bounded stages. This is stronger than treating any inbound packet as progress.
5. **Idempotent route/script resume.** Route state is checkpointed before cleanup/side effects, with bounded failure counters and fail-closed behavior. This maps well to Givre's long-running Clartek flow.

## What Givre should NOT copy from Givre

- Do not implement no-animation by dropping arbitrary fight packets.
- Do not equate fight-end with semantic win/loss without a separate outcome witness.
- Do not resume a preserved visible client before the replacement upstream has supplied an authoritative world/fight snapshot.

## Givre live checkpoint used for this note

- Runtime line: Clartek v1.0.17 / Dofus 3.6.12.16.
- Direct visible MITM: live.
- no-Frida: live.
- no-pcap: live.
- Repeated turbo fights: Sequence/ACK/fight-end healthy.
- `jnm` no-animation suppression: **not live-proven because the wire was not emitted by the tested spells**.

