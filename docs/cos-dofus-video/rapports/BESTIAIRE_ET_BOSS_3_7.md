# Bestiaire, donjons et mécaniques de boss — préparation 3.7

**Date : 4 novembre 2026.** Cette page commence la couverture des boss en complément de [M11 Gourlo](M11/RAPPORT.md). Elle documente une famille de **changements de règles signalés dans l'article [Mise à jour 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html)** (publication octobre 2026), ainsi que les [fiches de boss 3.7.12](https://dofus-grimoire.fr/boss/). Les effets ci-dessous sont des descriptions publiées, **pas des observations serveur et pas des tests réussis**. Toute valeur reprise doit être confirmée pour le sous-patch 3.7.13.17 et les variantes spécifiques du donjon.

## Pourquoi une IA doit comprendre le boss

La variable `boss.invulnerable` ne suffit pas. Les conditions de levée d'immunité peuvent dépendre d'un **tour**, d'un **seuil de PV**, d'une **case**, de l'**identité de l'attaquant**, de l'**ordre de mort**, d'un **compagnon invoqué**, d'un **état qui se retire**, ou d'un **événement sur un allié**. Un même sort reste légal mais inutile pendant une phase, dangereux sur une autre, voire contraire au succès recherché.

### Changement de 3.7 : registre d'alertes par mécanique

| Donjon/monstre | Changement rapporté en 3.7 | État ou événement à relever | Test négatif indispensable | Source |
| --- | --- | --- | --- | --- |
| Capitaine Méno | Un glyphe indique la prochaine case d'apparition d'ennemis | marque de préapparition, case, tour d'invocation | ne pas traiter le glyphe comme un dégât immédiat | [MAJ 3.7, Donjons](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) |
| Dantinéa | Glyphes annonçant l'apparition des Grokillages | positions des glyphes et entités après apparition | anticipation d'une case seulement « supposée » | même source |
| Founoroshi | Bloquer le boss un tour entier ne provoque plus le même OS au contact ; les glyphes de feu ne désactivent plus les portails Éliotrope | états de blocage, durée, glyphes, statut des portails | ancien plan supprimant les portails à tort | même source |
| Koutoulou | Frapper un allié peut faire diminuer ses états de Folie | état Folie individuel, cible et action qui le modifie | dommages alliés sans effet de réduction observé | même source |
| Krobes et Protozorreur | Des délais d'élimination ont été raccourcis (Krobe 3 tours, Protozorreur 6 au lieu de 5/10) | compteurs et états de cible, mort programmée, phase | poursuivre l'ancien délai et rater la fenêtre | même source |
| Torkélonia | Cycles lunaires modifiés ; notamment au premier et dernier quartier de nouvelles défenses/dommages | phase du tour, bouclier, résistances, prochaine phase | attaque prévue sur ancienne invulnérabilité | même source |
| Bethel | Dagon tué du mauvais côté peut entraîner une nouvelle phase avec Monolithe ressuscité | équipe du Dagon, décès, Monolithe vivant, phase | croire le combat fini après décès du Dagon | même source |
| Solar | Changement d'états au tour global en Songes, et non au tour propre de Solar | globalTurn, bossTurn, phase | faire changer d'état au mauvais acteur | même source |
| Vortex | Comportements de résurrection et bonus liés aux morts modifiés | tour et heure de mort, statut ressuscité/corrompu | inventer une résurrection désormais interdite | même source |
| Sylargh | États des ennemis ressuscités et dommages de poussée ajustés | résurrection, état indéplaçable, cible, collision | tenter un dispel qui ne peut plus retirer l'état | même source |
| Zarbivores / Vénérable Endormi | Limites d'invocation et changements de l'état Apathique/Alerté | état par entité, cooldown global, naissance | invoquer un bébé de plus malgré cooldown global | même source |
| Corrompus | Certains effets alliés ne transmettent plus de maladie | cible alliée/ennemie, état maladie, action | inférer une maladie d'un retrait PM allié | même source |
| Général PvM | Esquive PA des monstres réduite et faiblesses en dommages de poussée ajoutées à de nombreux monstres | esquive réelle, résistance de poussée, effet observé | maintenir des hypothèses d'avant 3.7 | même source |

**Ce qu'il faut entendre par « rapporté » :** l'article de mise à jour est un recoupement documentaire, les nouveaux chiffres ne sont pas directement mesurés sur le serveur. Les mécaniques exactes de levée d'invulnérabilité restent à rechercher boss par boss dans le bestiaire 3.7.12 et les événements de combat.

## Typologie de tests à réutiliser

1. **Phase temporelle** : un boss est invulnérable jusqu'à un tour, ou change de phase au tour global ; différencier initiative du boss et début de ronde.
2. **Case/zone** : une entité est adjacente au boss à un instant précis ; la simple traversée d'une case ne suffit pas sans preuve.
3. **Invocation associée** : l'entité à protéger/éliminer est morte avant l'action décisive.
4. **Résistance conditionnelle** : un type de dommage ou élément reste bloqué malgré une fenêtre ouverte dans un autre élément.
5. **Effet sur allié** : un sort allié retire un état dangereux, mais le challenge interdit ou pénalise l'action.
6. **Cycle de PV** : franchir un seuil modifie la phase, annulant la séquence de dégâts préprogrammé.
7. **Succès et challenges** : achever les ennemis dans un ordre contraire au succès peut rendre une tactique pourtant gagnante non pertinente.

## Fiche boss minimale

```text
bossId / monsterTemplate / gameBuild
globalTurn / bossTurn / nextActivation
flags: invulnerable, invisible, unmovable, dead, shielded
states: name, sourceActor, targetActor, duration, dispellable?
entities: summonId, owner, cell, alive, initiative
map: walkable, lineOfSight, walls, portals, glyphs
challenge: id, conditions, actors, targets, alreadyCompleted
evidence: eventId, timestamp, previousState, nextState, source
```

Chaque mécanique est liée à un événement précis. Lors d'une contradiction vidéo/documentation/client, l'ordre de priorité est **trace serveur compatible avec la version → données du client versionné → notes de patch officielles → guides documentés → conseils des créateurs**. Ne pas inventer les données manquantes du côté serveur.

## Scénarios de comparaison par composition

- **Solo** : un joueur doit préserver sa survie pendant la phase invulnérable plutôt que dépenser ses gros sorts à tort.
- **Duo** : un placeur déplace un tonneau/boss, un attaquant exploite une fenêtre ; inversion d'initiative invalide le combo.
- **Trio** : un soutien protège l'acteur qui déclenche le délock, l'attaquant finit la cible, mais le soutien peut aussi bouger un ennemi au mauvais moment.
- **Quatuor** : des objectifs indépendants (tonneau, invocation dangereuse, fenêtre de dégâts, challenge) peuvent occuper chaque personnage ; tester le gain vs deux duos.
- **5–8** : la carte encombrée et les initiaves imbriquées multiplient les possibilités de collisions et contres. Réserver des cases et confirmer les cibles ; ne pas attribuer automatiquement le coup fatal à son propriétaire plutôt qu'à son invocation.

## Missions d'approfondissement à ouvrir après M11/M25

Au minimum un donjon par type : invulnérabilité au temps, au placement, à un dégât de poussée, à une action de tonneau, à un état de cible, à un ordre de mort, à une invocation fragile, à un compteur de dégâts reçus, à une phase liée aux PV, à un succès spécial. Les noms précis exigent une fiche de mécanique correspondante, pas une généralisation depuis les guides anciens.

