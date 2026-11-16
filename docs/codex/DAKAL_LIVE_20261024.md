# Reprise Dakal — 24 octobre 2026

**F01 clôturé :** voir F01_VALIDATION_20261024.md. Instance courante PID 6092, personnage en jeu PID Socket 1073741827, script arrêté. Reconnexion, erreurs finales Lua et rotation réelle vérifiées ; état script périmé corrigé.

## Reprise F01 à 10:24 — état courant

Instance isolée relancée : PID 24316, même exécutable et canal Dakal. Compte hors ligne, script arrêté ; progression vers 200 toujours en pause. Les PID et états ci-dessous sont historiques.

Diagnostic rectifié : une taille Windows de zéro ne prouvait pas un journal vide. Lecture effective confirmée sur le fichier ouvert. Le MCP privilégie désormais le segment courant de cette instance, même si un ancien segment a des métadonnées plus récentes ; sélection explicite historique conservée. Tests couvrant rotation, historique, repli et refus des chemins arbitraires.

Vérification réelle : get_app_status retourne workerAlive=true, pendingLines=0, lastErrorType=null et un dernier flush ; read_log lit le même fichier transport-2026-10-24_10-23-30.log. list_bots conserve l'identifiant exact du compte hors ligne et connectionStatus=Inactif. Les réponses communes d'ordre ajoutent completionConfirmed=false ; cela ne prouve pas une confirmation finale pour toutes les commandes.

Validation : build application réussi ; 2615 tests réussis, 2 ignorés, zéro échec. Build GivreMcp réussi après restauration NuGet (assets absents). Aucune clé fr/en/es en double. Reconnexion et rotation réelles restent à vérifier ; F01 n'est pas déclaré intégralement terminé.

**Instance cible actuelle :** processus 20280, exécutable du worktree Dakal (`GivreClone.App/bin/Release/net10.0/GivreClone.exe`), stockage privé `TestRuns/dakal-validation-20261024/userdata`, canal `GivreWork-Dakal-20261024`. Helper MCP local : `%TEMP%/givre-dakal-control.py`. Ancien helper `givre-task-control.py` pilote encore l'ancienne instance ; ne pas lui reconnecter Rangerbot pendant que la nouvelle session tourne. Compte cible inchangé `00fe352d06bf491fb5abcc2029ce84d7`, PID Socket actuel 1073741827. Niveau 11 confirmé.

Essai de validation terminé à 10:06:25 : trois victoires, niveau 11, 28 320 XP. Capture retirée, `Rangerbot - Cimetiere prudent` relancé sur le nouveau canal, déplacement confirmé. Dernier état présente 105/119 PV et pods max 2805 contre 123 PV max / 2730 pods max précédemment : variations de caractéristiques à vérifier, sans les attribuer à une cause non observée.

Dernière vérification MCP : niveau 9, 113/113 PV, combat et script actifs, pods 247/2720. Suite après correction de fiche : 2 600 réussis, 2 ignorés, zéro échec (13 s). Clés de traduction fr/en/es uniques (2701/2585/2584).

## Essai cimetière après 09:31

Les gains des Pâturages ont baissé (39 à 359 XP dans les derniers journaux). Essai borné à cinq combats, groupes seuls contenant 4046 (Chafer Débutant), régénération avant engagement. Attention : le nom seul « Cimetière » résout la sous-zone 6 de niveau 25+, utiliser l'identifiant 449 pour Incarnam. `list_expedition_zones` accepte cet identifiant sous forme de texte ; `start_expedition` exige un nombre.

Dans l'instance non mise à jour, l'expédition brute commence par les cartes 153887744/153888002/153888004/153888006, coordonnées 0,0, sans chemin trouvé. Remplacement par un script utilisant seulement les sept cartes extérieures connues (copie dans `scripts/Rangerbot-Test-cimetiere.lua`). Remplacement effectué pendant un déplacement ; refus serveur puis tous les envois suivants échouent. Le lien causal avec l'annulation n'est pas établi. Arrêt du script et reconnexion du seul compte cible : PID Socket 1073741833, cellule serveur 168 confirmée, déplacement et combats rétablis sans autre modification.

Deux premières victoires vérifiées : 09:33:38 (13,5 s, 503 XP), puis bilan 09:34:18 (335 XP, niveau 9, XP totale 16 207). Ne pas extrapoler une progression jusqu'à 200 à partir de ces essais.

