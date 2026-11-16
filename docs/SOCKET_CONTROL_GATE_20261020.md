# Socket clientless — statut « En jeu » et commandes indisponibles

Audit ciblé du ViewModel le 20 octobre 2026, branche `integration-opus-20261020`. Inspection locale et tests hors ligne uniquement. Aucun jeu ou compte réel lancé.

## Diagnostic prouvé dans les sources

1. `AccountsViewModel.Socket.cs` initialise `row.IsSocketSession = true`, un PID synthétique, `row.Running = true` et `BotActivity.Connecting`. L'écran passe ensuite à `BotActivity.InGame` quand `LaunchService.ActivityOf(pid)` lit une carte (`CurrentMap`), indépendamment d'une disponibilité d'injection. `BotStatusMap.From` expose alors le statut `ingame` au superviseur.
2. `SocketProbe` fournit systématiquement `FeedFrame = Stores.Launch.FeedSocketFrame`, donc des paquets décodés peuvent peupler la carte et l'identité. Sur `compatibilityReview`, le même appel fournit `RegisterInjector = null` et `UnregisterInjector = null`. Le moteur peut donc afficher « En jeu » en **observation seule**, tandis que le transport `DispatchTransport.Inject` renvoie `false` pour ce PID synthétique sans injecteur. C'est la cause concrète principale du symptôme lorsque cette revue est active. Le prime traite la politique de revue et la validation wire ; aucun garde de version/protocole n'est contourné ici.
3. En temps normal, `SocketGameClient` enregistre `Inject` auprès de `LaunchService.RegisterSocketInjector`, et le retire dans `finally`. `Inject` renvoie `true` uniquement si sa porte interne `_injectionReady` est ouverte et si une trame bornée a pu entrer dans sa file. **True signifie mise en file**, pas écriture prouvée ni ACK serveur. La présence de l'injecteur dans LaunchService est un signal moins fort encore.
4. Les droits sont distincts : `AccountsViewModel.CanSocket` vérifie `Perm.Socket` ; `LicenseService.CanBot` dépend de l'autorisation ET de `!IsSuspended` ; l'injection `RelayForInject` est fermée si `CanBot` est faux. Les droits combat/récolte sont contrôlés à leurs points d'action. Un mapping publié ou une carte reçue ne prouvent pas ces droits ni l'ACK.

## Correctifs exclusifs VM/Launch

- **Reconnexion manuelle** : après `SocketProbe.RunAsync`, le branchement `manual` appelait `Dispatcher.UIThread.Post` pour effacer `_pidStats`, `Stores.World` et l'ancienne carte, puis enchaînait immédiatement la connexion suivante. Si l'ancienne callback s'exécutait après réception d'une nouvelle carte, elle détruisait les informations du nouveau contexte. Remplacé par `await Dispatcher.UIThread.InvokeAsync` : le nettoyage est terminé avant la tentative suivante.
- **Arrêt immédiat** : `StopCore` annule le CTS Socket et retire désormais synchroniquement l'injecteur via `LaunchService.UnregisterSocketInjector(pid)` avant `Forget`. Auparavant seul le `finally` asynchrone du transport retirait l'injecteur ; des actions tardives pouvaient encore trouver un canal d'écriture pendant cette fenêtre. Cette suppression reste idempotente avec le `finally` du transport.
- **Diagnostic opérateur** : nouvelle API **lecture seule** `AccountsViewModel.ObserveSocketControl(AccountRow? row)` → `SocketControlObservation(string State, bool InjectorRegistered, string Explanation)`. Elle lit la session, le lien, le droit Socket, `CanBot`, la complétude du mapping, la carte et `LaunchService.HasSocketInjector(pid)`. Aucun octet n'est envoyé, aucun test d'injection n'est simulé.

## Contrat d'intégration pour le prime / l'UI Bots

`SocketControlObservation.State` est l'une des chaînes stables `offline`, `suspended`, `license-blocked`, `botting-paused`, `mapping-unavailable`, `awaiting-map`, `observation-only` ou `channel-registered`.

`observation-only` est le cas qui explique directement **« En jeu » mais aucun déplacement/combat/expédition** : carte disponible, injecteur absent. `channel-registered` indique uniquement l'existence du callback dans le dispatch : les conditions internes de `SocketGameClient.Inject`, l'envoi sur le fil, les ACK et les effets gameplay restent à vérifier séparément. Ne pas transformer cet état en badge « certifié » ou faire démarrer automatiquement des scripts sur cette seule base.

Le code de l'UI ou de l'onglet Bots peut appeler cette méthode pour fournir un motif explicite aux utilisateurs, sans modifier `BotStatus.InGame` qui signifie encore « présence d'une carte observée ». Le prime conserve la propriété de `LaunchService.HasSocketInjector` et du chemin de compatibilité dans `SocketProbe` ; aucune nouvelle dépendance ni modification du transport n'a été introduite dans ce lot.

## Validation et limites

`SocketLaunchCapabilitiesTests` vérifie hors ligne l'ordre du nettoyage manuel, la suppression de l'injecteur avant oubli de l'identité, le branchement du diagnostic sur le dispatch réel, et la distinction `ingame`/ACK par les classes pures `BotStatusMap` et `BotReadiness`. Les assertions d'ordre inspectent les deux sources ViewModel car l'assembly de tests ne référence volontairement pas Avalonia. Elles ne sont pas une simulation de l'ordonnanceur UI.

Validation initiale : `git diff --check` sur les deux fichiers de ce lot **PASS**. Le build App Release a été lancé mais **BLOCKED hors périmètre** par `CS2001` sur `Services/Expedition/ExpeditionSocketSessionGate.cs` référencé et encore absent pendant un travail parallèle. Le test ciblé a lui aussi été lancé mais **BLOCKED hors périmètre** : `ExpeditionSocketSessionGateTests.cs` invoque `ExpeditionSocketSessionSnapshot`, type non encore livré par le lot expédition. Aucun de ces deux échecs ne démontre un défaut de notre correctif ; la compilation finale de ce lot reste à refaire une fois les sources expédition intégrées.

L'intégration finale nécessite la compilation Release de l'application et une preuve wire corrélée par le prime (carte et cellule authentiques, disponibilité de l'écriture, réponse serveur à une action) ; aucune conclusion « mouvement/combat fonctionnels » n'est tirée de ce diagnostic hors ligne.
