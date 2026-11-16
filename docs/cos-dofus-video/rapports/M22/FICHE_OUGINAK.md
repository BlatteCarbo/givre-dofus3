# M22 — Fiche détaillée Ouginak : Proie, Rage et contrôle du corps-à-corps

## Données et état nécessaires

```text
S_t = { preyId, preyState, rageCounter, transformedState, adjacentFightersAndSummons, legalAttackRange, resists, selfLifeAndRisk }
A = (lanceur, sortId, rang, variante, cellule, cibleId, coût)
résolution = (acceptation, événements autoritaires, nouveau S_t+1)
```

Les gains et consommations ne sont comptabilisés qu'après confirmation serveur. Utiliser une valeur **Unknown** dès qu'un état/une position ne peut être établie. La fidélité de la fiche dépend de [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/ouginak/) et de sa comparaison à [3.7](https://backfromafk.com/classes/ouginak/depuis-3-0/).

## Quatre règles de référence

**M22-R01 —** La cible Proie est une identité/état spécifique qui peut influencer les dégâts et les soins perçus par les alliés. *Limite :* Un changement de Proie doit invalider les calculs établis sur l'ancienne cible.

**M22-R02 —** Rage et les états dérivés se modifient selon des événements de coups décrits par la source 3.7.12. *Limite :* Le compteur ne doit pas croître sur une attaque refusée ou une cible hors portée.

**M22-R03 —** La transformation change les options de déplacement/portée et le profil de risque. *Limite :* Une routine de corps-à-corps ne doit pas se déclencher sur une cible qui s'est téléportée.

**M22-R04 —** Un nombre d'entités adjacentes peut modifier certains sorts, selon la configuration exacte. *Limite :* Décrire le comptage (allié/invocation/ennemi) et l'instant d'évaluation avant de coder.

## Micro-combo positif commenté

T1 : désigner une Proie et vérifier la cible ; T1/T2 : approcher légalement et observer le compteur ; T2 : utiliser une action au contact ou à courte portée seulement si la proie est encore accessible ; évaluer la survie du corps-à-corps au tour ennemi.

**État initial à renseigner :** niveau et variante exacts, nombre de PA/PM réel, lignes de vue et cases, positions des invocations, objets/bonus, états de toutes les cibles et séquence d'initiative. Les déplacements/bonus sont conditionnels : l'exemple est un scénario proposé.

**Observation attendue :** Proie E reste identifiée et à portée ; le bonus conditionnel éventuel est enregistré uniquement si l'état d'attaque et de Proie est confirmé.

## Deux contre-scénarios indépendants

1. **État manquant / action refusée :** Proie E se déplace hors de portée avant le tour Ouginak ; le bot abandonne son plan de mêlée au lieu de lancer à travers la carte.
2. **Temps, danger ou cible modifiée :** Un monstre à longue portée fuit ; la création d'invocations autour de la cible gêne l'allié ou augmente les dégâts de zone ennemis.

La vérification compare l'état produit par des événements réels et l'état prévu ; elle ne peut pas se limiter à « le sort était lançable ».

## Valeur pour les équipes

Ouginak + Osamodas : idées de placement des créatures autour de la proie discutées dans VID-DUO19-01 ; Ouginak + Osamodas + Féca : mêlée et couverture ; Ouginak + Pandawa + Eniripsa + Féca : hypothèse de mêlée sécurisée. Pour chaque composition, écrire les actions par tour et par acteur, le bénéficiaire, la ressource consommée, la menace supprimée et le repli lorsque l'initiative change. Ces compositions sont des candidats, sauf mention explicite d'un avis de créateur horodaté.

## Provenance et limites

[HUZ, chapitre Ouginak 12:46](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=766s) : transcription française automatique consultée jusqu'à 14:35, évoque Osamodas et Féca, opinion d'auteur `declare-video`. Aucun tour de jeu Ouginak reconstruit visuellement.

- Statuts : `corrobore-source` pour les textes de la base 3.7.12, `declare-video` seulement pour passages de vidéo effectivement transcrits, `hypothese` pour nos fixtures de test.
- Version 3.7.13.17 et ordre exact des événements à confirmer avant code.
- Aucun test xUnit ou résultat de session réelle exécuté dans ce dossier.

