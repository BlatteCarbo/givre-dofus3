# DofusDB Partner — import de cache local read-only (20 octobre 2026)

## Périmètre

Importeur **hors réseau, hors client Dofus**, sans actions gameplay, achats, sorties MCP implicites ou écriture de cache. Il prend en entrée le répertoire produit par `tools/dofusdb-partner-catalog/scrapy_partner.py` et son `review-summary.json`.

Le snapshot source a **19 collections / 87 327 documents annoncés** ; six collections alimentent cet index : `items` (21 554), `item-sets` (940), `recipes` (4 743), `jobs` (23), `monsters` (5 129) et `map-positions` (15 409), soit **47 798 lignes contrôlées**. La validation du résumé porte sur les **19 collections** ; les SHA des pages et les doublons sont vérifiés intégralement pour les **six collections indexées**. Les 13 autres collections sont seulement comptées dans le résumé, **pas** recertifiées page par page.

## Contrat C# public pour l'intégrateur

Namespace : `GivreClone.App.Services.DofusDbPartner`.

```csharp
var admission = new DofusDbPartnerAdmission(
    OperatorApprovedRuntimeUse: true,
    OperatorApprovedAiMcpUse: false,
    OperatorEvidenceId: "evidence-reviewed-20261020");

var result = DofusDbPartnerCatalogReader.LoadDirectory(
    cacheRoot, admission);
if (!result.Success) { /* afficher result.Reason ; ne pas exploiter de données */ }
var catalog = result.Catalog!;

// Recherche, UI et lecture consultatives :
IReadOnlyList<DofusDbPartnerItem> items = catalog.SearchItems("bouftou", 30, 0);
DofusDbPartnerItem? item = catalog.GetItem(2411);
IReadOnlyList<DofusDbPartnerSet> sets = catalog.SearchSets("bouftou");
DofusDbPartnerSet? set = catalog.GetSet(1);
IReadOnlyList<DofusDbPartnerSet> memberships = catalog.GetSetsContaining(2411);
IReadOnlyList<DofusDbPartnerRecipe> recipes = catalog.GetRecipesForItem(2411);
DofusDbPartnerJob? job = catalog.GetJob(2);
IReadOnlyList<DofusDbPartnerMonster> monsters = catalog.SearchMonsters("larve");
DofusDbPartnerMonster? monster = catalog.GetMonster(31);
DofusDbPartnerMapPosition? map = catalog.GetMapPosition(0);
IReadOnlyList<DofusDbPartnerMapPosition> maps =
    catalog.FindMapPositions(3, -3, worldMapId: 1, limit: 50);
```

`DofusDbPartnerLoadResult` contient `Success`, `Catalog` et `Reason`. En cas d'échec, `Catalog=null`, aucun index partiel n'est renvoyé. `DofusDbPartnerCatalog` expose `ItemCount`, `SetCount`, `RecipeCount`, `JobCount`, `MonsterCount`, `MapPositionCount` et `Provenance` (SourceKind `dofusdb`, SourceGameVersion `UNKNOWN`, droits enregistrés, SHA-256 du résumé et compteurs `SummaryCounts` vs `VerifiedCounts`).

Les DTO sont :

- `DofusDbPartnerItem(Id, NameFr?, NameEn?, Level?, ItemSetId?)`
- `DofusDbPartnerSet(Id, NameFr?, NameEn?, MemberItemGids)`
- `DofusDbPartnerRecipe(SourceRecipeId, ResultItemGid, ResultQuantity=null, Ingredients[ItemGid,Quantity?], JobId?)`, avec `XpPerCraft=null`
- `DofusDbPartnerJob(Id, NameFr?, NameEn?)`
- `DofusDbPartnerMonster(Id, NameFr?, NameEn?, SubareaIds)`
- `DofusDbPartnerMapPosition(Id, X, Y, SubAreaId?, WorldMapId?)`

Les identifiants des recettes sont **les identifiants de documents de la source DofusDB**, **pas des opcodes réseau**. Les membres des panoplies proviennent du champ **réel** `item-sets[].items[].id`. Les composants des recettes proviennent de `ingredientIds[]` et `quantities[]` alignés. Le schéma DofusDB capturé **ne fournit pas de quantité de sortie** ni l'XP gagnée : `ResultQuantity` et `XpPerCraft` sont donc **toujours null**, jamais 1, jamais une estimation.

