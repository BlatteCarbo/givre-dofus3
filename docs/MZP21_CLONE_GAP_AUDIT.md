# MZP21 — audit de l'écart de démarrage et du MITM du clone local (2026-10-16)

## R — résultat vérifié par lecture des sources

Le clone `GivreClone.App` compilé en local a déjà un chemin de démarrage autonome : `MZP19LocalClone.Enabled = true` (`GivreClone.App/Services/MZP19LocalClone.cs:13-22`). `Program.Main` démarre Avalonia (`GivreClone.App/Program.cs:11-29`), `App.OnFrameworkInitializationCompleted` prépare un répertoire privé avant l'initialisation des magasins (`App.axaml.cs:14-23`), puis `MainWindow.OnOpened` sélectionne `VersionVerdict.Permissive` au lieu de `LicenseService.CheckVersionAsync` (`Views/MainWindow.axaml.cs:125-148`). Après le splash, l'entrée du shell est directe ou passe par le choix de langue (`MainWindow.axaml.cs:167-195,426-435`). Le test précédent de l'UI réelle est documenté dans `docs/MZP19_LOCAL_131_STARTUP_20261016.md:16-24`; cet audit n'a pas relancé l'application.

Les libellés « Service indisponible » et « MITM TEST indisponible » ont **des causes différentes** :

1. **Erreur historique de démarrage** : dans la branche hors clone, `LicenseService.CheckVersionAsync` requiert un verdict `/version` signé et renvoie `VersionVerdict.FailClosed` sans clé vérifiable ou après échecs réseau/signature (`Services/LicenseService.cs:640-703`). `MainWindow.ShowControlBlock` transforme ce verdict en overlay bloquant, dont le titre français est `L.Control.KillTitle = Service indisponible` (`Views/MainWindow.axaml.cs:640-675`, `Lang/fr.axaml:65`). Le correctif MZP19 concerne seulement la distribution locale ; le texte historique reste présent dans les ressources. Vérifier **quel EXE a été exécuté** avant d'attribuer ce message à la build courante.
2. **Refus MITM actuel, exact et déterministe** : `Services/LaunchService.cs:50-53` construit `new ZaapEmulator(26666)` et `new GameLauncher()` sans fournisseur d'autorité. Au début de `LaunchAsync`, lignes 441-452, `!_launcher.HasIndependentTestAuthority || !_zaap.HasIndependentTestAuthority` renvoie immédiatement `LaunchOutcome(false, 0, "MITM TEST indisponible : launcher indépendant et protocole Zaap TEST non admis...")`. `GameLauncher` a besoin d'un `IIndependentTestGameLaunchAuthority` (`GivreLauncher/GivreLauncher.Core/Launch/GameLauncher.cs:12-37`). `ZaapEmulator.HasIndependentTestAuthority` exige une autorité ET interdit 26666 (`GivreLauncher/GivreLauncher.Core/Zaap/ZaapEmulator.cs:25-59`). **Même en fournissant une autorité à `GameLauncher`, le port 26666 empêcherait cette instance Zaap de passer le prérequis.**
3. **L'ancien chemin de lancement ne devient pas local en ouvrant le shell** : après le garde précédent, `LaunchService` utilise potentiellement `ZaapKeydataReader.ReadAllAsync` ou l'authentification historique, démarre un `HandshakeRelay` sur **5658** et invoque le lancement de client (`LaunchService.cs:459-555`). Le chemin UI historique extrait par ailleurs des jetons depuis `Stores.Accounts` (`ViewModels/AccountsViewModel.Launch.cs:400-467`) ; il n'est pas câblé au magasin d'alias TEST. La version visible du shell n'autorise donc aucune réutilisation de ce chemin pour le compte poubelle.
4. **Le parcours TEST affiché n'est encore qu'un prévol** : `Views/MZP4TestAccountsPanel.cs:52-72,115-128` ajoute le magasin TEST, prévol, journal et navigateur WebAuth. `CheckLauncherPreflight` appelle `MZP4LauncherPreflight.Evaluate`, puis annonce explicitement « lancement désactivé » (`:294-320`). `MZP4LauncherPreflightResult` force `MayLaunchClient`, `MayStartHost`, `MayInject`, `Authenticated` à `false` (`Services/MZP4LauncherPreflight.cs:58-67`). Ce panneau n'appelle pas `LaunchAsync` et ne comporte aucune commande de lancement de client.
5. **Même hôte isolé, autre refus déterministe** : `MZP2DedicatedBotHostPort.CanBindProtectedEngine(...) => false` (`Services/MZP2OwnedHostAdmission.cs:31-48`) ; `StartOwnedTestHost` retourne `HostTransportBindingUnavailable` avant `Start` (`:216-250`). `BotHostService.Start` requiert un permis, une factory gardée, un port et un magasin dédiés (`Services/BotHostService.cs:25-76`). `GivreCore.Mitm.TcpProxyServer` n'écoute qu'avec une route loopback TEST et une `ITestProxyRouteAuthority` (`GivreProxy/GivreCore/Mitm/TcpProxyServer.cs:16-35,77-111`). L'injecteur n'a pas de permis par défaut (`GivreProxy/GivreCore/Injection/DllInjector.cs:8-29,49-113`). Aucune preuve de session ou réponse positive réelle n'est câblée à l'hôte.
6. **Le patch 3.7 n'est plus ce premier blocage** : `MZP4LauncherPreflight.EvaluateAtAnchor` utilise `MZP0ProtocolAdmission.Supports37Family` (`Services/MZP4LauncherPreflight.cs:99-115`), suite au changement MZP21 décrit dans `docs/MZP21_DOFUS_37_FAMILY_ADMISSION_20261016.md`. Le protocole 3.7.11.11 reste candidat SHA-pinné, sans preuve de wire inbound, d'ACK ou d'effet de jeu. Un port de test dédié, différent de 5657/5658/26666, et un répertoire TEST à ACL privées sont obligatoires.

