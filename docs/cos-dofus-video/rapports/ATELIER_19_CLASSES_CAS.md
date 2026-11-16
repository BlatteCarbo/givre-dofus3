# Atelier pratique : comprendre réellement les 19 classes et leurs interactions

Date : 4 novembre 2026. Ce cahier accompagne les [19 fiches de classe](ATLAS_19_CLASSES.md) et les [missions détaillées](SYNTHESE.md). Les situations ci-dessous sont des **exercices inventés pour tester un raisonnement**, pas des combats regardés ni des résultats serveur. Elles évitent les valeurs arbitraires de sort : les PA/PM, portée et effets à inscrire proviennent du **rang et du patch effectivement utilisés**. Pour chaque exercice, le lecteur doit expliquer *pourquoi* une action devient faisable, *quand* et *pour qui* elle est utile, et ce qui fait échouer l'enchaînement.

## Comment résoudre chaque cas

1. **Copier le plateau** : alliés `A1,A2` sur cellules connues ; ennemis `E1,E2`, obstacle `O`, zone `G`, marque `M`, invocation `S`. Identifier les tours restants et l'ordre d'initiative.
2. **Séparer fait et projet** : `observé` (case, état, ressource, événement serveur) / `prévu` (sort, déplacement, bénéficiaire, dégâts attendus) / `inconnu`.
3. **Appliquer une action** et mettre à jour la carte seulement après preuve de résolution. Recalculer PA/PM du seul lanceur.
4. **Répéter avec un contre-scénario** modifiant une seule variable ; si le résultat ne change pas, le test n'isole pas la mécanique.
5. **Évaluer le gain** : risque évité, temps de préparation, vraie action alliée débloquée, protection apportée, respect de la condition de boss/challenge.

Les liens renvoient à des dossiers documentaires avec statuts de preuve exacts. N'utiliser ni un ancien sort ni une formule de dégâts comme oracle non confirmé pour le client 3.7.13.17.

## Dix-neuf cas diagnostiques

