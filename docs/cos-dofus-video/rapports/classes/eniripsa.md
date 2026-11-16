# Eniripsa — fiche de cadrage stratégique et technique

**Classe** : Eniripsa · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Soins, bonus, soutien et dégâts d'appoint. Source d'orientation de classe : [Dofus Grimoire — Eniripsa](https://dofus-grimoire.fr/classes/eniripsa/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Faire correspondre chaque sort au bénéficiaire effectif et à l'attaque/au dommage futur qu'il modifie. Variables minimales à observer : **PV max et actuels, états soignables, bonus actifs, durée et cible des boosts, tour des alliés, contraintes de soin**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Estimer la menace entrante, identifier l'allié risquant de mourir, choisir soin/bouclier/bonus/dégâts selon ce risque. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Pandawa — assurer la survie du placeur en continu. Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Pandawa + Eniripsa + Iop — soutien puis fenêtre de dégâts. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Pandawa + Eniripsa + Crâ + Féca — composition présentée dans VID-TEAM4-01. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Estimer la menace entrante, identifier l'allié risquant de mourir, choisir soin/bouclier/bonus/dégâts selon ce risque. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

Dépenser un tour à renforcer un allié qui a déjà joué ou qui n'a pas de cible accessible avant expiration. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Comparer un allié à faible PV réellement menacé et un allié indemne ; le soin doit avoir un effet marginal observable. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

sursoin, initiative, limites de boost, bouclier, désenvoûtement, challenge de soins. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 08:39](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=519s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). L'Eniripsa peut accompagner une classe qui s'expose et a besoin de soins. L'auteur préfère Sadida dans plusieurs styles, et mentionne Crâ/Enutrof/Féca dans les jeux à distance. Il avertit d'éviter un duo qui dépendrait de la même voie élémentaire contre les mêmes résistances. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Eniripsa, version 3.7.12](https://dofus-grimoire.fr/classes/eniripsa/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Aucun passage dédié n'a encore été évalué visuellement pour cette classe ; utiliser l'index des 19 sections HUZ pour commencer.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