## Sources présentes / packaging

- Racine source : `%USERPROFILE%\Videos\GIVRE\GivreShareWork` ; solution `GivreClone.slnx` (Protocol, Engine, App, Tests, RelayAgent et `../GivreProxy/GivreCore`). Références directes `GivreLauncher.Core` dans `GivreClone.App.csproj:69-74`.
- Composants voisins : `GivreProxy/GivreCore` et `GivreLauncher/GivreLauncher.Core` sont des **reconstructions partielles**, selon leurs `PROVENANCE.md`, pas des modules originaux certifiés. Certains textes de provenance sont antérieurs à l'ajout de `GameLauncher` et `ZaapEmulator` : consulter les `.cs` actuels pour le contrat réel.
- Distribution reconstruite inspectée par listing uniquement : `GivreShareWork/artifacts/MZP21Local37Build/GivreClone.exe`, DLL GivreCore/Launcher et dossiers `WebAuth`, `WebAuthTest`; une autre build existe sous `artifacts/MZP20LocalBuild`. Ces chemins sont des **artefacts de build**, pas l'exécutable original installé ni une preuve que l'utilisateur a lancé le dernier. L'espace local `%USERPROFILE%\AppData\Local\GivreReplica\TestRuns\givre-app-offline` contient `userdata` et le magasin `mzp3-test-profiles.json` ; aucun contenu de compte n'a été lu pendant cet audit.
- Le `README.md:38-50` décrit encore un ancien démarrage automatique du proxy 5658 et l'injection de `GivreKama.dll`, désormais incompatible avec les gardes source actuels. Ne pas l'utiliser comme preuve que la build moderne peut déjà démarrer le MITM.

## C — corrections de production à effectuer dans un lot attribué à la prime

