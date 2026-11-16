# ProfessionData — sources locales, schémas et admission (20 octobre 2026)

## Périmètre et résultat

Le moteur **ProfessionData** est une lecture pure et déterministe des recettes, objets, ingrédients et métiers ; il n'exécute aucun script, réseau, HDV ni action Dofus. Les fichiers sources restent sur le disque, inchangés. Le code n'aspire aucun service ni endpoint public DofusDB.

Fichiers :
- `GivreClone.App/Services/ProfessionData/ProfessionCatalog.cs` : DTO, provenance et requêtes.
- `ProfessionCatalogReader.cs` et `ProfessionCatalogReader.Sqlite.cs` : manifeste contrôlé par empreinte de *contenu* locale, décodage borné JSON/SQLite.
- `ProfessionPlannerCatalogAdapter.cs` : projection explicite vers les DTO du moteur `ProfessionPlanner`.
- `GivreClone.Tests/ProfessionDataCatalogTests.cs` : jeux de données synthétiques, succès et échecs.

APIs :

```csharp
using GivreClone.App.Services.ProfessionData;
var catalog = ProfessionCatalogReader.LoadDirectory(folder, expectedGameVersion: "3.7.12.12");
if (catalog.Admitted)
{
    var data = catalog.RecipesForJob(jobId: 13, maximumLevel: 30);
    var projection = ProfessionPlannerCatalogAdapter.ForJob(catalog, jobId: 13);
    // projection.Recipes -> ProfessionPlanner.ProfessionRecipe
    // projection.ExperienceTable -> ProfessionPlanner.XpTable (Verified=false)
    // catalog.FindItem(gid), catalog.Items, catalog.Jobs, catalog.LevelThresholds
}
```

## Origine et observations réellement constatées

L'inspection **en lecture seule** d'un fichier local `%USERPROFILE%\Videos\GIVRE\dofus.sqlite` (380 866 560 octets), dont **ni licence indépendante, ni empreinte de version, ni droit de distribution n'ont été certifiés ici**, a permis de vérifier les colonnes suivantes :

| Table | Champs effectivement lus | Interprétation admissible |
| --- | --- | --- |
| `RecipeData` | `id`, `jobId`, `quantities`, `resultId`, `resultLevel` | Identifiant interne texte, métier, tableau JSON de quantités, item résultat, niveau requis |
| `RecipeData_ingredientIds_junction` | `RecipeData_id`, `target_id` | Ordre des ingrédients par `rowid`, apparié 1:1 au tableau `quantities` |
| `ItemData` | `id`, `nameId`, `level` | ID, clé de traduction, niveau item ; `level=0` devient `UNKNOWN` |
| `JobData` | `id`, `nameId` | ID métier et clé de traduction |
| `translations` | `id`, `lang`, `value` | Traductions, français ou anglais explicite |

Cette copie contient 4 858 recettes et 24 532 liens ingrédient. Dix références de liens ne retrouvent aucun objet dans `ItemData` : elles restent **ID connus, métadonnées `UNKNOWN`**, avec `ProfessionItem.IsPlaceholder=true`. Le fichier n'a pas été admis tel quel par le module : il manque un manifeste admissible, une version vérifiée et un fondement de permission correspondant.

**Non prouvés par ce schéma SQLite** : XP gagné par craft, seuils d'XP de métier, quantité d'objets produite par craft, prix, probabilités de drop, durées de récolte/craft et chemins. `RecipeData.resultLevel` n'est pas une valeur d'XP ; `ItemData.craftXpRatio` ne constitue pas un XP par craft. Ces valeurs restent `null`, jamais `1` ou un calcul estimé. Dans le JSON local, les champs d'XP et les seuils peuvent être présents comme *déclarations de la source* ; leur empreinte prouve uniquement l'intégrité des octets, pas la réalité du jeu.

## Manifeste requis

Un dossier contient exactement `manifest.json` et son actif local `dofus.sqlite` ou `recipes.json`. Exemple (remplacer les valeurs par celles du fichier réel et la source effectivement autorisée) :

```json
{
  "schemaVersion": 1,
  "gameVersion": "3.7.12.12",
  "source": "local-extraction",
  "permission": "owner-authorized",
  "format": "json",
  "asset": {
    "file": "recipes.json",
    "bytes": 1234,
    "sha256": "<64-caracteres-hexadecimaux-SHA256-reels>"
  }
}
```

