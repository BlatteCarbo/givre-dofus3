# Foragerbot : farm Piou Rouge validé le 26 octobre 2026

## Mise à jour 26 octobre, 10:37 UTC — groupes ignorés et Coin des Bouftous

Demande utilisateur : expliquer les groupes avec Noir ignorés et ajouter le Coin des Bouftous. Défaut certain dans le script : `unavailable[g.id]` était partagé entre cartes et rempli AVANT `fightCell`, même pour un combat ensuite gagné. Un identifiant de groupe réutilisé pouvait donc exclure une autre cible pendant 30s. Cette cause est prouvée dans le code ; aucun groupe précis signalé par l'utilisateur n'a été identifié rétrospectivement comme victime de cette collision.

Correctif installé : clé carte + acteur + composition triée, pause de 30s seulement après échec d'engagement, suppression de la pause après succès et purge des délais expirés. Les refus sont journalisés avec carte/groupe/cellule. La sélection du moteur conserve ses contrôles d'accessibilité : une cible présente peut encore être momentanément injoignable ou prise par un autre joueur. Le moteur peut choisir un autre groupe éligible proche de la cellule demandée.

Parcours étendu : 36 cartes extérieures du Coin des Bouftous (sous-zone 5), puis les 24 de Tainéla. Source : base installée ouverte en lecture seule, jointure SubAreaData_mapIds_junction/MapInformationData, worldMap=1, coordonnées connues non (0,0) ; intérieurs exclus. Départ au Coin des Bouftous après relance, première destination 88212240 [0,7]. Filtre conservé : 1–8 monstres contenant au moins un gid149 ; régénération toujours désactivée.

Preuve réelle après relance : arrivée 88212240 (2182), attaque d'un Boufton Noir puis victoire fightId762 et laine885×1 (2183–2188). Poursuite sur plusieurs cartes, puis groupe mixte de cinq niveau134 attaqué sur88083215 (2199–2201). Le nouveau circuit est donc effectivement parcouru et utilisé pour combattre.

Syntaxe valide, lint vide ; sauvegarde sous le nom existant `Foragerbot - Tainela Boufton Noir rentable.lua`, arrêt relu puis relance. Téléportation vers le zaap 88082704 confirmée (2175) et parcours vers 88212240 en cours. Pas de recompilation de l'application, changement limité au script. Les autres comptes ne sont pas modifiés.

## Mise à jour 26 octobre, 10:15 UTC — régénération désactivée

Demande utilisateur : supprimer la régénération d'Foragerbot. Script Tainela local et sauvegardé mis à jour : `MIN_LIFE_PERCENT=0`, appel explicite `regenerate` supprimé. Syntaxe valide et lint vide ; script relancé sur Foragerbot uniquement. Filtre conservé : groupes 1–8 contenant au moins un Boufton Noir. Arrêt sur défaite conservé.

Preuve réelle après relance : curseurs 907–915, groupe de 4 niveau total 102, combat engagé puis victoire fightId 346, 90 kamas et laine noire 885×1. Curseurs 917–918 : groupe de 7 niveau total 178 ciblé ensuite, sans attente de régénération. Minerbot et Bastionbot restent en récolte ; aucun changement de leurs scripts.

Périmètre : bot `00fe352d06bf491fb5abcc2029ce84d7` exclusivement. Aucune action sur Bastionbot, Minerbot ou les tâches horaires. Aucun achat, vente, retrait de kamas, déploiement ou redémarrage. Worktree parent consulté uniquement en lecture.

## Script livré et actif

- Nom dans l'application : **Foragerbot - Piou Rouge prudent 20261026**.
- Fichier local : `scripts/foragerbot_piou_rouge_prudent.lua` dans ce worktree.
- Validation Lua : syntaxe valide, lint vide. Version finale relancée à 07:33:57 UTC.
- Circuit de 11 cartes de la Cité d'Astrub. Uniquement un Piou Rouge ordinaire (gid489), seul, niveau11–15. Composition observée contrôlée ; MAX_MONSTERS=1 et FORCE_MONSTERS={489} empêchent les groupes mixtes.
- Régénération à 90 %, attente bornée à120s ; banque à85 % des pods, dépôt complet et contrôle de diminution du poids. Fermeture par leaveDialog(), pour éviter de masquer le module bank avec la fonction bank(). Arrêt sur échec bancaire, deux trajets échoués, première défaite ou issue inconnue. Pause30s après circuit sans cible.
- Profil de combat conservé : AutoFight/AutoSpells/AutoPreFight actifs, Aggressive, Closest, Fast. Aucun onTurn personnalisé.

