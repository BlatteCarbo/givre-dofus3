# M10 — Fiche : poussée, obstacle et résistance sur Forgelance 3.7

## Identité
- Identifiant stable : **M10-POUSSEE-COLLISION-FORGELANCE-v1**.
- Sujet : dégâts directs Air et dégâts de collision d'une poussée, distance disponible, obstacle fixe/occupé, résistance poussée de la cible, effet de réduction de résistances.
- Contexte : exemple principal Forgelance avec Dégagement (niveau 175) ; référence source 3.7 extraite de [OpenDofusDB](https://openddb.dev/spells/23743-degagement), fiche [Grimoire 3.7](https://dofus-grimoire.fr/classes/forgelance/). N° exact client de V1/V2 et parité projet inconnus ; dernière référence du kit M02 : 3.7.13.17 à vérifier sur client/serveur.
- V1 : [INFI, Forgelance 700 do pou DOFUS 3.7](https://www.youtube.com/watch?v=_YXtVLQnCU8), 2026-11-01, ~39 min, FR auto, 1 412 extraits horodatés. Fenêtres lues : 01:05–02:35, 03:40–04:15, 16:50–17:45, 22:20–23:45, 37:50–39:00.
- V2 : [INFI, 600+ do pou DOFUS 3.7](https://www.youtube.com/watch?v=LKtiaIGNZt0), 2026-11-03, ~40 min, FR auto, 1 206 extraits. Fenêtres lues : 01:20–03:35, 09:45–10:35, 38:40–39:25.
- Méthode : get_video_info, get_available_languages, get_timed_transcript en français, `next_cursor=null` pour les deux. Vérification orale par transcription uniquement ; aucune image de plateau analysée, aucune valeur ou géométrie présentée comme observée vidéo.

## Règles atomiques et preuves
| ID | Affirmation | Statut de preuve | URL / intervalle | Limite |
| --- | --- | --- | --- | --- |
| M10-R01 | Dégagement a une ligne de dommages Air, repousse de **4 cases depuis son centre** et coûte **4 PA** ; un lancer par tour, exige la Lance et la rappelle si Désarmé. | corrobore-source | [OpenDofusDB Dégagement](https://openddb.dev/spells/23743-degagement) ; [Grimoire](https://dofus-grimoire.fr/classes/forgelance/) | Pas de validation serveur ; zone réelle et ordre des sous-effets à tracer. |
| M10-R02 | Le mur est considéré favorable au build de poussée ; une situation sans mur ne donne pas le même potentiel. | declare-video | [V1 02:00–02:25](https://www.youtube.com/watch?v=_YXtVLQnCU8&t=120s) ; [V1 17:13–17:39](https://www.youtube.com/watch?v=_YXtVLQnCU8&t=1033s) | « Plus de dégâts » n'est pas une formule ; de possibles dommages directs subsistent sans mur. |
| M10-R03 | Une cible adverse est décrite à **160 résistance poussée** et le joueur adapte sa décision. | declare-video | [V1 03:55–04:03](https://www.youtube.com/watch?v=_YXtVLQnCU8&t=235s) | Chiffre d'une cible de la vidéo, pas une règle générale ni preuve écran. |
| M10-R04 | Pluie d'Airain inflige des dommages Air et applique **-40 résistances poussée pendant 2 tours**, coût **2 PA**. | corrobore-source | [OpenDofusDB Pluie d'Airain](https://openddb.dev/spells/24219-pluie-d-airain) | L'ordre entre Air et application de malus est inconnu pour la même action ; tester **une poussée ultérieure**. |
| M10-R05 | Charge Héroïque est conditionnelle : elle repousse la cible **si le lanceur termine au contact**, et son déplacement dépend de la distance. | corrobore-source | [OpenDofusDB Charge Héroïque](https://openddb.dev/spells/23824-charge-heroique) | Distingue « rapprochement » du joueur de « poussée » de l'ennemi ; sort secondaire pour test négatif. |
| M10-R06 | La version 3.7 a modifié des centaines de résistances poussée de monstres ; des exceptions à résistance très élevée ou impossibles à déplacer subsistent. | corrobore-source | [Dofus pour les Noobs, MAJ 3.7 partie V](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) | Liste précise par monsterId et valeurs réelles non capturée. |
| M10-R07 | Une ancienne formule combine niveau, dommages poussée, résistances poussée et distance non parcourue. | hypothese/obsolete-pour-cible | [Raceko, non daté](https://raceko.wifeo.com/formulaire.php) | Non vérifiée en 3.7 ; pas d'implémentation ni de nombres oracle. |
| M10-R08 | Un équipement 3.7 peut procurer une résistance poussée variable de **31 à 40**. | corrobore-source | [Back from AFK, Bottes du Cul Botté depuis 3.6](https://www.backfromafk.com/objets/depuis-3-6/) | Montant propre à l'objet, avant autres effets et états ; exemple de variation de fixture. |

## État initial reproductible : géométrie abstraite
On choisit **Dégagement** parce qu'il fournit un seul sort de poussée de quatre cases avec dégâts Air documentés. Les coordonnées (x,y) sont des positions relatives sur une rangée **dans les cases du moteur validées comme alignées** ; elles ne sont ni des `cellId` DOFUS ni une preuve de déplacement sur une carte officielle.

- (L=(0,0)) : **Lance du Forgelance**, centre de poussée ; (E=(1,0)) : cible ennemie vivante ; (R) = Forgelance allié, position distincte légale, en état Désarmé avec la Lance active, Dégagement appris et disponible.
- Trois variantes de collision depuis E avec poussée vers (+x) : **A** obstacle fixe à (2,0) ⇒ 0 case libre derrière E ; **B** obstacle fixe à (4,0) ⇒ deux cases disponibles (2,0) et (3,0) ; **C** pas d'obstacle sur les cases 2 à 5 ⇒ quatre cases disponibles. On **propose** respectivement 4 / 2 / 0 cases de poussée non réalisées, à vérifier dans le moteur et le serveur avant de les utiliser pour le chiffrage.
- Contrôle des confusions : même rang du sort, mêmes caractéristiques, résistances Air/Poussée de E, même PV, même niveau, même bonus de dommages poussée de R, même crit/jet (ou plusieurs mesures agrégées), même orientation et conditions de lancer ; pas d'états Noa, renvoi, bouclier conditionnel, invulnérabilité, zone annexe ou challenge actif.
- Budget de l'action Dégagement : **4 PA** ; si précédé de Pluie d'Airain, budget **6 PA** et l'état de Lance doit être compatible avec les deux lancers. La variante de rotation complète 12 PA de Grimoire est informative, mais ce test isole deux actions.
- La direction effective « depuis la Lance », la collision avec entités et la légalité exacte de (x,y) doivent être confirmées via données géométriques serveur et non par simple dessin.

## Séquence et transitions
| Étape | Acteur | Action/cible | Précondition | Coût | Effets immédiats attendus | Effets différés / ordre | Preuve |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | Système | Choisir A, B ou C | L et E vivantes, obstacle connu, état de combat valide | — | Calculer rayon de poussée depuis L, cellules traversables et première cellule bloquante | Les règles de traversée/entité doivent rester versionnées | M10-R01/R02 |
| 1 | R | Dégagement sur L | Lance existante, R Désarmé, sort appris/disponible, PA, cible/LoS légales | 4 PA | Ligne de dommages Air sur E (29–32 de base selon fiche), repousse demandée de 4 depuis L ; la Lance revient si applicable | Enregistrer séparément dégâts Air, déplacement confirmé et dommage de collision ; leur ordre doit être observé | M10-R01 |
| 2 | Système | Résoudre position E | E vivante, pas Indéplaçable ou comportement particulier | aucun | A : E reste en (1,0) ; B : E atteint (3,0) ; C : E atteint (5,0), **sous hypothèse topologique** | A/B : collision à quantifier ; C : pas de collision de l'obstacle testé. Recalculer morts/obstacles après chaque dommage | M10-R02 |
| 3 (option) | R | Pluie d'Airain sur L avant Dégagement, dans une exécution indépendante | Lance, niveau ≥140, sort de variante disponible et 2 PA | 2 PA | Ligne Air de Pluie distincte, résistance Poussée de E −40 pendant 2 tours si effet accepté | Le Dégagement ultérieur est résolu avec la résistance réellement active ; ne pas ajouter arbitrairement +40 à un dommage | M10-R04 |
| 4 (option) | R | Charge Héroïque sur E | Sort 3 PA, lancer en ligne, cooldown, distance, position après rapprochement | 3 PA | R avance, dommage direct du meilleur élément, recul de E **seulement** si R termine au contact | Une absence de contact doit empêcher la branche poussée ; comparer contre un déplacement forcé automatique fictif | M10-R05 |

Récapitulatif **à vérifier** : `requestedPush=4`, `actualMoved` = A:0, B:2, C:4, `remainingPush` = A:4, B:2, C:0. Ces nombres sont des *scénarios géométriques discriminants*, pas des dégâts. Ne pas remplir `collisionDamage` depuis la formule ancienne.

## Intérêt tactique et limites
L'action de Dégagement peut rendre la cible plus loin tout en frappant Air. Un mur raccourcit la distance déplacée, ce qui favorise le volet **collision**, mais la cible finit potentiellement plus proche du lanceur : le choix dépend de la menace, de la case finale, du coût de placement et des PA restants. Pour comparer aux dégâts directs, conserver l'action alternative sélectionnée par le planificateur et ses propres coûts/conditions ; calculer les dommages attendus **seulement** avec un oracle serveur étalonné. Les statistiques PvP du build ne donnent aucune moyenne PvM. Les nouveaux malus de résistance poussée de 3.7 rendent le choix dépendant de monsterId/états, parfois avantageux, parfois inopérant.

**Lacunes critiques** : formule exacte serveur 3.7 (niveau, distance non réalisée, dommage de poussée, résistance, arrondis, bornes), collision avec une autre entité et dégâts collatéraux, interactions avec bord de carte, obstacle destructible, Indéplaçable, glyphes, déclencheurs de poussée, ordre Air/poussée et séquence kill au milieu, effets protecteurs/boucliers, date d'expiration du -40.

## Scénarios de test proposés — tous NON EXÉCUTÉS
| Cas | Entrées contrôlées | Actions | Signal observable / distinction | Statut |
| --- | --- | --- | --- | --- |
| P1 obstacle adjacent | Fixture A, L=(0,0), E=(1,0), mur=(2,0), 4PA, 0 malus de résistance | Dégagement sur L | E ne traverse pas le mur ; log conserve ligne Air et événement collision si autorisé ; relève damage réel. Distingue `actualMoved=0` et `remainingPush=4`. | proposé, non exécuté |
| P2 obstacle partiel | Fixture B, mur=(4,0), mêmes stats/crit/jet | Dégagement sur L | E termine sur (3,0), arrêt avant mur ; résultat collision à comparer à P1 en gardant Air identique. Distingue distance perdue 2 vs 4. | proposé, non exécuté |
| N1 voie libre | Fixture C, aucun obstacle jusqu'à (5,0), mêmes stats | Dégagement sur L | E se déplace de quatre cases, aucune collision sur obstacle ; *une ligne Air peut subsister*. Distingue dégâts Air du supplément collision. | proposé, non exécuté |
| P3 contrôle résistance | Deux fixtures A identiques, E résistance Poussée 0 vs 160, Air identique ; puis E à 160 avec Pluie -40 confirmée | Dégagement, ou Pluie puis Dégagement en réinitialisant PV avant mesure du second sort | Le log de **Dégagement** doit être attribuable à une résistance poussée distincte, à résistance Air constante ; séparer dégâts Air de Pluie. Hypothèse directionnelle : plus de résistance devrait réduire/limiter le supplément collision, le moteur doit vérifier arrondis et bornes. | proposé, non exécuté |
| N2 Indéplaçable | Même fixture A, E sous état Indéplaçable authentifié | Dégagement | Séparer les dommages Air des événements de déplacement refusés ; **le dommage collision sous Indéplaçable reste inconnu** jusqu'au serveur. Distingue état et obstacle. | proposé, non exécuté |
| N3 obstacle vivant | Remplacer mur=(2,0) par allié ou ennemi vivant, identité et équipe connues | Dégagement sur L | Vérifier les deux victimes, dégâts directs ou collatéraux et l'ordre ; ne jamais supposer l'effet du mur fixe sur une entité vivante. | proposé, non exécuté |
| N4 condition Charge Héroïque | Même cible/obstacle mais R ne termine pas au contact à cause d'un blocage | Charge Héroïque | Dommages directs éventuels et rapprochement ; **pas de poussée conditionnelle** si absence de contact. | proposé, non exécuté |

## Passage développeur et reprise
Voir [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md). En priorité : snapshots/événements serveur avec identité des entités, obstacles dynamiques, deux types de résistances, source des effets et dégâts séparés ; mesures de A/B/C puis malus -40. Captures des passages V1 02:00–02:25 et 17:13–17:39 à vérifier visuellement si une inspection des pixels est disponible. Aucune valeur `valide-test` ou `valide-live` attribuée.

