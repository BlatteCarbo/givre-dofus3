# Dofus — export local des recettes pour ProfessionCatalogReader

**Date :** 20 octobre 2026. **Worktree :**
`%USERPROFILE%\Videos\GIVRE\GivreWork-Integration-20261020`.
**OWN exclusif du worker-12 :** nouveaux
`tools/local-recipe-catalog/*`, ce document. Aucun changement aux anciens
fichiers de Givre, à la branche historique ou aux projets C#; aucun
commit/push, réseau, jeu ou automatisation de craft.

## Résultat validé sur la vraie base locale

Source : `%USERPROFILE%\Videos\GIVRE\dofus.sqlite`, **lecture seule**.
Taille : **380 866 560 octets**.
SHA-256 recalculé avant et après lecture :
`599908fe9396cde1800390497ca703949576b53acbd261271d103001bda9b199`.

| Objet SQL observé | Lignes | Utilisation démontrée |
| --- | ---: | --- |
| `RecipeData` | 4 858 | `id` UUID texte, `resultId`, `resultNameId`, `resultLevel`, `jobId`, `quantities` JSON |
| `RecipeData_ingredientIds_junction` | 24 532 | `RecipeData_id` UUID texte, `target_id` objet ; ordre examiné via `rowid` |
| `ItemData` | 20 979 | `id`, `nameId`, `level` ; `level=0` converti en UNKNOWN/null |
| `JobData` | 23 | `id`, `nameId` |
| `translations` | 1 696 710 | Traductions `fr` par `id` + `lang`, dont noms de métier et `resultNameId` |

Toutes les 4 858 recettes possèdent une liste d'IDs de liaison et une liste
`quantities` de **même longueur** (aucun mismatch observé, aucun
nombre invalide). La base contient **688 résultats absents d'`ItemData`**
et **10 identifiants d'ingrédients absents d'`ItemData`**, dont l'union des
identifiants manquants produit **688 objets supplémentaires** dans
`recipes.json` : **21 667 objets exportés**. Les identifiants manquants
sont conservés, niveaux UNKNOWN/null ; **688 noms de produits** ont pu être
retrouvés grâce au `resultNameId` explicite de `RecipeData` et à la
traduction locale `fr`, sans déduire un niveau d'objet.

**Métier correct : `jobId=16` est Bijoutier**, avec **735 recettes**.
**34** recettes ont `requiredLevel` entre 20 et 30 inclus.
`jobId=13` est **Sculpteur** et compte 289 recettes, donc ne pas
utiliser 13 pour l'objectif Bijoutier 20→30. Les recettes situées au niveau
30 ne sont accessibles qu'une fois ce niveau atteint ; pour progresser
de 20 vers 30, le filtre doit être `requiredLevel <= niveau actuel`.

La base observée **ne démontre ni l'XP gagnée par fabrication, ni le
rendement d'une recette, ni les seuils XP métiers**. Chaque recette
exportée possède `resultQuantity:null` et `experiencePerCraft:null`;
`levelThresholds:[]`. Des calculs de quantité totale de farm pour
obtenir N sorties ou un nombre exact de crafts pour passer 20→30
restent donc **UNKNOWN** jusqu'à preuve indépendante de ces valeurs.

## Livrables et contrats

- `tools/local-recipe-catalog/export_recipes.py` :
  `inspect` valide schema/empreinte/lignes réelles sans écrire ;
  `preview` produit un JSON **réel consultatif** et
  `manifest.review.json` non admissible ; `export` produit
  `recipes.json`, `manifest.json` compatible et
  `provenance.review.json` seulement si les attestations explicites
  sont passées (droits de source, ordre du junction, version seulement
  déclarée).
- `tools/local-recipe-catalog/test_export_recipes.py` :
  fixtures SQLite **fictives** et tests négatifs hors réseau.
- `tools/local-recipe-catalog/README.md` :
  toutes commandes, format et avertissements de provenance.
- `tools/local-recipe-catalog/.gitignore` :
  dossiers `.cache/` et `exports/` exclus du dépôt.

**Aperçu réel créé :**
`tools/local-recipe-catalog/.cache/real-20261020-v2/recipes.json`,
SHA256
`1d3ce5245edee95b992cf6901cf148b25b79b79b4520241e9ef356ef61cb7c59`.
Fichiers source et destination stockés localement ; `.cache`
est ignoré par Git. `manifest.review.json` annonce expressément
`gameVersion:UNKNOWN`, `permission:UNVERIFIED`,
`allowedForAiOrMcp:false`, `status:REVIEW_ONLY_NO_ADMISSIBLE_MANIFEST`.
Il n'existe **pas** de `manifest.json` admissible dans cet aperçu.
Ne pas présenter ce fichier comme une version Dofus 3.7.11.11 ni
3.7.12.12 prouvée : le SQLite ne porte pas un identifiant de version
client authentifié.