## Cotations réellement reçues

Astrub, carte191104004, 26/10/2026 07:28:30–07:28:36 UTC (08:28 heure de Paris). Ouverture réussie par `npc:npc(-1,6)` après voyage ; `sale:GetPriceItem(gid,1/10/100)` en mode achat. Curseurs729–733. Prix des lots en kamas :

- Bleu6897 : 30 / 298 / 4196.
- Jaune6902 : 5 / 377 / 5466.
- Rose6903 : 17 / 476 / 4876.
- Rouge6900 : 278 / 2399 / 34341.
- Vert6899 : 40 / 429 / 5299.

Violet6898, sésame287, laines885/384 et peaux362/2563/363 : -1 pour chaque lot, donc prix indisponible, jamais assimilé à zéro. Pas de comparaison économique prétendument exhaustive. Rouge choisi parmi les ressources effectivement cotées. Base installée ouverte en lecture seule : Piou Rouge489 possède49–81PV aux niveaux11–15 ; plume rouge à57 % de base au grade1, hors modificateurs. Prix affichés ≠ vente réalisée ; rendement horaire et liquidité non mesurés. Le lot100 ne doit pas être confondu avec un prix unitaire, ni utilisé comme garantie de réalisation.

`goToHdvBuy` échoue à Astrub et Bonta car l'hôtelier du carnet n'a pas de cellule résolue. La porte263 de Bonta mène à un atelier alchimiste. L'appel direct a permis les cotations sans modifier l'application. Cette limitation d'ouverture automatique reste présente.

## Combats et banque confirmés

1. 07:31:49–07:31:56 UTC, carte191102976 [3,-19], Piou Rouge seul niveau15. Victoire, fightId1516, journal `20261026-083149-1073741829`. Durée6s, un tour,85XP. Journal :12807 (Engourdissement) lancé vers centre371, ennemi399 tué ; ciblage de zone réellement efficace. Compteur journal2 casts, qui inclut les traces de lancement. PV1030/1030. Butin **6900×1**,3kamas. Curseurs771–778.
2. 07:32:11–07:32:26 UTC, carte191105028 [5,-17], Piou Rouge seul niveau11. Victoire, fightId2028, journal `20261026-083211-1073741829`.14,2s, deux tours,32XP. Butin **6900×1**,2kamas. Une occurrence « aucun sort castable » avant rapprochement. Curseurs791–798.
3. 07:32:39–07:32:50 UTC, carte191105024 [5,-19], Piou Rouge seul niveau14. Victoire, fightId2079, journal `20261026-083239-1073741829`.9,8s, un tour,70XP, aucun objet,13kamas. Curseurs814–820.

Dépôt ponctuel testé après les trois combats : banque192415750, ouverture avec328 piles annoncées. `exchange:putAllItems()`=true, `bank:close()`, poids **1963→27**. Kamas1997802→1997474, soit328 de frais d'ouverture. Aucun retrait. Curseurs831–834,07:33:04–07:33:06 UTC. Les deux plumes ont été déposées avec le reste du sac.

## État de reprise

Dernier relevé vers07:34:10 UTC : connecté Socket, scriptRunning=true, hors combat et en déplacement sur le circuit ; dernière carte lue191102976 [3,-19],1030/1030PV,27/3683pods,1997474kamas. Curseur console **850** ; poursuivre avec `read_bot_console(bot,since=850,limit=100)`.

Trois victoires et zéro défaite pour ce nouvel essai. Énergie351 avant essai, faible : raison supplémentaire de maintenir l'arrêt à la première défaite. Les anciens protecteurs4000PV restent exclus (aucune récolte). La surestimation des dégâts par masques d'effets n'est pas corrigée : la réussite sur Pious ne valide pas ce calcul, ni le combat contre des monstres plus forts.

