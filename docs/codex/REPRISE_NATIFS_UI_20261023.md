# Reprise natifs et UI — 23/10/2026

Branche `fix/natifs-ui-20261023`, base exacte `65948fb` (`codex-ui-20261023`). État Git initial propre sur `b355919`, puis création de la branche sans reset. Aucun AGENTS.md trouvé dans le worktree ni ses répertoires parents. Les six documents demandés ont été lus avant modification ; le début du rendu prévaut sur ses anciennes limitations.

## Correctifs concrets

1. **Précontrôle natif toujours négatif avec le bundle livré.** Les manifestes du skipper et des six options cherchaient `GivreTurboBootstrap.dll` dans le répertoire de distribution `NativeTurboRuntime`. Le projet livre `DofusNativeBootstrap.dll` ; le renommage en `GivreTurboBootstrap.dll` intervient seulement dans le staging par PID. Cela bloquait non seulement l'interface, mais aussi `LaunchService.NativeVisualOptions`, qui retournait zéro malgré les DLL valides. Les deux manifestes vérifient maintenant le nom effectivement livré, avec la même empreinte stricte. Le nom de destination du runtime reste correct et inchangé.
2. **Statut et droits visuels périmés.** Le changement de bot ne rafraîchissait que les cases, conservant le statut READY/Socket du précédent ; les changements du droit no-anim ne notifiaient pas les trois nouvelles capacités dérivées. Le fichier UI dédié recalcule maintenant statut et capacités sur les notifications existantes de sélection et de permission. Aucun fichier moteur réservé modifié.

## Vérification

Le nouveau test reconstruit un bundle temporaire à partir des fichiers déclarés dans le vrai `.csproj`, puis vérifie les empreintes et capacités. Il supprime, altère et restaure chaque dépendance visuelle ; la capacité doit disparaître puis revenir. Le skipper perd aussi sa capacité lorsque le bootstrap livré est supprimé. Ce test ne charge aucune DLL et ne contrôle aucun client.

Les sources de cycle de vie ont également été relues : masque zéro avant désactivation, exclusion skipper/options, restauration speedhack avec accusé corrélé à l'identité/génération/session, confirmation STATUS après UNLOAD, restauration des renderers tactiques au déchargement. Ces lectures et les tests de contrat existants ne prouvent pas un cycle réel dans Dofus.

Le premier build a révélé les références voisines manquantes dans le nouveau layout T3. Copies de sources `GivreProxy` et `GivreLauncher` préparées dans le parent du worktree, sans `.git`, `bin`, `obj` ni modification des worktrees existants. Aucun changement de projet requis.

Commandes finales exécutées séquentiellement avant commit : `dotnet build GivreClone.App -c Release --nologo --no-restore`, puis `dotnet test GivreClone.Tests -c Release --nologo --no-restore --logger "trx;LogFileName=natifs-ui-release-final.trx"`. Build réussi : zéro erreur (le premier build complet signalait les avertissements existants ; le dernier incrémental n'en signale aucun). Suite complète : **2 548 réussites, zéro échec, deux ignorés, 2 550 au total**. Le nouveau test de bundle est inclus et réussi. Résultat structuré local : `GivreClone.Tests/TestResults/natifs-ui-release-final.trx` (ignoré par Git). La première exécution avait détecté un défaut du nouveau lecteur de projet dans le test : les modèles MSBuild `%(Filename)` étaient pris pour des liens concrets ; filtre corrigé avant la nouvelle séquence complète.

Unicité XML de toutes les clés : fr 2 700, en 2 584, es 2 583 ; zéro doublon dans chaque fichier. Aucun Lang modifié et aucune nouvelle chaîne nécessaire.

## Limites et contrôle parent

Aucune session de jeu confirmée : aucun lancement, injection, commande native, F8 ou contrôle de jeu effectué. Aucun effet visuel ou déchargement réel annoncé validé. Aucun binaire natif reconstruit, aucune base SQLite remplacée, aucun log complet ni secret publié. Aucun push, publication ou intégration.

Après intégration par le parent : ouvrir les options avec le bundle Release, constater leur disponibilité ; changer entre bot graphique et Socket ; révoquer/rétablir le droit no-anim et vérifier le grisage. Après confirmation explicite de session, contrôler une option à la fois, fin de combat, changement de carte, retour au masque zéro, retour au skipper et désactivation tactique/F8 sur deux clients. Confirmer l'accusé de restauration avant déchargement speedhack et le STATUS final ; ne pas déduire l'effet en jeu de la seule présence du module.

## Fichiers précis

- `GivreClone.App/Services/CombatVisualModPolicy.cs`
- `GivreClone.App/Services/CombatVisualOptions.cs`
- `GivreClone.App/ViewModels/BotsViewModel.CombatVisualMods.cs`
- `GivreClone.Tests/CombatVisualModPolicyTests.cs`
- `docs/codex/REPRISE_NATIFS_UI_20261023.md`

Pas de modification partagée hors zone nécessitant un patch proposé. Les fichiers réservés Claude, `LaunchService.Packets.cs`, `GivreLuaHost.Combat.cs`, Expédition, scripts et catalogue restent intacts.
