# IA Éliotrope : tir à travers les portails (10/11/2026)

## Règles du jeu modélisées
Source : https://eliotrope.net, règles 02, 03, 05 et 06.
- Le lanceur vise le portail d'ENTRÉE. Le serveur contrôle la portée et la ligne de vue jusqu'à cette entrée.
- Le réseau va ensuite au portail inutilisé le plus proche, et ainsi de suite. Le dernier portail atteint est la sortie.
- Le sort ressort de la sortie avec le même décalage (dx, dy) que celui entre le lanceur et l'entrée.
- En cas d'égalité de distance, le jeu applique une règle horaire. Elle n'est pas modélisée : dans ce cas, aucun tir n'est fait.
- Bonus : +2 % de dégâts par case d'éloignement entre portails (donnée client : action 1181, diceNum 2).
- 4 portails au maximum ; le 5e efface le plus ancien. Vérifié en jeu : les marques sont de type 3, sur 1 seule case.

## Erreurs passées
- `EliotropeBreedId` valait 15, ce qui correspond au Steamer. La bonne valeur est **16**, soit `BreedId(index 15)`.
- Viser la case de l'ennemi « à travers les portails » provoquait les refus 171, 173 et 174.
- Les effets 1171 et 2971 du sort Portail n'étaient pas reconnus, ce qui faisait rejeter tout le sort.

## Code
- `ClassMechanics.ProjectThroughPortals(caster, entry, portalCells)` calcule (impact, distance parcourue dans le réseau, sortie). Il renvoie null en cas d'égalité ou si l'impact tombe hors de la grille.
- Dans `CombatTurnPlanner` :
  - `PortalShots` génère les tirs ; l'action vise le portail d'entrée ;
  - `PortalCells` prend les portails observés et ceux posés pendant le plan ;
  - `PortalPlacementValue` donne +10 seulement si la pose ouvre un tir, et 0 à 6 pour le premier portail.
- `LaunchService` retire les cases de portail de `markedCells` et ajoute « via-portail » dans le journal `execute`.

## Suite du 10/11 (autres classes)
- Portails : la ligne de vue entre la sortie et l'impact est maintenant vérifiée. Résultat : 0 refus 174 sur 13 tirs via portail (sessions 08:18 et 08:44).
- Crâ : `AutoSpellRotation` ne met plus `OnMe` sur un sort de portée min ≥ 1 (les flèches 32435/32436 ont un buff C sur le lanceur). Avant, 95 % des tours de Archerbot finissaient avec 3 PA ou plus inutilisés.
- Sram : la rotation classique refuse une case qui porte déjà une marque, ce qui supprime le refus 229 (piège sur la case).
- Fuite : sous `FleeBelowHpPercent`, un plan sans sort ne repasse plus par la rotation classique, qui renvoyait au contact. Le seuil est réglé à 35 % pour les 4 persos.
- PV : le serveur remet le perso à 50 % pile après chaque combat, victoire ou défaite (fiche 1702/3403). Une régénération réglée à 50 % ou moins ne se déclenche donc jamais.
- Sylvanbot : son script mineur fixe sa rotation lui-même via `fightBasic:addSpell`. Sacrifice Vaudou (13531) a été ajouté dans `planning.json`.
- Session 08:44 :
  - Eliobot : 44 victoires, 0 défaite ;
  - Shadowbot : 26 victoires, 5 défaites (duos de monstres à environ 3 000 PV, en partant à 50 % de PV) ;
  - aucun ban.

## CastLedger (10/11 soir)
- `Combat/CastLedger.cs` est la mémoire unique de lancers par combattant joué : recharge, refus 174/175, compteurs par tour et par cible. Le glossaire est dans `GLOSSARY.md` à la racine.
- Régression : la 1re version, écrite par un agent Gemini Flash Lite, enregistrait chaque lancer 2 à 3 fois (MarkCast plus RecordCast au même site). Résultat : Shadowbot à 51 % de tours avec 3 PA ou plus perdus (31 % avant) et 6 défaites en 21 combats. La suite de tests restait verte. C'est la relecture du code et la mesure des journaux qui ont repéré le problème, pas les tests.
- Corrigé : un seul RecordCast, placé après le contrôle de refus. Suite à 5266 tests OK. Les DLL précédentes sont sauvegardées dans `Temp/givre-live-backup-ledger1`.
- Leçon : après un refactor délégué, chercher par grep tous les sites d'enregistrement, et mesurer les PA perdus en jeu avant de valider.

