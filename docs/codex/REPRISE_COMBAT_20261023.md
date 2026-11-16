# Reprise combat du 23 octobre 2026

Base : `65948fb`, branche `fixes-integration-20261023`, worktree isolé `GivreWork-Fixes-20261023`.
Claude a échoué avant toute modification (limite API). Le parent reprend exclusivement son lot combat ; les deux agents Codex restent sur Expédition/scripts et natifs/UI dans leurs propres worktrees. Aucun changement des worktrees historiques.

## Correctifs

- Tueur à vue : la cible de repli est estampillée par époque de tour. Elle reste verrouillée pendant les déplacements du même tour, se renouvelle à sa mort depuis la position actuelle et au tour suivant. Une désignation serveur reste prioritaire. Les égalités sont déterministes. La politique testée est utilisée par le moteur.
- Zombie/Nomade : l'anticipation du coup de grâce couvre aussi plusieurs ennemis encore à 100 % de PV mais tous dans la zone d'un sort abordable et sous son estimation basse de dégâts (marge de 10 %). Le déplacement préalable ne déclenche plus le chemin de relance de sorts avant la rotation normale ; portée, zone et positions sont relues ensuite. Les anciennes heuristiques sont conservées.
- Les erreurs qui interrompent un tour sont journalisées avec PID, époque, fenêtre et compteur de séquences ; la garde empêchant de finir un nouveau tour depuis un ancien run reste en place. Les barrières Turbo ne sont pas contournées.
- Les scripts n'annoncent plus un combat terminé lorsque l'attente de dix minutes expire sans fin de combat serveur. Aucun faux horodatage de fin n'est créé. Message traduit dans les trois langues.
- Complément d'intégration : l'anticipation est réévaluée entre les attaques, y compris les reprises après approche et le filet final. Les positions sont relues après le mouvement préalable et entre les relances ; les PM déjà marchés ne sont plus proposés une seconde fois à la rotation.

## Limites

L'estimation de dégâts ignore les résistances, états et effets conditionnels : elle déclenche un mouvement prudent, ce n'est pas une garantie de victoire. L'anticipation des zones regarde les centres ennemis ou soi, pas chaque case vide possible. Les exigences Nomade restent non garanties en cas de tacle, déplacement refusé ou zone inconnue. Les mouvements sont comptés après changement de position confirmé, jamais sur la seule demande.

Observation locale en lecture seule : l'exécutable Correctifs est lancé (PID 652). Le pont local retourne un bot Socket en jeu, script actif, hors combat ; aucun bot MITM connecté. Le journal de cette session est vide. Aucune preuve nouvelle de combat réel, aucune injection de test ni interruption de la session active.

## Validation

Tests comportementaux ajoutés : maintien/changement de cible entre tours, mort, désignation serveur tardive, égalités, position inconnue ; dégâts de zone sur plusieurs ennemis, PV inconnus, seuil exact, ennemi hors zone.
Build application Release : réussi, 0 erreur (206 avertissements existants). Première suite : 2 555 réussites, 1 échec réseau intermittent dans `LocalZaapAuthorityTests.Unknown_hash_is_refused_and_gets_no_token`, 2 ignorés. Ce test réussit isolément ; seconde suite complète sans recompilation : **2 556 réussites, 0 échec, 2 ignorés, 2 558 au total**. Les neuf nouveaux cas passent. Résultats TRX conservés hors dépôt dans `GivreWork-Codex-validation-20261023/reprise-combat`. Clés de langue contrôlées avant commit, aucun doublon.
