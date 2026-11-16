# Fiche M32 — Matrice de synergies 1–4 acteurs et tour d'initiative

## Identité et version
- **ID principal** : `M32-PANDA-ENU-CRA-SUPPORT-CYCLE-v1`. **ID comparatif** : `M32-HUPPER-STEAM-SADI-ZOBAL-ROLES-v1`.
- Date 2026-11-04 ; PvM DOFUS 3 ; source des données de sorts [Grimoire MAJ 3.7.12](https://dofus-grimoire.fr/classes/), cible mentionnée dans kit Givre 3.7.13.17 **non confirmée sur serveur**.
- **Vidéos** : [HUZ REPLAY — duos fin 2025](https://www.youtube.com/watch?v=X9gMQA5rhVw), 2025-12-05, 37 min, **MCP FR auto horodaté lu** (1 203 segments, `next_cursor=null`), fenêtres 04:45–06:15, 08:25–10:50, 18:10–20:28, 21:10–28:20, 28:20–33:35 ; [Tenmalexis — teams de 4 en 2026](https://www.youtube.com/watch?v=U-L4-3toUGE), 2026-08-30, 12 min, **MCP info + langues seulement**, transcript FR bloqué IP ; [Tenmalexis 2025](https://www.youtube.com/watch?v=YFlyjdSo4Ek), 2025-05-02, 16 min, description ancienne version 3.1.
- `declare-video` = propos HUZ 2025 lus avec horodatage ; `avis-description-video` = description Tenmalexis ; `corrobore-source` = données 3.7.12 ; `hypothese` = préconditions/effets non contrôlés côté serveur ; aucune `observe-video`.

## Règles atomiques, statut et preuve

| ID | Affirmation unique | Statut | Source exacte ; limite |
| --- | --- | --- | --- |
| M32-R01 | Le trio Pandawa/Enu/Crâ apparaît comme noyau conseillé pour jeu à distance, auquel on ajoute une **quatrième classe selon objectif**. | `avis-description-video` | [Tenmalexis 2026, description chapitre progression 01:10](https://www.youtube.com/watch?v=U-L4-3toUGE&t=70s) ; [Tenmalexis 2025, description 08:09](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=489s), ce ne sont PAS des paroles horodatées examinées. |
| M32-R02 | HUZ conseille **Crâ+Enutrof** pour kiting/entrave, tout en reconnaissant certains cas favorables au Pandawa. | `declare-video` | [HUZ 05:12–06:15](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=312s) ; avis sans métrique de succès. |
| M32-R03 | Pandawa peut **porter et jeter** : 1 PA porter cible adjacente en état Sobre, 1 PA jeter sur case libre sous conditions. | `corrobore-source` | [Grimoire 3.7.12 Pandawa, Karcham](https://dofus-grimoire.fr/classes/pandawa/). Position résultante doit être confirmée par le combat. |
| M32-R04 | Enutrof lance Obsolescence **3 PA, -20 Esquive PM** puis Maladresse **1 PA, -2 PM tentés**. | `corrobore-source` | [Grimoire 3.7.12 Enutrof](https://dofus-grimoire.fr/classes/enutrof/). **Retrait effectif esquivable**, jamais garanti. |
| M32-R05 | Féca Barricade **2 PA** vise une protection **contre mêlée** ; les glyphes ont des phases dépendantes (début de tour/aura). | `corrobore-source` | [Grimoire 3.7.12 Féca](https://dofus-grimoire.fr/classes/feca/) ; [MAJ 3.7, marques au sol](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Durée et conditions réelles à vérifier. |
| M32-R06 | Eniripsa Mot Stimulant **2 PA** applique **+2 PA au bénéficiaire allié** au rang de référence ; soigne via sorts distincts, besoins de PV. | `corrobore-source` | [Grimoire 3.7.12 Eniripsa](https://dofus-grimoire.fr/classes/eniripsa/) ; [M15](../../M15/FICHE_ENIRIPSA.md) pour risques d'Encouragement/expiration. |
| M32-R07 | Huppermage doit appliquer/combiner des états élémentaires pour obtenir les effets bonus ; la présence de la classe ne déclenche aucune rune par défaut. | `corrobore-source` | [Grimoire 3.7.12 Huppermage](https://dofus-grimoire.fr/classes/huppermage/) ; HUZ [25:00–26:40](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1500s) = préférence duo avec Sadi. |
| M32-R08 | Steamer a un jeu conditionné par les tourelles et des actions de kite, et certaines voies de sorts ne sont disponibles qu'après niveaux/variantes précis. | `corrobore-source` + `declare-video` | [Grimoire 3.7.12 Steamer](https://dofus-grimoire.fr/classes/steamer/) ; HUZ [21:25–23:13](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1285s). |
| M32-R09 | Sadida nécessite état Infection/arbres/poupées ; un effet dépendant des **PM utilisés par l'ennemi** entre en tension avec l'entrave totale. | `corrobore-source` pour état Sadida ; `declare-video` pour conflit tactique | [Grimoire 3.7.12 Sadida](https://dofus-grimoire.fr/classes/sadida/) ; HUZ [31:56–33:13](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1916s). Condition exacte Chardon à confirmer version 3.7. |
| M32-R10 | Zobal doit avoir le **masque** compatible pour Plastron et donne des boucliers aux cibles dans sa zone effective ; ne soigne pas comme un Eni. | `corrobore-source` + `declare-video` avis | [Grimoire 3.7.12 Zobal](https://dofus-grimoire.fr/classes/zobal/) ; HUZ [26:40–28:20](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1600s). |
| M32-R11 | La mise à jour **3.7** modifie Crâ, et retire un ancien avantage d'amplification de l'Infection Sadida via portails. | `corrobore-source` | [Dofus pour les Noobs MAJ 3.7, 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Les vidéos antérieures ne peuvent pas prouver les nouveaux coefficients. |

## Contraintes comparables, matrice des rôles

| Option | Pourquoi la choisir | Ce qu'elle **n'apporte pas automatiquement** | Condition d'initiative |
| --- | --- | --- | --- |
| Solo **Crâ** | Frappe à distance et kite selon HUZ | Soin/placement d'autrui/entrave Enu | L'ennemi qui commence très près peut supprimer la phase de préparation. |
| Solo **Eni** | Soin/autonomie, si son deck le permet | Bénéficiaire d'un bonus de PA séparé, second personnage | Soins propres exécutables uniquement si Eni peut encore agir. |
| Duo **Crâ–Enu** | Retrait PM tenté et tir à distance | Retrait effectif garanti, ligne de vue, défense au corps-à-corps | Enu avant menace, Crâ après placement possible. |
| Duo **Crâ–Eni** | Buff PA/soin réel, possible jeu à distance | Réduction de PM d'Enu, sauvetage après mort déjà survenue | Eni avant Crâ pour exploiter +PA ce cycle. |
| Trio **Panda–Enu–Crâ** | Placement puis entrave puis tir | Soins/mitigation immédiats de quatrième | Panda avant Crâ pour rendre la zone utilisable. |
| Quatuor **+Féca** | Prévention d'un burst à la mêlée, glyphes | Soin de PV perdus, immunité à distance avec Barricade | Féca doit agir **avant** l'attaque pertinente. |
| Quatuor **+Eni** | Soin, +PA ciblé ; soutien si PV déjà faibles | Prévention de mort avant soin, couverture de plusieurs cibles sans zone/effet | Eni avant bénéficiaire ou avant dégâts à réparer selon usage. |
| Quatuor **+Hupper** | Combinaison état élémentaire, possible entrave/dommages | Sort de combinaison sans état préalable | Hupper agit après pose de marque/à son propre tour. |
| Quatuor **+Steamer** | Tourelle/échanges/repoussement, possibilité sustain selon build | Tourelle déjà en place et libre ; protection universelle | Tourelle posée **avant** l'effet/tour attendu, trajectoire recalculée. |
| Quatuor **+Sadida** | Infection en multi-cibles, arbres/poupées, contrôle | Infection déjà active sur toute la vague ; bonus via anciens portails pré-3.7 | Installer l'infection avant frappe, attention à l'ennemi qui doit **bouger**. |
| Quatuor **+Zobal** | Bouclier de groupe et maintien mêlée | Soin de PV, masque compatible sans changement | Bouclier posé avant l'impact ennemi, cibles réellement dans zone. |

## État initial reproductible (proposé)

- **Camp A :** P Pandawa, E Enutrof, C Crâ, S quatrième Féca **ou** Eni ; camp B : M1 monstre dangereux au contact de P, M2 tireur. Tous vivants ; aucune invocation initiale. `P.actorId`, `E.actorId`, `C.actorId`, `S.actorId` distincts.
- **Timeline positive :** `P→E→S→C→M1→M2`. Autres fixtures `C→M1→P→E→S` et `P→E→S→M1→C` pour distinguer initiative cible/effet.
- **Ressources** : budgets PA/PM/PO de chaque acteur lus du serveur ; pour illustration de compatibilité, P a au moins **2 PA** pour porter/jeter M1, E au moins **4 PA** pour Obsolescence+Maladresse, S au moins **2 PA** pour Barricade ou Stimulant. **Le Crâ possède un sort d'attaque connu de son deck réel**, coût à lire (non chiffré ici). Ne pas fixer 12 PA pour tous comme vérité serveur.
- **Positions relatives** : P=(0,0), M1 adjacent à P sur (1,0), E=(-2,1), C=(-4,1), S=(-3,0), `Z=(2,1)` case candidate de jet ; carte **abstraite**, non `cellId` DOFUS. Les règles de lancer en ligne/LoS et l'accessibilité exacte de Z sont des préconditions **à confirmer** avant tout projet.
- **Statuts** : P Sobriété, pas de cible portée ; M1 non stabilisé et déplaçable ; E a Obsolescence et Maladresse appris et disponibles ; S a bon sort/rang/variante ; C non encore joué ; aucune invulnérabilité de M1 connue ; `M1.pm` et son esquive lus en temps réel.

## Séquence d'un round : mêmes états, trois variantes de taille

| Phase | Acteur, action | PA attestés | Source d'autorité nécessaire et effet |
| --- | --- | --- | --- |
| T1.1 | **P** Karcham sur **M1**, puis Karcham jeter sur Z | 1+1 PA **dans Grimoire 3.7.12** ; PM éventuels propres à P | Confirmer porter et nouvelle cellule de **M1** ; rejeter tout plan de tir sur Z si jet non accepté. |
| T1.2 | **E** Obsolescence sur M1 puis Maladresse sur M1 | 3+1 PA | Confirmer -20 Esquive PM **si** appliqué et **0 à 2** PM effectivement retirés selon esquive ; aucune valeur garantie. |
| T1.3-A | **S=Féca** Barricade sur P si l'attaque attendue de M1 est mêlée | 2 PA | `P` bénéficiaire, statut anti-mêlée réel et durée ; ne protège pas d'office face à M2 distance. |
| T1.3-B | **S=Eni** Mot Stimulant sur C si C joue ensuite ; ou soin si P faible | 2 PA pour Stimulant | `C` bénéficiaire, gain PA **observé** quand C débute ; non rétroactif. Aucun soin chiffré inventé. |
| T1.4 | **C** frappe M1 s'il possède une LoS valide **après** jet | Coût issu du deck courant | Mesurer dégâts/PA et zone réelle ; pas de sort Crâ/rang présumé ici. |
| T1.5 | **M1/M2** jouent selon leur initiative | — | Attaque restante, PV perdus, PM vrais, état Barricade/soin différé ; décision de prochain cycle. |

**Solo** : supprimer E/S/C, P seul a placement sans frappeur complémentaire ; alternative C seul avec kite. **Duo** E+C : supprimer P/S, comparer entrée de la menace. **Trio** P+E+C : supprimer S ; protection dépend de distance/repli. **Quatuor** +Féca ou +Eni : comparer dommages reçus, économie de PA et éventuelle mort ; pas de score DPS non fondé. En trio/quatuor, le coût du mouvement de P, la ligne de tir de C et la résistance PM de M1 restent des variables partagées.

## Contre-scénarios discriminants

| Cas | Modification unique de l'initial | Actions | Résultat observable | Statut |
| --- | --- | --- | --- | --- |
| **P32-01** | Timeline P→E→S→C→M1, Z accessible, retrait partiel réel | Séquence ci-dessus | Jet confirmé, cible modifiée **avant** tir, bonus/protection attaché à la bonne cible, PM résiduels pris en compte | proposé/non exécuté |
| **N32-01 INIT** | Timeline devient **C→M1→P→E→S** | Crâ déjà joué, M1 agit avant support | Aucun tir Crâ bénéficiant du jet ou de Stimulant du **même cycle** ; aucune protection tardive n'annule le coup de M1 | proposé/non exécuté |
| **N32-02 LOS** | Z réellement **derrière obstacle** depuis C, autres paramètres inchangés | P pose M1 en Z puis E/S jouent | Crâ ne peut pas tirer son sort qui requiert LoS ; chercher autre cellule/mouvement légal | proposé/non exécuté |
| **N32-03 ESQUIVE** | `M1.esquivePM` forte, géométrie Z identique | Obsolescence+Maladresse | Le serveur confirme éventuellement **0 PM retiré** ; ne jamais déclarer M1 immobilisé ni l'attaque annulée | proposé/non exécuté |
| **N32-04 EFFET SADI** | M1 a zéro PM réellement dépensé après Enu ; S=Sadida avec effet de dégâts conditionné au mouvement | S prépare son sort, M1 ne bouge pas | Aucune activation de « dégâts par PM utilisés » supposée si précondition fausse ; version précise du sort et effets à prouver | proposé/non exécuté |
| **N32-05 MASQUE** | S=Zobal sans masque requis, ou allié hors zone | Tentative Plastron | Refus/absence de bouclier sur cible concernée ; aucune protection présumée sans statut/zone | proposé/non exécuté |

## Indicateurs et repli
- **Bénéfices** : PV effectivement préservés, cibles atteignables par C, déplacements adverses évités, objectifs de combat accomplis, PA réellement utilisés et tours nécessaires. **Ne jamais inventer un pourcentage de réussite** tiré d'un guide.
- **Risques** : mauvais ordre, LoS, refus du porter/jeter, résistance PM, états invulnérable/déplaçable, absence de sort appris, relance, expiration, cible différente du bénéficiaire, variante/masque/tourelle, fatigue d'initiative.
- **Repli** : réévaluer toutes les actions restant **dans les tours qui n'ont pas encore eu lieu** ; lorsque P échoue, ne pas prédire Z ; si E échoue, M1 conserve PM ; si S arrive tard, choisir secours pour le prochain cycle ; si C n'a pas LoS, passer à autre sort réellement légal.
- **Unknown** : toutes valeurs de dommages sans état serveur, formule d'esquive, durée des buffs dans la cible 3.7.13.17, effets exacts d'Infection et bonus portail post-3.7, variante précise de Crâ. Tests et logs inexistants.

Voir [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md) pour trois règles prioritaires et le contrat de replanning. Aucun test ni combat exécuté.