| Classe et dossier | Situation positive à examiner | Le contre qui doit changer le plan | Compétence acquise |
| --- | --- | --- | --- |
| [Crâ — M13](M13/RAPPORT.md) | Un tir à longue portée existe si ligne de vue et PO du rang autorisent la cible ; un recul peut empêcher l'approche ennemie | Même portée mais obstacle `O` ; Acuité peut lever la LdV tout en changeant la portée minimale | Légalité géométrique séparée de la puissance |
| [Ecaflip — M17](M17/RAPPORT.md) | Une action élémentaire confirmée modifie une Main ; un sort ultérieur exploite l'état réellement acquis | Premier sort refusé ou carte inattendue ; ne pas prévoir une combinaison de quatre cartes fictive | Condition et aléa, historique de carte |
| [Éliotrope — M04](M04/RAPPORT.md) | Plusieurs portails sont recensés avant d'utiliser une redirection et un axe favorable | Une diagonale donne égalité et modifie la sortie ; un portail ignoré ailleurs sur carte détourne le sort | Géométrie des réseaux, ordre des portails |
| [Eniripsa — M15](M15/RAPPORT.md) | Le soutien `A1` soigne ou donne PA à `A2` avant le tour où `A2` l'utilise | Le bénéficiaire a déjà joué ou son état Encouragé a été remplacé | Bénéficiaire, temporalité, remplacement de buff |
| [Enutrof — M16](M16/RAPPORT.md) | Une tentative de retrait PM sur `E1` protège une cible si le déplacement dangereux devient impossible | `E1` esquive une partie du retrait et peut encore atteindre `A2` | Retrait maximal annoncé vs retrait réellement observé |
| [Féca — M09](M09/RAPPORT.md) | Un glyphe agit à son déclencheur (aura, début de tour, fin de tour, traversée) | Une cible traverse une autre zone ou se téléporte par-dessus ; le trigger attendu peut différer | Événements déclencheurs, durée, nature de zone |
| [Forgelance — M24](M24/RAPPORT.md) | Une Lance placée sur `L` rend une attaque de zone sur `E1` et `E2` possible | `L` est enlevée, `E2` n'est plus dans la zone ou l'attraction se bloque | Entité instrument, zones, séquence sensible au placement |
| [Huppermage — M19](M19/RAPPORT.md) | État élémentaire sur `E1` puis second élément sur cette même cible active une combinaison | Le premier état a expiré ou second élément lancé sur `E2` | Effet conditionné à la même cible et à l'ordre |
| [Iop — M14](M14/RAPPORT.md) | Colère préparée, cible au contact et buffs encore valides au tour de frappe | `E1` n'est pas au contact ; Bond consomme les PA indispensables au buff | Préparation multi-tours et coût d'accès à la cible |
| [Osamodas — M18](M18/RAPPORT.md) | Invocation `S1` sur cellule libre, capacité disponible et prochain tour utile | Plafond d'invocations atteint ou `S1` meurt avant d'agir | Acteur autonome, propriété et capacité |
| [Ouginak — M22](M22/RAPPORT.md) | Proie `E1` reste à portée, les états et l'entourage remplissent les conditions d'une attaque | Proie change de cible, cible fuit ou les invocations voisines meurent | Rôle des états et de l'occupation |
| [Pandawa — M23](M23/RAPPORT.md) | Porter `E1`, le jeter sur une cellule libre qui dégage un tir allié | Case d'atterrissage occupée ou état interdit au lancer, initiative du tireur avant placeur | Portage et jet comme transition autoritaire |
| [Roublard — M02](M02/RAPPORT.md) | Deux bombes adaptées vivantes créent un mur avant l'arrivée de `E1` | Une bombe détruite, le mur disparaît ; Poudre et Détonateur ne sont pas le même trigger | Préparation, persistance, mort vs détonation |
| [Sacrieur — M05](M05/RAPPORT.md) | Attirer `E1` en ligne puis le frapper depuis une case sécurisée | État indéplaçable ou obstacle, ou trop de PV sacrifiés par le boost | Coût de placement et rendement |
| [Sadida — M21](M21/RAPPORT.md) | `E1,E2` portent réellement un état de propagation puis une attaque exploite la liaison | `E2` n'est pas Infecté, arbre disparaît ou le déclencheur diffère | Propagation, état par cible, entretien |
| [Sram — M01](M01/RAPPORT.md) | Réseau de marques avec ordre de pose et mouvement forcé sur une case déclenchante | Piège absent, ordre inversé ou obstacle arrête le déplacement | Chronologie et événements de marque |
| [Steamer — M20](M20/RAPPORT.md) | Tourelle vivante évoluée sur une case contrôlée menace une cible en portée | Tourelle détruite avant de jouer ou zone de tir obstruée | Investissement et identité d'invocation |
| [Xélor — M03](M03/RAPPORT.md) | Échange sur case occupée créant Téléfrag puis sort consommateur sur cette cible | Destination vide ; le Téléfrag attendu n'existe pas | Historique des cases et consommation d'état |
| [Zobal — M12](M12/RAPPORT.md) | Masque en vigueur autorise Plastron sur le véritable bénéficiaire | Masque incompatible ou invocation recevant une valeur de bouclier différente | États exclusifs, boucliers par type de cible |

## Cas développé 1 — Quel duo gagne réellement du temps ?

**Contexte artificiel** : un Crâ `C` doit éliminer un ennemi `E` à distance, un Enutrof `N` peut essayer de retirer des PM. `C` et `N` ont chacun leurs ressources ; `E` atteindra une case menaçante `cD` si ses PM restants suffisent. Le sort d'entrave tente un nombre de PM connu pour la version mais sa réussite n'est pas certaine.

