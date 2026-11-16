# M20 — Fiche détaillée Steamer : tourelles, états d'Évolution et attaques autonomes

## Données et état nécessaires

```text
S_t = { turretId, template, evolutionLevel, alive, cell, summonTurns, cooldown, targetRangeAndLos, ownerAndTeam, apGainEvents }
A = (lanceur, sortId, rang, variante, cellule, cibleId, coût)
résolution = (acceptation, événements autoritaires, nouveau S_t+1)
```

Les gains et consommations ne sont comptabilisés qu'après confirmation serveur. Utiliser une valeur **Unknown** dès qu'un état/une position ne peut être établie. La fidélité de la fiche dépend de [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/steamer/) et de sa comparaison à [3.7](https://backfromafk.com/classes/steamer/depuis-3-0/).

## Quatre règles de référence

**M20-R01 —** Chaque tourelle est une invocation persistante qui possède identité, position, état d'évolution et actions propres. *Limite :* La tourelle ne peut pas agir une fois morte ; sa nouvelle invocation recommence avec un nouvel état.

**M20-R02 —** Évolution change le jeu d'actions ou les bonus de la tourelle selon son palier. *Limite :* Le palier ne se déduit pas du nombre de tours écoulés si la montée a été refusée.

**M20-R03 —** Des sorts du Steamer peuvent interagir avec la présence/position de tourelles pour générer PA, soin ou contrôle. *Limite :* Le gain de PA dépend des conditions exactes du sort et doit être confirmé.

**M20-R04 —** Un mur ou une tourelle bloquant la ligne de vue/cheminement d'un allié peut réduire l'utilité de la stratégie. *Limite :* Comparer bénéfices offensifs et coût d'occupation.

## Micro-combo positif commenté

T1 : poser une tourelle à une case utile ; T2 : la faire évoluer si elle est vivante et si la survie est plausible ; T3 : déclencher/observer son sort propre puis replanifier selon la nouvelle portée et les obstacles.

**État initial à renseigner :** niveau et variante exacts, nombre de PA/PM réel, lignes de vue et cases, positions des invocations, objets/bonus, états de toutes les cibles et séquence d'initiative. Les déplacements/bonus sont conditionnels : l'exemple est un scénario proposé.

**Observation attendue :** Tourelle confirmée vivante, évolution confirmée, cible dans sa portée légale ; les effets sur cette cible sont attribués à l'acteur/tourelle correspondant.

## Deux contre-scénarios indépendants

1. **État manquant / action refusée :** Tourelle détruite entre pose et évolution ; un ancien niveau d'Évolution et les dégâts attendus n'existent plus.
2. **Temps, danger ou cible modifiée :** L'adversaire force un déplacement de la tourelle ou se met hors de ligne ; les PA d'investissement ne sont pas récupérés avant la fin du combat.

La vérification compare l'état produit par des événements réels et l'état prévu ; elle ne peut pas se limiter à « le sort était lançable ».

## Valeur pour les équipes

Steamer + Huppermage : duo documenté avec contrôle progressif dans VID-HUPPER-DUO-01 ; Steamer + Féca + Pandawa : protéger les tourelles et ouvrir les angles ; Steamer + Huppermage + Féca + Enutrof : hypothèse de jeu prolongé. Pour chaque composition, écrire les actions par tour et par acteur, le bénéficiaire, la ressource consommée, la menace supprimée et le repli lorsque l'initiative change. Ces compositions sont des candidats, sauf mention explicite d'un avis de créateur horodaté.

## Provenance et limites

[Xawpiya duos Huppermage 2026](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=65s) 01:05–03:15 décrit le Steamer comme allié de contrôle, transcription MCP consultée. Vidéos Capitemflam 2026 sur le Steamer repérées par index secondaire mais URL/frames non établies : aucune preuve mécanique n'en est tirée.

- Statuts : `corrobore-source` pour les textes de la base 3.7.12, `declare-video` seulement pour passages de vidéo effectivement transcrits, `hypothese` pour nos fixtures de test.
- Version 3.7.13.17 et ordre exact des événements à confirmer avant code.
- Aucun test xUnit ou résultat de session réelle exécuté dans ce dossier.

