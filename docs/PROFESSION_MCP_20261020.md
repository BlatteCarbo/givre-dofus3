# ProfessionMcp — contrats de lecture métiers, 20 octobre 2026

## Livrables

- `GivreClone.App/Services/ProfessionMcp/ProfessionMcpSnapshot.cs` : admission d'un catalogue métier local, versionné et autorisé pour exposition à une IA ; copie défensive des recettes, listes de stock et sources.
- `GivreClone.App/Services/ProfessionMcp/ProfessionMcpFacade.cs` : requêtes JSON strictes et bornées, conversion depuis **le vrai `ProfessionPlannerEngine.Plan`** vers des réponses MCP consultatives.
- `GivreClone.Tests/ProfessionMcpFacadeTests.cs` : tests offline du parseur, XP, matériaux, récursion, pagination, provenance et droits.

Contrats publics dans `GivreClone.App.Services.ProfessionMcp` :

```csharp
new ProfessionMcpProvenance(
    SourceKind, SourceId, GameVersion, ManifestSha256,
    AiDisclosureAuthorized: false, AuthorizationEvidenceId: null);
new ProfessionMcpSnapshot(ProfessionPlanRequest request, ProfessionMcpProvenance provenance);
ProfessionMcpFacade.QueryJson(string? requestJson, ProfessionMcpSnapshot? snapshot);
```

`ProfessionPlanRequest`, `ProfessionGoal`, `ProfessionPlan`, `ProfessionRecipe`, `MaterialSource`, `XpTable`, `ProfessionInventoryStack` et `ProfessionPlannerEngine.Plan` viennent de `GivreClone.App.Services.ProfessionPlanner` (worker-2). Le moteur conserve sa réserve d'inventaire commune à tout le graphe et rend les cycles/XP manquants par des étapes ou champs indéterminés. La façade n'ajoute **ni recette, ni prix, ni taux de drop, ni gain XP**.

## Exemples d'API

```json
{"op":"capabilities"}
{"op":"plan_job","jobId":28,"startLevel":20,"targetLevel":30}
{"op":"materials","jobId":28,"startLevel":20,"targetLevel":30,"offset":0,"limit":20}
{"op":"steps","jobId":28,"startLevel":20,"targetLevel":30,"offset":0,"limit":20}
```

`jobName` (1..64 lettres/chiffres/espaces/tirets/apostrophes/points) est une alternative à `jobId`, **pas un paramètre supplémentaire**. `trainingRecipeId` et `requestedCrafts` peuvent être passés pour demander une correspondance exacte avec le snapshot déjà validé par l'application. Les données propres au personnage (XP et inventaire vérifiés) sont exclusivement celles du `ProfessionPlanRequest` approuvé : un appelant MCP ne peut pas fabriquer un champ `currentXpVerified`. Le prime prépare le `ProfessionPlanRequest` à partir de la cible métier et des données admises, puis fournit le snapshot correspondant.

Réponses : `status=KNOWN` uniquement pour un plan complet ; `status=UNKNOWN`, `partial=true` pour un plan incomplet ou une donnée non prouvée. `plan_job` retourne besoin XP, crafts estimés s'ils sont vérifiables, nombre d'étapes et de ressources, totaux connus et devis complets seulement si attestés. `materials` regroupe les quantités par GID et distingue stock utilisé / reste à acquérir. `steps` expose les dépendances, les méthodes d'acquisition consultatives, coûts éventuellement nuls, et alternatives connues. L'absence de valeur n'est jamais transformée en zéro.

Les sorties contiennent `readOnly=true`, `executed=false`, `acknowledged=false`, `clientContacted=false`. Tout trajet et tout lien de navigation restent `routeStatus=UNKNOWN` et `routeLinks=[]` : ce moteur ne possède aucune preuve de mouvement entre cartes. Aucune action de jeu, Lua, HDV ou réseau n'est appelée.

## Bornes et refus

