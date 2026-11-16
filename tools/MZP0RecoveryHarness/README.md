# MZP0 guarded IActionSender — offline contract

This .NET 10 harness compiles the **actual** production `ActionSender.cs`,
`FrameSink.cs`, `PacketBus.cs`, `BotMessage.cs`, `EnvelopeBuilder.cs`,
`PayloadBuilder.cs`, `ProtocolCatalog.cs`, `ProtoSchemaText.cs`,
`RequestCorrelator.cs`, `MZP0RecoveryJournal.cs` and guarded adapter.
Only unavailable `GivreCore` symbols are stubbed. Its minimal protobuf
encoding is **test-only** and provides no game compatibility proof.

```powershell
dotnet test tools/MZP0RecoveryHarness/MZP0RecoveryHarness.csproj -c Debug
dotnet test tools/MZP0RecoveryHarness/MZP0RecoveryHarness.csproj -c Release
```

The public `IActionSender.SendRequestAsync` returns an UID **after**
`ActionSender` has already invoked its sink. A decorator around an arbitrary
`IActionSender` cannot atomically persist the UID before the first possible
send. The guarded implementation therefore constructs the real `ActionSender`
with its own `IFrameSink` decorator. Existing `ActionSender.cs`, `BotContext`
and behavior method signatures are unchanged.

For a real host integration, PRIME must supply **two independently implemented
authorities**, derived from actual trusted transport observations:

1. `IMZP0AuthenticatedSessionSource`: verifies live PID birth, connection
   binding, exact protocol version and continuing session ownership.
2. `IMZP0PositiveResponseVerifier`: verifies a genuine server **response**,
   its capture origin, generation/nonce, UID and explicit positive meaning.

Without these authenticated adapters, `ActivateAuthenticatedSession()` rejects
the session and **no bytes are sent**. This harness uses fakes only in tests;
it does not provide a producer, attach to the game, infer ACKs or replay frames.

The host can construct `MZP0GuardedActionSender` with the **same real sink,
catalog, bus, pacer, correlator** that it would pass to `ActionSender`, plus
its journal and both authorities. After an authenticated session activation,
expose it as `IActionSender` to `BotContext` / existing behaviors. Feed actual
trusted inbound messages to `TryAcknowledgeVerifiedInbound`; `WaitForEventAsync`
returns scrubbed labels only and never confirms a journal operation.

For every send, the guarded sink reads the UID from the **request envelope**,
atomically persists `BeginIntent`, opens a durable dispatch window, then
forwards the original bytes once. Successful `Send` means **attempted**, not
acknowledged. A failed/throwing send is `Uncertain`; restart converts old
unfinished records to `Uncertain`. The journal's UID watermark survives
terminal-record pruning, so resetting an existing correlator cannot replay.
`ActionResult.Frame`, awaited event `Payload` and `RawFrame` are scrubbed.

Integration and exact parity with GivreClone v1.3.1 remain **unverified**;
there is no in-repository production provenance or response verifier and
the complete GivreClone solution still references a missing GivreCore project.
