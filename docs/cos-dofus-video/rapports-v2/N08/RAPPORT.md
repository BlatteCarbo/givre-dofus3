# N08 — Audit adversarial des soutiens, sans nouveau combat

**4 novembre 2026 — audit statique, sans exécution de test ni modification du code.** Les actions `115/118/138/418` disposent déjà d'un routage des statistiques dans `SpellResolver.StatDelta` ; ce rapport **ne propose pas de refaire ce correctif**. Objectif : isoler des lacunes de simulation et de sélection de soutien qui ne sont pas couvertes par les fixtures consultées.

## Résultat

| ID | Cas discriminant | Observation étayée | Qualification |
| --- | --- | --- | --- |
| N08-A | Soigner une cible **Insoignable** | `SpellResolver.Heal` applique `min(soin, PV max−PV)` puis `HealingResolved` **sans vérifier l'interdiction de soin** ni lire une règle versionnée Insoignable ; `SpellFighter.States` existe | **Limite de modèle démontrée dans ce résolveur**, effet précis de l'état en 3.7.13.17 non encore prouvé. Le vrai résultat serveur est `Unknown` |
| N08-B | Donner PA/PM à un allié qui **n'a aucun sort atteignable** avant expiration | `CastOn` valorise directement `PointsResolved` positifs sur les alliés (6 points/PA, 3 points/PM), **indépendamment** de la prochaine attaque du bénéficiaire ; le test `Resource_only_rotation_boosts_an_ally_but_not_an_idle_caster` **attend même** un boost d'allié sans attaque dans sa fixture | **Heuristique documentée et couverture adversariale manquante**, pas preuve d'incident live |
| N08-C | Buff bénéfique **après** le tour de l'allié, ou bouclier qui expire avant la réponse adverse | `SpellResolver` décrit explicitement `buff duration/expiry and hooks need a turn simulator`. `CombatTurnPlanner.Rank` évalue un prochain cast **du lanceur**, pas le tour effectivement restant du bénéficiaire. Les tests de bouclier traitent exposition et réservations, pas l'ordre d'initiative/expiration | **Horizon de simulation absent**, à éprouver par deux fixtures identiques sauf ordre de tour et durée |

**Couvertures à préserver** : `Team_healer_prioritizes_the_unprotected_ally_using_observed_shield` ; `Team_healer_spends_its_cast_on_missing_healing_not_another_allies_reserved_heal` ; `Team_shield_reward_excludes_promised_protection_without_assuming_it_for_survival` ; `Percentage_healing_uses_target_maximum_life_not_the_casters_intelligence` ; masques, état absent connu et accès aux cibles dans les autres tests. Une limite « bouclier déjà suffisant » est **partiellement prise en charge** par la compensation d'exposition et les réservations ; ne pas la signaler comme défaut régressé sans test nouveau.

## Oracles proposés, indépendants des scores actuels

1. **Soin interdit** : deux plateaux identiques avec allié A à 30/100 PV, même cible, même coût et distance ; la seule variable est un état `Insoignable` confirmé vs absent. Oracle : la branche de soin **ne peut gagner de PV** si la source client exacte confirme cette interdiction ; sinon `Unknown` et interdiction d'accorder de points de guérison hypothétiques. Comparer `HealingResolved` et état après serveur. Ne pas choisir d'ID d'état au hasard.
2. **Boost utile/inutile** : un allié B prêt à jouer peut effectivement attaquer sa cible avec le bonus ; contrôle inverse : cible hors portée/LoS et aucune autre action consommant les ressources avant expiration. Oracle : **gain marginal confirmé** seulement dans le premier cas, zéro par hypothèse d'utilité dans le second ; ne pas scorer `+PA` comme `+dégâts` sans attaque possible.
3. **Initiative et durée** : S (soutien) puis B (bénéficiaire), comparé à B puis S, ainsi que protection expirant avant l'attaque ennemie. Oracle : une même action ne procure pas une valeur de protection ou d'attaque **rétroactive** ; les preuves requises sont l'identité du bénéficiaire, l'ordre des tours et l'expiration réellement datée.

## Arrêt et limites

Trois **lacunes de modèle/coverage** identifiées avec emplacement précis ; aucune fausse observation serveur, aucun résultat xUnit inventé. L'impact réel dans le client 3.7.13.17 dépend des tables de sorts, états, rangs et durées de N01/N07, puis d'une campagne offline séparée. Pas de changement du code, tests, source, jeu, compte ou Git.