## Reprise autorisee des trois bots / Turbo / controles3h — 26oct09:35–09:50UTC
Demande utilisateur : relancer3, corriger arrets, controles toutes3h, Foragerbot combat rentable, Turbo tous. Application HdVOffers PID26372 remplacee hors combat par CombatLife PID15920 depuis %USERPROFILE%/Desktop/GivreClone-CombatLife-20261026/GivreClone.exe. Seuls3comptes autorises reconnectes Socket ; quatrieme toujours offline. Correctif soins/vols de vie maintenant CHARGE (build application et3013tests precedemment passes), combat Minerbot contre protecteur encore a observer avant de declarer la boucle83tours resolue.
Bastionbot : script Pecheur200 garde isInFight avant/apres recolte et sur echec trajet ; circuits temporairement inaccessibles et six echecs passent a pause60s/resynchronisation au lieu arret definitif. Syntaxe valide, lint vide. Recoltes reelles reprises : goujons7, carpes7+16+9, truites7+20 et nombreuses suivantes. Protecteur gagne apres reprise, callback victoire=true/defaites0, peche continue ; ce nouveau cas valide la garde. Banque avant deploiement confirmee par pods12069->159 et frais2299 sans retrait.
Minerbot : variante scripts/Mineur-1-200-Minerbot.lua sauvegardee (OFFSET1,193331717 absente,9cartes au palier130). Gardes isInFight et pause/reprise sur routes bloquees ajoutees, pas suppression de l'arret sur defaite. Retour mine confirme188484100 puis188482052 : Fer12 ; puis etain10, manganese7+8, kobalte13, fer14 confirmes. Detours moteur permettent sortie193331715 via193331714/193200130 et arrivee193200131 apres refus du chemin direct ; ce chemin intermediaire traverse encore le secteur retire, ne pas pretendre correction complete de navigation. Dernier voyage vers4460544 en cours.
Foragerbot : fin du test Piou, nouveau farm Boufton Noir a Tainela. Cotations REELLES HDV ressources Bonta212601350 : plume rouge293/u,2983/10,39994/100 ; laine Bouftou384=41/195/1993 ; laine Boufton Noir885=1341/10982/187853 ; Pic Prespic407=399/4093/42971 ; peau larve bleue362=496/6480/69867 ; jaune2563=12/99/989 ; orange363=398/3999/47977. Prix affiches, pas ventes realisees ni mesure de liquidite ; les lots100 n'ont pas ete supposes moins chers que10.
Test expeditionTainela3combats puis script persistant Foragerbot - Tainela Boufton Noir rentable, fichier scripts/foragerbot_tainela_boufton_noir.lua. Groupes1–3,niveau total<=90, whitelist Bouftou101/BouftonBlanc134/BouftonNoir149/Chef148 et au moins1Noir ; archimonstres exclus.24cartes exterieures du catalogue, regeneration90%, banque85% avec baisse poids confirmee, aucun achat/vente/retrait. Plusieurs victoires, dont groupe3; laine noire recue. Derniers journaux104746/104803/104827 : victoires1,6s/4,5s/2,5s. Ancienne selection a boucle sur groupe indisponible : correction groupe ecarte30s + map request apres combat, FORCE149 et interdictions archis au niveau moteur pour eviter repli sur chef seul. La version finale a deja confirme plusieurs nouvelles victoires et butins, changements de carte apres combat.
Profils des3 relus Speed=Turbo. Bastionbot95946kamas/3053PV/5051sur14860pods ; Minerbot997440/2419/6572sur9927 ; Foragerbot543408/2173/181sur3928 au dernier list_bots,3scripts actifs. Aucun revenu horaire affirme depuis ces soldes. Pas de nouvelle depense stuff, pas retrait1M repete.
Curseurs apres redemarrage : Bastionbot422, Minerbot392, Foragerbot265 (reprendre depuis ceux-ci, pagination necessaire si400lignes rendues, meme si truncated=false). Quatrieme ignore.
Surveillance EXISTANTE remplacee par intervalle10800000ms, prochain26oct12:41:31UTC=13:41Paris, puis toutes3h. ID scheduled-task:command:mcp:d383d35d-8de9-4d59-b1e5-b578e6a91827:schedule-task:givre-hourly-miner-farm-20261026-v1. Pas deux controles concurrents.
Comme les controles3h ne tombent pas a22:50UTC, tache de FIN seulement ajoutee pour respecter echeance23:50Paris : scheduled-task:command:mcp:0b08e0f9-a478-41e4-ab23-827d4d846a4d:schedule-task:givre-stop-20261026-2350-v1 ; prochain22:50UTC confirme. Elle doit arreter3scripts apres combats, laisser comptes connectes, puis desactiver surveillance ET elle-meme. Aucun controle de farm supplementaire.
