# M08-F01 — Placer avant Colère avec soutien Élio/Féca

## Identité et acquisition

- ID : `M08-F01`. Quatre comptes : Pandawa (placeur), Éliotrope (portails/soutien), Féca (protection), Iop (dégâts). Un seul cas : `Karcham → Brassage → Colère` avec deux soutiens distincts.
- Vidéo principale : Tenmalexis, [La MEILLEURE TEAM de 4 sur DOFUS en 2025 ?!](https://youtu.be/YFlyjdSo4Ek), 2025-05-02, ~16 min, affichée comme MAJ 3.1 ; description mélange « 2.70 »/3.1. Vidéo annexe antérieure : [LES MEILLEURS TEAM DE 4 SUR DOFUS UNITY !?](https://youtu.be/RHEcA6s0a1A), 2024-11-23, ~17 min, chapitre 04:34, **description seulement**.
- Transcription principale : français original listé avec transcription automatique ; `lang=fr`, 225 fragments, curseur null. Passages [01:19–02:28](https://youtu.be/YFlyjdSo4Ek?t=79), [03:38–04:12](https://youtu.be/YFlyjdSo4Ek?t=218), [04:48–05:28](https://youtu.be/YFlyjdSo4Ek?t=288), [06:19–07:18](https://youtu.be/YFlyjdSo4Ek?t=379). Aucun plan visuel inspecté, aucune attribution `observe-video`.
- Référence cible : client 3.7.13.17 (indiqué dans le kit) ; sources Dofus Grimoire 3.7 et OpenDofusDB **sans extraction locale datée**. La vidéo 3.1 précède les changements de 2026.

## Règles atomiques

| ID | Affirmation | Statut | Preuve | Restriction |
| --- | --- | --- | --- | --- |
| M08-R01 | Pandawa Sobre : Karcham porte puis jette, coût 1+1 PA ; Brassage coûte 2 PA et augmente dommages subis par la cible | `corrobore-source` | [Karcham](https://openddb.dev/spells/12787-karcham), [Brassage](https://openddb.dev/spells/12816-brassage), [Grimoire 3.7 Panda](https://dofus-grimoire.fr/classes/pandawa/) | Cellule d'arrivée E, états empêchant le jet, droit de cibler E à contrôler ; aucun résultat serveur. |
| M08-R02 | L'Éliotrope peut poser 2 Portails à 1 PA chacun dans le même tour, si toutes les conditions légales réunies | `corrobore-source` | [Portail](https://openddb.dev/spells/14574-portail) ; tutoriel [04:48–05:28](https://youtu.be/YFlyjdSo4Ek?t=288) `declare-video` | Sans séquence de route inspectée, **aucun tir ni multiplicateur par portail n'est garanti**. |
| M08-R03 | Féca protège le Pandawa avec Bouclier Féca : 2 PA, relance 3 tours, résultat si cible en portée | `corrobore-source`, `declare-video` pour stratégie | [Bouclier Féca](https://openddb.dev/spells/12982-bouclier-feca) ; [vidéo 03:38–04:12](https://youtu.be/YFlyjdSo4Ek?t=218) | Le bouclier ne s'applique pas à Iop automatiquement ; priorités ennemies inconnues. |
| M08-R04 | Iop lance Colère à 1 PO pour 7 PA, retour sur 3 tours ; Puissance 3 PA pour bonus sur Iop | `corrobore-source` | [Colère](https://openddb.dev/spells/13124-colere-de-iop), [Puissance](https://openddb.dev/spells/13118-puissance) ; vidéo [07:05–07:18](https://youtu.be/YFlyjdSo4Ek?t=425) `declare-video` | Dégâts exacts non transférables de la vidéo ; relance/charge dépend d'un vrai lancer confirmé. |
| M08-R05 | À initiative I avant P, les changements de position de P n'existent pas encore pour I | `hypothese` de planification, conséquence du tour séquentiel | Fixture ci-dessous + règles coûts/portées ci-dessus | Tester par trace événementielle, ne pas attribuer cela à un plateau vidéo précis. |

## État initial (fixture synthétique, non observée)

```text
Grille orthogonale libre, échelle = 1 case.
I = Iop      (0,0)      12 PA / 6 PM
P = Pandawa  (5,0)      12 PA / 6 PM   état Sobre
E = ennemi   (6,0)      action adversaire non simulée
L = Élio     (1,3)      12 PA / 6 PM
F = Féca     (4,3)      12 PA / 6 PM
Jet visé :   (4,0)     libre avant Karcham
Portails :   A=(1,2), B=(4,2) libres au placement
```

Équipement permettant 12/6, niveau 200, variantes, visibilité, obstacles, états d'immunité, PV, résistances, cooldowns initiaux et ligne de vue sont des **préconditions à renseigner**, pas des faits observés. Pour ce test, tous les sorts indiqués sont appris et disponibles, aucun allié ne tacle le mouvement sur la trajectoire I→(3,0), aucun challenge restrictif. Déterminer serveur/id de cellule réel avant reproduction.

## Plan chronologique positif P → L → F → I

| Étape | Acteur | Action cible | Précondition | Coût de **cet acteur** | Transition attendue | Source |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | P | Karcham E(6,0), porter | P Sobre, E adjacent et portable | 1 PA → **11 PA, 6 PM** | E porté ; effet confirmé avant jet | [Karcham](https://openddb.dev/spells/12787-karcham) |
| 2 | P | Karcham E porté → (4,0) | case libre, route de jet valide | 1 PA → **10 PA, 6 PM** | E déplacé en (4,0) sur confirmation | idem |
| 3 | P | Brassage E(4,0) | P Sobre, E à portée | 2 PA → **8 PA, 6 PM** | E reçoit effet de dommages subis s'il est confirmé | [Brassage](https://openddb.dev/spells/12816-brassage) |
| 4 | L | Portail A=(1,2) puis B=(4,2) | cases vides, portée/rang/LdV légaux | 1+1 PA → **10 PA, 6 PM** | ensemble de portails actifs ; route et ordre restant inconnus | [Portail](https://openddb.dev/spells/14574-portail) |
| 5 | F | Bouclier Féca sur P(5,0) | rang 2 et portée 5, LdV | 2 PA → **10 PA, 6 PM** | P protégé dès confirmation | [Bouclier Féca](https://openddb.dev/spells/12982-bouclier-feca) |
| 6 | I | Marcher (0,0)→(3,0) | chemin de 3 cases libre | 3 PM → **12 PA, 3 PM** | I à 1 PO d'E(4,0) | hypothèse fixture |
| 7 | I | Colère de Iop sur E(4,0) | E vivant, I à 1 PO, hors relance | 7 PA → **5 PA, 3 PM** | dégâts et début de cooldown si effet confirmé | [Colère](https://openddb.dev/spells/13124-colere-de-iop) |
| 8 | Tous, T2–T3 | Entretenir la survie/position (actions non imposées) | Combat continue | **À recalculer par personnage** | E peut bouger, portails expirer, sorts changer d'état | aucune rotation complète attestée |
| 9 | P, T4 | Si E inchangé : Brassage sur E | P Sobre, cible accessible | 2 PA → **10 PA, 6 PM** sous fixture 12/6 | Effet nouveau sur E, ne pas supposer persistance T1 | [Brassage](https://openddb.dev/spells/12816-brassage) |
| 10 | L, T4 | Portails existants vérifiés, sinon réinstaller A/B | 2 portails requis | 0 ou 2 PA → **12 ou 10 PA, 6 PM** | Ne pas déduire qu'un sort Iop passe dans les portails | [Portail](https://openddb.dev/spells/14574-portail) |
| 11 | F, T4 | Bouclier sur P si disponible/nécessaire | portée et cooldown satisfaits | 2 PA si jeté → **10 PA, 6 PM** | P protégé, le Iop ne gagne aucun PA | [Bouclier Féca](https://openddb.dev/spells/12982-bouclier-feca) |
| 12 | I, T4 | Puissance sur I → Colère E | I a 12PA, à 1 PO, relance 3 tours revenue | 3+7 PA → **2 PA, 6 PM** sans marche | dégâts conditionnels ; meilleur usage si E encore marqué | [Puissance](https://openddb.dev/spells/13118-puissance), [Colère](https://openddb.dev/spells/13124-colere-de-iop) |

T4 est une **deuxième fixture conditionnelle** : le monde et les positions doivent être relus au début du tour. Le coût de replacer E en T4, s'il a bougé, n'est **pas** zéro ; rejouer Karcham (1+1) si les conditions le permettent puis recalculer. Les ressources de T2–T3 sont volontairement inconnues : les fixer serait inventer des actions.

## Contre-exemple négatif I → P → L → F

- Avant le jet, E=(6,0), P occupe (5,0) ; depuis I=(0,0), la case (5,0) adjacente est indisponible, et les deux cases voisines (6,1)/(6,-1) sont à **7 PM** sur la grille orthogonale. Avec I à 6 PM, **Colère à 1 PO illégale au T1**, malgré ses 7 PA théoriquement disponibles.
- I peut employer Puissance (3 PA) à titre préparatoire, **sur I uniquement**, et conserver 9 PA/6 PM avant tout déplacement ; il doit attendre un nouveau tour pour exploiter le jet de P. La préparation de Colère débute plus tard et son futur pic de dégâts aussi.
- Si l'action de P échoue parce qu'E est indéplaçable, que (4,0) est occupé ou que le jet est refusé, vérifier E(6,0) et abandonner l'attaque I planifiée. Si statut `Unknown`, ne pas faire comme si la case (4,0) était confirmée.
- Si L a posé un portail mais qu'un ennemi l'emprunte après le premier tour, recalculer danger et routes : [Portail](https://openddb.dev/spells/14574-portail) signale une restriction limitée au premier tour ; ne pas présumer que les portails soient sûrs ensuite.

## Gain, risque et hypothèses

Le gain principal **prouvable dans cette fixture si les transitions réussissent** est la *légalité de Colère T1*, soit un lancement possible de 7 PA après 2 PA de portage/jet (et 2 PA de Brassage optionnel). Pas de montant de dégâts chiffré : résistances, jets, soins/écrans, conditions d'invulnérabilité, durée et ordre de cumul des effets inconnus. L'Élio crée une option de ligne de tir ultérieure, le Féca une chance de survie du Pandawa, mais ni les portails ni le Bouclier ne sont automatiquement convertis en dommage du Iop. Tests ci-dessous, tous `propose, non execute`.

## Tests discriminants

| Cas | Entrée/variation | Actions | Attendu observable | Règle | Statut |
| --- | --- | --- | --- | --- | --- |
| M08-T+ | Fixture P→L→F→I, T1 | Karcham porter/jeter→Brassage→Portails→Bouclier→I marche+Colère | E reçu en (4,0), I à 1 PO, Colère légale, P 8PA/6PM ; L 10PA/6PM ; F 10PA/6PM ; I 5PA/3PM si effets confirmés | R01–R04 | proposé, non exécuté |
| M08-T-order | Même fixture, I joue **avant** P | I tente Colère au T1 | Aucun emplacement accessible au contact à 6 PM à cause du bloqueur (5,0) ; Colère impossible, pas de cast retroactif | R05 | proposé, non exécuté |
| M08-T-block | La case (4,0) est occupée ; P même état | Karcham porter puis jeter | Refus/alternative de destination ; **pas d'E fantôme à (4,0)** et I ne doit pas enchaîner en se fiant au plan | R01 | proposé, non exécuté |
| M08-T-turned | Brassage lancé en état Saoul sur E | P tente vulnérabilité | L'effet attendu de vulnérabilité Sobre **ne doit pas** être appliqué aveuglément ; effet Saoul différent | R01–R02 | proposé, non exécuté |
| M08-T-cd | I n'a pas lancé Colère T1 ; il tente une « Colère chargée » T4 | T1 refus puis T4 | Interdire le bonus basé sur un premier lancer fictif ; appliquer cooldown selon source valide | R04 | proposé, non exécuté |
| M08-T-portal | Un portail fermé ou obstacle de routage | Elio veut projeter une attaque | Aucun transfert/dégâts de portail présumé sans sortie et LoS certifiées | R02 | proposé, non exécuté |
| M08-T-protect | F agit après le tour de dégâts ennemi | Bouclier après dégâts | La mitigation ne peut pas modifier rétroactivement les dégâts passés | R03 | proposé, non exécuté |

## Passage développeur et reprise

Lire la source de vérité des positions, du jet, de l'état Sobre, des cooldowns, de l'initiative et des portails. Émettre des événements distincts : `EntityCarried`, `EntityThrown`, `PositionConfirmed`, `VulnerabilityApplied`, `PortalPlaced`, `ShieldApplied`, `SpellCastResolved`. Ces noms sont **propositions d'interface, pas une API existante**. Attendre le résultat du serveur et garder `Unknown` si la suite est non certifiée. Prochaine preuve : visionner [V08-A à 04:48](https://youtu.be/YFlyjdSo4Ek?t=288) et [06:19–07:18](https://youtu.be/YFlyjdSo4Ek?t=379) puis capturer plateau et séquence d'initiative ; comparer les fiches avec données de client 3.7.13.17. Vidéo V08-B non transcrite, donc aucune affirmation mécanique issue de ses paroles.
