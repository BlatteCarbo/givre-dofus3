# Round 6 Prompt 7 — UID opérateur et scanner local

## Changements possédés

- `GivreClone.App/ViewModels/HdvViewModel.cs` : effacement et invalidation des observations en attente au changement de PID.
- `GivreClone.App/ViewModels/HdvViewModel.UidRows.cs` : projection pure des six formats `HdvUidDiagnostic`, expressions ancrées, événements autorisés, limite de longueur et PID numérique valide. UID sac, UID lot demandé/candidat et disposition restent distincts ; aucune corrélation supplémentaire n'est inventée.
- `GivreClone.App/ViewModels/HdvViewModel.UidOperator.cs` : commande de fenêtre unique, collection limitée à 150 lignes, contrôle du PID routé et du PID incorporé, publication sur le dispatcher UI, invalidation des publications après effacement/changement de PID.
- `GivreClone.App/Views/HdvUidOperatorWindow.axaml` et `.axaml.cs` : fenêtre lisible et sélection de base SQLite, abonnement `TransportLog.EmittedWithPid` uniquement pendant son ouverture, désabonnement et effacement à la fermeture. Le callback transmet au VM ; seules les lignes reconnues deviennent des données liées à l'écran. Aucun fichier transport n'est ouvert.
- `GivreClone.App/Views/HdvSellOverlay.axaml` : bouton « Diagnostic UID » dans l'en-tête « Mettre en vente ». La fenêtre reste ouverte indépendamment de cet overlay.
- `GivreClone.App/ViewModels/HdvViewModel.Scan.cs` : chemin explicite vers un dataset local, optionnel et non persisté, utilisé par le scanner de recettes existant à sa prochaine exécution. Vide = base GameData courante. Ouverture par `GameDb.TryOpen`, qui utilise déjà SQLite ReadOnly et Pooling=false.
- `GivreClone.Tests/HdvUidOperatorRowsTests.cs` et `HdvScannerDatasetReadOnlyTests.cs` : compatibilité des six formats Round5, rejet de données ajoutées, distinction candidat/résultat, recettes SQLite et prix manquants, empreinte du fichier inchangée après lecture/calcul.
- `docs/codex/R6_P7_HDV_GUIDE.ps1` : guide humain avec arrêt sur toute réponse autre que OK, mode Preview sans interaction ; aucune commande de jeu/réseau/compte.

## Prérequis et includes

Les fichiers Round5 `Services/HdvUidDiagnostic.cs` et `Services/LaunchService.Hdv.cs` sont réutilisés sans modification par cette passe. Leurs émissions de traces alimentent le callback ; le journal transport doit déjà être activé, la vue ne l'active pas. Aucun payload brut ni prix ni identifiant de compte ne passe le filtre.

Aucun `.csproj` du dépôt n'a été modifié. L'App inclut normalement les nouveaux `.cs` et `.axaml` par ses globes SDK/Avalonia. Le projet de tests lié doit inclure explicitement `../GivreClone.App/ViewModels/HdvViewModel.UidRows.cs` avec un Link, en plus du prérequis Round5 `../GivreClone.App/Services/HdvUidDiagnostic.cs` déjà présent dans le projet sale lu au départ. Les deux nouveaux fichiers de tests sont inclus automatiquement par le SDK. Ces includes sont à intégrer par le responsable du `.csproj` ; les tests de cette passe utilisent un projet temporaire isolé avec includes explicites.

## Persistance vérifiée dans les sources

`HdvViewModel.Trade.cs` branche CollectionChanged achat/relist et PropertyChanged Needed/Allowed/Max/MinPrice/Keep ; SaveTradeRules sauvegarde les intentions utilisateur, RestoreTradeRules restaure les règles au changement de clé personnage/serveur. Qty et Lots sont init-only. `HdvViewModel.Load` active `_loading` pendant purge/restauration et ne restaure les listes qu'au changement de profil. Le clone profond et le JSON sont couverts par les tests existants réexécutés. Aucune lacune supplémentaire nécessitant une écriture dans les services de persistance n'a été établie. Une sauvegarde/restauration à travers un redémarrage réel de l'App reste à vérifier ; le test JSON seul ne la prouve pas.

## Dataset et limites

Le dossier local `proto-3.7.11.11` inspecté ne contient que `dofus.proto`. Aucun vrai SQLite ledouxm 3.7.11.11 n'a été identifié dans ce dossier. Le chemin de dataset est renseignable dans la fenêtre UID ; revenir à Rentabilité et lancer le scanner de recettes existant. Aucun téléchargement ni lecture d'un compte n'est effectué. La version n'est pas certifiée par le nom de fichier. Les recettes sont statiques ; les moyennes et prix de marché viennent du cache du PID/serveur sélectionné et les prix absents restent inconnus. Le scanner de marché avec requêtes réseau n'est pas lancé et n'utilise pas ce nouveau chemin.

`Confirmed` affiché est la disposition déclarée par le moteur, sans certification live additionnelle. La revue manuelle existante conserve ses garde-fous ; la nouvelle fenêtre ne déverrouille aucune opération. Le reprix reste explicitement non corrélé. Pas d'achat, vente, injection, connexion ou action live pendant cette passe ; pas de commit, push ou staging.

## Validation

24 tests ciblés réussis, zéro échec/ignoré, avec les sources actuelles liées depuis un projet temporaire : UID opérateur, persistance existante, scanner existant et nouvelle intégration SQLite synthétique. Résultats : `artifacts/R6-p7-tests/P7.trx`.

La compilation et le routage Avalonia sont vérifiés séparément dans un hôte temporaire, sans build de l'App partagée. Cet hôte utilise les vrais fichiers de projection, commande et fenêtre, avec un VM partiel minimal qui remplace les dépendances de session. Il ne valide donc pas l'intégration complète ni l'apparence réelle dans l'App.

Le pilote Avalonia Headless a passé cinq contrôles exécutés : émission TransportLog jusqu'aux lignes liées, rejet PID routé incohérent/PID étranger/suffixe, effacement invalidant la file dispatcher, plafonnement à 150 lignes, fermeture désabonnant et effaçant. Les vrais fichiers XAML/commande/handler sont compilés ; seules les dépendances session du VM sont remplacées par un hôte minimal. Projet temporaire de reproduction : `%USERPROFILE%/AppData/Local/Temp/givre-r6-p7-57ed3a06a5e04f2fa3d82f00b17151f5/P7.csproj` et son dossier frère `-ui/P7Ui.csproj`. Aucun `.csproj` du dépôt ni fichier Round5 n'a été écrit.

Include à ajouter par le propriétaire du projet de tests :

```xml
<Compile Include="..\GivreClone.App\ViewModels\HdvViewModel.UidRows.cs" Link="Linked\HdvViewModel.UidRows.cs" />
```

Le guide PowerShell ne valide que les déclarations humaines et ne fait pas de transaction. Son invocation de vérification est `./docs/codex/R6_P7_HDV_GUIDE.ps1 -Preview` ; le test de syntaxe utilise `System.Management.Automation.Language.Parser.ParseFile`.

Dernière vérification : recompilation isolée Avalonia réussie après remplacement de Watermark par PlaceholderText, puis 5/5 contrôles Headless réussis. Sortie conservée dans `artifacts/R6-p7-tests/uid-routing.txt`. Syntaxe PowerShell sans erreur et Preview exécuté sans interaction, sortie `artifacts/R6-p7-tests/guide-preview.txt`. `git diff --check` sur les trois fichiers suivis modifiés : code retour 0. Le build complet de GivreClone.App n'a pas été lancé pour laisser le responsable d'intégration sérialiser sa compilation.
