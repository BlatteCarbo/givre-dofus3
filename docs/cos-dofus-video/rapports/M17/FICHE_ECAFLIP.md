# M17 — Fiche détaillée Ecaflip : main de cartes, Pile ou Face et effets conditionnels

## Données et état nécessaires

```text
S_t = { main: [rank, suit, origin], max cards, activeSide(Pile/Face/Unknown), actionConfirmed, buffs, target, apRemaining, pendingEffects }
A = (lanceur, sortId, rang, variante, cellule, cibleId, coût)
résolution = (acceptation, événements autoritaires, nouveau S_t+1)
```

Les gains et consommations ne sont comptabilisés qu'après confirmation serveur. Utiliser une valeur **Unknown** dès qu'un état/une position ne peut être établie. La fidélité de la fiche dépend de [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/ecaflip/) et de sa comparaison à [3.7](https://backfromafk.com/classes/ecaflip/depuis-3-0/).

## Quatre règles de référence

**M17-R01 —** Chaque sort élémentaire concerné peut alimenter une Main avec une carte d'élément et de rang associés au coût ; limite annoncée de quatre cartes. *Limite :* Ne jamais générer une carte sur une action refusée ou sur un sort hors règle du passif.

**M17-R02 —** Pile ou Face modifie les conditions des sorts Lapement, Mésaventure, Destin d'Ecaflip et Esprit Félin selon le guide 3.7.12. *Limite :* Ce statut ne prouve pas le tirage exact, le détail des états ni le changement d'un nouveau patch.

**M17-R03 —** Rekop dépend de la combinaison actuelle de la Main et la défausse, selon la base communautaire 3.7.12. *Limite :* La valeur d'une main ne peut pas être ramenée à son nombre de cartes.

**M17-R04 —** Château de Cartes a été modifié entre 3.0 et 3.7, notamment une augmentation de son coût PA. *Limite :* Les vidéos pré-modification ne sont pas des preuves de la nouvelle valeur.

## Micro-combo positif commenté

T1 : tenter un sort élémentaire valide, enregistrer la carte seulement après le résultat confirmé ; T2 : fixer l'état Pile/Face avant un effet conditionnel lorsque cela apporte une valeur réelle ; T3 : comparer la Main actuelle à l'action qui la consomme, y compris le tour de défausse.

**État initial à renseigner :** niveau et variante exacts, nombre de PA/PM réel, lignes de vue et cases, positions des invocations, objets/bonus, états de toutes les cibles et séquence d'initiative. Les déplacements/bonus sont conditionnels : l'exemple est un scénario proposé.

**Observation attendue :** Cible éligible ; le sort élémentaire est confirmé puis la Main s'enrichit. L'action de consommation est légale et retire les cartes attendues d'après le serveur.

## Deux contre-scénarios indépendants

1. **État manquant / action refusée :** Même plateau, mais la première demande de sort échoue : le moteur n'ajoute pas de carte et ne prévoit pas une Main gagnante fictive.
2. **Temps, danger ou cible modifiée :** Un combat court se termine avant d'accumuler une Main utile ; une issue de hasard inconnue fait préférer une décision robuste plutôt qu'un plan unique.

La vérification compare l'état produit par des événements réels et l'état prévu ; elle ne peut pas se limiter à « le sort était lançable ».

## Valeur pour les équipes

Écaflip + Crâ : allié distance profite d'une fenêtre de soutien, à évaluer selon durée des boosts ; Écaflip + Féca + Iop : protection / fenêtre offensive / effet de main ; Écaflip + Pandawa + Eniripsa + Iop : hypothèse de mêlée et soutien, non filmée. Pour chaque composition, écrire les actions par tour et par acteur, le bénéficiaire, la ressource consommée, la menace supprimée et le repli lorsque l'initiative change. Ces compositions sont des candidats, sauf mention explicite d'un avis de créateur horodaté.

## Provenance et limites

Aucun tutoriel récent à transcription horodatée exploitable n'a été collecté spécifiquement pour M17. Le chapitre Ecaflip du [guide HUZ fin 2025](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1213s) est repéré par le sommaire ; passage non relu dans cette mission.

- Statuts : `corrobore-source` pour les textes de la base 3.7.12, `declare-video` seulement pour passages de vidéo effectivement transcrits, `hypothese` pour nos fixtures de test.
- Version 3.7.13.17 et ordre exact des événements à confirmer avant code.
- Aucun test xUnit ou résultat de session réelle exécuté dans ce dossier.

