# Jondo local startup

Started the existing isolated source laboratory at revision
683ab49c73f65b7c4ab7c1134c448f7fa850f6bc using Release dotnet run of
Jondo.Unity.Server, with JONDO_PUBLIC_BIND=0. The owned server process is
17312, launched under monitored session bash_677. No official game client
was modified or redirected.

Observed HTTP GET http://127.0.0.1:8888/api/estado returned 200 with
enLinea=true, base_=true, servicios=true, version=3.6.10.10 and proceso=17312.
Process inspection confirmed that PID's executable belongs to this isolated
source laboratory. Get-NetTCPConnection confirmed 15881, 6337, 5556 and
5555 bound to 127.0.0.1 for that PID. HTTP uses HttpListener with localhost
and 127.0.0.1 prefixes when public mode is disabled, as checked in source.

The shipped laboratory account authenticated successfully through api/entrar.
Its session retrieved four characters through api/personajes, HTTP 200:
13825558 (level 204, breed 9), 13825563 (level 200, breed 8),
13825564 (level 200, breed 13), 13825565 (level 202, breed 4).
The returned session token is not recorded here.

Started the existing PullTests, PushDamageTests and TeleportSymmetryTests
through dotnet test, Release, no restore. All 30 tests passed, zero failed or
skipped, exit 0, duration 4.2 seconds (build and test 6.5 seconds), bash_680.
TRX destination:
%USERPROFILE%/AppData/Local/Temp/givre-jondo-lab-20261102/results/jondo-local-displacement-20261103.trx.

These are emulator engine scenarios, not Givre-to-emulator integration
or authoritative official-server parity. This checkout supports client
3.6.10.11/3.6.10.10, not the currently reported installed 3.7.12.12 client.
A compatible graphical client has not been installed in this experiment.

## Isolated compatible client preparation

The user clarified the goal: exercise Givre combat and expeditions, including
dungeon entry, NPC dialogue, room transitions, exit, death and recovery, with
adjustable money and equipment. Those integration scenarios remain open.
Jondo exposes administrator commands .kamas, .level, .item and .itemset;
their existence is not evidence that Givre has executed these scenarios.

Fetched the 3.6.10.11 manifest from Ankama's Cytrus CDN (51,867,544 bytes).
The installed official client reports 3.7.12.12. Measured fragments excluding
optional map_textures_2x and map_textures_4x total about 12.85 billion bytes;
free space before installation was 16,696,291,328 bytes. The laboratory tool
requires the full selected size plus 2 GiB free before starting, copies only
SHA-1-matching files from the official client, and uses Jondo's existing
PackDownloader for remaining files with chunk and full-file verification.
Files are copied, not hard linked. Official client files are not modified.

Destination: isolated lab source/Cliente 3.6.10.11, beside the emulator source.
Installation is running under bash_686. Animations completed; audio is in
progress. No client completeness claim yet. MelonLoader 0.7.3 x64 download
is separately pending under bash_687; release metadata publishes SHA-256
5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416.

The initial startup monitor reached its default deadline and stopped the
server. It was relaunched without build in persistent session bash_682;
the original PID above is historical, not a current identity assertion.

## Current-client compatibility check

After the user suggested using the installed 3.7.12.12 binary, checked the
current upstream README and Jondo.Unity.Core/Paths.cs on main, not only the
local historical checkout. Both still explicitly select 3.6.10.11 and
3.6.10.10. The inspected local connection mapping directory contains only
connection-3.6.10.11.json. No 3.7.12.12 support was established. Merely
changing the launch executable would not establish protocol compatibility.

Sources checked:
https://raw.githubusercontent.com/Keka-Bron/JondoEmu/main/README.md
https://raw.githubusercontent.com/Keka-Bron/JondoEmu/main/Jondo.Unity.Core/Paths.cs

MelonLoader 0.7.3 x64 SHA-256 matched the release metadata exactly and was
extracted only into the isolated historical client. Audio and animations
have completed; bones installation is still running. The official Jondo
v0.2.1 launcher archive was downloaded to obtain its bundled JondoFix;
archive contents are being inspected before extraction.

