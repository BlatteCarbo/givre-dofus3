# N03 — Coût de décision du planificateur : audit hors jeu

**Examen local : 04/11/2026. Statut : BLOQUÉ — aucun benchmark exécuté.** Recherche, inventaire et vérification de l'environnement en lecture seule uniquement. Les trois documents et les preuves brutes sont dans le dossier N03 du worktree isolé worker-3.

## Décision de sûreté et résultat

**Le benchmark n'a pas été lancé.** Une session Dofus (PID 14252) et une application GivreClone (PID 15976) étaient en cours d'exécution au relevé, avec Ankama Launcher (PID 11356). Au second relevé, plusieurs processus dotnet sont apparus (PID 3956, 13060, 15180, 20256, 25708, 29220, 29224 ; disparus au contrôle suivant). Leur rôle précis n'a pas été inspecté : une compilation concurrente est **possible**, pas établie. Aucune session de jeu n'a été contrôlée ou manipulée ; la présence des processus empêche de certifier un environnement inactif et rend la charge CPU impropre à une mesure stable sans risque d'interférence.

**Deuxième obstacle indépendant : les binaires locaux sont obsolètes par rapport aux sources.** La DLL de test Release net10.0 date du **03/11/2026 à 12:49:22Z**, la Debug du **03/11 à 17:07:10Z**, alors que la fixture de benchmark a été modifiée le **03/11 à 18:05:17Z** et le planificateur le **04/11 à 08:36:02Z**. Un appel en mode no-build mesurerait potentiellement un algorithme et/ou une fixture antérieurs. Une nouvelle compilation depuis le worktree actif aurait violé le périmètre de la mission et peut entrer en conflit avec les travaux d'autres agents.

**Résultat chronométrique : non disponible.** Zéro décision mesurée, zéro durée, allocation, percentile, cas lent ou valeur de nœuds observés par cette mission. Ne pas attribuer de résultat de performance à l'ordinateur, à la session ni au code actuel. Les seuls nombres ci-dessous sont des paramètres **lus dans le code**, des caractéristiques de machine et des métadonnées de fichiers.

## Outils existants et corpus exact repéré

