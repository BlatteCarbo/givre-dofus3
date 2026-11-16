# Fiche M09-FECA-GLYPHE-001 — Terre Brûlée : déclencheurs indépendants

## Identité

- **ID stable :** M09-FECA-GLYPHE-001, règles M09-R01 à M09-R09.
- **Classes / acteurs :** poseur Féca ; alliés, ennemis de n'importe laquelle des **19 classes**, invocations et compagnons dans toutes compositions autorisées. L'identité, l'équipe, la source du déplacement et les immunités priment sur la classe. Aucune exécution exhaustive des 19 classes.
- **Version :** DOFUS 3.7 (références web octobre 2026) ; **cible locale 3.7.13.17 annoncée dans kit**, manifeste non vérifié. V1 DOFUS 3 juin 2025, V2 DOFUS 3.3 octobre 2025 ; transposition limitée.
- **Auteur / date :** worker M09, 04/11/2026.
- **V1 :** [Timtoobias, « NOUVEAU FECA MAÎTRE DES GLYPHES CHEAT SUR DOFUS 3 »](https://www.youtube.com/watch?v=n7_H49On03Q), 04/06/2025, 22 min, **FR automatique**, transcript complet **756 segments 00:00.16–22:04.76**, next_cursor nul. Passages : [00:53–01:36](https://www.youtube.com/watch?v=n7_H49On03Q&t=53s), [03:43–03:54](https://www.youtube.com/watch?v=n7_H49On03Q&t=223s), [05:21–05:32](https://www.youtube.com/watch?v=n7_H49On03Q&t=321s), [08:12–08:24](https://www.youtube.com/watch?v=n7_H49On03Q&t=492s), [15:08–15:23](https://www.youtube.com/watch?v=n7_H49On03Q&t=908s).
- **V2 :** [SABBAT, « DOFUS 3.3 - FÉCA MULTI DISTANCE »](https://www.youtube.com/watch?v=JYmV91dFDSA), 20/10/2025, 8 min, **FR automatique**, transcript complet **221 segments 00:00.08–08:21.39**, next_cursor nul. Passages [04:36–04:46](https://www.youtube.com/watch?v=JYmV91dFDSA&t=276s), [04:53–05:19](https://www.youtube.com/watch?v=JYmV91dFDSA&t=293s), [05:39–05:47](https://www.youtube.com/watch?v=JYmV91dFDSA&t=339s).
- **Méthodes :** MCP vidéo informations/langues/transcription, recherche et lecture de sources web contemporaines, tentative de navigateur CoS. **Échec de navigation/capture** : BROWSER_RESULT_UNCONFIRMED puis BROWSER_NOT_DISPATCHED. Aucune séquence de plateau observée ; toutes les preuves vidéo ci-dessous sont des **déclarations**, non des positions attestées.

## Règles et preuves atomiques

| ID | Assertion | Statut(s) et source | Justification / limite |
| --- | --- | --- | --- |
| **M09-R01** | Glyphes **aura**, **début de tour** et **fin de tour** ont événements distincts. | **corrobore-source** [mise à jour 3.7, § marques](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), datée 04/10/2026. | Ordre d'expiration et de déclenchement exact inconnu. |
| **M09-R02** | Terre Brûlée a **deux couches** : dégâts Feu aux ennemis au **début de leur tour** et aura retrait PM ennemis présents ou entrants. | **corrobore-source** [OpenDofusDB](https://openddb.dev/spells/12985-terre-brulee), [BackFromAFK 3.7](https://backfromafk.com/classes/feca/depuis-3-0/), [présentation 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). | **3 PA**, relance **3 tours**, marque en deux couches de durée **2 tours** selon DB ; perte effective PM et esquive non prouvées. |
| **M09-R03** | La présentation 3.7 décrit aussi une **résistance Feu alliée** dans l'aura de Terre Brûlée. | **corrobore-source** [présentation 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) uniquement ; détails **hypothese**. | Résumé DB omet cet effet, ne pas attribuer de valeur. |
| **M09-R04** | Défiance crée un glyphe **fin de tour** dégâts 4 éléments ennemis et aura résistance mêlée aux alliés présents/entrants. | **corrobore-source** [OpenDofusDB Défiance](https://openddb.dev/spells/12988-defiance) ; **declare-video** [SABBAT 05:14](https://www.youtube.com/watch?v=JYmV91dFDSA&t=314s). | **2 PA**, glyphe **1 tour** ; le jet « 1739 » évoqué par SABBAT n'est pas une formule. |
| **M09-R05** | Barrière, **variante exclusive de Défiance**, crée un anneau **au début du tour ennemi** si ciblage ennemi ou **à la fin du tour Féca** si ciblage allié. Dégâts au **franchissement** sous conditions de cible. | **corrobore-source** [OpenDofusDB Barrière](https://openddb.dev/spells/13019-barriere) ; **declare-video** [Timtoobias 03:43](https://www.youtube.com/watch?v=n7_H49On03Q&t=223s) sur le risque de sortir. | **3 PA**, ne pas considérer une téléportation sans chemin comme traversée prouvée. |
| **M09-R06** | Transhumance (**3 PA**) téléporte et **déclenche manuellement** des glyphes élémentaires à la case ciblée, **une fois par glyphe et par tour**. | **corrobore-source** [OpenDofusDB](https://openddb.dev/spells/12986-transhumance), [BackFromAFK](https://backfromafk.com/classes/feca/depuis-3-0/) ; **declare-video** [Timtoobias 05:21](https://www.youtube.com/watch?v=n7_H49On03Q&t=321s). | Variante **Égide** exclusive. Ordre TP/activation et cellules touchées à lire dans logs serveur. |
| **M09-R07** | Terre Brûlée **empêche d'autres glyphes élémentaires dans le même tour**, donc double pose élémentaire exige des tours différents. | **corrobore-source** [Terre Brûlée](https://openddb.dev/spells/12985-terre-brulee), [guide Féca](https://dofus-grimoire.fr/classes/feca/). | Le commentaire « second glyphe » [Timtoobias 01:01](https://www.youtube.com/watch?v=n7_H49On03Q&t=61s) ne prouve pas même tour ni règle actuelle. |
| **M09-R08** | Tacle/placement peuvent retenir une cible pour qu'elle **commence ou termine** son tour dans un glyphe ; tacle seul n'occasionne pas les dégâts du glyphe. | **declare-video** [Timtoobias 04:33](https://www.youtube.com/watch?v=n7_H49On03Q&t=273s), [SABBAT 05:39](https://www.youtube.com/watch?v=JYmV91dFDSA&t=339s) ; stratégie **hypothese**. | Pas d'échec de tacle ni de portée de téléportation vérifiés. |
| **M09-R09** | Sur un déplacement confirmé, recalculer **l'appartenance à l'aura** ; ne pas déclencher début/fin de tour sur simple **entrée ou sortie**. | Distinction **corrobore-source** [présentation 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; protocole de déplacement forcé **hypothese**. | Chemin parcouru par TP/poussée, ordre de résolution, auras superposées inconnus. |

**Statut par assertion :** aucune règle observe-video de cellule vérifiée, aucune valide-test ou valide-live.

## État initial et positions reproductibles

**Le scénario est une entrée de test construite à partir des règles sourcées, pas un tour que les vidéos démontrent dans son entier.**

- **Acteurs :** F (Féca, équipe 1), A (allié de n'importe quelle classe/compagnon, équipe 1), E (ennemi de n'importe quelle classe, équipe 2). Tours dans ce banc de test **F → E → A → F**. UID F/A/E distincts ; le propriétaire de la marque reste F.
- **Géométrie :** **G** = ensemble des cellules réelles de Terre Brûlée selon **rang et zone** extraites des métadonnées client. **c_in ∈ G**, **c_out ∉ G**, c_out est atteignable depuis c_in sans obstacle dans P1. Aucune identité serveur de cellule, rayon ou coordonnée arbitraire n'est donnée. Le placement doit être construit par le moteur géométrique à partir d'un plateau réel.
- **Ressources :** F avec Terre Brûlée équipée (**Vigie non équipée**), au moins **3 PA**, lancer légal de rang choisi (portée OpenDDB selon rang : 0–3, 0–4, 0–5 ; ligne de vue/règles de lancement réelles à contrôler). Transhumance testée ensuite seulement si **Égide non équipée**, ≥3 PA disponibles en plus. Dégâts, résistances, esquive PM et PM exacts **inconnus**.
- **État :** E commence présent sur c_in lorsque F achève la pose ; A d'abord hors G ; aucune autre marque ni déclenchement manuel antérieur. Pas de tacle en P1 ; variante N5 ajoute un tacleur. F pose une marque identifiée g avec couches **g.start** et **g.aura** ; chacune a **durée 2 tours** dans la fiche DB, instant exact d'expiration inconnu.
- **Conditions qui empêchent la reproduction exacte :** case légale de pose, rang effectivement utilisé, effet d'aura allié, ordre expiration/début de tour, événements serveur et téléportation/contraintes propres au combat.

## Séquence et transitions attendues, pas observées

| Étape / phase | Acteur | Action / cible | Prérequis | Coût | Effets attendus / ordre | Preuve et incertitudes |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | F, avant cast | F et E sont placés ; E sera **dans G** | Marque absente ; E vivant | — | Pas de dégât de glyphe | Setup **proposé**. |
| 1 | F, cast | Terre Brûlée sur cellule légale définissant G | Terre Brûlée équipée, relance OK, **3 PA** | **3 PA** selon DB | Créer une marque g, **deux couches** g.start/g.aura. E **présent** dans g.aura → entrave PM **si applicable**. **Pas de dommage g.start automatiquement sur la pose**. | [S2](https://openddb.dev/spells/12985-terre-brulee), [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) ; ordre pose/aura non vérifié. |
| 2 | F, fin de son tour | TurnEnd(F) | E reste dans G | — | **Pas de dommage g.start sur E pour la seule fin du tour de F** | R01 / R02. |
| 3 | E, début tour | TurnStart(E) en c_in ∈ G | g.start vivant, E vivant | — | **Dommages Feu à E au début de SON tour** ; jet après résistances **inconnu**. | R01 / R02. |
| 4 | E, mouvement | E **sort** de c_in vers c_out ; mouvement réellement accepté | PM, tacle, obstacle et destination compatibles | PM **inconnus** | Appartenance g.aura perdue en c_out ; entrave d'aura cesse selon moteur, à vérifier ; **aucun deuxième dégât g.start sur la sortie**. | R01 / R09. |
| 5 | E, fin tour | TurnEnd(E) en c_out | Aucune marque différente | — | **Pas de dommage Terre Brûlée g.start lors de la fin de tour**. | R01 / R02. |
| 6, **essai séparé** | F, tour suivant et g toujours actif | Transhumance vers case légale **dans G** ; E replacé dans G pour observer effet | Transhumance équipée, ≥3 PA, limites de relance, glyphe actif, manuel pas encore déclenché | **3 PA** selon DB | TP F puis demande **activation manuelle** du glyphe ; dégâts éventuels sur E concerné. Enregistrer activation de g pour ce tour, **une seule fois**. | [S5](https://openddb.dev/spells/12986-transhumance), [Timtoobias 05:21](https://www.youtube.com/watch?v=n7_H49On03Q&t=321s) ; ordre événementiel et portée exacte **inconnus**. |

**Bénéficiaires/risques :** ennemi E prend les dégâts début de tour et subit une entrave de PM à l'intérieur ; allié A peut bénéficier d'une aura de résistance Feu **mentionnée par S1 sans valeur prouvée**. La mobilité d'autres classes et le tacle influencent la **position au bon événement**, sans créer de dégâts supplémentaires non sourcés. Le déclenchement manuel fait avancer les dommages au tour F uniquement si les conditions serveur sont remplies. Aucun montant de dommages, résistances ou esquive n'est estimé.

## Contrastes hors P1, à tester séparément

**Défiance (fin de tour)** : dans une configuration **avec Défiance et sans Barrière**, F pose sa marque d (2 PA selon [S3](https://openddb.dev/spells/12988-defiance)) ; E qui **termine** son tour dans le glyphe d.end doit recevoir les dommages fin de tour ; E qui en **sort avant TurnEnd(E)** ne les reçoit pas. La couche d.aura de résistance mêlée s'applique aux alliés présents/entrants, séparée du déclenchement de dégâts. Le montant « 1739 » annoncé par SABBAT est un jet en 3.3 et ne sert pas de formule.

**Barrière (franchissement)** : dans une **autre** configuration, **Barrière équipée et Défiance absente**, sur un **ennemi E** elle pose anneau différé au **début du tour E** ; sur **allié A**, anneau différé à la **fin du tour F**. Elle occasionne des dommages aux entités admissibles qui **traversent** selon description, puis l'état/la marque sont retirés dans les cas définis. Le passage à pied et le saut par téléportation **ne doivent pas être déclarés identiques** sans replay. [Source 3.7 actuelle](https://openddb.dev/spells/13019-barriere). Coût **3 PA** issu de cette base.

## Scénarios discriminants proposés

| Cas | Entrée / actions | Résultat observable attendu | Règle testée | Exécution |
| --- | --- | --- | --- | --- |
| **P1** | Terre Brûlée ; E dans G à **TurnStart(E)** ; E marche ensuite vers c_out. | Proc g.start **au début** de E ; sortie met fin à la présence g.aura ; **aucun second proc début-tour lors de la marche**. | R01,R02,R09 | proposé, non exécuté |
| **N1 — entrée tardive** | E commence son tour **hors G**, puis entre dans G au milieu de son tour. | Aura de G s'applique si cible admissible ; **pas de dégât g.start immédiat** ; si E reste jusqu'au prochain TurnStart, dégâts à ce moment. | R01,R02 | proposé, non exécuté |
| **N2 — sortie précoce** | Autre acteur pousse E **hors G avant** son prochain début de tour. | E commence à c_out : **aucun dégât g.start** malgré passage ancien dans G ; aura hors zone inactive. | R02,R09 ; déplacement par tiers | proposé, non exécuté |
| **P2/N3 — Défiance** | Version Défiance : E est dans d.end à **TurnEnd(E)** (P2) ou en sort avant son TurnEnd (N3). | Dégâts 4 éléments **P2 seulement**, condition fin de tour ; aura alliée indépendante. | R01,R04 | proposé, non exécuté |
| **N4 — traversée Barrière** | Barrière sur E ; attendre apparition au TurnStart(E) ; comparer **marche franchissant le bord**, poussée et TP entre deux côtés **sans case intermédiaire**. | Marche : événement de traversée et suppression prévus par S4 ; poussée/TP : **résultat exact inconnu**, relever événement/état plutôt qu'inventer. | R05 | proposé, non exécuté |
| **N5 — tacle** | E dans G, tentative de sortie **refusée par tacle** ; oldCell=newCell d'après serveur. | **Aucun événement leave** ; E reste éligible à g.start si toujours présent à son prochain début de tour. PA/PM perdus à l'échec inconnus. | R08,R09 | proposé, non exécuté |
| **P3/N6 — manuel** | Transhumance dans g avec E dedans, puis autre tentative de déclenchement **du même glyphId** et même tour ; en variante, autre glyphId posé sur tour antérieur. | Première activation possible ; **deuxième activation manuelle du même g interdite au tour courant** ; une autre marque possède sa propre limite (à vérifier). | R06,R07 | proposé, non exécuté |
| **N7 — 19 classes** | Rejouer P1 avec occupants UID de **chacune des 19 classes**, leur équipe, compagnons/invocations, et avec déplacement par un **autre** acteur de composition variée. | Mêmes déclencheurs de phase sur cible admissible ; dommages et immunités gouvernés par effets/états/équipe et classe **seulement si règle documentée**. Aucune exception inventée. | contrat classe-agnostique | **matrice proposée**, non exécutée |

## Passage au développeur

- **Données requises :** glyphId unique, propriétaire UID, couches et type START_TURN / END_TURN / AURA / CROSSING, zone cellules, création/expiration, bénéficiaires/effets, usage de déclenchements manuels par glyphe/tour, ancienne/nouvelle cellule et mouvement détaillé, équipe/classe/compagnon, état de tacle, variante équipée et sources des effets serveur.
- **Ce qui est calculable immédiatement comme simulation candidate :** présence à TurnStart/TurnEnd, aura conditionnée à la cellule actuelle, combinaison de couches de la même marque, coût/restrictions des casts référencés ; **pas** de dégâts chiffrés, de ret PM estimé, ni de traversée TP supposée.
- **Inconnus bloquants :** client cible exact, zones de glyphes réellement chargées, ordre expiration vs proc, phase d'entrée de cible déjà sur zone à la pose, déclenchement manuel sur deux glyphes, traversée Barrière par déplacement forcé/TP, réponse à tacle, résistances Feu alliées, événements de refus/Unknown.
- **Priorité :** élevée pour la légalité et la valeur tactique des plans à plusieurs acteurs. Valider P1/N1/P2/N4 sur traces avant promotion de statut ; aucune validation réelle à ce stade.

## Reprise

Transcriptions V1/V2 intégrales (next_cursor nul), dates/version et liens précis dans rapport. Dernier passage utile V1 vers 16:14–16:27, V2 vers 05:39–05:47. Une vraie vidéo arrêtée sur plateau de glyphe et un replay expurgé de la **cible 3.7.13.17** sont requis avant d'attribuer observe-video, valide-test ou valide-live.
