# M05-F01 — Attirance + attaque, versus marche + attaques

## Identité

- Identifiant stable : `M05-F01`.
- Classe : Sacrieur, voie eau choisie **pour le test**, non généralisable à toutes les voies ; adversaire E, allié facultatif H.
- Source vidéo : SAPEUH, [LE SACRIEUR SUR DOFUS UNITY !](https://youtu.be/_8JB1a1H1mU), 2024-10-19, ~15 min, pré-Unity ; Capitemflam, [leveling du Sacrieur 160–165](https://youtu.be/9OCwxSU83vk), 2025-02-28, ~1 h, Dofus 3.0.
- Version de vérification externe : guides tiers 3.7 (oct. 2026) et fiches OpenDofusDB non estampillées ; cible locale annoncée 3.7.13.17 **non testée**.
- Transcriptions : FR automatique (457 et 1224 fragments, récupération complète sans curseur) ; passages lus [00:44–01:05](https://youtu.be/_8JB1a1H1mU?t=44), [03:14–03:24](https://youtu.be/_8JB1a1H1mU?t=194), [10:17–10:35](https://youtu.be/_8JB1a1H1mU?t=617), [45:06–45:15](https://youtu.be/9OCwxSU83vk?t=2706), [46:48–47:16](https://youtu.be/9OCwxSU83vk?t=2808). Aucune image inspectée : `observe-video` **non attribué**.
- Méthode : extraction directe par MCP de transcript, recoupement documentaire. Orthographe ASR fautive : « transpo », « transport », noms de sorts incertains.

## Règles atomiques et preuves

| ID | Affirmation / provenance | Statut | Preuve précise | Limites |
| --- | --- | --- | --- | --- |
| M05-R01 | Attirance rang 3 : coût 2 PA, 2–10 PO, en ligne, tire la cible | `corrobore-source` | [OpenDofusDB Attirance](https://openddb.dev/spells/12735-attirance) | Souffrance >=6 modifie la portée/longueur. Version exacte du client à certifier. |
| M05-R02 | Transposition rang 3 : 3 PA, 1–9 PO, échange de cellules, 3 tours de relance | `corrobore-source` | [OpenDofusDB Transposition](https://openddb.dev/spells/12736-transposition), [Grimoire 3.7](https://dofus-grimoire.fr/classes/sacrieur/) | États empêchant le sort non confirmés par fiche pour cible actuelle. |
| M05-R03 | Mutilation : 2 PA, PV du lanceur sacrifiés, augmente **sa** puissance | `corrobore-source` | [OpenDofusDB Mutilation](https://openddb.dev/spells/12737-mutilation) | Niveau/rang, valeur exacte du bonus et coûts répétés ultérieurs à lire du client. |
| M05-R04 | Dissolution 4 PA (vol de vie en zone), Stase 3 PA (1–5 PO, maximum 3/tour) | `corrobore-source` | [Dissolution](https://openddb.dev/spells/12757-dissolution), [Stase](https://openddb.dev/spells/12728-stase) | Aire exacte et résolutions dégâts/soins restent à vérifier dans les données client. |
| M05-R05 | L'attraction finit avant une case occupée/obstacle ; certains états interdisent le mouvement | `corrobore-source` pour guide, `hypothese` 3.7.13.17 | [Placement et déplacement, Dofuspourlesnoobs](https://www.dofuspourlesnoobs.com/placement-et-deacuteplacement.html) | Article non daté pour le client actuel, tests de plateau requis. |
| M05-R06 | Vidéo 2024 : le joueur écarte « Transpo » sous pesanteur et utilise Fluctuation pour rejoindre la cible | `declare-video`, `obsolete-pour-cible` à contrôler | [SAPEUH 10:17–10:35](https://youtu.be/_8JB1a1H1mU?t=617) | Sous-titre automatique ; pas d'observation directe du résultat, pas de règle exacte 3.7. |
| M05-R07 | Vidéo 3.0 : joueur veut permuter et laisser son épée au contact pour temporiser | `declare-video` | [Capitemflam 46:48–47:16](https://youtu.be/9OCwxSU83vk?t=2808) | L'ASR dit « transport », identité exacte du sort incertaine. |

## État initial reproductible — hypothèse de test

- Sacrieur S=(0,0), ennemi E=(8,0), allié facultatif H=(-1,1). Une ligne horizontale de cellules libres les sépare ; aucune autre entité, obstacle ou zone de danger.
- 12 PA / 6 PM **fixés par fixture**, non relevés d'une vidéo ; rang 3 pour Attirance, Stase et Mutilation ; Dissolution au moins acquise ; tous sorts actifs et hors relance ; Souffrance <6 ; PV disponibles pour Mutilation.
- Les distances, lignes de vue (LdV), choix de variantes, résistances, tacle, états d'immobilisation, challenges et durée des effets restent à **vérifier sur données de combat**. Définir `movementBlocked=false`, `lineOfSight=true` uniquement dans le test positif ; ce sont des entrées, pas des observations.

## Séquences et transitions attendues (simulations hypothétiques)

### Plan A — S s'approche

| Étape | Acteur | Action et cible | Condition | PA / PM dépensés | État après | Preuve du type d'action |
| --- | --- | --- | --- | --- | --- | --- |
| A1 | S | Se déplacer (0,0)→(3,0) | chemin libre, 3 PM | 0 PA / 3 PM | S=(3,0), 12 PA/3 PM | hypothèse fixture |
| A2 | S | Mutilation (S) | PV suffisants | 2 PA / 0 PM | 10 PA/3 PM ; PV diminués, puissance activée | [Mutilation](https://openddb.dev/spells/12737-mutilation) |
| A3 | S | Stase (E) | E à 5 PO, LdV valide | 3 PA / 0 PM | 7 PA/3 PM ; E/PV mis à jour seulement à confirmation | [Stase](https://openddb.dev/spells/12728-stase) |
| A4 | S | Stase (E) | E vivant, limite de lancer | 3 PA / 0 PM | 4 PA/3 PM | idem |
| A5 | S | Stase (E) | E vivant, troisième lancer | 3 PA / 0 PM | **1 PA/3 PM** | idem |

### Plan B — attirer la cible

| Étape | Acteur | Action et cible | Condition | PA / PM dépensés | État après | Preuve du type d'action |
| --- | --- | --- | --- | --- | --- | --- |
| B1 | S | Mutilation (S) | PV suffisants | 2 PA / 0 PM | 10 PA/6 PM ; PV baissés, puissance personnelle | [Mutilation](https://openddb.dev/spells/12737-mutilation) |
| B2 | S | Attirance (E) | E aligné à 8 PO, trajectoire libre, mobile | 2 PA / 0 PM | 8 PA/6 PM ; **E attendu (1,0)** par arrêt avant S, à contrôler | [Attirance](https://openddb.dev/spells/12735-attirance), [obstacles](https://www.dofuspourlesnoobs.com/placement-et-deacuteplacement.html) |
| B3 | S | Dissolution sur zone contenant E | cible encore en zone, LdV & cellule légales | 4 PA / 0 PM | 4 PA/6 PM ; dégâts, soin et fuite après effet confirmé | [Dissolution](https://openddb.dev/spells/12757-dissolution) |
| B4 | S | Stase (E) | E à 1 PO, vivant | 3 PA / 0 PM | **1 PA/6 PM** | [Stase](https://openddb.dev/spells/12728-stase) |

Au **refus** de B2, E reste (8,0) jusqu'à preuve du contraire ; B3 ne devient pas légal par simple ACK de commande. Au succès mais état E non reçu : position `Unknown` et **ne pas déclencher B3**. En cas d'effets/PA divergents, recalculer immédiatement et abandonner la séquence figée.

## Intérêt tactique, boost et exposition

- L'utilité de B est le repositionnement pour ouvrir une zone de dommages / un ally-followup, et la préservation de 3 PM par rapport à A. Son coût est **2 PA sans dégâts directs**, avec augmentation potentielle du danger près de l'équipe.
- La valeur de Mutilation doit être calculée séparément : `gain de dégâts des attaques réellement légales (présent + tours futurs) + survie liée aux soins/boucliers - attaque sacrifiée par 2 PA - dommage/risk des PV sacrifiés`. Le sort augmente la puissance **de S** ; un allié peut profiter d'E déplacé, pas de la puissance de S.
- Comparateur sans boost B' : Attirance (2) + Dissolution (4) + 2 × Stase (6) = **12 PA**, si E survit et tous lancers légaux. Le choix boost vs Stase supplémentaire est **indéterminé** faute de statistiques, résistance, PV, durée de combat et niveaux exacts.
- **Fallback** : choisir A si B2 impossible ; sinon Transposition d'un allié H (3 PA) seulement si la destination sûre, portée, état, coût et relance sont revérifiés ; ne pas transformer une démo ancienne en autorisation de cast.

## Tests discriminants proposés (aucun exécuté)

| Cas | Entrées | Actions | Résultat attendu à observer | Distingue |
| --- | --- | --- | --- | --- |
| M05-T+ | S=(0,0), E=(8,0), ligne libre, Souffrance <6, 12PA/6PM, rang 3, mobile | B1→B2→B3→B4 | B2 amène E à (1,0) si trajet/législation actuelle confirmés ; 1PA/6PM ; attaques seulement après positions certifiées | M05-R01/R02/R04 |
| M05-T-obstacle | Même état, entité bloquante en (3,0) | B1 puis B2 | Déplacement s'arrête avant obstacle / refus selon la sémantique effective ; **jamais** E=(1,0) par défaut ; replanification B3 | M05-R05 |
| M05-T-align | E=(8,1), rang 3 Attirance « en ligne » | B2 | Attirance refusée ou illégale, PA non supposés consommés | M05-R01 |
| M05-T-state | E=(8,0) état Indéplaçable | B2 | Pas de déplacement certifié ; B3 non présumé légal depuis E=(1,0) | M05-R05 |
| M05-T-order | S=(0,0), E=(8,0) | Dissolution avant Attirance | L'attaque de zone ne bénéficie pas du placement ultérieur ; comparer au plan B | effet de l'ordre |
| M05-T-boost | PV juste au-dessous du seuil de sécurité ; mêmes dégâts de base | comparer B et B' | Priorité à B' si Mutilation compromet la survie ; classement chiffré impossible sans données | M05-R03 |

## Passage développeur / reprise

Conserver identité acteur, PA/PM par acteur, cellules certifiées, portée + obstacles, états de déplacement, rang, variantes, relance, portée effective selon Souffrance, PV avant/après Mutilation et confirmation d'effet. Le moteur peut comparer les **coûts** et la **légalité conditionnelle**, mais ni le rendement exact ni la séquence vidéo sans images. Suivi : V05-A 10:17, V05-B 46:48 pour preuve visuelle, puis table client 3.7.13.17. Tests : proposés uniquement.
