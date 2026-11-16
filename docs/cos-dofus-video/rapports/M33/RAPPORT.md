# M33 — Équipes de cinq à huit combattants, compagnons et invocations

**4 novembre 2026.** Spécification de coordination pour les équipes étendues. La source [Dofus Pour Les Noobs — Compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html) décrit les compagnons contrôlés, [M06 Korbax](../M06/RAPPORT.md) inclut des transcripts MCP réels du compagnon ; [M07](../M07/RAPPORT.md) distingue acteurs de challenge. La taille effective admissible dépend du mode de combat et des règles du donjon : **l'exemple à 5–8 est une fixture de planificateur, pas une garantie que tous les modes autorisent ce format**.

## Les quatre problèmes spécifiques à l'équipe étendue

1. **Occupation** : deux placeurs ou invocations peuvent viser la même case ; un déplacement de groupe peut casser la portée d'un autre allié.
2. **Ordonnancement** : les améliorations de PA/PM et protections sont dépendantes des tours bénéficiaires, des durées et de la disponibilité des variantes.
3. **Identité d'auteur** : personnage, compagnon et invocation ont des IDs distincts ; les challenges peuvent compter leurs attaques différemment, selon la règle actuelle à prouver.
4. **Réponse adverse** : chaque nouvelle invocation offre une possibilité de blocage mais aussi une cible pour attaque de zone et un éventuel ennemi ressuscité/phase de boss.

## Exemple à six combattants (fictif, configurations à confirmer)

Équipe A : Pandawa (place), Enutrof (entrave), Crâ (tir), Féca (protection), Eniripsa (soin/buff), compagnon Korbax (poussée/placement). Ennemis E1 et E2 ; E1 protégé par une phase de boss, E2 menaçant Crâ. Tour hypothétique : P éloigne E2 de Crâ et libère l'angle du Féca → N tente ralentir E2 → Korbax recalcule une poussée uniquement si cela ne le conduit pas dans un glyphe interdit → C frappe E2 → F crée une zone de protection → Eni soigne le membre exposé. Chaque étape ne devient utilisable qu'après confirmation. L'ordre fictif ne dit rien des initiatives réelles.

### Deux contre-situations

**N1 conflit de case :** Pandawa veut déposer E2 en case q, Korbax annonce finir en q ; le premier déplacement réussi réserve q, le deuxième doit se recalculer. Une cellule de fin dans `TeamIntent` n'est pas une occupation confirmée et peut avoir changé.

**N2 challenge d'attaquant :** A=Crâ a frappé E2, Korbax veut frapper E2 ; selon la définition Duel historique, cela peut violer l'unicité de l'attaquant par ennemi si le compagnon est compté. **Le comptage exact 3.7.13.17 reste Unknown** ; l'IA doit tenir compte du risque et chercher une autre cible.

**N3 mort d'invocation :** le Féca protège une invocation hypothétique qui vient de mourir ; le plan ne doit pas cumuler sa protection fantôme avec le vrai score de survie.

**N4 faux soin :** un Eniripsa agit après le coup qui aurait dû sauver la cible ; le plan doit mesurer si la cible est encore vivante.

**N5 évènement boss :** un boss ressuscite un monstre au tour global ; la file de cibles à tuer est recalculée, pas « fin de combat ».

## Ajouts 7e et 8e combattants : bénéfice marginal

N'ajouter un acteur que si sa **fonction marginale** répond à une menace encore non couverte (dispel, soutien, cible secondaire, délock, protection d'une invocation) et si les **coûts** (encombrement, collisions, tours, contraintes de challenge, adaptations de boss) sont acceptables. Un second Crâ peut augmenter les tirs sans créer de nouvelle capacité de placement. Un second Pandawa peut améliorer l'accès aux deux ennemis, ou déplacer une cible hors de la zone du premier. L'algorithme doit comparer **l'action marginale réelle**, pas seulement additionner les meilleurs tours individuels.

## Couverture de classe et dossiers reliés

Toutes les classes possibles sont dans [l'atlas 19](../ATLAS_19_CLASSES.md). Les combats nécessitant marques/portails : [M01](../M01/RAPPORT.md), [M04](../M04/RAPPORT.md), [M09](../M09/RAPPORT.md). Les compagnons et challenges : [M06](../M06/RAPPORT.md), [M07](../M07/RAPPORT.md). Les invocations : [M18](../M18/RAPPORT.md), [M20](../M20/RAPPORT.md), [M21](../M21/RAPPORT.md). Les enjeux PvM : [bestiaire 3.7](../BESTIAIRE_ET_BOSS_3_7.md).

**Statut :** scénario proposé, non exécuté, aucun tutoriel spécifique de combat à huit personnages reconstitué dans ce rapport. Les vidéos M06 permettent des conseils sur Korbax, pas une preuve des six tours ci-dessus.