The archive entry JondoEmu/JondoFix/JondoFix.dll (157,696 bytes) was extracted
only into the laboratory client's Mods directory; extraction exited 0.
The persistent server now responds as PID 18872, with all status flags true.
JondoFix supports JONDO_EMULATOR_ROOT; the eventual client launch must point
it at the isolated source root rather than relying on its folder-name search.

Givre integration has a second compatibility boundary beyond starting the
graphical client. GivreClone.Protocol/GameCodec.cs explicitly describes its
generated codec as frozen on 3.6.10.11 and unused in production; current
EnvelopeBuilder resolves fields through the active catalogue, and the
MapInteractivesReader documents changed field positions after 3.6.10.11.
These facts do not prove complete historical protocol support. A successful
Jondo client login must not be reported as a successful Givre test until
the active catalogue, decoders and emitted actions are checked against it.

Later preparation check: animations, audio, bones, configuration, data, i18n,
map_common and picto finished; skins is installing. The launcher script
parsed successfully under PowerShell and .NET 8/10 runtimes are installed.
Filesystem inspection found Mods but no MelonLoader directory despite the
earlier Expand-Archive exit 0. Therefore MelonLoader installation is NOT yet
verified. A read-only Windows Defender detection query is pending to
distinguish removal/quarantine from an extraction issue; no exclusion,
restoration or alternate extraction has been attempted around this finding.
Free space at that check was 7,202,201,600 bytes.

Installation completed successfully under bash_686: every selected fragment,
including win32_x64, was installed and CLIENT_INSTALLED was emitted, exit 0.
The repeated extraction with ErrorActionPreference=Stop produced the expected
MelonLoader runtimeconfig and exited 0. The full laboratory-specific Defender
query returned NO_LAB_DEFENDER_DETECTION. Runtimeconfig requires .NET 6 with
LatestMinor roll-forward; installed 8/10 runtimes do not satisfy that contract.
A private .NET 6.0.36 x64 runtime archive download is pending under bash_698;
the lab launch script points DOTNET_ROOT and DOTNET_ROOT_X64 at client/dotnet.

Private runtime extraction passed and reported Microsoft.NETCore.App 6.0.36.
Start-LabClient.ps1 launched owned client PID 20500 under bash_700. Its log
confirms HostFXR from the private runtime, MelonLoader 0.7.3 and Unity
6000.3.16f1 from the isolated path. Cpp2IL finished dumping the installed
client in 30.6 seconds; interop generation is still running under the
client, observed by persistent log subscription bash_701. No server-selection
or world-entry success has been observed yet.

Started Jondo's existing protocolbuilder proto command against the newly
dumped Ankama.Dofus.Protocol.Game.dll. This creates a separate lab schema
client-game-3.6.10.11.proto, not a replacement Givre catalogue. Its result
is pending under bash_702. Semantic message/field names and admission remain
unresolved; extracting obfuscated wire tags alone does not solve them.

Protocol extraction finished successfully: 2,169 messages, 6,186 fields,
550 enums, exit 0. A comparison with Jondo's shipped schema is pending.
Source inspection of datos/anclas_3.6.10.10.tsv explicitly distinguishes
measured message meanings from proposed semantic names. The orphan names
in nombres_reales_3.6.10.10.tsv cannot be paired to opcodes from metadata
alone. These proposals are not being promoted to verified Givre mappings.

Comparison completed: SCHEMA_EQUAL_NORMALIZED=True after stripping CR
characters only. The extracted schema and the shipped schema differ in
line endings, not content. Interop generation finished at 13:08:41 and
JondoFix loaded. Subsequent initialization reached Local Emulator Active=True,
Jondo Administrator=True and DNS/socket redirection ACTIVE at 13:13:51;
the client attempted its loopback ZAAP connection on port 15881 at 13:14:04.
An initial non-responsive probe was followed by Responding=True, so it is
not established as a persistent hang. No matching Windows application crash
event was returned. Desktop screenshot/AX/window enumeration subsequently
timed out, preventing visual confirmation through that channel. The earlier
control API probe still returned no connected character; a post-initialization
probe is pending. Givre integration remains unverified.

