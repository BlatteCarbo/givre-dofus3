# Référentiel de combat DOFUS 3 pour GivreClone — mode d'emploi

Date de démarrage et bilan documentaire : 2026-11-04. **M01–M33 présentes** (trois fichiers chacune), **19 fiches** et **5 016 combinaisons indexées** de classes distinctes. Le dossier couvre **solo**, **duos**, **trios**, **quatuors**, équipes étendues, **compagnons**, **challenges**, **monstres**, **boss**, **cartes**, **équipements** et **mécaniques transverses**. Le corpus est évolutif : ni une fiche ni une ligne de matrice ne certifie une mécanique ou une synergie vérifiée.

## Quel document lire ?

1. `ATLAS_19_CLASSES.md` : couverture exhaustive des noms de classes et des axes d'étude ; pointer vers une fiche détaillée par classe dans `classes/`.
2. `SYNERGIES_DUO_TRIO_QUATUOR.md` : compositions documentées dans des vidéos, situations qui les justifient, limites et schémas de décision pour duo/trio/quatre et plus.
3. `BASE_MECANIQUES_TRANSVERSES.md` : grammaire commune des états, ressources, déplacements, marques, déclencheurs et signaux à observer ; elle évite d'apprendre une classe en vase clos.
4. `INDEX_SOURCES_TUTORIELS.md` : registre des tutoriels réellement interrogés et des références de données, avec liens et intervalles.
5. [SYNTHESE.md](SYNTHESE.md) : **registre complet M01–M33**, niveaux de preuve, différences de version et limites ; [PLAN_RECHERCHE_COUVERTURE.md](PLAN_RECHERCHE_COUVERTURE.md) : bilan et backlog de vérification.
6. [M01 à M33](SYNTHESE.md) : chaque dossier possède son `RAPPORT.md`, une `FICHE_*.md` et un `PASSAGE_DEVELOPPEUR.md`. Les rapports M23/M24 détaillent Pandawa/Forgelance, M27/M28/M30/M31 les cycles de boss, M32/M33 les compositions 1–8. Les [compléments indépendants M32/M33](complements/LIRE_LES_COMPLEMENTS.md) conservent **six rapports plus longs d'un second agent**, sans écraser les versions principales.
7. `BESTIAIRE_ET_BOSS_3_7.md` : changements des donjons et monstres dans la mise à jour 3.7, conséquences pour le planificateur, tests.
8. [PARCOURS_APPRENTISSAGE.md](PARCOURS_APPRENTISSAGE.md) : cours guidé ; [ATELIER_19_CLASSES_CAS.md](ATELIER_19_CLASSES_CAS.md) : 19 exercices diagnostiques et compositions avec contres ; [LIRE_MATRICES.md](LIRE_MATRICES.md) : 171 duos, 969 trios, 3 876 quatuors, avec statuts de preuve.
9. [EQUIPEMENT_STATS_ET_VOIES.md](EQUIPEMENT_STATS_ET_VOIES.md) : PA/PM/PO, voies/variantes, niveaux, résists et esquive ; [CONTRAT_INTEGRATION_IA_COMBAT.md](CONTRAT_INTEGRATION_IA_COMBAT.md) et [PRIORITES_INTEGRATION.md](PRIORITES_INTEGRATION.md) : état, événements serveur, machines à états et tests proposés pour le code.
10. [CONTROLE_QUALITE_2026-11-04.md](CONTROLE_QUALITE_2026-11-04.md) : contrôles finaux documentés, vérification des matrices et décisions de conservation des enquêtes indépendantes.

## Comment lire les preuves ?

- **`declare-video`** : le créateur dit ou conseille cela, transcription horodatée ; pas nécessairement une règle du moteur.
- **`observe-video`** : des captures du plateau et leur instant exact démontrent le comportement.
- **`corrobore-source`** : source externe précise, avec date ou version.
- **`hypothese`** : proposition pédagogique, scénario ou point de modélisation à démontrer.
- **`contradictoire`**, **`obsolete-pour-cible`** : incertitude ou divergence documentée.
- **`valide-test`**, **`valide-live`** : uniquement après exécution et artefact ; aucun statut de ce type attribué par les recherches.

Les sous-titres automatiques peuvent déformer les noms (« Nucra » pour Enutrof/Crâ, « Héliotrope » pour Éliotrope). Une vidéo sur la version 3.3, 3.6 ou 3.7 ne prouve pas les chiffres de la version cible exacte. Consulter la fiche de sort et un message de combat avant d'en faire une règle exécutable.

## Critère de maturité d'une mécanique

**Indexée** (classe/sujet identifié) → **décrite** (exemple pédagogique) → **attestée** (source exacte) → **simulable** (préconditions, transitions et ambiguïtés maîtrisées) → **testée** (replay exécuté) → **validée en réel** (confirmation documentée). Ne pas sauter un état.

## Navigation rapide pour un développeur

Une fiche de classe est une carte d'exploration, non un profil de combat prêt à copier. Une mission Mxx ne couvre qu'une mécanique précise. Pour implémenter : prendre une règle atomique prouvée, préciser l'événement serveur autoritaire, définir l'impact sur l'état partagé, établir un cas positif et négatif adversarial, vérifier le code actuel puis seulement proposer un changement. Toute décision incertaine doit conserver l'état `Unknown` et un repli sûr.