`map-positions` fournit des identifiants de position cartographique DofusDB (dont `id=0`) et des coordonnées X/Y, **pas une transition prouvée**, ni un mapId/une cellule de session réseau, ni un chemin inter-cartes certifié. Aucun trajet automatique ne peut être déduit de ces données.

## Contrôles de sécurité et limites

- Demande une **approbation runtime explicite de l'opérateur**, avec `OperatorEvidenceId` alphanumérique borné ; sinon refus.
- Le fichier source dit `sourceGameVersion=UNKNOWN`, `aiMcpAllowed=false`, `authorizedRuntimeUse=false`, `sourceRightsVerifiedIndependently=false`, `gameplayCertified=false`. **Ces valeurs restent intactes dans Provenance.** Un opérateur peut fournir une assertion runtime distincte et, séparément, une assertion IA/MCP. Cela ne rend **jamais** les droits « vérifiés indépendamment ».
- Lecture locale uniquement de chemins connus dans les six sous-dossiers, jamais d'URL, de liens reparse, d'exécutables, d'autres fichiers, de secrets ou de logs de session.
- Refus si résumé incomplet, collection manquante, pages manquantes/supplémentaires, ordre de pagination ou total incohérent, métadonnée SHA altérée, octets inattendus, doublons `_id` ou doublons d'identifiant métier, champs nécessaires invalides ou tableau `ingredientIds/quantities` de tailles différentes.
- Page `max 8 MiB`, métadonnées `max 8192 octets`, résumé `max 64 KiB`, `max 20 000 pages/collection`, `max 2 000 000 documents/collection`, pagination et recherche retournent au plus 100 résultats.
- Les tableaux issus des pages sont recopiés ; les dictionnaires et tableaux exposés sont en lecture seule. Aucun stock live, rôle client, état de combat ou compte joueur n'est ingéré.

**Contrat légal :** un partenariat autorisant un crawl ou une revue locale ne démontre pas automatiquement un droit de réutilisation dans une application ni un droit d'envoi à un LLM/MCP. Avant le raccord UI/service, le prime doit établir les autorisations réelles et fournir une preuve d'opérateur appropriée. Avant de transmettre à une IA, il doit également vérifier expressément les droits IA/MCP ; le lecteur ne modifie jamais `review-summary.json` pour le prétendre.

## Compilation / tests

Fichiers strictement sous :

- `GivreClone.App/Services/DofusDbPartner/DofusDbPartnerCatalog.cs`
- `GivreClone.App/Services/DofusDbPartner/DofusDbPartnerCatalogReader.cs`
- `GivreClone.Tests/DofusDbPartnerCatalogTests.cs`
- `docs/DOFUSDB_RUNTIME_IMPORT_20261020.md`

App SDK inclut automatiquement les nouveaux `.cs`. Le projet de tests lie manuellement les sources pures ; **le prime doit ajouter** :

```xml
<Compile Include="..\GivreClone.App\Services\DofusDbPartner\*.cs"
         Link="Linked\DofusDbPartner\%(Filename)%(Extension)" />
```

Puis :

```powershell
dotnet test GivreClone.Tests\GivreClone.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~DofusDbPartner -v:q -clp:ErrorsOnly
dotnet build GivreClone.App\GivreClone.App.csproj -c Release --no-restore -v:q -clp:ErrorsOnly
```

Les tests utilisent des **données synthétiques explicites** et couvrent admission, schéma, compteurs, recherches bilingues, membres, recettes incomplètes, métiers/monstres/positions, SHA altéré, pages supplémentaires, identités dupliquées, erreurs de pagination, droits source, volumes de requête. Un test de validation du vrai cache s'exécute si `GIVRE_DOFUSDB_LOCAL_TEST_CACHE` pointe vers le dossier réel, ou si le dossier standard relatif au workspace existe. Ce test n'utilise **aucun** appel réseau ni mutation des données source.

**Résultat local vérifié : 18/18 tests ciblés réussis en Release**. Le test d'intégration du cache réel a aussi été exécuté isolément avec le chemin explicite `GIVRE_DOFUSDB_LOCAL_TEST_CACHE` : **1/1 PASS**, avec vérification effective des six collections (47 798 documents), des SHA/pages, des 19 compteurs de résumé totalisant 87 327 et des champs de droits/version. Build `GivreClone.App` Release : **0 erreur** (197 avertissements préexistants).

**Raccordement UI / lancement de GivreWork : réservé au prime** ; aucun ancien reader, view model, projet, bridge ou processus de jeu n'est modifié par ce lot.
