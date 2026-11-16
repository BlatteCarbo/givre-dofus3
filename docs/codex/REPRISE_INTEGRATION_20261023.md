# Intégration de la reprise du 23 octobre 2026

Worktree `GivreWork-Fixes-20261023`, branche `fixes-integration-20261023`, base `65948fb`. Le parent est seul intégrateur et éditeur de la publication. Les worktrees historiques et leurs modifications sont conservés ; aucun nouveau commit sur `integration-opus-20261020` au démarrage.

## Lots réunis

- Combat du parent, commit `1337052`, puis complément d'intégration : renouvellement de Tueur à vue, anticipation du coup de grâce de zone avant les attaques, relecture des positions et budget de mouvement après les déplacements imposés, journalisation des interruptions de tour, fin de combat script confirmée par le serveur.
- Codex A, commit source `ca626ba` : sur carte unique, attente bornée avec contrôle des limites temps/anomalie/combats au prochain tick ; sortie de la boucle multi-cartes au seuil de pods pour rendre la main à la banque ; reconstruction des cartes et monstres lors du retrait de sous-zones ; calcul de distance sans débordement entier.
- Codex B, commit source `26bc939` : précontrôle du bootstrap sous son vrai nom livré (`DofusNativeBootstrap.dll`) ; actualisation des capacités et statuts visuels au changement de bot ou de permission.
- Claude : échec avant modification, limite API. Aucun travail attribué à Claude n'est annoncé livré ; le parent a repris le combat.

Les commits des agents sont appliqués sans commit automatique, puis l'ensemble est compilé et testé avant le commit d'intégration. Aucun code de master n'a servi de base.

## Vérifications et limites

Version finale, y compris correction du budget de PM : build Release réussi (0 erreur, 206 avertissements existants), puis **2 563 tests réussis, 0 échec, 2 ignorés (2 565 au total)**. Résultats structurés hors dépôt : `GivreWork-Codex-validation-20261023/reprise-integration/reprise-final.trx`. Clés fr/en/es uniques contrôlées avant commit.

Les nouveaux tests couvrent les politiques de cible et de dégâts de zone, les scripts générés exécutés avec hôte simulé, le catalogue installé en lecture seule et les vrais binaires natifs déclarés dans le projet (empreintes, absence, altération, restauration de fichiers). Ils ne prouvent pas l'effet des options dans Dofus ni une opération bancaire acceptée par le serveur.

Session active observée par processus et pont local en lecture seule : version Correctifs, un bot Socket connecté avec script actif, pas de MITM connecté. Aucune interruption, aucun remplacement de la publication active. Le test réel tactique/options, restauration et accusés Turbo MITM reste à réaliser sur un client graphique.

Point restant identifié par Codex A : après expiration de l'attente d'équipe, `FightOne` peut engager sans tous les suiveurs. Le comportement historique est explicitement borné ; il faut vérifier la stratégie de regroupement et la reprise du trajet avant de modifier ce contrat. Aucun correctif silencieux de ce comportement n'est inclus.

La portée de cette livraison est celle des défauts ci-dessus. Il ne s'agit pas d'une certification que toutes les fonctions du projet sont complètes ou exemptes de bugs.

## Reprise Codex : verrou des déplacements Socket

Le traitement inachevé des chemins serveur raccourcis est terminé : confirmation d'un préfixe exact du chemin demandé, cadence sur le chemin accepté, annulation des actions liées à une destination non atteinte et résultat Superseded pour le script. La distinction reste conservée lorsque le client confirme lui-même avant l'accusé serveur. Les autres chemins, séquences ou cartes restent rejetés.

Validation : build Release réussi ; 2 569 tests réussis, 0 échec, 2 ignorés ; harness réel LaunchService : 11 scénarios réussis, dont chemin raccourci, absence de faux changement de carte et déplacement suivant débloqué après ACK. Clés de langue uniques. Validation du cas réel carte 191102980 en Socket encore nécessaire.

## Combat : reprise des travaux interrompus et raccordement de la zone

