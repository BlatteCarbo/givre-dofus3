# Compagnons, centres de zone et reprise après combat — 24 octobre 2026

Base : `ba77756`, worktree d'intégration `fix/f01-f16-final-20261024`. L'instance utilisateur PID 6740 conserve le binaire du worktree Dakal ; aucune interruption de sa session ni modification de la base installée.

## Corrections

- Le moteur ignorait explicitement tous les tours autres que celui du personnage principal. `FightControlReader` décode désormais l'identité du maître, l'acteur contrôlé signé et ses sorts/grades par le catalogue courant. Seul un contrôle accordé au personnage authentifié autorise le tour du compagnon : une équipe alliée ou un identifiant négatif ne suffit pas.
- Le tour contrôlé utilise sa position et ses statistiques de combattant, sans reprendre la fiche PA/PM, les caractéristiques ni les sorts du maître. La rotation offensive automatique et les contrôles de zone existants sont réutilisés. Identité du compte, liste des sorts du personnage et paramètres persistés restent ceux du personnage principal. Tours et délais de relance sont conservés par acteur pendant le combat, puis purgés. Les sorts de soutien sans dégâts ne sont pas ajoutés automatiquement : leurs effets ne sont pas devinés.
- Le client Socket accuse aussi les séquences du combattant actuellement contrôlé. Le chemin MITM corrèle ces séquences à ses propres actions. Une annulation par génération libère une attente de l'ancien tour avant de démarrer le nouveau ; son nettoyage tardif ne retire pas le jeton du remplaçant. Aucun accusé serveur fictif n'est fabriqué.
- Le choix d'un centre de zone n'exige plus qu'un ennemi soit directement ciblable. Une case vide, ou la case du lanceur si le sort l'autorise, peut toucher même un ennemi unique hors portée directe. Le centre doit réellement couvrir l'ennemi choisi ; portée minimale/maximale, ligne de vue, occupation exigée, alliés, défis, conditions et plafonds des ennemis touchés restent vérifiés. Les plafonds locaux des cibles secondaires sont aussi comptés. Les zones sont mises en cache pendant cette recherche.
- Le suivi Turbo d'un déplacement et la génération de refus sont armés avant l'injection. Une réponse synchrone/rapide ne peut plus devenir antérieure à sa propre requête, ni un refus disparaître dans le compteur de référence lu après l'envoi. Les barrières de séquence restent présentes.
- `FightEndEvent` ne signifie pas que la carte de jeu de rôle est déjà revenue. Le lancement de `move`, `bank`, `onFightEnd` et le retour de `fight()` attendent désormais son snapshot d'acteurs. Cela empêche un script impératif de classer l'instance de combat comme « dehors » et de repartir vers une entrée/zaap. L'attente pompe les hooks et respecte l'arrêt du script ; une nouvelle agression repasse dans le chemin combat.

## Preuves et limites

Dans le journal courant de l'ancienne instance, le donjon annonce sa salle 1 à 18:34:45, le combat finit à 18:34:53.987, `fight()` rend la main à 18:34:54.051 et la carte annoncée par le client change à 18:34:54.700 ; une tentative de retour vers l'entrée par zaap suit. L'arrêt du script est journalisé à 18:35:06.400, sans exception Lua associée dans les événements consultés. Ce relevé établit l'ordre des événements ; il ne prouve pas à lui seul l'origine de chaque arrêt signalé par l'utilisateur. Les journaux complets ne sont pas livrés.

Tests hors ligne : décodage avec champs déplacés, identifiant négatif, grade absent, maître absent ; contrôle appartenant à un autre personnage, isolation identité/sorts/statistiques, retour au maître, conservation et purge des relances, annulation du tour précédent ; centre vide pour un ennemi unique, centre sur le lanceur, refus de centre interdit et préférence à égalité ; vraie attente Lua après combat et annulation avant l'instruction suivante. Une garde de source contrôle l'armement Turbo avant le transport. Inventaires Lua et protocole régénérés.

La validation en jeu de ce nouveau binaire reste à effectuer : compagnon équipé, passage maître → compagnon → maître, sort de zone autorisant ces centres et reprise de plusieurs combats d'Expédition. Le moteur conserve une seule zone représentative par sort/grade ; il ne modélise pas tous les effets déclenchés ou masques conditionnels. Aucun gain de vitesse chiffré n'est annoncé sans mesure sur la nouvelle version.

## Validation finale

Build Release de l'application : zéro erreur, 208 avertissements. Suite complète : **2 800 réussites, zéro échec, deux ignorés** (2 802 tests), résultat local ignoré Git `GivreClone.Tests/TestResults/companions-aoe-turbo-final.trx`. Les empreintes des DLL applicatives dans la sortie App et dans celle des tests sont identiques. Dictionnaires fr/en/es : 2 701 clés chacun, uniques et identiques. `git diff --check` passe.

La première passe complète donnait 2 799 réussites et une garde du manifeste protocole en échec ; les champs de contrôle sont maintenant explicitement reconnus par le scan et `RequiredMapping.cs` est régénéré. Une première passe ciblée avait chargé une DLL antérieure à l'extraction du helper privé d'attente après combat ; reconstruction complète puis nouvelle copie vérifiée avant la validation finale. Aucun test comportemental n'a été supprimé.

La redirection locale `Directory.Build.targets` et `obj/local-deps` déjà présents servent toujours à retrouver les dépendances dans ce worktree. Ce fichier local et `artifacts/` ne font pas partie du commit. Builds et tests exécutés séquentiellement.

Publication séparée : `%USERPROFILE%/Desktop/GivreClone-Compagnons-Zones-Turbo-20261024/GivreClone.exe`. Empreintes des DLL App et Protocol identiques aux sorties validées. Démarrage de cette publication avec un profil vide `compagnons-zones-smoke-20261024` et un canal dédié : fenêtre répondante, contrôle local fonctionnel, journal initialisé sans erreur, zéro compte et zéro superviseur. Seule cette instance de smoke (PID 7836) est refermée ; PID 6740 reste répondant. Ce contrôle vérifie le démarrage, pas un combat réel.
