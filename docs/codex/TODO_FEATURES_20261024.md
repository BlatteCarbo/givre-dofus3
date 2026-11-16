# Fiabilisation du bot, fonctionnalité par fonctionnalité

Priorité utilisateur du 24/10 : corriger et raccorder les fonctionnalités. Progression niveau 200 en pause ; script de farm arrêté et arrêt confirmé par MCP. Des combats ponctuels restent utiles comme essais, sans objectif de farm.

Référence actuelle : branche `fix/f01-f16-final-20261024`, worktree dédié ; essais isolés `port-validation-20261024`, pipe `GivreWork-Port-20261024`. PID6740 conserve une ancienne version et ne sert pas à valider le nouveau code. Foragerbot ne doit pas être rétabli. Voir [reprise de tous les lots](REPRISE_TOUS_LOTS_20261025.md).

## Reprise d’intégration du 24/10

Worktree courant : `fix-f01-f16-final-20261024`, base `6ca99c9`. Livraison F13 et lots F09–F16 relus et intégrés ; correctifs transversaux F06/F07, craft multi-piles, reprise de chasse et budget global de forge ajoutés. Preuves, tests et limites : [F06_F16_INTEGRATION_20261024.md](F06_F16_INTEGRATION_20261024.md). Les statuts distinguent corrections de code et confirmations réelles ; F01–F16 ne sont pas tous clos.

## Règle de validation

Pour chaque lot : reproduire → suivre UI/MCP jusqu'au traitement et au message serveur → corriger → tester succès et refus → vérifier arrêt/reprise et sauvegarde → compiler l'application et lancer les tests adaptés. Une option visible, une commande acceptée ou un test unitaire vert ne suffit pas à déclarer une fonctionnalité opérationnelle. Consigner une preuve et les limites de chaque validation. Les éléments ci-dessous sont des travaux à vérifier, pas une déclaration que tous ces modules sont cassés.

## Ordre des lots

