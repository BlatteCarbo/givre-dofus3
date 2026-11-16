# MZP10 — contrôle de compatibilité patch + constat Launcher 2

## RESULT

Le vrai `GivreClone.App` historique est reconstruit progressivement, mais **reste incapable de lancer une session de jeu authentifiée complète**. MZP10 porte de la nouvelle logique dans la production (contrôle mapping + page Comptes), plutôt qu'un simple rapport. Il **ne** constitue **pas** un clone 100 % fonctionnel de GivreClone v1.3.1.

La propriété supposée « mapping stable entre les `3.7.x.x` » est désormais un **cas de comparaison vérifiable**, pas une autorisation implicite. Une égalité SHA des artefacts reste une candidature tant que deux vrais décodeurs indépendants ou une preuve serveur manquent.

## CHANGES

1. `GivreClone.App/Services/MZP10MappingVersionAdmission.cs` : classe de production pure, versions strictes `major.minor.patch.build`, distingue build exact, famille différente, artefacts modifiés, fallback serveur, absence de preuve et candidat de même famille. SHA-256 à domaines séparés de `proto`, `matching`, `fields`, `confirmed` ; refus des versions invalides, artefacts absents, champs géants. **Jamais d'ACK, de session ou d'action certifiée par un digest.**
2. `GivreClone.App/Services/MappingCatalog.cs` : demande le build exact candidat `3.7.10.10` au lieu de `latest`, confronte la réponse signée/déchiffrée et refuse `fallback`, build incompatible et empreintes incomplètes avant publication en mémoire. Le catalogue reste soumis à `RequiredMapping` et à la licence d'origine ; **aucun bypass licence**. Dernier verdict conservé seulement en mémoire et purgé avec le catalogue.
3. `GivreClone.App/Services/Transport/Socket/DofusClientVersion.cs` : remplace l'ancien fallback `3.6.12.16` par le candidat 3.7 connu `3.7.10.10`, explicitement pas une lecture certifiée du Dofus mis à jour.
4. `GivreClone.App/Views/MZP10MappingVerificationPanel.cs` + `GivreClone.App/Views/AccountsView.axaml` : vrai panneau « Compatibilité des mappings Dofus 3.7 » dans **Comptes**, état catalogue et décision, build cible, bouton « Revérifier version et mapping (sans réseau) ». Ni auth ni login, port, injection, capture ou ouverture de compte sur ce contrôle.
5. `/givre/GivreLauncher/GivreLauncher.Core/Launch/GameLauncher.cs` et `GivreClone.App/Services/DofusPaths.cs` : identifient enfin le nouveau chemin réel de Dofus installé avec Launcher 2, `%LOCALAPPDATA%/Ankama/Games/dofus/Dofus.exe`, en conservant les anciens chemins. Ne lisent jamais profils ni clés Launcher.
6. `tools/MZP10MappingVersionHarness/` : compile le **fichier de production réel**, 29 assertions source-only (cross-major, cross-minor, versions adversariales, drift d'un champ et d'un alias, SHA, fallback, refus du préfixe seul).
7. `tools/MZP10MappingUiHarness/` : `ProjectReference` à la **véritable App** et constructeur du panneau réel sous Avalonia Headless. Vérifie qu'il est monté dans le vrai `AccountsView.axaml`, que le bouton rafraîchit les états sans preuve, et ne peut annoncer un gameplay.
8. `tools/MZP10Launcher2Forensics/inspect_asar.py` et `inspect_protocol.py` : lecture bornée de **métadonnées ASAR et d'un seul fichier protocole statique** de Launcher 2. Pas de processus, pas de compte, pas de clé, pas de dump original ou d'extraction entière. L'archive app.asar n'est pas copiée au dépôt.

## INSTALLATIONS OBSERVÉES, EN LECTURE SEULE

