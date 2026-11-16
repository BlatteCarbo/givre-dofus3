# GivreWork — audit parallèle et corrections offline du 13 novembre 2026

Worktree : `fix-f01-f16-final-20261024` ; base observée : `b4d28a8` (2 novembre 2026). À l'ouverture, 277 fichiers suivis portaient déjà des changements non commités. Aucun reset, checkout, lancement de client, envoi de paquet, publication ou arrêt de processus.

## Correctifs appliqués par le coordinateur

| Zone | Avant | Changement | Preuve |
| --- | --- | --- | --- |
| `GivreClone.PureTests/GivreClone.PureTests.csproj` | Le projet de tests pur ne compilait pas : références manquantes à `InventoryReader`, `MapInteractive`, puis `MappingScan`, `CombatVisualOptions`, `MZP19LocalClone` | Lier les sources autonomes `MapInteractive`, `MappingScan` et le codec MZP3 ; conserver les adaptateurs dépendant du protocole complet, le cache HTTP et le préflight de bundle natif dans la suite complète | `dotnet test GivreClone.PureTests/GivreClone.PureTests.csproj --no-restore --verbosity quiet` : 362/362, exit 0 |
| `GivreClone.App/Services/Eco/RuneCatalog.cs` | `TierFor` excluait `Pa`/`Ra` quand `PaFrom` ou `RaFrom` valait `0`, alors que `0` signifie « dès le départ » | Respecter `current >= threshold` pour les deux paliers | `RuneCatalogThresholdTests` : deux cas en échec avant, six cas réussis après ; `RuneCatalogTests` (suite complète) : 6/6, exit 0 |
| `GivreClone.Protocol/MZP3FrameAndEnvelope.cs` | En lecture non exacte, le décodeur rejetait deux frames valides cumulées dans un même buffer comme `TooLarge` | Limiter la taille déclarée de chaque frame, n'appliquer le plafond de longueur totale que dans le mode exact | `MZP3FrameCodecTests` : 1 échec avant, réussite dans les 362 tests après ; garde sur longueur déclarée > limite conservée |

Le harness historique `tools/MZP3ProtoHarness` a reçu le scénario de deux grandes frames regroupées, mais n'est pas exécutable dans cette copie : dépendance source `GivreRework/.../P0WirePrimitives.cs` absente. Le nouveau test autonome exécute le code production lui-même et couvre ce défaut. Aucune validation d'un flux jeu réel n'a été effectuée.

## Constats des quatre agents (audit statique, encore à traiter)

Les priorités indiquent l'impact probable et demandent un test de reproduction dédié avant déclaration de bug confirmé.

| Priorité | Constat et scénario | Emplacement | Action recommandée |
| --- | --- | --- | --- |
| P1 | Nouvelle routine de bot démarre avant l'arrêt effectif de l'ancienne, avec commandes potentiellement concurrentes | `GivreClone.Engine/Behavior.cs:23-46` | Sérialiser arrêt/démarrage, observer l'ancienne tâche, invalider les commandes de génération périmée |
| P1 | Rotation mêlant sort compatible et sort non planifiable : attaques restantes ignorées, PA inutilisés | `GivreClone.App/Services/Combat/AutoSpellRotation.cs:30-53` ; `LaunchService.Combat.Turn.cs:994-999,1113-1118,1481-1490` | Parcourir tous les sorts avant de choisir le planificateur ; tests des deux ordres et du tour complet |
| P1 | Confirmation tardive d'un modificateur de sort écartée après consommation anticipée du cast | `LaunchService.Combat.SpellMods.cs:38-43,76-82` ; `Turn.cs:247-258` | Conserver l'attente jusqu'à la réponse corrélée et purger en fin de tour/session |
| P1 | Défi manuel re-proposé après choix ; nombre égal de propositions nouvelles non rafraîchi | `GivreClone.App/ViewModels/BotRowViewModel.Fight.cs:149-164,230-235` ; `LaunchService.Combat.PreFight.cs:415-463` | Suivre l'identité des défis, distinguer proposé/en cours/confirmé, tester double-clic et changement de bonus |
| P1 | Défis et bilan de combat visibles dans le journal graphique mais absents des lecteurs console MCP sans script | `GivreClone.App/ViewModels/BotsViewModel.GameJournal.cs:48-68` ; `GivreClone.App/ViewModels/BotsViewModel.Lua.cs:545-558` ; `GivreClone.App/ViewModels/BotsViewModel.Control.cs:184-188` | Publier les événements système dans une console MCP corrélée, bornée et dédupliquée |
| P1 | Les refus d'envoi de chat n'ont aucun message opérateur | `GivreClone.App/ViewModels/BotsViewModel.Console.cs:50-63` ; `Services/LaunchService.Chat.cs:28-58` | Validation d'entrée et motif local de refus ; distinguer acceptation et livraison |
| P2 | Réduction d'état et publication peuvent inverser l'ordre d'événements si deux captures s'entrelacent | `GivreClone.Engine/Recovery/MZP1GuardedEngineWiring.cs:98-148` | FIFO de publication par session ; essai concurrent déterministe |
| P2 | `SessionBinder` conserve les sessions détachées et recherche un PID parmi les anciennes connexions | `GivreClone.Engine/SessionBinder.cs:14-89` | Éviction sur fermeture, clé de session générationnelle, test de reconnexion avec même PID |
| P2 | Fin de combat de zone surestimée (PV sans bouclier/résistances), déplacement anticipé injustifié | `GivreClone.Engine/ChallengeFinishPolicy.cs:8-15` ; `LaunchService.Combat.Turn.cs:820-861` | Estimation conservatrice par cible ou abstention si protections inconnues |
| P2 | Calcul des menaces consomme le budget avant de filtrer les distances ; reranking coûteux et portail reconstruit par voisin | `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:285-318,565-640,1143-1223,1290-1297` | Bench Release isolé avec mêmes décisions, puis modifier bornage et allocations |
| P2 | Une interaction à portée >1 peut déclencher des centaines de recherches A* | `GivreClone.Protocol/MapPathfinder.cs:151-162,224-229,325-331` | Recherche multi-objectifs conservant ordre des cases et contraintes |
| P2 | Profils et planning indexés par nom ; collision entre personnages homonymes | `CombatProfileStore.cs:35-46` ; `PlanningStore.cs:33-44` | Clé compte/serveur/personnage et migration contrôlée |
| P2 | Console MCP change de clé à la résolution d'identité ; `zaapi_to` n'est pas suivi comme `travel_to` | `GivreClone.App/Services/Mcp/McpConsole.cs:12-19` ; `GivreClone.App/ViewModels/BotsViewModel.McpJournal.cs:49-60,100-129` | Clé session stable et état terminal des zaapis |

## Ordre proposé

1. Sécurité des transitions de comportements et de sessions (exécution unique, ordre des événements, reconnexion).
2. Complétude d'un tour de combat (rotation mixte, confirmations de sorts, puis protections/finition).
3. Console opérateur cohérente (défis, erreurs de chat, clés MCP, suivi zaapi).
4. Performance : réutiliser `docs/cos-dofus-video/rapports-v2/N03/bench/Program.cs` et `CombatPlannerPerformanceTests.cs`, mesurer sur le code exact et comparer les décisions avant/après ; optimiser ensuite le coût des menaces et des trajets.

Les constats d'agents sont des analyses de chemins de code, pas une preuve de bug serveur. Les nouvelles corrections ont été vérifiées par tests hors jeu uniquement. Les quatre agents ont opéré en lecture seule ; seuls les fichiers listés dans la section « Correctifs » et les scénarios de test associés ont été modifiés par le coordinateur.
