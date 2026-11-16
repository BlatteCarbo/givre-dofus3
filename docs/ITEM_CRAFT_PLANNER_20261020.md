# Planificateur universel d'objets et de panoplies — 20 octobre 2026

## Périmètre et règles de preuve

**Bijoutier n'est qu'un exemple** : ce moteur est indépendant du métier et accepte un seul objet, une liste d'objets ou une panoplie entière. Le code est purement consultatif : aucune action de jeu, achat HDV, drop garanti, navigation, craft exécuté, requête réseau ou ACK serveur. Les recettes, objets, prix, taux de drop et appartenances à une panoplie doivent provenir d'un **catalogue externe versionné et explicitement vérifié**.

**L'exemple « Panoplie Bouftou » des tests est entièrement FICTIF**. Les identifiants 100/101/102/500, les coûts, les quantités, les composants (« laine », « fil »), les niveaux de métier, et l'ID de set 999 appartiennent à la fixture `FAKE-UNIT-TEST-NOT-DOFUS`. Rien n'est présenté comme une recette Dofus 3.7.10.10 ni comme un set véritable.

## Fichiers et API pour prime / UI / MCP

- `GivreClone.App/Services/ItemCraftPlanner/ItemCraftPlanner.cs`
- `GivreClone.Tests/ItemCraftPlannerEngineTests.cs`
- Documentation présente.

Espace de noms : `GivreClone.App.Services.ItemCraftPlanner`.

```csharp
using GivreClone.App.Services.ItemCraftPlanner;

// Les ressources entrantes ci-dessous doivent être réellement fournies/vérifiées par un importeur,
// ou rester absentes. PAS de valeurs Dofus prétendument connues.
var ctx = new ItemCraftContext(
    Recipes: Array.Empty<CraftRecipe>(),
    Inventory: Array.Empty<ItemStockStack>(),
    Sources: Array.Empty<AcquisitionSource>(),
    Sets: Array.Empty<EquipmentSetDefinition>(),
    KnownJobLevels: new Dictionary<int,int>());

var single = ItemCraftPlanner.PlanItem(new ItemCraftTarget(123, 2), ctx);
var list = ItemCraftPlanner.PlanList(new[]
{
    new ItemCraftTarget(123, 2),
    new ItemCraftTarget(456, 1),
    new ItemCraftTarget(123, 3)
}, ctx);
var set = ItemCraftPlanner.PlanSet(new EquipmentSetTarget(
    SetId: 999, Name: "Set renseigné par l'utilisateur",
    ItemIds: new long[] { 123, 456 }, Quantity: 1), ctx);

// single/list/set sont CONSULTATIFS ; Executed=false ; Acknowledged=false.
// set.SetMembershipVerified=false tant qu'aucun catalogue fiable ne prouve les membres.
```

### Entrées

| Type | Signification |
| --- | --- |
| `ItemCraftTarget(ItemId, Quantity)` | Objet + quantité positive, jusqu'à un milliard |
| `EquipmentSetTarget(SetId, Name, ItemIds?, Quantity)` | Panoplie en N exemplaires ; membres explicites facultatifs |
| `EquipmentSetDefinition(SetId, Name, ItemIds, SourceVersion, Verified)` | Catalogue de panoplies vérifié séparément ; si inconnu, les membres sont `UNVERIFIED` |
| `CraftRecipe(RecipeId, ResultItemId, ResultQuantity?, Ingredients, SourceVersion, Verified, JobId?, RequiredLevel?, KamasPerCraft?, SecondsPerCraft?, RequiresWorkshop, WorkshopVerified)` | Recette versionnée, rendement et ingrédients éventuellement inconnus, métier/atelier séparément justifiés |
| `CraftIngredient(ItemId, Quantity?)` | `null` = nombre d'unités inconnu, pas de quantité fabriquée |
| `ItemStockStack(StackId, ItemId, Quantity, Container, Verified)` | Pile physique identifiée de sac/banque ; non vérifiée = non consommable |
| `AcquisitionSource(ItemId, Kind, SourceId, SourceVersion, Verified, UnitKamas?, UnitSeconds?, MaxUnits?)` | Source de récolte/drop/achat ; aucune probabilité supposée |
| `RecipeChoices` / `MethodChoices` | Choix explicites par item, sans inventer de solution « optimale » |
| `CraftOptimizationWeights(Kamas,Time)` | Comparaison d'alternatives feuilles seulement si **tous** prix, durées, capacités et sources sont comparables |

