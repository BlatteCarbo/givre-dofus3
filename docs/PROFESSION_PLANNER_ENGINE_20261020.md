# Moteur métiers / BOM récursif — 20 octobre 2026

## Statut et garanties

Implémenté **sans action en jeu ni achat** dans `Services/ProfessionPlanner/ProfessionPlannerEngine.cs`. Aucun prix, drop, map, taux de récolte, recette ou XP du jeu n'est présumé. Toutes les valeurs de la démonstration Bijoutier 20→30 (plumes de Piou, huile de sésame, etc.) sont **fictives / synthétiques** : ce n'est pas une recette Dofus vérifiée.

### API publique pour UI (worker11) et MCP (worker10)

```csharp
using GivreClone.App.Services.ProfessionPlanner;

var goal = new ProfessionGoal(
    JobId: 7, JobName: "Bijoutier (exemple fictif)",
    StartLevel: 20, TargetLevel: 30,
    CurrentXp: null, CurrentXpVerified: false);

var request = new ProfessionPlanRequest(
    Goal: goal,
    TrainingRecipeId: null, RequestedCrafts: null,
    Recipes: Array.Empty<ProfessionRecipe>(),
    Inventory: Array.Empty<ProfessionInventoryStack>(),
    Sources: Array.Empty<MaterialSource>(),
    XpTable: null);
// Sans XP, pas de nombre de crafts présumé.
ProfessionPlan plan = ProfessionPlannerEngine.Plan(request);
// Une demande manuelle indépendante des XP :
ProfessionPlan item = ProfessionPlannerEngine.PlanItem(request, itemGid: 100, quantity: 20);
// Suggestions uniquement si TOUTES les recettes candidates sont vérifiées/comparables :
ProfessionTrainingRecommendation suggestion =
    ProfessionPlannerEngine.SuggestTrainingRecipes(request);
```

- `ProfessionGoal(JobId?, JobName?, StartLevel, TargetLevel, CurrentXp?, CurrentXpVerified)` ; si l'XP n'est pas prouvé, le nombre de crafts requis vaut **null**.
- `XpTable(Version, Verified, Thresholds)` : valeurs d'XP **cumulées** par niveau, fournies et versionnées par l'intégrateur ; pas de table codée en dur.
- `ProfessionRecipe(RecipeId, ResultGid, ResultQty, Ingredients, RequiredLevel, JobId?, XpPerCraft?, XpVerified, SourceVersion, Verified, SecondsPerCraft?, KamasPerCraft?)` : recette observée + éventuels XP/coûts/temps prouvés.
- `ProfessionInventoryStack(StackId, ItemGid, Quantity, Container)` : un StackId unique par pile physique ; stocks de sac/banque agrégés une seule fois ; sous-craft en surplus réinjecté dans la réserve commune.
- `MaterialSource(ItemGid, AcquisitionMethod.{Gather|Drop|Buy}, SecondsPerUnit?, KamasPerUnit?, SourceVersion, Verified, MaxUnits?)`. Pour une source `Craft`, fournir une `ProfessionRecipe`. Les prix et durées absents restent **null** ; aucune extrapolation de drop-rate.
- `ProfessionPlanRequest` accepte `RecipeChoices` (GID→RecipeId), `MethodChoices` (GID→méthode) et `KnownJobLevels` (ID métier secondaire→niveau vérifié), plus `ProfessionPreferences(KamasWeight,TimeWeight)`.
- `ProfessionPlan.Steps` liste des `ProfessionPlanStep(Id, Kind, ItemGid, Quantity, RecipeId, Detail, Dependencies, Verified, Kamas?, Seconds?, Alternatives)` ordonnés en **postordre topologique** ; une étape `Craft` dépend des étapes nécessaires à ses ingrédients.
- `ProfessionPlan.Progress` donne `TargetXp`, `NeededXp`, `PlannedCrafts`, `PotentialXpEarned`, `PotentialFinalXp`, `TargetReachableFromKnownXp` ; `UsedStock`, `RemainingStock`, `Warnings`, `KnownKamasSubtotal`, `FullKamasEstimate?`, `KnownSecondsSubtotal`, `FullSecondsEstimate?`.
- `ProfessionTrainingRecommendation.Candidates` expose les options d'entraînement. `SuggestedRecipeId` est **null** dès qu'une option n'est pas comparable en XP, kamas et durée. L'optimisation est fondée seulement sur des coûts et durées vérifiés, avec pondérations explicites. Aucun choix arbitraire par défaut.

