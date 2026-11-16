# Export local des panoplies — audit du 20 octobre 2026

## Livrable

Exporteur Python standard library entièrement autonome, conservé dans **`tools/local-set-catalog/`**. Il sépare deux opérations :

- **`audit` : lecture seule**, sans déclaration de propriété ou droit. Lit `ItemSetData.items`, `ItemData.itemSetId`, `translations`, effectue la vérification des appartenances et produit un diagnostic JSON dans la console.
- **`export` : opération explicite**, réservée à un *snapshot local que l'utilisateur affirme posséder et être autorisé à réutiliser*. Requiert déclaration `--source-owned`, référence de droits, version déclarée et sa référence, **SHA-256 préalablement relevé**. Écrit un nouveau dossier atomiquement, sans écraser un dossier existant.

Aucune API DofusDB ni récupération réseau. Aucune donnée partenaire ingérée dans les outils IA/MCP. Aucun fichier de l'application ou de la base source n'est modifié. Le lecteur C# existant `EquipmentSetCatalogReader` reste inchangé.

### Entrée : SQLite constatée

| Table | Champs lus | Décodage |
| --- | --- | --- |
| `ItemSetData` | `id`, `items`, `nameId` | `items` est une liste JSON ordonnée des IDs membres ; `nameId` pointe vers `translations` |
| `ItemData` | `id`, `itemSetId`, `nameId` | Chaque pièce retenue doit pointer vers le même set que celui qui la liste |
| `translations` | `id`, `lang`, `value` | Nom FR ou EN si disponible et non ambigu ; sinon `null` |

Les objets hors panoplie portent **`ItemData.itemSetId=-1` dans le fichier examiné**. Ils sont comptabilisés comme sentinelles d'absence de panoplie, et ne sont pas réécrits artificiellement comme membres d'un set. Le code accepte aussi la sentinelle `0` dans une fixture synthétique ; tout autre identifiant négatif est rejeté.

