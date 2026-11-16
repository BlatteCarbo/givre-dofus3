# EquipmentSetData — panoplies et objets (20 octobre 2026)

## Résultat et périmètre

Le lecteur local `EquipmentSetData` permet de sélectionner **une panoplie entière** ou un objet quelconque à partir des **identifiants et associations d'une source autorisée**. Il expose les pièces précises de chaque panoplie, une recherche générique par nom et un raccord consultatif au catalogue `ProfessionData`. Il ne lance pas Dofus, n'ouvre aucune URL et n'effectue aucun craft, collecte, achat ni requête MCP.

Les fichiers nouveaux sont exclusivement :

- `GivreClone.App/Services/EquipmentSetData/EquipmentSetCatalog.cs` : DTO et recherche.
- `EquipmentSetCatalogReader.cs` : import JSON local strict, manifeste/version/SHA, limites et validation bidirectionnelle.
- `EquipmentSetRecipeBinder.cs` : raccord des pièces d'une panoplie à des `ProfessionData.ProfessionRecipe` partageant **exactement** le même `gameVersion`.
- `GivreClone.Tests/EquipmentSetDataCatalogTests.cs` : fixtures synthétiques et cas négatifs.

## Inspection réellement faite

Le dépôt d'intégration `artifacts/game-data-review/3.7.12.12/` fournit seulement `maps.sqlite` et `dofus.proto` : **pas de `dofus.sqlite`, pas de snapshot `ItemSetData` versionné avec manifeste SHA**, et donc aucune composition réelle certifiable à importer dans le catalogue aujourd'hui.

L'inspection **en lecture seule** de `%USERPROFILE%\Videos\GIVRE\dofus.sqlite`, fichier situé hors du dépôt, a établi la structure d'un export SQLite existant :

