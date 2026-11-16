# MZP25 — Analyse des deux DLL d'origine, reconstruction source-owned et portage GivreActive

## R — périmètre, degré de preuve

Les analyses REA/Ghidra ont été faites **uniquement sur les copies originales détachées** de `GivreMods.dll` et `GivreKama.dll`. Le clone a reçu des composants écrits depuis des sources disponibles et des tests hors ligne. Une fonction disponible dans le code du clone n'est **pas**, par ce seul fait, prouvée équivalente à une fonction d'une DLL originale protégée.

| Binaire d'origine isolé | Empreinte SHA-256 | Décompilation Ghidra vérifiée | Limite |
| --- | --- | --- | --- |
| `GivreKama.dll` (12 551 168 octets) | `4F94B008B13CED359D27B976DDF489CDD92E0F3BDEBF7E2BA29B7ED0F7D067B1` | 1 462 fonctions Ghidra estimées, 5 fragments de pseudocode produits; imports `connect`, `GetAdaptersAddresses`, `BCryptHash` | Gros bloc `.7x[`; sections code standards sans octets raw; entrée atypique `syscall`; pas de routine métier récupérée |
| `GivreMods.dll` (14 305 280 octets) | `B8F95D30B178D4FD3A34B88DAA148F29D62D72371524067DF12CDD6CE06B0343` | 1 958 fonctions Ghidra estimées; pseudocode d'entrée techniquement présent, mais mauvais flot de contrôle; imports `GetClassNameW`, `WriteConsoleW` | Section `.M9P` dominante; pas de fonction de rendu tactique ou d'animation fiable attribuable à l'original |

Preuves exactes, adresses, scripts, logs et limites :
- `docs/MZP25_KAMA_DECOMP.md`, `artifacts/MZP25Kama/`
- `docs/MZP25_MODS_DECOMP.md`, `artifacts/MZP25Mods/`
- `docs/MZP24_MODS_KAMA_REA.md`, `docs/MZP24_MODS_REA.md` pour inventaires PE/REA préalables.

## C — ajouts source-owned compilables

### 1. Mods autonomes et vérification de provenance

La solution embarque désormais les **huit DLL publiées par notre compilation du dépôt Rust MIT `dofus3-native-host` commit `010eaac`** dans `SourceOwnedMods/` à côté de l'application. Il s'agit des modules tactique, suppression des animations, interface analyzer, runtime inspector, network dumper (désactivé par défaut, contient potentiellement des données sensibles), module exemple, et de leur host/bootstrap. Ils ne sont ni tirés du contenu protégé des DLL originales, ni installés dans le jeu, ni chargés automatiquement par ce changement.

Le nouveau `MZP25NativeModCatalog` vérifie leurs SHA-256 et refuse les fichiers inattendus, liens symboliques et chemins hors paquet. Le panneau Réglages affiche la conformité du paquet et rappelle que le chargement dans Dofus reste **non certifié**. `MZP25NativeModCatalog.OwnedCaptureHook` épingle également la DLL `GivreKama.dll` **compilée depuis le code C++ du clone** (actuellement 183 808 octets, SHA `376840BC7DBBEE45920408A19065C7F3246BF259845A79349C82B0699F08E795`) ; elle est distincte de la DLL d'origine. Le build local comporte une modification contrôlée de `GivreKama.cpp` pour sélectionner le port via `GIVRE_RELAY_PORT` (source SHA `00B9BF03191CAA5C2C3532D8A53281DEB844574A02F7CE949FDCEE1961131662`). Un SHA source/binaire ne valide pas la redirection en jeu.

Le clone local impose `MZP25NativeModOwnership` avant écriture de `mods-<PID>.ini`, injection du hook, invocation d'injection legacy et activation du runtime natif. L'autorité exigée est la génération de processus lancée réellement par la factory TEST du clone, via `MZP9OwnedClientRegistry.IsOwnedAndLive`. L'ancienne DLL empaquetée `GivreMods.dll` n'est plus admise comme module de remplacement dans le chemin local.

Les destinations d'écriture ont également été isolées : `MZP25NativeModPaths` place les paramètres de mods dans `GivreReplica/TestRuns/givre-app-offline/userdata/ModsControl` et le runtime natif dans `.../userdata/NativeTurbo/<pid>`, **jamais** dans `%LOCALAPPDATA%/GivreLauncher` ou `%LOCALAPPDATA%/GivreClone/NativeTurbo` en mode clone. Le chemin historique est conservé uniquement si le flag source `MZP19LocalClone.Enabled` est désactivé. L'ancien lecteur binaire `GivreMods.dll` ne lit pas ce nouveau chemin ; le clone ne prétend donc pas l'avoir rendu fonctionnel par ce changement.

### 2. Portage ciblé GivreActive — confirmations de banque

`GivreActive/EasyBot/PacketAnalyser/Function/interactif/Usebank.vb` confirme les retraits par UID/variation de stock; la version antérieure du clone pouvait considérer n'importe quel événement de coffre comme l'accusé du retrait et renvoyer dépôt réussi sur la seule injection. La lecture de GivreActive était **strictement read-only**.

Ajouts :

