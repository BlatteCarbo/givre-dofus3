# N03 — Passage développeur : instrumentation décisionnelle hors jeu

**État : BLOQUÉ, 0 décision mesurée, aucune modification du moteur.** Machine active avec Dofus et GivreClone pendant l'audit du 04/11/2026. Les anciennes DLL de test ne correspondent pas temporellement aux fichiers source actuels. Aucun benchmark honnête ne peut être conclu de ces éléments. Voir [RAPPORT.md](./RAPPORT.md), [PREUVES.md](./PREUVES.md), [AUDIT_BRUT.json](./AUDIT_BRUT.json).

## Trois constats exploitables

| Priorité | Ce qui est prouvé | Point d'intégration concret |
| --- | --- | --- |
| **1 — mesure unitaire déjà partielle** | GivreClone.Tests/CombatPlannerPerformanceTests.cs **lignes 13–51** définit une fixture de 560 cases/3 acteurs/19 sorts, 11 PA/5 PM, budget 60k ; warmup 1+5 et **15 mesures** ; log min/médiane/max temps et médiane allocations, plus actions/score de plan attendu. **Aucune série brute** n'est publiée. | Réutiliser cette **fixture exacte** comme contrôle de non-régression ; créer une instrumentation externe mission-only si un futur créneau isolé est autorisé. Ne pas modifier ce test du projet actif. |
| **2 — Work n'est pas nombre de nœuds** | CombatTurnPlanner.cs **lignes 61–71, 122–139, 186–214, 520–628** : budget partagé entre construction de la menace, parcours, candidats, réponses adverses et beam ; MaxWork **180k**, BeamWidth **8**, MaxDepth **3**. La sortie expose **Work** et BudgetExhausted mais pas nodesExpanded. | Reporter **work_units** et **budget_exhausted** tels quels ; **nodes_expanded=null** jusqu'à preuve instrumentée dédiée. Une mesure d'unité Work ne prouve pas « nœuds/s ». |
| **3 — environnement non sûr et build périmé** | Dofus PID 14252, GivreClone 15976, launcher 11356 actifs ; des dotnet ont été vus brièvement. Release DLL **03/11 12:49Z**, Debug DLL **03/11 17:07Z**, mais fixture **03/11 18:05Z** et planificateur **04/11 08:36Z**. | Ne faire ni build/test/bench sur l'arbre actif, ni exécuter ces anciens binaires comme mesure de source actuelle. Attendre fin naturelle des sessions, copier dépendances et compiler **seulement sous N03/isole** après vérification offline. |

## API et données contractuelles

**Choix cible :** CombatTurnPlanner.Choose(CombatTurnSnapshot snapshot, int maxWork = MaxWork), appelé sur une fixture **déjà construite**. Retour : CombatTurnPlan.Actions (Move/ Cast, cellule, sort, cible, coûts), Score (Damage/Finishes/Healing/Shielding/Incoming/Position/Uncertainty/Control/FriendlyFire), Work, BudgetExhausted, Intent, Unmodeled.

**Entrée nécessaire :** cellules et voisinage, marche et LoS, combattants/UID/équipes, PA/PM/HP/états, sorts et variantes, cellules légales, policy de mouvement et style, maxcasts, nombre d'ennemis, menace connue ou non, et budget maxWork. Les 560 cases / 3 fighters / 19 sorts du test ne constituent **qu'un point** du corpus. Les variantes 7 cases, LoS bloquée, sans sort, budget saturé sont documentées dans les tests fonctionnels, mais **aucune durée n'a été mesurée pour ces cas**.

**Schéma minimal de sortie** (fichier JSONL par invocation, pas seulement log xUnit agrégé) :

- Référence source et DLL SHA-256, version .NET, CPU, RAM, date UTC, état des processus concurrents.
- Fixture ID et hash du snapshot, 560/7 cases, nombre de combattants et sorts, budget demandé, warm/cold.
- Durée **Choose seule** en ns/ms, allocation courante en octets, compte GC (si observables), Work consommé, BudgetExhausted.
- Nombre de nœuds uniquement **s'il est instrumenté indépendamment**, autrement null ; nombre d'actions et actions exactes ; Score par composant, exception, état du résultat.
- Une ligne par itération, manifest horodaté et hashé, classification slow case dérivée des **données de la même série**.

