# M26 — Passage au développeur : automate Folie Koutoulou

## Trois règles prioritaires

1. **Folie est un état par combattant**, pas un score global. [Guide Temple de Koutoulou](https://www.dofuspourlesnoobs.com/temple-de-koutoulou.html), `corrobore-source`. Chaque changement doit être associé à la bonne identité et au bon tour ; ne jamais présumer le nombre restant lors d'un événement non identifié.
2. **La version 3.7 permet de réduire la Folie d'un allié en lui infligeant des dégâts.** [MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Le déclencheur réel est une application de dommages, pas une demande de cast ; le bénéfice doit être mis en balance avec PV/bouclier perdus et objectifs de challenges.
3. **Les dommages directs peuvent provoquer une permutation distincte du sort de déplacement propre au boss.** [Guide boss](https://www.dofuspourlesnoobs.com/temple-de-koutoulou.html). Tester plusieurs lignes de dommages, historique de partenaire, positions/états, et différencier immunités de téléportation.

## Entrées nécessaires

`fightId, monsterTemplate, gameBuild, globalTurn, actorTurn, actorId, allyIds, bossId, folieState[actorId], cells[actorId], knownStatuses[actorId], cooldowns, damageType, damageLineIndex, statesObservedAt, challengeId`.

Actions candidates avec `sourceActor, targetActor, targetCell, baseDamageRisk, expectedFolieReduction, forcedMovementEvents`. Les sorties `ObservedFolieEvent` et `ObservedMovement` doivent être appliquées **seulement après réponse serveur**. Le couple d'identités suffit à mémoriser les échanges répétés, mais les limites exactes de l'effet par tour exigent traces de la cible.

## Tests proposés

- **P01** : A1 inflige une ligne de dommage confirmée à A2 en Folie >0 ; vérifier la diminution réelle et le coût en PV/bouclier.
- **N01** : cast refusé ou effets inconnus, pas de diminution fictive.
- **N02** : A1 frappe E1 directement, non un allié ; aucune baisse de Folie d'A2, un swap différent peut être reçu.
- **N03** : deux lignes de dommages successives, identité du partenaire de swap recalculée après la première.
- **N04** : sous Pesanteur, distinguer capacité propre de boss (empêchée selon guide) et swap déclenché par des dommages directs (règle distincte).
- **N05** : A2 finit son tour en LdV du boss puis un autre allié finit à couvert ; seules les bonnes identités gagnent Folie.

## Branches solo, duo, trio, quatuor

Le contrôle de danger doit résister à l'initiative inversée, au sort d'allié qui ne peut être dirigé sur un ami, à l'impossibilité de finir hors LdV et au challenge interdisant une attaque pourtant utile. Le plan principal n'est rentable que s'il évite un seuil fatal ou une perte de tour plus coûteuse que la perte PV infligée volontairement.

**Statut** : analyse uniquement. Sources communautaires version 3.7 et guide multi-patch ; client 3.7.13.17 et messages exacts non observés, pas de test ni combat. Bloqueur : replay expurgé montrant baisse de Folie sur un allié et ordre des swaps.

