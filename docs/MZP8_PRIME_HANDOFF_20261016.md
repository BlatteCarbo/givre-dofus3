# MZP8 — reprise PRIME du véritable GivreClone.App (16 octobre 2026)

## RESULT

Le projet historique `GivreClone.App/GivreClone.App.csproj` a **compilé en Release Windows x64** avec `0 erreur` (`143 avertissements`), **et en Debug Windows x64** avec `0 erreur` (`144 avertissements` dont un explicite sur les mappings absents). Ce résultat ne certifie ni l'authentification Ankama réelle, ni le handshake original, ni l'arrivée carte, ni le gameplay, ni la parité v1.3.1. Le paquet MZP8 est expressément une pré-livraison OFFLINE-ONLY.

Le build portable net10.0 a d'abord été bloqué par disque C: saturé (`MSB3021/MSB3027`), non par erreurs C#. Un build RID `win-x64` a ensuite réussi. Les seuls fichiers supprimés étaient cinq copies de symboles `.pdb` **générées, non suivies par Git** dans `GivreClone.App/bin/Release/net10.0/**` (trois SkiaSharp du build multiplateforme, deux SkiaSharp/HarfBuzz du nouveau build x64). Aucune source, dump, compte ou installation n'a été supprimé.

## CHANGES

- Fournisseur reconstruit `/givre/GivreLauncher/GivreLauncher.Core` : `Launch/GameLauncher.cs` contrôle chemin exact, SHA-256 et autorité TEST indépendante ; refuse tout lancement sans cette dernière.
- `Accounts/Account.cs` et `Ankama/IShieldCodePrompt.cs` alignent les DTO et l'interface interactive Shield sur les vrais consommateurs de `GivreClone.App`.
- `Ankama/AnkamaAuth.cs` construit un réel échange HTTPS HAAPI `CreateToken?game=1` au moyen d'une clé API **déjà légitimement obtenue** ; seul un serveur HTTPS 2xx avec JSON `token` correct peut produire une réponse. Version exacte 3.7.10.10 non prouvée ; le renouvellement, le mint Shield et l'authentification WebView2 officielle ne sont pas reconstitués.
- `Zaap/ZaapKeydataReader.cs` : uniquement fixtures de TEST explicitement autorisés, jamais lecture implicite des comptes originaux ; ce n'est pas le format Ankama réel retrouvé.
- La lecture des fixtures TEST refuse aussi les chemins sortant de `TestRuns`, les jonctions/reparse points de tous les ancêtres sous `%LOCALAPPDATA%`, les doublons d'identité, les champs invalides et les fichiers au-delà de 64 KiB.
- `Transport/HandshakeRelay.cs` : véritable TCP duplex loopback TEST et PID/naissance/génération à autorité externe ; port 5658 protégé, aucune route jeu non attestée. Pas de parser original 26 octets ni d'ACK forgé.
- `Zaap/ZaapEmulator.cs` : registre éphémère des tickets seulement à l'issue d'une authentification légitime, serveur loopback TEST soumis à une autorité indépendante de protocole, refus 26666. **Pas un faux Zaap compatible production**.
- `Proxy/ProxySettings.cs` : `HasAuth` + `ToWebProxy()` et refus des paramètres invalides, réutilisés par les consommateurs historiques.
- `GivreClone.App/GivreClone.App.csproj` : référence vraie `System.Diagnostics.EventLog` v10.0.0 pour le code d'audit Windows préexistant, sans supprimer la fonctionnalité.
- `GivreClone.App/GivreClone.App.csproj` : les cinq contenus `data/` Debug, absents du dépôt détaché et non suivis par Git, ne sont copiés que lorsqu'ils existent ; un warning MZP8 rend l'absence de mapping visible. Cela ne reconstruit aucun mapping.
- `tools/MZP8LauncherSafetyHarness/` : sécurité Launcher/HAAPI/ports/test-replay, tout hors réseau et sans processus live.
- `tools/MZP8Release/package.py` : archive reproductible de sortie Windows x64, liste de types/fichiers contrôlée, exclusions captures, keydata, comptes, dumps, logs, PDB, manifest SHA-256.

**Provenance** : tout nouveau fournisseur est du C# **reconstruit dans cette passe** ; il ne s'agit ni du `GivreLauncher.Core` originel, ni d'une décompilation de l'exécutable v1.3.1.

## VALIDATION