Après élargissement aux quatre espèces 4046/4048/4047/4049, cinq victoires sur cinq : 758, 272, 272, 621 et 638 XP. Dernier bilan 09:38:12 : niveau 9, XP totale 18 768. Dernier combat : 27,8 s, quatre tours, deux déplacements refusés/ignorés signalés. Le test borné s'est arrêté comme prévu. Script continu `Rangerbot - Cimetiere prudent` ensuite lancé, groupes seuls, retour à 100 % des PV avant engagement, arrêt sur défaite ou pods à 90 %. Copie dans `scripts/Rangerbot-Cimetiere-prudent.lua`. Aucun élargissement à deux monstres à ce stade.

Le MCP a observé `mapId=153887744, inFight=true` pendant un combat au cimetière. Ces cartes de combat à 0,0 sont donc présentes dans le catalogue de sous-zone ; `CleanMaps` conserve une carte par coordonnée et ne garantit pas d'en exclure toutes les cartes de combat. La parité MCP/UI précédemment corrigée n'élimine pas à elle seule ce défaut supplémentaire.

## Défaite contre Chafer Furtif, puis essai de groupes de deux

Niveau 10 atteint à 09:38:56 (19 322 XP, 118 PV max). Combat suivant `20261024-093900-1073741833` perdu contre Chafer Furtif : événement `InvisibleDetected` déplace le suivi de 314 vers 329 ; les tours suivants visent toujours 329, ennemi affiché à 36/40 PV. Neuf déplacements refusés ; le journal affiche encore 116/118 PV au quatrième tour, puis une mort quelques secondes plus tard. Cela motive un diagnostic des cibles invisibles et des PV ; l'absence d'autres mises à jour dans ce journal ne suffit pas à identifier le champ protocole fautif. Exclusion 4047 ajoutée aux deux scripts cimetière sauvegardés et aux copies du dépôt. Parcours continu arrêté par MCP.

Après résurrection, essai Pâturages borné à cinq combats, taille 1–2, exclusion Bouftor Éthéré, régénération à 100 %. Copie `scripts/Rangerbot-Test-Paturages-duo.lua`. Trois premières victoires : 134, 75, 722 XP. Le troisième journal `20261024-094102-1073741833` confirme explicitement deux ennemis (Boufton Orageux + Boufton Pâlichon), victoire 29,9 s, quatre tours, zéro refus relevé. Total 20 253 XP, niveau 10.

Autre anomalie à coordonner avec le moteur : ce journal annonce placement Aggressive vers 232 alors que le monstre est déjà en 232, `inject=OK` ; l'état suivant garde le joueur en 373. Un ordre accepté n'est pas une confirmation de placement. Ne pas prétendre que le placement proche fonctionne sur cette trace.

Essai Pâturages terminé : cinq victoires, gains 134/75/722/335/812 XP ; total 21 400, niveau 10, 118/118 PV, pods 298/2725. Dernier combat gagné en 15,7 s, deux tours, zéro souci relevé. Script borné arrêté normalement. Reprise de `Rangerbot - Paturages prudents`, groupes 1–2 dès le niveau 10 ; ajout d'un arrêt sur défaite au script sauvegardé et à sa copie locale, pour éviter de répéter un combat problématique sans diagnostic.

## Correctif du lecteur des équipes de placement

`FightPrepReader.ReadPlacementCells` renvoyait `defenders_positions` dans `Challengers` et `challengers_positions` dans `Defenders`, malgré les noms du catalogue. Les trois consommateurs de préparation utilisent `Challengers` pour le personnage attaquant en PvE ; l'inversion explique la proposition d'une case occupée par le monstre. Retour du lecteur remis dans l'ordre nommé. Aucun fichier réservé de combat modifié. Test de régression avec deux listes distinctes : le joueur attaquant reçoit bien les cases challengers. Validation : build application Release sans erreur ni avertissement ; 2 601 tests réussis, 2 ignorés, zéro échec ; clés fr/en/es uniques. Correctif non déployé dans l'instance active ; confirmation de placement en jeu encore nécessaire.

