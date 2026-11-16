# ItemCraftMcp — grand catalogue local, UUID et fermeture de recettes

Date : 20 octobre 2026. Implémentation hors-jeu, lecture seule, sans scraping, commandes de bots, ACK serveur ni transactions.

## Problème constaté

Le catalogue local admis possède **4 858 recettes**, au-delà du plafond de **4 000 recettes par calcul** de `ItemCraftPlanner`. Une projection globale des 4 858 dans `ItemCraftContext` échoue à la validation du moteur. Le préfixe arbitraire des 4 000 premières recettes aurait caché des recettes situées en fin de catalogue, notamment pour des objets/panoplies choisis en recherche.

Le catalogue source contient des **clés de recette texte/UUID** ; `ItemCraftPlanner.CraftRecipe.RecipeId` n'accepte que `long`. Les quantités de sortie de certaines recettes sont `null` : inventer `ResultQuantity = 1` fausserait tous les ingrédients, besoins et achats.

Les catalogues de cibles ont également **20 979 items et 929 panoplies** : le précédent plafond MCP de 20 000 cibles aurait laissé des objets invisibles. `ItemCraftMcpSnapshot.MaxTargets` vaut désormais **40 000** (contre 20 000), sans modifier les contrôles de provenance ni le plafond de lignes source `MaxInputRows=40 000`.

## Contrat C# définitif pour le prime

Les changements se limitent à `GivreClone.App/Services/ItemCraftMcp/*`, `GivreClone.Tests/ItemCraftMcpFacadeTests.cs` et cette documentation. Le raccord du service d'accès à la source et du bridge appartient au prime.

```csharp
using GivreClone.App.Services.ItemCraftMcp;
using GivreClone.App.Services.ItemCraftPlanner;

// proof vient exclusivement de l'intégrateur qui a vérifié les deux manifests,
// les deux SHA256 et l'autorisation contractuelle IA/MCP.
ItemCraftMcpEvidence proof = /* proven source-reviewed permission */;

var rows = approvedLocalRecipes.Select(r => new ItemCraftMcpRecipeRow(
    LocalRecipeKey: r.Id,             // UUID/string d'origine, jamais un opcode ou ID officiel inventé
    ResultItemGid: r.ResultItemId,
    ResultQuantity: r.ResultQuantity, // nullable : ne pas remplacer null par 1
    Ingredients: r.Ingredients.Select(i => new CraftIngredient(i.ItemId, i.Quantity)).ToArray(),
    SourceVersion: proof.GameVersion,
    Verified: r.Verified,            // validation réelle de l'intégrateur, pas donnée utilisateur
    JobId: r.JobId,
    RequiredLevel: r.RequiredLevel
)).ToArray();

var catalog = new ItemCraftMcpRecipeCatalog(proof, rows);
var snapshot = new ItemCraftMcpSnapshot(proof, approvedTargets,
    context: null, inventoryVerified: false, jobLevelsVerified: false,
    recipeCatalog: catalog);

string json = ItemCraftMcpFacade.QueryJson(
    "{\"op\":\"plan_item\",\"itemGid\":12345}", snapshot);
```

Le constructeur `ItemCraftMcpRecipeCatalog(ItemCraftMcpEvidence, IReadOnlyCollection<ItemCraftMcpRecipeRow>)` accepte **jusqu'à 50 000 recettes** et **500 000 entrées d'ingrédients**, avec au plus 64 ingrédients/recette et des clés de 1 à 80 caractères ASCII limités à lettres, chiffres, `-`, `_`, `:`, `.`. Il refuse les clés dupliquées, y compris différences de casse, les GID invalides et les quantités hors bornes. Il copie tous les tableaux pour protéger le snapshot contre les mutations ultérieures.

Un `ItemCraftContext` facultatif peut apporter **uniquement** des sources d'acquisition, des stocks observés et des niveaux métiers certifiés ; avec `recipeCatalog`, il doit contenir `Recipes=[]`. Le moteur reconstruit le `ItemCraftContext` par cible, en utilisant les recettes de fermeture et les définitions de panoplies admises depuis `ItemCraftMcpSnapshot`. Si aucun contexte n'est fourni, aucun état sac/banque/métier n'est présumé connu.

## Algorithme de fermeture et limites