1. `dotnet run --project tools/MZP8LauncherSafetyHarness/MZP8LauncherSafetyHarness.csproj -c Release -m:1 -v:q` : **29 contrôles hors ligne PASS** ; inclut un fixture synthétique TEST, les comptes dupliqués, et le refus des chemins extérieurs. Aucune socket publique, processus ou compte original.
2. `dotnet build ../GivreLauncher/GivreLauncher.Core/GivreLauncher.Core.csproj -c Release --no-restore -m:1 -v:q` : PASS, 0 erreur/avertissement. Même résultat Debug pour le fournisseur avant dernier ajout (à revérifier après celui-ci).
3. `dotnet restore GivreClone.App/GivreClone.App.csproj -r win-x64 --ignore-failed-sources -v:q` puis `dotnet build GivreClone.App/GivreClone.App.csproj -c Release -r win-x64 --no-restore -m:1 -v:q` : **PASS** 0 erreur, 143 avertissements dans la sortie MSBuild. Warnings principalement analyzers CA1416 et Avalonia `Watermark` obsolète.
4. `dotnet restore ... -r win-x64 -p:Configuration=Debug`, puis `dotnet build GivreClone.App/GivreClone.App.csproj -c Debug -r win-x64 --no-restore -m:1 -v:q` : **PASS**, 0 erreur / 144 warnings ; `data/` absent signalé. Une première tentative Debug avait échoué uniquement sur cinq copies de fichiers absents (`MSB3030`), puis correction `Exists()` sans aucun contenu inventé.
5. Régressions réellement relancées : `GivreCoreProtobufContractHarness` 31 PASS, `GivreLauncherTransportHarness` 18 PASS, `MZP7CoreContractHarness` 61/61 PASS, `MZP8LauncherSafetyHarness` 29 PASS. Sous GivreRework `MZP7P0Harness` 52 assertions PASS source-only, **G0 non certifié**.
6. Après durcissement de la lecture TEST, `GivreClone.App` Release `win-x64` **recompilé avec 0 erreur, 143 avertissements**. `tools/MZP8Release/package.py` invoqué **deux fois sur ce dernier binaire** : archives byte-identiques (`PACKAGE_REPRODUCIBLE=True`), SHA-256 **`CB7C233663A04AD898CA24C0A5DAF84EE11C35F3FD03346FE39352B1DBFD380C`**, **23 240 385 octets**, **66 entrées**, vérification ZIP intégrale `testzip()`. Fichier `artifacts/MZP8-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip` et `artifacts/MZP8-Historical-App-Manifest.json`. Cette dernière empreinte remplace celle du paquet MZP8 intermédiaire.
7. `git -c core.safecrlf=false diff --check` : PASS au contrôle intermédiaire. Le dépôt demeure sale préexistant ; pas de `git clean/reset`.

## BLOCKERS

- **Aucun gameplay observé.** Le nouveau `GivreClone.App` compile, mais ne peut pas légitimement ouvrir un Zaap original 26666, un MITM 5658 ni créer de client sans un protocole original prouvé et une vraie identité TEST.
- Aucun vrai login, réponse corrélée, carte, combat, HDV, banque ou reprise H24 testés.
- `AnkamaAuth` ne couvre pas les flux OAuth/refresh/Bouclier non attestés ; endpoint candidat hors validation live. Ne pas essayer avec des secrets existants ni prétendre à l'authentification accomplie.
- Le nombre d'avertissements reste élevé ; réduire les analyzers en ciblant Windows, corriger réellement l'API Avalonia, ne pas simplement masquer des avertissements.
- Les quatre workers sollicités ont échoué à l'ouverture de leurs chats, sans contribution ; PRIME a continué directement. Ne pas relancer ces fenêtres sans corriger d'abord la défaillance.
- GivreRework inchangé durant cette reprise : MZP7 source-only, G0 **NOT_COMPARED**, deux décodeurs/provenance 3.7.10.10 non établis.
- C: s'est trouvé à 0 octet libre pendant le build, puis a fluctué pendant des écritures externes concurrentes. Seuls huit fichiers PDB régénérables sous `GivreClone.App/bin/Release/net10.0/**` ont été retirés de manière bornée ; dernier espace libre observé après recompilation et archives : ~79 Mo. Prioriser une gestion prudente d'espace sans nettoyer d'artefacts utilisateurs.

## NEXT P0

Comparer les vrais appels `LaunchService` aux fournisseurs reconstruits, brancher une **instance TEST séparée** au port non protégé avec identité réelle, retrouver le format Zaap/handshake autorisé, prouver le décodage inbound 3.7.10.10 et le retour serveur avant toute action. Tester Debug, régressions Core/Protocol/Engine, revue des anomalies sécurité et UI Windows sans toucher au GivreClone original (trois bots).