Sources admises : `local-extraction` et `privately-licensed-offline` uniquement, avec permission `owner-authorized`. La chaîne de provenance est déclarative et son authenticité d'éditeur n'est pas confirmée (`Source.PublisherVerified=false`). Un manifeste arbitrairement fabriqué ne prouve pas un contrat privé. Les octets issus de l'**API publique DofusDB** sont exclus du pipeline, même si l'utilisateur dispose séparément du droit de scraper des pages du site ; un contrat particulier précisant l'usage IA devra être évalué en dehors de ce module.

Conditions d'admission : version quatre composantes numérique, version attendue facultative égale, rôle/nom de fichier fixe, taille exacte, SHA-256 réel calculé avant/après lecture, absence de lien symbolique sur dossier/fichiers, absence de WAL/journal SQLite, en-tête SQLite et tables/colonnes vérifiés, quantité strictement positive, pas de décalage entre ingrédients et quantités, IDs uniques et sans orphelins de recette. Erreur = `Admitted=false`, `Recipes=[]`, explication sous `Issues`.

Limites : manifeste 64 Kio, JSON 24 Mio, SQLite 1 Gio, 100 000 objets, 1 000 métiers, 50 000 recettes, 500 000 liens ingrédients, 50 000 seuils, 32 ingrédients par recette, niveaux 1–200, quantités par lien 1–1 000 000. Les noms sont libres mais bornés à 256 caractères ; les traductions absentes restent `null`. SQL en lecture seule (connexion `ReadOnly`, `query_only`, cache privé, connexions non mutualisées).

## Schéma source JSON v1 (locale/licenciée)

```json
{
  "schemaVersion": 1,
  "items": [
    {"id": 101, "name": "Plume", "level": 1},
    {"id": 102, "name": null, "level": null}
  ],
  "jobs": [{"id": 13, "name": "Bijoutier"}],
  "recipes": [{
    "id": "recette-source-unique",
    "jobId": 13,
    "resultItemId": 101,
    "requiredLevel": 20,
    "resultQuantity": null,
    "experiencePerCraft": null,
    "ingredients": [{"itemId": 102, "quantity": 3}]
  }],
  "levelThresholds": []
}
```

`resultQuantity`, `requiredLevel`, `experiencePerCraft` peuvent être absents ou `null`, et resteront `UNKNOWN`. Si des seuils existent dans un export source possédé, utiliser `{"jobId":13,"level":20,"cumulativeExperience":12345}` avec la vraie valeur, sans la dériver d'une formule supposée. Les niveaux XP successifs doivent augmenter strictement.

## Contrat avec ProfessionPlanner et UI/MCP

`ProfessionPlannerCatalogAdapter.ForJob(catalog, jobId)` fabrique une projection bornée (max. 2 000 recettes) avec identifiant numérique stable SHA-256 de `gameVersion + "\n" + recipe.Id` ; collisions détectées et rejetées. Les recettes sans niveau requis **ou quantité produite explicitement connue** restent consultables dans `catalog.Recipes`, mais sont exclues de la projection moteur : aucun output `1` inventé.

`Verified=false`, `XpVerified=false`, `XpTable.Verified=false` restent corrects après un SHA valide : celui-ci prouve l'identité des octets, pas la véracité de la donnée. Les étapes consultatives pourront signaler l'incertitude, et la certification des valeurs fonctionnelles nécessitera un autre canal d'attestation défini par le prime. L'adaptateur ne marque ni la présence d'une ressource sur une carte, ni une recette réalisable, ni un gain XP garanti.

Intégration compilation : `GivreClone.App` inclut automatiquement les fichiers du dossier. `GivreClone.Tests.csproj` doit inclure `<Compile Include="..\GivreClone.App\Services\ProfessionData\*.cs" Link="Linked\ProfessionData\%(Filename)%(Extension)" />` ; l'ajout est fait par le prime selon son OWN. La façade MCP et le panneau UI peuvent consommer `ProfessionCatalog` et `ProfessionPlannerCatalogProjection`.

Tests offline : `dotnet test GivreClone.Tests/GivreClone.Tests.csproj --filter FullyQualifiedName~ProfessionDataCatalogTests`. Couverture : admission JSON/SQLite synthétiques, sha altéré, manifeste absent, version erronée, chemin traversant, licence/source API interdite, appariement invalide, quantité zéro, métadonnées d'item absentes et conservation de `UNKNOWN`, déterminisme de la projection et XP non certifié.
