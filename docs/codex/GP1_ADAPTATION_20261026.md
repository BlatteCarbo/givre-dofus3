# Incarnam / GP1 / Dofus Argente — adaptation du 26 octobre 2026

## Sources et perimetre reel

Deux fichiers utilisateur : script Incarnam de 73 Ko et archive DOFUS ARGENTEE PC.zip de 222603 octets. Le ZIP contient un Lua Insa99 du 01/05/2020 (Dofus2.55.6.9), 64 indices ETAPE, et un PDF ; ses PNJ et fonctions couvrent Incarnam et l'arrivee a Astrub, pas toutes les quetes de Rathrosk. L'original reste dans les pieces jointes, jamais execute tel quel.

Reference consultee : https://ganymede-app.com/guides/2, GP1 Incarnam Astrub. Controles cibles du debut (Temple, village) et de la fin (Rathrosk). Le guide est une reference de progression ; une instruction de guide n'est pas une confirmation serveur.

Le catalogue **GP1_ARGENTE_CATALOGUE_20261026.json** est derive en lecture seule de la base installee : descente des succes1679 et dependances, puis exclusion des18 alternatives de classe incompatibles avec Eniripsa7. Il contient **56 quetes / 444 objectifs** avec positions, types, parametres et prerequis. C'est un catalogue de reconstruction, **pas un script Astrub complet valide**. Les etapes SQL triees par ID ne constituent pas une preuve d'ordre d'execution.

## Corrections du script fourni

**scripts/Incarnam-Quetes-Valrianess-2026.lua** conserve les phases utiles du script utilisateur, avec :

- IA native AutoSpells/Turbo a la place de l'IA utilisant d2data:objectFromD2O indisponible. Style Fleeing, distance2-4, caracteristiques Chance gerees par le profil ; pas de second hook onTurn.
- Etiquettes de493 reponses PNJ issues de NpcData/translations de la base installee ; pas de remplacement positionnel aveugle si une reponse est inconnue. Preferences de Lykhen/Ternette revues sur leurs dialogues reels.
- Identite Valrianess/Kourial et snapshot de quetes obligatoires ; arret sur defaite, interaction non confirmee ou phase bloquee. Une phase bloquee reste non terminee.
- Les retours de craft:putItem sont controles avant Ready. document:read/click absents remplaces par l'ouverture de l'element observe et document:get/quest:start.
- Tutoriel guide536 signale indisponible plutot que simule. Interieurs du Temple parcourus par changements de carte confirmes et portes observees369/475 ; pas de validation du seul trajet demande.
- Webhook retire, aucun message externe. Ventes et objectif5000kamas desactives ; pas de retrait bancaire automatique. Original non modifie.

Sauvegarde application : **Incarnam quetes Valrianess 2026**. Copie utilisateur dans Downloads/PL eniripsa 200/PL eniripsa 200/Incarnam-Quetes-Valrianess-2026.lua. Dix regressions MoonSharp sur le vrai script passent : identite/serveur, snapshot inconnu, objectif d'une quete terminee, reponse inconnue/pref absente, reponse annoncee, refus de pose craft, defaite, un seul moteur de combat et notifications desactivees.

## Defauts du moteur prouves en session

### Une consultation ne demarre pas une quete

QuestStepInformationEvent pour1632 a repondu avec `information={quest_id:1632}` sans details alors que le personnage n'avait pas encore commence cette quete. LaunchService.LotI.cs l'ajoutait a tort dans _activeQuests. Desormais seuls les details de la reponse peuvent enrichir cette liste ; une liste de quetes ou un QuestStartedEvent restent les autorites pour etablir une activite sans details. La reponse reste horodatee pour les attentes de consultation. Trois tests verifient consultation inconnue, details existants conserves et details autoritaires acceptes.

### Le drapeau brut etait interprete a l'envers

Temoin reel : quete1632, etape2252, objectif9762, parler a Ternette. A17:40:00.674UTC, avant la visite : API ancienne reached=true, paquet catalogue objective_reached=1. Apres la conversation24919/24927/24920, a17:40:02.934UTC : API ancienne reached=false, valeur brute0/omise ; les objectifs9763 a9766, pas encore effectues, restent a1. **Sur ce patch, 1 signifie encore a faire.**

QuestReader normalise maintenant ce drapeau en `Reached=FAIT`. Les attentes de succes, doneObjectives et les alias conservent ce sens metier. Un test reproduit le temoin avant/apres en protobuf, avec l'objectif9763 restant en attente. Les anciens tests qui assumaient le sens inverse ont ete corriges. Pas d'inversion des evenements explicites QuestObjectiveValidatedEvent, qui signifient bien valide.

### Confirmation fraiche optionnelle

quest:validateObjective(q,o,timeout,true) demande un nouvel accuse au lieu de retourner true depuis un ancien drapeau. Le mode frais exige une quete active et l'objectif observe ; il conserve les gardes de permission et le meme encodeur type. Test : objectif/quete inconnus refuses, ancien comportement idempotent conserve par defaut. Les demandes fraiches de9738/9739 n'ont pas eu d'accuse ; elles ne sont PAS revendiquees comme reussies.

## Etat operationnel et limites

Compte cible00fe352d06bf491fb5abcc2029ce84d7, **Valrianess niveau7**, instance isolee kourial-eni-20261026, pipe GivreWork-Kourial-Eni-20261026. Ne pas reconnecter Foragerbot dans l'autre application : c'est le meme compte. Bastionbot, Minerbot et le compte Shadowbot n'ont pas ete pilotes pendant cette adaptation.

La conversation de Lykhen a effectivement commence1632, puis la visite de Ternette a fait evoluer9762. La phase1631 n'est pas declaree terminee. Les six sculptures du Dofusarium etaient rejetees par MapInteractivesReader faute de position. Elles sont maintenant conservees dans une liste de lectures de scene liee au PID et a la carte, exposee a Lua sans entrer dans la grille/recolte. Le test reel du 26/10 a17:55UTC voit les six objets541698 a541703 mais leurs lectures184 depuis la case courante sont refusees par le serveur. La position d'interaction reste a resoudre ; aucune validation directe correspondante n'a ete confirmee. Le script s'arrete explicitement si ce blocage persiste ; ne pas remplacer cela par un faux succes.

La chaine Astrub manque dans l'archive. Sa reconstruction necessite encore des actions executables et controlees pour les objectifs de scene, de combat, de livraison et de craft du catalogue ; le Dofus Argente19629 n'a pas ete obtenu. Ce rapport ne ferme donc pas le parcours GP1 global.

## Validation

Build App Release : reussi, 0 erreur. Tests cibles quetes/lectures/isolation :28 reussis. Script :10 controles reussis. Suite complete initiale apres corrections :3081 reussis,1 echec,2 ignores ; le test PostFightRoleplayTests en echec a reussi dans la suite de confirmation22/22. Suite complete apres inversion :3083 reussis,0 echec,2 ignores. Apres ajout des lectures de scene :3086 reussis,1 echec de seuil de performance MapInspectionTests,2 ignores ; rerun des20 MapInspectionTests reussi. Suite complete de confirmation consignée ci-dessous.

Les notes mentionnant les objectifs9738/9739 faits avant ce diagnostic de polarite sont depassees : elles refletaient l'ancienne interpretation erronee du drapeau. Aucun journal complet, compte secret, jeton ou webhook dans cette livraison.

Suite complete de confirmation (application isolee fermee) : **3087 reussis, 0 echec, 2 ignores**, total3089. FR/EN/ES :2705 cles uniques chacun. Le compte a ete deconnecte apres le test bloque ; aucune boucle de tentatives n'est laissee en cours.
