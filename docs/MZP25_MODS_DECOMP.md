# MZP25 — `GivreMods.dll` original : décompilation Ghidra bornée et vérifiée

**Worker-2 · 16 octobre 2026 · analyse STATIC READ-ONLY.** Cible **exclusivement détachée** : `offline-evidence/v131-app/verified-binaries/GivreMods.dll`. Ghidra 12.1.4 a été exécuté en mode *headless* avec projet privé `artifacts/MZP25Mods/project/`, import `-readOnly`, timeout d'analyse par fichier 75 s, `-max-cpu 2`, post-script d'observation `BoundedModsHeadless.java` puis `-deleteProject`. Aucun chargement de la DLL dans un jeu, aucun processus Givre/Dofus ciblé, aucune session, capture réseau, compte, secret ou intervention sur l'installation originale. La JVM Ghidra a annoncé son cache d'outil générique sous `%LOCALAPPDATA%\ghidra\packed-db-cache`, emplacement interne de Ghidra distinct des résultats du worker : son état antérieur n'a pas été audité.

## R — résultat vérifié

**SHA-256 de la cible** : `B8F95D30B178D4FD3A34B88DAA148F29D62D72371524067DF12CDD6CE06B0343` pour **14 305 280 octets**. Le parseur REA et `pefile` relèvent PE32+ x64, base `0x180000000`, entrée **RVA `0x14606A0`**, 12 sections, dont `.M9P` **14 302 720 octets bruts**, 2 symboles importés (`USER32!GetClassNameW`, `KERNEL32!WriteConsoleW`), zéro export et zéro répertoire .NET/CLI. Manifest unique RT_MANIFEST type 24, ID 2, langue 1033, 381 o, hash `4BB79DCEA0A901F7D9EAC5AA05728AE92ACB42E0CB22E5DD14134F4421A3D8DF`.

**Evidence REA liées au SHA exact, sorties sauvegardées par MZP24 :**

- `inspect-pe-resources` — `ev_3f4db5b538cdd1ea4f4c429a733f3ccc7537debadc963657bc461cb397d2ce13` ; coverage complète du seul répertoire de ressources PE ; source `artifacts/MZP24Mods/GivreMods_rea_inspect-pe-resources.json`.
- `inspect-managed-artifact` — `ev_2e5dc9452b0e73f2ccd92ff75b5e9f6f2af19336a24771aa991360c57bb8fd47` ; classe `not-managed`, aucun membre CIL ; source `artifacts/MZP24Mods/GivreMods_rea_inspect-managed-artifact.json`.

**Analyse Ghidra headless : résultat réel `exit=0`**. Le log `artifacts/MZP25Mods/ghidra_headless.log` atteste successivement `Analysis succeeded for file`, `Post-analysis succeeded for file`, `Discarded file import due to readOnly option`, et `Import succeeded`. Le post-script écrit `artifacts/MZP25Mods/ghidra_bounded_result.txt`. Il a observé **1 958 objets « Function » issus de l'analyse automatique** dans le programme importé, ce qui ne garantit pas 1 958 fonctions valides ou de haut niveau : le code est vraisemblablement transformé. Il n'y a pas de PDB exploitable (`Skipping PDB processing: missing PDB information`). Plusieurs avertissements d'unwind x64 et de flot de contrôle sont présents dans le log ; ils limitent la fiabilité des fonctions et signatures déduites.

### Adresses/xrefs exactement vérifiés

| Objet | Adresse/RVA | Ghidra et portée |
| --- | --- | --- |
| DLL originale image base | `0x180000000` | En-tête PE et base effective du programme Ghidra |
| Entrée du PE | RVA `0x14606A0` / VA `0x1814606A0` | Ghidra : mémoire **`.M9P`**, objet `entry@1814606a0`, corps **82 adresses** |
| `GetClassNameW` | `0x1808AE000 → EXTERNAL:00000001` | **1 référence** dans la table d'import Ghidra ; pas un appel métier interne prouvé |
| `WriteConsoleW` | `0x1808AE010 → EXTERNAL:00000002` | **1 référence** dans la table d'import Ghidra ; pas un appel métier interne prouvé |
| `FUN_1808c817f` | `0x1808C817F` | **Cible seulement nommée** par la décompilation de `entry`, sans décompilation indépendante ni conclusion fonctionnelle certifiée dans cette passe |

