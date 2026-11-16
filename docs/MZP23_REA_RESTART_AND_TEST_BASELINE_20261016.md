# MZP23 — reprise REA, base de tests réelle, cause racine des 111 échecs (2026-10-16)

## Résultats vérifiés (exécutés dans cette session)
- **REA MCP** opérationnel (`binary_session` répond, package 6.5.0). Fournisseurs listés : rea-artifact-graph, rea-dotnet-static, sqlite ; **Ghidra 12.1.4 candidat installé** (`REA-Tools`).
- `inspect_managed_artifact` sur `app/GivreClone.exe` (SHA-256 21c9de5b…0758) : **non géré** (pas de CLI directory) → ILSpy inapplicable, seule la voie native (Ghidra) reste. Evidence `ev_3b0c07f0…0794`.
- **Ghidra via MCP** : `open_binary(provider_id=ghidra)` réussit mais l'analyse auto de `GivreMcp.exe` (1,8 Mo) dépasse le délai de requête MCP ; **la même analyse via la CLI `rea analyze <path> --provider ghidra --format json` réussit en ~2 min** (exit 0, Evidence `ev_6c0789a4…5d7a`, sortie `artifacts/MZP23REAEvidence/GivreMcp_ghidra_overview.json`). L'échec « code 1 » du rapport MZP22 venait d'une syntaxe erronée (`--path` n'existe pas ; le chemin est positionnel). Contournement : toujours passer par la CLI pour les gros binaires, puis relire par `--snapshot`.
- `dotnet build GivreClone.slnx -c Release` : **0 erreur, 163 avertissements** (Avalonia `Watermark` obsolète).
- `dotnet test` : 152 échecs / 663 réussites sans mapping → **111 échecs / 704 réussites** après installation de `data/` et `server-mapping/` (copie octet pour octet de `GivreRework/data/3.7/{matching_current,fields}.json`, provenance : catalogue candidat 3.7 non promu).

## Cause racine des échecs restants (observée)
1. `confirmed.json` n'existe nulle part sur le disque (7 tests `MappingContractTests`).
2. Les fixtures encodent des **formes de l'époque 3.6.11.12** : `ForgeEmissionTests.Request()` exige `GameMessage.request = #1` ; le mapping candidat 3.7 donne `request = 4`, le README du clone dit 5. **Trois numérotations contradictoires** ; laquelle est vraie pour 3.7.11.11 ne peut se trancher que sur une trame réelle du client (non faite ici). Aucun test n'a été modifié pour « passer ».
3. Dans l'original, le mapping est **streamé en production** (commentaires `EnvelopeBuilder`), donc absent de `GivreClone.exe` : le clone doit le générer depuis le build du jeu (pipeline Cpp2IL/protodec déjà présent dans GivreActive) ou le recevoir d'une source versionnée.

## Non fait / non prouvé
Aucune analyse Ghidra de `GivreClone.exe` (54 Mo, NativeAOT), aucun lancement Launcher 2/Zaap, aucune session réseau, aucune parité fonctionnelle certifiée. Les gardes `HasIndependentTestAuthority` de MZP21 restent le blocage du lancement indépendant.

## Prochaines étapes ordonnées
1. Fixer l'enveloppe 3.7.11.11 sur une capture réelle (compte test), puis régénérer `fields.json`/`confirmed.json` et réaligner les fixtures.
2. `rea analyze GivreClone.exe --provider ghidra` en tâche de fond (>10 min attendu), snapshot réutilisé par MCP.
3. Implémenter l'autorité de lancement TEST (MZP21 §C.1–C.4).

## Addendum — analyse Ghidra de GivreClone.exe (copie détachée, SHA-256 21c9de5b…0758)
Lancée par la CLI (`rea analyze <path> --provider ghidra --format json`), ~4 min. Résultat dans `artifacts/MZP23REAEvidence/GivreClone_ghidra_overview.json` : l'analyse a **abouti** (Evidence partielle `ev_1766abd8…390e`) mais la commande a fini en `cleanup_incomplete` (répertoire temporaire `%TEMP%\rea-ghidra-cbac6465…` non supprimable, verrou Windows) ; ce n'est pas un échec d'analyse.

Observations (image_base 0x140000000) :
- **181 804 procédures** détectées ; `.text` 31 Mo, `.rdata` 56 Mo.
- **Seulement 102 chaînes définies** pour un binaire de 54 Mo, et **sections aux noms aléatoires** (`.Hx{` 19 Mo, `.8VA`, `.g0z` 54 Mo) : cohérent avec une protection/virtualisation du code (le README mentionne un dossier `vmp/` de production). Inférence, non prouvée : la logique métier n'est probablement pas lisible telle quelle dans `.text`.
- Conséquence : la reconstruction fonctionnelle de GivreClone.exe par décompilation directe est incertaine ; les sources du clone et l'observation de comportement (fichiers MZP) restent la voie principale.
