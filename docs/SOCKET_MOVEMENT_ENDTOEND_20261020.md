# Socket — mouvement hors combat, preuve end-to-end offline (20 octobre 2026)

## Résultat et périmètre

Lot limité à `GivreClone.App/Services/LaunchService.Movement.cs` et au banc de tests `GivreClone.Tests/SocketMovementEndToEndHarness/`. Aucun client Dofus, connexion, compte réel ou paquet vers un serveur n'a été utilisé. Le statut « En jeu » renseigne un contexte Socket, mais ne prouve ni la disponibilité de l'injecteur ni la réussite d'une commande gameplay.

Le fonctionnement attendu se déroule ainsi :

1. **Dispatch prêt** : `SocketProbe` enregistre l'injecteur du **pid synthétique** lorsque `SocketGameClient` possède son contexte de jeu ; `LaunchService.HasSocketInjector(pid)` permet de distinguer cette condition. L'enregistrement et les droits de session sont gérés par le prime et le worker-10.
2. **`MoveTo(pid,mapId,source,target,...)`** : confirme bornes des IDs/cellules, refuse une carte différente de la carte serveur connue, applique `MapPathfinder.KeyMovements` (orientations et tournants packés), puis forge par **noms du mapping** `MoveForge.BuildFramed` (`MapMovementRequest`, historiquement jrw).
3. **Dispatch `DispatchTransport.Inject`** : choisit l'injecteur Socket du même pid ou le relais MITM. `true` signifie que la trame a été acceptée par le transport, **pas ACK du serveur**. Si l'injecteur Socket n'existe pas, aucun repli MITM vers un autre pid.
4. **Écho serveur `MapMovementEvent` (historiquement jsj)** : `LaunchService.Packets.cs` décode le chemin réel, vérifie l'acteur si son characterId est connu et la destination du mouvement en attente. `ConfirmMoveFallbackAsync` vérifie en plus la séquence, source/fin du trajet et la carte courante. Un autre acteur qui termine à la même cellule ne doit pas déclencher notre JQI.
5. **Confirmation différée `MapMovementConfirmRequest` (historiquement jqi)** : Socket joue le rôle du client. Le délai est basé sur `ClientMoveDurationMs(serverPath)` depuis **la réception de l'écho serveur**, avec marge. L'heure d'`Inject` correspond à la mise en file de `SocketGameClient`, pas forcément à l'écriture TCP ; déduire cette file du temps d'animation provoquait des JQI immédiats sur des trames retenues.
6. **Résultat serveur** : seul `MapMovementConfirmResponse` (historiquement jsq) libère la garde, règle l'attente Lua et autorise les actions suivantes ; `MapMovementRefusedEvent` (historiquement jqt) refuse l'action et peut recaler `SelfCell` sur des coordonnées décodées. Un JQI accepté par `Inject` reste « confirmation mise en file », jamais « arrivé ». Les traitements serveur existants sont dans `LaunchService.Packets.cs`, fichier non modifié dans ce lot.

## Bugs directement observés dans le code et corrigés

| Défaut démontré | Conséquence | Correctif dans Movement.cs |
| --- | --- | --- |
| `_botMoveConfirm` / `_botMoveSentAt` armés **après** `relay.Inject` | Un écho synchronisé ou très rapide d'un pid synthétique trouve aucun mouvement en attente ; aucun JQI planifié | Armement avant dispatch, dans une section critique qui bloque deux clics concurrents ; rollback conditionnel en cas de refus d'injection |
| Absence de contrôle `jrw.Length` ou `jqi.Length` | Une forge refusée pour mapping absent pouvait appeler `Inject` sur un tableau vide ; faux succès du transport | Refus explicite avant injection, motif `MoveForge.LastForgeError` ; aussi appliqué au `MapChangeRequest` |
| JQI retiré de `_botMoveConfirm` **avant** injection | Si forge ou injection JQI échoue, l'attente est perdue ; aucun second essai au nouvel écho, impossibilité de finaliser | Maintenir l'état jusqu'à jsq/jqt ; deux réessais bornés en cas de refus local de dispatch, sans second écho ; un statut séparé empêche plusieurs JQI après une injection acceptée |
| Sous-estimation du temps d'animation depuis l'enfilement Socket | Des actions retenues par la file Socket rendaient `elapsed` supérieur au temps client et provoquaient l'envoi du JQI immédiatement après l'écho, avec risque de rollback | Attendre une durée entière prudente depuis l'écho réellement reçu (la durée peut être conservatrice ; nécessite des mesures live ultérieures) |
| Garde locale expirée après 4 secondes alors que **l'ACK manque** | Nouvelle demande forgée depuis une cellule optimiste ; refus/desync serveur possible | En Socket, ne pas émettre un second mouvement tant que `_botMoveConfirm` indique un résultat serveur non résolu |
| La fenêtre `_movingUntil` était ouverte **après** dispatch | Un ACK synchrone pouvait la refermer puis le retour de `Inject` la rouvrir à tort | Ouvrir la fenêtre avant dispatch ; ne pas effacer le recalage déjà établi par un refus synchrone via `SelfMoved` optimiste |
| `MoveTo` acceptait pid 0, cellules invalides ou carte différente d'une carte déjà confirmée | Requête non significative ou carte stale transmise | Validations de source, cible, mapId et contrôle conditionnel `_serverMapId` ; aucune carte/cellule inventée |