The post-initialization API still returned no connected character. Unity logs
show an absent bones_assets_boneindexdata.bundle, missing audio banks, and
ZAAP Internal error. This supersedes any interpretation that CLIENT_INSTALLED
proved launch readiness. Added a read-only manifest audit to the local probe;
animations passed, audio reports 5,412 invalid files, with absence of 100.bank
confirmed separately on disk. Audit of remaining fragments is running under
bash_709. The probe now supports repair of only missing/hash-mismatched files
with per-fragment free-space checks; repair has not been run yet.

The ZAAP registry source independently shows a five-minute idle launch expiry.
This client's first handshake occurred over eleven minutes after registration,
consistent with expiry during first interop generation. A fresh launch after
asset repair will test that explanation without weakening authentication.
Requested stopping only the isolated executable, with normalized full-path
identity check, under bash_712 before any repair or relaunch.

The full audit completed under bash_709: audio 5,412 invalid and bones 1
invalid; all other selected fragments reported zero invalid files. The first
repair build failed MSB3027/MSB3021 because the audit's ClientProbe process
still held the apphost executable. No repair ran in that failed invocation.
After the audit exited, started a fresh repair under bash_714; it has passed
the animations fragment and is processing remaining fragments. Do not rebuild
the same output directory until that process exits. The next audit build
now reports AUDIT_COMPLETE and returns exit 1 if any manifest file is invalid.
The launch script additionally rejects missing bone index and reference audio
assets; its updated PowerShell parser check passed under bash_715. The old
client process is stopped, and the noisy obsolete log subscription was killed.

Repair progress: audio now reports all 1,984,030,640 bytes reusable and no
download required; this includes valid existing files plus copies from the
official installation that match the historical manifest SHA-1. This is not
yet the final audit verdict. The isolated official version marker reads
Version=3.6.10.11 and BuildDate=2026-08-24T11:33:20+00:00. Givre's installed
version probe can consume that marker with hashes of the local executable,
GameAssembly and metadata, so missing local version evidence is not the
remaining catalogue obstacle. Full historical semantic fields and mapping
publication requirements still need satisfying before Givre admission.

The running repair also reports the entire bones fragment reusable after
checking/copying, including its previously absent index, followed by valid
configuration, data, i18n, map_common and picto fragments. It has not emitted
its final completion sentinel yet. No concurrent rebuild or client launch
was attempted during this repair. The repaired client must pass the explicit
post-repair manifest audit before its next launch.

Repair bash_714 completed successfully, exit 0, with every selected fragment
fully reusable and CLIENT_INSTALLED emitted. Started the final full SHA-1
audit under bash_716 using the updated nonzero-on-invalid verdict. Its outcome
is pending; no relaunch or world-entry claim is made at this checkpoint.

The post-repair audit has now independently reported audio invalid=0
(previously 5,412), as well as animations invalid=0. Remaining fragments
are still being hashed. The next launch must use fresh registration after
the complete audit, not credentials from the expired first invocation.

Post-repair audit subsequently confirmed bones, configuration, data, i18n,
map_common and picto invalid=0. Skins, start-script and win32_x64 plus the
aggregate verdict are still pending at this checkpoint. No game process is
being kept alive against the already expired first-launch registration.

Final post-repair audit bash_716 completed: every selected fragment invalid=0,
AUDIT_COMPLETE invalid=0, exit 0. Started a fresh client launch under bash_717
using the existing launch script and a newly registered ZAAP instance. Armed
a bounded control-API subscription for an actually connected character under
bash_718. Client launch and API world entry remain distinct verification gates;
the latter has not fired at this checkpoint.

World entry is now independently confirmed by the control API subscription:
LAB_WORLD_CONNECTED for the caller's [#KEKA-BRON#], level 204, map 197918722,
cell 339, not imprisoned. The subscription exited 0. The repaired second
launch used cached interop generation and passed the previous ZAAP handshake
failure. A nonfatal updater_isUpdateAvailable unknown-result warning remains
in the client log; world entry proves it did not prevent this connection.
This validates the local client-to-emulator path, NOT Givre-to-emulator
integration, combat automation, dungeon traversal or expedition recovery.

