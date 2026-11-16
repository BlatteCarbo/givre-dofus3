# Xélor — fiche de cadrage stratégique et technique

**Classe** : Xélor · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Téléfrags, échanges de position et séquences de déplacements. Source d'orientation de classe : [Dofus Grimoire — Xélor](https://dofus-grimoire.fr/classes/xelor/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Suivre positions d'origine, tentatives, swaps réellement effectués, états créés/consommés et événements de téléfrag. Variables minimales à observer : **positions de tous acteurs, historique d'actions utiles, états Téléfrag, remboursements PA confirmés, relances et sort-cible**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Choisir un déplacement qui satisfait une condition de création d'état, confirmer l'échange puis employer cet état avant expiration. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Sram — retenir une cible à exploiter, selon VID-DUO19-01. Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Xélor + Pandawa + Féca — placement et protection des échanges. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Xélor + Sram + Enutrof + Féca — hypothèse de contrôle. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Choisir un déplacement qui satisfait une condition de création d'état, confirmer l'échange puis employer cet état avant expiration. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

Cible non déplacée ou obstacle empêchant l'échange ; un téléfrag seulement attendu n'autorise aucun bonus anticipé. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Même sort sur deux plateaux : case finale occupée selon condition vs case vide ; constater si l'état déclenche réellement. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

téléportation, échange, état, remboursement, ordre d'actions, position. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 34:50](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=2090s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). Enutrof est préféré comme partenaire de retrait PM et soutien, car il maintient des ennemis accessibles au Xélor. Féca et placeurs/tanks sont cités aussi, mais peuvent exiger davantage de survie en début de progression. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Xélor, version 3.7.12](https://dofus-grimoire.fr/classes/xelor/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Aucun passage dédié n'a encore été évalué visuellement pour cette classe ; utiliser l'index des 19 sections HUZ pour commencer.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

