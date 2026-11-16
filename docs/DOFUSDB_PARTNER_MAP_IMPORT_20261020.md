# DofusDB — import carte partenaire (lot 2), 20 octobre 2026

## Périmètre et état

Worker-12, uniquement `tools/dofusdb-map-importer/*` et ce document.
Site DofusDB public, inspecté de façon bornée; aucun client Dofus, test gameplay,
authentification, anti-bot ou téléchargement massif. Aucun code ou asset tiers
recopié. Résultat **OUTIL PRÊT POUR EXPORT PARTENAIRE LOCAL / UN
ENREGISTREMENT RÉEL EN CACHE ISOLÉ / IA-MCP BLOQUÉS**.

### Autorisations distinctes

1. Le **partenariat et l'autorisation de scraping du site** sont expressément
   déclarés par l'utilisateur. Ce droit est considéré comme l'instruction
   pour développer l'outil partenaire, mais la portée contractuelle exacte,
   les conditions de débit et les éventuelles dérogations aux règles robots
   ne sont pas documentées dans les pièces de ce lot.
2. La **licence publique de l'API** à https://api.dofusdb.fr/
   (LPNC-IA 1.0, version 2026) limite les usages commerciaux et interdit
   notamment l'ingestion des données API par des agents IA ou des pipelines
   pilotés par IA, sauf éventuelle exception contractuelle indépendante.
3. Le présent travail ne prouve donc pas un droit de réutilisation IA/MCP.
   Tous les manifestes portent `partnerExceptionVerified=false`,
   `allowedForAiMcpOrGameplay=false`. Aucune source API n'a été exposée
   à l'IA/MCP, ni exploitable depuis le client par ce lot.

### Sources inspectées et résultats réels

| Source | Preuve | Implication |
| --- | --- | --- |
| `https://dofusdb.fr/robots.txt` et `https://beta.dofusdb.fr/robots.txt` | HTTP 200 le 20/10/2026 ; groupe listant `ChatGPT-User`, `ChatGPT Agent`, `OpenAI` et autres agents IA, `Disallow: /` | Le `probe-site` utilise `ChatGPT-User`, respecte robots, et s'arrête à `BLOCKED_ROBOTS`. Aucune tentative de contournement. |
| `https://dofusdb.fr/tools/map` | Une lecture HTML anonyme via client PowerShell ordinaire, HTTP 200, `text/html`, 39 774 octets transmis ; le DOM est une SPA et référence reCAPTCHA Enterprise | Accès public à la page HTML, mais **aucune donnée mapId/edge prouvée dans le HTML** ; pas de résolution CAPTCHA ni d'API non publique. |
| JS public `https://dofusdb.fr/js/app.fb2bd37a.js` | HTTP 200, 181 001 caractères dans la réponse examinée ; lecture statique des chemins applicatifs | Source front-end référence `https://api.dofusdb.fr` en `baseURL` et des routes GET ; ceci prouve des **noms de routes**, pas leur payload ni la connectivité des grottes. |
| `https://api.dofusdb.fr/robots.txt` | HTTP 404 lors du contrôle du 20/10/2026 | Robots de l'API non validables pour le collecteur récurrent; `probe-site` n'accède jamais à ce domaine. |
| `https://api.dofusdb.fr/map-positions?$limit=1` | Une unique vérification ponctuelle de schéma, client PowerShell anonyme, HTTP 200 JSON 1 479 octets, SHA256 `0fe169260e6cac12d5354aeeda21047efcd2046fb860b37dcb6acc95dade6842` | Cache brut isolé `tools/dofusdb-map-importer/.cache/schema-one-map-positions.json` ignoré par Git ; réponse intégrale non transmise au modèle ni à MCP. Un record, jeu/version non attestés, aucune transition établie. |
| `https://beta.dofusdb.fr/almanax/tools/map` | Page intitulée *Carte du monde - DofusDB* relevée en recherche publique | Monde/couches possibles par interface; aucune énumération exhaustive vérifiée. |

Le test **réel** `python importer.py probe-site --max-pages 1` a créé
`.cache/live-site-probe/checkpoint.json` avec `status=BLOCKED_ROBOTS`,
`pages=[]`. Le CLI renvoie volontairement le code 2 pour un statut bloqué;
aucune page n'a été demandée par ce collecteur.

Routes **réelles observées dans le JS du site**, et non endpoints de
partenariat confirmés : `GET /map-positions`,
`GET /map-positions/{id}`, `GET /map-references`,
`GET /map-references/{id}`, `GET /interactives`,
`GET /interactives/{id}`, `GET /point-of-interest`,
`GET /transport-from-maps`, `GET /transport-from-maps/{id}`,
`GET /worlds`, `GET /subareas`, `GET /areas`,
`GET /custom-interactives`. L'app JS inclut la route de l'API publique,
mais ni le contenu des réponses, ni leur schéma, ni la présence de
descente de mine/zaap par ces routes n'ont été vérifiés **hors le seul
schéma `/map-positions` décrit ci-dessous**. Référence principale :
`https://dofusdb.fr/js/app.fb2bd37a.js`.

