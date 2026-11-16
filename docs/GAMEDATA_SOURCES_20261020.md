# Sources statiques Dofus — audit, lecture bornée, comparaison sans import

Date : 20 octobre 2026. Worktree : GivreWork-Integration-20261020.
Ce lot est entièrement **review-only / hors réseau au runtime / hors jeu**.
L'inspection de sources publiques ci-dessous n'est PAS une homologation du client,
des paquets, des prix HDV ou des données de combat.

## Sources et état de vérification

| Source | Usage candidat | Preuve et limites |
| --- | --- | --- |
| ledouxm / dofus-sqlite (Douxlem / dofusqlite) | Objets, traductions, sorts, recettes, quêtes, positions ; autre fichier maps.sqlite pour interactifs | https://github.com/ledouxm/dofus-sqlite, README et releases. Les classes sont extraites du client, tables ItemData, RecipeData, SpellData, QuestData, translations. Pipeline autonome et assets versionnés, mais un nom de release seul ne prouve ni la teneur du fichier local ni les effets dans le jeu. |
| OpenDofusDB | Source potentielle de recherche / comparaison des catalogues et des versions | Référencée historiquement dans docs/MZP10_PRIME_HANDOFF_20261016.md. Aucun export local et schéma piné de ce fournisseur trouvé dans ce lot. **Pas de connecteur inventé** et pas de promesse de couverture versionnée 3.7.12.12. |
| Client installé / bundles Unity | Référence locale à réconcilier par empreintes, IDs et champs de version | GivreClone.App/Services/GameNameResolver.cs lit déjà Items, SpellLevels, quests et i18n des bundles du client. Ne pas le remplacer aveuglément par une autre release. |
| GameDb existant | Consultation SQLite en lecture seule des recettes/objets/quête | GivreClone.App/Services/LotM/GameDb.cs : 44-61 ouvre SQLite ReadOnly, pooling false ; 337+ exploite ItemData / RecipeData / translations, 473+ QuestData. Aucun téléchargement dans cet adaptateur. |

### Inventaire local vérifié (ne pas confondre provenance et présence)

- GivreActive/dofus.sqlite **existe mais fait 0 octet** : il ne s'agit pas d'une base exploitable.
- Le dossier GIVRE/proto-3.7.11.11 ne contenait que dofus.proto ; ce protocole n'est ni une base de données ni une preuve de données gameplay.
- GivreActive/DataSets avait des dossiers historiques 3.6.12.16, 3.7.4.4, 3.7.5.5 et 3.7.7.6, mais pas de dossier versionné 3.7.11.11 / 3.7.12.12 identifié par ce parcours.
- Le dossier round6-prompt2-release mentionné dans d'anciens rapports n'existait pas dans l'arbre d'intégration inspecté. **Ne pas annoncer la disponibilité actuelle de ce SQLite par extrapolation des rapports historiques.**
- native/ItemIdOverlay/src/items.provenance.json et items.tsv existent. Le manifeste dérivé indique 3.7.11.11, versionVerified=true, release ledouxm 6.0_3.7.11.11, 20 784 objets, 1 objet sans ItemType exclu, aucun prix HDV. Le TSV est une projection d'objets, pas un snapshot complet de sorts/cartes/quêtes/recettes.
- docs/codex/R6_PROMPT2_ITEM_OVERLAY.md rapporte pour la release 3.7.11.11 (publiée 2026-10-16T12:14:20Z) un SQLite de 384966656 octets / SHA-256 bcb66ca4611bdff29c5e303cb4dd4770790c6c4905e6a81b0e8c2a33726501b7, plus 4 743 recettes, source 20 785 objets. Ces **preuves historiques consignées** ne peuvent être reverifiées sur un fichier absent.
- Le prime a signalé à ce worker que l'asset dofus.sqlite de la release 3.7.12.12 a une empreinte différente, alors que maps.sqlite et dofus.proto seraient identiques entre les deux tags. Sans les deux fichiers complets, leurs empreintes externes intégrales et des contrôles byte-à-byte locaux, **la comparaison de données réelles demeure NON EFFECTUÉE**. Un SHA différent ne localise pas les tables ou lignes modifiées.
- Le prime a placé **uniquement** maps.sqlite de la release 3.7.12.12 sous artifacts/game-data-review/3.7.12.12/maps.sqlite. Taille observée : 782 336 octets, SHA-256 recalculé et piné : 10f0e6f54ec1bd49f3458a55768096813d4f1be3d08afbe91c9d6be479d1cf0b. Son schéma réel est map_interactions(mapId,worldId,gfxId,cellId,interactionId), 31 707 lignes, 6 335 mapId selon le relevé séparé du prime. **Aucune équivalence gfxId → ressource/nom n'est connue** ; ne pas inférer ces noms.

## Livrable codé — GameDataSnapshotReader

Fichier : GivreClone.App/Services/GameDataUpdates/GameDataSnapshotReader.cs.

API de consultation, sans écriture sur les sources :

