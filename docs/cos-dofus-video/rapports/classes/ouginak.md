# Ouginak — fiche de cadrage stratégique et technique

**Classe** : Ouginak · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Traque de cible, mobilité et pression rapprochée. Source d'orientation de classe : [Dofus Grimoire — Ouginak](https://dofus-grimoire.fr/classes/ouginak/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Suivre la désignation de proie, les états spécifiques et l'occupation des cases autour de la cible. Variables minimales à observer : **état de proie, jauge/état de transformation s'il existe pour la version, mobilité, cible à portée, alliés voisins**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Identifier une cible accessible, rapprocher les acteurs utiles, attaquer seulement après confirmation du placement et de l'état requis. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Osamodas — invocations autour de la cible, piste VID-DUO19-01. Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Ouginak + Osamodas + Féca — mêlée protégée avec invocations. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Ouginak + Osamodas + Eniripsa + Pandawa — hypothèse mêlée structurée. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Identifier une cible accessible, rapprocher les acteurs utiles, attaquer seulement après confirmation du placement et de l'état requis. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

La cible se téléporte avant la phase offensive ou le nombre d'entités autour d'elle a changé. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Cible entourée par des entités confirmées puis mêmes cases vidées ; vérifier si le sort conditionnel diffère réellement. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

occupation, invocation, proie, mobilité, corps-à-corps, état/tacle. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 12:46](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=766s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). L'auteur suggère Osamodas pour profiter de créatures autour d'une cible, et Féca comme partenaire de mêlée protecteur. Il avertit de la lenteur ou du risque d'un duo presque exclusivement rapproché. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Ouginak, version 3.7.12](https://dofus-grimoire.fr/classes/ouginak/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Aucun passage dédié n'a encore été évalué visuellement pour cette classe ; utiliser l'index des 19 sections HUZ pour commencer.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