| Table | Colonnes observées | Signification retenue |
| --- | --- | --- |
| `ItemSetData` | `id`, `items` (tableau JSON d'IDs), `nameId`, `bonusIsSecret`, `isCosmetic`, `effects` | Identifiant de panoplie, membres explicitement énumérés, référence de nom |
| `ItemData` | `id`, `nameId`, `itemSetId` | Identifiant d'objet, référence de nom, identifiant de panoplie |
| `translations` | `id`, `lang`, `value` | Traduction possible de `nameId`, si la ligne est présente |

La copie inspectée contient 929 lignes `ItemSetData`. Ni sa version exacte, ni ses droits d'exploitation pour cette finalité, ni une attestation éditeur ne sont établis à partir d'un nom de fichier. **Ce fichier n'est pas consommé par le nouvel importeur**.

`GameNameResolver` expose `ItemDataLoaded`, une classification d'objets et des résolutions de noms ; il n'expose pas une liste vérifiée des pièces d'une panoplie. Le module n'utilise donc pas le nom pour déduire une composition.

L'exemple utilisateur *Panoplie Bouftou entière* reste un objectif d'interface : aucune liste canonique de ses membres n'est codée ou simulée ici. Les jeux de données d'essai utilisent exclusivement des ensembles et objets explicitement **synthétiques**.

## Manifeste local v1

Créer un dossier explicitement sélectionné contenant `manifest.json` et `equipment-sets.json`. Exemple de **structure** du manifeste — remplacer les octets/empreintes par les valeurs effectivement calculées et utiliser seulement la version/source que l'on est autorisé à déclarer :

```json
{
  "schemaVersion": 1,
  "gameVersion": "3.7.12.12",
  "source": "local-extraction",
  "permission": "owner-authorized",
  "format": "json",
  "asset": {
    "file": "equipment-sets.json",
    "bytes": 1234,
    "sha256": "<64-caracteres-hexadecimaux-de-la-vraie-empreinte>"
  }
}
```

Valeurs de `source` admises par ce lecteur : `local-extraction` ou `privately-licensed-offline`. Permission déclarée exacte : `owner-authorized`. `source=DofusDB-public-api` est refusé. Les restrictions de licence d'utilisation IA de l'API publique DofusDB sont distinctes de l'autorisation de l'utilisateur de consulter ou scraper le site en tant que partenaire. **Aucune donnée de son API publique ne doit entrer dans ce pipeline ni être diffusée à MCP.** Le lecteur n'émet aucun droit MCP : `EquipmentSetSource.AiDisclosureAuthorized == false`.

Une empreinte SHA-256 vérifie l'intégrité des **octets locaux** et la version est une **déclaration dans le manifeste** ; ni l'empreinte ni les labels ne prouvent l'origine éditeur, un contrat de licence ou la compatibilité avec un serveur Dofus. `PublisherVerified=false` dans tous les cas.

### Jeu de données v1 — exemple fictif

```json
{
  "schemaVersion": 1,
  "sets": [
    {
      "id": 700,
      "name": "Ensemble démo atelier",
      "memberItemIds": [100, 101]
    }
  ],
  "items": [
    { "id": 100, "name": "Coiffe de démonstration", "setId": 700 },
    { "id": 101, "name": "Ceinture de démonstration", "setId": 700 },
    { "id": 300, "name": "Ressource de démonstration", "setId": null }
  ]
}
```

Seuls les `id`/relations présents dans cet export peuvent être affichés. Le `name` d'une panoplie ou d'un objet peut être `null` si sa traduction est introuvable ; l'ID reste exploitable, mais la recherche par nom l'ignore. `setId=null` signifie l'absence **déclarée** de panoplie. Ne jamais transformer silencieusement une valeur sentinelle SQLite `0` en un identifiant de panoplie.

L'extracteur offline qui préparera ce JSON doit lire `ItemSetData.items` et `ItemData.itemSetId`, résoudre les noms depuis les traductions accessibles et conserver la provenance des octets et les droits de réutilisation. **Ce module ne génère pas ce JSON depuis le fichier brut non admis.**

## Admission et contrôles

Le chargement rejette entièrement le catalogue en cas de problème ; aucun résultat partiel ambigu n'est exposé.

- Métadonnées : version jeu exactement quatre composants numériques (0–65535 chacun), schéma manifeste 1 et schéma données 1, format `json`, permission/source admises, nom d'asset exact.
- Intégrité : tailles exactes, SHA-256 64 caractères et comparaison avant/après décodage, parse JSON strict (ni commentaire, ni virgule terminale, ni propriété dupliquée ou inconnue).
- Chemins : pas de traversée relative, pas de fichiers ou dossier liés, parents de dossier vérifiés pour détecter les points de réanalyse.
- Composition : membre connu dans `items`, membre une seule fois dans une panoplie, un item dans une seule panoplie, `item.setId` égal à `set.id`, pas d'item déclaré comme appartenant à une panoplie qui ne l'énumère pas.
- Bornes : manifeste 64 Kio ; JSON 24 Mio ; 10 000 panoplies ; 100 000 objets ; 64 pièces par panoplie ; 100 000 appartenances au total ; noms 256 caractères ; résultats de recherche au plus 100.

Les erreurs sont consultables via `catalog.Issues` avec `Admitted=false`, `Sets=[]` et `Items=[]`.

## API pour UI et moteurs de planification

```csharp
using GivreClone.App.Services.EquipmentSetData;
using GivreClone.App.Services.ProfessionData;

var sets = EquipmentSetCatalogReader.LoadDirectory(datasetDirectory, expectedGameVersion: "3.7.12.12");
if (!sets.Admitted) { /* afficher sets.Issues et interdire la simulation */ }

// Recherche : chaque résultat correspond à un vrai ID de l'export.
var matchingSets = sets.FindSets("Ensemble", maxResults: 50);
var matchingItems = sets.FindItems("Coiffe", maxResults: 50);
var exactSet = sets.FindSet(700);
var exactItem = sets.FindItem(100);
var pieces = sets.ItemsOfSet(700);

// Catalogue ProfessionData admis et épinglé indépendamment :
ProfessionCatalog? recipes = ProfessionCatalogReader.LoadDirectory(recipeDirectory, "3.7.12.12");
var joined = EquipmentSetRecipeBinder.ForSet(sets, recipes, setId: 700);
if (joined.Available) { /* joined.Set, joined.Pieces, joined.Issues */ }
```

`FindSets` / `FindItems` retournent **toutes** les correspondances sous forme bornée, triées par ID et sans deviner une correspondance unique. Pour une recherche de Panoplie Bouftou, l'UI ne peut présenter une composition réelle que lorsqu'une source admissible contient cette panoplie et ses membres.

`EquipmentSetRecipeBinder.ForSet` fait une jointure par `ResultItemId == Piece.Id` (et version exacte). Pour chaque pièce, les statuts possibles sont :

| Statut | Sens |
| --- | --- |
| `LOCAL_RECIPE_UNVERIFIED` | Recette présente dans le catalogue source, gameplay non attesté |
| `NO_RECIPE_IN_SUPPLIED_SNAPSHOT` | Recette absente de cette copie ; cela ne prouve pas que l'objet ne se fabrique pas |
| `RECIPE_SOURCE_UNKNOWN` | Catalogue de recettes non fourni ; impossible de conclure |

Les recettes sont des `ProfessionData.ProfessionRecipe` existantes, avec niveaux et quantités explicitement connus ou `UNKNOWN`. Aucune formule d'XP, taux de drop, prix HDV, stock banque/havre-sac, production multiple, emplacement de ressource, trajet ou recette sous-jacente n'est inventé. Le moteur récursif de planification peut être raccordé par le prime après validation des catalogues et des droits appropriés.

## Validation

Commande :

```powershell
dotnet test GivreClone.Tests\GivreClone.Tests.csproj --no-build --filter FullyQualifiedName~EquipmentSetDataCatalogTests
```

Tests synthétiques : admission et empreinte sans écriture de données, recherche, absence de Bouftou canonique, manifest absent/SHA modifié, versions et formats refusés, source publique/permission non admise, traversée de chemin, membres inconnus ou dupliqués, appartenances incohérentes, identifiants dupliqués, JSON dupliqué, version de schéma invalide, noms inconnus et jointure recettes à version égale/différente.

Validation finale au 20/10/2026 : **21 réussis / 0 échoué**, compilation ciblée `GivreClone.Tests` **0 erreur**. Les tests additionnels sur les limites, la duplication de champs du manifeste et les noms vides sont exécutés et réussis. Des erreurs de compilation temporaires dans des fichiers `ItemCraftMcp` créés simultanément ont été corrigées par leurs propriétaires avant cette validation finale. Le prime a déjà intégré `Services/EquipmentSetData/*.cs` au csproj tests. Aucun jeu réel Dofus n'a été copié, partagé ou importé.
