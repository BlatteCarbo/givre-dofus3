# Round 6 Prompt 2 — qualified 3.7.11.11 dataset integrated

## Result

The native source now embeds the actual French item/type/recipe-count TSV generated from ledouxm/dofus-sqlite release v6.0_3.7.11.11. Release metadata was fetched from https://api.github.com/repos/ledouxm/dofus-sqlite/releases/tags/v6.0_3.7.11.11 . Release ID 414926772, published 2026-10-16T12:14:20Z, draft=false, prerelease=false.

Downloaded assets and README are confined to `artifacts/round6-prompt2-release`. Both assets matched the size and SHA-256 published in GitHub's release API:

- dofus.sqlite: 384966656 bytes; `bcb66ca4611bdff29c5e303cb4dd4770790c6c4905e6a81b0e8c2a33726501b7`
- i18n_fr.json: 28878001 bytes; `f972c8efa2bf22cc626fc2e58e96695f68579cd846303de0afa8d17ac8cb0016`

The release supplies a flat French text-ID/string JSON dictionary, not fr.sqlite. The SQLite also contains translations(id,value,lang), used for independent verification of every exported name/type. The downloaded README describes that translation join schema. RecipeData.id contains UUID text; junction.RecipeData_id also contains UUID text despite its INTEGER column declaration. The generator joins the actual values without coercing recipe IDs.

## Files changed in the actual-release follow-up

- `tools/Generate-ItemOverlay.py`: optional release JSON argument; verify exact release identity plus asset size/SHA-256 before export; preserve unverified mode for fixture/local inputs; record provenance; explicitly exclude ItemData rows whose ItemTypeData reference is missing.
- `tools/Test-ItemOverlayDataset.py`: release proof, digest tamper rejection and missing-type exclusion tests added to existing tests.
- `native/ItemIdOverlay/src/items.tsv`: actual release data, 20784 rows with five columns (name, level, type, gid, recipeCount).
- `native/ItemIdOverlay/src/items.provenance.json`: release URL, metadata/input/output hashes, exported/source counts and exclusion list.
- `native/ItemIdOverlay/src/table.rs`: adjust the existing expected suffix to the now-known zero count; add a real bundled-data regression for item 2540 and its 186 ingredient recipe uses.
- This report.

No NativeTurboRuntime.cs, test csproj, packaged native DLL, accounts or original MizanBot files were changed by this follow-up. Other workers' concurrent changes were preserved. No app build, live action, commit, push or staging was performed.

## Data evidence

- Source ItemData: 20785 rows; RecipeData: 4743 recipes; 24591 ingredient junctions, all successfully joined to a recipe.
- Exported items: 20784. Exactly one excluded item, GID 34569 (Test Masquer effet), has no matching ItemTypeData row. No type was invented.
- 2849 exported items participate in at least one recipe; other exported counts are known zero.
- All exported names and types match French SQLite translations after whitespace normalization.
- All counts match an independent Python set-of-recipe-IDs calculation, separate from the generator's COUNT(DISTINCT ...) query.
- SQLite quick_check: ok.
- Example: Sachet de Haricots / 16828 / Conteneur / 0 recipes; Substrat de Futaie / 2540 / Substrat / 186; Pepite / 14635 / 370.
- TSV SHA-256: `bfd68f5f3157cdb504207689df7811cc5c15267e8fe80c3d5aa809b185cf56d5`.
- Detailed independent check: `artifacts/round6-prompt2-release/dataset-validation.json`.

Release association and downloaded-byte integrity are verified. This does not certify live tooltip rendering or game protocol semantics.

## Reproduce

From the repository root, Python >=3.12:

```powershell
python tools/Test-ItemOverlayDataset.py
python tools/Generate-ItemOverlay.py artifacts/round6-prompt2-release/dofus.sqlite artifacts/round6-prompt2-release/i18n_fr.json 3.7.11.11 artifacts/round6-prompt2-release/generated artifacts/round6-prompt2-release/release.json
cargo test --offline --manifest-path native/ItemIdOverlay/Cargo.toml
cargo build --release --offline --manifest-path native/ItemIdOverlay/Cargo.toml
```

Generator inputs are opened read-only with immutable=1. It never uses ItemData.price as HDV data. Missing French translations still fail before writes; missing type references are explicitly excluded and listed. The fifth TSV column counts distinct recipes using the item as an ingredient, not quantities or craft slots. Old four-column rows remain accepted as unknown-count rows.

## Build and integration handoff

- Rust tests: 12 passed, 0 failed.
- Generator tests: PASS, including duplicate/orphan recipes, known zero, Unicode/whitespace, deterministic output, missing translations, version rejection, release size/digest proof and tamper rejection.
- Release build: successful, 3 pre-existing unused-helper warnings plus linker informational warning.
- DLL version: 0.4.0.
- Build output: `native/ItemIdOverlay/target/release/givre_item_id_overlay.dll`.
- Stable handoff copy: `artifacts/round6-prompt2-release/GivreItemIdOverlay.dll`.
- FINAL DLL SHA-256: `4bb3076604a5f077e5cb6717c39805a1d9645293d603348bd9aea3b0e9df938a`.

Coordinator must copy the final DLL to `native/GivreTurboRuntime/ItemIdOverlay/GivreItemIdOverlay.dll` and update NativeTurboRuntime.OverlaySha256 together, then run the existing binary/hash assertion in MitmTurboSourceTests. The earlier pre-dataset 75d9... hash is superseded.

Settings integration: `SettingsView.axaml.cs` calls InitializeItemOverlaySettings(), the dedicated partial provides #id/[id] and enable controls. `LaunchService` supplies `ItemOverlayPreferences.FilePath` to `LocalZaapAuthority.ItemOverlaySettingsPath`; the authorized start recipe publishes it in the launched client's GIVRE_ITEM_OVERLAY_SETTINGS environment variable. This keeps the isolated launcher tests independent of the application data root. Rust reads the absolute filename once at module load; no hardcoded TestRuns root, no file reads inside TextElement.set_text and no UI tree traversal.

HDV prices remain omitted. A future pipeline must use the true BidHouseStore.AverageFor(pid,gid) cache with safe PID/session scoping; no static SQLite price or invented observation is displayed.

No live client validation was performed. After coordinated packaging/hash update, the user can launch the owned test client, hover inventory item 16828 (known zero) or 2540 (186 recipes), switch to [id], relaunch and hover again, then disable/relaunch and check absence of suffix.