Les corrections n'interviennent ni dans `SocketGameClient`, ni `SocketProbe`, `AccountsViewModel` ou l'interface, ni dans le mapping ni le protocole. Le code continue de refuser une forge non résolue par le catalogue ; aucun opcode fixe n'est introduit.

## Banc de tests avec **véritable LaunchService**

Le nouveau projet console autonome `GivreClone.Tests/SocketMovementEndToEndHarness/SocketMovementEndToEndHarness.csproj` référence l'application existante pour appeler **les vraies méthodes MoveTo et ConfirmMoveFallbackAsync**. Il utilise un injecteur mémoire enregistré sur le pid **`0x40000001`**, des cellules de fixture `100 → 101`, une valeur map de fixture et les méthodes réelles `MoveForge`, `MapPathfinder`, `EnvelopeBuilder`. Les réponses `MapMovementConfirmResponse` et `MapMovementRefusedEvent` sont des `DecodedPacket` *explicitement synthétiques*, remis en mémoire à `OnDecoded` sans socket ni client.

Il vérifie notamment :

- sans injecteur Socket, aucun JRR/JQI envoyé ;
- carte/cellule/source invalide ou forge non résolue : zéro dispatch, zéro attente fantôme ;
- `Inject` refusé : garde et pending nettoyés, prochain essai possible ;
- deux clics concurrents : **un seul** MapMovementRequest ;
- écho reçu pendant l'injection : confirmation déjà armée, JQI différé et non doublé ;
- écho d'origine/séquence erronées : aucun JQI ;
- JQI refusé par transport : pending conservé, **réessai automatique borné** après 250 puis 500 ms même sans nouvel écho ; aucune réussite déclarée si les trois tentatives échouent ;
- JQI accepté : état encore en attente de réponse serveur, sans libérer la garde ;
- jsq/jqt synthétiques : seul leur traitement décodé efface le pending et libère la garde ; un ACK synchrone ne recrée pas une activité en déplacement.

Commande de validation du banc (un projet séparé pour éviter que son `Program.cs` console ne soit compilé dans le projet xUnit global) :

```powershell
dotnet build GivreClone.App/GivreClone.App.csproj --configuration Debug --no-restore -clp:ErrorsOnly -p:BuildProjectReferences=false
dotnet build GivreClone.Tests/SocketMovementEndToEndHarness/SocketMovementEndToEndHarness.csproj --configuration Debug --no-restore -clp:ErrorsOnly -p:BuildProjectReferences=false
dotnet run --no-build --no-restore --project GivreClone.Tests/SocketMovementEndToEndHarness/SocketMovementEndToEndHarness.csproj --configuration Debug
```

Résultats observés : **10 PASS / 0 FAIL** au banc avec pid synthétique, build de l'application **0 erreur** (198 avertissements provenant de l'ensemble de l'application à la compilation de validation), build du banc **0 erreur / 0 avertissement**. Tests existants ciblés `MoveForgeTests` et `Patch361112Tests` : **19 réussis / 0 échec**. La validation **hors ligne** prouve la logique d'assemblage et les transitions locales du moteur avec un injecteur synthétique ; elle ne prouve pas que le serveur Dofus 3.7.12.12 a accepté des octets gameplay. Aucune confirmation réelle `jsq` n'est fabriquée ou déclarée comme observée.

## Risques / travaux coordonnés avec le prime

**Prérequis transport :** `SocketGameClient` doit enregistrer et désenregistrer l'injecteur au bon moment et prouver que sa file d'envoi transmet réellement le `MapMovementRequest`. Une session affichée « En jeu » sans `HasSocketInjector(pid)` bloque désormais proprement avec diagnostic. Le responsable est le prime (`SocketProbe`/`RegisterInjector`) et worker-10 (`SocketGameClient`).

**Preuve de carte/cellule :** `MoveTo` n'obtient pas lui-même le snapshot du serveur ; il reçoit `mapId/source` du demandeur et utilise `_serverMapId` s'il existe. Le fait qu'un script ou un panneau visualise une cellule n'en fait pas une cellule confirmée. Si `MapCurrentEvent`/snapshot cellulaire manquent, l'accès doit rester diagnostiqué comme non certifié dans la couche appelante.

**Chemin raccourci par le serveur :** le handler existant de `LaunchService.Packets.cs` n'appelle le fallback que si la dernière cellule de l'écho est égale à la destination demandée. Un chemin réellement raccourci peut donc ne pas entraîner de JQI et requiert une évolution coordonnée du handler, avec preuve de l'identité de l'acteur et recalage de cellule. Cette condition n'est pas affaiblie ici pour ne pas confirmer la marche d'un autre acteur ou inventer un résultat.

**Fin de session et ACK :** la perte de la connexion peut retirer `_botMoveConfirm` dans `ForgetSession`. Les envois JQI différés revérifient ce marqueur avant envoi. L'absence d'ACK reste observable comme telle ; le code ne simule ni arrivée, ni `MapMovementConfirmResponse`.
