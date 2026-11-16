# P04 — Effets critiques utiles au planificateur

Date : 2026-11-04. Statut : **audit exécuté de données sérialisées, aucun combat exécuté**. Cible : DOFUS **3.7.13.17**, build client 2026-11-03. Le résultat machine est [critiques_candidates.json](critiques_candidates.json), reproduit par [audit_crit.py](audit_crit.py).

## Résultat nouveau face à N01/N07

Sur **364 grades** des quatre classes Sram, Roublard, Xélor et Éliotrope, **183** possèdent une branche `critical` non vide et **181** n'en possèdent pas dans cet export. Parmi les 183 : **180 grades** présentent une variation `diceNum/diceSide` par index ; **3 grades** présentent une variation du champ `value` (Rémission I/II/III). Le contrôle de multiensembles sur les **19 champs structurels** (action, cible, durée, délai, état/condition, géométrie, groupe, trigger...) donne **0 différence structurelle** et **0 changement du nombre d'effets** entre les deux branches des 183 grades. Ce résultat concerne seulement les champs comparés et ce sous-ensemble : aucun effet critique ajouté/retiré, changement de cellule ou invocation propre au critique n'est prouvé ici.

Une comparaison naïve par `effectIndex` produit des différences de `targetMask`/`duration` pour Rémission et Clépsydre : **les mêmes effets changent d'ordre**, sans prouver un changement de cible/durée. Les `effectUid` diffèrent souvent entre normal/critique ; ce sont des identifiants de lignes distincts, pas des sorts distincts.

## Douze exemples exacts (grade 1)

Les valeurs ci-dessous sont sérialisées ; les bornes de dés ne sont pas des dommages appliqués, car résistances/états/conditions sont encore à résoudre.

| Classe, sort et ID | Action / index | Normal → critique | `criticalHitProbability` du grade | Interprétation contrôlée |
| --- | --- | --- | ---: | --- |
| Sram **Truanderie** 12902 | 97 / 2 | 15–17 → 19–21 | 15 | Bornes de valeur |
| Sram **Poisse** 12922 | 91 / 1 | 29–32 → 35–39 | 20 | Vol de vie/dégât à vérifier par cible |
| Sram **Extorsion** 12938 | 92 / 1 | 17–19 → 20–23 | 15 | Bornes de valeur |
| Roublard **Extraction** 13433 | 94 / 0 et 1 | 16–18 → 20–22 ; 8–9 → 10–11 | 25 | Deux lignes d'effet, chaque cible distincte |
| Roublard **Rémission** 13445 | 265 / 8 | `value=10` → `13` | 25 | **Différence non assimilable à des dégâts directs** ; sens de l'action 265 à prouver |
| Roublard **Plombage** 13488 | 99 / 0 | 30–34 → 37–42 | 15 | Bornes de dégâts |
| Xélor **Rouage** 13283 | 92 / 3 | 16–18 → 19–21 | 15 | Bornes de valeur |
| Xélor **Clépsydre** 13298 | 96 / 7 | 33–37 → 40–44 | 15 | Bornes ; les swaps d'ordre des indices 1–4 ne changent pas le multiensemble structurel |
| Éliotrope **Affront** 14575 | 98 / 0 | 15–17 → 18–20 | 10 | Bornes |
| Éliotrope **Thérapie** 14581 | 92 / 0 et 3000 / 1 | 15–17 → 18–20 sur les deux | 10 | Dégât et ligne de soin indexée séparément |
| Éliotrope **Cicatrisation** 14587 | 1109 / 0 | 6 → 8 | 15 | Soin indexé par PV maximum dans le simulateur courant |
| Éliotrope **Sinécure** 14610 | 93 / 0 et 2999 / 1 | 9–11 → 12–14 sur les deux | 5 | Ligne de dégâts et ligne de soin |

Ces douze identifications proviennent de la jointure `spellId + grade + branch + effectIndex` entre N01/LEVELS et N01/EFFECTS. Chaque exemple possède `effectUid` normal/critique et changement détaillé dans la fixture. La probabilité du grade **n'est pas nécessairement la probabilité effective** après caractéristiques, bonus, plafond, état ou réduction serveur.

## Conséquence concrète pour l'espérance

Le résolveur actuel indique explicitement qu'il simule une moyenne de jet **sans critique** : `GivreClone.App/Services/Combat/SpellResolver.cs:62-66`. Les actions **115** (modificateur de chance critique) et **418** (bonus aux dommages critiques) existent dans `SpellResolver.cs:111-122`, sans preuve que le moteur actuel les applique au bon cast futur. La caractéristique `CriticalHit` est bornée dans `DamageEstimator.cs:52-53` ; cette borne interne ne valide pas la formule de probabilité DOFUS.

Pour une métrique `M` modélisée, proposer :

`E[M | état vérifié] = (1-p_eff) × E[M_normal] + p_eff × E[M_crit]`,

où `p_eff` vient d'un oracle indépendant qui recoupe les conditions du cast, les bonus 115/418 et les plafonds. Pour dommage, soin et bouclier, calculer chaque branche **après** résistances, limitations et états; pour état, mouvement, invocation, effet retardé et choix aléatoire, conserver des issues discrètes et **Unknown** si la distribution manque. Un `value=13` pour l'action 265 n'est pas un gain de 3 dégâts tant que sa sémantique n'est pas démontrée.

## Oracles avant développement

1. Comparer, sur un même sort/grade, l'événement de crit confirmé, les lignes d'effets réellement appliquées, PV/PB et positions avant/après ; refuser une moyenne si la branche est partielle.
2. Étalonner `p_eff` sur des séries de casts autoritativement reçus avec et sans bonus **115**, puis avec bonus **418** en distinguant fréquence et dégâts critiques. Documenter équipements, cible, version et éventuels états.
3. Vérifier **Rémission 13445 action 265** : confirmer si `value` de 10/13 modifie un effet de protection et sa durée/masque ; les lignes 950/1160 sont réordonnées et ne suffisent pas à conclure.
4. Vérifier que `criticalEffect` absent signifie branche normale, impossibilité de critique, ou information manquante selon la règle de moteur : les seules données de N01 ne tranchent pas.

**Livré :** statistiques reproduites, douze exemples tracés, calcul candidat et oracles à obtenir. **Non livré :** calibration serveur de `p_eff`, replay de crit, patch du résolveur.
