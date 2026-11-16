# Fiche M12 — Zobal : état des masques, Plastron et invocation alliée

## Identité

- **ID stable : ZOB-M12-MASQUES-PLASTRON-20261104** ; date de l'analyse : 04/11/2026.
- **Mécanique :** masque porté par le Zobal et son effet sur l'accès aux sorts ; buffs des alliés proches ; zone d'effet de **Plastron**, réduction du bouclier sur **invocation**, vie/dégâts du Masque Grimaçant. Compositions solo, duo, trio, quatuor et invocations/compagnons.
- **Cible :** DOFUS Unity **3.7** ; fiche de sorts communautaire [Dofus Grimoire MAJ 3.7.12](https://dofus-grimoire.fr/classes/zobal/), dernière référence client du kit **3.7.13.17** non directement relue. La précision 3.7.13.17, les flux de combat et le serveur sont **unknown**.
- **Vidéos candidates vérifiées :** [Skyzio 3.2](https://www.youtube.com/watch?v=mR47IeK3JGQ) 10/08/2025 39 min ; [Capitemflam 3.0](https://www.youtube.com/watch?v=jEu26stOu30) 14/04/2025 54 min ; [Tarkan](https://www.youtube.com/watch?v=_UvkN3XYFFs) 30/05/2026 36 min, patch précis non indiqué.
- **Langues et intervalles :** MCP get_available_languages = « français (auto-generated) » sur les trois. **Aucune transcription réellement lue** : get_timed_transcript(lang=fr) bloqué par IP/YouTube ; navigateur V1 affiche « loading » et récupère HTTP 429 sur les sous-titres. Seuls chapitres **de description** consultables : [V2 2:42](https://www.youtube.com/watch?v=jEu26stOu30&t=162s), [V2 11:39](https://www.youtube.com/watch?v=jEu26stOu30&t=699s), [V3 0:50](https://www.youtube.com/watch?v=_UvkN3XYFFs&t=50s). **Aucune observation d'image de plateau**.
- **Autres sources :** [OpenDofusDB Zobal](https://openddb.dev/classes/14-zobal) et pages exactes citées ci-dessous ; [comparaison dofusdude 3.2→3.7](https://www.backfromafk.com/classes/zobal/depuis-3-2/). Les sources sont communautaires, à confronter à la version du client cible.

## Registre atomique avec provenance

| ID de RAPPORT | Règle établie pour les fiches consultées | Source précise | Statut |
| --- | --- | --- | --- |
| ZOB-M12-01 / 02 | Une famille de masque active au maximum, changement avec **1 PA** et une **relance 2 tours** sur chaque masque de base ; sous-ensemble des sorts dépendant de la famille. | [C1 §« Les masques »/« À savoir »](https://dofus-grimoire.fr/classes/zobal/), [C2 classe](https://openddb.dev/classes/14-zobal) | **corrobore-source**. |
| ZOB-M12-03 | Intrépide : **+1 PA** sur Zobal porteur et bonus sur alliés proches **hors Zobals** en **croix de 1 case**. | [C2 Intrépide](https://openddb.dev/spells/13386-masque-de-l-intrepide), [C1](https://dofus-grimoire.fr/classes/zobal/) | **corrobore-source** ; durée allié C2 premier rang **1 tour**, C1 haut rang **2 tours** ; caler selon rang. |
| ZOB-M12-04 | Pleutre rang 3 : **+1 PM, +40 Fuite** au porteur et alliés proches éligibles. | [C1 §Pleutre](https://dofus-grimoire.fr/classes/zobal/), [C2](https://openddb.dev/spells/13387-masque-du-pleutre) | **corrobore-source** ; effets des rangs 1/2 à charger explicitement. |
| ZOB-M12-05 | Psychopathe rang 3 : **+10 % dommages de mêlée, +20 Tacle**, alliés **hors Zobals**. | [C1 §Psychopathe](https://dofus-grimoire.fr/classes/zobal/), [C2](https://openddb.dev/spells/13388-masque-du-psychopathe) | **corrobore-source**. |
| ZOB-M12-06 | Plastron est associé à l'état **Intrépide**, **4 PA**, lancé **sur soi** sans LoS ; **relance 3 tours**, bouclier indiqué **2 tours**. | [C1 §Plastron / masques](https://dofus-grimoire.fr/classes/zobal/), [C2](https://openddb.dev/spells/13397-plastron) | **corrobore-source**, application serveur non testée. |
| ZOB-M12-07 | La zone de Plastron est un **cercle de 3** autour du lanceur : alliés éligibles, y compris invocations ; invocation reçoit **la moitié** du bouclier d'un personnage. | [C1 §Protéger le groupe / Plastron](https://dofus-grimoire.fr/classes/zobal/), [C2 Plastron](https://openddb.dev/spells/13397-plastron) | **corrobore-source** ; frontière de cases / filtres exacts à valider. |
| ZOB-M12-08 | Rang 3 de Plastron **normal** : personnage **600 % du niveau du Zobal**, invocation **300 %** ; **critique** : **720 % / 360 %**. Rangs 1/2 détaillés dans RAPPORT.md avec lien C1. | [C1 §Plastron](https://dofus-grimoire.fr/classes/zobal/) | **corrobore-source** ; arrondi, cumul, absorption inconnus. |
| ZOB-M12-09 | Plastron et Ginga sont des variantes mutuellement exclusives ; familles de masques elles-mêmes en variantes de sorts. | [C2 classe](https://openddb.dev/classes/14-zobal) | **corrobore-source** ; connaître le set exact des sorts équipés. |
| ZOB-M12-10 | Grimace invoque un Masque Grimaçant **maîtrisable**, distinct, qui **partage les dégâts qu'il subit** avec son invocateur pendant sa vie. | [C2 Grimace](https://openddb.dev/spells/13424-grimace), [C3 delta](https://www.backfromafk.com/classes/zobal/depuis-3-2/) | **corrobore-source** pour description ; ordre du partage après Plastron **hypothèse** à tester. |
| ZOB-M12-11 / 12 | Bonus de masque sur alliés **hors Zobals** ; Plastron décrit « alliés » sans exclusion de Zobal. La réception d'un **buff de masque par invocation** n'est pas prouvée par la réduction de bouclier Plastron. | [C1](https://dofus-grimoire.fr/classes/zobal/), [C2 Intrépide](https://openddb.dev/spells/13386-masque-de-l-intrepide), [C2 Plastron](https://openddb.dev/spells/13397-plastron) | **corrobore-source** pour textes ; **hypothèse** pour les filtres en situation. |

**Statuts non attribués :** declare-video (aucun extrait de parole), observe-video (aucune capture de plateau), valide-test et valide-live (aucune exécution).

## État initial reproductible (scénario d'oracle proposé)

- **Z**, un Zobal allié avec une configuration de sorts **Plastron équipé et Ginga non équipé** (variante exclusive, source [C2 classe](https://openddb.dev/classes/14-zobal)). La fiche **Plastron rang 3** n'est disponible qu'à partir du **niveau 144** selon [C1 §Plastron](https://dofus-grimoire.fr/classes/zobal/) ; ne pas employer le rang 3 si le personnage ne l'a pas.
- **Masque initial :** Z porte l'état **Intrépide** confirmé au début du scénario. Cela évite d'estimer une économie fictive de PA lors d'un changement de masque. Réservoir de PA de Z **au moins 4** au moment où il lance Plastron. Si masque à lancer d'abord, calculer les coûts et les gains dans l'ordre et sur le bon acteur.
- **Entités additionnelles** : **A**, un personnage allié non Zobal ; **S**, une invocation appartenant à A ; **G**, le Masque Grimaçant de Z si Grimace est équipé à la place de Diffraction ; **Z2**, un autre Zobal allié dans le test quatuor ; **E**, un ennemi témoin.
- **Positions relatives** : Z au centre de la zone ; A et S **dans le cercle de 3** (à mesurer selon la fonction d'aire de jeu) ; E à l'extérieur. Cas négatif : S **hors cercle**, A **dedans**, puis comparaison avec S dedans/A dehors. Les identifiants de cellule ne sont pas fournis par les sources : les lire dans la future fixture du client ; ne pas inventer de carte.
- **Données de combat** : portées/LoS de la fiche de sort, résistance et PV initiaux enregistrés, boucliers préexistants **inconnus ou absents explicitement dans une fixture contrôlée**, distinction critique/non critique, rang, buffs temporaires, cooldown Plastron, source des effets.
- **Ordre d'initiative** : consigner l'ordre Z/A/S/E et vérifier le moment auquel une protection doit exister ; aucune action d'A ou S ne doit consommer les PA de Z.

## Séquence opérationnelle et transitions

| Phase | Acteur | Action et cible | Préconditions | Coût | Effet / état attendu | Déclencheur ultérieur et preuve |
| --- | --- | --- | --- | --- | --- | --- |
| T0 | Observation | Lire état complet de Z, A, S, cellule, masques, buffs, Plastron équipé | Aucun acteur en cours de disparition, snapshot récent et identifié | 0 | Ensemble de cibles classées personnage/invocation, équipes, propriétaires, position et niveau Z connus | Snapshot/effets reçus du serveur : encore **à obtenir** |
| T1a, optionnelle | Z | **Masque de l'Intrépide** sur soi | État Intrépide absent, sort équipé, relance disponible, PA suffisants | **1 PA** fiche C1/C2 | État Intrépide, buff +PA porteur, bonus alliés dans la croix si éligibles ; **ne pas précréditer** le bonus avant sa confirmation | [C2 Intrépide](https://openddb.dev/spells/13386-masque-de-l-intrepide), [C1](https://dofus-grimoire.fr/classes/zobal/) |
| T1b, alternative | Z | Rester sous Intrépide déjà confirmé | Masque actif et Plastron équipé | 0 | Plastron apparaît comme action candidate | [C1 masques](https://dofus-grimoire.fr/classes/zobal/) |
| T2 | Z | **Plastron** centré sur soi | Intrépide actif, **4 PA**, sort disponible, état de groupe actualisé | **4 PA** fiche C1/C2 | À réception de l'effet réel : bouclier sur chaque allié éligible du cercle ; chaque invocation bénéficiaire reçoit le montant réduit de moitié ; durée décrite **2 tours** | [C1 §Plastron](https://dofus-grimoire.fr/classes/zobal/), [C2](https://openddb.dev/spells/13397-plastron). Cooldown décrit **3 tours**. |
| T3 | Z / moteur | Classer les bénéficiaires et enregistrer l'effet | T2 confirmé (et statut critique connu) | 0 | Pour rang 3, multiplicateur normal **600 % / 300 %**, critique **720 % / 360 %** du niveau de Z ; stocker l'effet par cible, provenance, échéance | [C1 §Plastron](https://dofus-grimoire.fr/classes/zobal/), arrondi **unknown** |
| T4 | E | Attaque contre S ou G | Cible encore vivante, ordre des tours connu, attaque et résistance fixées pour le test | Coût du sort ennemi : **inconnu** | Mesurer PV, bouclier résiduel de S/G, et si G : PV du Zobal ; **ne pas prédire de partage sans oracle** | [C2 Grimace](https://openddb.dev/spells/13424-grimace) |
| T5 | Tours | Avancer sur les événements de début/fin de tour | Flux serveur ordonné | 0 | Diminuer/retirer les effets selon durée et phase de tick réellement observées ; mettre à jour cooldown et vie de S/G | C1/C2 indiquent durée, **phase exacte du tick unknown** |
| T6 | Z | Changer vers Pleutre ou Psychopathe après Plastron | Sort de masque disponible, PA suffisants | **1 PA**, C1/C2 | Masque actif remplacé, sous-ensemble des sorts actualisé, bonus de Plastron chez les cibles **à vérifier quant à sa persistance** | ZOB-M12-01/02 ; test discriminant proposé |
| N1 | Z | Tenter Plastron alors que Pleutre actif et Intrépide inaccessible | Famille de masque incompatible | aucun prélèvement **si refus confirmé** | Refuser plan localement ; si commande envoyée et réponse indéterminée, statut **Unknown** | ZOB-M12-06 |
| N2 | Z | Tenter Plastron avec Ginga à la place | Ginga sélectionné comme variante | aucun prélèvement si non envoyé | Plastron non proposé | ZOB-M12-09 |
| N3 | Z | Tenter Plastron sur invocation morte / hors zone | S absent ou cellule S hors cercle | coût uniquement si action légale effectivement lancée | Ne pas attribuer de bouclier à S ; les autres alliés dans la zone peuvent en recevoir | ZOB-M12-07 ; test proposé |

Pour un **refus/timeout**, un accusé de transport n'atteste pas le bouclier. Garder l'état **Unknown/Refused** selon la réponse et ne jamais utiliser un bouclier prédit comme une protection **confirmée**. Réconcilier l'identité Z et de chaque invocation S (propriétaire, instance, session/époque), sans fusionner des invocations de même type.

## Protection : modèle d'entité et questions non résolues

- **Bénéficiaire joueur Z, A ou Z2** : montant normal « personnage » ; **S et G** : montant « invocation » si la classe de cible est effectivement admissible au sort. Un compagnon contrôlé n'est **pas** automatiquement une invocation au sens des effets : **unknown** et test distinct.
- **Masque actif sur Z** : une seule valeur de famille (Intrépide/Pleutre/Psychopathe) et une identité du sort réellement lancé ; **les effets sur alliés sont des buffs attribués à leur propriétaire**, pas une copie de l'état de masque du Zobal.
- **Bouclier** : valeur brute provenant de la fiche et du résultat critique, statut appliqué par cible, durée, cumul/écrasement avec protections précédentes et absorption. La règle d'arrondi d'un pourcentage sur niveau, le seuil à zéro, les résistances/érosion et l'ordre de partage de Grimace sont **unknown** et nécessitent un oracle.
- **Masque Grimaçant** : ce mot désigne une **entité invoquée** par Grimace ; il ne désigne pas l'état Intrépide/Pleutre/Psychopathe porté par le Zobal. Le partage de ses dommages peut faire du bouclier une protection indirecte du Zobal, mais sa quantité dépend de l'ordre réel et du type d'attaque.
- **Cumul et fin de tour** : impossible de conclure que deux Plastrons se cumulent ou qu'un effet dure jusqu'à un événement précis (début Z, fin S, début E) à partir d'une simple description « 2 tours ».

## Scénarios de test proposés

| ID et taille | État/action | Observable discriminant attendu ou inconnue précise | Statut |
| --- | --- | --- | --- |
| S01 **solo positif** | Z Intrépide, Plastron rang 3, Z seul | Bouclier sur Z au montant **600 % niveau Z** si coup normal, **720 %** si critique ; coût **4 PA**, relance 3 tours | proposé, non exécuté ; C1 |
| S02 **solo avec propre invocation** | Z + G (Grimace), tous deux dans la zone ; Plastron puis attaque de G | Z version personnage ; G **moitié** si éligible ; comparer dommages/absorption/transfert pour trouver l'ordre du partage | proposé, non exécuté ; C1/C2 Grimace |
| D01 **duo positif** | Z + Osamodas O, invocation S d'O, toutes les entités dans le cercle, Plastron | O montant personnage ; S montant invocation (moitié) si S éligible. Pas de privilège fondé sur « invocation du Zobal » | proposé, non exécuté ; C1/C2 |
| D02 **duo négatif** | O dedans, S hors rayon, Plastron | O bénéficiaire ; S **aucun effet** de ce cast ; inverser positions et vérifier S dedans/O dehors | proposé, non exécuté ; C1 |
| T01 **trio** | Z + O + Crâ C + S, C hors zone, S dedans ; bouclier puis déplacement | Seules cibles éligibles présentes au cast reçoivent l'effet ; vérifier que C entrant après ne gagne pas rétroactivement le bouclier | proposé, non exécuté ; partie entrée après **unknown** |
| Q01 **quatuor** | Z1 + Z2 Zobal + O (+ S) + Iop I ; tous dans le cercle ; lancer Intrépide puis Plastron | Bonus de masque **non** sur Z2 selon « hors Zobals », mais **Plastron** attendu sur Z2 si non exclu ; S demi-bouclier ; suivi PA individuel | proposé, non exécuté ; C1/C2 |
| N01 **mauvais masque** | Z sous Pleutre ; Plastron demandé sans revenir à Intrépide | Sort localement indisponible ; aucun bouclier attribué | proposé, non exécuté ; C1 |
| N02 **variante** | Z a équipé Ginga (sans Plastron) | Impossibilité de lancer Plastron ; Ginga ne traite qu'une cible selon C1 | proposé, non exécuté ; C2 |
| N03 **rang/critique** | Comparer rangs 1, 2 et 3 et critiques observés ; même niveau source, cibles joueur et invocation | Vérifier chacune des paires **500/250, 550/275, 600/300** ou **620/310, 670/335, 720/360 %**, mode d'arrondi et temps | proposé, non exécuté ; C1 |
| N04 **switch** | Z sous Intrépide octroie buff à A adjacent, puis change de masque ; A se déplace | Vérifier durée, persistance et si entrant tardivement reçoit l'effet ; ne pas présumer aura | proposé, non exécuté ; durée par rang **unknown** |
| N05 **expiration** | Plastron appliqué, enregistrer bouclier à chaque événement de tour | Vérifier le moment où « 2 tours » se termine et l'événement rendant Plastron de nouveau lançable « 3 tours » | proposé, non exécuté ; phases **unknown** |
| N06 **refus/Unknown** | Cast refusé, délai ou accusé de transport sans résultat ; ou S meurt avant résultat | Aucun bouclier marqué « confirmé » sans effet autoritatif ; préserver l'incertitude | proposé, non exécuté |

## Valeur tactique et repli

**Solo :** protéger Z et éventuellement l'invocation qui tacle est utile seulement si une menace peut consommer ces boucliers ; faire tourner le masque pour infliger des dommages peut coûter des PA et modifier le repositionnement. **Duo/trio/quatuor :** regrouper les alliés sur la zone donne plusieurs bénéficiaires à un lancer, mais ne pas perdre une attaque de l'allié juste pour le déplacer en zone sans comparer menace et coût de préparation. **Repli :** si Plastron est hors relance ou mauvais masque, considérer déplacement / contrôle / protection ciblée d'une autre variante réellement équipée et légale ; ne pas construire artificiellement une rotation ou un bouclier impossible. **Formules de dégâts :** hors périmètre ; aucun chiffre de dommages de combat n'a été déduit.

## Passage au développeur

Calculer la disponibilité des sorts via état de masque et variantes ; classifier les cibles, créer un événement de bouclier par entité bénéficiaire et calculer la défense **uniquement avec le vrai rang, le vrai niveau du Zobal et le statut critique** ; attendre les confirmations serveur. Ne pas coder une simulation des transferts de Grimace sans réplique. Priorité haute (un mauvais masque rend le lancer illégal, une invocation surprotégée fausse une évaluation de survie). Voir [PASSAGE_DEVELOPPEUR.md](./PASSAGE_DEVELOPPEUR.md).

## Reprise

Aucune page de transcription MCP disponible (YouTube/IP bloqué) ; dernière observation navigateur V1 à 6:42 et panneau de transcript toujours en chargement ; prochaine preuve utile : source de sort/client 3.7.13.17 et combat instrumenté avec Z, invocation S d'un allié, second Zobal Z2 et journal des boucliers. Aucun test ni jeu exécuté.
