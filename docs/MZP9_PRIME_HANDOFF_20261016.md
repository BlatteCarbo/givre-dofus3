# MZP9 — reconstruction produit GivreClone, bilan partiel vérifié

## RESULT

MZP9 prolonge MZP8 dans le **vrai projet historique `GivreClone.App`** et corrige deux risques concrets de la chaîne de lancement et d'arrêt. Le projet a compilé `Release win-x64` avec **0 erreur / 143 warnings**, et `Debug win-x64` avec **0 erreur / 144 warnings** (le warning supplémentaire signale explicitement `data/` manquant).

**Aucun client Dofus, aucune authentification Ankama réelle, aucun bot en jeu n'a été lancé.** Les trois bots de l'installation GivreClone originale sont exclus de tous les tests. Il reste impossible de qualifier la reconstruction de clone jouable ou de parité fonctionnelle v1.3.1. Givre G0 demeure `NOT_COMPARED`.

## CHANGES

1. `/givre/GivreShareWork/GivreClone.App/Services/LaunchService.cs` : une admission précoce refuse le lancement MITM sans autorités TEST indépendantes de `GameLauncher` et `ZaapEmulator`. Le refus précède toute lecture de keydata, toute requête HAAPI et toute création de processus. Le code ne continue plus après un échec `_mitm.Start()`.
2. `/givre/GivreLauncher/GivreLauncher.Core/Launch/GameLauncher.cs` et `Zaap/ZaapEmulator.cs` : capacité TEST explicite, non équivalente à une autorisation de session réelle.
3. `/givre/GivreLauncher/GivreLauncher.Core/Launch/MZP9OwnedClientRegistry.cs` : termine uniquement le **handle `Process` exact** issu de `GameLauncher.Launch` après double admission TEST, et vérifie PID/heure de naissance. Aucun `Process.GetProcessById` utilisé pour arrêter un PID non attribué ; les faux PIDs socket ou PIDs réutilisés ne peuvent pas justifier un arrêt. `LaunchService.TryKill` et `AccountsViewModel.TryKill` sont raccordés à ce registre. L'événement asynchrone `Exited` ne dispose jamais de l'objet `Process` encore utilisé par l'appelant (bug découvert par véritable harness).
4. `/givre/GivreLauncher/GivreLauncher.Core/Zaap/MZP9ZaapThriftBinary.cs` : codec Apache Thrift STRICT binaire, requests `connect`, `auth_getGameToken`, `auth_getGameTokenWithWindowId`, réponse string, budgets, UTF-8 strict, STOP obligatoire, champs attendus, refus des doublons, changements de version et trailing bytes ; préserve désormais explicitement `WindowId` et rejette les identifiants contenant des caractères de contrôle. Ce dialecte est **candidat seulement** : l'IDL public `Valentin-alix/AnkamaLauncherEmulator/resources/zaap.thrift` (blob Git SHA `d6a0bc2d53dc3404dbf48891682ec6a1864cb01f`) a été lu en source publique. L'implémentation C# est indépendante, pas une copie de code Python ni une preuve de protocole client 3.7.10.10. Aucun listener Thrift live n'a été ouvert.
5. `tools/MZP9TransportIntegrationHarness/**` : ouvre uniquement deux ports loopback éphémères maîtrisés par le test. Vérifie duplex TCP réel, observation par direction, revocation de la permission, refus d'injection et d'un fallback avec proxy configuré. Cela n'est pas un ACK Dofus.
6. `tools/MZP9OwnedChild/**` et `tools/MZP9OwnedProcessHarness/**` : démarrent uniquement un petit programme C# factice sous un nom `Dofus.exe` **dans un répertoire TEMP neuf et propre au harness**, avec autorité de test synthétique, puis vérifient son PID/handle propre et son arrêt exact. Aucune connexion jeu et aucun compte. Le premier test a révélé une course `Process.Dispose()`/`Exited` qui a été corrigée et retestée.
7. `tools/MZP9ZaapThriftHarness/**` : décodage/encodage tests syntaxiques Thrift candidats, et attaques adversariales.
8. `tools/MZP9Release/package.py` : permet une pré-livraison déterministe, distincte de MZP8, avec allowlist stricte du projet historique, SHA par fichier et exclusion des secrets/captures/comptes/mappings bruts.

## VALIDATION

| Validation | Résultat | Niveau de preuve |
|---|---|---|
| MZP9 transport intégration | **15 checks PASS** | Boucle TCP synthétique localhost |
| MZP9 test-process owned | **6 checks PASS** | Processus enfant factice créé et arrêté par handle |
| MZP9 dialecte Thrift | **50 assertions PASS** | Encode/decode offline, aucun client Dofus |
| MZP8 sécurité Launcher | **29 checks PASS** | Contrats/fail-closed offline |
| Historique `GivreClone.App` Release win-x64 | **0 erreur, 143 avertissements** | Compilation de l'application entière |
| Historique `GivreClone.App` Debug win-x64 | **0 erreur, 144 avertissements** | Compilation de l'application entière |

**Publication reproductible finale** après modification du dialecte et dernier build Release : `artifacts/MZP9-Historical-GivreCloneApp-WinX64-UNVERIFIED-GAME.zip`, **23 245 070 octets**, 66 entrées, SHA-256 **`356C87BF28A5FFA5C7746799C3F3C6AF38F58A02E0FD679A116B945F657E354C`** ; deux générations identiques (`REPRODUCIBLE=True`). Manifeste `artifacts/MZP9-Historical-App-Manifest.json`. Ceci remplace l'empreinte intermédiaire MZP9 avant l'ajout de WindowId.

Les anciennes régressions Core Protobuf 31, Transport 18, Typed Protocol 61 et Givre MZP7 52 étaient PASS dans le passage précédent ; ne pas les qualifier de nouveaux runs MZP9 sans réexécution.

## BLOCKERS

- **Aucune autorité indépendante de TEST effectivement raccordée** au lancement historique ; ses ports 26666/5658 sont protégés. Le refus de lancement est une protection correcte mais signifie que ce binaire n'est pas encore utilisable pour jouer.
- La version exacte du protocole client Dofus PC **3.7.10.10** (Zaap/handshake/game mapping, source inbound, UID et ACK) n'est pas prouvée. Le service public Thrift 26116 est une piste, pas la preuve du port ni des arguments de l'original v1.3.1 ou du client actuel.
- Auth Ankama WebView2, refresh/2FA/certificat, launcher exact et compte TEST propriétaire indépendamment vérifié non raccordés. Ne pas réutiliser des mots de passe écrits en chat ni consulter le keydata de l'installation Ankama existante.
- Gameplay (carte, combat, inventaire, banque, HDV, Lua H24) : aucun test live, aucune parité v1.3.1 certifiée.
- UI Windows interactive non testée ; compiler l'App ne démontre pas sa navigation live. Workers historiques non démarrés : cette passe est faite en PRIME uniquement.

## NEXT PRIORITY

Valider par source légitime le service Zaap/handshake exact et ses changements 3.7.10.10, puis intégrer l'autorité de lancement d'une **instance TEST distincte** et une authentification **officielle interactive** (compte saisi par l'utilisateur, jamais repris de l'original). Prouver la chaîne `login serveur -> liste serveur -> personnage -> carte -> action -> réponse UID corrélée`, puis seulement déverrouiller fonctionnalités de jeu et sessions H24. Pendant tout le processus, conserver fail-closed, intégrité d'identité, version et révocation.