The user independently confirmed selecting Talkasha on the server-selection
screen and then an already created character. Thus the observed API entry
followed manual server and character selection, not unattended automation.
The online sheet reports 152,289,583 kamas, so no currency grant was needed.
The current map contains level-200+ groups. A read-only coordinate lookup
resolved (4,-19) to map 191104000 for a simpler future test location; no
teleport, equipment grant or fight has been triggered at this checkpoint.

Local administration teleport subsequently succeeded, bien=true. A separate
conectados response confirms the same character on map 191104000, cell 327,
level 204. No stats, level or equipment were changed. This demonstrates the
lab's normal map-change path and resource-control API, not Givre movement.
The existing Givre socket review preparation explicitly rejects cross-family
reviews, so its 3.7 receipt cannot admit this 3.6 catalogue. No receipt was
fabricated or version check relaxed.

Further integration inspection identified two independent barriers, not just
a missing game catalogue. SocketConnectionClient hardcodes the official
connection hostname and port 5555; ConnectionMessages.BuildIdentification
hardcodes the current login layout (device #1, token request #3, client
version #5). Jondo has a separately versioned semantic connection mapping
and generated codec under Jondo.Unity.Protocol.Connection.Wire. A legitimate
historical Socket integration must therefore select a local endpoint and
version-checked connection codec as well as provide the complete game mapping.
No official hostname redirection, cross-family admission override, or catalogue
relabeling was performed. The current official Socket behavior is unchanged.

Direct schema comparison narrows the codec concern: the inspected historical
request envelope, correlation, identification, device, token/shield and version
tags match the current encoder (root request #1; request correlation #1,
identification #3, selection #4; device #1, token method #3, version #5).
Therefore these inspected fields do NOT establish a codec incompatibility.
Response decoding and game-node fields remain to be compared; a separate
historical codec should not be added unless that comparison demonstrates need.

Differential response probe completed under bash_723, exit 0:
HISTORICAL_CONNECTION_RESPONSES_PASS identification selection. The lab-only
connection-probe project links the actual Givre ConnectionMessages and
ProtoWire sources, and feeds them bytes emitted by Jondo's generated 3.6.10.11
ConnectionCodec. It asserts embedded character identity/class/level, online
server ID, rights, and the selected game host/port/ticket. All values are
synthetic, no authentication token or second live session was involved.
This covers successful identification and server selection only; it does
not certify all errors/events, a real Givre login, or any game-node message.

The reverse-direction probe also passed under bash_724: actual Givre
BuildIdentification/BuildSelectServer frames were deframed and decoded by
Jondo's generated codec with the expected correlation, device, synthetic token
and selected server ID. No alternative login codec is needed for these cases.

Live transport probe bash_725 completed with exit 0 and
LIVE_LOCAL_CONNECTION_PONG_PASS 127.0.0.1:5555. It sends an unauthenticated
Ping to the real loopback listener, reads one bounded varint-framed response
under a five-second cancellation deadline, and verifies Pong with both the
actual Givre decoder and Jondo's codec including correlation. It does not
log in, issue a game ticket, displace the connected client, or send gameplay
commands. This proves the local connection listener/framing path only.

Bounded catalogue coverage inspection found 317 distinct message names in
Givre's generated RequiredMapping.Scanned field inventory, 294 Jondo anchor
rows, and 2,169 structural old-to-new message pairs. None of the named anchor
rows exactly matches a message name in that required-field inventory. This is
not evidence that the messages differ semantically: Jondo's anchors largely
use proposed Dofus-2-style names (for example GameMapMovementRequestMessage),
where Givre requires current semantic names and named nested fields.
MapMovementConfirmRequest is a matching name but has no fields and therefore
does not occur in that inventory. It must not be counted as missing support.
Consequently simply joining these TSVs cannot produce a complete fields.json;
semantic correspondence and nested-field verification remain necessary. No
zero-field placeholder catalogue or relaxed publication gate was introduced.

A lab-only movement subset now verifies the actual MoveForge and EnvelopeBuilder
against the repository's historical 3.6.10.11 generated protobuf classes. Sources
for the subset's tags/aliases are the installed schema, WorldMoveHandler and
the frozen historical proto. The initial byte-equality check failed solely
because MoveForge serializes key_cells before map_id whereas generated protobuf
serializes ascending tags. No production forge was changed: protobuf accepts
either field order. The corrected probe deframes and decodes with generated
GameMessage/MapMovementRequest and verifies UID, type URL, map and packed path
values, plus the empty confirmation frame. bash_730 passed with exit 0:
HISTORICAL_MOVEMENT_FORGE_PASS decoded-walk packed-directions confirmation.
This subset runs only in its own process; GameCodec remains unconnected to
production and no movement bytes were sent to the game. It is not a published
catalogue, a complete mapping, or live Givre movement evidence.

A distinct public source was identified after the scoped local schema search:
https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.6.10.11
GitHub release 376246341 publishes dofus.proto asset 528880340, 195,261 bytes,
SHA-256 5d63a44a0c055715359ea73c3e412ffd832325002ad2cd4e45cc21c3f63acf70.
Download bash_732 completed with exit 0. The independent file-size and SHA-256
check under bash_733 also exited 0 with PUBLIC_SCHEMA_HASH_OK. Reading the
downloaded file shows obfuscated message and field names (mhh, gfcd, etc.);
the scoped search found no MapMovementRequest, CharacterSelectionRequest or
map_id names. This is not the missing named schema and adds no established
semantic mapping coverage. Structural correspondence with the installed client
has not been verified for this public file.

## Historical combat forge subset

The isolated movement-probe now also invokes the actual MoveForge methods for
spell casting, placement, ready/unready and turn finish. Combat aliases and tags
were cross-checked against the extracted client schema and Jondo FightProtocol:
jwh cell #1 / spell #4, jzy entity #1 / cell #2, kaq ready #1; jxy is empty.
The probe checks framing length, request UID, type URL and the exact decoded
varint field set, including empty unready and turn-finish payloads.

bash_737 exited 0 with HISTORICAL_MOVEMENT_FORGE_PASS and
HISTORICAL_COMBAT_FORGE_PASS cast placement ready unready turn-finish.
This is a lab-only catalogue subset in a separate process. It neither publishes
a complete historical catalogue nor sends actions to Jondo. Combat acknowledgments,
incoming fight events and an actual Givre game session remain unverified.

The follow-up probe under bash_738 also exited 0, retaining both previous
PASS markers and adding HISTORICAL_COMBAT_ACK_FORGE_PASS sequence-ack turn-ready.
It verifies GameActionAcknowledgementRequest -> jti (valid #1, action_id #2)
and FightTurnReadyRequest -> jwz (is_ready #1), against extracted schema types
and the Jondo sequence/turn acknowledgement routes. The synthetic sequence ID
is 42. These checks still send no packet to the server; incoming events and an
actual Givre game session remain unverified.

## Incoming sequence compatibility boundary

The live packet handler reads SequenceEndEvent.author_id, action_id and
sequence_type through FieldOf before issuing acknowledgments and releasing
the action barrier (LaunchService.Packets.cs, sequence handling). The current
data/fields.json assigns these fields to #4, #2 and #3 respectively. Historical
Jondo FightProtocol.BuildSequenceEnd and the extracted jwi schema instead use
author #2, action #1 and type #3. SequenceStartEvent also differs: current
author #2 / type #1, historical jto author #1 / type #2.

Therefore reusing the current incoming catalogue with historical aliases would
misread the author and action number, even though the outgoing ACK forge passes.
No production catalogue was modified. A separate version-checked historical
incoming catalogue is required before claiming the live combat barrier works.
The historical jxh confirmation builder supplies fighter #2; its extracted
schema has two int64 fields, so this must not be mislabeled as SequenceEndEvent.

The isolated probe now exercises RawProtobuf.Parse and PayloadReader.Int with
a historical sequence-end wire fixture (action 42, author 13825558, type 7).
A historical field catalogue yields all three expected values; the actual
current data/fields.json is a negative control and misreads both action and
author as predicted. bash_739 exited 0 with HISTORICAL_SEQUENCE_READER_PASS
author action type; current-catalog mismatch reproduced, retaining all prior
movement, cast and acknowledgement PASS markers. This tests the parser/reader
boundary, not the private live packet handler or its event-driven barriers.

## Availability recheck

bash_741 exited 0 and observed the isolated historical Dofus.exe running as
PID 10024 from source/Cliente 3.6.10.11. The Jondo status endpoint still reports
enLinea/base_/servicios=true, version 3.6.10.10, process 18872. No GivreClone
process was returned by that process-name query. This establishes available
laboratory processes, not a Givre session or compatibility of current native
mods with the historical client. No module was injected during this recheck.

The authenticated read-only conectados observation under bash_743 exited 0
and returned an empty list. Thus the historical client process is open but
there is no observed in-world session at this check. The first observation
command (bash_742) failed PowerShell parsing before making a request; its
replacement builds the JSON body from a hashtable. The client Latest.log
records com.ankama.zaap.ZaapError at 13:50:50 and 14:10:58, through
Ankama.Launcher.ZaapWorker.Execute/ZaapTask.Execute. That establishes launcher
RPC errors, not their cause or proof that they caused the world disconnection.
No reconnect, client restart, gameplay action or equipment change was performed.

The deterministic Jondo StaleLaunchTests and GoingBackTokenTests run under
bash_744 exited 0: 11 passed, zero failed or skipped, duration 3.2 seconds
(6.2 seconds build/test). Source confirms the stale-launch sweep requires
five minutes without a sign of life AND no connected socket; resolving a
game session refreshes LastSeenUtc. These tests establish registry behavior,
not the cause of the observed Zaap errors. A native desktop window enumeration
subsequently returned no Dofus window; no client input was attempted.

Follow-up process probes bash_745/746 exited 0: Dofus PID 10024 reports
Responding=true and MainWindowHandle=10029326; Dofus, Jondo and the terminal
all report Windows SessionId=1. The native desktop window lookup still cannot
resolve that handle. This is an unresolved observation mismatch, not evidence
of a hung client, another Windows session, or a confirmed client screen.

The fixture-only sequence check was replaced by invoking BuildSequenceEnd on
the actual Jondo Server.dll already built by Jondo.Unity.Tests. The assembly
is loaded in the isolated probe; the server entry point is not called. Its
serialized bytes are parsed by Givre RawProtobuf and PayloadReader using
the historical catalogue, retaining the current-catalogue negative control.
bash_748 exited 0 with JONDO_SERIALIZER_SEQUENCE_PASS actual-server-builder
to Givre-reader, along with all previous probe markers. This strengthens
the serializer/reader boundary evidence, not the live session claim.

bash_749 also exited 0 after invoking Jondo's actual ReadCast,
ReadPlacementMove, ReadReady and ReadSequenceAck on frames emitted by Givre
MoveForge. It returned the expected spell/cell, actor/cell, ready/unready and
sequence ID, with JONDO_COMBAT_DECODERS_PASS and all prior PASS markers.
Both directions of these specific serializer/reader boundaries are now
covered. No socket session, game action, complete catalogue or native-renderer
test follows from this result. Further repetition of these same probes is
not needed unless their inputs or implementations change.

## Fresh isolated-client launch after planner validation

bash_776 again observed the historical client PID 10024 and healthy local
Jondo services (PID 18872). Native desktop enumeration still did not list
the client, so no blind input was sent. The old client was stopped only after
checking its exact isolated executable path (bash_779, exit 0); its original
launcher bash_717 then reported LAB_CLIENT_EXIT=-1. Two preceding stop commands
failed PowerShell quoting/path checks and did not stop the client; the success
marker printed by bash_777 is not evidence of an actual stop.

The unchanged Start-LabClient.ps1 is now running under bash_780 with fresh
local authentication and launch registration. This restarts only the isolated
3.6.10.11 client, not the official client or emulator server. Connection and
Givre integration remain unverified until subsequent observations.
