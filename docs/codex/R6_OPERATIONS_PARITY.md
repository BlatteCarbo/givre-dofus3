# Round 6 / Prompt 6 — Audit exploitation ciblé

Date : 2026-10-18. Écriture : `%USERPROFILE%\Videos\GIVRE\GivreWork`.
Référence en lecture seule : `%USERPROFILE%\Videos\GIVRE\GivreActive`.

## Résultat

Une lacune reproductible corrigée : une plage persistée avec un masque de jours
invalide pouvait être déclarée utilisable par le garde du scheduler, tout en
n'étant jamais active. Les briques demandées existent déjà dans le clone ; aucun
second scheduler, moteur de banque, éditeur ou système de proxy n'a été ajouté.
La présence des sources ne certifie ni parité exhaustive ni fonctionnement live.

## Références et méthode

Lecture ciblée de `GIVRE_PARITY_PLAN.md`, `prompt-mizan-feature-port.md` et
`MIZAN_REPO_ANALYSIS.md` dans GivreActive, puis vérification des sources listées
ci-dessous. Le plan contient des sections historiques et des preuves de versions
différentes : aucune n'est promue ici en preuve protocole 3.7.11.11. L'analyse
Mizan préexistante est une référence documentaire secondaire ; aucun fichier,
compte, journal ou binaire MizanBot d'origine n'a été consulté.

Les chemins de la colonne référence sont relatifs à GivreActive ; ceux des
colonnes clone et tests sont relatifs à GivreWork. Les tests existants cités
sont des points de couverture retrouvés dans les sources, PAS des résultats
d'exécution de cette passe. Seul le banc R6OperationsHarness a été exécuté.

| Fonction | Référence lue | Clone inspecté et constat | Tests retrouvés / limite |
|---|---|---|---|
| Planning | `EasyBot/PacketAnalyser/Function/MOVE/ScriptLUA/ReadScript.vb:3049` lit `PLANNING` ; documents de parité | `GivreClone.App/Services/Planning.cs`, `PlanningStore.cs`, `ViewModels/BotsViewModel.PlanningConnect.cs:375` : planning hebdomadaire persistant, occurrences stables, affectation de scripts et garde avant déconnexion. Défaut de validation corrigé ci-dessous. | `GivreClone.Tests/PlanningEvalTests.cs`, `PlanningAssignmentTests.cs`, `PlanningSchedulerTests.cs` ; nouveau banc isolé exécuté. |
| Reconnexion | `EasyBot/Sockets/MITM.vb:4904`, `:5207` et appels dans `ReadScript.vb` | `GivreClone.App/ViewModels/BotsViewModel.Reconnect.cs`, `AccountsViewModel.Socket.cs` : prérequis avant coupure, reprise du script, génération de session, backoff automatique ; déconnexion imposée par le serveur parquée jusqu'à action manuelle. Aucun port générique supplémentaire justifié. | Tests de planning pour décisions pures ; pas de test dédié de toute la reconnexion UI identifié dans cette passe. Aucun réseau exercé. |
| Multi-compte et proxy par compte | `EasyBot/web/proxy/SocksProxy.vb` ; appels équipe `ReadScript.vb` ; analyse documentaire | `GivreClone.App/ViewModels/AccountsViewModel.cs` : proxy supprimé conservé comme sélection manquante, garde `MultiAllowedFor`/`RouteReady` dans `BotsViewModel.Reconnect.cs` ; `Services/Transport/Socket/SocketProbe.cs` : proxy de jeu distinct ; `Services/LocalLaunch/LocalHandshakeRelay.cs` refuse le proxy non pris en charge. | `GivreClone.Tests/ProxyImportParserTests.cs`, `LocalHandshakeRelayTests.cs` : parseur et relais, pas une preuve d'isolation réseau de bout en bout. |
| Pods | `EasyBot/PacketAnalyser/Function/MOVE/ScriptLUA/PluginsScript/inventory.vb:16` (`pods`, `podsMax`, `podsP`) | `GivreClone.App/Services/GivreLuaHost.cs:440`, `:511`, `GivreLuaHost.Bindings.cs` : seuil `MAX_PODS`, lecture du modèle, maintenance puis branche banque ; `GivreLuaHost.SnowEconomy.cs` : compatibilité `podsP`. Pas de duplication utile. | Les tests de dépôt ci-dessous couvrent les reçus, pas tout le cycle seuil/route/maintenance. État inconnu et reprise après transfert à confirmer dans une future validation d'intégration. |
| Banque | `EasyBot/PacketAnalyser/Function/interactif/Usebank.vb`, `PluginsScript/inventory.vb:28`, `scripts/Product/AstrubPious/BankAstrub.lua` | `GivreClone.App/Services/LaunchService.Bank.cs`, `GivreLuaHost.Bank.cs`, `GivreLuaHost.Route.cs:313` : ouverture, dépôt, retrait, fermeture et route `npcBank`. Les reçus utilisent des UID/inventaire ; ne pas remplacer par une simple attente de baisse des pods. | `GivreClone.Tests/MZP25BankReceiptTests.cs`, `MZP27BankDepositTrackerTests.cs`. Référence supplémentaire lue : `GivreActive/tools/Test-HarvestCraftBankOrchestration.ps1`, non exécutée. |
| Craft | `EasyBot/PacketAnalyser/Function/MOVE/ScriptLUA/PluginsScript/craft.vb` : `PutItem`, `ChangeQuantityToCraft`, `Ready` | `GivreClone.App/Services/LaunchService.Craft.cs` : séquence d'établi et verdict serveur ; `Services/LotM/CraftPlanner.cs` : graphe récursif, stock partagé consommé une fois, cycles et overflow ; `LotMLuaModule.Craft.cs` : plan Lua local. Le craft n'est pas absent. | `GivreClone.Tests/CraftPlannerTests.cs`, `CraftInventoryTests.cs`, `LotMLuaModuleTests.cs`. Pas de validation live recette/atelier/multicraft. |
| Simulateur | `MIZAN_REPO_ANALYSIS.md`, section 9.4 ; aucune implémentation équivalente établie par cette passe dans GivreActive | `GivreClone.App/Services/CombatSimulator.cs` lu uniquement : simulateur offline présent, terrain simplifié et dégâts approximatifs documentés. Aucun changement combat. | Aucun test dédié référençant `CombatSimulator` trouvé dans `GivreClone.Tests/*.cs` via recherche récursive ; audit fonctionnel complet hors périmètre. |
| Éditeur Lua et complétion | `EasyBot/Main/DesignSwift/BotformSwift.vb:2090` : sélecteur API avec autocomplétion | `GivreClone.App/Controls/LuaEditor/LuaCodeEditor.cs:549` : complétion contextuelle, membres/globaux/locaux et snippets ; `Services/LuaEditor/LuaLanguage.cs` : catalogue embarqué et compilation de syntaxe sans exécution ; `GivreClone.Engine/LuaTooling/LuaCallContext.cs`, `LuaApiCatalog.cs` : contexte et API. Pas de nouvel éditeur à créer. | `GivreClone.Tests/LuaTooling/LuaCallContextTests.cs`, `LuaApiCatalogTests.cs`, `LuaLinterTests.cs`, `LuaContentTests.cs` ; interaction Avalonia non exercée. |