Après ce commit source, nouvelle défaite dans l'ancienne instance : `20261024-094427-1073741833`, groupe de deux Bouftons Pâlichons, deux tours, suivi à 106/118 PV puis mort. Aucun refus comptabilisé. Les cinq victoires du test précédent ne valident donc pas durablement les groupes de deux. Retour du script sauvegardé à `config:setMaxMonsters(1)`, arrêt puis relance après résurrection ; copie locale actualisée. Les pertes de PV et la cible restante doivent être confrontées aux messages bruts avant toute correction de mapping ; pas de conclusion sur la seule estimation de dégâts. Niveau 10 conservé.

## Capture ciblée Lua/MCP et correction LifePointsLost

Le journal disque courant reste à zéro octet depuis 01:14:39. La fenêtre Givre est minimisée ; l'activation via computer-use n'a pas permis sa capture. Aucune action sur les autres fenêtres/comptes. Pour lire des événements actuels, script de diagnostic enregistré avec `registerMessage("GameActionFightEvent", ...)`, limité à 80 impressions et trois combats, groupes seuls. Il reçoit des tables décodées par le catalogue, pas des octets bruts. Copie `scripts/Rangerbot-Diagnostic-PV.lua`.

Combat vers 09:51:18 : un Boufton commence à 21 PV. Premier hit Air (action 98) : table `life_points_lost={element_id=1,loss=4,target_id=-1,permanent_damages=15}`. Dernier hit : `loss=4,permanent_damages=6`, suivi de `death` du même acteur. Les valeurs 15+6 correspondent aux 21 PV initiaux et à sa mort, contrairement à 4+4. Attaque adverse Eau, action 96, à 09:51:22 : table `element_id=3,loss=3,target_id=<moi>,permanent_damages=31`. Les petites valeurs interprétées comme pertes sont les éléments (Air=4, Eau=3) ; les valeurs nommées element_id sont l'érosion (1/3).

Correction des trois noms permutés dans les deux catalogues locaux `data/fields.json` et `server-mapping/fields.json` : `loss=6`, `element_id=4`, `permanent_damages=1` ; cible 5 et bouclier 2 inchangés. Test reconstruit sur ces numéros, élément Eau 3 et perte réelle 31 : le lecteur doit retirer 31. Build application Release : zéro erreur/avertissement. Suite : 2 602 réussis, 2 ignorés, zéro échec ; clés fr/en/es uniques. Aucun changement aux fichiers réservés du moteur. Le mapping du processus actif n'a pas été rechargé : validation live du correctif encore nécessaire.

Le diagnostic borné a confirmé une victoire et 22 366 XP, niveau 10. Capture ensuite arrêtée ; script cimetière relancé, un monstre par groupe, Chafer Furtif explicitement exclu, pour poursuivre les gains sans garder le dump actif.

## Validation dans une instance isolée

Ajout de `GIVRE_CLONE_RUN_NAME` : nom court alphanumérique, tirets et underscores seulement, ancré sous le répertoire TestRuns existant ; valeur absente = stockage historique inchangé. Traversées, séparateurs et chemins absolus refusés. Tests liés à la vraie classe sans lancer de préparation ACL. Dossier de validation créé avec ACL privée, un seul compte chiffré copié, autostart désactivé ; profils copiés, télécommande réseau désactivée, base SQLite copiée séparément. Aucune base installée remplacée. Le canal MCP distinct utilise le réglage existant `GIVRE_CONTROL_PIPE`.

Premier essai refusé pour versions 3.7.11.11 / 3.7.12.12 : l'ancien reçu de compatibilité pointait encore sur le catalogue avant correction. Retour temporaire à l'ancienne instance, puis revue : codes strictement identiques, seuls les trois champs LifePointsLost diffèrent. Reçu local repinné sur fields.json corrigé avec justification, empreintes de schéma/installation conservées. Il reste un candidat diagnostic, pas une certification générale. Connexion réussie après libération de l'ancienne session ; aucun redémarrage des trois autres comptes demandé.

Validation réelle : combat `20261024-100340-1073741827`, victoire en 12,6 s, 482 XP. Placement initial joueur 276, demande 290, snapshot serveur joueur 290 et monstre 398 : déplacement de placement confirmé. Hit reçu `loss=20,element_id=4,permanent_damages=2` ; journal de tour passe de 123/123 à 103/123 PV. Ennemi passe de 44 à 28 après un hit de 16. Deuxième victoire confirmée, XP totale 27 663, niveau 11. La capture Lua est bornée à trois combats, script `Rangerbot - Validation correctifs`, copie locale dans scripts/. Le journal disque de cette nouvelle instance est aussi à zéro octet : le symptôme ne paraît pas limité à l'ancienne rotation, cause encore inconnue.