Sur l'unique page `/map-positions`, schéma observé :
`{total, limit, skip, data[]}`. Le premier élément possède
`id`, `m_id`, `posX`, `posY`, `subAreaId` et
`worldMap` numériques, et `isTransition` booléen.
Le record porte `id=0` et un identifiant `worldMap` négatif.
Ces valeurs sont préservées mais leur sens en jeu n'est pas certifié :
`isTransition` n'indique ni la destination ni un passage praticable.

## Architecture

- `importer.py probe-site`: URLs HTTPS `dofusdb.fr` et `beta.dofusdb.fr`
  uniquement ; chemins `/tools/map` et `/almanax/tools/map`,
  pagination facultative `page=1..8`, max 8 pages. Robots obligatoire
  avant page; UA honnête `ChatGPT-User`, aucun cookie/token ni authentification,
  aucun suivi de redirect, limite 2 MiB/réponse, 1 req/s global minimum,
  HTTP 429 max 3 essais avec backoff borné et arrêt si attente > 10 s.
  403 et CAPTCHA arrêt immédiat, sans bypass. Seules captures HTML
  `UNVERIFIED_STATIC_HTML`, avec SHA256/longueur/URL en `checkpoint.json`.
- Sur pagination interrompue par `--max-pages`, état
  `UNVERIFIED_PARTIAL` et reprise explicite `--resume`
  après validation des SHA256 des captures existantes, sans refetch.
  Les statuts bloqués ne sont jamais repris automatiquement.
- `importer.py normalize` : conversion **offline d'exports partenaire
  explicitement structurés** au contrat JSON v1 (inventé et identifié
  comme **contrat proposé**, aucun schéma réseau DofusDB prétendu réel).
  Les champs `worldId`, `worldMapId`, `subAreaId`, `layer`, `floor`,
  `position` restent indépendants. Validation et plafonds : 2 MiB entrée,
  30 000 cartes, 512 entrées de chaque catégorie par carte, types numériques
  stricts, clés inconnues/dupliquées rejetées.
- `normalize --api-snapshot-review` : adaptateur au **schéma réellement
  observé** d'une page `/map-positions`, sans requête réseau.
  `id -> mapId`, `posX/posY -> position`, `subAreaId`,
  `worldMap -> worldMapId`. `worldId`, `layer`, `floor`
  restent nuls; `isTransition` est un simple indicateur de source.
  Identifiant nul conservé avec `UNVERIFIED_ZERO_ID`.
  Revue locale du seul échantillon : **1 record / 0 arête**, JSON
  normalisé 772 octets, sortie en `.cache/map-positions-review`
  exclue de Git, `allowedForAiMcpOrGameplay=false`. La version du
  jeu est déclarée par l'opérateur et non prouvée par la réponse.
- **Transferts** : `ordinary`, `door`, `stairs`, `cave-entry`,
  `cave-exit`, `zaap`, `portal`; uniquement une cible `toMapId`
  explicite produit `EXPLICIT_IN_UNVERIFIED_EXPORT`. Une destination
  absente de l'export reçoit `EXPLICIT_TARGET_NOT_IN_EXPORT`.
  Les voies sans destination ont `UNKNOWN_TARGET`. Aucune inférence
  topologique, aucun chemin/action de jeu certifié. `cellId` nul si absent.
- Ressources `resourceId` et `cellId` et points zaap sont conservés
  **sans inventer de noms**, avec pointeur source. Les fichiers
  `partner-source-capture.json`, `maps.normalized.json` et
  `manifest.json` comprennent empreintes SHA256, octets, version, URL
  de contexte, date, origine auto-déclarée. Statut toujours `UNVERIFIED`.

## Validation et intégration

`python -m unittest discover -s tools/dofusdb-map-importer -p test_importer.py -v`
: **15/15 PASS**, tests **synthétiques offline** des edges déclarés, de la séparation
layers/floors/world, source et manifest SHA256, champs inconnus, destinations
absentes, données mal typées, entrées excessives, version incohérente,
URL hors whitelist, refus robots, HTTP 403/429, CAPTCHA, redirection,
pagination, reprise/checkpoint, schéma Feathers connu et cas
`id=0/worldMap<0`. Aucun gameplay ni donnée réelle embarquée
dans les fixtures : mapId 9001/9002 entièrement **fictifs**.

Contrat disponible au prime : CLI séparé, manifesté et consultatif.
Intégrer à `Services/WorldAtlas` seulement après :

1. Remise du schéma/export partenaire effectivement autorisé (ou preuve
   contractuelle d'accès au site) et contrôle champ par champ sur source
   versionnée, via revue humaine.
2. Confirmation distincte, explicite, de la portée contractuelle
   **IA/MCP**, si une ingestion par l'IA ou un serveur MCP est envisagée.
3. Concordance avec version client Dofus, origine SHA256 réelle,
   appariement des mapId/couches et transitions, et contrôles négatifs de
   routes. Conserver `UNKNOWN` pour les passages non prouvés.

Ces trois points ne sont pas établis par la disponibilité anonyme du HTML
ou la présence des noms de routes API dans les bundles JS.
