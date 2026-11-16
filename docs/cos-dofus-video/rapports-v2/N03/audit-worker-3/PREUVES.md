# N03 — Preuves locales et protocole exact de reprise

**Date de collecte : 04/11/2026, machine Windows, source en lecture seule. Aucun benchmark, build, test, JIT intentionnel, copie d'assemblage ni action de jeu exécuté.** Les contrôles listés ci-dessous ont été lancés dans la racine source, sans mutation. Les résultats machine-lisibles sont dans [AUDIT_BRUT.json](./AUDIT_BRUT.json).

## Provenance des consignes

- Source lue via chemin **absolu** : %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/docs/cos-dofus-video/VAGUE_2_HORS_YOUTUBE.md, lignes **10–23, 30–36, 56–69** ; N03 autorise une mesure **seulement** sur copie isolée, sinon protocole.
- Synthèse lue : même racine, docs/cos-dofus-video/rapports/SYNTHESE.md, **93 lignes**, état documentaire 33 missions et aucune validation xUnit du laboratoire (lignes **69–72, 88–92**).
- **AGENTS** : tentative de lecture de AGENTS.md à la racine source, du worktree worker-3, de leurs répertoires parents pertinents et de GivreClone.Tests/App ; **fichiers non trouvés**. Recherche récursive limitée à GivreClone.Tests, GivreClone.App/Services/Combat et docs/cos-dofus-video n'en a pas trouvé. Une recherche initiale de tout le dépôt avec rg a échoué sur deux répertoires artifacts/kourial-tools en accès refusé ; **cela ne prouve pas qu'aucun AGENTS n'existe ailleurs**.

## Contrôles exécutés et retours réels

