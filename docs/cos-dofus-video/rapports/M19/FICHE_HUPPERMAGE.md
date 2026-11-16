# M19 — Fiche détaillée Huppermage : états élémentaires, combinaisons de deux éléments et runes

## Données et état nécessaires

```text
S_t = { targetId→elementState, elementHistory, rune[owner,cell,element,duration], cooldowns, ap/mp, targetPosition, comboEffectVersion }
A = (lanceur, sortId, rang, variante, cellule, cibleId, coût)
résolution = (acceptation, événements autoritaires, nouveau S_t+1)
```

Les gains et consommations ne sont comptabilisés qu'après confirmation serveur. Utiliser une valeur **Unknown** dès qu'un état/une position ne peut être établie. La fidélité de la fiche dépend de [Grimoire 3.7.12](https://dofus-grimoire.fr/classes/huppermage/) et de sa comparaison à [3.7](https://backfromafk.com/classes/huppermage/depuis-3-0/).

## Quatre règles de référence

**M19-R01 —** Une cible reçoit un état élémentaire lié à l'action réellement résolue ; la combinaison ne se déclenche qu'à la succession conforme de deux éléments. *Limite :* Conserver par cible l'état précédent, pas un drapeau global Huppermage.

**M19-R02 —** Le couple d'éléments détermine la catégorie d'effet ; les six couples doivent être discriminés. *Limite :* Ne pas attribuer un retrait PM universel à toute combinaison.

**M19-R03 —** Les runes sont des ressources de terrain distinctes des états élémentaires sur combattants. *Limite :* Une rune expirée, déplacée ou consommée ne doit plus participer à l'effet.

**M19-R04 —** La version 3.7 a modifié beaucoup de sorts Huppermage ; les combos 2025 doivent être contrôlés. *Limite :* Ne pas figer des dégâts/portées tirés des vidéos anciennes.

## Micro-combo positif commenté

T1 : appliquer un premier état élémentaire à la cible identifiée ; T1/T2 : déclencher un second élément différent si la cible, la portée et les ressources le permettent ; ensuite relever l'effet supplémentaire serveur et conserver séparément l'éventuelle rune.

**État initial à renseigner :** niveau et variante exacts, nombre de PA/PM réel, lignes de vue et cases, positions des invocations, objets/bonus, états de toutes les cibles et séquence d'initiative. Les déplacements/bonus sont conditionnels : l'exemple est un scénario proposé.

**Observation attendue :** Deux effets de deux éléments différents sont confirmés sur la même cible avant expiration ; une combinaison spécifique est attendue selon le tableau du patch.

## Deux contre-scénarios indépendants

1. **État manquant / action refusée :** Même séquence avec première application refusée ou cible changée : aucun effet de combinaison ne peut être inventé.
2. **Temps, danger ou cible modifiée :** La cible disparaît/est désenvoûtée avant le deuxième sort ou un ennemi tue la tourelle de l'allié durant l'installation du terrain.

La vérification compare l'état produit par des événements réels et l'état prévu ; elle ne peut pas se limiter à « le sort était lançable ».

## Valeur pour les équipes

Huppermage + Steamer : contrôle et tourelles ; +Sadida : endurance et propagation ; +Zobal : protection ; Huppermage + Steamer + Féca : compenser le tour de préparation ; Sram + Féca + Sadida + Huppermage : composition commentée dans VID-TEAM4-01. Pour chaque composition, écrire les actions par tour et par acteur, le bénéficiaire, la ressource consommée, la menace supprimée et le repli lorsque l'initiative change. Ces compositions sont des candidats, sauf mention explicite d'un avis de créateur horodaté.

## Provenance et limites

[Xawpiya Huppermage duos 2026](https://www.youtube.com/watch?v=TmHIdyxtGmY) : sous-titres 01:05–03:15 (Steamer), 03:15–05:15 (Sadida), 05:15–07:05 (Zobal), `declare-video`. [Tenmalexis, tutoriel Huppermage 2025](https://www.youtube.com/watch?v=ACIK1JFdttc) : chapitres détaillés dans la description, mais transcription bloquée par IP ; seules les métadonnées/chapitres sont utilisables.

- Statuts : `corrobore-source` pour les textes de la base 3.7.12, `declare-video` seulement pour passages de vidéo effectivement transcrits, `hypothese` pour nos fixtures de test.
- Version 3.7.13.17 et ordre exact des événements à confirmer avant code.
- Aucun test xUnit ou résultat de session réelle exécuté dans ce dossier.

