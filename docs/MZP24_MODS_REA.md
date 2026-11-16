# MZP24 — `GivreMods.dll` : inventaire statique REA / PE et comparaison des candidats

**16 octobre 2026 · worker-2 · lecture seule du binaire original détaché.** Objet : `offline-evidence/v131-app/verified-binaries/GivreMods.dll` ; aucun fichier d'installation, processus Givre/Dofus, compte, secret ou réseau n'a été ouvert. Les sorties sont créées sous `artifacts/MZP24Mods/` uniquement. Ce dossier est distinct de `artifacts/MZP24Binary/` consacré auparavant à Kama. Les résultats Ghidra approfondis sont dans `docs/MZP25_MODS_DECOMP.md`.

## R — observations réellement vérifiées

L'objet mesure **14 305 280 octets**, SHA-256 **`B8F95D30B178D4FD3A34B88DAA148F29D62D72371524067DF12CDD6CE06B0343`**. Un parseur PE indépendant (`pefile`) relève un **PE32+ x64** (`Machine=0x8664`), image base `0x180000000`, point d'entrée **RVA `0x14606A0`** (VA `0x1814606A0`), **12 sections**, zéro export, aucun répertoire CLR, aucun import différé et aucune table de signature Authenticode.

REA **6.5.0** a réussi les deux examens explicites sur la copie détachée :

| REA | Evidence exacte | Résultat |
| --- | --- | --- |
| `inspect-pe-resources` (`rea-pe-resources`) | `ev_3f4db5b538cdd1ea4f4c429a733f3ccc7537debadc963657bc461cb397d2ce13` | Ressources : **1/1**, `RT_MANIFEST` type 24, ID 2, langue 1033 ; offset **14 304 344**, taille **381** octets, SHA-256 `4BB79DCEA0A901F7D9EAC5AA05728AE92ACB42E0CB22E5DD14134F4421A3D8DF`. `coverage=complete` pour le **parseur de ressources**, pas pour la fonctionnalité du programme. |
| `inspect-managed-artifact` (`rea-dotnet-static`) | `ev_2e5dc9452b0e73f2ccd92ff75b5e9f6f2af19336a24771aa991360c57bb8fd47` | `classification.status=not-managed`; CLI absent, zéro membre/assembly CIL extractible ; 12 sections PE. L'absence d'IL rend ILSpy inapplicable sur cette DLL. |

Le parseur PE voit exactement **deux imports statiques**, avec un symbole par DLL : `USER32.dll!GetClassNameW` et `KERNEL32.dll!WriteConsoleW`. L'absence d'autres imports déclarés **n'exclut pas** des symboles résolus dynamiquement ou occultés par la protection. `GetClassNameW` seul ne prouve pas une interface tactique ; `WriteConsoleW` seul ne prouve pas un journal opérationnel.

### Carte des sections et protection

| Section | RVA | Taille brute | Taille virtuelle | Observation |
| --- | --- | ---: | ---: | --- |
| `.text` | `0x1000` | 0 | 245 226 | Aucun contenu brut dans la section classique |
| `.rdata` | `0x3D000` | 0 | 87 250 | Idem |
| `.data` | `0x53000` | 0 | 10 212 | Idem |
| `.pdata` | `0x56000` | 0 | 11 964 | Idem |
| `.detourc` | `0x59000` | 0 | 8 720 | Nom compatible Detours, pas preuve d'un hook posé |
| `.detourd` | `0x5C000` | 0 | 24 | Même limite |
| `.fptable` | `0x5D000` | 0 | 256 | Table structurelle |
| `.p5-` | `0x5E000` | 0 | 8 713 516 | Virtuelle, pas d'octets bruts attribués |
| `.\\8A` | `0x8AE000` | 512 | 96 | Bloc court brut |
| `.M9P` | `0x8AF000` | **14 302 720** | 14 302 388 | Près de tout le binaire, entropie 7,111 bits/octet **sur les premiers 65 536 octets seulement** |
| `.rsrc` | `0x1653000` | 512 | 469 | Manifeste unique |
| `.reloc` | `0x1654000` | 512 | 176 | Relocations |

Le caractère inhabituel des noms, les sections virtuelles non accompagnées de données brutes et le bloc `.M9P` sont cohérents avec une transformation/protection du code. **Aucun packer déterminé avec certitude.** L'analyse REA ressource/managée ne récupère pas le corps métier.

### Comparaison des binaires natifs réellement présents dans le clone

La preuve `artifacts/MZP24Mods/GivreMods_pe_compare.json` relève taille, SHA-256, sections, imports et ressource du binaire original et des **cinq candidats locaux**, sans les charger :