Une **décompilation de fonction** a été demandée et renvoyée avec `decompile_complete=true` et sans message d'erreur direct : `entry@1814606a0`. Son pseudo-code vérifié, tronqué volontairement à une courte portion utile, contient un appel `FUN_1808c817f()`, une opération XOR à offset de registre non déterminé et termine par `halt_baddata()`. Ghidra marque explicitement `Control flow encountered bad instruction data` et `Bad instruction - Truncating control flow here`. L'absence d'échec API du décompilateur ne signifie **pas** que le flot reconstruit représente le code fonctionnel du jeu.

**Verdict de décompilation :** la fonction d'entrée et les deux références d'import sont vérifiables au niveau adresse, mais **aucune fonction `no-animation`, `collapse`, `instant-move`, tactique, Animator2D ou Unity n'est identifiée avec une preuve de désassemblage métier**. La présence de `.detourc` et `.detourd` est compatible avec un code instrumenté, mais aucun hook précis ou RVA associé à un mod n'a été certifié. Ni les xrefs d'IAT, ni le nombre de fonctions supposées, ni les libellés des sources du clone ne suffisent à inventer ces fonctions dans l'original.

### Correspondance source clone et pistes no-animation/tactique, bornées par la preuve

| Piste du clone | Source exacte vérifiable | État face au binaire original |
| --- | --- | --- |
| Contrôle par bot : `skipper`, `tactical`, `collapse`, `instantmove` | `GivreClone.App/Services/ModControl.cs:8-42` | Format de fichier **déclaré côté clone** ; aucune lecture du `.ini` ni constante originale retrouvée par cette analyse |
| Profil UI : quatre switches de combat | `GivreClone.App/ViewModels/BotsViewModel.Combat.cs:180-223` | Apparence/contrat du clone seulement ; parité originale inconnue |
| Contrôleur de chargement de `GivreMods.dll` best-effort | `GivreClone.App/Services/LaunchService.cs:699-716` | Le fait que la DLL originale soit chargeable ne prouve pas ses hooks ni ses effets |
| Fallback skipper via `DofusCombatAnimationSkipper.dll` + `DofusNativeHost.dll` | `GivreClone.App/Services/NativeTurboRuntime.cs:15-54`, `:225-260`, `:343-366`, `:469-519` | Alternative source-owned ; **SHA, architecture de déploiement et imports distincts** de GivreMods |
| Module tactique `DofusNativeTactical.dll` | `native/GivreTurboRuntime/ours-010eaac/PROVENANCE-OURS.md:1-17` | Module indépendant compilé depuis `dofus3-native-host` ; aucune preuve que l'original implémente la même solution |

Le comparatif `artifacts/MZP24Mods/GivreMods_pe_compare.json` atteste **5/5 candidats binaires source locaux distincts** de l'original. Par exemple, `DofusCombatAnimationSkipper.dll` version construite localement 286 208 octets, SHA `A71084FDD4A7EAF0085944280423C31A9D352FA1566A71331F1212F745C124A3`, contre 14 305 280 octets pour GivreMods ; `DofusNativeTactical.dll` 172 032 octets, SHA `BADF4D23047432F6AE5B8FE4BCA8960DEA43457D2D71108786100F7353E96D2F`. Les étiquettes de versions/bindings appartiennent aux sources et aux attestations de compilation du clone, **pas à l'original protégé**.

## C — création de fichiers

Tous les fichiers voulus par ce lot sont dans `docs/MZP25_MODS_DECOMP.md` et `artifacts/MZP25Mods/` :

