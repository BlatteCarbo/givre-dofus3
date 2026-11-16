# Socket clientless — audit du lancement, 20 octobre 2026

## Chemin vérifié

`SocketTestAsync` attend le catalogue ; `StartSocketCore` vérifie le droit Socket, les champs obligatoires du mapping, le droit multi, la route principale et le proxy de jeu. Il alloue un PID synthétique et lance `SocketProbe.RunAsync` en tâche de fond. Les entrées planning, équipe, « Lancer tout » et reconnexion utilisent le mode `Account.Mode`; le bouton « Lancer » force le MITM. Les jetons de navigateur et keydata Launcher sont traités séparément. Aucun client Dofus ni compte réel n'a été lancé pour cet audit.

## Défauts confirmés dans la logique source et corrections

- **Compte Socket bloqué dans « Running »** : l'initialisation `_launch.WarmUpForSocket()`, la génération de `deviceSeed`, la lecture locale des jetons et la remise à zéro du lien précédaient la protection d'erreur de `SocketProbe`. Une exception terminait silencieusement la tâche détachée, sans passer par `StopCore`. Le lancement signale désormais un refus permanent et nettoie la carte et le PID via `GiveUp`. Une exception échappant au transport est convertie en issue transitoire, intégrée au budget de reconnexion. Une annulation intervenue pendant le préchauffage empêche aussi l'ouverture d'une nouvelle session.
- **Attente de reconnexion désynchronisée** : les états `SocketParked` / `SocketBackoff` et l'événement `SocketSessionLost` étaient publiés avec `Dispatcher.Post`, sans garantie qu'ils précèdent le traitement de la demande « Reconnecter » ni une reprise rapide. La transition UI est désormais attendue (`InvokeAsync`) avant la mise en attente et le réveil ; les flags sont aussi effacés sur l'UI avant l'ouverture de la session suivante, sans `Post` différé susceptible de modifier l'état d'une autre session.
- **Mapping incomplet entre deux connexions** : le garde de reconnexion contrôlait seulement la présence du catalogue, alors que l'entrée initiale exige aussi les champs essentiels. La reconnexion échoue désormais proprement en présence d'un mapping incomplet.
- **Planning démarré avant le mapping** : en lancement automatique Socket, le planning appelait directement `StartSocketCore`, sans attendre le flux initial, contrairement au bouton manuel. Il attend maintenant le fetch de catalogue sans modal interactif et vérifie que la session n'a pas démarré entre-temps.
- **IP de sortie périmée** : le contrôle du proxy pouvait terminer après un arrêt puis remplacer l'IP affichée par celle de l'ancien PID. L'affectation passe maintenant par le thread UI et contrôle le PID courant.

## Limites distinctes du ViewModel

- Le `MappingCatalog.LoadLocalEdition` retire explicitement `ClientChallengeInitRequest.fyvk` de son décompte des lacunes en le décrivant comme manquant et le Socket direct comme non opérationnel. Ce dossier ne forge ni champ ni opcode pour le contourner. Le protocole reste dépendant d'un catalogue complet, vérifié pour le build installé.
- `SocketProbe` exige un manifeste Cytrus installé cohérent avec `SessionVersionBinding`; sans preuve de version locale, le Socket clientless est refusé au transport. Le « Socket lancé » du ViewModel décrit une tâche amorcée, pas une connexion authentifiée ni un ACK gameplay.
- La connexion réelle, l'identification personnage et le gameplay ne peuvent être certifiés hors ligne ; le worker transport/prime assure l'analyse de `SocketProbe`/`SocketConnectionClient`.

## Vérification

Tests hors ligne : `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SocketLaunchConfigurationTests` : **5/5 PASS**, mode persisté, `AuthSource`, sélection serveur, routes compte/jeu, sans lancement de Dofus. Build `dotnet build GivreClone.App/GivreClone.App.csproj -c Release --no-restore -clp:ErrorsOnly -p:WarningLevel=0` : **FAIL dans un fichier hors périmètre**, `Views/MainWindow.axaml:405` (AVLN3000, setter `Visual.ZIndex` avec binding de type non accepté). Transmis au prime pour correction par le propriétaire UI ; aucune erreur de compilation du ViewModel Socket n'a été signalée dans ce build, mais la compilation générale n'est pas validée.

**Risque restant :** l'état UI et la boucle de session ne sont pas couverts par un test d'intégration Avalonia injectant un faux transport ; cet audit traite les courses d'ordre constatées à la lecture du code, pas une connexion réseau vérifiée.
