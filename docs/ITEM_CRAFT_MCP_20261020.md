# ItemCraftMcp — façade IA pour un objet ou une panoplie, 20 octobre 2026

## Livrables

- `GivreClone.App/Services/ItemCraftMcp/ItemCraftMcpEvidence.cs` : attestation descriptive de provenance, SHA-256 de manifeste, version Dofus exacte, droits IA/MCP explicites.
- `ItemCraftMcpSnapshot.cs` : catalogue d'objets et ensembles source-approved, stockage copié défensivement, option contexte du moteur pur `ItemCraftPlanner` et preuve séparée de stock/métier.
- `ItemCraftMcpEngineAdapter.cs` : raccord direct au moteur `GivreClone.App.Services.ItemCraftPlanner.ItemCraftPlanner.PlanItem / PlanSet`. Les matériaux et étapes sont dérivés du **même calcul récursif partagé entre toutes les pièces** ; aucun taux de drop, recette ni position de farm n'est créé par la façade.
- `ItemCraftMcpPlanResult.cs` : projection autonome de résultats pré-calculés si le prime préfère alimenter le MCP avec des plans certifiés/figés.
- `ItemCraftMcpFacade.cs` : six opérations JSON strictes, uniquement consultatives.
- `GivreClone.Tests/ItemCraftMcpFacadeTests.cs` : tests synthétiques hors jeu, y compris droits, cycles, quantités manquantes et stock de plusieurs pièces.

## API C#

```csharp
using GivreClone.App.Services.ItemCraftMcp;
using GivreClone.App.Services.ItemCraftPlanner;

var provenance = new ItemCraftMcpEvidence(
    SourceKind: "local-owned", SourceId: "reviewed-catalog",
    GameVersion: "3.7.10.10", ManifestSha256: verifiedSha256,
    AiDisclosureAuthorized: true,
    AuthorizationEvidenceId: approvedEvidenceId);

var approvedTargets = new ItemCraftMcpTarget[]
{
    // Charger les vrais identifiants à partir du catalogue local versionné.
    // ItemCraftMcpTarget(ItemCraftMcpTargetKind.Item, itemGid, name, [], provenance, verified)
    // ItemCraftMcpTarget(ItemCraftMcpTargetKind.Set, setId, name, memberGids, provenance, verified)
};

ItemCraftContext approvedContext = /* contexte local validé par l'intégrateur */;
var snapshot = new ItemCraftMcpSnapshot(provenance, approvedTargets,
    context: approvedContext,
    inventoryVerified: false, jobLevelsVerified: false);

string responseJson = ItemCraftMcpFacade.QueryJson(requestJson, snapshot);
```

`inventoryVerified` reste faux tant que les stocks sac/banque/havre-sac ne sont pas certifiés par l'état observé du personnage. Lorsque faux, le moteur reçoit **une liste de stocks vide** et la réponse laisse `owned` et `missing` à `null`. `jobLevelsVerified` reste faux tant que les niveaux de métier ne sont pas vérifiés : le moteur reçoit un dictionnaire vide. Les valeurs en cours de chargement ou stale ne justifient aucun `true`.

Les paramètres `plans` permettent de fournir une collection facultative d'`ItemCraftMcpPlanResult` déjà issus du moteur et vérifiés, indexés par `(kind, targetId, quantity)`. Si un plan pré-calculé correspond exactement, la façade le lit. Sinon, si `context` est disponible, elle calcule à la demande via le moteur purement local. Sans l'un ni l'autre, `UNKNOWN` ; aucun calcul de remplacement inventé.

## Schéma JSON v1

Maximum 4096 octets UTF-8 par requête. Corps objet strict ; doublons, clés non documentées, sous-objets, nombres flottants, commentaires, virgules terminales et commandes de gameplay refusés.

| Opération | Champs permis | Résultat |
|---|---|---|
| `capabilities` | `op` | Schéma, bornes, six opérations, read-only |
| `list_targets` | `kind` optionnel `item/set`, `query` optionnel, `offset`, `limit` | Articles/panoplies du catalogue admis et pièces documentées |
| `plan_item` | `itemGid`, `quantity` optionnel (défaut 1) | Résumé du calcul d'un article |
| `plan_set` | `setId`, `quantity` optionnel (défaut 1) | Résumé du calcul de toutes les pièces |
| `materials` | `kind`, `itemGid` ou `setId`, `quantity`, `offset`, `limit` | Matériaux feuilles du graphe, quantités et méthodes |
| `steps` | mêmes champs que `materials` | Noeuds du graphe de dépendances, source et statut |

