# M21 — Fiche détaillée Sadida : Arbres, Poupées, Infecté et propagation

## Données et état nécessaires

```text
S_t = { tree[owner,id,cell,leafy,alive,expiry], doll[owner,id,cell,alive], targetInfected[actorId,source,expiry], range, zone }
A = (lanceur, sortId, rang, variante, cellule, cibleId, coût)
résolution = (acceptation, événements autoritaires, nouveau S_t+1)
```

Les gains et consommations ne sont comptabilisés qu'après confirmation serveur. Utiliser une valeur **Unknown** dès qu'un état/une position ne peut être établie. La fidélité de la fiche dépend de [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/sadida/) et de sa comparaison à [3.7](https://backfromafk.com/classes/sadida/depuis-3-0/).

## Quatre règles de référence

**M21-R01 —** Les Arbres et Poupées sont des entités/cases du terrain, avec propriétaire, capacité et état vivant. *Limite :* Si l'arbre meurt entre pose et déclenchement, sa zone ne doit plus être attendue.

**M21-R02 —** Infecté est un état par cible avec source et expiration ; certains sorts peuvent en appliquer/propager les effets. *Limite :* Ne pas considérer tous les monstres d'un groupe comme infectés par simple proximité.

**M21-R03 —** L'effet indirect sur plusieurs ennemis dépend des règles propres du sort et de l'état de chacun. *Limite :* L'infection n'autorise pas tout sort de dégâts à toucher automatiquement toutes les cibles.

**M21-R04 —** Les invocations occupent des cases, peuvent intercepter des attaques et gêner les alliés. *Limite :* Le score tactique doit intégrer également leur exposition et leurs coûts.

## Micro-combo positif commenté

T1 : poser Arbre sur case contrôlée ou infecter une cible ; T2 : propager l'état si les cibles éligibles et positions sont maintenues ; T3 : utiliser un sort dont l'effet sur Infecté est attesté, puis mettre à jour les cibles touchées individuellement.

**État initial à renseigner :** niveau et variante exacts, nombre de PA/PM réel, lignes de vue et cases, positions des invocations, objets/bonus, états de toutes les cibles et séquence d'initiative. Les déplacements/bonus sont conditionnels : l'exemple est un scénario proposé.

**Observation attendue :** Deux cibles E1/E2 portent Infecté confirmé ; le sort à propagation prévu dans la version 3.7 est légal, la résolution sur chacune est consignée séparément.

## Deux contre-scénarios indépendants

1. **État manquant / action refusée :** E2 n'est plus Infecté ou l'Arbre propagateur est mort ; E2 ne reçoit pas de dégâts fantômes.
2. **Temps, danger ou cible modifiée :** L'ennemi élimine les poupées/arbres en zone, ou le combat se termine avant la mise en place.

La vérification compare l'état produit par des événements réels et l'état prévu ; elle ne peut pas se limiter à « le sort était lançable ».

## Valeur pour les équipes

Sadida + Huppermage : contrôle et soutien durable (VID-HUPPER-DUO-01) ; Sadida + Enutrof + Pandawa : groupe contrôlé et propagation ; Pandawa + Sadida + Enutrof + Féca : composition évoquée VID-TEAM4-01. Pour chaque composition, écrire les actions par tour et par acteur, le bénéficiaire, la ressource consommée, la menace supprimée et le repli lorsque l'initiative change. Ces compositions sont des candidats, sauf mention explicite d'un avis de créateur horodaté.

## Provenance et limites

[Xawpiya Huppermage+Sadida 2026](https://www.youtube.com/watch?v=TmHIdyxtGmY&t=195s), sous-titres 03:15–05:15 consultés (conseil de synergie seulement). [Guide Sadida PvM octobre 2026](https://krozone.fr/blog/sadida-dofus-2026-guide-pvm-solo-1-a-100-sans-lenteur/) consulté, source écrite et non transcription.

- Statuts : `corrobore-source` pour les textes de la base 3.7.12, `declare-video` seulement pour passages de vidéo effectivement transcrits, `hypothese` pour nos fixtures de test.
- Version 3.7.13.17 et ordre exact des événements à confirmer avant code.
- Aucun test xUnit ou résultat de session réelle exécuté dans ce dossier.

