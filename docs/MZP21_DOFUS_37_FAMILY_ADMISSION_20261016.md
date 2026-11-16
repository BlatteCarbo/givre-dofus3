# MZP21 — Dofus 3.7-family catalogue admission, without old patch lock

## User requirement

Stop rejecting the user-supplied 3.7 protocol catalogue only because the running client is a different **3.7.x.y** patch. The old independent offline decoder entry `MZP0ProtocolAdmission` had required exact `3.7.10.10`, while the installed Cytrus manifest identifies **3.7.11.11**.

## Verified source evidence

`DofusReverseIndexer/local-output/historical-extractions-v3/3.7.11.11/dofus-3.7.11.11.proto` has SHA-256 `bf3799e516ee52d968432db5651fa662a23308d94ac8314862189e59dbd41651` according to the source-only extraction audit. The available 3.7.10.10 and 3.7.11.11 proto snapshots are byte-identical. This does **not** prove every 3.7 patch is identical or demonstrate authenticated gameplay frames.

## Production changes

- Added strict `Supports37Family` input checking: four canonical ASCII-integer components with major 3 and minor 7; rejects noncanonical/foreign family input.
- Removed exact `TargetVersion` equality from `MZP0ProtocolAdmission.Review`, `MZP4ProtocolAdapter.Open`, `MZP5InboundReader.Open`, and `MZP7TypedCodec.Open`.
- Retained independent SHA-256 pins for matching/fields, collision checks, field manifest and stream/source/generation/sequence checks. A 3.7 label alone never substitutes for digest evidence.
- The TEST launcher *configuration preflight* accepts any valid 3.7.x.y version candidate, still requiring a private TEST directory and rejecting protected ports `5657`, `5658`, `26666`.
- No edit to old `LaunchService` fixed 26666/5658 transport: the old MITM launcher is **still intentionally unavailable** while independent owned launcher/Zaap and real wire traffic are unproven. This is a different condition from the version-family check.
- MZP19/20 separately repair startup-local licensing and a local TEST folder with unsafe inherited Windows DACLs; the original installed GivreClone is not modified.

## What the acceptance means

`3.7.10.10`, `3.7.11.11`, and canonical newer 3.7 versions can now create a **SHA-pinned offline catalogue candidate**. `3.6`, `3.8`, invalid labels, missing fields, ambiguous tags, invalid SHA and unowned sessions still fail. Being a candidate does not mean the server accepted a command. No session ownership, inbound message, or server ACK is fabricated.

## Validation and remaining independent blockers

- `tools/MZP0ProtocolAdmissionHarness` — 13 passing unit tests.
- `tools/MZP4ProtoIntegrationHarness` — 32/32 passing.
- `tools/MZP4LauncherHarness` — 38/38 passing.
- `tools/MZP5ProtocolHarness` — 133/133 passing.
- `tools/MZP7CoreContractHarness` — 61/61 passing.
- The legacy reconstructed launcher still has fixed ports and lacks independent Launcher2 + Zaap session authority, so it cannot safely be invoked for a TEST user while the original Givre clients are running.
- Full 1.3.1 feature parity, gameplay framing, typed event dispatch and server ACK are not yet certified. Tests above are offline only.
