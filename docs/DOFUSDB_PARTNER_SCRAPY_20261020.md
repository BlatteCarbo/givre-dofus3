# Lot DofusDB : collecte Scrapy contrôlée — 20 octobre 2026

Demande de l'utilisateur : partenaire DofusDB, autorise la collecte, autorise
l'installation si utile de Scrapy/Scrapling/Crawl4AI/Crawlee. Choix : **Scrapy**
dans un environnement virtuel isolé (dépendance utile pour pagination,
temporisation, contrôle de cache) avec **CloakBrowser déjà présent** pour
les pages JavaScript. Les deux frameworks alternatifs orientés furtivité et
le crawler LLM ne sont pas nécessaires pour les JSON publics observés.

Fichiers : `tools/dofusdb-partner-catalog/scrapy_partner.py`,
`test_scrapy_partner.py`, `SCRAPY_README.md` et `.gitignore` mis à jour.
Le flux public API suit une licence LPNC-IA 1.0, distincte du partenariat
de scraping de site déclaré : **collecte revue locale seulement** ; pas de
promotion IA/MCP ni de redistribution de données. Respect du refus HTTP,
robots lorsqu'il existe, absence de cookies/CAPTCHA/stealth ou redirection.

Les probes d'une fiche ont confirmé 19 collections paginées pertinentes
sur `api.dofusdb.fr` : équipement, recettes, ensembles, monstres/drops,
cartes, téléportations, métiers, zones, quêtes et autres. Les routes
`resources` (404) et `custom-interactives` (401) ont été refusées.

Chaque page est conservée avec son SHA256 dans `.cache/`, ses comptes,
ses offsets et sa date ; le manifeste de couverture reste local et interdit
explicitement l'accès IA/MCP. Un snapshot intégral n'est pas une preuve
de version du jeu, et les données ne doivent pas être utilisées directement
pour déclencher du farm/craft ou imiter un ACK serveur.

Validation et chiffres exacts de la collecte : lire les résultats
`review-summary.json` et les tests du lot ; ne pas confondre une page
sondée, un lot partiel et une extraction exhaustive.

**Résultat pilote réel :** 19 collections × 20 documents = **380 documents
capturés**, une page par collection, état `REVIEW_ONLY_PARTIAL`.
**Validation offline : 17/17 tests Scrapy/collecteur PASS.**

**Lot étendu terminé :** 19 collections ciblées, **87 327 documents**,
**1 756 pages**, toutes les pages présentes et validées dans le manifeste
local `tools/dofusdb-partner-catalog/.cache/full-catalog/review-summary.json`.
La dernière passe a retourné `complete=true` et
`status=REVIEW_ONLY_COMPLETE` ; **cela certifie la couverture des
collections sélectionnées, pas de tous les endpoints du site**.
Le collecteur n'a pas utilisé de Dofus.exe ni de compte. `sourceGameVersion`
reste `UNKNOWN`, `gameplayCertified=false`. Les droits IA/MCP ont été
déclarés par l'utilisateur et sont gérés dans un reçu opérateur séparé,
sans prétendre vérification juridique indépendante ; le snapshot de
capture conservateur maintient `aiMcpAllowed=false` jusqu'à promotion
explicite après contrôle de version/provenance.

Incident résolu : `transport-from-maps` a plusieurs documents de transport
avec le même identifiant métier `id`. Le contrôle de duplication est désormais
effectué sur `_id` unique (si présent) plutôt que `id`, avec tests négatifs
sur vrais doublons de documents. Aucune page corrompue n'a été admise.