| Élément | Localisation relative à la racine source | Faits lus |
| --- | --- | --- |
| **Banc spécifique** | GivreClone.Tests/CombatPlannerPerformanceTests.cs, lignes **10–53** | Test xUnit Full_board_budgeted_search_is_repeatable. Construit **560 cellules**, **3 combattants** (UID fictifs 1,2,3), **19 sorts synthétiques**, **11 PA / 5 PM** ; appelle **CombatTurnPlanner.Choose(snapshot, 60_000)**. Exécute 1 appel initial pour établir le résultat attendu, puis 5 échauffements, puis **15 appels chronométrés**. |
| **Mesure existante** | Même fichier, lignes **34–51** | Stopwatch mesure chaque appel Choose ; GC.GetAllocatedBytesForCurrentThread mesure allocations du thread appelant. Test vérifie mêmes Actions, Score, Work pour les 15 itérations ; imprime **min, médiane, max de temps**, **médiane d'allocations**, **Work**, BudgetExhausted, Actions et Score du plan attendu. Il **ne stocke/affiche pas** les 15 mesures individuelles, p90/p95/p99, GC, chaque décision brute, les cas lents ou le nombre réel de nœuds explorés. |
| **Algorithme** | GivreClone.App/Services/Combat/CombatTurnPlanner.cs, lignes **61–71, 122–139, 186–214, 520–628** | BeamWidth=8, MaxDepth=3, MaxWork=180_000 ; budget partagé avec recherche de menace, parcours, cibles/sorts, réponses ennemies. Choose retourne **Work** = budget consommé, BudgetExhausted et Actions/Score ; **Work n'est pas un compteur de nœuds**. |
| **Autres fixtures** | GivreClone.Tests/CombatTurnPlannerTests.cs, exemples lignes **12–32** puis ~1000 lignes de tests | Générateur Board sur **7 cases**, sorts synthétiques et cas de portée, LoS, déplacement, contrôles/challenges, alliés et refus. Ce sont des **tests fonctionnels**, pas des benchmarks mesurés ni un corpus JSON prêt à exécuter. |
| **Autres tests pertinents** | CombatPlannerResolverTests.cs, BurstPlannerTests.cs, CombatDecisionTests.cs | Scénarios ciblés de résolution, burst, position, cible ; utiles pour les futurs **cas adverses**, mais leur chronométrage n'est pas déjà exposé. |
| **Outil hors sujet** | tools/Benchmark-LuaEditor.ps1 | Benchmark LuaEditor, **pas** décision de combat ; il ne convient pas à N03. |
| **Assemblages existants** | GivreClone.Tests/bin/{Debug,Release}/net10.0/GivreClone.Tests.dll | DLL et runtimeconfig disponibles, mais les DLL datent d'avant les deux sources essentielles. Elles ne doivent pas être présentées comme binaires du code actuel. |
| **Dépendances de compilation** | GivreClone.Tests/GivreClone.Tests.csproj, lignes **4–29 et 34–56** | Cible net10.0, xUnit/Test SDK. Lien des sources CombatTurnPlanner.cs et SpellResolver.cs depuis l'App ; références à des projets frères **GivreProxy/GivreCore** et **GivreLauncher/GivreLauncher.Core**, qui existent. Toute copie isolée doit conserver ces dépendances. |
| **Traces anciennes** | GivreClone.Tests/TestResults/*.trx | Des TRX existent. Recherche du nom du test de performance dans les TRX : aucun résultat (code retour rg=1). Ce constat ne prouve pas qu'aucune mesure historique existe ailleurs. |

## Environnement observé, hors mesure

- **Matériel** : HP Laptop 14s-dq2xxx ; Intel Core i7-1165G7, 4 cœurs / 8 processeurs logiques, horloge max déclarée **2701 MHz** ; RAM physique **16 805 027 840 octets**.
- **Système** : Windows, build **10.0.26300**, architecture **win-x64**.
- **SDK .NET** : **10.0.402** ; MSBuild **18.9.15** ; host/runtime .NET **10.0.13**, runtime **8.0.32** aussi installé ; test project TargetFramework **net10.0**.
- **Statut de processus** : jeu et bot en cours au relevé. Les commandes d'inventaire n'ont envoyé aucune commande au jeu ni à ces processus.
- **État du code** : SHA-256 dans [PREUVES.md](./PREUVES.md) et [AUDIT_BRUT.json](./AUDIT_BRUT.json). Les empreintes désignent des fichiers **source et anciens binaires** relevés séparément, non un build contrôlé.

## Valeurs qu'un futur benchmark doit sortir

Pour **chaque** échantillon, exporter les champs suivants : date/fixture/source hash/runtime/CPU, processus de mesure, budget maxWork, nombre de cellules/combattants/sorts/AP/MP, segment cold ou warm, indice de répétition, durée **Choose-only** en nanosecondes et millisecondes, allocations du thread en octets si disponibles, nombre **Work** consommé, BudgetExhausted, nombre de nœuds (champ **null tant qu'il n'existe pas d'instrumentation indépendante**), actions ordonnées brutes, Score détaillé, signal d'échec/exception, et hash canonique de la décision.

Les distributions **p50/p90/p95/p99**, min, max, écart-type et taux BudgetExhausted doivent se calculer **après** collecte, à partir de lignes brutes non agrégées. Les **cas lents** doivent être nommés par ID de fixture et accompagnés de snapshot canonique, actions/score/work/durée ; un « cas lent » n'est pas démontré sans mesures. **Les décisions brutes** doivent être conservées même quand elles diffèrent du premier plan. Séparer les mesures cold start (chargement, compilation JIT/TieredPGO) de Choose warm ; ne pas amalgamer le temps de génération des 560 cellules dans Choose.

## Budget et nœuds : distinction essentielle

La fixture existante impose **maxWork 60 000** ; le moteur autorise **MaxWork 180 000**. La méthode Budget.Take est sollicitée notamment pendant la construction des cartes de menace et la génération/évaluation des actions. Le résultat Work=60 000, quand la fixture l'affirme, désigne **les unités de budget comptées**, et non **60 000 nœuds**. L'exécution et la valeur réelle de Work n'ont pas été observées ici. Si le nombre de nœuds est requis, l'obtenir dans un **second passage instrumenté uniquement sur copie temporaire isolée**, avec code de diagnostic volontairement séparé du passage de chronométrage ; sinon renseigner null, jamais Work renommé « nodes ».

## Protocole prêt à reprendre

Le protocole de sécurité, la procédure d'isolement et les commandes sont dans [PREUVES.md](./PREUVES.md), la demande précise au développeur dans [PASSAGE_DEVELOPPEUR.md](./PASSAGE_DEVELOPPEUR.md).

**Critères de reprise :** absence **constatée** de Dofus, GivreClone et compilations pertinentes après que leurs propriétaires ont naturellement terminé leurs sessions ; corpus et dépendances copiés dans N03/isole, jamais compilés depuis la source ; hash des binaires construits depuis la copie ; échantillons complets et sortie capturée avec code retour 0. Sinon **garder N03 BLOQUÉ**, sans chiffre ni promesse de gain.