Validation source de l'isolation : build application Release zéro erreur (206 avertissements existants), 2 610 tests réussis, 2 ignorés, zéro échec ; clés fr/en/es uniques.

À 09:21, niveau 8 confirmé, 108/108 PV. Régénération scriptée observée de 71 % à 100 % puis de 88 % à 100 %. Script sauvegardé mis à jour pour `config:setMaxMonsters(character:level() >= 10 and 2 or 1)` à chaque tick ; version rechargée via MCP, déplacement suivant confirmé. L'augmentation à deux monstres reste à vérifier lorsqu'il atteindra effectivement le niveau 10.

Étape précédente : niveau 7. Après une défaite contre Bouftor Éthéré (journal 20261024-091500-1073741831), script enregistré dans l'application sous `Rangerbot - Paturages prudents` : groupes seuls, monstres 973/972/971, exclusion 984, régénération à 95 % avant engagement, cible 100 %. Parcours étendu aux sept cartes extérieures 153879300, 153879301, 153879813, 153879812, 153880324, 153880325, 153880836 pour éviter les allers-retours sur deux cartes nettoyées. Profil passé à Aggressive, ChallengeRules désactivé pour mesurer les combats sans contrainte de défi. Victoires suivantes vérifiées à 09:18:25 et 09:18:49 (188 et 146 XP). Aucun niveau 200 déclaré.

À 09:28:24, victoire et niveau 8, XP totale 14 083 dans le bilan Lua. Script actif et déplacements confirmés. Copie du script sauvegardée dans `scripts/Rangerbot-Paturages-prudents.lua`.

Défaut de fiche reproduit via MCP : `get_character_info` indique niveau 6, tandis que `get_bot_state` et `character:level()` indiquent 8. `LotMGame.Sheet()` privilégiait le niveau du cache de sélection, qui ne suit pas les gains en session. Il privilégie désormais `CharacterLevel`, actualisé par `CharacterLevelUpEvent`, avec repli sur la sélection si inconnu. Correctif source non déployé dans la session active. Build Release réussi : zéro erreur, 206 avertissements existants dans d'autres fichiers.

Worktree dédié : `GivreWork-Dakal-20261024`, branche `codex-dakal-20261024`, base `eb78608` de `fixes-integration-20261023`. Aucun fichier réservé au moteur de combat ou au déplacement modifié.

## État confirmé

- Achat sur Tylezia : 1 498 ogrines contre 2 575 062 kamas ; solde après échange 1 500 ogrines et 1 831 169 kamas.
- Pack 7 jours acheté pour 1 500 ogrines. Après redémarrage, le client indique une expiration au 30/10/2026 à 23:52.
- Nouveau Crâ `Rangerbot` créé sur Dakal ; Pandawa existant conservé. Niveau 2 après sortie du tutoriel.
- Équipement initial confirmé par les positions de l'inventaire et par l'apparence du personnage à la sélection.
- Pilotage par le pont MCP local : déplacements, dialogues, équipement, profil et scripts. Les acceptations d'ordre ne sont pas considérées comme une confirmation serveur.
- Premier combat contre un seul monstre gagné : `combat:lastFightResult().won=true`, `outcome=2`. L'expédition suivante a engagé et terminé un autre combat, puis rencontré des délais d'engagement et des refus de déplacement. Niveau 200 non atteint.

## Défauts et preuves à poursuivre