- Intégration sélective du correctif de zone proposé par Claude (`ef4d337`/`565763e`) : GameNameResolver utilise désormais SpellZoneSelection, lit targetMask et privilégie les effets élémentaires. Déchaînement conserve son carré de dégâts au lieu du point sur le lanceur. Les tests couvrent le cas réel et les priorités de sélection. Le modèle conserve une seule zone par grade ; les sorts à plusieurs zones distinctes restent une limite.
- Reprise du lot Codex non committé du worktree combat-review-safety-20261023 : budgets de lancers avant anticipation d'un coup de grâce, contrôle de cible, tentatives de mouvement dédupliquées tant que la situation ne change pas, styles de fin de tour bornés par les PM restants. Adaptation locale : les PM serveur ne bornent le budget que lorsque leur lecture au début du tour était connue, afin de conserver le repli existant sans statistiques courantes.
- Correction directe de TurboFightPolicy.BarrierClosed : un accusé plus ancien que la requête ou le début de séquence ne ferme plus la barrière MITM, même si l'identifiant d'action est réutilisé. La récupération supplémentaire proposée par Claude n'est pas intégrée dans cette livraison.
- Le changement de retour de FightOne proposé dans le lot non committé n'est pas repris : le message de fin fictive reste supprimé et le timeout continue de retourner false.

Validation de l'ensemble : build Release réussi, 2 588 tests réussis, 0 échec, 2 ignorés. Aucun changement de langue, aucune base remplacée. Aucun combat réel exécuté par cette reprise. Les autres modifications des worktrees et de master ont été conservées.

## Expédition Astrub sans engagement : correction du choix des cartes

Retour réel : le changement de carte fonctionne, mais aucun combat n'est lancé. Version 859132e confirmée en cours d'exécution (lecture du chemin du processus). Le pont local confirme un script Socket actif ; le journal contient des arrivées aux ateliers 192939016, 83886090, 192414728 et 192937990, sans tentative d'attaque.

Cause : CleanMaps élisait le calque majoritaire sans distinguer worldMap=-1 des vrais calques de l'atlas. La sous-zone 95 possède 86 cartes hors atlas contre 78 sur le calque 1 : le nettoyage retenait donc les intérieurs puis dédupliquait leurs coordonnées, excluant les extérieurs où sont les monstres.

Correctif : lorsqu'un calque atlas non négatif est connu, seuls ces calques participent au choix. Le repli historique est conservé si tous les calques sont inconnus/hors atlas. Les filtres de monstres configurés sont conservés. Aucun arrêt ni ordre envoyé à la session active pendant le diagnostic.

Validation : build Release réussi, 2 591 tests réussis, 0 échec, 2 ignorés. Trois nouvelles régressions : majorité d'intérieurs, repli sans atlas, et route Astrub sur la base installée en lecture seule (extérieurs 191102980 et 188745222 conservés, ateliers visités exclus). Clés de langue uniques. Le lancement réel d'un combat après correction reste à vérifier.

## Turbo : supprimer les 1,2 s perdues sur un sort refusé pour manque de PA

Trace réelle 23:12:58–23:13:02 : trois TextInformationEvent 170 (PA disponibles 2, PA requis 3), chacun reçu 20–35 ms après le lancer, mais ignoré par la barrière. Le moteur attendait alors 1 200 ms pour une fin de séquence qui ne vient pas pour un lancer refusé. Les ACK des séquences acceptées partaient en 0–1 ms.

Correctif : la notification serveur 170, avec deux paramètres PA valides, termine l'attente du lancer courant et réveille son signal immédiatement. Le refus est corrélé au PID et à la requête de sort en attente, ne fabrique ni fin de séquence ni accusé, et ne résout pas un déplacement. La nouvelle requête et la remise à zéro du combat effacent ce refus. Le budget local de PA est recalé sur les PA annoncés par le serveur, pour ne pas retenter les sorts trop chers. Le suivi du lancer est armé avant le dispatch pour accepter aussi un refus reçu durant Inject ; un dispatch refusé n'arme pas d'attente serveur.

Les barrières des actions sans réponse explicite restent conservées. Cette livraison cible le manque de PA attesté par les logs ; elle ne classe pas arbitrairement tous les messages système comme des refus de sort.

Validation : build Release de l'application réussi ; harness réel LaunchService : 13 scénarios réussis, dont réveil sur refus après début d'attente, refus avant attente/durant dispatch, message étranger ignoré, aucune propagation au prochain sort ni au déplacement, aucune création d'ACK. Les tests purs couvrent les paramètres valides, zéro PA, messages incomplets/étrangers et nombres invalides. Clés de langues uniques.

Suite complète finale : 2 600 réussis, 0 échec, 2 ignorés. Validation des nouveaux temps en combat réel encore nécessaire.
