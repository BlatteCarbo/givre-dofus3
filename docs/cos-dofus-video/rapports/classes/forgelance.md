# Forgelance — fiche de cadrage stratégique et technique

**Classe** : Forgelance · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Zones d'attaque, lance et placement dynamique. Source d'orientation de classe : [Dofus Grimoire — Forgelance](https://dofus-grimoire.fr/classes/forgelance/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Suivre la présence et la position de la lance ainsi que les zones de sorts rattachées à son état. Variables minimales à observer : **lance visible/position confirmée, cellules libres, géométrie de zone, bonus conditionnels, tour du lanceur**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Placer une zone accessible aux ennemis regroupés par un allié, vérifier que la lance et ses zones atteignent réellement leur cible. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Pandawa — création d'angles et regroupement selon VID-DUO19-01. Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Pandawa + Forgelance + Féca — protection du placeur et zones. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Pandawa + Forgelance + Enutrof + Eniripsa — hypothèse contrôle/survie. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Placer une zone accessible aux ennemis regroupés par un allié, vérifier que la lance et ses zones atteignent réellement leur cible. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

L'allié a déplacé la cible hors de l'aire d'effet avant l'action du Forgelance. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Cible déplacée sur/hors de l'aire de frappe avec mêmes autres paramètres ; comparer disponibilité et effet. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

aires, collision, position de la lance, téléportation, orientation, conditions des zones. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 14:35](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=875s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). Forgelance + Pandawa est sa préférence : le Pandawa regroupe ou bloque la cible pour les zones du Forgelance. Il discute aussi les limites d'Éliotrope + Forgelance en pur duo : les ennemis peuvent exploiter les portails laissés ouverts. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Forgelance, version 3.7.12](https://dofus-grimoire.fr/classes/forgelance/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Le registre vidéo contient des sections ou compositions correspondantes, à vérifier au lien horodaté.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

