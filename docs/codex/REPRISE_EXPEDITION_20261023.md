# Reprise Expédition — 23/10/2026

## Cadre

Branche `codex-expedition-runtime-20261023`, créée depuis `65948fb` (`codex-ui-20261023`) après constat d'un worktree propre sur `b355919`. Travail exclusivement dans le worktree T3 `t3-9cc13385`. Aucun autre agent lancé, aucun Chat On Steroids, aucune intégration ni publication. Les six documents demandés ont été lus dans le checkout de référence, dont le début du rendu qui remplace ses anciennes limites. Aucun AGENTS.md trouvé dans le worktree ou ses répertoires ancêtres applicables.

## Correctifs concrets

- Une expédition à carte unique appelait `map:forceFight()`, qui boucle côté hôte tant qu'aucun groupe n'est attaquable. Pendant cette attente, `limitReached()` n'était jamais rappelé : durée maximale et fermeture d'anomalie ignorées. Le script utilise désormais `fight()` puis une attente de 2 secondes en l'absence de cible, avant de rendre la main à la boucle de l'hôte. Les limites sont revérifiées avant la prochaine tentative. Aucun combat en cours n'est interrompu par la durée maximale.
- Avec banque/HDV, `STOP_PODS` vaut volontairement zéro pour laisser le moteur appeler `bank()`. Mais la boucle Lua vidant une carte pouvait enchaîner les combats sans rendre la main au moteur. Elle sort désormais dès `MAX_PODS` atteint, avant un nouvel engagement. L'hôte peut alors exécuter la fonction bancaire existante.
- La multisélection A+B puis A conservait les cartes et monstres de B lorsque A restait la première sélection ; une sélection vide ne vidait rien. Reconstruction complète des cartes, monstres et valeurs de drops depuis la sélection courante, y compris lorsqu'elle est vide. Conservation des filtres des monstres toujours présents. Une sélection multizone ne conserve plus un arrêt lié à l'anomalie d'une seule sous-zone.
- Le tri de proximité pouvait déborder sur les coordonnées entières extrêmes. Calcul des distances en `long`.

## Régressions

`ExpeditionLoopRegressionTests` exécute les scripts générés dans MoonSharp avec `CoreModules.Preset_SoftSandbox`, comme l'hôte : retour après attente sans repousse, limite de combats sur carte unique, durée maximale pendant l'attente, fermeture d'anomalie, seuil bancaire pendant le nettoyage multicarte, absence de combat supplémentaire au plafond, coordonnées extrêmes. Six tests (certains vérifient plusieurs assertions comportementales).

La multisélection nécessite encore une vérification de l'interface réelle : choisir A+B, garder A, puis tout désélectionner ; vérifier compte de cartes, monstres, estimation de valeur et refus de lancement sans zone. Le projet de tests lie les sources pures et ne compile pas ce ViewModel ; le build complet de l'application vérifie sa compilation.

## Audit des raccordements et limites

Chaîne lue : sélection UI → `BuildExpeditionProfile` → `ExpeditionScript.Build/BuildDungeon` → callbacks de l'hôte Lua → sélection de groupe et attaque. Le catalogue GameData recharge déjà après échec sur cette référence. Les API `dungeon:exit`, `fight`, `getCountFight` et les setters de filtres sont bien liées ; ce ne sont pas des fonctions absentes. Le catalogue de paquets `LocalScriptCatalog` est explicitement un registre de prévisualisation sans exécution ni octroi de droits : aucun branchement aveugle vers RunLua ajouté.

Le réglage sauvegardé ne conserve encore que la première sous-zone (format historique `ExpeditionState`) : la restauration d'une sélection multiple n'est pas revendiquée. La sélection géométrique des groupes est déjà testée par la suite existante ; `GivreLuaHost.Combat.cs` est réservé et n'est pas modifié.

Point réservé à transmettre au parent : `FightOne`, bloc `TeamCombatWaitPending`, ignore le booléen de `WaitUntil` puis peut attaquer même si les suiveurs restent absents après timeout. Proposition de patch, sans modification du fichier : remplacer l'appel isolé par `if (!WaitUntil(() => !_launch.TeamCombatWaitPending(_pid), TeamWaitTimeoutMs, ct)) return false;`, puis revérifier carte/position après attente avant sélection. Valider avec équipe réunie, suiveur absent et changement de carte pendant attente. Cette proposition nécessite la décision du propriétaire du combat ; elle n'est pas incluse au commit.

Aucun lancement du jeu, combat réel, dépôt bancaire réel ou validation serveur effectué. Les tests utilisent des réponses simulées ; ils ne prouvent pas une action acceptée par Dofus. Aucun changement de base SQLite, profils combat, UI/native combat, paquets ou fichiers historiquement réservés.

## Validation

Build application Release réussi : 0 erreur, 207 avertissements. Ensuite suite complète Release réussie : **2553 réussites, 0 échec, 2 ignorés, 2555 tests**, durée des tests 39 secondes. Les six nouveaux tests passent. Le test `Installed_catalogue_lists_a_complete_expedition_selection` passe sur le catalogue installé (lecture seule). `git diff --check` sans erreur. Les références relatives à GivreCore et GivreLauncher.Core sont absentes du parent du worktree T3. Première tentative de build échouée pour cette raison. Copies de ces deux dépendances dans `%TEMP%/givre-expedition-validation-deps`, sans bin/obj et sans modifier leurs checkouts ; redirection locale par `-p:DirectoryBuildTargetsPath=.../Validation.targets`, non suivie par Git. Aucun csproj du dépôt modifié.

Commandes de validation (dans le worktree T3) : `dotnet build GivreClone.App -c Release --nologo -p:DirectoryBuildTargetsPath=<TEMP>/givre-expedition-validation-deps/Validation.targets`, puis `dotnet test GivreClone.Tests -c Release --nologo -p:DirectoryBuildTargetsPath=<TEMP>/givre-expedition-validation-deps/Validation.targets --logger "trx;LogFileName=expedition-release.trx"`. Résultats structurés dans `GivreClone.Tests/TestResults/expedition-release.trx` (ignoré Git) ; journaux conservés en TEMP, non publiés.

Unicité contrôlée pour toutes les clés `x:Key` : fr 2700, en 2584, es 2583 ; zéro doublon dans chaque fichier. Aucune nouvelle chaîne nécessaire et aucun Lang modifié.

## Fichiers livrés

- `GivreClone.App/Services/ExpeditionScript.cs`
- `GivreClone.App/ViewModels/BotsViewModel.Expedition.cs`
- `GivreClone.Tests/ExpeditionLoopRegressionTests.cs`
- `docs/codex/REPRISE_EXPEDITION_20261023.md`