| Lot | Fonctionnalité | Travaux et critère de sortie | État |
|---|---|---|---|
| F01 | Diagnostic et commandes MCP | Comprendre les journaux disque vides, retrouver des traces fraîches par compte ; rendre les erreurs finales de connexion et d'action visibles ; distinguer accepté, exécuté et confirmé. Vérifier rotation et reconnexion. | Terminé — preuves F01_VALIDATION_20261024.md |
| F02 | Fiche du personnage et équipement | Vérifier PV actuels/max, érosion, caractéristiques, pods et points dépensés. Identifier les changements d'équipement observés sans ordre explicite pendant nos essais. Vérifier effets réels et bonus de panoplie. | Terminé — preuves F02_VALIDATION_20261024.md ; auto-répartition au passage de niveau à observer |
| F03 | Combat de base | Placement légal, cible actuelle, portée/ligne de vue, PA/PM, poussée/téléportation, dégâts, mort, fin de tour et résurrection. Tester aussi refus et cible invisible, sans boucles de lancers sur une ancienne cellule. | Buff de portée du Crâ confirmé en MITM et Socket ; annulation testée ; autres classes, refus et cibles invisibles à poursuivre |
| F04 | Turbo et défis | Réutilisation des identifiants de séquence, absence de blocage entre tours ; Tueur à vue, Nomade et sorts de zone. Comparer Socket/MITM sur scénarios bornés. | Combat MITM de trois tours sans expiration ; garde AFK en attente de preuve réelle, défis de zone et comparaison statistique ouverts |
| F05 | Expédition | Catalogue, filtres et taille des groupes réellement respectés ; carte accessible, groupe atteignable, engagement confirmé ; exclure les cartes réservées au combat des parcours ; arrêt sur limite, pods, défaite et zone vide. | Limites combat, pods, zone vide et défaite confirmées en réel ; exclusion des groupes sans réponse testée hors ligne, parcours complet ouvert |
| F06 | Déplacements et trajets | Cellule serveur comme référence, refus et recalage, chemins alternatifs, changements de carte, portes/interactifs, zaaps ; arrêt/remplacement de script pendant un déplacement ; enregistreur de trajet et relecture. | Trois défauts prouvés corrigés dans le code ; nouvelles mesures en jeu restantes |
| F07 | Scripts Lua | Inventorier les API annoncées, réellement branchées et documentées comme stubs ; contrôler hooks, événements, délais, arrêt, reprise et erreurs. Les appels non implémentés doivent être explicites. Tester scripts sauvegardés et éditeur. | Audit intégré ; annulation propagée au combat et aux actions différées ; API indisponibles explicites, validation réelle restante |
| F08 | Inventaire, banque et échanges | Équiper/utiliser/déplacer un objet avec retour serveur ; quantités et effets réels ; dépôt/retrait, kamas, ouverture/fermeture, banque dans un trajet ; refus propres et absence de perte d'état. | Équipement confirmé en jeu ; validation des échanges après timeout corrigée le 25/10 ; aller-retour bancaire partiel confirmé en réel ; échange joueur restant |
| F09 | Récolte, métiers et craft | Niveaux métiers fiables, ressource présente et accessible, compétence disponible, récolte/craft confirmé ; recettes, quantités, manque de ressources, pods pleins et retour banque. | Multi-piles testé ; UID/quantité/côté et nombre de crafts corrélés aux accusés le 25/10 ; métier complet réel ouvert |
| F10 | HDV et outils économiques | Prix et fraîcheur des données, achat/vente par quantité/UID, lots et taxes, inventaire de vente, remise en vente ; séparer conseil estimé et opération réelle. Aucun achat important ajouté au programme sans besoin explicite. | Prix fractionnaires corrigés ; chaîne HDV achat/vente/remise en vente non validée intégralement |
| F11 | Chasses au trésor | Départ, indices, recherche de carte, déplacements, validation, combat et récompense ; traiter indice inconnu, échec et reprise. | Navigation et Lua direction corrigés et testés ; chasse complète récompensée restante |
| F12 | Forgemagie | Vérifier ouverture de l'atelier, objet/runes, effets et puits, choix et application des runes, résultat serveur, limites et arrêt. Coordonner les fichiers réservés avant modification. | États et budgets testés ; pose corrélée à l’UID/quantité/côté le 25/10 ; atelier réel et concurrence globale ouverts |
| F13 | Équipes et planning | Chef/suiveurs, rejoindre le combat, placement/prêt, déplacement de groupe, départ d'un membre ; horaires, déconnexion/reconnexion et lancement de script ; aucune action sur un mauvais compte. | Planning et regroupement strict après timeout corrigés et testés ; sélection MITM isolée par client ; regroupement réel ouvert |
| F14 | Connexion et comptes | Choix personnage/serveur, Socket/MITM, erreurs finales, reconnexion après incident ; abonnement lu depuis une réponse réelle. Ne pas réacheter un abonnement pour refaire un test. | Sélection explicite Rangerbot confirmée en MITM ; demandes liées au client exact, PID réutilisé couvert ; choix serveur non câblé |
| F15 | Modules natifs | Tactique et six options d'animation : activation individuelle, effets visibles, disponibilité selon mode/PID, changement de profil, désactivation, restauration et déchargement. | Six masques actifs en combats gagnés, retour DISABLED ; timings visuels partiels. Tactique échoue : MapRenderer non résolu |
| F16 | Interface et persistance | Boutons et commandes branchés, états indisponibles explicites, filtres/listes, infobulles, traductions, thèmes, raccourcis, sauvegarde/rechargement et changement de bot. Inclure tableau de bord, notifications et réglages. | Sauvegardes testées, 2 701 clés uniques par langue ; captures client disponibles, audit de tous les écrans ouvert |

## Corrections déjà acquises, à conserver dans les régressions

