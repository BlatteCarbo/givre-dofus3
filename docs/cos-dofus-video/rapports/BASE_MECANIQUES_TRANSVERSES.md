# Mécaniques communes à maîtriser avant les combos

Cette page est un cours transversal utilisable pour les 19 classes et les équipes de 1 à 8. Elle sépare **la légalité**, **l'effet**, **le gain tactique** et **la preuve**. Les exemples de plateau sont des fixtures imaginées pour enseigner la méthode, **pas des observations d'une vidéo**.

## 1. Vue globale d'un tour

```text
Observation serveur S0
    ↓
Construire hypothèses / menaces / actions légales
    ↓
Choisir l'action A1 selon objectif, risque et ressources
    ↓
Envoi de l'intention → réponse serveur / événement confirmé
    ├── accepté + état visible → S1, recalcul des ressources et positions
    ├── refus/erreur         → S0 ou nouvel état observé, replanification
    └── résultat inconnu    → conserver Unknown, ne pas rejouer l'effet
    ↓
Décider A2 à partir de S1 (et non de S0)
    ↓
Fin du tour / réponses des alliés et adversaires → S2
```

**On ne doit pas déduire du simple ACK que le sort a été effectivement résolu.** Une animation peut suivre plusieurs trames ; inversement une trame résolue n'implique pas un résultat de dégâts si la cible a bougé ou est invulnérable. Dans le code local, `GivreClone.Protocol/FightMarks.cs` distingue pose, retrait et activation de marques ; `GivreClone.Protocol/MapFightReader.cs` lit des événements de déplacement et marques. `SpellResolver` sait signaler des effets différés/non modélisés et `CombatTurnPlanner` fonctionne par recherche bornée. Ces éléments sont un **point de lecture**, pas une validation en production des séquences ci-dessous.

## 2. Quels états conserver ?

| Famille | Exemple | Données à conserver | Contre-exemple discriminant |
| --- | --- | --- | --- |
| Déplacements | poussée, attirance, échange, téléportation | positions avant/après, obstruction, portage, source confirmée | échanger sur case occupée vs case vide |
| Visibilité et ligne de vue | invisibilité, mur, sort sans ligne de vue | identité, visibilité, certitude de la case, obstacles, contraintes propres au sort | cible présumée sur ancienne case |
| Ressources | PA, PM, PO, points reçus et perdus | par acteur et par tour, coût réel, esquives, boosts différés | PA supposé gagné par un autre acteur |
| Marques au sol | piège Sram, glyphe Féca | ID, auteur, cellule, type, actif, pose/retrait/trigger | trigger observé mais retrait non observé |
| Entités créées | poupée, bombe, tourelle, invocation | propriétaire, équipe, case, PV, état, niveau d'évolution, initiative | invocation morte avant sa prochaine action |
| États temporaires | Téléfrag, masques, boosts, infecté | identité de la cible, durée, empilement, source, consommation | réutilisation d'un état consommé |
| Portails | Éliotrope | ordre, cases, accès, ouvert/fermé, chemin et sortie | entrée vue mais sortie différente à cause d'un autre portail |
| Déclencheurs | entrée de zone, fin de tour, dégâts de poussée | déclencheur exact, ordre des événements, limites par tour | attribuer le dégât à la pose plutôt qu'au déclenchement |
| Dommages | direct, zone, poison, indirect, reflet | jet, élément, bonus, résistances, boucliers, critique, cibles | convertir un seul jet vidéo en formule universelle |
| Objectifs de combat | succès, challenge, invu de boss | cible, acteur, ordre, événements déjà validés, nombre de tours | kill interdit par challenge mais meilleur en dégâts |

## 3. Géométrie du plateau

Écrire les cellules `cA` (allié), `cE` (ennemi), `cB` (blocage) et `cT` (case cible), sans inventer des identifiants `0..559` à partir d'une vidéo. Pour chaque action, vérifier :
- voisinage réellement accessible et occupation des cases ;
- portée min/max, modifiable ou non ; ligne, diagonale, cône, cercle et ordre de propagation ;
- ligne de vue, obstacles et éventuelles exceptions du sort ;
- chemin de la poussée, collision, cible déplacée, déclenchement de piège/portail/glyphe ;
- cible arrivée sur case différente de celle calculée avant l'action précédente.

