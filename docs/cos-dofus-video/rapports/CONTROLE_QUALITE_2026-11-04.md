# Procès-verbal de consolidation documentaire — 4 novembre 2026

## Objet et frontière

Contrôle de **présence, structure, encodage, liens et indexation** du référentiel documentaire du laboratoire DOFUS 3 / Unity pour GivreClone. Emplacement : `docs/cos-dofus-video/rapports/` du worktree `fix-f01-f16-final-20261024`. Cette vérification n'est **ni** une validation des mécaniques en jeu **ni** un replay serveur ou une campagne xUnit. Le code de GivreClone, les fichiers Git, les processus, les sessions et les comptes n'ont pas été modifiés.

## Matrice des contrôles réellement exécutés

| Contrôle | Résultat |
| --- | --- |
| Présence des missions | **33/33** répertoires M01–M33, chacun avec **exactement trois fichiers**, `RAPPORT.md` et `PASSAGE_DEVELOPPEUR.md` obligatoires |
| Fiches de classe | **19** Markdown distincts dans `classes/` |
| Corpus après complément et présent procès-verbal | **144 fichiers, dont 140 Markdown** (à comparer à l'inventaire initial de 136/132) |
| Encodage et contenu | Lecture **UTF-8 stricte** ; **0** fichier Markdown vide, **0** décodage invalide, **0** caractère `U+FFFD` |
| Liens locaux | **0** lien relatif Markdown non résolu (vérification des fichiers réellement présents) |
| Matrice duos | **171** données, **171** identifiants uniques, **171** compositions uniques, taille et absence de répétition d'une classe vérifiées |
| Matrice trios | **969** données, **969** identifiants uniques, **969** compositions uniques, taille et absence de répétition vérifiées |
| Matrice quatuors | **3 876** données, **3 876** identifiants uniques, **3 876** compositions uniques, taille et absence de répétition vérifiées |
| Git source | HEAD observé `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660` ; `git status --short -- docs/cos-dofus-video` rend `?? docs/cos-dofus-video/`, c'est-à-dire dossier **untracked** ; aucune opération Git d'écriture |
| Validation de code/jeu | **Aucun** build, test xUnit, combat, replay, compte, bot ou action de jeu exécuté dans cette mission |

Les contrôles de liens utilisent des motifs de lien Markdown à cible relative, un décodage URL éventuel et la résolution du chemin relativement au fichier source ; les liens externes ne sont **pas** passés un par un en test de disponibilité. Les Markdown peuvent également contenir des liens de référence non couverts par ce scanner : **0 lien cassé détecté** ne veut pas dire « toute URL externe validée ». Les décomptes CSV ont été obtenus avec `Import-Csv -Delimiter ';' -Encoding UTF8` puis deux ensembles de contrôle des `id` et `classes` ; le contrôle de taille compare le nombre de noms séparés par ` + ` au champ `taille`.

## Audit de provenance et versions

- [Index des tutoriels](INDEX_SOURCES_TUTORIELS.md) : distingue transcriptions horodatées réellement obtenues, vidéos authentifiées **metadata-only**, guides et exercices sans vidéo. **Aucun** plateau inspecté par capture n'est ici réputé `observe-video`.
- [Présentation de la mise à jour 3.7 du 4 octobre 2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) : recoupement documentaire des changements 3.7 de Torkélonia, Solar en Songes, Krobes/Protozorreur, Bethel et Nécronyx ; la date de la source ne transforme pas une règle en résultat du client local.
- [OpenDofusDB — journal 3.7.13.17](https://openddb.dev/changelog) : extraction communautaire affichée au **3 novembre 2026**, comparant la version 3.7.13.17 à 3.7.12.12. Le laboratoire n'a pas audité toutes les fiches de sorts de cette extraction ni relié ces données à des traces du client exact.
- [M23 Pandawa](M23/RAPPORT.md), [M24 Forgelance](M24/RAPPORT.md) : fiche de sorts 3.7.12, vidéos spécialisées identifiées mais transcripts bloqués, scénario commun avec positions **fictives** et coûts indiqués comme hypothèses de fixture.
- [M27 Torkélonia](M27/RAPPORT.md), [M28 Solar](M28/RAPPORT.md) : vidéos Deplai **3.6** identifiées par titre/date/chapter, transcriptions IP bloquées. Les règles 3.7 proviennent des **guides**, pas du son ni des images de ces vidéos.
- [M30 Krobes/Protozorreur](M30/RAPPORT.md), [M31 Bethel](M31/RAPPORT.md) : pas de vidéos spécialisées identifiées de façon exploitable, guides et mises à jour écrites.

## Résolution des productions concurrentes d'agents

M32 et M33 ont été produits **indépendamment** par le coordinateur et par le worker-2 dans son worktree isolé. L'examen des tailles et des SHA-256 montre que les `RAPPORT.md` et `PASSAGE_DEVELOPPEUR.md` sont distincts (les versions worker sont nettement plus étoffées). Le coordinateur a conservé **sans aucun remplacement** les dossiers [M32](M32/RAPPORT.md) et [M33](M33/RAPPORT.md) et ajouté les **six** documents isolés dans [complements/](complements/LIRE_LES_COMPLEMENTS.md), avec chemins Markdown relatifs ajustés. Ils restent des analyses comparables, pas une fusion automatique de règles.

Workers 3/4/5 livrés et sleeping, worker-1 sleeping, worker-2 a transmis ses six fichiers et est sleeping ; worker-6 a échoué et n'est crédité d'aucun livrable. L'inventaire principal confirme la présence des M01–M33 indépendamment de leur auteur.

## Limites non résolues

Le contrôle technique du corpus ne remplace pas une relecture sémantique **de chaque ligne** des 140 Markdown, la consultation de chaque URL externe, une capture des images de plateaux, un replay ni la certification de la version/du rang de chaque sort. Les **5 016 combinaisons** constituent des **entrées de recherche**, pas 5 016 synergies mesurées ou 5 016 compositions recommandées. Les situations en solo, duo, trio, quatuor et 5–8 restent, sauf mention contraire sourcée, des cas hypothétiques positifs/négatifs à valider.

**Décision de clôture de la vague** : couverture documentaire initiale, indexation et provenance centralisées ; **aucune promotion** en statut `valide-test` ou `valide-live`. Les validations empiriques, changements de code et actions sur le jeu relèvent d'une mission indépendante, non exécutée ici. Voir [synthèse finale](SYNTHESE.md) et [backlog priorisé](PLAN_RECHERCHE_COUVERTURE.md).