**JIT et chargement :** séparer chargement/initialisation runtime, construction du snapshot, premier Choose, 30 warmups proposés, puis répétitions stables (proposition 200 par fixture et budget). Le premier appel ne doit pas se retrouver dans la médiane du steady state. Les options TieredCompilation et TieredPGO doivent être enregistrées, pas déduites du code.

## Décisions brutes et budget : oracles utiles

**Oracle 1** : pour une même fixture, le test existant affirme que les quinze plans ont les mêmes Actions, Score et Work que le plan attendu ; c'est une **assertion lue**, non un résultat exécuté N03. En reprise, enregistrer chaque plan et comparer ces trois champs, y compris divergence de tie-break et de budget.

**Oracle 2** : lorsque BudgetExhausted=true, qualifier séparément **decision_stable** (même actions/score entre répétitions) et **quality_versus_budget** (le plan change-t-il entre 5k/30k/60k/180k ?). La stabilité ne prouve **pas** l'optimalité. Une baisse du temps avec un budget plus faible n'est pas un gain d'algorithme si la décision perd en qualité.

**Oracle 3** : cataloguer les 10 pires échantillons **réellement mesurés** par durée, avec fixture, actions, score, Work, budget et erreur éventuelle. Ne pas baptiser une fixture « cas lent » sur la seule taille du plateau : le coût dépend de la menace/LoS/sorts et des chemins explorés.

**Oracle 4** : en présence de deux plans différents, vérifier l'équivalence de l'état de départ, le budget et les entrées exactes avant d'attribuer la variation au JIT/GC. Conserver les deux plans bruts. Aucune hypothèse de gain de vitesse ou de correction n'est émise ici.

## Protocole de validation

1. **Garde impérative** : aucune session Dofus ou GivreClone, aucune compilation concurrente liée au projet ; demander au coordinateur une fenêtre sans processus actif avant d'aller plus loin. Ne pas tuer ni interrompre les processus d'autrui. Si non vérifiable : rester BLOQUÉ.
2. **Copies isolées** : reproduire la fermeture des ProjectReference et Compile Include dans N03/isole, y compris GivreProxy/GivreCore et GivreLauncher/GivreLauncher.Core ; pas de réutilisation du bin/obj actif. Exclure les secrets, comptes et journaux. Vérifier l'origine de tous les chemins.
3. **Build offline de la copie** : SDK .NET préinstallé, aucun package réseau ni installation, code retour/console log, hash DLL. Si une dépendance n'est pas localement disponible : STOP.
4. **Test fumée préexistant** : utiliser la fixture xUnit sur l'assemblage **nouvellement construit dans N03**, vérifier exactement un test exécuté et code retour 0 ; capturer TRX/log sans attribuer aux agrégats une distribution complète.
5. **Banc N03 distinct** : mesurer cold vs warm séparément, corpus et budgets figés, 200 appels proposés/cas, JSONL brut et p50/p90/p95/p99/min/max calculés et contrôlés ; sans instrumenter le code de production.
6. **Intégrité et handoff** : prouver qu'aucune sortie ou fichier n'a été modifié ailleurs ; joindre hash source/assemblage, commandes, codes retour, logs, exemples de décision et 10 cas les plus lents. Si un blocage revient, fournir le protocole actualisé, jamais des chiffres interpolés.

## Blocages à lever explicitement

- **B01** : jeu et bot en activité lors de la mission — aucune autorisation de les arrêter ou de perturber leur CPU.
- **B02** : le moment où plusieurs processus dotnet ont été observés ne permet pas de certifier une absence de build concurrent. Aucune attribution de ces processus.
- **B03** : artefacts Release/Debug datés d'avant les sources ; un no-build ne représente pas le code courant.
- **B04** : le seul test performance fourni n'exporte que des agrégats et une décision attendue ; nœuds et cas lents bruts non instrumentés.
- **B05** : le graphe de projets contient des ProjectReference vers deux répertoires frères ; un build « juste du projet de tests » dans un dossier isolé incomplet pourrait retomber vers l'original.
- **B06** : absence d'AGENTS.md trouvés dans les zones contrôlées ; recherche globale a des dossiers non lisibles, pas de revendication exhaustive.

**Conclusion opérationnelle N03 :** protocole prêt, **aucune mesure exécutée**, aucune recommandation d'optimisation chiffrée. Demander une fenêtre de mesure sans session active et, si nécessaire, un accord distinct pour l'instrumentation diagnostique des nœuds (actuellement non disponibles).
