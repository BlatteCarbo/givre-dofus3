# MZP1 — branchement du moteur sur les actions protégées

**Statut : source réelle raccordée jusqu'à `BotContext` et `BehaviorHost` ; raccord au `BotHost` actuel NON EFFECTUÉ et NON CERTIFIÉ.** Aucun producteur de provenance ou vérificateur de réponse serveur n'est présent sous forme de contrat live authentifié dans cette chaîne. Sans ces deux preuves, la fabrique MZP1 reste bloquée : elle ne crée pas d'`ActionSender` permissif.

## Traçage exact du moteur existant

| Fichier et point de jonction | Ce que réalise la source | Limite prouvée |
| --- | --- | --- |
| `GivreClone.Engine/BotHost.cs`, constructeur, lignes 23–33 | Crée `TcpProxyServer`, `BotOrchestrator` et `DofusCaptureHost` | `BotHost.Start()` démarre la surveillance et injection de processus ; **interdit à cette passe** |
| `GivreClone.Engine/BotOrchestrator.cs`, lignes 21–35 | `Binder = new SessionBinder(CreateSession)` ; `CreateSession` fabrique `new BotSession(connectionId, catalog, new ProxyFrameSink(...), pacer)` | Aucun paramètre pour fabrique d'actions protégées ni autorités |
| `GivreClone.Engine/SessionBinder.cs`, lignes 13–23 et 42–58 | `Func<int, BotSession>` ; crée une session depuis le seul `connectionId` lors de la première capture ; transmet `CapturedMessage` à `BotSession.Dispatch` | Type `BotSession` concret ; PID de capture éventuellement nul ; pas de preuve de naissance PID, génération ou version |
| `GivreClone.Engine/BotSession.cs`, lignes 11, 26–35, 38–53 | Classe `sealed` ; crée `GameState`, `PacketBus`, `RequestCorrelator`, **`new ActionSender`**, `BotContext` et `BehaviorHost` ; repli puis diffusion des captures | `Actions` est get-only et construit en dur ; aucune substitution possible sans changement du consommateur |
| `GivreClone.Engine/BotContext.cs`, lignes 16–22 | Son constructeur accepte déjà `IActionSender` | **Point réutilisable tel quel**, sans changement de signature |

La classe `BotSession` ne peut pas être héritée et son constructeur n'accepte pas `IActionSender` ou une fabrique. L'assembler MZP1 construit donc le **vrai** `BotContext` et le **vrai** `BehaviorHost` à partir de véritables `GameState`, `PacketBus`, `RequestCorrelator`, du transport `IFrameSink` existant et de `MZP0GuardedActionSender`. Le harness lie et compile les sources exactes `BotSession.cs`, `BotContext.cs`, `Behavior.cs`, `GameState.cs`, `ActionSender.cs`, etc. Son test négatif vérifie que `BotSession.Actions` est toujours un `ActionSender` non protégé.

## Nouveau contrat disponible dès compilation Engine

La classe `MZP1GuardedEngineWiringFactory.Create` accepte `expectedConnectionId`, `requiredProtocolVersion`, le vrai `ProtocolCatalog`, `IFrameSink`, `IPacer`, un `MZP0RecoveryJournal` et **deux fournisseurs indépendants** :

- `IMZP0AuthenticatedSessionSource` : PID et naissance de processus, connexion et version exacte, avec nouvelle vérification à chaque usage. Un PID seul ou `BotSession.ProcessId` ne suffit pas.
- `IMZP0PositiveResponseVerifier` : preuve de source serveur et sémantique positive d'une **Response** corrélée UID/génération/nonce. Un événement, l'arrivée d'un message ou `SendOutcome.Sent` ne prouvent jamais un ACK.

En cas d'absence ou de désaccord, l'assemblage produit un véritable `BotContext` dont `Actions` est un `IActionSender` qui **refuse** les actions ; `Admission` indique une raison non sensible (`MissingIdentityAuthority`, `MissingResponseAuthority`, `IdentityMismatch`, etc.). La fabrique ne démarre aucun proxy, aucun client et aucune capture, et n'invente aucune attestation. Le journal reste inactif si les autorités manquent.