- `BoundedModsHeadless.java` : post-script Ghidra statique bornant fonctions, xrefs et une sélection de décompilations à trois fonctions et 1 600 caractères de pseudo-code chacune ; aucune modification d'un programme importé.
- `ghidra_headless.log` : log brut PowerShell UTF-16, Ghidra v12.1.4, import, analyse et statut de nettoyage.
- `ghidra_bounded_result.txt` : inventaire natif extrait par Ghidra, 1 958 objets fonctions, entrée, deux références externes, pseudo-code d'entrée limité.
- `verify_mods_ghidra.py` : vérifie SHA de l'original, IDs REA, adresses, résultats Ghidra, mention `halt_baddata` et statut read-only.
- `GivreMods_verified_static_evidence.json` : synthèse machine vérifiée, avec empreintes du log et des observations Ghidra.

Le volet précédent `docs/MZP24_MODS_REA.md` et `artifacts/MZP24Mods/` ne contient que REA, PE et comparaison aux sources. **Aucun fichier Kama n'a été touché**. Le moteur Ghidra a utilisé son cache standard mentionné plus haut ; il n'a pas sauvegardé de projet d'analyse exploitable hors du dossier de travail déclaré, et son log dit que l'import a été **écarté** à cause de `-readOnly`.

## V — validations effectivement exécutées

1. `Get-FileHash` SHA-256 de l'original détaché : concorde avec l'inventaire MZP21/MZP24.
2. REA `inspect-pe-resources` et `inspect-managed-artifact` : exit **0** chacun ; Evidence IDs ci-dessus, deux fichiers JSON normalisés.
3. `pefile` scanner `artifacts/MZP24Mods/inspect_mods_pe.py` : **`STATIC_PE_PINNED_PASS`**, 12 sections, deux imports exacts, ressource unique, cinq candidats différents.
4. Ghidra `analyzeHeadless.bat ... -import <offline/GivreMods.dll> -readOnly -analysisTimeoutPerFile 75 -max-cpu 2 -postScript BoundedModsHeadless.java <résultat> -deleteProject` : **exit 0**, logs `Analysis succeeded` et `Post-analysis succeeded`; `decompile_complete=true` sur l'entrée **mais flot invalide**.
5. `py -3 -B artifacts/MZP25Mods/verify_mods_ghidra.py` : **`PASS_MZP25_ORIGINAL_PINNED`**, recoupe **trois SHA originaux**, REA et Ghidra, adresses exactes, nature des xrefs et limitation du pseudo-code. Un premier essai a refusé les logs UTF-16 de PowerShell ; le parseur a été corrigé pour accepter le BOM réellement produit, puis le contrôle a réussi. Aucun test client, chargement DLL, mémoire de jeu ni test réseau.

## B — ce qu'on ne peut pas conclure

- Le gros bloc `.M9P` et les instructions invalides empêchent une attribution fiable de routines d'animation ou tactiques depuis la décompilation obtenue. Aucun « opcode », hook, classe Unity, RVA de feature ou parité comportementale original n'est certifié.
- Les deux xrefs observés sont exclusivement des références à la table d'import, pas une preuve de fonction appelante reconnue. La cible `FUN_1808c817f` reste non vérifiée par décompilation propre ; la classer comme moteur `no-animation` serait inventer.
- Les noms fonctionnels du clone, son `NativeTurboRuntime` et les binaires open source sont des **candidats d'implémentation indépendants**, pas des symboles récupérés de la DLL originale.
- Aucun jeu lancé, aucun ACK, aucun effet visuel combat, ni observation runtime. Pour améliorer le clone sans supposer la parité : valider d'abord en tests autonomes les entrées `skipper/tactical/collapse/instantmove`, l'isolation par PID, la désactivation propre, la restauration d'animation et la concurrence entre `GivreMods` et le fallback ; la preuve de gameplay exige une session de test autorisée distincte et ne fait pas partie de MZP25.

**FINISH : MZP24 Mods + MZP25 Mods preuves statiques et limites consignées ; aucune feature binaire inventée.**
