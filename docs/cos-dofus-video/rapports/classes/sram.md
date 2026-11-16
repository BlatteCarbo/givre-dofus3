# Sram — fiche de cadrage stratégique et technique

**Classe** : Sram · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Pièges, invisibilité, déplacements forcés et dégâts déclenchés. Source d'orientation de classe : [Dofus Grimoire — Sram](https://dofus-grimoire.fr/classes/sram/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Gérer pose, activation, déclenchement et retrait séparément ; position invisible et cible présumée restent incertaines. Variables minimales à observer : **identifiants de pièges, cellules, activation, propriétaire, durée, révélations, invisibilité, ordre de déclenchement**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Préparer une séquence de pièges reliés par mouvement forcé, attirer/pousser une cible, replanifier au premier trigger ou refus. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Féca — temporisation ; Roublard — préparation de mur ; Xélor — placements (VID-DUO19-01). Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Sram + Roublard + Enutrof — préparation et contrôle. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Sram + Féca + Sadida + Huppermage — VID-TEAM4-01. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Préparer une séquence de pièges reliés par mouvement forcé, attirer/pousser une cible, replanifier au premier trigger ou refus. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

Obstacle ou piège manquant interrompt la chaîne, ou supposition que le trigger a détruit automatiquement la marque. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Deux pièges et mouvement forcé dans ordre A-B puis B-A ; relever la séquence et l'éventuelle disparition de marques. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

pièges, trigger, invisibilité, repoussée, collision, état Unknown. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 10:59](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=659s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). Féca est préféré pour immobiliser des ennemis et laisser le temps à un réseau de pièges, mais l'auteur discute aussi Roublard (mur) et Xélor (Téléfrag autour de positions tenues). Ne pas déduire les conditions d'activation d'un piège de ces conseils généraux. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Sram, version 3.7.12](https://dofus-grimoire.fr/classes/sram/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Le registre vidéo contient des sections ou compositions correspondantes, à vérifier au lien horodaté.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

