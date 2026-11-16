# M23-F01 — Pandawa : porté/jetté certifié avant zone alliée

## Identité et provenance

- **M23-F01**, mécanisme : **Karcham (porter et jeter) + Brassage Sobre** avec préparation de trois cibles pour la zone alliée du Forgelance. Date d'analyse : **04/11/2026**, documentaire.
- **Cible** Dofus PC **3.7.13.17** (référence projet rapportée dans le kit) ; **source des règles** : [Dofus Grimoire 3.7.12 Pandawa](https://dofus-grimoire.fr/classes/pandawa/) et [OpenDofusDB Karcham](https://openddb.dev/spells/12787-karcham), [Brassage](https://openddb.dev/spells/12816-brassage) ; compatibilité client non certifiée.
- **Vidéos individuelles réelles**, obtenues par MCP `get_video_info` / `get_available_languages` : SAPEUH, [*PANDAWA SUR DOFUS 3 !* 18/01/2025, ~15 min](https://www.youtube.com/watch?v=I59ZNptd3P8) et [*LE MODE PARFAIT...* 31/07/2025, ~18 min](https://www.youtube.com/watch?v=dnD4p3lqwvk), **FR auto**, transcriptions inaccessibles (**blocage YouTube par IP**), pas d'intervalle examiné.
- **Vidéo pertinente d'équipe transcrite** : XAWPIYA, [*Les meilleures teams de 4 en 2026*](https://www.youtube.com/watch?v=VCVjlE8pVcI), 08/06/2026, ~9 min, **294 fragments FR auto `next_cursor=null`** ; [01:06–01:28](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=66s) rôle du Pandawa pour regrouper, [03:36–03:48](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=216s) Forgelance associé au contrôle de zone. `declare-video`, **aucune image observée**.
- **Comparaison ancienne** : Tenmalexis, [*Meilleure team de 4 en 2025*](https://www.youtube.com/watch?v=YFlyjdSo4Ek), 02/05/2025, ~16 min, **3.1 annoncée**, transcription FR **225 segments** ; [04:48–05:10](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=288s) présentation Brassage/placeur. Les nombres de dégâts/PA de cette version ne sont pas utilisés.

## Règles traçables

| ID | Affirmation | Statut documentaire | Source et nuance |
| --- | --- | --- | --- |
| **M23-R01a** | **Karcham** sur entité adjacente en état **Sobre** : **1 PA** portage, puis **1 PA** jet vers case libre, portée depuis cellule *actuelle* du Pandawa. | `corrobore-source` 3.7.12 ; conformité 3.7.13.17 `hypothese` | [Grimoire Pandawa](https://dofus-grimoire.fr/classes/pandawa/), [Karcham](https://openddb.dev/spells/12787-karcham). Guide indique **jusqu'à 6 PO en ligne** après portage pour rang haut ; exact compteur par cible à contrôler. |
| **M23-R01b** | **Chamrak** est **variante de Karcham** : pas deux portages disponibles simultanément ; change les conditions de visée de phase jet. | `corrobore-source` | [OpenDofusDB, classe Pandawa](https://openddb.dev/classes/12-pandawa), [Chamrak](https://openddb.dev/spells/12810-chamrak). |
| **M23-R02** | **Brassage** sur cible en état Sobre : coût **2 PA**, jusqu'à **6 PO** et dommages subis indiqués **×110 %** ; en état Saoul : **Pesanteur** et état Brassage (3 tours), **pas** la même vulnérabilité. | `corrobore-source`, durée effective de vulnérabilité inconnue | [Brassage OpenDofusDB](https://openddb.dev/spells/12816-brassage), [historique 3.7](https://backfromafk.com/classes/pandawa/depuis-3-4/). Brassage est variante de Souillure. |
| **M23-R03** | Arrivée de E1 au centre d'une zone alliée : le planificateur doit utiliser l'état **après** un jet **confirmé**, pas la cellule anticipée ni l'ordre d'une vidéo accélérée. | `hypothese` de modèle ; à tester | Fixture détaillée, [XAWPIYA 2026 à 01:14](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=74s) illustre le rôle, non les cellules. |
| **M23-R04** | Saoul bloque le portage Karcham ; mobilité/mitigation et autres sorts sont modifiés par l'état. | `corrobore-source` guide 3.7.12 | [Grimoire Pandawa, Sobre/Saoul](https://dofus-grimoire.fr/classes/pandawa/). État exact à confirmer côté serveur. |
| **M23-R05** | **Varappe** : +2 lancers/tour Karcham ou Chamrak, +1/cible, pas +4 comme avant, relance 3 tours. | `corrobore-source` 3.7 | [Varappe OpenDofusDB](https://openddb.dev/spells/12813-varappe), [diff 3.4→3.7](https://backfromafk.com/classes/pandawa/depuis-3-4/). Non nécessaire au scénario de base. |

## État initial du test

**Fixture construite pour le moteur — aucune case issue d'une vidéo.** Géométrie orthogonale sans obstacle sauf exceptions annoncées. Les 4-voisins sont employés pour mesurer les **3 PM de marche**, et non comme calcul universel de portée des sorts.

```text
P Pandawa    (0,0)   12 PA/6 PM, Sobre, Karcham rang 3, Brassage équipé
E1 adversaire(1,0)   vivant et portable
E2 adversaire(5,1)   immobile le temps de la fixture
E3 adversaire(4,2)   idem
F Forgelance (2,3)   allié : 12 PA/6 PM indépendants
R réception  (5,0)   LIBRE ; L ancrage de future Lance (4,1) LIBRE

Chemin Pandawa après avoir porté E1 :
(0,0) → (1,0) → (2,0) → (3,0) : 3 PM
                         ↘ jeter E1 en ligne sur R=(5,0), distance 2.
Carré 3×3 de centre L=(4,1) : E1 à R, E2=(5,1), E3=(4,2).
```

Hypothèses nécessaires : E1 peut être porté ; sortie du portage n'occupe pas la cellule d'arrivée de P ; aucun tacle/état empêchant mouvement ; capacité de lancer en ligne et LdV légales ; rang/variante/limites compatibles ; aucun challenge incompatible ; buffs et cooldowns disponibles ; combat et phase d'initiative identifiés. Ne pas inventer un `cellId` Dofus ni des PV.

## Tour unique P → F (suite F détaillée M24)

| N° | Acteur | Action et cible | Précondition | Coût PA/PM **propre** | État produit à certifier |
| --- | --- | --- | --- | --- | --- |
| 1 | **P** | Karcham **porter** E1(1,0) | P Sobre ; E1 adjacent, portable, vivant | 1 PA/0PM → **11PA/6PM** | E1 porté ; P lié à E1 ; E1(1,0) vacante |
| 2 | **P** | Marche (0,0)→(3,0) **en portant** | libre après P1, chemin disponible, pas de tacle | 0PA/3PM → **11PA/3PM** | P=(3,0), E1 toujours porté |
| 3 | **P** | Karcham **jeter** E1 sur R=(5,0) | R libre, 2 PO en ligne, état Porteur actif, jet prêt | 1PA/0PM → **10PA/3PM** | E1=(5,0), E1 non porté |
| 4 | **P** | Brassage **Sobre** sur E1 | E1 toujours cible à 2 PO ; 2PA, effet visé sur E1 | 2PA/0PM → **8PA/3PM** | E1 reçoit `damageTakenModifier` si confirmé ; E2/E3 inchangés |
| 5 | **F** | Fer Rouge → Effondrement → Pluie d'Airain → Ydra | F joue **après P**, ancrage L libre | **12 PA/0 PM** à F, indépendamment de P | Réévaluer la **zone sur E1/E2/E3** selon positions confirmées |

**Bénéficiaire réel :** le jet permet au **F** et autres frappeurs **après P** de couvrir E1 en plus de E2/E3. Brassage vise **E1**, non F, et ne vaut qu'en cas d'effet actif pendant l'attaque. Ce tour P consomme **4 PA et 3 PM**, conserve **8 PA/3 PM** ; les **12 PA de F** ne proviennent pas de P.

## Scénarios négatifs vérifiables

| ID | Modification précise | Étapes tentées | Résultat discriminant, non exécuté |
| --- | --- | --- | --- |
| **M23-T+** | Fixture intacte, ordre **P → F**, L/R libres | 1 à 5 | Après **état serveur** : E1=(5,0), P=(3,0), 8PA/3PM P ; E1 éligible au carré F. Le multiplicateur de Brassage est conditionnel à son état actif. |
| **M23-T-occupied** | R=(5,0) **occupée** par un obstacle/entité | Porter, marcher, jeter sur R | **Aucun** état confirmé E1=(5,0). Rejeter/replanifier destination et gérer `carried` / `unknown` selon résultat réel ; F ne compte pas E1. |
| **M23-T-order** | Même fixture mais **F → P** | F lance zones, P place E1 plus tard | F ne touche pas E1 à (1,0) par sa zone L ; **aucun dégât rétroactif** après le jet. |
| **M23-T-saoul** | P commence **Saoul** et aucun changement d'état confirmé | Tente Karcham et Brassage | Portage illégal ; ne pas attribuer vulnérabilité Sobre à E1. Brassage Saoul a effet différent, à étudier sans le confondre. |
| **M23-T-no-target** | E1 meurt ou devient indisponible entre P1 et P3 | Tentative de jet | Ne pas déposer une entité fantôme ; état portage/cellules vérifiés sur source autoritative. |
| **M23-T-collision** | R libre mais chemin P traversé par entité/état de tacle | P2/P3 | Ne pas certifier P(3,0), et ne pas jeter depuis la cellule initialement prévue si P2 échoue. |

Chaque test est **proposé, non exécuté**. La formule de dommages de collision, résistance de poussée, ordre des dégâts/jet pour Pandikulation, et interactions exactes invocations/challenges restent `unknown`.

## Tailles d'équipe

- **Solo P** : regrouper E1 pour une zone Pandawa **si sort et PA légaux** ; ne pas imposer la séquence du Forgelance sans F.
- **Duo P → F** : zone F augmente d'une cible potentielle, mais danger d'avoir E1 près de P ; si F joue avant, choisir une attaque préexistante ou conserver un setup pour cycle suivant.
- **Trio P → Énu → F** : Énu tente retrait PM sur E déplacé, ses PA/PM et la réussite restent **unknown** ; F lit le résultat et les zones.
- **Quatuor P → Féca → Énu → F** : protection cible l'allié choisi et doit précéder les attaques ennemies ; pas de PA communs, P ne produit que du placement et une marque E1.
- **5+** : inclure obstacles des invocations et compagnons, challenges de chaque acteur, conflits de destination et ordre d'initiative de tous les placeurs ; pas de macro non conditionnelle.

## Passage au développeur et reprise

Variables : `actorId`, `turnOrder`, `PA/PM` par acteur, `pandaStance`, `variant/rank`, `castCounterPerTarget`, `carriedEntityId`, `carriedById`, `targetCell`, `freeCell`, `LoS`, `lineCast`, `movementPath`, `tackle`, `targetStates`, `vulnerabilityRecipient`, `serverResolutionStatus`, `fightEpoch`. **Noms illustratifs, aucune API confirmée**. Refus de P3 ou `Unknown` bloque le plan F dépendant de E1 à R. Vérification future : export client 3.7.13.17 et replay de portage/jet avec occupation de R ; transcript individuel après déblocage, capture visuelle réelle. Statuts `valide-test` / `valide-live` absents.
