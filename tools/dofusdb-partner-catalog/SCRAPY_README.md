# DofusDB — collecteur Scrapy pour revue partenaire locale

Le propriétaire du compte déclare disposer d'un partenariat permettant la
collecte du site. La licence **publique de l'API** précise séparément des
restrictions IA/MCP et commerciales. **Ce collecteur capture seulement des
fichiers JSON de revue locale dans `.cache/`**. Il ne publie aucun jeu de
données, ne communique avec aucun assistant/MCP, n'envoie aucune commande au
client Dofus et n'affirme pas que des droits IA ont été vérifiés.

## Installation réalisée

Scrapy 2.19.0 installé dans `.venv/` (ignoré par Git) ; CloakBrowser 0.6.3
était déjà installé dans Python, donc pas de nouvelle installation.
Scrapling, Crawl4AI et Crawlee ne sont pas nécessaires à la capture des
collections JSON déjà observées. Aucun navigateur furtif n'est utilisé pour
contourner les protections du site.

## Commandes (depuis la racine GivreWork)

```powershell
$py = "tools/dofusdb-partner-catalog/.venv/Scripts/python.exe"
$script = "tools/dofusdb-partner-catalog/scrapy_partner.py"

# Contrats et données synthétiques ; aucun accès réseau :
& $py -m unittest discover -s tools/dofusdb-partner-catalog -p 'test_*.py' -v

# Contrôle d'un seul lot de 20 fiches par collection :
& $py $script --partner-review --output tools/dofusdb-partner-catalog/.cache/site-review `
  --page-size 20 --max-pages-per-collection 1 --delay 1

# Reprendre sans repasser sur les pages déjà hashées, jusqu'à la couverture totale :
& $py $script --partner-review --output tools/dofusdb-partner-catalog/.cache/site-review `
  --page-size 20 --max-pages-per-collection 20000 --delay 1
```

**Collecte complète effectivement démarrée dans cette reprise :** cache
`tools/dofusdb-partner-catalog/.cache/full-catalog`, `--page-size 50`,
`--max-pages-per-collection 20000`, `--delay 1`, lancement Windows en
processus indépendant. Pour suivre les captures :

```powershell
$out = "tools/dofusdb-partner-catalog/.cache/full-catalog"
(Get-ChildItem $out -Recurse -Filter 'page-*.meta.json' -File).Count
Get-Content "$out/run.stderr.log" -Tail 20
Get-Content "$out/review-summary.json" -ErrorAction SilentlyContinue
```

**Exécution réelle terminée** pour les 19 collections : 1 756 pages,
87 327 documents, `status=REVIEW_ONLY_COMPLETE`, `complete=true` dans
`.cache/full-catalog/review-summary.json`. La couverture n'est pas une
certification de version Dofus ni des autres routes non explorées.

Le manifeste de fin est écrit une fois la collecte arrêtée. Pour reprendre
après l'arrêt du processus sans changer le format du cache, reprendre avec
**`--page-size 50` et le même chemin** (jamais mélanger tailles de page).
Les données brutes restent exclusivement dans `.cache/`, même après reprise.

L'argument `--partner-review` confirme explicitement une **revue locale
autorisée par le détenteur des droits applicables** ; il ne signe pas un
contrat DofusDB et n'autorise pas la diffusion IA. La commande est bornée à
une seule requête simultanée, délai d'au moins 1 seconde, respecte le
`robots.txt` lorsqu'il est publié, **n'utilise ni cookies, proxy tournant,
token, navigateur furtif, redirection, CAPTCHA ni retries sur HTTP 401/403/429**.
Le navigateur CloakBrowser a été utilisé pour observer `robots.txt`, pas pour
contourner l'accès.

## Périmètre effectivement documenté

Les collections JSON `/items`, `/item-sets`, `/recipes`, `/monsters`,
`/map-positions`, `/interactives`, `/jobs`, `/subareas`, `/areas`, `/worlds`,
`/map-references`, `/transport-from-maps`, `/point-of-interest`, `/dungeons`,
`/quests`, `/npcs`, `/achievements`, `/mounts` et `/spells` ont répondu en 200
lors de sondages d'un enregistrement. `/resources` a répondu 404 et
`/custom-interactives` 401 ; ces deux routes sont **exclues**. Le site est
plus vaste que les 19 collections constatées, et ce lot ne prouve pas que
**tout le site** est extrait.

Le format actuellement observé est un objet `{data,total,limit,skip}` ; le
parseur vérifie les tailles et le compte exact de chaque page. Les noms des
collections sont figés et vérifiés ; aucune URL saisie par l'utilisateur
n'est exécutée. La capture enregistre un JSON par page et un `.meta.json`
avec URL, date, SHA256, nombre d'octets et d'enregistrements, puis un
`review-summary.json` agrégé. Une reprise contrôle le SHA et les nombres
de chaque page existante, ainsi que les IDs dupliqués. Les décalages sont
repris après la dernière page strictement vérifiée. Une variation de
`total`, un document incomplet/dupliqué, une réponse non JSON, un refus
HTTP ou une limite de volume interrompt la collecte ; les pages antérieures
restent disponibles en **revue partielle**, jamais considérées complètes.

Les documents sont dédupliqués sur leur identifiant **`_id`** lorsqu'il est
présent : `id` est une clé métier qui peut légalement se répéter dans
`transport-from-maps` (plusieurs connexions pour une même carte). Les tests
imposent que deux `_id` identiques soient toujours refusés.

## Limites de conformité et d'exploitation

- `sourceGameVersion: UNKNOWN` : même une collection complète n'est pas un
  état certifié d'une version précise du jeu.
- `aiMcpAllowed:false` et `authorizedRuntimeUse:false` dans les manifestes :
  une autorisation écrite distincte est nécessaire avant intégration aux
  assistants IA/MCP ou à un produit commercial. La déclaration utilisateur
  autorise ici une **capture locale de revue**, pas une permission vérifiée
  d'ingestion IA du flux public.
- Les recettes doivent être recoupées par ID et rendement, les compositions
  d'ensembles et les sources de drop doivent être normalisées dans un
  importeur séparé, avec validation de version et droits ; aucune liaison
  vers les plans exécutables n'est faite automatiquement.
- `complete:true` signifie uniquement qu'une suite de pages stable a été
  capturée avec les comptes requis ; pas authenticité éditeur ni ACK gameplay.

En cas de droits partenaire plus larges, conserver leur **preuve et périmètre
API/site/IA/MCP/commercial** à côté de l'export, avant toute promotion.