**Exercice A — position** : A pousse E de deux cases vers B. Cas 1 : les deux cases libres ; cas 2 : la seconde bloquée ; cas 3 : piège sur la première. Les trois sorties ne peuvent être supposées identiques. Les dégâts de collision et l'ordre du piège exigent des règles versionnées.

**Exercice B — initiative** : placeur P avant tireur C vs tireur C avant P. Même inventaire et même plateau, mais le tir débloqué par P n'existe que dans le premier ordre au tour en question. Dans le second, le plan peut préparer le tour suivant.

## 4. Préparation, exécution, entretien, abandon

Une mécanique doit être décrite comme machine à états :

```text
Absent → Préparé → Actif → Déclenché → Consommé/Expiré
            ↘ détruit / annulé / inaccessible
```

Ne pas attribuer la même chronologie à toutes les mécaniques. Exemples :
- **Sram** : piège posé ; activation et déclenchement demandent des événements distincts ; disparition seulement sur unmessage de retrait fiable.
- **Roublard** : bombe posée ; vieillissement/Combo, mur dérivé des bombes vivantes ; mort et détonation sont deux déclencheurs différents.
- **Féca** : marque posée ; effets selon événements de zone et tour du combattant. 
- **Xélor** : état créé sur cible ; consommation par sort ; historique de cellule et remboursement AP sur événement réel.
- **Éliotrope** : portails et redirection ; leur ordre et position changent l'itinéraire.

## 5. Les ressources et leur bénéficiaire

Un bonus n'est utile que si son **bénéficiaire** peut en profiter. Pour chaque action de soutien, enregistrer `lanceur, cible, coût, durée, gain réel, action augmentée, risque évité`. Un gain de 2 PA sur le soutien après le tour du finisseur peut être gaspillé si la durée est courte. Une protection posée sur un allié hors de danger peut réduire le score par rapport à une action de contrôle. De même, une entrave ne vaut que si elle interdit au moins un mouvement ou sort réellement dangereux.

## 6. Séparer efficacité et validité

Une tactique peut être **valide mais peu rentable** (cinq tours de préparation pour une cible faible), **rentable mais illégale** (cible derrière le mur), **valide mais risquée** (portail qui ouvre aussi une route ennemie), ou **performante mais incompatible avec un challenge** (trop de cibles touchées, mauvais tueur). Comparer au minimum :
1. dommages nets sur ennemis et alliés ;
2. PV/boucliers/états restants à la fin de l'échange ;
3. ennemis pouvant encore attaquer au prochain tour ;
4. tours et ressources de préparation ;
5. réversibilité si une action est refusée ;
6. respect des challenges, invulnérabilités et objectifs.

## 7. Simuler un monstre, pas seulement une cible à frapper

Un adversaire inclut au moins : PV, résistances, modes d'attaque, portée, déplacement, déclencheurs, immunités, danger de ses invocations, ciblage et phase de boss. Exemple de [Gourlo le Terrible](M11/FICHE_GOURLO.md) : le placement près d'un tonneau et le **tour du tonneau** importent plus qu'un gros sort direct sur le boss. À confirmer via sources de version et replay ; voir M11.

## 8. Contrats de test adversarial

- **Positif** : préconditions connues et vraies, séquence observée puis état conforme.
- **Négatif obstacle** : même plan, mais une seule case est bloquée.
- **Négatif état** : état requis absent ou déjà consommé.
- **Négatif temps** : une invocation ou un buff expire avant l'action suivante.
- **Négatif initiative** : l'allié exécutant joue avant la préparation.
- **Négatif serveur** : refus ou résultat inconnu ; aucune transition inventée et aucun double lancement destructeur.
- **Négatif version** : rang, variante, portée ou cible diffèrent de la source ancienne.
- **Négatif challenge** : le plan crée le plus de dégâts mais fait échouer l'objectif.

Les cas restent **proposés, non exécutés**. Conserver fixtures, événements attendus et paramètres pour qu'un développeur puisse les traduire en xUnit ou replay après confirmation.

## 9. Guide de lecture des vidéos

Sur un extrait horodaté : relever *ce que dit l'auteur*, *ce qui est visible sur le plateau*, *ce que change chaque action*, puis seulement proposer une règle. Les commentaires « ici » et « là » sont inutilisables sans capture. Comparer l'état du jeu avant l'action et après le signal de résolution ; les coupes de montage interdisent de présumer un tour continu. N'utiliser les vidéos de 2022–2025 comme source de chiffres pour DOFUS 3.7 qu'après recoupement.

