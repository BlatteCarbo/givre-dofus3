# M29 — Fiche : prédicats de challenges pour tous les formats

**Référence documentaire** : [Les challenges](https://www.dofuspourlesnoobs.com/les-challenges.html), liste issue de 2.68 ; version 3.7.13.17 à confirmer. **Aucune victoire ou réussite de challenge exécutée par le laboratoire.**

## Contrat minimal d'identité

```text
actorId, playerAccountId?, companionId?, ownerSummonerId?,
actorType = player | companion | invocation | monster | Unknown
targetId, actionId, damageApplied, healApplied, killed, turnEnded
fightKey, actorTurn, globalTurn, lastServerEventId
positionCertainty, actorCell, partyCells
```

On distingue **lanceur du sort** et **propriétaire de l'invocation**. Une invocation pouvant déclencher un effet n'est pas automatiquement un combattant compté dans chaque challenge : hypothèse à vérifier par texte de défi et trame serveur.

## Machines à états

```text
Duel:
  enemy X (non assigné)
    └─ attaque alliée de A, si réellement comptabilisée → X attribué A
       ├─ attaque A sur X → admissible
       └─ attaque B sur X → violation (sous réserve statut compagnon)

De la même lignée:
  début tour A → déplacements/sorts observés → fin tour A
    ├─ au moins un autre combattant allié aligné → satisfait sur ce tour
    └─ aucun autre allié aligné → violation (selon version)
```

Les états de challenge sont des **conditions de combat indépendantes**, et une réussite ne se réduit jamais au score de dégâts.

## Comparaison solo → 2 → 3 → 4 → 5–8

En **solo**, un challenge requérant « au moins un allié » doit être vérifié dans ses conditions de génération et l'éligibilité éventuelle d'un compagnon ; ne pas le déclarer possible avec le seul personnage. En **duo**, distinguer chaque attaquant. En **trio**, alignement avec **un** autre ne veut pas dire tous. En **quatuor**, les placeurs modifient l'alignement de partenaires dont le tour est déjà fini. En **5–8**, les invocations peuvent créer des confusions d'identité et les kills indirects modifier des historiques.

## Exercice

A attaque X, B attaque Y, le compagnon C attaque X, A pousse B dans la même ligne que lui après le tour de B. Tracer (a) Duel : C et A sont-ils deux combattants comptabilisés ? (b) De la même lignée : à la fin de **quel** tour alignement examiné ? (c) Miroir : quelle cellule occupe un second allié **à l'instant exact du kill** ? Les réponses qui demandent la classification des compagnons restent `Unknown` jusqu'à preuve actuelle.

**Tests positifs/négatifs** : voir [RAPPORT.md](RAPPORT.md). Aucun test exécuté.

