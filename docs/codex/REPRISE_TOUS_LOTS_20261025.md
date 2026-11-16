# Reprise de tous les lots F01–F16 — 25 octobre 2026

Branche fix/f01-f16-final-20261024, après 34c81ce. Suivi repris intégralement ; aucune déclaration de clôture globale. Les preuves antérieures de source, tests simulés et jeu réel restent distinctes. Les anciens rapports enfants signalant des compilations interrompues décrivent leur créneau historique ; les validations de l’intégrateur les remplacent pour le code livré. PID6740 reste une ancienne version, pas une preuve du binaire courant. Foragerbot ne doit pas être rétabli.

## État consolidé

| Lot | Preuve acquise | Critère restant |
|---|---|---|
| F01 Diagnostic/MCP | Journaux relus, sélection par ID exact, accepté distinct de confirmé ; ordres et résultats suivis via pipe isolé | Conserver la rotation et les erreurs de reconnexion dans les régressions |
| F02 Fiche/équipement | Équipement serveur, pods et effets ; montée Crâ 15→16 observée | Observer la répartition automatique des points à une nouvelle montée |
| F03 Combat | Buff de portée serveur en Socket puis MITM, portée 8 au lieu de 6 ; victoire réelle | Autres classes, cible invisible, refus et dissipations |
| F04 Turbo/défis | Identifiants réutilisés couverts ; combat MITM de trois tours sans expiration | Défis de zone, mesure comparative répétée, suppression AFK explicite en réel |
| F05 Expédition | Limite de combat, pods, zone vide, défaite : arrêts réels ; groupes accessibles et cartes filtrés | Parcours complet et exclusion temporaire après échec observée en jeu |
| F06 Déplacements | Annulation propriétaire, portes génériques et refus courts corrigés/testés ; trajets réels partiels | Relecture enregistrée complète, portes/zaaps/refus mesurés sur la version courante |
| F07 Lua | 977 liaisons inventoriées, aucune signature documentée absente ; API indisponibles explicites | Implémenter les API actuellement indisponibles avec données/protocole suffisants, tester davantage de scripts sauvegardés |
| F08 Banque/échanges | Équipement réel ; banque et reçus testés ; validation sans contenu confirmé désormais refusée | Aller-retour bancaire réel en cours ; échange joueur/mule simultané restant |
| F09 Métiers/craft | Multi-piles, recette invalide, aucun retry après Ready ; nouveaux accusés corrélés UID/quantité/côté et compte exact | Récolte/craft complet en jeu, ressource épuisée et progression métier |
| F10 HDV/économie | Prix fractionnaires exacts et corrélation des ventes testés | Chaîne réelle achat/vente/taxes/repricing ; aucune transaction importante lancée |
| F11 Trésor | Navigation annulable, reprise directionnelle, retour coffre et refus testés sur Lua généré | Une chasse complète récompensée, indice inconnu et reprise réelle |
| F12 Forge | Résultats vides/puits inconnus et budgets testés ; pose corrélée à l’objet attendu | Atelier réel, UID de résultat, exclusion globale des autres dialogues/transactions |
| F13 Équipes/planning | Attentes strictes, génération de combat, occurrences de planning et jointures testées | Regroupement réel de plusieurs comptes compatibles et retrait d’un membre |
| F14 Connexion | Rangerbot sélectionné réellement en MITM ; demandes liées au client avant hook, invalidées à la fermeture | Choix serveur au lancement toujours explicitement non câblé ; incidents réseau/planning réels |
| F15 Natifs | Six masques indépendants actifs en combats gagnés et retour DISABLED | Tactique en échec MapRenderer ; timings/restauration visuelle détaillée et F8 non validés |
| F16 UI/persistance | Export sans secrets dérivés, persistance et 2 701 clés par langue testés ; captures client possibles | Revue visuelle de chaque écran, notifications/raccourcis et changement de bot |

Preuves de jeu détaillées : [MITM_VALIDATION_20261024.md](MITM_VALIDATION_20261024.md). Corrections antérieures : [F06_F16_INTEGRATION_20261024.md](F06_F16_INTEGRATION_20261024.md). Le tableau initial reste dans [TODO_FEATURES_20261024.md](TODO_FEATURES_20261024.md), actualisé avec les preuves récentes.

## Défauts corrigés pendant cette reprise

### F09/F12 : accusés d’atelier

La pose attendait n’importe quel ExchangeObjectsAdded/ModifiedEvent du PID. Un autre UID, une quantité différente ou le côté distant pouvait autoriser le nombre de crafts, le Ready ou la première rune. L’attente vérifie maintenant le côté local, l’UID et la quantité exacts, en acceptant les représentations ObjectItem et ObjectItemInventory. Une repose après resynchronisation ne retire plus le marqueur d’échec sans accusé correspondant ; elle arrête avant Ready si la confirmation manque. Le nombre de crafts doit correspondre exactement à la demande, plutôt qu’à la seule présence de CountModified. Les erreurs et fermetures restent des refus, pas des confirmations de pose.

Régressions exécutant la source Craft avec transport simulé : autre UID, quantité différente, côté distant, nombre serveur différent, nombre exact autorisé, objet direct, multi-piles, aucun résultat après validation. Pas de consommation de rune ni de craft réel revendiquée.

### F08/F13 : contenu d’échange non confirmé

SettleTradeStep expirait au bout de 1,5 seconde puis la validation partait quand même. Il retourne maintenant false si le contenu attendu n’est pas confirmé, si la carte change, si l’échange se ferme ou s’il est remplacé. Les chemins prêt simple, prêt bloquant et retrait tiennent compte de cet échec. Le Ready différé d’une mule recontrôle l’activation auto-ready et l’identité exacte de l’échange : auto-accept conservé avec auto-ready désactivé n’autorise plus un envoi tardif.

Régressions sur le vrai service sans transport : contenu absent/partiel/complet, échange vide valide, fermeture, mauvaise carte, session inconnue, option désactivée et échange remplacé. L’exclusion globale de toutes les opérations HDV/banque/forge simultanées reste ouverte ; cette correction ne prétend pas la fournir.

## Validation du code

Build application Release réussi. Suite complète : 2 968 réussis, 2 ignorés, 0 échec. Le manifeste de champs protocole requis a été régénéré puis la suite reconstruite. Aucun journal complet, identifiant de connexion ou secret livré.

## Essai réel F06/F07/F08, 25/10 à 09:53–09:54 (journal UTC 08:53–08:54)

Client MITM Rangerbot PID7088. Trajet Lua vers la banque intérieure 192415750 : arrivée par zaap 191105026 confirmée, bord vers 191104002, puis porte type32 cellule288. Arrivée serveur à 08:54:04.009 UTC. Ouverture banque confirmée à .109, dépôt puis retrait d’une Bave de Bouftou : sac 33→32→33, banque 0→1→0. Retours deposit=true, withdraw=true, close=true ; fermeture et fin du script à .292. Aucun dépôt global ni achat. Ce test valide un aller-retour partiel et ce trajet, pas tous les refus bancaires ni les transferts de kamas.

F09 : find_resource_maps(Blé) depuis l’intérieur de banque a retourné une liste vide malgré le catalogue de ressources présent. Filtrage de monde intérieur à investiguer ; aucune carte de ressource inventée. Une récolte réelle n’est pas encore revendiquée dans cette reprise.
