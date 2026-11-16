# Hot Socket to visible MITM

Date: 2026-10-31. Source reading only. No build, no test run, no game action, no commit.

`OLD` = `%USERPROFILE%/Downloads/givre-GivreActive/Videos/GIVRE/GivreActive`.

`CUR` = `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.

The working transfer is a local replay onto a new Dofus process. The bot’s already-open game TCP stays the only upstream. The visible client never opens that upstream itself. A fight is the state this replay cannot serve: the cache is frozen for the whole fight, the click is refused, and the only fight catch-up in the old tree belongs to a client that was already attached.

## What stays connected

Two different sockets are involved.

The session that stays up is `MITM._serverTcpClient` / `_serverStream`. `PrimaryUpstreamTcpConnected` and `PrimaryUpstreamStreamWritable` read those objects (`OLD/EasyBot/Sockets/MITM.vb:9720-9738`). `TryArmCurrentWorldMirror` returns false unless both are true (`OLD/EasyBot/Sockets/SwitchSocket.vb:85-86`). After the map replay, client bytes are written to that same stream by `TryPassthroughClientGameFrame` (`OLD/EasyBot/Sockets/MITM.vb:9816-9838`). Arm, cancel, and a rejected map replay do not close it. The preserved-session logs are `socket-session-preserved`.

The socket that is closed is the visible client’s first connection, the login connection into Givre. `ChangeGameServer` writes a rewritten SelectServer response, then closes that login `TcpClient` and waits on a new loopback listener for the client’s game connection (`OLD/EasyBot/Sockets/SwitchSocket.vb:4166-4214` and `:1631-1704`). That is the normal login-to-game hop of the visible process. It is not a new login of the bot.

`IMitmRelay` and `MitmTransport` in the current tree do the other design. `LocalHandshakeRelay.ServeAsync` reads the hook header and calls `ConnectAsync` to the real destination (`CUR/GivreClone.App/Services/LocalLaunch/LocalHandshakeRelay.cs:96-140`). `IMitmRelay` is only `Start`, `Inject`, `OnData`, and `OnClosed` (`CUR/GivreClone.App/Services/Transport/IMitmRelay.cs:6-11`). `MitmTransport` forwards those four members (`CUR/GivreClone.App/Services/Transport/MitmTransport.cs:10-42`). Nothing in that interface answers from a cache or attaches the new client to an existing `SocketGameClient`.

The current Socket-to-MITM command is an explicit reconnect. `BotsViewModel.SessionTransitions.cs:25-28` says the visible client is created after the old Socket loop has exited. `AccountsViewModel.SessionTransitions.cs:100-138` cancels `SocketCts`, unregisters the injector, waits for the socket task, forgets the pid, and clears the map. `LaunchTarget` then calls `LaunchCoreAsync` with `sourceAlreadyClosed: true` (`:149-156`). `SessionTransition` will not start the next authentication until that close finishes (`CUR/GivreClone.Engine/Session.SessionTransitions.cs:70-74`). `SocketCharacterSelectionGate` parks one session before character selection and owns no frames (`CUR/GivreClone.App/Services/Transport/Socket/SocketCharacterSelectionGate.cs:7-9`). It is not this handoff.

## Exact mechanism

Stages are a single integer on `SwitchSocket`: 0 off, 1 armed, 2 local character accepted, 3 live forwarding (`OLD/EasyBot/Sockets/SwitchSocket.vb:43-45`). `WorldMirrorPausesScript` is `False` (`:49`). The Lua farm keeps running. Comments that say the script stays paused (`:238-241`) describe a branch that the constant skips (`:166`, `:257-258`).

Operator path:

1. A second MITM click on an already bound window only restores that window (`OLD/EasyBot/Main/DesignSwift/BotformSwift.vb:5617-5627`). The default Socket click minimizes the owned window and leaves the TCP binding up (`:5557-5567`, `SwitchSocket.vb:660-711`). Ctrl-click is the close path.
2. If `PacketAnalyser.lancementfight` is true, the MITM click returns immediately. Chat text: combat in progress, launch ignored, current connection unchanged (`BotformSwift.vb:5630-5633`). `StartDofus` is not called.
3. Otherwise `Program.StartDofus` runs (`BotformSwift.vb:5677`, `Program.vb:4633`).
4. When `MITMAUTO` is set, `EnsureClassicMitmUpstreamReplayReady` runs first (`Program.vb:4674-4678`).
5. If `loadCompletely` is already true, that function calls `TryArmCurrentWorldMirror` (`Program.vb:3782-3791`). Failure completes the classic bootstrap flag, leaves the socket up, and returns false. Success logs `world-mirror-launch:socket-session-preserved` and continues into the normal visible launch (`StartDofusDirectNoZaap`, official-launcher attach, or the Step9 no-Frida path at `Program.vb:4685-4726`).
6. If the launch task exits while the mirror is still pending and no visible client was adopted, `CancelCurrentWorldMirror` runs (`Program.vb:4749-4753`). The five-minute timer does the same (`SwitchSocket.vb:198-207`). Cancel clears stage and snapshots (`:214-226`). It does not close `_serverTcpClient`.

`TryArmCurrentWorldMirror` (`SwitchSocket.vb:80-211`) accepts only when all of these hold:

- stage is 0
- `loadCompletely`, and neither `lancementfight` nor `MITM.IsInFight`
- primary upstream TCP is connected and its stream is writable
- `ViaLogin` is false, `_migration` is false, and no visible client is already bound
- `mapid > 0`, `idperso > 0`
- login replay slots are ready (`InitialLoginReplayReady`, `SelectServerReplayReady`)
- configured protocol scope equals `ScopedProtocol3744` (`:90-92`). Any other scope returns false. Presence of the 3744 types in source does not prove which scope a running process has selected.

It then copies three caches under `replayCacheSync`: `RegisterMessagePacket` (one raw frame per integer id), `RegisterMessagePacketMessage`, and `ShortCutMessage` (`:99-112`). The copy is the snapshot the client will read. The live dictionaries keep updating on the socket side.

The snapshot must already contain non-empty frames for these semantic names (`:125-128`):

- `CharacterListEvent`
- `CharacterSelectionEvent`
- `CharacterLoadingCompleteEvent`
- `MapCurrentEvent`
- `MapComplementaryInformationEvent`

Ids are `Math.Abs(name.GetHashCode())` via `MITM.StringToIntId` (`MITM.vb:6810-6812`). `MapCurrentEvent` is stale unless `TryReadWorldMirrorMapId` returns a map id equal to `world.mapid` (`SwitchSocket.vb:136-141`). `MapComplementaryInformationEvent` is stale only when that parse succeeds and the id differs (`:143-151`). A failed complementary parse is not treated as stale. The parser is `Protocol3744.MapCurrentEvent3744` / `MapComplementaryInformationEvent3744` after `GameFrame.TryExtract3744Envelope` (`:316-339`). The comment at `:144-146` says the 3.7 complementary field tags are unconfirmed. This report does not add field numbers.

`ContextCreationEvent`, `CharacterCharacteristicsEvent`, `SpellsEvent`, and a non-empty shortcut list are logged as `world-mirror-hud-preflight` (`:117-121`). They are not in the required set.

On success the code stores pipeline generation, map id, cell id, and character id (`:155-158`), checks them again (`:347-357`), then publishes the snapshot, sets `Synchronization = False`, stage 1, and `worldMirrorBootstrapOpen = 1` (`:188-196`). While that flag is set and stage is not 3, `SocketKeepsBootstrapDuringMirror` is true (`:72-77`). `MITM.VisibleClientOwnsBootstrap` is then false, so Givre keeps sending its own context and map requests (`MITM.vb:10576-10583`). The comment there states the failure if nobody does: the bot freezes after a map change while the visible client is still on the character screen.

Cache fill, before any mirror exists: every server frame targeted at Client or Koli is passed to `EmuleMITMConnexionRegister` with the raw payload (`MITM.vb:6902-6908`). Recording starts when id `6104` arrives and stops adding new ids when id `226`, `6130`, `6268`, `6622`, `6791`, or `6828` arrives (`SwitchSocket.vb:2857-2908`). Those six numbers are literals. The block does not rename them. Keys that already exist are still replaced, except id `6267`, and except while `lancementfight` is true. The whole update sits inside `If lancementfight = False` (`:2847-2914`). `6104` sets `initialLoginReplayReadyValue`. `4944` sets `selectServerReplayReadyValue` (`:2896-2901`).

Visible client, after it connects to the loopback:

1. Its `LoginMessage` request is classified by `ResolveLoginReplaySlot` (`SwitchSocket.vb:1995-2010`). `Identification` replays cache key `6104` (`:3112-3114`). `SelectServer` loads the cached message under key `4944` and calls `ChangeGameServer` (`:3116-3121`).
2. `ReadGameServerEndpoint` requires a `LoginMessage` response whose `SelectServer` result is success, with a non-empty `Host` and `Ports(0)` (`:1508-1538`). `BuildLoopbackSelectServerRedirect` clones that message and changes only `Success.Host` and `Success.Ports` to loopback (`:1551-1558`). Token, UUID, and other fields stay the clone of the captured message.
3. The accepted game connection is checked against the Dofus process that armed the handoff (`:1656-1659`), then a new read pipeline starts (`:1697-1701`). `ViaLogin` becomes false. No second upstream TCP is opened in `RunGameServerHandoff`.
4. While `Synchronization` is false, game requests are answered from the snapshot (`ServerEmulator`, case 5, `:3152-3383`). They are not written upstream.
5. `CharacterSelectionRequest` at stage 1 is accepted only when its `CharacterId` equals `worldMirrorCharacterId` (`:436-458`, field `CharacterId` on `CharacterSelectionRequest3744`). Stage becomes 2. A mismatch logs `world-mirror-character-selection-rejected` and returns (`:3219-3222`).
6. `MapInformationRequest` at stage 2 calls `EnsureFreshWorldMirrorState(90000)` (`:3350-3356`, timeout constant `:59`). On success it writes the cached `MapComplementaryInformationEvent`. If the world is still valid, it sets `socketype = False`, `Synchronization = True`, and stage 3 (`:3373-3379`).
7. At stage 3, `TryPassthroughClientGameFrame` writes the client’s raw frame to the existing upstream (`:3522-3544`). `IsMirrorBootstrapRequest` drops `IdentificationRequest`, `CharacterSelectionRequest`, `CharacterListRequest`, `AuthenticationTicketRequest`, and `ServerSelectionRequest` (`:3041-3047`, used at `:3529-3531`). A second character selection cannot re-authenticate the socket.

`EnsureFreshWorldMirrorState` (`:424-434`) loops until the snapshot still matches the live session, or `TryRefreshWorldMirrorSnapshot` succeeds, or 90 seconds elapse. Refresh copies the live cache again and requires the same character, the same server-pipeline generation, a connected writable upstream, and a loaded bot that is not in a fight (`:365-416`). It also re-checks the five frames and the map id. The comment at `:58-59` says a fight lasts about 15-30 seconds, the client asks for the map during the fight, and an 8 second wait used to give up and leave the client frozen. The constant is now 90 seconds. The attempt as a whole is abandoned after 300 seconds (`:51`, `:200-207`).

## What the client receives

Bytes are the cached raw frames. `sendPacketById` returns without writing when the id is absent (`SwitchSocket.vb:3765-3772`). `TrySendReplayPacketById` is the strict variant: a miss or a failed write aborts that request handler.

### Login, before the game connection

| Client request | What is written | Source |
|---|---|---|
| `LoginMessage` / Identification | cached frame at key `6104` (slot name `initial`) | `SwitchSocket.vb:3112-3114`, ready flag `:2896-2898` |
| `LoginMessage` / SelectServer | clone of cached `LoginMessage` at key `4944`, `Success.Host` and `Success.Ports` set to the reserved loopback port | `:3116-3121`, `:1541-1558` |

The source does not give protobuf type names for keys `6104` and `4944` beyond those slot names.

### Character list

On `CharacterListRequest`, if stage is 1, these cached frames are sent when present, then the handler returns and does not set `Synchronization` (`:3185-3204`):

- `QueueStatusEvent`
- `CharacterListEvent`
- `CharacterListWithRemodelingEvent`
- `GameActionItemListEvent`

The visible character screen produces the later selection. The socket does not send another selection upstream.

### Character and inventory

On `CharacterSelectionRequest`, after the local id check, the handler sends the following cached frames when the id is present (`:3224-3297`). Names are the identifiers in that block.

Always attempted from cache: `QueueStatusEvent`, `CharacterCharacteristicsEvent`, `HavenBagRoomUpdateEvent`, `CharacterExperienceGainEvent`, `NotificationsEvent`, `ArenaUpdatePlayerInformationEvent`, `AchievementsEvent`, `TagStoragesRefreshEvent`, `EmotesEvent`, `JobDescriptionEvent`, `JobExperiencesUpdateEvent`, `JobCrafterDirectorySettingsEvent`, `PrismListEvent`, `ChatChannelsEnabledEvent`, `ZaapKnownListEvent`, `AlignmentRankUpdateEvent`, `InventoryWeightEvent`, `SequenceNumberEvent`, `TextInformationEvent`, `SpouseStatusEvent`, `CharacterCapabilitiesEvent`, `AlmanachCalendarDateEvent`, `HouseAccountEvent`, `ExchangeMoneyMovementLimitEvent`, `HavenBagPackListEvent`, `HavenBagRoomUpdateEvent` (second time), `StorageKamasUpdateEvent`, `CharacterAppearanceCollectionEvent`, `CosmeticInventoryLoadedEvent`, `OutfitsLoadedEvent`, `OutfitAuraChangedEvent`, `OrnamentSelectErrorEvent`, `TitleSelectErrorEvent`, `OutfitEntityLookChangedEvent`, `TitlesAndOrnamentsEvent`, `HaapiSessionEvent`, `GameActionItemListEvent`.

Strict writes, which abort the handler on failure:

- `CharacterSelectionEvent` (`:3229-3230`)
- `CharacterLoadingCompleteEvent` (`:3282-3283`)

Inventory (`:3236-3256`):

- If the snapshot contains `InventoryContentEvent`, that exact frame is required. A failed write logs `world-mirror-inventory-replay-failed:socket-session-preserved` and calls `CancelCurrentWorldMirror`.
- If the key is absent, the code logs `world-mirror-inventory-snapshot-missing:legacy-fallback` and calls `TrafficInventoryContent`.
- `TrafficInventoryContent` (`:3656-3679`) builds `InventoryContentEvent` from `PacketAnalyser.kamas` and parallel lists `inventoryitem`, `inventorypostion`, `inventoryitem2`, `inventoryquantity`, plus effects from `GameMessageParser.AllObjectsEffects` keyed by uid. The comment at `:3236-3239` says this projection can omit 3.7 fields and leave an inventory that does not open as usable contents. The five-name preflight does not require `InventoryContentEvent`, so this fallback is reachable.
- `QuestsEvent` and `QuestsFollowedEvent` are then attempted from cache. A miss is silent.

`YoshiRequest` or `ClientIdRequest` replays the subscription bootstrap frame (`:3166-3182`). For scope `ScopedProtocol3744` the wire alias is `meq`, and only when the 3744 registry maps `meq` to `AccountSubscriptionElapsedDurationUpdateEvent` (`:485-499`). Scope `ScopedProtocol361112` uses alias `mee`. The arming guard has already required 3744, so a mirror that reached this point is on the `meq` path when that registry check passes. A miss falls back to the hash id of `AccountSubscriptionElapsedDurationUpdateEvent`.

### Map

On `ContextCreationRequest` (`:3303-3328`): cached `ContextDestroyEvent`, `ContextCreationEvent`, `LifePointsRegenBeginEvent`, `NpcsMapQuestStatusUpdateEvent`, then a strict `MapCurrentEvent`, then cached `TimeEvent`, `CharacterCharacteristicsEvent`, `SpellsEvent`, then each raw shortcut frame from the snapshot (`ShortcutBarContentEvent` frames accumulated at `:2860-2862`).

On `MapInformationRequest` (`:3350-3379`): a strict `MapComplementaryInformationEvent`, then cached `AreaFightModifierUpdateEvent`. If the live session still matches, stage becomes 3 and the log is `world-mirror-live-forwarding:socket-session-preserved` plus `world-mirror-hud-not-verified:map-transport-ready-only`. The second line means the map bytes were written. It is not a check that the Unity HUD finished initializing (`:114-116`).

If the 90 second wait cannot find an idle matching world, the handler logs `world-mirror-map-replay-rejected:world-changed` and returns (`:3351-3355`). `Synchronization` stays false. The upstream stays up. If the world changes during the complementary write, `CancelCurrentWorldMirror` runs (`:3367-3370`).

## Why a fight breaks it

The state that cannot be replayed is the in-progress fight. The mirror serves one last world snapshot per message id. It does not serve an ordered fight.

Three source facts:

1. The click never arms during a fight. `BotformSwift.vb:5630-5633` returns while `lancementfight` is true. `TryArmCurrentWorldMirror` would also return false, because it requires `Not lancementfight` and `Not IsInFight` (`SwitchSocket.vb:84-85`). `IsCurrentWorldMirrorStateValid` and `TryRefreshWorldMirrorSnapshot` repeat that requirement (`:350-351`, `:370`).

2. Fight traffic never enters the dictionary. `EmuleMITMConnexionRegister` skips every cache write while `lancementfight` is true (`:2847-2914`). New ids are also not inserted after `ListenPacket` is cleared (`:2906-2908`), which happens once the world bootstrap ids listed above have been seen. A fight is a sequence of later messages. Overwriting one slot, or skipping the sequence entirely, cannot reconstruct join, placement, turns, and actions. The required arming set is the five world frames. No fight message is in that set.

3. The client is then switched to live forwarding of the socket that is already inside the fight (`:3373-3378`). A client that was just given `MapCurrentEvent` and `MapComplementaryInformationEvent` is on a map. The source refuses to do that handoff while the fight flags are set, and labels a failed wait `world-changed`. The 90 second wait exists so the client’s map request, which arrives during a short fight, can be answered with the post-fight map (`:58-59`, `:419-422`). A longer fight expires the wait. The five-minute attempt timer can also cancel the mirror while the client is still on the character screen.

The other fight path is not a hot attach. `ResumePreservedVisibleClientAfterAuthoritativeFightSnapshot` (`SwitchSocket.vb:1487-1488`) only flushes `preservedReconnectCatchupFrames`. Those frames are buffered when an already-owned visible client has been suspended across an upstream reconnect (`:1292-1316`, `:2828-2843`). The flush runs from `PacketAnalyser.paq5921` after a `GameFightSynchronizeMessage` whose roster contains the current character (`PacketAnalyser.vb:7614-7628`, `:7631-7750`). The call is guarded by `VisibleClientReconnectSuspended` (`:7746-7750`). A newly launched mirror client never fills that buffer.

After a fight-snapshot flush, `ShouldSuppressPostFightCatchupBootstrapRequest` drops wire codes `ksv`, `kom`, `ktc`, `kkv`, `iir`, and `kkg` for that pipeline generation (`SwitchSocket.vb:1249-1260`). The comment says that burst was observed after fight-snapshot replay and does not belong on an already-active fight upstream. The guard is active only while `lancementfight` remains true (`:1242-1246`). That is evidence the old client, once it has been shown a fight catch-up, must not restart character and context bootstrap. It is not a recipe for building a fight for a client that was never in it.

A fresh server login can put a character that is still in combat back into the fight. The current socket client already records that shape: `CharacterForceSelectionEvent` is followed by `FightJoinRunningEvent`, and the injector treats `FightJoinRunningEvent` as an alternative to `ContextCreationEvent` (`CUR/GivreClone.App/Services/Transport/Socket/SocketGameClient.cs:980-1001`). The hot mirror never sends `CharacterSelectionRequest` upstream (`SwitchSocket.vb:3393-3398` and `:3529-3531`), so the server never starts that rejoin for the new process.

`ShouldDropVisibleClientFightControl` (`SwitchSocket.vb:3048-3077`) is a later filter for a client that is already in stage 3. It drops the visible client’s `GameActionAcknowledgementRequest` and `FightTurnReadyRequest` while the bot is fighting with a tactic other than `NoAction`, and it drops `FightTurnFinishRequest` in that same case. That explains double ACKs after a successful attach. It does not put a loading client into a fight.

## Ranked fixes

### 1. Wait until the fight ends, then replay the refreshed world snapshot

This is the behavior the old source already aims at, and the smallest change that preserves the open socket.

The gap is the click. `BotformSwift.vb:5630-5633` returns before `TryArmCurrentWorldMirror`, so the 90 second wait inside `EnsureFreshWorldMirrorState` never starts when the operator clicks during a fight. The wait only helps when the fight begins after stage 1 or 2.

Keep the map write behind `Not lancementfight` and `Not IsInFight`. After the flags clear, call the same refresh the source already has: recopy the cache, require the five frames, and require `MapCurrentEvent`’s parsed map id to equal the live `mapid`. Then send that post-fight `MapCurrentEvent` and `MapComplementaryInformationEvent` and only then set stage 3. Extend the wait to the existing attempt budget (`WorldMirrorAttemptTimeoutMs`, 300000) or until the fight flags fall, whichever policy is chosen, and cancel the mirror without closing the upstream if the budget expires. The log line to keep is `world-mirror-map-replay-rejected` or `world-mirror-bootstrap-timeout:socket-session-preserved`.

Do not forward fight deltas to a client that has only been given the map snapshot. Do not synthesize a fight join.

### 2. Disconnect the socket immediately before the visible client connects

This is what the current tree does (`AccountsViewModel.SessionTransitions.cs:100-138`, then `LaunchCoreAsync` at `:155-156`). A new login can receive the server’s own fight rejoin, which the current socket comments name as `CharacterForceSelectionEvent` then `FightJoinRunningEvent` (`SocketGameClient.cs:980-1001`). That is a real server sequence on a new connection. It is a different session: the socket task is cancelled, the injector is removed, and the map store is cleared.

`EnsureClassicMitmUpstreamReplayReady` exists to avoid that. Its comment says replaying login against an upstream that is already past character selection recreates the visible/internal desynchronization, and it refuses to fabricate a rewind (`Program.vb:3776-3786`). Use a disconnect only as an explicit fallback after the wait in fix 1 expires, with a status that says the hot session was dropped. It should not be the default for a loaded socket.

### 3. Replay fight state into the newly launched client

The hot path has no fight snapshot to replay. The dictionary is not updated during `lancementfight`. The reconnect catch-up buffer exists only for a visible client that was already bound and then suspended. `paq5921` applies `GameFightSynchronizeMessage` to the bot’s own roster and then flushes that raw buffer. It does not build a fight for a process that has just opened.

Replaying `GameFightSynchronizeMessage` alone, or appending live fight frames after `MapComplementaryInformationEvent`, is a new protocol. The bootstrap-suppression list (`ksv`, `kom`, `ktc`, `kkv`, `iir`, `kkg`) shows the opposite constraint: once the upstream is in a fight, a client that restarts character and context bootstrap must not send those requests upstream. This report does not specify fields for a synthetic fight join.

## Smallest port into the current tree

Leave cold MITM launches on `LocalHandshakeRelay` and `MitmTransport`. A mirror is a second acceptor used only when a socket is already in the world for the same server and character. Do not teach `ServeAsync` to skip `ConnectAsync` for every client.

Files:

| File | Change |
|---|---|
| `CUR/GivreClone.App/Services/LaunchService.Packets.cs` `FeedSocketFrame` (`:134-138`) | This is the raw server-to-client wire entry for a socket pid. Copy those bytes into a per-pid last-frame map keyed by the message name the existing decoder already produces. Skip the copy while `IsInFight(pid)` is true, matching `SwitchSocket.vb:2847`. Keep the required names as the five strings above. Do not add a 3744-only parser; current scope has to be checked the way `TryArmCurrentWorldMirror` checks `ScopedProtocol3744`. |
| New type next to the socket transport, for example `GivreClone.App/Services/Transport/Socket/WorldMirrorSession.cs` | Stage, pipeline generation, character id, and the snapshot. Accept selection only when the decoded character id equals the live id. Serve map bytes only when `IsInFight` is false and the cached map id matches. Expose “forwarding enabled” for stage 3. |
| `CUR/GivreClone.App/Services/LaunchService.cs` around `:605-635` | When the transition is a same-identity socket attach, do not construct a `LocalHandshakeRelay` that dials the real host. Accept the hooked client locally, write snapshot bytes to it, and after stage 3 write client frames through the existing `SocketGameClient.Inject` (`SocketGameClient.cs:216`). Server frames that already reach `FeedSocketFrame` are also written to the visible client once forwarding is enabled. |
| `CUR/GivreClone.App/ViewModels/AccountsViewModel.SessionTransitions.cs` | `CloseSource` (`:107-121`) must not cancel `SocketCts`, unregister the injector, or `Forget` / `ClearMap` for this mode. `LaunchTarget` (`:152-156`) must not call `LaunchCoreAsync(..., sourceAlreadyClosed: true)`, because that starts a new authentication. `Busy()` (`:65-67`) already rejects `IsInFight`. For fix 1, wait there instead of returning "Source session is busy", and still refuse movement, exchange, and NPC dialog. |
| `CUR/GivreClone.App/ViewModels/BotsViewModel.SessionTransitions.cs:25-28` | Replace the comment that says the socket loop exits before the visible client is created. The engine state machine can stay: `commitAndClose` may be a no-op, and `Session.SessionTransitions.cs:72-73` still forbids a second login while the socket owns the upstream. |
| `CUR/GivreClone.App/Services/Transport/IMitmRelay.cs`, `MitmTransport.cs`, `LocalHandshakeRelay.cs` | No behavior change on the cold path. They remain the splice that opens a new upstream. |
| `CUR/GivreClone.App/Services/Transport/Socket/SocketCharacterSelectionGate.cs` | Leave it. It has no frames and no streams. |

Do not port `TrafficInventoryContent`. Require the cached `InventoryContentEvent` frame the way the strict branch already does, and fail the arm when it is missing. Do not port the Lua-pause comments. The constant that runs is `WorldMirrorPausesScript = False`.

`ScopedProtocol3744` is a hard gate in the old arming function. The current catalog and the selected runtime scope have to be the same identity before any of those cached frames are written to a client. A failed `MapComplementaryInformationEvent` map-id parse must stay “not stale”, as in `SwitchSocket.vb:143-151`, until the current schema is actually confirmed.

## Deterministic tests

No test was run for this report. Existing current coverage is the close-then-launch transaction in `CUR/GivreClone.Tests/SessionTransitionTests.cs`. The old handoff harness `OLD/tools/SwitchSocketHandoffTests/Program.cs` reflects into `Givre.exe` and exercises loopback redirect, replay-slot flags, and pipeline retirement. It does not call `TryArmCurrentWorldMirror`.

Add tests beside `SessionTransitionTests`, with in-memory stages and byte arrays. No sleeps and no sockets to a game host.

- Arm fails when any of the five frames is missing or empty, and when the decoded `MapCurrentEvent` map id differs from the live map id.
- A server frame delivered while the in-fight flag is true does not replace the stored `MapCurrentEvent` bytes. The next `MapCurrentEvent` after the flag clears does replace them.
- A selection whose character id differs does not advance to stage 2 and writes nothing to the injector.
- A map request while the in-fight flag is true writes no `MapComplementaryInformationEvent` and leaves forwarding off. After the flag clears, the bytes written equal the post-fight cache entry.
- At stage 3, a `CharacterSelectionRequest` is not passed to `Inject`. One ordinary client frame is passed once.
- The socket-to-MITM delegate list for a same-identity mirror contains no socket-cancel and no `UnregisterSocketInjector`. The existing tests that expect `close` then `launch` stay on the explicit reconnect path.
- An inventory fallback builder is absent: arm fails closed when `InventoryContentEvent` was never cached.

## Live checks

These are operator checks. They were not performed here.

Out of fight, same account already loaded in Socket:

- Status stays connected on the same character id and server id the whole time. The socket task is still the same object.
- The new Dofus window reaches the same map id the socket already has.
- Logs, if the old app is the one under test: `world-mirror-armed`, then `world-mirror-live-forwarding:socket-session-preserved`. `world-mirror-hud-not-verified:map-transport-ready-only` means stop at “map bytes were written”. Look at the window separately.
- Inventory opens with the same contents the socket had. If the old app logs `world-mirror-inventory-snapshot-missing:legacy-fallback`, the strict frame was absent and the reconstructed inventory is the known weak path.

During a fight:

- On the old app, the MITM click produces the combat-ignored chat line and does not start Dofus. The socket status is unchanged.
- If a fight starts after the mirror is armed and before the map request, the client waits. When the fight ends inside the budget, the client gets the new map and the socket is still the original one. When the budget expires, the expected logs are `world-mirror-map-replay-rejected:world-changed` or `world-mirror-bootstrap-timeout:socket-session-preserved`, and the socket is still connected.
- A visible client that appears inside the fight without a new login is outside what this source implements. Treat that result as a different session, not as proof the cache replayed the fight.

After a successful attach, a later fight is the already-bound filter in `ShouldDropVisibleClientFightControl`. Duplicate turn-ready and acknowledgement from the visible client are dropped while the bot’s tactic is not `NoAction`. That check is separate from the hot attach.

## Limits

- Locations are source locations. No running process was observed, and no test binary was executed.
- `TryArmCurrentWorldMirror` refuses every scope other than `ScopedProtocol3744`. A cache full of frames is not enough.
- Keys `6104` and `4944`, and the `ListenPacket` stop ids, are cited as the literals in `EmuleMITMConnexionRegister`. They are not given modern message names in that block.
- The complementary map-id parse is allowed to fail without rejecting the snapshot. That is a stated uncertainty in the old comment, not a verified 3.7 schema.
- `docs/codex/GIVREACTIVE_PARITY_20261031.md` already classifies this feature as a protocol-conditional replay and the current tree as a parking primitive. This report is the mechanism, the fight gap, and the port slice.