```text
Tour du N : État E = (case e0, PM observés=6)
         tentative retrait → résultat ? (inconnu tant que serveur muet)
         ├─ retrait effectivement appliqué → E PM observés=2, danger cD peut être supprimé
         └─ retrait esquivé → E PM observés=6, danger cD persiste
Tour du C : choisir tirs / recul / repli en fonction de l'état N réellement confirmé
```

Pour évaluer ce duo : comparer les PV alliés restant après la réponse ennemie, l'attaque Crâ effectivement accessible et le coût d'opportunité du tour Enutrof. **Variation d'initiative `C→N`** : le Crâ ne doit pas prédire au présent le retrait d'un allié qui n'a pas encore joué. Références [M13](M13/FICHE_CRA_PORTÉE_LDV_RECUL.md) et [M16](M16/FICHE_ENUTROF.md).

## Cas développé 2 — Trio : qu'apporte vraiment le troisième acteur ?

**Contexte artificiel** : duo Pandawa `P` + Forgelance `F` capable de placer deux ennemis dans une zone de Lance. Ajout du Féca `D` pour protéger P.

- Avec **`P→F`** : P déplace une cible sur la zone, F l'exploite ; résultat prévu mesurable en dégâts et en risque sur le placeur.
- Avec **`P→D→F`** : D protège le placeur si un monstre peut réellement le tuer pendant le prochain tour ; sinon le Féca aurait pu frapper ou contrôler au lieu de protéger.
- Avec **`F→P→D`** : le placement ne profite pas à F au tour actuel ; considérer une préparation pour la prochaine rotation et comparer à une attaque indépendante.
- Si la case cible est occupée, ni P ni F ne peuvent compter sur le jet. Si la Lance a déjà été rappelée, le sort conditionnel doit être recalculé.

Le **gain marginal** de D est le risque effectivement évité, pas la simple présence d'une troisième classe. Références [M23 Pandawa](M23/RAPPORT.md), [M24 Forgelance](M24/RAPPORT.md) et [M09 Féca](M09/RAPPORT.md).

## Cas développé 3 — Quatuor : protéger la fenêtre ou la perdre

**Contexte artificiel** : Pandawa `P`, Éliotrope `L`, Féca `D`, Iop `I` préparent une Colère de Iop au tour de retour légal. Le schéma `P→L→D→I` est un **ordre d'exercice** (pas celui d'une vidéo).

```text
Tour préparatoire :
 P place E et choisit une case de sortie potentielle
 L ajuste le réseau de portails en examinant TOUTES les sorties
 D protège le bénéficiaire le plus exposé
 I lance une première Colère si le sort est légal ; seul son résultat confirmé
   permet d'armer une future fenêtre, jamais l'intention émise
Tours intermédiaires :
 survie, PM/PO, cooldown, durée des boosts, boss phase et challenges observés
Tour de retour :
 confirmer que E est atteignable
 vérifier ressources réelles I, priorité du placement P, statut des portails L
 arbitrer Colère + boosts vs Bond + Colère si besoin de mobilité
```

**Contres** : Iop joue avant P ; portail Éliotrope fermé ou sortie différente ; boss devient invulnérable ; bouclier Féca expire ; E est envoyé sur une case non traversable ; un challenge interdit l'attaque. Replanifier sans inventer de PA ou de placement. Références [M08](M08/RAPPORT.md), [M14](M14/RAPPORT.md), [M04](M04/RAPPORT.md).

## Cas développé 4 — Deux stratégies pour un boss à mécanique

**Contexte documentaire** : [Gourlo](M11/RAPPORT.md) peut dépendre du tour et de la position du Tonneau Pirate ; [Founoroshi](M25/RAPPORT.md) a changé l'interaction entre glyphes de feu et portails en 3.7. Il faut choisir le bon **état de boss** avant la stratégie.