1. `read_log` ne lisait que les 100 000 premières lignes, puis renvoyait leurs derniers résultats comme une fin de journal. Les événements MITM récents étaient invisibles dans une session longue. Correctif : parcourir tout le fichier sélectionné (rotation à 25 Mio), conserver seulement les dernières lignes correspondantes ; annulation et masquage conservés. Compilation Release de l'application réussie sans avertissement ni erreur ; 2 600 tests réussis, 2 ignorés, 0 échec. Première exécution interrompue après absence prolongée de progression ; relance avec détection de blocage terminée en 19 s. Correctif non déployé dans l'instance active.
2. Refus/désynchronisation : à 00:15:13, carte 153879299, déplacement 5→72 annoncé puis écho et accusé à 00:15:14. L'attaque suivante ne démarre pas. À 00:15:26, le déplacement 72→139 est refusé ; coordonnées serveur x=5,y=5, cellule 5. L'acteur du modèle reste en cellule 72 alors que SelfCell est recalé à 5 et non confirmé. Ne pas conclure à une arrivée réelle sur le seul accusé ou l'écho de trajet. Investiguer confirmation trop précoce/rollback et synchronisation du modèle avant modification du moteur réservé.
3. L'interactif de sortie du temple change bien la carte mais `goUseInteractive(475)` retourne false après délai. Pour cette porte, utiliser un contrat qui confirme le changement de carte plutôt qu'une récolte terminée.
4. La capture de découverte était coupée avant l'achat de l'abonnement ; ne pas prétendre posséder une capture exhaustive de cet achat. Elle a été réactivée avant la création et la sortie du tutoriel. `GuideModQuitRequest` est observé sous le code `ibd`, charge utile vide. Les alias BAK préexistants ne prouvent pas un parcours d'achat câblé.

## Reconnexion

À la reprise du matin, la session du Crâ était déconnectée. Des redémarrages ont été nécessaires. Le pont refuse `connect_bot` avec `server="Dakal"` (choix du serveur explicitement non câblé) ; le code prend en charge une sélection de personnage par `character="Rangerbot"` sans argument serveur. Ordre de reconnexion avec ce nom accepté, entrée effective en jeu restant à vérifier.

Le profil relu au matin avait `Speed=Turbo`, `NoAnim`, `NoDeathAnimations`, `Collapse` et `InstantMove` actifs. Leur provenance n'est pas établie. Tous les effets expérimentaux et les deux options visuelles par défaut ont été désactivés pour la vérification, vitesse remise à `Fast`. Reconnexion du seul compte cible demandée ensuite. Ce changement constitue un essai diagnostique, pas une preuve de causalité des rollbacks historiques.

## Reprise Socket à 08:20

Le paramètre `mode="socket"` de `connect_bot` est pris en charge par le pont malgré son absence dans la signature MCP descriptive initialement consultée. Après déconnexion du seul compte cible, connexion avec `character="Rangerbot", mode="socket"`, sans argument serveur : entrée en jeu confirmée sur Dakal, PID synthétique 1073741831, carte 153879299.

Expédition Route des Âmes (450), groupes d'un monstre, limite cinq combats : cinq victoires confirmées par `list_combat_journals`, 104 à 146 XP chacune, zéro problème comptabilisé. Passage au niveau 3, déplacement confirmé vers 153879811. Certaines attentes de propositions de défi prennent environ cinq secondes ; plusieurs tours signalent une cible hors de portée, sans empêcher ces victoires.

À 08:23 environ, nouvelle expédition de vingt minutes, groupes d'un à deux monstres, arrêt à 90 % des pods. Aucun changement des trois autres comptes. Objectif niveau 200 toujours en cours. Le mode MITM reste à diagnostiquer ; le fonctionnement Socket ne prouve pas sa correction.

## Niveau 6 et parité des cartes MCP/UI

Route des Âmes : niveau 5 atteint. Test Pâturages (445), groupes seuls : victoires confirmées, gains de 264 à 518 XP dans les journaux correspondants, niveau 6 atteint. Le test borné s'est terminé ; une déconnexion serveur pour inactivité a ensuite été observée. Reconnexion MCP réussie, 98/98 PV, 38 kamas. Nouvelle expédition Pâturages de trente minutes sur groupes d'un à deux monstres démarrée et premier engagement confirmé.

Défaut supplémentaire : `start_expedition` MCP employait les cartes brutes de la sous-zone contrairement à la vue Expédition, qui appelle déjà `ExpeditionScript.CleanMaps`. Les traces montrent des entrées et sorties inutiles de bâtiments (153355272, 153355264). Le pont applique maintenant le même filtrage par coordonnées/calque que l'interface avant le tri. Build Release réussi sans avertissement ni erreur après arrêt du testhost orphelin de notre première suite (PID 20728). Suite : 2 600 réussis, 2 ignorés, zéro échec. Correctif source non encore déployé dans l'instance active pour préserver les sessions des autres comptes.

Ne publier ni journaux complets ni jetons. Le journal historique utile est `transport-2026-10-23_23-22-13.log` ; les identifiants PID changent à chaque redémarrage.
