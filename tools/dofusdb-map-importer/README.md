# DofusDB partner map importer (review only)

Pure Python 3 / standard library. All files and commands below run **offline**
except the explicit `probe-site` command. The application/LLM/MCP/runtime
cannot read these exports through this tool. The data is for human review of
licensed source and map relationships.

## Commands

```powershell
cd %USERPROFILE%\Videos\GIVRE\GivreWork-Integration-20261020
python -m unittest discover -s tools/dofusdb-map-importer -p test_importer.py -v

python tools/dofusdb-map-importer/importer.py normalize `
  --input tools/dofusdb-map-importer/fixtures/synthetic_partner_export.json `
  --output-dir tools/dofusdb-map-importer/outputs/synthetic `
  --game-version 3.7.12.12

# Offline review of a one-page JSON response acquired separately and
# quarantined under .cache (never transmitted to AI/MCP):
python tools/dofusdb-map-importer/importer.py normalize `
  --input tools/dofusdb-map-importer/.cache/schema-one-map-positions.json `
  --api-snapshot-review `
  --output-dir tools/dofusdb-map-importer/.cache/map-positions-review `
  --game-version 3.7.12.12

python tools/dofusdb-map-importer/importer.py probe-site `
  --url https://dofusdb.fr/tools/map `
  --max-pages 1 `
  --output-dir tools/dofusdb-map-importer/outputs/site-review
```

The site probe **reads robots.txt first**. If the AI user agent is disallowed,
it records `BLOCKED_ROBOTS` and does not request site pages. When robots
permits, it fetches only explicitly allowlisted HTML map pages, at most
eight, at least one second between requests, and observes strict response
caps. HTTP 403, CAPTCHA, redirects and unexpected MIME types stop collection.
HTTP 429 retries only with bounded delay (max 3 total attempts, <=10s waits).
The API domain is never requested by this command.

If a probe ends `UNVERIFIED_PARTIAL` after hitting the page cap and you
are authorized to continue, raise `--max-pages` and add `--resume`.
The checkpoint verifies previous capture bytes and SHA256; it never starts
over by silently refetching pages.

## Explicit partner export schema v1

`normalize` processes a **local operator-supplied JSON file** that matches
`fixtures/synthetic_partner_export.json`. That file is wholly **synthetic**;
map IDs, item IDs, coordinates, underground floors, and edges are invented
solely as test fixtures. It is not a site/API dump.

The source format is a proposed **partner handoff contract**, not an
observed DofusDB endpoint schema. The exporter must provide mapId,
worldId/worldMapId, layer, floor, position, resourceId, zaap cell and explicit
transitions. Missing optional fields remain JSON null; missing target IDs
stay `UNKNOWN_TARGET`. It never creates edges from coordinates, directions,
or proximity. An explicit link whose destination map was omitted is marked
`EXPLICIT_TARGET_NOT_IN_EXPORT`. Every record stores its JSON pointer back
to the captured source, identified by SHA256.

With `--api-snapshot-review`, the normalizer instead accepts the
**observed** `/map-positions` page structure `{total,limit,skip,data}`
from a previously acquired local capture. Fields are projected as
`id -> mapId`, `posX/posY -> position`, `worldMap -> worldMapId`
and `subAreaId`. The observed first record has `id=0` and a negative
`worldMap` value; these are preserved with unverified status. No version,
layer/floor, resource, zaap or destination map is implied. In particular,
the source `isTransition` boolean is not a navigable edge. This command
never calls the API.

Output consists of `partner-source-capture.json`, `maps.normalized.json`
and `manifest.json` (schemaVersion 1, raw/normalized SHA256 and byte lengths,
version, date and provenance). The normalizer refuses to overwrite results.
All records remain `UNVERIFIED` for in-game behavior. A verified private
license exception and separate runtime evidence would be needed before any
IA/MCP/runtime integration.

## Important boundary

The user reports a partner agreement permitting site scraping. Public
`api.dofusdb.fr` is separately licensed under **LPNC-IA 1.0**, which restricts
AI pipelines and making API data available to an AI system. Permission to
scrape the website is not, by itself, verification of an AI/MCP exception.
No real map records are included in this repository tool. The probe caches
HTML only when permitted by robots and does not decode game data.

Full evidence and known unknowns:
`docs/DOFUSDB_PARTNER_MAP_IMPORT_20261020.md`.