### Sortie `ItemCraftPlan`

`Targets` est une liste déterministe d'objets uniques, avec quantités fusionnées ; `TargetSummaries` expose les étapes de chaque produit. `Steps` est un DAG **topologiquement ordonné** : une recette `CraftAtWorkshop` référence dans `Dependencies` ses sous-étapes d'acquisition ; un `UsePlannedSurplus` pointe vers le craft qui produirait le surplus. Il n'y a **pas de double consommation du stock** entre pièces d'une même panoplie.

`ResourceNeeds` est une vue **agrégée unique par ItemId/méthode** pour les quantités à obtenir en récolte, drop ou achat. Contrairement aux étapes détaillées, elle ne répète pas les besoins entre les pièces. `UsedStock` et `RemainingStock` concernent le stock initial vérifié ; `PlannedSurplus` est un stock **virtuel projeté**, non crédité comme inventaire réel. `Missing[itemId] = null` signifie que la quantité manquante est elle-même inconnue.

`KnownKamasSubtotal` et `KnownSecondsSubtotal` somment uniquement les valeurs connues. `FullKamasEstimate?` et `FullSecondsEstimate?` valent `null` s'il manque la moindre preuve/valeur, s'il y a des quantités inconnues, cycles ou limites d'accès. `Warnings`, `IsComplete`, `SetMembershipVerified`, `RequiresReview` permettent à l'UI de distinguer ce qui doit être étudié. **`Executed=false`, `Acknowledged=false` partout.**

## Décisions défensives

1. Si aucun catalogue vérifié ne contient la panoplie et qu'aucun `ItemIds` n'est fourni, renvoyer zéro pièce et une alerte ; si des membres sont saisis manuellement, les planifier **sans valider l'appartenance à la vraie panoplie**.
2. Un seul registre de stock pour **tous** les objectifs. Plusieurs piles/containers d'un GID ne peuvent pas être consommés plusieurs fois. Les sous-crafts multi-rendement ajoutent seulement leur surplus *vérifiable par le plan* à un registre de surplus projeté, réutilisé dans une autre branche avec la dépendance d'origine.
3. Les cycles directs/indirects, quantités recette absentes, rendements inconnus, recettes alternatives ambiguës, niveaux de métier non attestés, ateliers non confirmés et sources sans preuve produisent un statut incomplet. Les ingrédients prouvables d'un craft bloqué restent inspectables, mais son produit ou son surplus n'est pas validé.
4. Le coût/temps inconnu restent `null`; un prix affiché ne suffit jamais à provoquer un achat. Les drops et collectes sont des **méthodes possibles**, jamais un taux, lieu ou succès de récolte prouvé.
5. Plusieurs sources concurrentes ne sont comparées automatiquement que si toutes possèdent une version, `Verified=true`, et des prix, durées et capacités comparables. Sinon elles restent des `ItemCraftAlternative` pour sélection manuelle.
6. Bornes : 128 objectifs, 128 pièces par set, 4 000 recettes, 20 000 piles, 10 000 sources, 4 096 étapes, profondeur récursive 32, quantités max 1 milliard ; calculs en `checked`.

## Tests offline

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --filter FullyQualifiedName~ItemCraftPlannerEngineTests
dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore
```

21 tests ciblés à couvrir : objet et chaîne multi-niveaux, ensemble complet fictif et quantité ×2, liste fusionnée, consommation banque/sac une seule fois, rendements et ingrédients inconnus, recette/source absentes, cycles, métiers/ateliers, surplus et dépendance, set manuel non vérifié, choix multiples, comparaison avec/sans prix, sources et piles non prouvées, capacité globale d'une source, dépassement de limites, déterminisme du DAG.

**Aucune modification de l'ancien ProfessionPlanner, des anciennes interfaces No Code, d'autres workers, des projets partagés ni des fichiers jeu. Aucun commit ni push.**
