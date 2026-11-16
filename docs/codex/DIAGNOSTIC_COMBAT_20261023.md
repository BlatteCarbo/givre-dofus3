# Turbo MITM, tours écourtés et défis : diagnostic pour Claude

## Confirmation dans les journaux réels et correctif appliqué

Accès aux journaux obtenu avec la reprise administrateur. Dans `transport-2026-10-23_19-04-53.log`, PID MITM 14172 : 182 demandes, 152 barrières fermées et 58 **traces** d’expiration (plusieurs traces peuvent correspondre à une même demande). PID Socket 1073741825 : 107 demandes, 102 fermetures, 5 traces d’expiration. Les accusés des barrières MITM normalement fermées suivent la fin de séquence en 0–1 ms. Ces comptes ne sont pas des nombres de combats ou des taux d’échec.

Exemple MITM : action 31 accusée à 19:20:36.028 ; nouvelle demande à 19:20:45.185, nouvelle fin d’action 31 à 19:20:45.186, mais pas de nouvel accusé du bot pour 31 ; expiration à 19:20:46.694 puis seconde chance. Le test global `_turboAcked.ContainsKey((pid, actionId))` dans `LaunchService.Packets.cs` confondait une occurrence historique avec celle de la séquence courante. L’identifiant est réutilisé.

**Correctif appliqué hors fichiers réservés** : `TurboSequenceAckPolicy.ShouldInject` compare l’horodatage du dernier accusé à la borne de la requête/séquence actuelle. Un ancien accusé ne bloque plus le nouvel envoi ; un accusé déjà fourni par le client pour cette séquence reste respecté. Sans aucune borne de génération, le comportement conservateur est maintenu. Huit tests couvrent ces cas.

Ce correctif traite une cause concrète d’attentes. Il ne prouve pas la disparition de toutes les expirations et ne change pas les barrières de sécurité ni les heuristiques Nomade/Tueur à vue. Refaire un combat MITM comparable et rechercher des accusés réémis lorsque les numéros sont réutilisés.

Audit de la référence `4065ec0`, sans modifier les fichiers de combat réservés. Cas signalé : Sacrieur, Déchaînement (zone), Tueur à vue et Nomade. Les observations ci-dessous viennent du code ; elles ne constituent pas la reconstitution d'un combat réel.

La branche de Claude a avancé pendant cet audit : `a730467` corrige le choix de case par distance de marche et introduit un abandon des défis de mouvement après trois tours sans attaque ; `e4b57fd` concerne ensuite la forgemagie. Ces commits ne sont pas réécrits ni fusionnés ici. Avec cette version, rechercher aussi `défi de mouvement abandonné` : un échec de Nomade peut être volontaire après ces trois tours, ce qui est différent du défaut d'anticipation du coup de grâce décrit plus bas. Le repli Tueur à vue et l'heuristique du coup de grâce de zone n'ont pas changé dans ce diff.

## Limite d'accès aux traces

Les outils natifs Codex renvoient `Accès refusé` sur `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/logs`. Aucune trace réelle de la session récente n'a pu être lue. Les seules traces trouvées dans les dossiers de développement sont des fixtures et des journaux de compilation ; elles ne sont pas utilisées comme preuve d'un combat utilisateur. Les fichiers `MizanBot/NativeTurbo/*/combat-speedhack.restore-request` sont lisibles mais ne contiennent pas les séquences de combat.

Ajout utilisable après intégration : Console → Technique → **Analyser les attentes de combat**, qui lit l'historique du personnage en mémoire. Il affiche requêtes, attentes résolues, expirations, prolongations, maxima, demandes de fin de tour après expiration et erreurs natives. Une expiration suivie d'une fermeture tardive n'est pas comptée comme une fin de tour. L'historique est borné : pas de conclusion sur les lignes déjà élaguées.

## 1. Lenteur Turbo MITM : divergence certaine du code, causalité à vérifier sur session

- `Services/LaunchService.Combat.Turn.cs`, `WaitAfterCast` : Socket attend une barrière pendant 1 200 ms, puis continue même si la méthode retourne `false`. MITM attend `WaitTurboActionBarrierWithGrace` et lève une exception si elle reste ouverte.
- `Services/LaunchService.Combat.cs`, `TurboBarrierTimeoutMs` et `WaitTurboActionBarrierWithGrace` : MITM avec accusés du bot = 1 500 ms puis 3 500 ms supplémentaires ; sans accusés du bot = 5 000 ms puis 3 500 ms. Une séquence retardée peut donc consommer 5 s ou 8,5 s.
- Le `catch` final de `RunFightTurn` finit le tour si sa fenêtre est encore ouverte, sans journaliser l'exception. Cela explique un chemin possible « attend longtemps, puis passe son tour » ; ce chemin doit être confirmé avec les lignes du combat concerné.

