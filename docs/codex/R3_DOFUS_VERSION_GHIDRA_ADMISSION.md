# Round 3 — version Dofus et limites des preuves offline (17 octobre 2026)

## Résultat

**Admission Socket non modifiée, refus maintenu si la version exacte est inconnue.**

- `Services/MZP11CytrusVersionProbe.cs:40-50` recherche uniquement `manifest.cytrus` à côté de `Dofus.exe`, puis `InspectManifest` exige un SQLite compressé et un `releaseVersion` de forme contrôlée.
- `Services/MZP12SocketVersionBinding.cs:33-53` exige la correspondance exacte de version et de SHA-256 avec le mapping publié. Cette garde est volontairement conservée.
- Installation examinée : `Dofus.exe`, `GameAssembly.dll`, `manifest.json` et `.release.infos.json` présents, `manifest.cytrus` absent au chemin attendu. `.release.infos.json` contient seulement `gameUid=dofus` et `release=dofus3` : **aucune version protocolaire**. `manifest.json` est un dictionnaire de fichiers et de hachages ; ces hachages prouvent des contenus, **pas à eux seuls** une correspondance protocolaire certifiée.
- Aucun résultat ne permet d'affirmer la version exacte 3.7.11.11 de cette installation, ni la conformité au mapping, ni un ACK serveur. Ne pas substituer une date de fichier, un hash PE, un import `WS2_32` ou le nom `dofus3` à un `releaseVersion` authentifié.

## Ghidra / REA

La compétence Ghidra headless a été examinée. `java`, `analyzeHeadless` et Ghidra ne sont pas exposés par les commandes ni par l'installation `C:\Program Files` vérifiées dans cette session. **Aucune décompilation Ghidra n'a été exécutée ni revendiquée**, aucun processus de jeu n'a été lancé ou accroché, aucun jeton n'a été ouvert.

## Suite vérifiable

1. Trouver une source de `releaseVersion` officielle et **locale**, reliée sans ambiguïté à l'exécutable installé (ex. manifeste Cytrus validé dans un autre chemin fourni par Launcher), sans consulter les données de compte.
2. Ajouter un type de preuve avec `Source`, `Build`, `SHA256`, `InstallRoot`, `EvidenceKind`; refuser conflit/inconnu et exiger accord avec le mapping publié avant admission.
3. Tests offline avec fixtures : preuve manquante, metadata seule (`gameUid`/`release`) → refus, releaseVersion valide mais mapping différent → refus, conflit entre deux preuves → refus, source locale exacte et vérifiée → admission structurelle **sans** certification gameplay.
4. Ne pas faire de rétro-ingénierie dynamique ni utiliser de compte pour rendre un test vert.