| Candidat source/compilation locale | Octets | SHA-256 | Nombre d'imports |
| --- | ---: | --- | ---: |
| `native/GivreTurboRuntime/ours-010eaac/NativeMods/DofusCombatAnimationSkipper.dll` | 144 384 | `C8F2A344BDF88D933269298CBF6CC9CC39FB3EADCB9E732128C4D82E47EE1EAB` | 69 |
| `native/GivreTurboRuntime/ours-010eaac/NativeMods/DofusNativeTactical.dll` | 172 032 | `BADF4D23047432F6AE5B8FE4BCA8960DEA43457D2D71108786100F7353E96D2F` | 69 |
| `native/GivreTurboRuntime/v0.4.0-skipper1.0.5-speedhack1.0.3/DofusCombatAnimationSkipper.dll` | 286 208 | `A71084FDD4A7EAF0085944280423C31A9D352FA1566A71331F1212F745C124A3` | 76 |
| `native/GivreTurboRuntime/v0.4.0-skipper1.0.5-speedhack1.0.3/DofusCombatSpeedHack.dll` | 310 272 | `895A195A6551EFB4A1085E500B745676CE8BF44A0967426DE3E14ABD5D1A0A24` | 83 |
| `native/GivreTurboRuntime/v0.4.0-skipper1.0.5-speedhack1.0.3/DofusNativeHost.dll` | 2 627 584 | `22D2C6743C23D48F80BC0A9E114BA13BCFB07587470AEDB188A9FDBA8846EC1B` | 151 |

**5/5 SHA différents de l'original.** Sources des artefacts candidats : `native/GivreTurboRuntime/ours-010eaac/PROVENANCE-OURS.md:1-17` et `native/GivreTurboRuntime/v0.4.0-skipper1.0.5-speedhack1.0.3/PROVENANCE.md:1-44`. Elles attribuent ces modules à une construction source `dofus3-native-host` et à ses propres évolutions : cela permet d'étudier des fonctions de substitution, **sans les faire passer pour du code de GivreMods original**.

### Correspondance précise avec la surface du clone

| Contrat du clone constaté en source | Localisation | Statut côté original |
| --- | --- | --- |
| `skipper=0/1`, `tactical=0/1`, `collapse=0/1`, `instantmove=0/1` écrits dans un `.ini` par PID | `GivreClone.App/Services/ModControl.cs:8-42` | **Non démontré** par imports/ressources natifs |
| UI `CombatNoAnim`, `CombatCollapse`, `CombatInstantMove`, `CombatTactical` | `GivreClone.App/ViewModels/BotsViewModel.Combat.cs:180-223` | **Fonctions visibles du clone**, aucune parité originale attestée |
| L'interface met à jour les mods sous droits explicites et exclut les PID de session socket | `GivreClone.App/Services/LaunchService.Combat.cs:742-778` | Contrat d'intégration source, pas preuve d'une DLL originale |
| Fallback native-host skipper + speedhack combat-only avec séparation d'instance PID | `GivreClone.App/Services/NativeTurboRuntime.cs:15-54`, `:225-260`, `:343-366`, `:469-519` | **Implémentation alternative indépendante**, distincte de `GivreMods.dll` original |

## C — créations strictement bornées

`docs/MZP24_MODS_REA.md`, `artifacts/MZP24Mods/inspect_mods_pe.py`, `artifacts/MZP24Mods/GivreMods_pe_compare.json` et les JSON de preuve REA `GivreMods_rea_inspect-pe-resources.json`, `GivreMods_rea_inspect-managed-artifact.json`. Deux copies **brutes** des sorties REA (`*.raw.json`, écrites en UTF-16 par PowerShell) sont également conservées ; les copies normalisées sont UTF-8. Aucun des artefacts Kama MZP24 précédents n'a été modifié.

## V — tests statiques réellement exécutés

`node <cache REA 6.5.0>/scripts/rea.mjs inspect-pe-resources <DLL détachée> --format json` : **exit 0** ; `inspect-managed-artifact` : **exit 0** ; `py -3 -B artifacts/MZP24Mods/inspect_mods_pe.py` : **`STATIC_PE_PINNED_PASS`**, SHA original exact, CLR 0, imports exactement 2, ressource exactement 1, cinq comparatifs distincts. Aucun test de fonctionnement du mod dans le jeu n'a été fait.

## B — limites

Le corps natif protégé n'est pas disponible via les deux opérations REA ; impossible d'attester no-animation, collapse, instant-move ou tactique à partir des seules structures PE. Même si Ghidra établit des fonctions ou des xrefs, il faut une logique désassemblée lisible et un lien direct avec ces comportements avant de parler de feature récupérée. Voir le verdict Ghidra dans `docs/MZP25_MODS_DECOMP.md`.