- Zones orientées : `CombatZones.OrientationTo` normalise les diagonales. Au contact, la zone en T du Crâ (32443) devenait une croix incluant le lanceur ; le sort était refusé quatre tours de suite, avec défaite le 24/10 à 11:02. Deux combats réels à 12:39 sont gagnés, dont un défi Nomade réussi.
- 1er combat d'une session : le tour attendait tout `EnsureLoaded`, soit 30 s seul et 1 à 3 min client compris ; 43 s de blocage le 24/10 à 12:47. Les sorts ont désormais leur propre chargement parallèle, `EnsureCombatLoaded`. À 13:04, le tour est joué en 95 ms, avant la fin du chargement complet. Les temps par étape sont journalisés : `[données] chargement`.
- Déplacement de défi avant le coup de grâce : préférer une case d'où un sort peut finir ou toucher. Couvert par build/tests ; non encore observé en réel, faute de Nomade proposé avec coup de grâce possible.
- Poussées/attirances : la trame réelle Slide n'a pas de `start_cell`, et le lecteur l'exigeait. Aucune poussée n'était suivie : les coups partaient dans le vide contre le Poutch, à 13:51. Fuyard hors de portée : il se rapproche désormais. Sort sans séquence : ignoré, le tour continue. Barrière Turbo : horodatage strict, contre la fin de tour envoyée trop tôt. Validé en réel le 24/10 à 14:09. Poutch 1 500 PV : poussée suivie (233→219), rapprochements 13→10→7, dégâts à chaque tour et victoire. Aucune barrière bloquée.
- Défis : chances apprises par personnage (`challenge-stats.tsv`). La nuit précédente, Statue a échoué 13 fois sur 14 et Nomade 12 fois sur 14.
- F08 (Codex, `55adbc7`) : équipement, usage, banque et kamas confirmés par les événements serveur. Validé en réel le 24/10 à 16:4x sur Rangerbot. Déséquiper les bottes renvoie confirmed, et resultUid désigne la pile fusionnée (38→39, pods max 2865→2815). Rééquiper renvoie confirmed, avec un nouvel UID 5311418 et les pods max revenus à 2865. Les scénarios banque restent à jouer en jeu : dépôt/retrait partiel, kamas, retour banque en trajet.
- F06 partiel (Codex, `5a20f0e`) : un refus de déplacement donne une position confirmée avant de réveiller le script. Coupe-circuit des trajets refusés en boucle (`ee241e1`).
- Fiche : privilégier le niveau actualisé pendant la session, au lieu du niveau de sélection (`8377051`). Validation du prochain changement de niveau encore à compléter.
- Placement : listes challengers/defenders remises dans l'ordre (`42ed456`). Un placement réel 276→290 est confirmé, ennemi en 398.
- PV : permutation dégâts/élément/érosion corrigée (`72ca17a`). Perte de 20 PV observée, modèle 123→103 au tour suivant.
- Expédition MCP : même nettoyage des cartes que l'UI ; cela ne filtre pas encore toutes les cartes de combat à coordonnées 0,0.
- Lecture des logs : suppression de la limite aux 100 000 premières lignes ; les tailles Windows périmées avaient été prises à tort pour des fichiers vides ; lecture réelle confirmée.
- Instance isolée et validation réelle des deux correctifs combat (`a2bf838`). Dernière suite : 2 610 réussis, 2 ignorés, zéro échec. Ces résultats ne valident pas l'ensemble du bot.

## Répartition des essais

**Autonome par défaut :** audit du code et des branchements, compilation, tests, MCP, captures ciblées non sensibles, interface disponible, essais courts sur le compte de validation, reproduction, correction et régression. Faire les opérations réversibles et préparer des scénarios concrets avant de solliciter l'utilisateur.

**Intervention utilisateur seulement si nécessaire :** authentification manuelle/MFA/CAPTCHA ; déverrouillage du bureau si requis ; reproduction sur une classe, un sort, un objet ou un accès absent du compte disponible ; observation visuelle si les outils ne peuvent réellement pas accéder au client. Demander alors le scénario exact, les clics nécessaires et le résultat attendu, sans transférer une campagne générale de tests à l'utilisateur.

**Coordination :** respecter les réservations de fichiers existantes et coordonner les modifications du moteur, de la banque, du déplacement, du trésor et de la forgemagie avant édition. Un seul responsable d'intégration/publication. Pas de publication de journaux complets ni de données d'authentification.

## Suivi de chaque lot

Tenir pour chaque bug : reproduction, comportement attendu/observé, cause prouvée ou hypothèse, fichiers/commit, vérification automatisée, essai réel, limites et éventuelle intervention utilisateur. Marquer terminé seulement avec une preuve adaptée au périmètre. Traiter les lots dans l'ordre ; remonter un blocage commun avant de poursuivre les fonctions dépendantes.