| Contrôle lecture seule | Commande exécutée (PowerShell 5.1) | Code retour | Preuve factuelle |
| --- | --- | --- | --- |
| Processus jeu/bot | Get-Process puis filtrage ProcessName par dofus, ankama, givre, dotnet, testhost, msbuild, vstest, java, unity. | **0** | Ankama Launcher **11356**, Dofus **14252**, GivreClone **15976**, UnityCrashHandler64 **28220**. Les processus étaient actifs. |
| Chemins des exécutables | Get-Process -Name GivreClone,Dofus,Ankama* -ErrorAction SilentlyContinue ; Select ProcessName,Id,Path,StartTime. | **0** | Dofus depuis répertoire d'installation Ankama ; GivreClone depuis **répertoire temporaire givre-mark-id-20261104**, et non le worktree de la mission. Ce fait n'atteste ni l'absence de session ni la correspondance avec la source. |
| Compilation/test concurrent potentiel | Get-Process -match dotnet,msbuild,vstest,testhost,csc ; contrôle répété. | Premier contrôle **0 sans entrée**, deuxième **0 avec 7 PID**, troisième **1 sans entrée** (absence de résultat Get-Process dotnet). | Dotnet apparu **PID 3956, 13060, 15180, 20256, 25708, 29220, 29224** avec démarrages locaux **12:59:35–12:59:48** ; aucune ligne de commande inspectée, donc **nature des tâches inconnue**. Impossible de garantir absence durable d'une compilation. |
| Machine | Get-CimInstance Win32_Processor ; Select Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed. | **0** | Intel Core i7-1165G7, 4C/8T, MaxClockSpeed 2701 MHz déclaré. |
| Mémoire | Get-CimInstance Win32_ComputerSystem ; Select TotalPhysicalMemory,Manufacturer,Model. | **0** | **16 805 027 840 B** ; HP Laptop 14s-dq2xxx. |
| .NET | dotnet --info. | **0** | SDK 10.0.402, MSBuild 18.9.15, host 10.0.13, Microsoft.NETCore.App 10.0.13 et 8.0.32, win-x64. **Cette commande n'est pas un benchmark.** |
| Référence de benchmark | read source GivreClone.Tests/CombatPlannerPerformanceTests.cs. | Lecture **réussie** | Lignes 13–50 : entrée 560 cellules/3 fighters/19 sorts, 11 PA/5 PM, budget 60k, initial+5 warmups, 15 mesures, min/médiane/max temps et médiane allocations. |
| Interne du budget | read CombatTurnPlanner.cs. | Lecture **réussie** | Lignes 61–71 budget par défaut 180k ; 122–139 Budget.Take ; 186–214 menace ; 600–628 beam et Work. Work **n'est pas nombre de nœuds**. |
| Binaire Release | Get-ChildItem Test/bin/Release/net10.0 ; Get-FileHash SHA256. | **0** | DLL 5 060 608 B, **2026-11-03T12:49:22.364Z** ; SHA256 E903031B9AA0989A3D2DCEB5E712F4D903EAE04EA3F067128B6CB4602A45B1E6. |
| Binaire Debug | Même commande Debug. | **0** | DLL 5 325 824 B, **2026-11-03T17:07:10.792Z** ; SHA256 BF439E8644500DA03283A85B1EB400F2EF116F86796B8C6EDF37CB14292FF5B9. |
| Fixture source | Get-FileHash SHA256 CombatPlannerPerformanceTests.cs. | **0** | 2 706 B, modifié **2026-11-03T18:05:17.980Z** ; SHA256 628E6BA91F0611F433335D395ECB7A62232E67A77AA29C49A05F7132131BD917. |
| Planificateur source | Get-FileHash SHA256 CombatTurnPlanner.cs. | **0** | 42 395 B, modifié **2026-11-04T08:36:02.192Z** ; SHA256 7618468265FC2D32B6C186ADFB0E3917947A1C03B6C7C5E903344399BB142A3F. |
| Projet de tests | Get-FileHash SHA256 GivreClone.Tests.csproj. | **0** | 20 675 B, modifié 2026-11-03T09:15:25.009Z ; SHA256 2F6FA9397EACAD604992A38F67A11B53D91CD183D742CE2EA4D693E4936BE5FF. |
| Résultats existants | rg -l -g '*.trx' 'Full_board_budgeted_search_is_repeatable' GivreClone.Tests/TestResults. | **1** | Aucune correspondance trouvée dans le répertoire inspecté. **Pas de mesure historique N03 acceptée.** |
| Outil benchmark alternatif | Get-ChildItem tools -File filtré par bench/perf. | **0** | Uniquement **Benchmark-LuaEditor.ps1**, sans rapport avec CombatTurnPlanner. |
| Dépendances | Test-Path sur deux ProjectReference externes, assets.json et DLL runtimeconfig. | **0** | GivreProxy/GivreCore, GivreLauncher/GivreLauncher.Core, Test/obj/project.assets.json et Test/bin/Release/net10.0/*.runtimeconfig.json **présents dans ou autour de la source** ; des assets de la source ne sont pas réutilisables aveuglément après une copie. |

**Note de méthode :** « code retour 1 » du dernier Get-Process n'est pas une erreur d'autorisation, mais aucun processus correspondant à ce **dernier instant**. La capture initiale de rg sur tout le dépôt a eu **code 2** à cause de deux répertoires non accessibles ; cet incident ne démontre aucune modification ou absence générale d'AGENTS. Les répertoires et PID sont des métadonnées de contrôle, aucune ligne de commande pouvant contenir des secrets n'a été copiée.

## Procédure conditionnelle à exécuter ultérieurement — non exécutée

### Phase 0 : garde de sécurité impérative

1. Le propriétaire des sessions décide lui-même qu'elles sont terminées. **Ne jamais arrêter, suspendre, tuer, déplacer ou redémarrer les processus existants**, ni lancer Dofus ou GivreClone.
2. Dans un nouveau terminal PowerShell, confirmer à deux instants rapprochés l'absence **de Dofus, GivreClone, testhost, vstest, msbuild et dotnet pertinents** avec la commande ci-dessous ; vérifier aussi auprès du coordinateur l'absence d'un worker en compilation et l'autorisation du créneau. Cette commande est un **garde**, pas une preuve absolue d'absence de session :
    
        Get-Process | Where-Object { $_.ProcessName -match '(?i)dofus|givre|testhost|vstest|msbuild|dotnet' } | Select-Object ProcessName,Id,CPU,StartTime

3. Si une application, session ou compilation pertinente reste active, **STOP**, consigner l'horodatage et les PID, conserver le statut BLOQUÉ. Ne pas réessayer en boucle ni imposer une affinité CPU au jeu.

### Phase 1 : capture immuable et sortie strictement isolée

1. Définir une sortie **sous le dossier N03 seulement**, p.ex. chemin N03/isole/, et collecter d'abord la liste exhaustive des **ProjectReference, Compile Include, CopyToOutputDirectory** du projet de tests, y compris références dans **GivreProxy** et **GivreLauncher**, et les fichiers Directory.Build.*. Exclure de la copie **comptes, jetons, journaux, secrets, .git, sessions, installations du jeu**. Ne pas faire un robocopy aveugle de toute la racine contenant des fichiers privés.
2. Copier en lecture seule les **sources strictement nécessaires** à l'exécution de CombatTurnPlanner, les projets et les fichiers de build utilisés. Recréer leur arborescence **relative** sous N03/isole/GivreWork/ pour que les ProjectReference des projets frères ne pointent **jamais vers l'original**. Contrôler après copie que **toutes les références sont dans N03/isole**, et consigner chaque taille, SHA-256 source et copie.
3. Verrouiller le corpus : fixture existante full-board, **560 cellules / 3 fighters / 19 sorts / 11 PA / 5 PM**, budget 60k ; ajouter uniquement des variantes **déjà présentes dans les tests** (p.ex. le petit Board 7 cases, l'obstacle LoS dans CombatTurnPlannerTests, un tour sans sort), en conservant leur statut de **fixtures fonctionnelles**. Aucun combat live, compte ou protocole réseau. N'annoncer aucune performance d'une variante avant exécution.
4. L'assemblage Release existant **n'est pas une alternative sûre** car antérieur au code. Il faut **construire uniquement dans N03/isole**, avec le SDK .NET **déjà installé**, et vérifier un restore **strictement offline** à partir des paquets déjà présents. Si le projet réclame installation, paquet réseau, référence encore dirigée vers source ou artefact de build partagé : **STOP** et renseigner le blocage. Ne pas réutiliser directement les fichiers project.assets.json de l'arbre source, leurs chemins peuvent être absolus.
5. Après build isolé, enregistrer commande complète + code retour + sortie de construction + SHA-256 de DLL, PDB et versions du runner. Une DLL construite ne prouve pas qu'un test a été exécuté.

### Phase 2 : vérification préalable limitée du banc existant

Un **test fumée** doit être exécuté sur la **copie déjà construite**, avec sortie TRX/logs dans N03. Exemple de patron de commande, **non exécuté** ; la variable project doit être entièrement résolue dans N03 :

    dotnet test "<N03>/isole/GivreWork/fix-f01-f16-final-20261024/GivreClone.Tests/GivreClone.Tests.csproj" -c Release --no-build --no-restore --filter "FullyQualifiedName~CombatPlannerPerformanceTests.Full_board_budgeted_search_is_repeatable" --logger "console;verbosity=detailed" --results-directory "<N03>/mesures/smoke"

Conditions pour accepter le retour : code 0, **exactement le test visé exécuté** (pas « 0 tests »), assembly SHA attaché, aucune exception, même plan attendu. Le test n'expose que **15 mesures agrégées** et n'est donc **pas suffisant seul** pour l'exigence N03 de distribution complète et de décisions brutes.

### Phase 3 : banc temporaire de mesure stricte, dans N03 uniquement

Créer le banc **dans N03/isole/bench seulement**, sans modifier CombatTurnPlanner.cs ni CombatPlannerPerformanceTests.cs originaux. Il peut s'appuyer sur les API publiques du planificateur et les fixtures immuables copiées ; son code, sa version et ses commandes doivent être déposés dans N03 si l'exécution devient autorisée. **Aucun code de banc nouveau n'a été généré dans cette mission bloquée.**

1. **Chargement/JIT** : démarrer un processus neuf, horodater séparément chargement du runtime et construction du snapshot ; mesurer **premier appel Choose (cold)** isolément. Le warmup consistant à exécuter ensuite **au moins 30 appels** du même corpus **sans les inclure** dans les distributions est une **proposition de protocole**, pas une mesure.
2. **Décision pure** : pour chaque fixture et budget (proposer **5 000, 30 000, 60 000, 180 000 unités Work**, plus le budget par défaut si distinct), appeler **Choose(snapshot, maxWork)** sur snapshot déjà construit, et isoler les horodatages de Stopwatch.GetTimestamp / Stopwatch.Frequency. Proposer **au moins 200 itérations** en processus stable par cas, en utilisant plusieurs lots et ordre des budgets alterné. Reporter la durée réelle d'échauffement, la configuration runtime **TieredCompilation / TieredPGO**, la collecte GC et l'affinité par défaut ; ne pas comparer cold et warm dans un même percentile.
3. **Allocations** : GC.GetAllocatedBytesForCurrentThread avant et après Choose, soustraire, indiquer **thread courant seulement** ; capturer GC.CollectionCount(0/1/2) avant/après en dehors du chronométrage si possible. Ne pas présenter l'empreinte RAM du processus comme « allocation de Choose ».
4. **Choix** : enregistrer pour chaque invocation les actions **Move/Cast et leurs cellules, sorts, cibles, coût**, le Score complet, Work, BudgetExhausted, Unmodeled, sortie exception éventuelle. Comparer déterminisme de plan et détecter les cas où le budget change l'action. Toute différence de décision doit conserver les deux sorties brutes, pas seulement une moyenne.
5. **Nœuds** : la méthode publique ne donne **que Work**. Le champ nodes_expanded reste **null** tant qu'aucun outil indépendant n'en démontre le nombre. Ne pas instrumenter le code de production dans ce lot. Une future demande séparée pourra autoriser une télémétrie propre qui ne modifie ni le moteur actif ni les mesures chronométriques.
6. **Cas lents** : trier les échantillons **après** collecte, conserver les 10 plus lents avec fixture complète, budget, heure, Actions, Score et Work ; ajouter les cas ayant BudgetExhausted=true et les exceptions. Aucun cas lent observé à ce jour.
7. **Mesures statistiques** : sortir JSONL ou CSV brut par itération, plus p50/p90/p95/p99, min/max, moyenne et écart-type **calculés** ; consigner la méthode de percentile et nombre d'échantillons. Compléter un fichier manifest.json avec hash du source, binaires, OS/CPU/.NET, fichier brut et heures UTC.

### Phase 4 : contrôle d'intégrité et critère d'arrêt

- Avant et après l'essai, comparer les empreintes des **trois sources et deux binaires** relevées ici ; vérifier aucune écriture hors de N03/isole et N03/mesures. Si elles ont changé ailleurs par un autre acteur, signaler le changement ; ne pas nettoyer ou restaurer l'arbre.
- Journaliser **la commande réellement exécutée, code retour et sortie capturée** ; l'exemple de phase 2 n'est pas une commande réalisée le 04/11.
- Si un jeu/bot/compilation démarre pendant le banc : **interrompre uniquement le banc de mesure qui nous appartient si c'est sûr**, ne toucher à aucun autre processus, et invalider les échantillons contaminés. Aucun arrêt de processus ni lancement de banc n'a eu lieu dans cette mission.
- Clore N03 en **BLOQUÉ** si une seule condition manque. Le protocole produit une tâche reproductible, **pas de métrique**. Une reprise ultérieure peut produire de nouvelles preuves sans réécrire ce constat.

## Schéma des données brutes à produire après déblocage

**JSONL :** run_id, source_sha256, binary_sha256, fixture_id, snapshot_hash, cells, fighters, spells, ap, mp, budget_maxWork, run_kind (cold / warmup / warm), iteration, timestamp_utc, runtime, elapsed_ns, allocated_bytes_current_thread, gc0_delta, gc1_delta, gc2_delta, work_used, budget_exhausted, nodes_expanded (null en l'absence de mesure dédiée), actions_raw, score_raw, unmodeled, exception, process_list_sample_ref.

**N03 à ce jour :** liste des lignes de benchmark **vide**. [AUDIT_BRUT.json](./AUDIT_BRUT.json) contient uniquement les métadonnées réellement relevées et les champs explicitement nuls ; aucune valeur synthétique ne s'y glisse.
