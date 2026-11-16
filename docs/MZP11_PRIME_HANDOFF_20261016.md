# MZP11 — version locale réelle Dofus et contrats Thrift Launcher 2

## RESULT

Le client Dofus installé le 16 octobre 2026 a un `manifest.cytrus` **GZIP contenant une vraie base SQLite**. La seule ligne `game` indique `gameUid=dofus`, `releaseName=dofus3`, `platformName=windows` et `releaseVersion=6.0_3.7.11.11`, soit **version de jeu installée 3.7.11.11**. Cela corrige l'ancien ciblage statique 3.7.10.10. La ressource PE `6000.3.16f1` était la version Unity, pas la version Dofus.

MZP11 **ne constitue pas un clone jouable** : aucun flux de connexion serveur, personnage, carte, action, ACK, combat, HDV, banque ou reprise H24 n'a été validé sur une instance TEST authentifiée. GivreRework G0 reste `NOT_COMPARED`.

## CHANGES

- `GivreClone.App/Services/MZP11CytrusVersionProbe.cs` : lecteur de production du GZIP/SQLite local, ouverture du seul fichier manifeste à côté du `Dofus.exe` choisi, décompression bornée à 128 MiB, original verrouillé en lecture seule. Vérifie `gameUid`, release, plateforme, unicité de la ligne et syntaxe `6.0_3.7.11.11` ; calcule SHA-256 de l'original. Copie SQLite temporaire dans un dossier GUID exclusif de `%TEMP%` puis détruite sans modifier l'installation. **Métadonnées locales uniquement, pas de preuve serveur**.
- Dépendances `Microsoft.Data.Sqlite.Core 8.0.10` + `SQLitePCLRaw.bundle_winsqlite3 2.1.6`, explicitement initialisées. Utilise `winsqlite3.dll` de Windows, **pas** l'ancien bundle natif `SQLitePCLRaw.lib.e_sqlite3` signalé vulnérable NU1903. La première compilation sur le bundle natif avait émis NU1903 ; il a été remplacé puis restauré et testé. Voir documentation de Microsoft sur les bundles SQLitePCLRaw.
- `GivreClone.App/Services/MappingCatalog.cs` : lors de `EnsureFetchedAsync`, lit réellement la version installée et demande le build exact (3.7.11.11 aujourd'hui, sans constante). Après une réponse mapping signée, revérifie SHA du manifeste et version avant tout fold/publication ; refuse changements d'installation, fallback, version imprécise, absence de manifeste, erreur de version et absence d'artefacts complets. Aucune licence contournée, aucun ACK inféré.
- `GivreClone.App/Services/Transport/Socket/DofusClientVersion.cs` : privilégie la version du manifeste (fallback historique désormais candidat 3.7.11.11). Ce candidat ne remplace pas la négociation/identification réelle du serveur.
- `GivreClone.App/Services/MZP4LauncherPreflight.cs` : l'ancien candidat de TEST 3.7.10.10 est remplacé par 3.7.11.11. Le préflight source-only reste une candidature, jamais une autorisation de lancement.
- `GivreClone.App/Views/MZP10MappingVerificationPanel.cs` : le bouton existant `Revérifier ... (sans réseau)` inspecte désormais réellement le manifeste local et affiche version/train en précisant explicitement **pas d'ACK serveur**. La page réelle `AccountsView.axaml` reste connectée.
- `tools/MZP11CytrusProbeHarness/` : utilise la véritable App et la même classe de production, crée uniquement des fixtures SQLite/GZIP sous un dossier TEMP guidé : version normale, version 3.8, mauvaise identité, lignes dupliquées, manifest malformé, absence de fichier, digest, et **inspection lecture seule du client réellement installé**.
- `tools/MZP11Launcher2ContractHarness/verify.py` : inspection bornée d'**un seul** fichier (`node_modules/protocol-generated-nodejs/ZaapService.js`, 150459 bytes) à l'intérieur du nouvel ASAR de Launcher 2 (sans exécuter JS). Compare cinq ensembles de types/numéros : `connect` champs `gameName STRING/1, releaseName STRING/2, instanceId I32/3, hash STRING/4`; `auth_getGameToken` champs `gameSession STRING/1, gameId I32/2`; variante `WithWindowId` avec `windowId I32/3`; réponses avec `success STRING/0, error STRUCT/1`. Cette validation **n'est pas** une réponse authentifiée du launcher.
- `tools/MZP10MappingUiHarness` et `tools/MZP4LauncherHarness` mis à jour aux nouvelles versions réelles (source originale toujours utilisée dans leurs tests).

## VALIDATION

- `dotnet run --project tools/MZP11CytrusProbeHarness/MZP11CytrusProbeHarness.csproj -c Release -r win-x64`: **10 PASS**, réel manifeste `3.7.11.11`, hash du manifeste `30117D42F6FAA1230985B58CB4865384E348C5FCCF93573AACAAA50A00B94C94` ; ni réseau ni client lancé.
- `py -3 -B tools/MZP11Launcher2ContractHarness/verify.py`: **5 contrats PASS** contre le code généré dans l'installation autorisée Launcher 2.
- `tools/MZP10MappingUiHarness` : **8 checks Headless PASS** sur la vraie App ; la page ne prétend pas à un login ni un gameplay.
- `tools/MZP4LauncherHarness` : **35/35 PASS** sur le préflight source original mis à jour.
- `tools/MZP10MappingVersionHarness` : **29 PASS** ; `tools/MZP9ZaapThriftHarness` : **50 PASS** ; `tools/MZP8LauncherSafetyHarness` : **29 PASS**.
- Projet historique `GivreClone.App` Win-x64 Release : **0 erreur** (contrôle incrémental 0 warning), Debug : **0 erreur**, 144 warnings connus dont absence explicite du dossier `data/`; aucun `NU1903` après passage au SQLite Windows.
- Les commandes build, tests et audits ont été effectuées sans toucher ni aux trois bots de GivreClone original, ni aux comptes, processus, dumps, clés ou jetons Ankama, ni à l'installation originale.

## BLOCKERS / NEXT

1. **Aucun corpus inbound serveur certifié pour Dofus 3.7.11.11** et aucune comparaison de deux décodeurs véritablement indépendants. L'hypothèse de stabilité de la famille 3.7.x.x reste non certifiée au niveau gameplay.
2. Le vrai `GivreClone.App` a toujours un launcher d'instance TEST non admis, un Zaap MITM 5658/26666 qui doit rester protégé contre les sessions GivreClone originales et aucune preuve d'authentification officielle.
3. Reconstituer la chaîne par parcours utilisateur officiel d'une **instance TEST distincte**, associée à l'autorité indépendante, puis prouver `session → réponse corrélée → personnage → carte → action protégée → réponse UID`. Ne pas réutiliser un mot de passe communiqué en chat ni consulter les comptes déjà ouverts dans Launcher 2.
4. Ensuite seulement débloquer les états gameplay, combat, récolte, banque, HDV, Lua, planning et H24 avec tests Windows et session réelle. Refaire les tests de version à chaque nouveau patch Cytrus.