L'export contient uniquement les items membres de sets cohérents. Il ne représente donc **pas** l'ensemble des objets du jeu (pour les autres objets, utiliser un catalogue d'objets distinct). Une recette de set ne peut être déduite de cette base seule : les recettes proviennent séparément de `ProfessionData`.

## Audit de la copie locale réelle

Fichier analysé **sans le modifier** : `%USERPROFILE%\Videos\GIVRE\dofus.sqlite`.

| Observation | Valeur |
| --- | ---: |
| Octets du SQLite | 380 866 560 |
| `ItemSetData` | 929 panoplies |
| `ItemData` | 20 979 objets |
| Membres listés par `ItemSetData.items` | 3 597 références |
| Membres absents d'`ItemData` | 169 |
| Membres avec `itemSetId` contradictoire | 2 |
| Objets avec `itemSetId=-1` | 17 553 |
| Panoplies rejetées pour incompatibilité | 158 |
| Panoplies structurellement cohérentes | **771** |
| Pièces identifiées dans ces 771 panoplies | **2 918** |
| `PRAGMA user_version` | 0 — **aucune version de jeu vérifiée** |

SHA-256 du **fichier principal SQLite au moment de l'audit** :

```text
599908fe9396cde1800390497ca703949576b53acbd261271d103001bda9b199
```

La base possède des fichiers auxiliaires `-wal` (**0 octet observé**) et `-shm` (**32 768 octets observés**). L'audit utilise `mode=ro&immutable=1` et lit le fichier principal sans intégrer d'éventuelles transactions WAL ; la vue n'est donc pas déclarée comme un snapshot scellé ou exhaustif. **L'export réel est refusé si ces fichiers auxiliaires existent.** Il faut préparer séparément un snapshot statique, légalement réutilisable, dont l'intégrité et la version soient documentées.

Ni le nom `dofus.sqlite`, ni son SHA-256, ni `user_version=0` ne prouvent une version précise de Dofus. En particulier, attribuer automatiquement `3.7.12.12` ou `3.7.10.10` à ce fichier serait injustifié. Le paramètre `--game-version` reste obligatoire et ne peut être fixé que d'après une preuve externe conservée par son propriétaire.

Exemples d'associations **observées et structurellement cohérentes** dans cette copie, sans validation serveur ni de licence :

| Panoplie observée | Set ID | Item IDs lus de la liste |
| --- | ---: | --- |
| Panoplie du Jeune Aventurier | 5 | 2473, 2474, 2475, 2476, 2477, 2478 |
| Panoplie Gelax | 6 | 2446, 2447, 2469, 2470, 2471, 2472 |
| Panoplie Tortue | 9 | 2530, 2531, 2532 |
| Panoplie du Sanglier | 21 | 6908, 6909, 6910 |
| Panoplie Moskito | 23 | 6915, 6916, 6917, 6918 |

**Panoplie du Bouftou, ID 1** : le tableau `ItemSetData.items` rapporte les IDs `2411, 2414, 2416, 2419, 2422, 2425, 2428, 18666`. **L'ID 2416 est absent d'`ItemData`** de cette copie, empêchant la validation de tous les membres. La panoplie est donc rejetée par l'export de catalogue C# actuel. La Panoplie du Bouftou Royal (ID 4) présente également un membre absent (2440). Le set 703 énumère des items 11854/13089 dont le `itemSetId` enregistré pointe vers le set 732 ; les deux associations sont contradictoires.

### Vue consultative `UNKNOWN_METADATA`

**Choix de sûreté : pas de placeholder d'item admis.** Un item référencé par `ItemSetData.items` mais absent d'`ItemData` ne possède aucune donnée suffisante pour affirmer sa fiche ou l'appartenance réciproque. Créer un `EquipmentSetItem` artificiel avec `setId` inféré rendrait `EquipmentSetCatalogReader.Admitted=true` pour une composition que le lecteur ne sait pas qualifier. C'est exclu.

Dans l'audit, une panoplie concernée conserve une **observation séparée** avec `viewStatus: "UNKNOWN_METADATA"`, `catalogAdmitted: false`, sa liste source `observedMemberItemIds`, son `observedName` et ses `missingItemIds`. Les contradictions utilisent `INCONSISTENT_MEMBERSHIP`. Si un export partiel est réalisé ultérieurement avec toutes les assertions requises, la même vue figure sous `provenance.json.excludedSetObservations` ; elle n'entre **jamais** dans `equipment-sets.json`.

L'interface peut un jour afficher ces observations en lecture seule avec statut **UNKNOWN_METADATA** et avertissement de version, mais elle ne doit pas les proposer comme panoplies complètes ni en déduire des recettes ou actions. Aucun changement d'UI ou du lecteur C# n'est effectué dans ce lot.

## Commandes

Depuis la racine du dépôt (aucune installation de dépendances Python) :

```powershell
python tools/local-set-catalog/local_set_export.py audit --db "%USERPROFILE%\Videos\GIVRE\dofus.sqlite" --source-sha256
```

La commande `audit` n'a pas besoin d'`--source-owned`, n'écrit pas d'export et affiche les incohérences, les IDs rejetés, les auxiliaires SQLite et le hash source.

Exemple **de syntaxe** pour un snapshot possédé, scellé et dont les droits ont été établis. La version et les références ci-dessous sont fictives et doivent être remplacées ; **ne pas utiliser cette commande comme déclaration que la copie auditée possède ces droits** :

```powershell
python tools/local-set-catalog/local_set_export.py export `
  --db "C:\jeu-de-donnees-autorise\dofus.sqlite" `
  --output "C:\jeu-de-donnees-autorise\export-panoplies-v1" `
  --source-owned `
  --rights-evidence "reference-extraction-personnelle-documentee" `
  --game-version "3.7.12.12" `
  --version-evidence "reference-version-client-documentee" `
  --expected-source-sha256 "<SHA256_64_CARACTERES_REELS>" `
  --allow-partial
```

`--allow-partial` est **explicitement nécessaire** pour écarter les sets incompatibles dans un export de provenance similaire à la copie auditée. Sans cette option, l'export refuse toute source contenant un set incohérent. Les IDs exclus et leurs causes sont consignés. Aucun nom, pièce ou ID de panoplie n'est deviné.

`--source-owned` est une **assertion de l'utilisateur**, `--rights-evidence` et `--version-evidence` des références textuelles : l'outil ne peut pas vérifier un contrat, l'extraction effective ou la version du serveur. Le manifeste généré utilise exclusivement `source=local-extraction`, `permission=owner-authorized` conformément au contrat du lecteur, sans prétendre que ces assertions ont été vérifiées par un tiers. Les labels partenaires et les octets de l'API publique DofusDB ne sont pas admis par ce processus. `provenance.json` précise toujours `rightsIndependentlyVerified=false`, `gameVersionIndependentlyVerified=false`, `publisherVerified=false`, **`aiDisclosureAuthorized=false`**.

## Sorties et intégrité

L'export écrit uniquement un **nouveau dossier**, créé par renommage atomique d'une zone temporaire locale, avec :

- `equipment-sets.json` : exactement `schemaVersion`, `sets` (`id`, `name`, `memberItemIds`) et `items` (`id`, `name`, `setId`). 100 % de ses membres ont un item source et un tag `itemSetId` réciproquement cohérent.
- `manifest.json` : schéma v1 accepté par `EquipmentSetCatalogReader`, version déclarée, format JSON, longueur du fichier et SHA-256 réel ; aucune propriété ajoutée qui ferait rejeter l'import.
- `provenance.json` : **séparé du catalogue admis**, SHA-256 du fichier source, référence de la version/droits *déclarés* et audit chiffré, y compris les observations consultatives rejetées.

Un changement de SHA d'entrée ou de taille/mtime de la base, un lien symbolique/junction, un dossier de sortie déjà existant, un WAL/SHM/journal SQLite, un nom de fichier ou un champ hors bornes bloque l'export. Les tailles maximales du lecteur sont respectées : 64 Kio manifeste, 24 Mio JSON, 10 000 sets, 100 000 items, 64 membres/set, 100 000 liens. Les traductions manquantes/dupliquées deviennent `name=null`, jamais un libellé inventé.

## Tests offline

```powershell
python -m unittest discover -s tools/local-set-catalog -p 'test_*.py' -q
```

**20 tests réussis** (Python 3.14, .NET 10 disponible), incluant test d'intégration qui génère un export à partir d'un petit SQLite entièrement fictif puis le charge par le **véritable `EquipmentSetCatalogReader` C#** via `ReaderSmoke.csproj`. Tests négatifs : absence de `--source-owned`, preuves factices/vides, version erronée, SHA source différent, mutation après audit, source incomplète, relation inverse contradictoire, JSON invalide, doublons, tables manquantes, noms ambigus, sentinelles `-1`/inconnues, destination existante, lien symbolique, refus d'export si SQLite comporte des fichiers auxiliaires et refus CLI sans assertion.

Le test n'utilise ni client Dofus, ni collecte Internet, ni données de panoplie Bouftou fictivement ajoutées. Aucune licence IA n'est induite par la réussite technique du test.