- ReadDirectory(folder, expectedVersion) : exige un dossier explicite avec manifest.json de 65 536 octets maximum, schemaVersion=1, source déclaré, gameVersion à quatre composantes numériques et 1 à 2 assets explicitement nommés.
- Assets autorisés : dofus.sqlite sous role gamedata (obligatoire), maps.sqlite sous role maps (optionnel). Ni URL, ni chemin relatif avec slash, ni symlink de fichiers/dossier, ni archive ZIP, ni autre catégorie arbitraire. Taille >= 4 KiB et <= 1 GiB pour chaque SQLite ; nombre d'assets <= 2.
- Vérifie taille réelle puis SHA-256 de tous les fichiers avec lecture en flux borné. Les échecs échouent fermés. Vérifie signature SQLite et n'ouvre la connexion qu'en ReadOnly, Cache=Private, Pooling=false avec PRAGMA query_only=ON ; limite interrogation de sqlite_master à 513 tables, de table_info à 96 colonnes et timeout 3 secondes par commande.
- Découverte de schémas attendus **sans lecture complète des lignes** : ItemData (items), SpellData (sorts), RecipeData (recettes), MapPositionData/MapPositionsData (positions), QuestData (quêtes), translations ; maps.sqlite ne reçoit qu'un signal de présence de tables d'interactifs, pas une sémantique supposée.
- Le booléen PublisherVerified reste toujours false. Reviewable prouve seulement que les fichiers locaux correspondent au **manifeste local choisi par l'opérateur** et que des tables attendues existent ; EligibleForRuntimeImport est **toujours false**.
- Compare(snapshotA, snapshotB) : retourne NOT_COMPARABLE si un manifeste/base échoue ou les versions ne sont pas distinctes et ordonnées. Sinon SCHEMA_ONLY, indiquant quelles catégories ont un schéma observé inchangé, modifié ou absent. Même SCHEMA_ONLY n'affirme jamais égalité des lignes, compatibilité protocole ou autorisation de mettre les données en production.

### Aperçu des cartes avec l'archive partielle disponible

Fichier : GivreClone.App/Services/GameDataUpdates/MapInteractionReviewReader.cs.

La méthode Preview(path, expectedSha256, mapId?, maxRows) accepte **uniquement**
maps.sqlite, empreinte complète explicite et limite entre 1 et 64 lignes. Elle vérifie
SHA-256 et le schéma exact à cinq colonnes, puis lit une page bornée depuis
map_interactions sur une connexion SQLite ReadOnly / query_only. Un filtre mapId
est paramétré. Elle ne donne que les cinq IDs numériques et indique la troncature ;
ni les noms des ressources, ni l'accès aux cellules, ni les actions de récolte ou
les graphes de navigation ne sont inférés. PublisherVerified et
RuntimeImportAuthorized restent faux. Cela permet une **inspection utile de la
base de cartes 3.7.12.12 sans supposer que dofus.sqlite correspondant est présent**.

### Contrat de manifeste local (schéma v1)

Clés racines strictes : schemaVersion (entier 1), source (ledouxm/dofus-sqlite, OpenDofusDB ou local-extraction), gameVersion (ex. 3.7.11.11), assets (liste).

Chaque asset : role, file, bytes, sha256, sans clé supplémentaire. Le SHA-256 est une chaîne hexadécimale de 64 caractères ; bytes est la taille réelle. Les noms de fichiers acceptés sont exactement dofus.sqlite et maps.sqlite. Les objets à clés JSON dupliquées sont rejetés. Il faut fournir les vrais fichiers, pas uniquement leurs chemins.

Ne pas considérer ce manifeste auto-déclaré comme une attestation de fournisseur. Pour une promotion future, prévoir une preuve externe indépendante (release ID, asset ID, SHA-256/size confirmés côté GitHub, date/version et comparaison du client local) avant toute ingestion, avec accord du prime et tests négatifs adaptés.

## Intégration UI proposée — PAS câblée

Ajouter ultérieurement dans les réglages / GameData une fenêtre **« Sources statiques — revue »** :

1. Choix explicite de deux dossiers de snapshots hors répertoire d'installation du jeu.
2. Affichage version déclarée, SHA local vérifié, source *déclarée non authentifiée*, cinq catégories de tables, différences de schéma.
3. Affichage « 3.7.11.11 vs 3.7.12.12 non comparable » si l'une manque ; aucune action « Importer » tant que la preuve d'origine et la concordance exacte avec le client/runtime ne sont pas certifiées.
4. Noms d'objets/sorts/recettes lus via GameDb existant seulement après sélection, en lecture seule, avec champs absents explicitement inconnus. Les tarifs HDV ne viennent jamais de ces tables statiques.

L'UI n'a pas été modifiée dans ce lot (hors OWN). Aucun paquet de gameplay, compte, processus Dofus, téléchargement, mise à jour automatique ou bundle/runtime modifié.

## Validation

Tests synthétiques offline dans GivreClone.Tests/GameDataUpdateSnapshotTests.cs :
acceptation manifest+SQLite avec cinq tables, vérification taille/empreinte, constat sans écriture, comparaison versionnée SCHEMA_ONLY uniquement, mismatch version, falsification d'un octet, chemin/URL refusé, version invalide, taille excessive, absence de traductions/tables et clés JSON dupliquées, fournisseur déclaré mais jamais authentifié, snapshot manquant.

GivreClone.Tests/GameDataUpdateMapsTests.cs couvre l'aperçu SQLite synthétique
et, **si présent**, le vrai fichier maps.sqlite déjà téléchargé par le prime :
hash intégral, schéma, colonnes et empreinte inchangée après lecture.
Validation initiale : **26 tests GameDataUpdate sur 26 PASS** après recompilation
des sources liées au projet de tests ; revue locale des cartes réelle sans réseau.
Validation finale après dernières modifications : dotnet build GivreClone.App
**PASS, 0 erreur / 202 avertissements** sur l'arbre partagé ; dotnet test
GivreClone.Tests avec filtre FullyQualifiedName~GameDataUpdate
**26/26 PASS, 0 échec/ignoré** ; git diff --check des fichiers OWN **PASS**.
Les avertissements de build reflètent l'ensemble des dépendances courantes du
worktree et ne constituent pas une preuve de comportement en jeu.

La compilation/résultats exacts de ce lot sont rapportés par le worker séparément et doivent
être rejoués après intégration par le prime. Ces tests ne contactent pas le réseau et ne valident pas le client.
