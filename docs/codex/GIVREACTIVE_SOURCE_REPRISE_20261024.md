# Reprise du source GivreActive — 24 octobre 2026

Source utilisateur consulté en lecture seule : `%USERPROFILE%/Downloads/givre-GivreActive/Videos/GIVRE/GivreActive`.
Application réelle : `EasyBot/SwiftBot.vbproj`, VB.NET / WinForms. Les répertoires de dépendances et de décompilation ne constituent pas l'application à porter. Aucun lancement de l'ancien bot, aucune donnée de compte importée.

Destination : branche récente `codex-dakal-20261024`, pas le checkout `master`.

## Premier comportement repris : identité après reconnexion en combat

Dans `EasyBot/PacketAnalyser/PacketAnalyser.vb`, `paq153` initialise l'identifiant, le nom, le niveau, la classe et l'apparence directement depuis `CharacterSelectedSuccessMessage`. Il ne dépend pas d'une liste préalable de personnages pour identifier celui que le serveur a sélectionné.

Dans le clone, le traitement `CharacterSelectionEvent` ne lisait que le niveau. Le correctif inachevé récupérait aussi l'identifiant, mais cherchait encore le nom et la classe dans la liste du compte. Il pouvait donc laisser l'identité incomplète lorsque cette liste n'avait pas été reçue lors d'une reconnexion en combat.

Adaptation au protocole actuel, sans recopier les numéros de champs de l'ancien client :

- `CharacterInfoReader.ExtractSelectedId` lit l'identifiant dans le même sous-message Character que les informations de sélection, via le catalogue de champs.
- La confirmation serveur transmet aussi ses informations à `AdoptCharacterId` : nom, classe et niveau disponibles sans liste préalable.
- Une identité reçue après l'identifiant complète le personnage déjà reconnu ; les notifications identiques ne sont pas répétées.
- Le filet de sécurité du combat n'adopte qu'un combattant dont l'identifiant appartient à la liste des personnages du compte. Un autre identifiant ne remplace pas un personnage déjà connu.

Régressions : confirmation sans liste, identité tardive, liste contenant plusieurs personnages, autre combattant, isolation entre PID, identifiant manquant/négatif et sous-message sans informations Character. Ce sont des scénarios hors ligne ; ils ne prouvent pas encore que la reconnexion réelle rejoue le prochain tour.

Validation finale : build Release de l'application réussi (207 avertissements existants, zéro erreur) ; suite complète **2 721 réussites, zéro échec, 2 ignorés**. Clés fr/en/es uniques et `git diff --check` sans erreur. Compilation isolée dans `artifacts/reprise-20261024`, puis assemblages de tests placés dans leur disposition habituelle `GivreClone.Tests/bin/Release/net10.0` : plusieurs tests de source exigent cette disposition. La première tentative hors dépôt échouait sur cette recherche de fichiers, pas sur les nouvelles régressions. La DLL applicative nouvellement compilée a été copiée dans la sortie des tests avant l'exécution finale. L'instance applicative PID 6740 est restée ouverte et inchangée ; aucun essai de reconnexion réel forcé.

## Fonctions utiles : comparaison ciblée, pas audit exhaustif

| Source ancien projet | Équivalent déjà présent dans le clone | Suite utile |
| --- | --- | --- |
| `Main/DesignSwift/UserProductivityServices.vb` : `RecentScriptStore` (récents, épingles, recherche) | `BotsViewModel.LuaEditor.cs` : récents persistés, ouverture et bibliothèque locale | Conserver l'existant ; l'épinglage reste une amélioration à examiner. Ne pas créer une deuxième bibliothèque. |
| `Main/DesignSwift/MainformSwift.vb` : centre de commandes et historique de huit actions | `MainViewModel.QuickNavigation.cs`, catalogue `UiNavigation` : navigation Ctrl+K | L'historique des actions peut enrichir la recherche existante ; pas besoin de recréer un centre de commandes. |
| `Main/DesignSwift/GivreKnowledgeCenter.vb` : catalogue Lua par réflexion | `Engine/LuaTooling`, `Services/LuaEditor/LuaLanguage.cs` : catalogue, linter, coloration, diagnostics et autocomplétion | Réconcilier les capacités annoncées avec les API rendues explicitement indisponibles par F07 ; ne pas importer les anciennes signatures sans vérifier le binding réel. |
| `UserProductivityServices.vb` : `RoutePreflightAdvisor` et `LuaScriptInspector` | `BotsViewModel.LuaEditor.cs` : syntaxe contrôlée avant lancement et diagnostics manuels | Reprendre les diagnostics de fichier IA absent/illisible et d'état courant avant départ. Le contrôle doit suivre les mêmes règles de résolution que le runtime, sans exécuter le script pour l'inspecter. |
| Même fichier : `TeamReadinessReporter`, `FleetConfigDriftReporter`, `FleetHealthReporter` | Équipes, planning, supervision et reconnexion existants ; correctifs F13 livrés séparément | Ajouter des causes précises à la supervision existante après intégration/relecture de F13. Pas de deuxième orchestrateur. |
| `PacketAnalyser.vb` : `PreserveAuthoritativeSpellSnapshotForReconnect` / `RestoreAuthoritativeSpellSnapshotForSelectedCharacter` | Le clone nettoie les états par PID lors de `LaunchService.Forget` | Étudier une sauvegarde des sorts liée à l'identité exacte, uniquement si les journaux prouvent qu'ils manquent à la reconnexion. Ne pas conserver aveuglément les états d'un PID réutilisé. |

## Ce qui ne doit pas être importé tel quel

- Les trames et types historiques correspondent à un autre client. Utiliser le mapping courant du clone.
- L'ancien projet contient aussi des méthodes non opérationnelles, documentées dans son propre `GIVRE_PARITY_PLAN.md` ; sa présence dans le source n'est pas une preuve de fonctionnement.
- Les contrôles WinForms dépendent des singletons de l'ancien bot. Porter les comportements dans les services/viewmodels Avalonia, pas les formulaires entiers.
- Les identités, inventaires, sorts et actions différées doivent rester liés au bon personnage et à la bonne session. Une restauration de cache ne vaut pas confirmation serveur.

## Travaux laissés visibles

F06 a identifié trois défauts encore distincts : attente inutile après une porte réussie, attente supplémentaire après refus de saut, action d'arrivée envoyée après arrêt d'un script. Ils sont détaillés dans `F06_VALIDATION_20261024.md` et ne sont pas corrigés par ce travail sur l'identité.

F13 dispose d'une livraison séparée sur `codex-f13-20261024` (`d47f7e0`), à relire et intégrer sans écraser les travaux présents. Le présent lot n'annonce pas son intégration.