## Règles d'admission et simulation

1. Une recette/source ne devient utilisable dans un plan que si `Verified=true` **et** `SourceVersion` est renseigné ; aucune source absente n'est remplacée par une valeur implicite.
2. Une recette du métier cible est accessible au niveau `StartLevel`, sinon elle devient `Unresolved`. Une recette d'un **autre métier** n'est accessible que si son niveau figure dans `KnownJobLevels`. Aucune compétence extérieure n'est présumée.
3. `RequestedCrafts` permet une explosion manuelle des matières premières **sans inventer l'XP** ; si la table `XpTable` et l'XP du personnage ne sont pas confirmés, le niveau final n'est jamais annoncé.
   La version de la table XP doit également être identique à `ProfessionRecipe.SourceVersion` pour utiliser son XP/craft dans un calcul de progression.
4. Le stock est réservé une seule fois à travers **toutes** les branches récursives. Plusieurs piles pour un même GID sont agrégées. Les surplus de recette (`ResultQty > demand`) deviennent disponibles pour les branches suivantes.
5. Si plusieurs méthodes ou recettes existent, le moteur ne choisit automatiquement qu'entre méthodes feuilles toutes documentées avec **prix et temps comparables**. Sinon il expose les `AcquisitionOption` et renvoie `Unresolved` (choix humain ou MCP explicite). Un `MethodChoice` explicite peut conserver un coût inconnu.
6. Les cycles, recettes absentes, limites de stock/source, niveaux verrouillés et options incomparables deviennent des nœuds `Unresolved` avec raison. Les totaux exacts restent `null` si une branche, un prix ou une durée manque.
7. Validations bornées : maximum 2 000 recettes, 10 000 piles, 10 000 sources, 64 ingrédients par recette, 2 048 étapes, profondeur 32, quantités contrôlées et multiplications arithmétiques `checked` ; données invalides rejetées plutôt que silencieusement saturées.
8. **Invariants absolus** : `Executed == false`, `Acknowledged == false`. Le graphe est consultatif ; aucune trace ne constitue un ACK d'achat, de craft, de récolte ni une validation serveur.

## Tests hors ligne

```powershell
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --filter FullyQualifiedName~ProfessionPlannerEngineTests
```

Tests xUnit dans `GivreClone.Tests/ProfessionPlannerEngineTests.cs` : scénario synthétique Bijoutier 20→30, consommation de piles sac/banque, ingrédients répétés, XP inconnue, XP non vérifiée, crafts manuels, aucune recette, cycles, sous-métiers, comparaisons coûts/temps, coûts inconnus, surplus de stacks, limite d'offre, provenance, overflow, déterminisme, recommandations strictes. Aucun client Dofus, requête externe, prix réel, modification d'état jeu ou envoi HDV.

**Validation observée : 21/21 tests ciblés PASS (Release)**. La vérification de compilation générale relève de l'intégration prime/worker11 pour les fichiers UI qu'ils créent en parallèle.

**Intégration** : l'UI et le MCP doivent transformer les catalogues externes *versionnés* en objets d'entrée sans les considérer vérifiés par défaut ; l'UI doit marquer `Unresolved` et `null` explicitement. Le moteur n'implémente ni scraping, ni table d'XP intégrée, ni collecte d'inventaire, ni transfert banque, ni exécution d'un plan. Ne pas afficher « 20→30 en N crafts » sans XP et recette confirmés.