**Hypothèse A (Gourlo)** : protéger Tonneau vivant et amener le boss près de lui avant son déclencheur. Si l'invocation meurt ou joue trop tôt, la fenêtre de dégâts ne se produit pas. Ajouter des dégâts directs contre un boss encore résistant peut être moins bon que protéger une case et une invocation.

**Hypothèse B (Founoroshi)** : ne pas fermer fictivement un portail parce qu'un glyphe de feu le recouvre ; conserver son vrai statut, mais mesurer le danger de la route pour les alliés et ennemis. Un Féca défensif, un Éliotrope et un Crâ peuvent agir différemment si la ligne de vue est ouverte mais que l'accès expose l'équipe.

**Test négatif commun** : même sortie/tour/ressources, mais une seule condition d'invulnérabilité ou de portail change ; le plan ne doit pas sortir les mêmes dégâts ou le même trajet.

## Cas développé 5 — Compagnon et challenge : à qui appartient l'attaque ?

Supposer `J` un personnage, `K` Korbax contrôlé, `S` une invocation autonome et `E1,E2` ennemis. Le challenge Duel, selon [M07](M07/RAPPORT.md), contraint les attaques d'un même ennemi à une attribution au premier attaquant. Avant de lancer :
- identifier pour chaque action `actorId` réel de J/K/S et `ownerId` éventuel ;
- déterminer dans **la version cible** si l'action de S compte comme celle de son propriétaire et si K est traité comme un combattant allié pour le challenge ;
- conserver `firstAttacker[E1]` et les exceptions effectivement prouvées ;
- refuser une prédiction d'attaque susceptible d'invalider le challenge lorsque la règle d'attribution est Unknown.

**Négatif** : J attaque E1 puis K attaque E1 ; le moteur doit savoir s'il rate Duel ou si une exception versionnée est applicable. Pour De la même lignée, la condition se vérifie en fin de tour de **chaque** allié : finir en ligne avec J quand J a déjà quitté la cellule n'est pas une garantie. Ajouter une invocation entre deux alliés ne remplace pas automatiquement la présence d'un combattant admissible.

## Questions d'examen corrigées

1. **« Un tutoriel dit -5 PM, donc E sera immobilisé ? »** Non : la description du sort indique une *tentative* ; l'esquive, la résistance et les événements déterminent la perte réellement appliquée.
2. **« Le piège s'est déclenché, il est forcément supprimé ? »** Non : distinguer événement de trigger et événement de retrait, avec preuve.
3. **« Avec un Féca, tout le monde est protégé ? »** Non : vérifier la cible, la zone, la durée, la nature du bouclier et l'état au tour où arrive le danger.
4. **« La vidéo montre une explosion à 20 000 dégâts, on implémente la valeur ? »** Non : l'équipement, la résistance, les charges, les jets et le nombre de cibles ont influencé ce résultat.
5. **« Quatre bons personnages font une bonne team ? »** Pas forcément : il faut connaître l'initiative, les conflits de zone/portail/invocation, le rôle de chacun et les tests de dégradation.
6. **« Un soin est forcément rentable s'il rend des PV ? »** Non : comparer le risque futur et l'occasion d'éliminer une menace, avec cible/buff réellement concerné.
7. **« Le plan est écrit, il suffit de le rejouer après un timeout ? »** Non : un résultat inconnu du serveur exige réconciliation, pas relancement aveugle.

## Niveau d'exigence pour la prochaine vidéo

Rendre une fiche `source_lue` exige titre, auteur, date, version, langue et intervalle réellement extrait ; `reproductible` exige cases, état initial complet, une action par ligne et un test négatif discriminant. Un tutoriel transcrit avec des mots « ici », « là » sans capture reste une **source d'hypothèses**. Les mises à jour 3.7 qui changent les sorts ou boss doivent être signalées. Une classe est réellement approfondie lorsque ses mécaniques prioritaires et au moins un duel, un trio ou quatuor ont des règles sourcées, des cas d'échec et un passage au développeur lisible.

