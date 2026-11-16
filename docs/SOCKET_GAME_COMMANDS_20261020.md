# Socket clientless — commandes mouvement/combat, preuve d'envoi et ACK (20/10/2026)

## Résultat et décision

Le client Socket se connecte et reçoit des événements de jeu en 3.7.12.12 alors que le catalogue local se déclare 3.7.11.11. **« En jeu » n'est pas équivalent à « pilotage possible »**. Le diagnostic historique `SOCKET_371212_RUNTIME_OBSERVATION_20261020.md` constate connexion, sélection, contexte et 624 paquets reconnus sur 624 à un instant, mais **aucun aller-retour d'action fabriquée attribuable au même client**. Aucune affirmation de compatibilité générale des codes/ACK ne peut en découler.

La politique actuelle distingue :

1. **Build exact et mapping présent** : publication du transport de pilotage uniquement après `CharacterSelectionEvent.success` **ET** contexte serveur `ContextCreationEvent`, ou `FightJoinRunningEvent` pour une reprise. Ce changement supprime la fenêtre où le dispatch exposait un injecteur dès la connexion TCP et où la sélection seule était interprétée à tort comme une readiness gameplay.
2. **Candidat inter-patch en revue** : l'injection générale demeure interdite. Une seule capacité de diagnostic peut être fournie après l'admission SHA effectuée par `SocketProbe` et un **opt-in explicite** du propriétaire `SocketGameParams.ReviewedCrossPatchPilot=true`, false par défaut. Elle accepte `MapMovementRequest` et `MapMovementConfirmRequest` **seulement**, l'un après l'autre, à partir du catalogue mappé par nom ; pas d'accès aux autres commandes.
3. **Refus ou absence de confirmation** : verrouillage de la session d'injection, sans retry automatique, transfert de compte, contournement du serveur ou publication d'un faux ACK. Une connexion vivante peut continuer en consultation.

Le processus de bootstrap du jeu reste distinct : `SendGame` peut envoyer la sélection, les requêtes de carte/contexte, le ping et les accusés de séquences de combat calibrés par les captures précédentes. Ces réponses **du client** (par ex. `GameActionAcknowledgementRequest`) ne sont pas une confirmation **du serveur** pour la commande du moteur. `Inject` est, lui, le chemin du moteur `MoveForge → RelayForInject → SocketGameClient.Inject → Channel<Outgoing> → NetworkStream.WriteAsync`, et son retour `true` signifie **mis en file**, pas écrit ni accepté serveur.

## Lecture des preuves existantes sans informations privées

Seules les **notes consolidées et le code**, ainsi que l'inventaire non sensible du dossier de logs, ont été inspectés. Le répertoire privé `%APPDATA%\GivreClone\logs` n'était pas disponible depuis cette session pour recompter des trames récentes ; aucune lecture de jeton, login, mot de passe, serveur, IP, payload, trace raw ou nouveau login n'a été effectuée.

| Source | Constat limité | Limitation |
|---|---|---|
| `docs/codex/TRANSPORT_LOG_ANALYSIS.md` (17/10) | Logs 16/10 : **11 316 TX / 50 127 RX** et **2 094 TX / 8 464 RX** ; messages de mouvement et confirmation nommés ; sur 17/10, **8 105** MapMovementEvent et **5 052** MapMovementConfirmResponse sur une session | Comptes de lignes existantes, pas d'identifiant de transaction ni d'octets wire corrélés |
| `docs/SOCKET_371212_RUNTIME_OBSERVATION_20261020.md` | À 22:29, sélection/contexte et tableau « En jeu » ; **35 lignes socket-tx et 671 socket-rx** selon un relevé | Aucun envoi de déplacement forgé ou ACK de cette action prouvé |
| `SocketGameClient.cs` | Le `SendGame` journalise la longueur d'une trame de rôle client puis fait `WriteAsync`/feed vers le décodeur ; les injections moteur écrivent sous le même verrou mais ne sont pas fed au décodeur (évite faux « mouvements manuels ») | `WriteAsync` réussi ≠ ACK serveur ; ne pas annoncer une action « réussie » à partir de la file |
| `LaunchService.Packets.cs` (lu sans modification) | `MapMovementRequest` émis, `MapMovementConfirmRequest` sortant, `MapMovementConfirmResponse` et `MapMovementRefusedEvent` entrants possèdent déjà des handlers de garde et de position | Pas de reçu montrant que les quatre types furent correctement reliés à un **essai Socket 3.7.12.12** |

## Correction démontrable dans SocketGameClient

**Prématurité du dispatch :** auparavant `p.RegisterInjector` était invoqué à la connexion TCP, puis `_injectionReady` passait à 1 immédiatement après `CharacterSelectionEvent.success`. Cela permettait au transport d'apparaître « disponible » alors que le contexte/chargement de carte n'était pas encore attesté. Le callback et la readiness sont maintenant publiés uniquement ensemble, après sélection réussie et contexte de jeu ; la rejoin de combat ne lève **pas** l'exigence d'identité de personnage.