**Manifeste d'export conditionnel, conforme à
`ProfessionCatalogReader.LoadDirectory` :** clés racine
`schemaVersion=1`, `gameVersion` numérique en quatre segments,
`source=local-extraction`, `permission=owner-authorized`,
`format=json` et `asset={file:'recipes.json',bytes,sha256}`.
La licence/source n'est jamais certifiée indépendamment par ce manifeste.
Le lecteur exige sa présence exacte, vérifie la version attendue, la
taille et l'empreinte avant toute admission locale.

**Point de confiance déterminant :** la table
`RecipeData_ingredientIds_junction` est un ensemble avec clé primaire
`(RecipeData_id,target_id)`, **sans ordinal explicite**. L'exporteur
aligne les ingrédients `ORDER BY rowid` sur `quantities[i]` pour
reproduire l'ordre employé par le lecteur SQLite C#. La concordance
des **longueurs** a été observée sur toute la base, mais **l'égalité
sémantique des paires n'est pas prouvable uniquement avec ce schéma**.
Le mode `export` exige donc `--attest-junction-rowid-order`; sinon
`preview` garde l'incertitude visible. Ne pas promettre des coûts de
fabrication exacts tant que cet ordre n'a pas été certifié auprès de
la provenance initiale.

## Mesures de sécurité et limites

Lecture `sqlite3` `mode=ro&immutable=1`,
`PRAGMA query_only=ON`, sans WAL/SHM créés par l'outil.
Source SHA-256 fournie explicitement, calculée avant et après toute lecture.
Le source réel contenait un `-wal` de **0 octet** et un `-shm`
résiduel au moment de l'audit. Leur contenu n'a pas été modifié.
Un WAL contenant des données, un rollback journal ou une source ayant
changé pendant la lecture provoque un refus.
Limites : SQLite <=1 GiB, JSON <=24 MiB, <=50 000 recettes,
<=500 000 liens, <=32 ingrédients par recette, quantités 1..1 000 000.
Rejet des doublons, liens orphelins, listes désalignées, valeurs négatives
ou booléennes, mauvais hash, mauvais schéma, noms trop longs, sortie
existante, attestations manquantes. Aucune mise en réseau, aucune copie
des données DofusDB partenaires publiques, aucun workflow MCP.

**Usage IA/MCP interdit par défaut**. La revendication du partenariat
DofusDB et le droit de scraper le site ne prouvent pas une exception
à la licence de l'API publique LPNC-IA 1.0 pour ingestion IA/MCP.
Ce lot utilise exclusivement la base **locale** fournie et évite toute
assertion de droits sur celle-ci. Le flag `--attest-source-owned`
relève d'une action positive de l'opérateur après vérification externe.

## Validation

- `python -m unittest discover -s tools/local-recipe-catalog -p test_export_recipes.py -q`
  : **10/10 PASS**, fixtures synthétiques couvrant manifeste/UUID,
  quantité exacte selon rowid attesté, XP/yield null, placeholders,
  nom direct via `resultNameId`, SHA/mauvaise version/permission,
  WAL non vide, liens orphelins et mauvais comptes.
- `python -m py_compile tools/local-recipe-catalog/export_recipes.py
  tools/local-recipe-catalog/test_export_recipes.py` : **PASS**.
- `inspect` sur le vrai fichier : **PASS** ; `preview` sur le vrai
  fichier : **PASS**, 4 858 recettes et 24 532 associations conservées
  sans élévation des autorisations.

## Handoff prime

Contrat d'import : appeler `ProfessionCatalogReader.LoadDirectory(dir,
expectedGameVersion)` **uniquement** sur un répertoire d'`export`
dont les attestations et la version ont été vérifiées. Le dossier
`preview` est consultatif et ne doit pas être promu automatiquement.
Pour les plans Bijoutier : ID de métier 16, XP et yield UNKNOWN ;
les quantités d'ingrédients sont des paires déclarées par le SQLite et
nécessitent validation de la convention d'ordre pour être qualifiées
de correspondance exacte. Aucun calcul ferme/craft complet ne peut
être présenté comme certifié à ce stade.
