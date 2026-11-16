# Équipements, statistiques, rangs de sorts et progression : comment les intégrer aux combos

Ce guide complète l'[atlas des 19 classes](ATLAS_19_CLASSES.md). Il explique les **conditions réelles** qui font qu'un combo décrit dans un tutoriel reste faisable, même avec la bonne classe et le bon partenaire. Références documentaires : [Back from AFK, changements du jeu jusqu'à 3.7](https://backfromafk.com/) (octobre 2026), [Dofus Grimoire, sorts par classe 3.7.12](https://dofus-grimoire.fr/classes/), [Mise à jour 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Ces sources ne certifient pas chaque inventaire ou sort du client local 3.7.13.17.

## 1. L'ordre des décisions avant de choisir une panoplie

1. **Quel adversaire ?** Résistances élémentaires et de poussée, esquive PA/PM, PV, mobilité, immunités, cible de challenge, durée avant phase critique.
2. **Quel rôle ce personnage remplit-il ?** Placeur, soutien, finisseur, contrôle, invocation, survie, zone. Un Pandawa de placement n'a pas nécessairement les mêmes exigences qu'un Pandawa de dégâts.
3. **Quelle mécanique veut-on exploiter ?** Sort, rang, variante, portée minimum/maximum, ligne de vue, cible, zone, PA, durée et relance, conditions de déclenchement.
4. **Combien de tours avant le gain ?** Un Roublard qui investit dans des bombes, un Iop qui charge Colère, un Steamer qui fait évoluer une tourelle et un Sadida qui plante des arbres ne partagent pas le même horizon.
5. **Quelle marge de survie ?** PV, boucliers, résistances, mobilité et évitement des attaques ennemies, surtout après une initiative défavorable.
6. **Quel partenaire rend ce plan possible ?** La valeur d'un bonus de portée ou de PA est celle des actions réellement ajoutées au tour du bénéficiaire ; une protection vaut les dégâts qu'elle évite sur la personne effectivement menacée.

Un équipement n'est « optimal » que pour un *scénario* (niveau, ennemis, carte, rangs, initiative, objectifs). Les recommandations universelles de type « cette classe toujours Terre » ne sont pas des spécifications.

## 2. Les attributs à stocker séparément

| Statistique ou paramètre | Sert principalement à | Erreur de simulation fréquente | Test |
| --- | --- | --- | --- |
| PA actuels et prochains | Faisabilité des séquences et soutiens | Prévoir le gain d'un buff sur un acteur qui a déjà joué | Soin/boost avant vs après tour |
| PM actuels, tacle et fuite | Mobilité et mise au contact | Marcher à travers une zone de tacle sans pertes | Avec/sans adversaire taclant |
| Portée et portée modifiable | Accès au tir, contrôle | Un bonus PO corrige aussi la portée minimale ou un sort fixe | Crâ proche/loin et bonus PO |
| Ligne de vue | Tir direct et ciblage allié | Un sort sans LdV devient gratuit en distance | Même portée, obstacle présent |
| Résistances élémentaires | Dégâts directs et poisons | Mélanger résistance fixe et pourcentage | Deux ennemis, même PV, résistances différentes |
| Dommages et résistances de poussée | Collision et déplacements | Appliquer le coefficient Air/élément à la collision | Mur vs terrain libre ; deux résistances poussée |
| Retrait/esquive PA et PM | Probabilité/résultat d'entrave | Déduire d'un sort « -3 PM » que 3 PM sont perdus | Retrait pleinement esquivé vs confirmé |
| Critiques, dégâts critiques, hasard | Variance et coups décisifs | Prédire automatiquement un critique sur le meilleur jet | Même action, deux résultats légaux |
| Vitalité, bouclier et soins | Survie et maintien des préparations | Ignorer les boucliers temporaires ou le statut Insoignable | Buff expiré, état Insoignable |
| Initiative et ordre des tours | Bénéfice temporel d'une synergie | Faire bénéficier le Iop d'un placement qui arrive après lui | P→I vs I→P |
| Invocations maximales et occupation | Osamodas, Sadida, Steamer, Roublard | Créer une entité au-delà de la limite | Une place restante vs cap atteint |
| Rangs/variantes et cooldown | Légalité du sort | Combiner deux variantes exclusives ou lancer un sort avant son déblocage | Même build, variante remplacée |
| Identité de l'acteur et de la cible | Challenges, passifs et buffs | Attribuer au propriétaire le coup réellement porté par une invocation | Compagnon attaquant X vs son propriétaire |
| États et déclencheurs | Téléfrag, Infection, Masques, marks | Garder un état après consommation/désenvoûtement | Sort exécuté avant/après retrait de l'état |

## 3. Quatre paliers pédagogiques

Ces paliers servent à comparer l'évolution des *questions* à poser, pas à imposer des builds sans preuve.

| Palier illustratif | Objectif de recherche | Risque principal | Exemple de fiche à reprendre |
| --- | --- | --- | --- |
| Découverte (1–50) | Sorts débloqués, coût réel, capacité à se déplacer/taper sans préparation longue | Guide niveau 200 inaccessible au niveau 30 | Osamodas Tofu, classes Crâ/Iop |
| Progression (51–100) | Modifications de rang, premier soutien utilisable, seuil de survie | Confondre effet bas et haut rang, variante absente | Eniripsa, Sacrieur, Féca |
| Confirmé (101–150) | Coordination d'un duo/trio, invocations, entrave, nouveaux modes | Manque de portée/PA pour un tour théorique | Xélor, Huppermage, Steamer |
| Haut niveau (151–200) | Fenêtres de burst, résistances boss, variants et synergies 4+ | Reprendre chiffres de vidéo 2025 pour 3.7, coûts de préparation | Iop Colère, Roublard, donjons 3.7 |

Avant d'annoncer un coût, vérifier que le **rang concerné** et la variante choisie sont disponibles ; une case `sort inconnu` reste Unknown, elle n'est pas remplie depuis un build pris au hasard.

## 4. Trois exercices concrets à lire avec leur fiche

### A — Contrôle de mobilité : Enutrof + Crâ

Le Crâ veut rester à distance et le monstre peut atteindre sa cellule au prochain tour. L'Enutrof annonce deux retraits « jusqu'à -3 PM » et « jusqu'à -2 PM » dans [M16](M16/RAPPORT.md), mais l'esquive peut laisser le monstre mobile. Tester trois états de fin de tour : **retrait nul**, **partiel**, **complet**, calculer les cellules ennemies encore atteignables dans chacun, puis décider où le Crâ finit son tour. Ne jamais entrer `ennemi PM=1` avant d'avoir observé la perte réelle.

### B — Collision : Forgelance + placeur

Un placeur peut rapprocher un ennemi du mur puis un Forgelance tente de le repousser. [M10](M10/RAPPORT.md) montre pourquoi les dommages directs et les dommages de poussée doivent rester distincts. Comparer l'état avant et après placement avec **mêmes** PA/PM, puis changer seulement la résistance de poussée ennemie. Une vidéo de build vantant « 700 do pou » ne prouve aucun jet spécifique.

### C — Burst : Iop + Pandawa + Éliotrope + Féca

[M14](M14/RAPPORT.md) propose une charge de Colère qui revient plusieurs tours après le premier lancer, et [M08](M08/RAPPORT.md) décrit le rôle de préparation des alliés. La ressource la plus importante n'est pas seulement 12 PA : c'est **la survie du Iop jusqu'à la fenêtre**, une case au contact de la cible, le timing du placement, les buffs encore actifs, la résistance du monstre et l'absence d'invulnérabilité. Comparer le budget de Bond + Colère à la situation où le Pandawa a déjà positionné l'ennemi ; ne pas confondre les tours de préparation avec une seule action.

## 5. Changer de patch change parfois le résultat

Le résumé de [MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) signale une réduction de l'esquive PA des monstres et de nombreuses faiblesses de résistance poussée ; les données du comparateur [Back from AFK](https://backfromafk.com/) montrent aussi les sorts, objets et panoplies modifiés. Une team « contrôle PA » ou « dommages de poussée » peut donc gagner ou perdre de la valeur sans changer ses quatre classes. Pour une étude rigoureuse, enregistrer `gameBuild` sur **tous** les paramètres et ne pas supposer que le build vidéo est le même.

## 6. Contrat d'entrée pour une simulation de composition

```text
team:
  fighters:
    - actorId, classId, level, spellRanks, chosenVariants
      currentAP, currentMP, range, characteristics, resistances
      tackle, dodge, pushDamage, pushResistance, summonsMax, initiative
      life, shields, knownStates, cooldowns
  companions: actorId, template, control, initiative, spells
  summons: actorId, ownerId, template, cells, alive, autonomousOrControlled
enemies:
  actorId, template, resistances, pushResistance, apDodge, mpDodge, states, threats
combat:
  build, map, obstacles, los, objectives, bossPhase, challenges, turnOrder
evidence:
  sourceUrl, sourceVersion, transcriptTimestamp, serverEventId, status
```

L'entrée `unknown` ou non attestée d'une statistique critique interdit une prédiction trop précise. Un estimateur peut donner une fourchette, mais ne doit pas la transformer en résultat serveur.

## 7. Que mesurer pour comparer deux panoplies ou deux équipes ?

Réutiliser **le même plateau et le même adversaire**, modifier une seule variable : équipement, ordre d'initiative, élément de la cible, état d'invocation, variante ou niveau. Mesurer la probabilité/possibilité d'atteindre les cases, les PA restant après le combo, les PV sauvés, les attaques adverses évitées, le nombre de tours nécessaires et les échecs de challenge. Les résultats doivent être attachés à des replays/test réels lorsque le projet passe en phase de validation ; cette fiche ne prétend pas avoir effectué ces mesures.

