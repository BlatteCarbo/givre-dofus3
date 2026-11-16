# Audit P1 — preuves de stock live (20 octobre 2026)

## Portée

Code de production lu en lecture seule : `LaunchService.Packets.cs`, `LaunchService.Bank.cs`, `LaunchService.GameLink.cs`, `LaunchService.Character.cs`, `LaunchService.LotJ.cs`, `LaunchService.Interaction.cs`, `LaunchService.cs`, `InventoryStore.cs`, `GivreClone.Protocol/InventoryReader.cs`, `UnifiedStock/UnifiedStockPlanner.cs`, `ItemCraftAccessService.cs`. **Seul `UnifiedStock/LiveStockEvidence.cs` a été corrigé**. Aucun client Dofus, socket, opération coffre/banque, scrape, compte réel ni commit/push.

## Constats prouvés, critiques

1. **Publication anticipée du snapshot.** `LaunchService.Packets.cs:184` déclenche `PacketDecoded?.Invoke(p)` **avant** `Inventory.Seed` (ligne 380) et `ApplyStorageContent` (ligne 453). L'ancienne version de `LiveStockEvidence.OnPacket` posait immédiatement un horodatage `Fresh/CompleteSnapshot=true`, alors que les stores contenaient encore les valeurs antérieures. Une lecture UI concurrente pouvait observer une banque ou un sac périmé avec une preuve fraîche.
2. **Coffre de maison ≠ banque.** `LaunchService.Packets.cs:447-450` ignore les paquets `Storage*` du coffre à code (`_chestSession`) pour préserver le store Banque. L'ancien observateur enregistrait pourtant le `StorageInventoryContentEvent` comme snapshot bancaire. Le nouveau code refuse cette promotion et vérifie aussi `InHavenBag`.
3. **UID et session.** Les stores sont par PID, indexés par **UID d'instance** (`InventoryStore`). `LaunchService.Forget(pid)` vide les stores et le personnage ; `CharacterSelectionRequest` peut sélectionner un personnage différent sur le même PID ; `ForgetSession` sur perte du lien conserve délibérément l'identité/les stores. Une preuve doit être invalidée au changement de session, non simplement comparer `CharacterId` lors de la lecture.
4. **Version.** L'ancien `Read` écrivait le paramètre `exactGameVersion` du *demandeur* sur le `StockObservation` sans comparer la version live. Le nouveau `Read` exige l'égalité stricte avec `LaunchService.LotMGameVersion` et la version enregistrée au snapshot.
5. **Champ StorageInventoryContentEvent.objects évolutif.** `ApplyStorageContent` utilise `FNum("StorageInventoryContentEvent", "objects")` et `InventoryReader.ReadContentAt`, pas un champ numérique constant. Le nouvel observateur lit les listes de messages imbriqués au moyen de la même lecture d'item versionnée, exige une seule interprétation possible, des UID/GID/quantités valides, pas de messages ignorés et l'**égalité exacte** avec les données réellement appliquées dans `Bank.Items` avant de publier.

## Correctif appliqué

- Deux états distincts par PID : **en attente** après paquet décodé, **publié** après signal **post-store**. `HdvUpdated` suit systématiquement `Inventory.Seed` sur `InventoryContentEvent`; `BankUpdated` suit `ApplyStorageContent` (y compris le waiter `BankOpenAsync` qui applique parfois le même paquet sur un autre thread). Le code compare le contenu UID/GID/quantité/position du snapshot aux lignes du store ; un simple événement d'arrivée n'est jamais un ACK de publication.
- La publication et l'invalidation sont sérialisées par un verrou interne ; le prochain paquet **serveur** invalide toute preuve intermédiaire non promue. Invalidation des deux réserves au début de session (`AuthenticationTicketAcceptedEvent`), aux `CharacterSelectionRequest/Event`, à `GameLinkLosing`/`GameLinkLost`/`GameLinkRestored`. Refus de lecture pendant `IsLinkSuspended`.
- Refus de banque pour les sessions de coffre à code (`ChestSessionActive`) et, **par prudence**, durant un séjour dans le Havre-Sac (`InHavenBag`). `StorageOpen` reste exigé.
- Contrôles d'entrée/UID (GID strictement positif, UID positif unique, quantité non négative, maximum 10 000 piles, version exacte). Si les messages imbriqués ne sont pas interprétables ou si le store diverge au moment du signal de mise à jour, **pas** de `Fresh`.
- Aucun stock Havre-Sac ajouté implicitement.

