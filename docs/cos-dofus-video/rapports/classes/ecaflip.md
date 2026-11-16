# Ecaflip — fiche de cadrage stratégique et technique

**Classe** : Ecaflip · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Dégâts adaptatifs, aléas et soutien conditionnel. Source d'orientation de classe : [Dofus Grimoire — Ecaflip](https://dofus-grimoire.fr/classes/ecaflip/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Suivre les états, probabilités ou tirages de sorts qui changent le plan légal et la variance de résultat. Variables minimales à observer : **effets aléatoires, ressources/états temporaires, bonus et malus, probabilités documentées, historique de tirages si pertinent**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Construire un plan robuste aux issues possibles d'un effet conditionnel, puis replanifier uniquement après le résultat confirmé. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Eniripsa — explorer le compromis soutien/résistance et rythme offensif. Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Féca + Ecaflip + Eniripsa — survie conditionnelle et pression mesurée. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Ecaflip + Pandawa + Féca + Crâ — hypothèse de compensation des aléas. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Construire un plan robuste aux issues possibles d'un effet conditionnel, puis replanifier uniquement après le résultat confirmé. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

Plan fondé sur une issue favorable d'un tirage non encore observé. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Même état avant tirage, deux résultats distincts ; vérifier que le plan ne suppose jamais l'une des issues. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

aléatoire, critique, état temporaire, dispel, variance, soin, bonus. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 20:13](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1213s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). L'auteur évoque Féca/Enutrof pour ralentir les adversaires et Crâ pour le jeu plus tardif orienté critiques. Il insiste sur le fait que le niveau et l'équipement changent la pertinence du duo. Aucune valeur de réduction des résistances critiques n'est reprise. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Ecaflip, version 3.7.12](https://dofus-grimoire.fr/classes/ecaflip/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Aucun passage dédié n'a encore été évalué visuellement pour cette classe ; utiliser l'index des 19 sections HUZ pour commencer.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

