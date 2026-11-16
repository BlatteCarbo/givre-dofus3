# M18 — Fiche détaillée Osamodas : invocations indépendantes, boost Piqûre Motivante et capacité

## Données et état nécessaires

```text
S_t = { summonerId, summonId, templateId, cell, alive, states, ap/mp per actor, owner, initiative, capAndOccupancy, buffSourceAndExpiry }
A = (lanceur, sortId, rang, variante, cellule, cibleId, coût)
résolution = (acceptation, événements autoritaires, nouveau S_t+1)
```

Les gains et consommations ne sont comptabilisés qu'après confirmation serveur. Utiliser une valeur **Unknown** dès qu'un état/une position ne peut être établie. La fidélité de la fiche dépend de [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/osamodas/) et de sa comparaison à [3.7](https://backfromafk.com/classes/osamodas/depuis-3-2/).

## Quatre règles de référence

**M18-R01 —** Une invocation possède son identifiant, ses PV, sa case, son propriétaire et son ordre de tour propres. *Limite :* Un acteur invoqué ne doit pas être confondu avec son invocateur pour l'identité d'une action.

**M18-R02 —** Piqûre Motivante a des effets différents selon cible invocation/allié, décrits en 3.7.12. *Limite :* Les gains AP/MP doivent être attribués au bénéficiaire observé et à la bonne durée.

**M18-R03 —** Les familles de créatures et leurs variantes ont des caractéristiques/actions distinctes. *Limite :* Une invocation générique sans template/rang ne suffit pas pour simuler ses actions.

**M18-R04 —** La saturation d'une carte par invocations peut empêcher les déplacements et lignes de vue alliés. *Limite :* La présence de plus d'entités n'est pas automatiquement un gain tactique.

## Micro-combo positif commenté

T1 : choisir une case d'invocation où la créature pourra protéger ou agir, vérifier la capacité ; T2 : appliquer un bonus au bon bénéficiaire s'il vivra assez longtemps pour le rentabiliser ; T3 : calculer son action dans son propre tour à partir d'un état serveur actualisé.

**État initial à renseigner :** niveau et variante exacts, nombre de PA/PM réel, lignes de vue et cases, positions des invocations, objets/bonus, états de toutes les cibles et séquence d'initiative. Les déplacements/bonus sont conditionnels : l'exemple est un scénario proposé.

**Observation attendue :** Case libre et limite non atteinte ; l'invocation confirmée dispose de son propre ID et son attaque au tour suivant est attribuée à la créature.

## Deux contre-scénarios indépendants

1. **État manquant / action refusée :** Même carte, mais plafond atteint ou case occupée ; aucune créature fantôme ne peut agir.
2. **Temps, danger ou cible modifiée :** Un monstre de zone détruit les deux invocations avant le prochain tour ; le plan doit recalculer les coûts de remplacement.

La vérification compare l'état produit par des événements réels et l'état prévu ; elle ne peut pas se limiter à « le sort était lançable ».

## Valeur pour les équipes

Osamodas + Ouginak : les entités peuvent influencer l'entourage de la cible, piste HUZ à vérifier ; Osamodas + Pandawa + Iop : placement puis invocations/bonus utiles au finisseur ; Osamodas + Féca + Enutrof + Crâ : hypothèse de contrôle avec espace à gérer. Pour chaque composition, écrire les actions par tour et par acteur, le bénéficiaire, la ressource consommée, la menace supprimée et le repli lorsque l'initiative change. Ces compositions sont des candidats, sauf mention explicite d'un avis de créateur horodaté.

## Provenance et limites

[Métadonnées d'un gameplay Osamodas Eau](https://www.youtube.com/watch?v=v8KbySowKsM) identifiées (chapitres 00:00, 06:32, 10:48, 15:40), mais sans transcription MCP ni captures du plateau pour cette mission. Éviter d'en déduire des règles de classe.

- Statuts : `corrobore-source` pour les textes de la base 3.7.12, `declare-video` seulement pour passages de vidéo effectivement transcrits, `hypothese` pour nos fixtures de test.
- Version 3.7.13.17 et ordre exact des événements à confirmer avant code.
- Aucun test xUnit ou résultat de session réelle exécuté dans ce dossier.

