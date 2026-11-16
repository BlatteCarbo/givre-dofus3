# Iop — fiche de cadrage stratégique et technique

**Classe** : Iop · **Version des références générales** : Dofus Grimoire 3.7.12 ; **version cible à confronter** : 3.7.13.17 cité dans le code local. **Statut global : indexée ; mécaniques et combos à établir par preuve.** Ce dossier est un guide de recherche et de tests, pas une liste de sorts validée.

**Profil à étudier :** Dégâts concentrés, préparation et mobilité offensive. Source d'orientation de classe : [Dofus Grimoire — Iop](https://dofus-grimoire.fr/classes/iop/). Pour les données de sorts, récupérer le rang et la version précise via [OpenDofusDB](https://openddb.dev/) ou les données locales.

## Ce qui fait la différence pour cette classe

Distinguer bonus préparatoires, fenêtres d'attaque, accès mêlée et risque d'exposition après dégâts. Variables minimales à observer : **états de charge, durée des bonus, accès à la cible, PA, mobilité, initiative des placeurs et protecteurs**. La fiche d'un sort seule ne suffit pas : établir l'événement exact qui produit l'effet et l'état après confirmation.

## Tour solo à reconstituer

Préparer si le bénéfice excède l'action immédiate, attendre le placement confirmé et convertir la fenêtre de frappe en élimination effective. Avant chaque décision, noter carte, ligne de vue, ressources, initiative, possibilité de survie au tour ennemi, états et délai jusqu'à l'effet utile. Comparer à une action simple immédiatement disponible.

## Les partenaires et les compositions

- **Duo à examiner :** Pandawa — placement préalable aux grosses attaques ; à vérifier en version cible. Consigner quel acteur prépare l'autre et ce qui se passe lorsque l'ordre d'initiative est inversé.
- **Trio candidat :** Pandawa + Eniripsa + Iop — placement/soutien/finition. Distinguer ressources des trois participants, bonus sur la bonne cible, protection réellement nécessaire.
- **Quatuor candidat ou cité :** Pandawa + Féca + Eniripsa + Iop — hypothèse mêlée sécurisée. Vérifier l'ordre des quatre tours, la durée de préparation, les conflits de cellules et les dommages collatéraux.
- **5–8 acteurs :** vérifier limites du combat, occupation du terrain, éventuels compagnons/invocations, attribution des kills et challenges ; ne pas doubler fictivement les gains d'une même cible.

## Scénario positif — à formaliser en fixture

Situation : au moins deux cases cibles et un allié/ennemi contextuel, avec une seule condition clé changée. **Action hypothétique :** Préparer si le bénéfice excède l'action immédiate, attendre le placement confirmé et convertir la fenêtre de frappe en élimination effective. Attendu *sous réserve de la mécanique exacte* : plan légal, ressources distinctes, état modifié **après** événement observé. Écrire le nom exact du sort et les valeurs seulement après consultation de la version cible.

## Scénario négatif — doit invalider le plan naïf

Lancer une attaque décisive avant le tour du placeur, sans portée ou sur cible devenue inaccessible. Attendu : la transition non observée n'est pas appliquée ; le moteur écarte le combo, revient aux actions légales ou conserve `Unknown` si le serveur ne confirme pas.

**Test discriminant prioritaire :** Comparer même cible avec/sans placement préalable, dégâts estimés et risque de finir au contact. Statut : `hypothese`, proposé, non exécuté.

## Interactions à cartographier

charge, mobilité, zones, tacle, soins, boucliers, boss invulnérables. Pour chaque interaction : précondition, sort et variante, cible et cellule, coût, événement serveur, durée, impact et contre-exemple.

## Lecture ciblée d'un tutoriel sur les duos

**[HUZ REPLAY — Les meilleurs duos par classe, 01:13](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=73s)**, vidéo du 5 décembre 2025 ; transcription horodatée FR automatique inspectée le 4 novembre 2026 ; **statut `declare-video` uniquement** (avis d'auteur, plateau non examiné). L'auteur met en avant Iop + Enutrof pour entrave et placement, Iop + Sram pour placement et autonomie, et Iop + Pandawa si le Panda agit avant le Iop. Il met en garde contre le manque de soin naturel du duo Iop/Pandawa. Les mécaniques et valeurs de sorts ont évolué jusqu'à 3.7 : revalider les conditions exactes avant de convertir cette suggestion en logique de simulation.

## Sources, limites et travail restant

- [Fiche générale Iop, version 3.7.12](https://dofus-grimoire.fr/classes/iop/) — source tierce, utile pour découvrir les sorts et vérifier les variantes ; les chiffres sont à recouper avec la cible.
- [Comparateur de changements de classe 3.3–3.7](https://backfromafk.com/) — l'utiliser pour repérer les anciennes règles obsolètes.
- [Index tutoriels et passages déjà consultés](../INDEX_SOURCES_TUTORIELS.md). Aucun passage dédié n'a encore été évalué visuellement pour cette classe ; utiliser l'index des 19 sections HUZ pour commencer.
- Collecter un tutoriel récent par mécanique, sa transcription datée et les captures du passage concerné ; relever un cas positif, un cas négatif et les conséquences pour le simulateur.
- Toutes les combinaisons ci-dessus sont des **pistes** (`hypothese`) tant qu'une preuve spécifique n'est pas citée ; une source d'opinion sur les meilleurs duos donne `declare-video`, pas `valide-test`.

