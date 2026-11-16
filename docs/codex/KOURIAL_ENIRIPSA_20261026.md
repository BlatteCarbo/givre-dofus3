# Kourial / Eniripsa — 26 octobre 2026

Demande : adapter le dossier utilisateur `Downloads/PL eniripsa 200/PL eniripsa 200` puis lancer le compte demande sur Kourial. Original Lua/XML conserve sans modification.

## Identite et instance

- Le login demande correspond au compte **00fe352d06bf491fb5abcc2029ce84d7**, celui d'Foragerbot, et PAS au quatrieme compte. Le script Foragerbot a ete arrete puis le compte deconnecte de l'application principale. Ne pas le reconnecter depuis cette application pendant la session Kourial.
- Personnage cree : **Valrianess**, Eniripsa F, ID **61973463395**, **Kourial (355)**. CreationResult confirme le 26/10 a 16:52:33 UTC ; liste mise a jour et premiere selection recues. Connexion jeu `dofus2-ga-kourial.ankama-games.com`.
- Instance separee : run **kourial-eni-20261026**, pipe **GivreWork-Kourial-Eni-20261026**. PID initial 17000, ephemere. Un seul compte configure. Donnees sous `%LOCALAPPDATA%/GivreReplica/TestRuns/kourial-eni-20261026/userdata`.
- Binaire : `%USERPROFILE%/.t3/worktrees/GivreWork/kourial-eni-20261026/GivreClone.App/bin/Release/net10.0/GivreClone.exe`.
- Pour lancer ce binaire, variables indispensables : `GIVRE_CLONE_RUN_NAME=kourial-eni-20261026`, `GIVRE_CONTROL_PIPE=GivreWork-Kourial-Eni-20261026`, `GIVRE_GAMEDATA=%USERPROFILE%/AppData/Local/GivreReplica/TestRuns/givre-app-offline/userdata/gamedata/dofus.sqlite`. Ne pas ouvrir le binaire sans ces variables : cela selectionnerait le run principal par defaut.
- Appel local dans le worktree principal : `artifacts/kourial-session-call.ps1 -Tool <outil> -Arguments <JSON>`. Aucun secret dans ce helper.

## Correctif creation

Branche **fix/kourial-eni-20261026**, worktree separe **kourial-eni-20261026**, base **ef0364e** de `integration/agents-20261026-reviewed`.

`SocketConnectionClient` refusait le serveur choisi si `Characters.Count == 0`, et refusait egalement un compte sans aucun personnage avant d'appeler le selecteur. Le flag de creation vient maintenant de `SocketCreationRegistry.Has(login)` sans consommer la demande ; il est preserve lors d'une correction de version. Creation autorisee uniquement avec choix explicite d'un serveur present, accessible, sans nom de personnage existant demande. Aucun repli vers un autre serveur.

15 tests selection/encodage passes ; build App Release reussi. Creation sur un serveur vide validee reellement par cette session.

## Adaptation du script

Fichier **scripts/Eniripsa-1-200-Kourial.lua**, copie utilisateur **Downloads/PL eniripsa 200/PL eniripsa 200/Eniripsa-1-200-Kourial-2026.lua**. Sauvegarde application : **Eniripsa 1-200 Kourial 2026**.

- Ancien clear() destructif, tarifs HDV fixes et equipements imposes retires.
- Ancienne branche de fin niveau200 inaccessible (`<=200` avant `==200`) remplacee par arret explicite.
- Cartes du catalogue installe, calques distingues, pious actuels 489-494 et sorts appris actuels (25867/25870/25858/25877 au depart), aucun chargement des XML 2020.
- Profil sauvegarde : Turbo, AutoFight/AutoSpells/AutoPreFight, Chance automatique, defis desactives, pas de resurrection automatique.
- Garde Valrianess/Kourial, groupes limites, regeneration explicite avec arret si echec, arret apres defaite ou resultat inconnu. Arret conservateur a 85% pods ; banque du nouveau personnage PAS encore validee, aucun objet supprime.
- Neuf regressions MoonSharp sur le script reel passent : mauvais personnage/serveur, niveau200, pods, combat en cours, PV inconnus, regeneration refusee, cible admissible, defaite.
- Temple nouveau : passage Dofusarium vers 241444353 puis 241445377. La porte cellule475 echouait via travelTo (marche sur la porte) ; **goUseInteractive(475)** a confirme le passage vers 154010883. Pas de modification du moteur de portes dans ce lot.
- Au moins onze victoires confirmees dans les premieres minutes, niveau4 observe ensuite, XP et butin recus ; script actif. Les niveaux hauts, le portail Incarnam/Astrub et les futurs equipements ne sont PAS valides en session. Le dernier palier Gelees conserve l'idee historique ; aucun rendement jusqu'au niveau200 promis.

## Incidents et controles

Premiere ouverture isolee : dossier cree avec proprietaire groupe Administrateurs -> refus du garde MZP19. Proprietaire corrige uniquement sur les deux nouveaux dossiers de ce run ; aucun garde desactive. Deuxieme ouverture fonctionnelle.

Capture utilisateur Offline.exe : processus provenant de **agents-b02-codex-20261026/docs/agents/20261026/B02/b02-identity**, distinct de Givre. Son tests.log indiquait une assertion B02IdentityTests ; aucun processus d'un autre agent arrete ici.

Suite complete initiale : **3077 passes / 1 echec / 2 ignores**. L'echec portait sur les empreintes de l'inventaire Lua herite de la branche revue. `python docs/portal/lua-f07-inventory.py` regenere l'inventaire : 979 liaisons, aucune signature documentee absente. Reexecution complete en cours au moment de cette note, resultat a consigner ci-dessous.

Les anciennes surveillances 3h et fin de session ont ete relues : toutes deux deja desactivees. Aucune nouvelle tache programmee creee.

## Validation finale

Suite complete apres regeneration : **3078 reussis, 0 echec, 2 ignores**, 33 secondes. Build App Release final : reussi, 0 erreur. Neuf regressions du script : reussies. Dictionnaires FR/EN/ES : 2705 cles uniques chacun. git diff --check propre. Dernier etat reel : niveau4, 70/70PV, 91/1015pods, script actif.
