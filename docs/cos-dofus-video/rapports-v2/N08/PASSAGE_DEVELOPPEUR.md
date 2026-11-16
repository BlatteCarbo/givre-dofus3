# N08 — Passage au développeur (propositions seulement)

1. **Reprendre N01/N07 avant tout oracle.** Récupérer dans les données exactes de 3.7.13.17 le sort qui interdit les soins, son action et l'état appliqué ; vérifier si l'immunité porte sur tous les soins, le vol de vie ou seulement des catégories. Ne pas coder d'ID supposé.
2. **Fixture hors jeu A :** `SpellBoard` à PV manquants, allié en état d'interdiction confirmé ; tester `SpellResolver.Resolve` puis `CombatTurnPlanner.Choose`. Assertion souhaitée : aucune guérison certifiée, aucun score qui présuppose un soin impossible. Une ignorance du mécanisme doit produire un état `Unknown` et non un résultat favorable inventé.
3. **Fixture hors jeu B :** `PlannerSpell` 111/120/128 de buff allié, sans sort utilisable au prochain tour du bénéficiaire (portée, LoS, état). Comparer au scénario atteignable, mesurer `Score.Control` et la décision, avec séparation « prime du support » et « gain observé ».
4. **Fixture C :** deux calendriers S→B et B→S, puis un bouclier expirant avant le danger. Prévoir une représentation de durées/tours et un oracle basé sur la position temporelle du bénéficiaire, pas sur `Rank` caster-only.
5. **Vérifier la couverture existante** des tests de réservation d'heal/shield et des actions 115/118/138/418 avant d'ajouter quoi que ce soit. Geler les sources et sortir les données dans un worktree de développement séparé si un changement est autorisé.

**Aucun patch de production livré** et aucune prétention d'une exécution de ces trois fixtures : ce document est un contrat d'investigation priorisé.
