# N03 — Mesures du planificateur hors jeu

**04/11/2026 — benchmark réellement exécuté sur cinq fixtures synthétiques**. Aucun lancement du jeu/bot, aucune session modifiée, aucun test de production ou épreuve en conditions réelles. Le banc d'essai [bench/Program.cs](bench/Program.cs) ne référence qu'une DLL **déjà compilée**, sans écriture dans le code du projet.

## Résultat

La commande de restauration et d'exécution locale a retourné **code 0**, puis `OFFLINE_BENCH_DONE cases=5 rows=70`. Les [70 observations brutes](measurements.jsonl) comportent deux warmups et douze mesures régulières par fixture. Mesures ci-dessous **en millisecondes**, sur ces seules configurations artificielles, sans prétendre mesurer l'incident réel « plan lent ».

| Fixture | n steady | médiane | P95 (sur 12) | maximum | travail moyen | allocation thread moyenne | décision du premier essai régulier |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Sans sort | 12 | 0,252 | 0,607 | 0,607 | 51 | 46 080 o | aucune |
| Une attaque à distance | 12 | 3,250 | 26,929 | 26,929 | 169 | 278 728 o | `cast:10@80(3AP)` |
| Deux attaques | 12 | 28,946 | 84,088 | 84,088 | 603 | 1 307 768 o | `cast:10@80;cast:11@80` |
| Soin contre attaque | 12 | 11,668 | 28,027 | 28,027 | 325 | 501 440 o | `cast:12@37;cast:10@80` |
| Trois attaques et deux ennemis | 12 | 64,898 | 149,118 | 149,118 | 1 423 | 3 022 384 o | séquence détaillée dans JSONL |

Les cellules `80`, `37` et les sorts `10–13` sont des **identifiants synthétiques de fixture**, pas des sorts ou des combats attribuables à DOFUS. Les valeurs de durée peuvent varier avec la concurrence machine et le GC. Aucun échantillon n'a épuisé le budget ; ce corpus ne révèle donc pas les pires cas ni une régression de budget.

**Limite majeure de version** : la DLL Release réellement mesurée, `GivreClone.Tests/bin/Release/net10.0/GivreClone.dll`, date du **3 novembre 2026**, tandis que `CombatTurnPlanner.cs` a été modifié le **4 novembre 2026**. **Ce résultat mesure l'ancienne compilation**, pas forcément la source courante du planificateur. La compilation de la source courante a été exclue pour ne pas toucher au worktree actif. Ne pas faire de comparaison avant/après ou de promesse de gain à partir de ces nombres.

## Interprétation et prolongements

- Le premier warmup de la fixture sans sort a pris **467,423 ms**, contre **0,252 ms** de médiane steady. Cela démontre pourquoi il faut séparer chargement/JIT et décision sans prétendre identifier toute la cause des warmups.
- La recherche plus ramifiée « trois attaques » produit ici une médiane plus élevée (64,898 ms) et plus d'allocations que « une attaque », mais les configurations ne sont pas des essais toutes choses égales par ailleurs.
- Pour la suite, choisir des snapshots **anonymisés historiques** représentatifs des incidents, enregistrer build/sha/CPU/runtime, budget, actions, allocations et P50/P95/P99 sur au moins plusieurs dizaines de répétitions, puis comparer à une compilation **isolée et autorisée** du commit correspondant. Ne jamais relancer un plan live pour le mesurer.

Voir [PREUVES.md](PREUVES.md), [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md), [environment.json](environment.json) et le code source autonome du bench.