Entrée max. **4096 octets UTF-8**, JSON strict sans commentaires, virgules terminales, champs dupliqués ou inconnus, profondeur max. 5. `limit` 1..40 (20 par défaut), `offset` 0..2048 ; réponse max. **65 536 octets UTF-8**, sinon `UNKNOWN` sans JSON partiel. Jusqu'à 2 000 recettes, 10 000 piles de stock, 10 000 sources d'acquisition, 64 ingrédients par recette et 201 seuils XP ; le moteur borne lui-même la profondeur à 32 et le graphe à 2 048 étapes. Un refus ne retourne jamais le nom, l'empreinte ou le contenu du catalogue non admis.

Les fautes de schéma retournent `INVALID_REQUEST`. Les droits inconnus, versions discordantes, données indisponibles, cycles, XP/recettes manquants ou dépassements de calcul retournent `UNKNOWN` ; un calcul partiel peut être détaillé lorsque la source a été autorisée.

## Provenance et droits IA

`ProfessionMcpProvenance` exige une version exacte à quatre composantes, un SHA-256 de **64 hexadécimaux** sur un manifeste dont l'intégrateur a vérifié les bytes, un SourceKind/SourceId bornés, `AiDisclosureAuthorized=true` et un `AuthorizationEvidenceId` non vide attribué après revue juridique. **Les champs ne constituent pas à eux seuls une preuve juridique**, et doivent être fournis par un service de confiance après admission indépendante, jamais directement depuis une requête MCP ni une source tierce.

Toutes les `SourceVersion` des recettes et sources d'acquisition, ainsi que la version de table XP si elle existe, doivent correspondre exactement à `GameVersion`. Une seule valeur discordante bloque toute la divulgation : cela empêche qu'une recette de version ou d'origine incertaine serve à calculer un faux itinéraire de craft.

Les sources dont `SourceKind` ou `SourceId` se normalisent avec « dofusdb » sont expressément bloquées, y compris avec `AiDisclosureAuthorized=true`. Le partenariat de scraping déclaré n'est pas un droit démontré d'ingestion et redistribution **IA/MCP** sous LPNC-IA 1.0. Le prime doit confirmer la licence spécifique et la chaîne de provenance avant toute modification de cette restriction. Interdire aussi les **sources composites** et les réexportations DofusDB renommées dans le processus d'admission externe : la façade ne peut pas détecter une origine qui a été délibérément effacée de la provenance.

## Raccord du prime

1. Charger localement le catalogue propre à GivreWork, contrôler hash du manifeste, version de jeu, origines de **toutes** les recettes/sources/seuils et autorisation IA/MCP, puis former `ProfessionPlanRequest` en conservant les champs `Verified` et les quantités réelles.
2. Créer `ProfessionMcpSnapshot` avec sa provenance validée. Choisir le goal dans l'application en amont ; la façade refuse une requête MCP dont métier/niveaux/crafts contredisent le snapshot. Si le snapshot de ce goal n'existe pas, retourner `UNKNOWN`.
3. Dans le bridge `LocalControlBridge`, ajouter un chemin explicite en lecture seule, par exemple `query_profession_plan(requestJson)` → `ProfessionMcpFacade.QueryJson`, en conservant `McpEnabled` et les contrôles locaux d'autorisation.
4. Dans `GivreMcp/GivreTools.cs`, ajouter un tool `[McpServerTool(Name = "query_profession_plan", ReadOnly = true)]` qui appelle `ControlPipeClient.CallAsync("query_profession_plan", new { requestJson }, ct)`. Le prime possède ces fichiers ; le présent lot ne les modifie pas.
5. Ne pas rendre accessibles les commandes de `run_lua_script`, `save_script`, `buy`, `craft` ou les secrets à cette API. La simulation n'est ni une action HDV ni une validation serveur.

Le csproj de tests doit compiler les sources pures via deux liens `ProfessionPlanner/*.cs` et `ProfessionMcp/*.cs` (intégration réservée au prime). Commande de validation ciblée :

```powershell
dotnet test GivreClone.Tests\GivreClone.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~ProfessionMcp -v:q -clp:ErrorsOnly
```

Tests entièrement synthétiques et hors-jeu ; aucune navigation, import DofusDB, connexion réseau ni action sur le client.