`Sale.vb` a seulement été lu comme référence de séquencement des acquittements
existants. Aucune modification HDV et aucun port d'identifiant historique effectué.

## Lacune corrigée : cohérence des plages utilisables

Avant : `PlanningEval.HasUsableSlot` testait seulement `Days != 0`.
Pour `{ Days = 128, StartMin = 600, EndMin = 720 }`, il rendait `true`, alors que
`ActiveSlotIndex` ne pouvait trouver aucun jour actif (seuls les bits 0 à 6 sont
parcourus). `BotsViewModel.PlanningConnect.cs:375` utilise précisément cette
fonction pour conserver une connexion lorsqu'aucune plage n'est utilisable.
Le défaut pouvait donc faire franchir ce garde avec un planning invalide ; aucune
déconnexion réelle n'a été provoquée pour le démontrer.

Après : un prédicat commun exige `1 <= Days <= 127` et
`0 <= StartMin, EndMin < 1440`. Il est utilisé par `HasUsableSlot`,
`ActiveStartDay` et `WeekIntervals`. Les masques mixtes contenant un bit hors
semaine sont refusés entièrement, sans troncature silencieuse. Les plages invalides
restent stockées mais sont ignorées par l'évaluation ; aucune réécriture de données.
Les bornes égales signifient toujours 24 heures. Les listes mixtes conservent
l'index et l'identité de leurs plages valides.

Ce correctif concerne des valeurs numériques malformées dans un planning persisté
ou construit en mémoire. Il ne constitue pas une validation complète du JSON
(collections nulles, références nulles, identités dupliquées), ni une modification
des politiques de reconnexion.

## Changements de cette passe

- `GivreClone.App/Services/Planning.cs` : 8 ajouts, 3 suppressions.
- `tools/R6OperationsHarness/R6OperationsHarness.csproj` : banc .NET 10 indépendant,
  lie directement la source de production `Planning.cs`, sans référence App.
- `tools/R6OperationsHarness/Program.cs` : régression et scénarios limites.
- `docs/codex/R6_OPERATIONS_PARITY.md` : ce rapport.

Les modifications Socket de la passe précédente et les fichiers des autres
workers sont conservés. Aucun changement de projet de tests partagé, combat,
HDV, notifications, AppConfig ou MainWindow dans cette passe.

## Preuves exécutées

Commande depuis GivreWork :

    dotnet run --project tools/R6OperationsHarness/R6OperationsHarness.csproj -c Debug --disable-build-servers

Avant correction : échec reproductible `InvalidOperationException: Unusable slot
accepted: days=128, start=600, end=720` dans le banc.

Après correction : sortie 0, trois groupes PASS :

1. Neuf cas de plages invalides (jours 128/-1/129/0, bornes négatives, 1440 et
   extrêmes entiers). Chaque cas est contrôlé sur les 10 080 minutes d'une semaine
   pour l'activation et l'occurrence, plus chevauchement, prochain début et reprise proche.
2. Planning mixte valide/invalide : garde utilisable, index préservé, début affiché,
   proximité de connexion et fin exclusive.
3. Nuit, chevauchement dimanche/lundi et couverture 24/7 sur 10 080 minutes.

`git diff --check -- GivreClone.App/Services/Planning.cs` : sortie 0,
avertissement LF/CRLF uniquement. Pas de build de l'application, de lancement de
jeu, d'accès aux comptes ou secrets, ni d'action live. Pas de staging, commit ou push.
Le serveur LSP C# n'est pas installé ; la compilation du banc vérifie la source
Planning réelle, pas l'intégration Avalonia ou la suite partagée complète.