1. **Séparer l'UI TEST de l'ancien compte Givre** : brancher dans `MZP4TestAccountsPanel` un contrôleur propre au clone utilisant l'identité du profil `MZP3TestAccountService`, les preuves WebAuth de sa session et le journal privé. Ne jamais router ce bouton par `AccountsViewModel.LaunchCoreAsync` ou par l'ancien `Stores.Accounts`; conserver les prévols et leur statut diagnostique.
2. **Fournir un lancement Launcher 2 authentique** : adapter `IIndependentTestGameLaunchAuthority` à une véritable session de jeu créée pour le compte TEST, avec `ProcessStartInfo` déterminé depuis la configuration de release, SHA de l'EXE, handle enfant et revalidation avant spawn. Le `GameLauncher` actuel n'accepte que le nom `Dofus.exe` (`GameLauncher.cs:61-117`), tandis que l'étude de Launcher 2 montre que l'exécutable peut être `zaap-start.bat` selon la configuration : déterminer la recette effective avant d'assouplir ce contrôle. Voir `docs/MZP20_LAUNCHER2_STATIC_PROCESS_AND_TICKET_20261016.md:39-101,183-207`.
3. **Retirer les constantes de transport du chemin TEST** : injecter un `ZaapEmulator` paramétré sur le port TEST privé avec `IIndependentTestZaapWireAuthority` fondée sur la vraie session, et injecter un `GameLauncher` muni d'une autorité indépendante. Utiliser le même choix de ports dans le relais, le prévol et le journal ; ne jamais reprendre les ports Givre 5657/5658/26666. Refuser les chemins configurés non authentifiés et toute collision de port. Le constructeur actuel des champs `readonly` de `LaunchService` est le point d'injection à refactoriser.
4. **Câbler le vrai hôte et sa réponse** : connecter l'hôte privé à `BotHostService.StartBound` avec `ProtocolCatalog`, `IFrameSink` et `IPacer` issus de cette connexion ; implémenter `IMZP2OwnedHostPort.CanBindProtectedEngine` à partir de preuves de source/session et de réponse serveur, sans remplacer son `false` par une constante `true`. `MZP1OwnedClientPermit`, `IMZP0AuthenticatedSessionSource`, `IMZP0PositiveResponseVerifier` et `ITestProxyRouteAuthority` doivent être matérialisés par cette intégration, pas par un PID choisi dans l'UI.
5. **Rendre les commandes et Lua exploitables uniquement après admission réelle** : `LaunchService.RelayForInject` dépend toujours de `LicenseService.CanBot` (`LaunchService.cs:117-129`), et plusieurs ViewModels testent `LicenseService.IsAuthorized`. Le clone local n'ouvre plus la session de licence historique, donc introduire un contrat distinct de capacité **TEST vérifiée** lié à l'hôte et à la session authentifiés ; garder le refus lorsque la source, l'ACK, la version ou la session sont incertains. Tester la révocation, le crash et le redémarrage avant d'activer scripts et déplacements.
6. **Éviter les diagnostics trompeurs** : faire afficher séparément « prévol local OK », « Launcher 2 TEST manquant », « Zaap TEST manquant », « transport MITM non lié », « protocole candidat », et « réponse serveur non vérifiée ». N'annoncer « opérationnel » qu'après un lancement détenu par le clone et un aller-retour sur le compte TEST.

## V — commandes de test ciblées (lecture des sources, non exécutées dans ce lot)

Depuis `%USERPROFILE%\Videos\GIVRE\GivreShareWork` sur Windows avec .NET 10 :

```powershell
dotnet build GivreClone.slnx -c Release -m:1
dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release -m:1
dotnet run --project tools/MZP4LauncherHarness/MZP4LauncherHarness.csproj -c Release -m:1
dotnet run --project tools/MZP4AccountsUiHarness/MZP4AccountsUiHarness.csproj -c Release -m:1
dotnet run --project tools/MZP8LauncherSafetyHarness/MZP8LauncherSafetyHarness.csproj -c Release -m:1
dotnet run --project tools/MZP9ZaapThriftHarness/MZP9ZaapThriftHarness.csproj -c Release -m:1
dotnet run --project ../GivreProxy/GivreCore/tests/GivreCoreProtobufContractHarness.csproj -c Release -m:1
dotnet run --project ../GivreLauncher/GivreLauncher.Core/tests/GivreLauncherTransportHarness.csproj -c Release -m:1
```

Commandes de diagnostic ciblé sans exécution de l'application :

```powershell
rg -n -F 'MITM TEST indisponible' GivreClone.App/Services/LaunchService.cs
rg -n -F 'HasIndependentTestAuthority' GivreClone.App/Services/LaunchService.cs ../GivreLauncher/GivreLauncher.Core
rg -n -F 'CanBindProtectedEngine' GivreClone.App/Services/MZP2OwnedHostAdmission.cs
```

Les validations précédentes sont **rapportées** (non reproduites ici) dans `docs/MZP19_LOCAL_131_STARTUP_20261016.md`, `docs/MZP18_ISOLATED_TEST_BROWSER_AND_LAUNCHER_BLOCKER_20261016.md` et `docs/MZP21_DOFUS_37_FAMILY_ADMISSION_20261016.md`. Ce lot n'a exécuté ni test, ni app, ni hook, ni lancement de compte et n'a modifié aucun code de production. Un build seul prouve la compilation, pas la connexion gameplay.

## B — limites factuelles

Il manque encore une création TEST indépendante de processus Dofus 3.7.11.11 avec ticket authentifié Launcher 2, une route Zaap validée, la propriété OS de la connexion, le protocole runtime attribué au bon client et une réponse serveur/ACK corrélée. La cible `v1.3.1` est une étiquette de reconstruction et non une preuve de parité binaire. Aucune conclusion de fonctionnement MITM réel ou de parité feature-by-feature ne découle des sources ni de l'UI locale accessible.