**Mutation de trames :** la file d'injection gardait le tableau `byte[]` du producteur. Le producteur pouvait le modifier ou le réutiliser avant le vrai `NetworkStream.WriteAsync`, d'autant plus que la file attend les animations, les accusés et parfois un délai de fenêtre. La file copie désormais `(byte[])framedToServer.Clone()` AVANT `TryWrite`. Le test contractuel contient une reproduction isolée de l'aliasing avec `Channel<byte[]>`.

**Revue inter-patch :** la présence d'un callback dans `SocketProbe` ne suffit plus à publier un injecteur. `ReviewedCrossPatchPilot` (défaut false) exige en plus que le mapping de session indique une cible revue exactement égale à la version installée et un SHA Protobuf de revue déclaré. Il ne convertit pas cette égalité de schéma en « mapping de messages réseau certifié ».

## Diagnostic opt-in de mouvement en revue (pas certification wire)

À chaque `Inject` après la readiness sélection+contexte :

- Une trame est clonée et structurée ; maximum **4 Kio** pour le diagnostic. Préfixe `varint(length)` exact, une enveloppe `GameMessage.request`, un seul type_url, code résolu via `cat.CodeForName`, champs `map_id` positif et `key_cells` packés (taille 1..256 octets). Rien n'est inféré d'une égalité `.proto`.
- Un **seul mouvement en vol**. `MapMovementRequest` fait passer la machine à `MOVEMENT_PENDING`. Seul un `MapMovementEvent` identifiant **notre personnage** fait passer à `SELF_ECHO_OBSERVED_NOT_ACK`.
- La seule action suivante admissible est `MapMovementConfirmRequest` (corps vide, envoyé par le transport/moteur existant après l'écho). Un `MapMovementConfirmResponse` serveur la fait passer à `SERVER_RESPONSE_OBSERVED_REVIEW_ONLY`; **ce n'est pas un ACK corrélé à un UID**. Alors seulement, un mouvement de plus peut être tenté, jamais deux simultanément.
- Un `MapMovementRefusedEvent`, l'absence d'un des événements attendus dans **20 secondes**, un mapping insuffisant, un frame invalide ou une file pleine bloque/refuse proprement ; au timeout/refus, la readiness de la session est révoquée.

La fenêtre de diagnostic ne permet **ni les sorts, ni les attaques de monstres, ni la préparation de combat, ni l'HDV, ni la banque, ni l'exécution arbitraire de Lua**. Les commandes hors whitelist sont refusées sans journaliser leur contenu ; les rejets sont comptés et un message borné est émis ponctuellement. Aucun envoi spontané n'est introduit : chaque diagnostic démarre seulement depuis une demande explicite du moteur/utilisateur.

L'attaque `AttackMonsterRequest` **reste interdite en revue** même après un déplacement observé. L'autoriser seule mettrait le personnage en combat sans le parcours confirmé `FightPlacementPositionRequest → FightReadyRequest → FightSpellCastRequest / FightTurnFinishRequest` et ses ACK ; cela pourrait piéger la session. Les commandes et champs doivent être vérifiés dans la version cible, puis chaque action/fin de tour corrélée à une réponse serveur. Le contrôle global en version *exacte* n'a pas été volontairement réduit par cette façade diagnostique.

## API à raccorder par le prime (hors OWN)

`SocketGameParams.ReviewedCrossPatchPilot` est un `bool` init-only, **false** par défaut. Le prime peut le définir **uniquement** après `MZP12SocketVersionBinding.Evaluate(...).IsUnverifiedCrossPatchReview` et revalidation de l'identité SHA et du reçu local ; il ne doit jamais le rendre true pour un autre client, un mapping non sourcé ou sur simple heuristique de famille 3.7. La lecture par `ReviewMovementDiagnosticStatus` retourne `NOT_STARTED`, `MOVEMENT_PENDING`, `SELF_ECHO_OBSERVED_NOT_ACK`, `CONFIRM_PENDING`, `SERVER_RESPONSE_OBSERVED_REVIEW_ONLY` ou `REFUSED_OR_TIMED_OUT`.

L'interface doit afficher **« essai mouvement en revue — non certifié »**, ne pas montrer « ACK serveur confirmé » sans identité de requête/retour, et garder les boutons combat/expédition désactivés tant que les parcours de protocole ne sont pas vérifiés. Un retour `true` de `Inject` reste la seule acceptation **locale**, jamais la preuve du déplacement réalisé sur carte.

## Validation / limites

Tests purs et contrôles de source : `GivreClone.Tests/SocketGameCommandsContractTests.cs` (readiness, publication différée, clonage, whitelist, suivi SELF, refus/timeout, garde de combat et absence de fausse promotion), et les six tests précédents `SocketTransportRegressionTests.cs`. Les tests ne lisent aucun secret, ne se connectent à aucun serveur et n'émettent aucune trame gameplay.

À certifier séparément sur **un seul client de test autorisé**, et pas dans ce lot : identité du message wire en 3.7.12.12, ordre/UID request et ACK, réponse de mouvement après confirmation, position serveur stable, trajectoire des combats (placement, sorts, fin de tour). Aucun de ces résultats n'a été inventé. Aucun lancement client/live, aucune modification de `SocketProbe`, `MappingCatalog`, `MainWindow`, `AccountsVM`, aucun commit ni push par worker-10.