Lorsque les deux autorités sont fournies et attestent la même session attendue, `Context.Actions` référence réellement `MZP0GuardedActionSender`. Chaque envoi passe par le `IFrameSink` de journalisation durable de MZP0, sans rejeu, sans frame exposée dans `ActionResult` et sans promotion automatique d'ACK. `TryDispatchCaptured(CapturedMessage)` replie l'état, diffuse un événement *projeté sans payload ni raw frame* sur le `PacketBus` et consulte le vérificateur de réponse ; il refuse PID manquant, mauvaise connexion ou PID discordant. En cas de révocation, le moteur bloque les actions suivantes et arrête le comportement actif. Le journal est **détenu par l'appelant**, qui doit gérer son cycle de vie et l'isolation par session.

Cette projection peut demander l'ajout futur d'un modèle de données sûr pour les comportements qui exploitent aujourd'hui directement les champs bruts du bus. Elle ne reproduit pas encore tous les lecteurs ou comportements métier.

## Modification du consommateur nécessaire pour passer en production

À réaliser par PRIME dans un **autre scope approuvé**, après obtention des preuves externes ; aucun des fichiers ci-dessous n'a été modifié :

1. Dans `BotSession`, fournir un constructeur utilisant directement **les mêmes instances** `State`, `Bus`, `Correlator`, `Actions`, `Context` et `Behaviors` qu'un `MZP1GuardedEngineWiring`, ou introduire un type commun accepté par `SessionBinder`. Supprimer en mode protégé le chemin `new ActionSender(...)` (actuel ligne 33). Simplement créer un wrapper à côté de `BotSession` ne change **pas** `BotSession.Actions`.
2. Dans `SessionBinder`, modifier le `Func<int, BotSession>` ou ajouter une étape de création/admission disposant de **l'observation authentifiée d'origine** avant d'activer la session. `CapturedMessage.ProcessId` seul, surtout si nul, ne donne pas les ticks de naissance PID ou la version cliente ; `CreateSession(int)` ne les reçoit pas.
3. Dans `BotOrchestrator.CreateSession`, transmettre les deux autorités fiables, la version client exacte et un journal distinct par connexion ; attribuer le `MZP1GuardedEngineWiring` au contexte servant réellement à `BehaviorHost`. Le choix *guarded* ne doit pas retomber implicitement sur l'ancien `ActionSender`.
4. Dans la réception native, alimenter `TryDispatchCaptured` **uniquement** avec captures dont l'identité est rattachée à la source authentifiée et fournir un vérificateur externe de response positive spécifique au protocole. Fermer/révoquer à chaque retrait de l'identité, déconnexion ou rebind.
5. Faire les validations de bout en bout **uniquement après** disposer du vrai `GivreCore` et d'une source authentifiée. L'hôte existant surveille/injecte potentiellement tous les PID ; ne jamais lancer `BotHost.Start()` dans ce travail.

Il n'existe ici **aucune implémentation live des interfaces d'autorité**. Les implémentations présentes dans `MZP1GuardedWiringTests.cs` sont seulement des fixtures entièrement hors ligne. `MZP1Admission.Ready` dans ces tests démontre le contrat d'injection, **pas** une preuve de provenance ou de réponse réelle ni une parité avec GivreClone EXE.

## Validation reproductible, sans GivreCore ni réseau

```powershell
dotnet test tools/MZP1GuardedWiringHarness/MZP1GuardedWiringHarness.csproj -c Debug
dotnet test tools/MZP1GuardedWiringHarness/MZP1GuardedWiringHarness.csproj -c Release
```

Les sources Engine et Protocol de production sont **liées telles quelles** par le `.csproj` isolé. Seuls `GivreCore.Mitm` et `GivreCore.Protobuf` manquants sont représentés par des stubs de test, réutilisant ceux du harness MZP0. Une réussite de tests ne démontre **pas** la fidélité wire du vrai `GivreCore` ni la sûreté d'un futur adaptateur de capture.
