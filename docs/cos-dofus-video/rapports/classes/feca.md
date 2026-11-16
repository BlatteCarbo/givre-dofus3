# Féca — fiche de cadrage stratégique et technique

**Classe** : Féca · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Protection, glyphes, altérations et maîtrise de zone. Source d'orientation de classe : [Dofus Grimoire — Féca](https://dofus-grimoire.fr/classes/feca/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Modéliser séparément création, activation, traversée, début/fin de tour et expiration d'un glyphe. Variables minimales à observer : **ID/cases/activation de glyphe, propriétaire, bonus défensifs par cible, durée, ordre de tour et protections**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Poser une zone qui affecte effectivement une cible, préserver l'allié exposé, attendre le déclencheur prouvé avant de scorer les dégâts. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Sram — entrave et temps de préparation du réseau selon VID-DUO19-01. Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Féca + Sadida + Huppermage — contrôle durable. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Pandawa + Enutrof + Crâ + Féca — VID-TEAM4-01. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Poser une zone qui affecte effectivement une cible, préserver l'allié exposé, attendre le déclencheur prouvé avant de scorer les dégâts. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

Attribuer des dégâts de glyphe dès la pose alors que le déclencheur est ultérieur ou inexistant. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Comparer entrée sur le glyphe, immobilité sur le glyphe et sortie ; seul le signal établi par le sort doit produire l'effet. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

glyphes, états, zones, protection, durée, génération des marks, ordre d'effets. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 23:11](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1391s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). L'auteur décrit plusieurs styles Féca (distance, entrave, mêlée) et évoque Pandawa pour placement/protection ainsi que Iop pour un jeu rapproché. La version des sorts et l'orientation élémentaire sont déterminantes ; aucun bonus chiffré de cette ancienne vidéo n'est transposé. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Féca, version 3.7.12](https://dofus-grimoire.fr/classes/feca/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Le registre vidéo contient des sections ou compositions correspondantes, à vérifier au lien horodaté.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