## Pistes d'amélioration mesurées (10/11, 21 journaux)
- Compagnon de Eliobot : il marche vers sa case de tir, puis son sort 4244 est refusé (« case ciblée invalide », 245 fois). 124 déplacements sur 186 ne mènent à aucun tir.
- Shadowbot : 35 défaites sur 306 combats, surtout contre les duos Boumbardier+Rouquette, Fangshu+Ino-Naru et Crachefoux+Rouquette.
- Trajets : `action_failed` sans chemin world-graph (cartes 209191177 et 209192710), retenté 14 fois chacun au lieu d'être marqué inaccessible.

## Analyse combats 10/11 soir (16:41-23:15)
- **Eliobot** : 216 victoires / 77 défaites. On relève 944 boucles de sorts et 31 315 barrières non fermées, soit environ 2 h 39 d'attente, pour des combats de 48 s en moyenne.
  - Cause : les lancers restés sans réponse n'étaient plus enregistrés dans CastLedger (des variables locales mortes subsistaient après la refonte).
  - Correctif (tâche 18) déployé à 23:20. Sauvegarde : Temp/givre-live-backup-ledger4.
- **Shadowbot** : 521 victoires / 44 défaites, combats de 8 s.
- **Archerbot** : 11 défaites sur 11 récentes. Il se fait agresser pendant ses récoltes (cartes 88218625, 97388039, 88218881).
  - Décision de l'utilisateur : « Ne rien changer ».

## Correctifs du 10/11, vérifiés en jeu
- **`setForbiddenMonsters` / `setMandatoryMonsters`.**
  - Bug : `ListPair` passait la liste par `Nums()`, qui ne garde que les nombres. Les noms étaient donc perdus et une liste de monstres par nom n'avait jamais rien filtré. Constat sur Shadowbot : 17 attaques contre des monstres pourtant interdits.
  - Correctif : `MonsterListPair` conserve les nombres et les chaînes, et `SelectMonster` filtre sur les deux. L'inventaire Lua a été régénéré (`python docs/portal/lua-f07-inventory.py`), comme l'exige `LuaApiInventoryTests`.
  - Vérifié en jeu : 0 attaque sur 15 contre Rouquette, Boumbardier, Fangshu et Ino-Naru.
  - Piège à retenir : l'id 2303 correspond à l'archimonstre « Crachefouxtre le Surpris », pas au Crachefoux commun.
- **Compagnon de Eliobot.**
  - Correctif : un sort qui exige une case libre (`NeedFreeCell`, ici 4244 Nimbostratus) ne déclenche plus de déplacement vers une case de tir.
  - Vérifié en jeu : 0 déplacement de ce type (124 sur 186 avant) ; 32 victoires, 0 défaite.
- **Expédition.**
  - Contexte : `travelTo` renvoie seulement un booléen, si bien que la raison `no_route` n'arrivait jamais au script.
  - Correctif : après 2 échecs sur une carte, elle est marquée inaccessible. Fait dans le générateur et dans le script de Shadowbot.
  - Pas encore observé en jeu : la session n'a eu aucun trajet en échec.
- **PA perdus par Eliobot (65 % des tours hors fuite).** Ce n'est pas un bug. Cas observés : ennemi à plus de 10 cases (sa portée max est de 10, avec 4 PM), défis Zombie, Statue ou Nomade, défi Versatile, ou cible déjà morte.

## Mesuré en jeu (10/11, session 07:42)
- 30 victoires pour 1 défaite.
- 7 portails posés et 4 tirs via portail, dont 0 refusé et 3 qui ont achevé leur cible.
- Aucun ban.
- Profil : 3 monstres max, `FleeBelowHpPercent` à 35.