## Différence Banque / Havre-Sac

L'application contient `InHavenBag(pid)` (sous-zone 851) et des actions de déplacement/téléportation Havre-Sac. Le protocole contient un alias `MapComplementaryInformationsDataInHavenBagMessage`, mais **ce n'est pas un contenu de stockage**. Des documents Ankama distinguent le contenu du Havre-Sac et celui de la banque ; il serait donc **incorrect d'affirmer qu'un stockage Havre-Sac distinct n'existe pas dans le jeu**. Cet audit constate seulement l'absence, dans les sources examinées, d'une chaîne certifiée `snapshot complet du stockage Havre-Sac → UID/GID/quantité → store dédié → identité stable du coffre`. Tant que ces éléments ne sont pas prouvés, `StockLocation.HavenBag` demeure `UNKNOWN` plutôt que zéro ou un alias arbitraire de la banque.

## Tests et reproduction

```powershell
dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore -v:q -clp:ErrorsOnly
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --filter FullyQualifiedName~LiveStockEvidenceAuditTests
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --filter FullyQualifiedName~UnifiedStockPlannerTests
git diff --check -- GivreClone.App/Services/UnifiedStock/LiveStockEvidence.cs
```

Les tests `LiveStockEvidenceAuditTests` vérifient les **contrats d'ordre de publication dans les vrais fichiers sources** (les tests purs ne référencent pas `LaunchService`, qui appartient à l'application Avalonia) et le comportement du moteur aval sur snapshots partiels, doubles UID, personnages/versions incompatibles. **Il ne s'agit pas d'un test de timing live ou d'une preuve que chaque événement Dofus du build actuel a été capté**. La validation en jeu n'est ni demandée ni effectuée.

## Limites et suivi

- **Invariants restants non vérifiables ici :** `PacketDecoded`, `HdvUpdated` et `BankUpdated` ne transportent pas un même identifiant de transaction corrélé. Le patch compare le contenu et exige la promotion post-store, mais ne peut démontrer une atomicité réseau absolue sous un flux de plusieurs paquets simultanés ; il préfère **ne pas publier** plutôt que qualifier un état douteux comme complet.
- La réutilisation d'un PID après `Forget` **sans événement d'authentification/sélection décodé** ne produit pas de callback dédié sur l'observateur. Les stores et CharacterId sont remis à zéro par `Forget`, et l'entrée est retirée au prochain événement d'authentification reconnu. Un événement public `SessionEpoch` au niveau du launcher fournirait une garantie plus forte, mais serait hors OWN.
- `accountKey` est fourni par l'appelant `ItemCraftAccessService` et non authentifié par `LiveStockEvidence` ; `CharacterId` et la version sont contrôlés localement. L'intégration ne doit pas passer un identifiant de compte déduit du seul nom de personnage.
- Les corrections incrémentales du store après le snapshot initial sont lues depuis `InventoryStore` et `Bank` pendant sa fenêtre de fraîcheur (5 min par défaut) ; il n'existe pas de version atomique du store/UID exposed pour garantir un point-in-time cohérent pendant une modification concurrente. Une API de snapshot versionné côté LaunchService permettrait un contrôle plus fort, hors OWN.
- Le caractère vide d'un `StorageInventoryContentEvent` ne permet pas de distinguer, sans la disponibilité des champs de mapping, un vrai coffre vide et un message muet. L'acceptation n'a lieu **qu'après application au store** ; pour une preuve cryptographique/versionnée de schéma, il faudrait un contrat explicite avec `FieldNumber`, hors OWN.
- `UnifiedStockPlanner` distingue bien `Inventory`, `Bank`, `HavenBag`, déduplique les UID partagés et refuse les versions/personnages/snapshots non vérifiés. `AllSourcesFresh` restera naturellement faux tant que l'état du Havre-Sac n'a pas de preuve de stockage distincte.

**Pas de commit ni push.**
