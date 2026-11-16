# Round 6 / Prompt 4: Socket mirror prerequisite

Status: partial, offline only. No operational mirror, session replay, GUI button,
client launch, injection or live validation is delivered by this change.
No original MizanBot files, logs, accounts or credential stores were inspected.

## Verified source architecture

- `GivreClone.App/Services/Transport/Socket/SocketGameClient.cs` owns the game
  stream, serialized writes, bot injection queue and automatic protocol responses.
  Character selection is scheduled asynchronously through one `SelectCharacter`
  function; its five-second confirmation timeout starts after sending selection.
- `GivreClone.App/Services/Transport/Socket/SocketProbe.cs` creates a new game
  client per attempt. It currently supplies no parking gate.
- `GivreClone.App/Services/LocalLaunch/LocalHandshakeRelay.cs` checks owned PID,
  parses the native hook header, opens a NEW upstream connection, and closes both
  streams on either pump's completion. Its first-connection classification is
  explicitly heuristic. It has no route to an existing Socket game stream.
- `GivreClone.App/Services/LaunchService.cs` routes synthetic Socket PIDs to
  registered injectors; GUI processes use a separate owned-process relay path.
  Neither path transfers protocol ownership to a graphical client.

## Implemented boundary

`SocketCharacterSelectionGate` is an opt-in, single-session synchronization point.
When supplied in `SocketGameParams.SelectionGate`, the real `SelectCharacter`
function waits before its first selection request, including forced selection.
`Parked` completes only when that function reaches the gate. `TryResume` succeeds
once, after parking. `EndSession` cancels waiters, including failures before
parking; `SocketGameClient` calls it in its session-finally block.

The gate contains no packet buffers, tokens, streams, endpoints or logging.
With the default null option, automatic selection follows its existing path.
The receive loop and existing automatic responses continue while selection is
parked. This is NOT a quiescent transport ownership boundary. Existing injectors
are not suspended. A caller must not start bot actions during this preparatory
phase. The existing server-idle watchdog remains active: parking is not an
unlimited server-side session lease. Forced-selection reconnect behavior has
not been validated while parked and must not be enabled by a production caller.

No current caller opts in. An already selected session cannot be parked retroactively.
No UI capability is advertised. `LaunchService.cs`, `LocalHandshakeRelay.cs`,
`SocketProbe.cs` and shared test project definitions remain untouched by this work.

## Offline validation

Run from the repository root:

    dotnet run --project tools/R6SocketParkingHarness/R6SocketParkingHarness.csproj -c Debug --disable-build-servers

The isolated, dependency-free .NET 10 harness links the production gate source,
without referencing or building the application. It checks parking before resume,
early/duplicate resume rejection, single-use entry, end before/during parking,
idempotent end, cancellation before/during parking and 200 concurrent resume/end
races. No network listener, account data, session frames or game process is used.
This does not execute `SocketGameClient`, validate its protocol integration, or
prove compatibility with Dofus 3.7.11.11. Application compilation and existing relay
tests must be run later by the build owner when the shared build slot is available.

## Missing integration and evidence

Observed on 2026-10-18: the harness exited 0 and printed five PASS groups,
including all 200 concurrent resume/end races. `git diff --check` exited cleanly
with line-ending warnings only. Other dotnet processes were present; no application
build was launched. These results validate the synchronization primitive only.

1. A session coordinator must create one fresh gate per connection attempt, map it
   to the correct synthetic PID, release it on Stop/retry, and define timeout and
   forced-selection policies. `SocketProbe` currently supplies no such coordinator.
2. Establish a client initialization protocol using authorized evidence from an
   owned test client. Packet counts in the catalogue do not specify replay payloads,
   connection ordering, challenge lifetime, request IDs or client state. No frame
   transcript is retained and no invented login replay is implemented.
3. Introduce a single authoritative upstream owner. Before GUI takeover, drain or
   suspend Socket reactions, pending ACKs, engine injections and scheduled sends;
   arbitrate GUI requests and preserve ordering. The parking gate alone does none
   of this. Two simultaneous writers with independent state are not a mirror.
4. Add an explicit owned-PID attachment route to the relay BEFORE any new upstream
   dial. Bind it to the correct live session and intended endpoint, preserve proxy
   policy, and reject unknown, stale or duplicate attachments. Do not reuse its
   first-connection heuristic as a session identity proof.
5. Keep the upstream stream owned by the session coordinator. GUI detach must
   cancel and close only that attachment, then restore Socket response ownership
   without duplicate ACKs or disconnecting the bot. Current relay pump-finally
   closes upstream, so forwarding the existing stream into it is insufficient.
6. Resolve graphical PID vs synthetic PID in decoder state and injectors; add
   cleanup for Stop, server close, mapping purge, failed launch and stale callbacks.
7. Only after these contracts pass offline integration tests, implement a dedicated
   `LaunchService.SocketMirror.cs` and the UI action. Do not offer a button that
   starts an independent login and calls it a mirror. Native mod timing belongs
   after verified attachment, not a guessed 11-16 second delay.

Required future offline scenarios: fragmented handshake, wrong owned PID/session,
wrong endpoint/proxy, duplicate attach, one upstream dial only, strict byte order,
response-owner transfer, GUI failure during bootstrap, GUI detach with bot alive,
server close, Stop and reconnect races. Live work requires the user's separate
authorized test session; none was performed here.