- Launcher 2 enregistré en version **0.14.3**, programme `Ankama Launcher 2.exe` produit `0.14.3.5859`; Launcher historique 3.15.5 également installé, **non manipulé**.
- `Dofus.exe` installé sous `Ankama/Games/dofus`, dernière modification relevée **16/10/2026 10:56**. SHA-256 fichier PE : `1F13781121325CC224BD41B823BBBFDF6BD160390F046D185E602537F5BB57D4`. Ressource PE **`6000.3.16f1` Unity**, qui n'est pas le build protocole Dofus et ne saurait certifier `3.7.10.10`.
- `app.asar` Launcher 2 de ~474 MB : **en-tête uniquement**, 16 426 chemins indexés, dont 284 contenant « zaap », 81 « thrift », 55 « cytrus ». Puis lecture bornée du **seul** `node_modules/protocol-generated-nodejs/ZaapService.js` (150 459 octets) ; les méthodes `connect`, `auth_getGameToken`, `auth_getGameTokenWithWindowId` et les méthodes de paramètres/infos existent réellement. Ceci renforce la preuve de **présence du contrat Thrift** dans Launcher 2 ; pas de compatibilité wire complète ni de handshake/réponse authentifiée en jeu.
- Sources publiques indiquant le dernier jeu visible au 13/10/2026 : **3.7.10.10** (OpenDofusDB). Ces données publiques ne prouvent PAS la version binaire installée au 16/10/2026.

## VALIDATION

- `dotnet run --project tools/MZP10MappingVersionHarness/MZP10MappingVersionHarness.csproj -c Release -m:1 -v:q` : **29 PASS**.
- `dotnet run --project tools/MZP10MappingUiHarness/MZP10MappingUiHarness.csproj -c Release -r win-x64 -m:1 -v:q` : **8 PASS Headless**, sur les classes réelles de la vraie App.
- Régression `MZP4AccountsUiHarness` : **25 PASS Headless** sur le panneau TEST d'origine, sans réseau ni client.
- `dotnet build GivreClone.App/GivreClone.App.csproj -c Release -r win-x64 --no-restore -m:1 -v:q` : **0 erreur, 143 avertissements**, vrai projet WinExe après montage du panneau MZP10.
- `dotnet build GivreClone.App/GivreClone.App.csproj -c Debug -r win-x64 --no-restore -m:1 -v:q` : **0 erreur, 144 avertissements** ; avertissement supplémentaire du data/ absent.
- `py -3 -B tools/MZP10Launcher2Forensics/inspect_asar.py` et `inspect_protocol.py` : PASS, lecture statique bornée du nouveau Launcher 2, aucune extraction massive.
- `py -3 -B tools/MZP10Release/package.py` exécuté **deux fois** après le build Release : ZIP byte-identique (`REPRODUCIBLE=True`), chemin `artifacts/MZP10-Historical-GivreCloneApp-WinX64-OFFLINE-ONLY.zip`, **23 246 999 octets**, **66 entrées**, SHA-256 **`67B2DA80F2A4106883DD3A5F5CE26A7A3D332881F9147DF20E375F1F43D9206F`**, manifeste `artifacts/MZP10-Historical-App-Manifest.json`.

## BLOCKERS / NEXT PRIORITY

- Une version courante exacte du protocole doit être acquise depuis une **source de client versionnée et vérifiée** ; les numéros Unity ne suffisent pas. Un vrai replay de messages inbound versionnés est requis avant de déclarer mapping 3.7.x.x compatible.
- Le launcher en production n'a pas encore de `IIndependentTestGameLaunchAuthority`, ni protocole exact Zaap/handshake, ni instance de compte TEST authentifiée. Les anciens ports 5657/5658/26666 restent interdits car associés au GivreClone original (3 bots actifs signalés).
- Il n'y a pas d'injection TEST réussie, d'ACK serveur, d'entrée carte, de combat ni de validation H24. Les fonctions historiques compilées ne sont pas déclarées utilisables tant que l'authentification / transport / version / provenance ne sont pas effectivement raccordés.
- **GivreRework G0 toujours `NOT_COMPARED`**, pas de deuxième décodeur indépendant exact 3.7.10.10 ni source attestée.
- P0 suivant : faire porter le véritable `ZaapService.js`/Thrift à un adaptateur **local TEST** certifié avec origine vérifiée ; valider l'application contre un protocole de réponses connu, construire le lanceur TEST via authentification officielle interactive, puis prouver login/serveur/personnage/map/ACK sur une session propriétaire isolée, avant combat/banque/HDV.