1. Construire une table par `ResultItemGid` sur **toutes** les recettes admises. Les clés UUID sont triées `StringComparer.Ordinal` et reçoivent un ID positif temporaire interne, stable quelle que soit la permutation de l'entrée.
2. Pour `plan_item`, partir du GID cible ; pour `plan_set`, partir **de toutes** les pièces documentées de l'ensemble. Explorer les ingrédients récursivement avec un ensemble des GID déjà visités pour arrêter les cycles.
3. Inclure **toutes les recettes candidates** pour chaque GID atteint (sans sélectionner silencieusement une recette ambiguë). Les branches inconnues restent UNKNOWN selon la logique du moteur.
4. En cas de plus de **4 000 recettes atteignables**, **8 192 GID explorés** ou **50 000 inscriptions en file**, renvoyer `UNKNOWN`, sans calculer sur une fermeture tronquée. Le graphe moteur reste soumis à sa profondeur maximale de 32 et aux caps de nœuds ; la projection MCP conserve `MaxPlanRows=2048`.
5. Exécuter le moteur canonique `ItemCraftPlanner.PlanItem` / `PlanSet`, qui partage une seule réserve de stock et le surplus des sous-crafts entre pièces. Aucune quantité de sortie manquante n'est inférée.

Les IDs numériques locaux utilisés **exclusivement en mémoire** pour adapter les UUID à `CraftRecipe.RecipeId` ne sont jamais présentés comme des IDs officiels. Pour les étapes provenant d'un catalogue UUID, la sortie JSON expose `localRecipeKey` et `recipeId: null`. Pour les anciens contextes possédant de véritables IDs numériques, la sortie `recipeId` existante reste inchangée.

L'état `KNOWN` pour un plan entier reste conditionné aux preuves de recettes, ingrédients, atelier, métiers, acquisition et stock ; sinon `UNKNOWN` avec des étapes/quantités partielles lorsque la source est divulguable. Une entrée avec `ResultQuantity=null` conserve cette incertitude : les matériaux éventuellement affichés par le moteur n'acquièrent pas de quantité fictive.

Le moteur métier accepte normalement une paire `JobId=null`, `RequiredLevel=null` comme recette sans spécialité ; lors d'une extraction locale, ces deux champs peuvent simplement être **non renseignés**. L'adaptateur MCP rétrograde donc toute recette de fermeture sans ces preuves au statut **UNKNOWN** tout en conservant les ingrédients attestés en lecture. Il évite ainsi de valider un craft sans niveau métier vérifié. Les clés locales dont l'étiquette révèle une origine `dofusdb` sont aussi rejetées.

## Droits IA, protection et intégration

La validation source appartient à `ItemCraftAccessService.ApprovedMcpSnapshot()` (prime). Un manifeste local avec SHA n'est pas une autorisation IA à lui seul. Le prime vérifie séparément les **SHA des catalogues sets et recipes**, versions égales, source `local-extraction` et autorisation IA/MCP explicite avant de fabriquer `proof`.

Le nouveau catalogue exige `proof.CanDisclose()`, exactement la même version/SourceKind/SourceId/SHA/AuthorizationEvidenceId entre le snapshot et le sous-catalogue, et des `SourceVersion` homogènes pour toutes les recettes. Sources publiques DofusDB et réexports non licenciés : **refusés**. Le catalogue ne lit aucun disque, n'accède pas aux partenaires ni à l'inventaire. Sans les attestations exigées, `list_targets`, `plan_item`, `plan_set`, `materials` et `steps` retournent `UNKNOWN` sans exposer les clés privées, items ou chiffres.

**Aucun changement** dans `GivreMcp`, `LocalControlBridge`, les csproj ou `ItemCraftAccessService` par ce worker. Le prime remplace son ancienne branche `convertible.Length <= 4000` par le constructeur de catalogue indexé, et prend soin de ne pas exclure les UUID ou quantités de sortie inconnues. Il peut conserver `QueryJson` et les six opérations JSON existantes sans modification de signature.

## Validation hors jeu

Tests intégrés à `GivreClone.Tests/ItemCraftMcpFacadeTests.cs` avec **données explicitement synthétiques** couvrant notamment 4 858 recettes dont la dernière, UUID et mapping stable, panoplie avec fermeture multi-cibles, rendement absent, ingrédient sans quantité, cycles, absence de droits, DofusDB, versions discordantes, ambiguïté, dépassement de fermeture et de volume, doublons de clés, immutabilité, ainsi que 20 979 items + 929 ensembles et le plafond 40 000.

```powershell
dotnet test GivreClone.Tests\GivreClone.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ItemCraftMcp|FullyQualifiedName~ItemCraftPlanner' -v:q -clp:ErrorsOnly
dotnet build GivreClone.App\GivreClone.App.csproj -c Release --no-restore -v:q -clp:ErrorsOnly
```

Ces validations sont purement offline. Il n'y a ni ordre HDV, ni trajet de farm live, ni paquet Dofus, ni connexion/ACK serveur.