```json
{"op":"capabilities"}
{"op":"list_targets","kind":"set","query":"Bouftou","limit":20}
{"op":"plan_item","itemGid":101,"quantity":1}
{"op":"plan_set","setId":999,"quantity":1}
{"op":"materials","kind":"set","setId":999,"quantity":1,"limit":20}
{"op":"steps","kind":"item","itemGid":101,"quantity":1,"offset":0,"limit":20}
```

Les `101` et `999` sont de **simples identifiants d'exemple** et ne désignent aucun équipement Dofus validé. Le test « ensemble fictif » ne doit jamais être annoncé comme la vraie Panoplie Bouftou. Une recherche `Bouftou` sans catalogue sourcé retourne `UNKNOWN`, sans pièces ni recettes imaginées.

La façade impose `limit` de 1 à 32 (défaut 20), `offset` 0..2048 ; réponse maximum 65 536 octets, sinon retour neutre `UNKNOWN`. Snapshot : 20 000 cibles, 32 pièces enregistrées par ensemble, 512 plans pré-calculés, 2048 matériaux/noeuds dans une projection. Le moteur gère les calculs multi-niveaux, les réserves communes entre cibles, les sous-crafts à rendement multiple et les cycles. L'adaptateur refuse les graphes dépassant la taille de projection.

Les résultats expriment `status=KNOWN` seulement si cibles, pièces, recettes, atelier et niveaux nécessaires, stock observé et acquisitions sont prouvés. Un plan partiel présente `status=UNKNOWN`, `partial=true`; les champs de quantité non prouvée valent `null`. Le calcul ne remplace jamais les quantités inconnues par zéro. `materials` totalise uniquement les étapes feuilles de stock, farm/drop/achat proposés et les branches irrésolues ; les sous-crafts intermédiaires restent dans `steps` pour éviter un double comptage. Une méthode BUY est une **option consultative**, sans transaction HDV.

Toutes les réponses déclarent `readOnly=true`, `executed=false`, `acknowledged=false`, `clientContacted=false`, `runtimeCertified=false`. Le retour de route reste `routeStatus=UNKNOWN` et `routeLinks=[]` tant qu'aucune route inter-cartes de farm n'est réellement prouvée par un autre module.

## Provenance et droits de données

Le manifeste porte `SourceKind`, `SourceId`, `GameVersion`, `ManifestSha256` (64 hex), `AiDisclosureAuthorized` et `AuthorizationEvidenceId`. Ces champs sont une **attestation déclarative**, pas un système de vérification contractuelle ou cryptographique. Le prime doit vérifier le SHA du fichier local et les droits d'usage IA de toutes les sources en amont, avant d'accorder `AiDisclosureAuthorized=true`. Versions des recettes, acquisitions et ensembles doivent toutes correspondre exactement à la version du manifeste. Les cibles sont également contrôlées par origine/manifest.

Les données publiques DofusDB (et variantes `Dofus-DB`, `dofus.db`, etc.) sont interdites en sortie MCP même avec un drapeau d'autorisation générique. Le partenariat allégué pour le scraping du site ne suffit pas à établir le droit d'ingestion/redistribution dans une IA sous LPNC-IA 1.0. Une source partenaire réexportée sous un autre nom reste interdite ; sa provenance réelle doit être vérifiée à l'admission. Les snapshots sans source admise ou ceux contenant une entrée de version/droits discordants ne divulguent aucun nom, GID, ensemble, recette ou source.

## Intégration réservée au prime

Le prime possède `GivreMcp/GivreTools.cs`, `LocalControlBridge`, les fichiers de navigation et les csproj. Pour rendre la fonction visible, ajouter explicitement un outil MCP de **lecture seule**, par exemple :

```csharp
[McpServerTool(Name = "query_item_craft", ReadOnly = true)]
public Task<JsonElement> QueryItemCraft(string requestJson, CancellationToken ct) =>
    Call("query_item_craft", new { requestJson }, ct);
```

Le bridge du prime conserve `McpEnabled`, authentifie la requête locale, charge le snapshot approuvé et appelle `ItemCraftMcpFacade.QueryJson`. L'endpoint ne doit pas exposer `run_lua_script`, `buy`, `craft`, aucun secret ni commande générique. La valeur retournée est une simulation métier calculée hors-jeu, sans preuve d'ACK serveur.

Tests :

```powershell
dotnet test GivreClone.Tests\GivreClone.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~ItemCraftMcp -v:q -clp:ErrorsOnly
```

**47 tests ItemCraftMcp PASS** et **68/68 tests ItemCraftMcp + ItemCraftPlanner PASS en Release** au 20 octobre 2026, construits uniquement sur des recettes et panoplies **fictives**. Aucun réseau, bot, sauvegarde d'inventaire, transaction, vrai game client ou API DofusDB.