À regarder ensemble : `[turbo-metric] request_tx`, fins de séquences, `barrier_closed`, `BARRIERE NON FERMEE`, `seconde chance`, puis `fin du tour (FightTurnFinishRequest)`. Une attente totale élevée avec `sequence_end_to_ack_ms` faible pointe vers le temps **avant** la fin de séquence ; un délai élevé après cette fin pointe vers les accusés. Vérifier aussi les messages `[turbo-native]` et `[mod]` de la session : demander le mode Turbo ne prouve pas que le module natif est chargé et actif.

Correctif à intégrer dans les fichiers réservés : journaliser le motif d'arrêt du tour et les données de corrélation de la barrière. Ne pas remplacer la barrière MITM par une poursuite aveugle du chemin Socket : cela pourrait injecter l'action suivante alors que la précédente n'est pas terminée.

## 2. Tueur à vue : cible de repli conservée entre les tours

`Services/LaunchService.Combat.Challenges.cs` : `ChallengeFightState.Sight` vit pendant tout le combat. Dans `RefreshTargets`, le repli `TueurAVue` conserve cette cible tant qu'elle est vivante. Il n'existe aucun reset de `Sight` au début d'un nouveau tour. Quand le serveur fournit une cible désignée vivante, cette branche est contournée.

Or la cible doit être déterminée à chaque début de tour, puis lorsqu'elle est achevée. Le repli peut donc rester sur l'ennemi du tour précédent alors qu'un autre est devenu le plus proche. La [description de référence](https://www.dofuspourlesnoobs.com/les-challenges.html) confirme cette distinction avec Focus.

À intégrer par Claude : estampiller la cible de repli avec l'époque du tour, conserver la cible pendant les déplacements du même tour et la recalculer à la mort depuis la position actuelle ; au tour suivant repartir de la position de début de tour. Les cibles désignées par le serveur restent prioritaires.

Scénario : tour 1, A le plus proche ; tour 2, A vivant mais B plus proche. Le tour 2 doit viser B. Si B meurt et qu'il reste des PA, choisir la cible suivante du même tour. Inclure les égalités et l'arrivée d'une cible désignée serveur dans les tests.

## 3. Nomade + sort de zone : anticipation incomplète du dernier coup

`Services/LaunchService.Combat.Turn.cs` : le déplacement imposé avant un possible coup de grâce est déclenché si `enemyCountTurn == 1` **ou** si un ennemi est à 30 % de PV ou moins. Ce test manque le cas de plusieurs ennemis encore au-dessus de 30 % que Déchaînement peut tous tuer dans sa zone. Le déplacement de fin de tour arrive alors trop tard, puisque le combat est terminé.

À intégrer par Claude : avant chaque lancement offensif susceptible de terminer le combat, vérifier les ennemis réellement dans la zone et leurs PV absolus par rapport aux dégâts prudents. Dépenser les PM requis avant un tel lancement, puis recalculer portée, orientation et sécurité de la zone depuis la nouvelle position. Tenir compte des déplacements déjà confirmés, des pertes de PM et du tacle ; ne pas créditer les PM d'un déplacement simplement demandé ou refusé.

Scénario de régression : 2 ou 3 ennemis à 100 % de leurs PV, tous tuables par Déchaînement ; Nomade actif et PM restants. Le mouvement exigé doit précéder le lancement. Inclure un déplacement refusé, un tacle et une perte de PM.

## 4. Sécurité des zones : filtre présent, données à vérifier

`ChallengeZoneOk` vérifie déjà **tous les ennemis** dans la zone calculée. Ne pas diagnostiquer à tort un simple contrôle de la cible centrale. En revanche, `GameNameResolver.SpellZone` conserve une seule forme, celle du plus grand rayon parmi les effets ; en l'absence de zone, le filtre de défi retombe sur la seule case centrale. Vérifier la forme, l'orientation, le rayon, le grade appris et les éventuels effets déclenchés de Déchaînement dans les vraies données de la session avant de modifier cette logique.

Vérifier aussi `ChallengesAbandoned` : le profil peut explicitement abandonner les défis sous un seuil de PV. Ce cas est journalisé et doit être distingué d'un défaut de ciblage.

## Correctifs livrés hors fichiers réservés

La sélection de groupes avant combat ignore désormais les groupes géométriquement inaccessibles et cherche les autres groupes éligibles, en conservant le comportement existant si la géométrie est encore inconnue. Les filtres du script d'expédition et du donjon sont explicitement réinitialisés. Tests sur une carte avec un groupe proche isolé et un groupe plus éloigné accessible, limites de déplacement et géométrie inconnue.