- `Services/MZP25BankReceipt.cs` : décode les références UID, GID et quantités des événements `StorageObjectRemoved/Update` (unitaires ou en lot), et des événements sac `ObjectDeleted/Quantity`; champs via catalogue existant, refus si champs absents.
- `LaunchService.Scripting.cs` / `LaunchService.Packets.cs` : attendent les événements reçus sous un **prédicat facultatif**. Un événement non pertinent ne désarme plus un waiter de banque; le registre n'écrase plus silencieusement un waiter déjà actif. Les autres appels restent sans prédicat.
- `LaunchService.Bank.cs` : retrait compté d'après la quantité spécifique effectivement confirmée pour l'UID/GID sur la même carte, et non d'après la quantité demandée; dépôt d'une liste suivi par UID depuis les événements entrants du sac et retourné `Ok=false` sans preuve de disparition des UIDs attendus. Chaque opération borne l'attente et détache l'observateur en `finally`.

Il s'agit d'une corrélation **observée dans les événements décodés**, pas d'une confirmation serveur du protocole 3.7 runtime vérifiée. La possibilité de changements parallèles de même UID, de réorganisation de lots ou d'événements absents exige des tests en jeu et une sérialisation d'opération si nécessaire. La règle fail-closed peut produire des faux négatifs, pas des succès spéculatifs.

Autres améliorations candidates **NON implémentées** dans ce lot : pause/reprise Lua avec génération et purge des callbacks, transaction de génération de carte avant validation des transitions. Preuves et priorités `docs/MZP25_GIVREACTIVE_PARITY.md`.

## V — tests de référence et exécution

- Tests xUnit des règles d'admission du PID et des hashes des modules, fixtures temporaires d'empreinte et refus des chemins hors paquet.
- Tests xUnit du calcul des chemins privés de contrôle / runtime, conservation du chemin historique non clone, rejet PID et chemins invalides.
- Tests xUnit de la corrélation de banque : UID non pertinent, lot partiel, UID/GID différents, quantité négative ou incohérente, refus d'un événement coffre arbitraire, mapping de champ absent.
- Rust `cargo test --workspace --release --offline` sous `native/dofus3-native-host` : **succès exit 0**, 10 tests Rust au total, sans lancement de Dofus.
- Voir `artifacts/MZP25BankTests.log`, `MZP25ModsTests.log`, `MZP25RustTests.log` et `MZP25FinalBuild.log` pour les valeurs exactes de tests/build. Ne pas assimiler un test offline à une exécution serveur.
- `artifacts/MZP25VerifiedBuild.log` : **0 erreur, 165 avertissements** au build Release précédent l'extraction du calcul de chemins en fonction pure. `artifacts/MZP25FinalFocusedTests.log` et le dernier journal de build réévaluent ensuite ce dernier changement. Le smoke Windows a observé un processus propre du clone PID **28188** (distinct de GivreClone original), `GivreClone v1.3.1 · clone local`, fenêtre apparue après un démarrage lent ; la lecture automatisée détaillée de l'onglet Réglages n'a pas pu être terminée (timeout de l'outil UI).
- La suite ciblée **finale** `MZP25FinalFocusedTests.log` : **31 réussis, 0 échoué**, suite au retrait d'une assertion du test Zaap dépendant du libellé exact du log ; réponses Thrift (jeton initial puis absence au deuxième appel et alias) et non-divulgation restent testées. Un premier essai a eu 30 PASS / 1 FAIL pour cette assertion fragile, puis a été corrigé et relancé.
- La suite xUnit **globale** `MZP25TestResults/MZP25FullSuite.trx` : **1 194 au total, 1 078 PASS, 114 FAIL, 2 SKIP**. Échecs non attribués globalement à une seule cause : contrats de mapping, tests Patch361112/LotE, forges et lecteurs ont des assertions échouées ; plusieurs fixtures `data/` et `server-mapping/` sont manquantes ou incompatibles. Il reste des régressions ou préconditions historiques à réconcilier individuellement.
- Dernière compilation avec garde répertoire pur `MZP25NativeModPaths` : `artifacts/MZP25IntegratedBuild.log` : **0 erreur, 165 avertissements**. Exécutable `artifacts/MZP25IntegratedBuild/GivreClone.exe` compilé. La fenêtre du précédent build `MZP25VerifiedBuild` a été observée, mais **pas celle de ce nouveau binaire précis**. Le paquet final contient les huit DLL Rust source-owned et le hook C++ local, sans preuve d'usage dans Dofus.

## B — écart restant avec « toutes les features »

Le clone présente des modules de navigation, combat, Lua, banque, récolte, HDV, quêtes, superviseur et mods, mais **leur parité totale avec GivreClone v1.3.1 n'est pas établie**. Il manque encore un protocole Dofus 3.7 certifié (l'UI signale huit messages essentiels manquants), une session TEST de transport/Zaap et un ACK serveur attribuable au client lancé par le clone. Les DLL originales étant vraisemblablement protégées, leur décompilation statique ne livre pas leurs fonctions métier. Le portage GivreActive doit être validé avec des corpus/fixtures puis un test réellement connecté avant activation autonome.

Aucun processus GivreClone installé ni client officiel n'a été injecté, arrêté ou modifié pendant cette passe. Aucun secret de compte ni token n'a été consulté.
