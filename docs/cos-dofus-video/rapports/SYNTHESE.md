# Synthèse consolidée — référentiel de combat DOFUS 3 / Unity

**Bilan documentaire du 4 novembre 2026**, dans le worktree `fix-f01-f16-final-20261024`. Objet : apprendre les 19 classes, leurs mécaniques propres, les interactions entre personnages en solo/duo/trio/quatuor/5–8, et les exceptions de compagnons, invocations, challenges, monstres, boss et équipements. Les documents sont des **sources et propositions de tests** pour le simulateur et le planificateur GivreClone, pas des résultats de combat validés.

**État de livraison** : **33 dossiers M01–M33**, trois fichiers chacun (rapport, fiche, passage développeur), **19 fiches de classes**, plus six approfondissements M32/M33 indépendants ; après consolidation et ajout du contrôle qualité, **140 Markdown et 144 fichiers** dans `rapports/`. Toutes les missions sont **présentes matériellement** ; leur présence ne signifie pas que chaque règle est vérifiée sur le client cible. Les matrices indexent **171 duos + 969 trios + 3 876 quatuors = 5 016 combinaisons mathématiques** de classes distinctes : ce ne sont **pas 5 016 synergies prouvées**.

## Registre complet des 33 missions

**Mode de lecture** : `transcrit` = sous-titres horodatés réellement récupérés, pas visionnage du plateau ; `métadonnées` = titre/date/description/chapitres mais paroles indisponibles ; `guide` = source écrite (souvent 3.7 ou 3.7.12) ; `exercice` = situation pédagogique construite, non observée. La limite 3.7.12 → 3.7.13.17 s'applique notamment à tous les coûts, sorts, variantes, états et délais chiffrés non extraits du client exact. Chaque ligne pointe vers les trois livrables de la mission depuis son `RAPPORT.md`.

| Mission | Objet | Sources / profondeur réelle | Limite discriminante |
| --- | --- | --- | --- |
| [M01](M01/RAPPORT.md) | Sram, réseaux de pièges et activation | Tutoriels Pchen/Shurikn/GOATESQUE transcrits ; ordres de pose et tests adverses | Déclencheur, retrait et cases réels non rejoués |
| [M02](M02/RAPPORT.md) | Roublard, bombes, murs, explosion | Titiloo 2025/26 transcrit ; branches Poudre/Détonateur | Mur dérivé et chiffres non certifiés client exact |
| [M03](M03/RAPPORT.md) | Xélor, Téléfrag | SAPEUH 2025 et INFI historique transcrits ; cases avant/après | Attribution, coûts, états et ordre serveur inconnus |
| [M04](M04/RAPPORT.md) | Éliotrope, redirection | Yossat 2025 : 320 segments et timecodes ; géométrie | Départage des sorties sans capture de plateau |
| [M05](M05/RAPPORT.md) | Sacrieur, déplacement/attaque | SAPEUH/Capitemflam transcrits ; bénéfice contre risque | Conditions et variantes 3.7.13.17 non observées |
| [M06](M06/RAPPORT.md) | Compagnon Korbax | INFI/Capitemflam transcrits ; placement, identité du combattant | Règles et coûts historiques, impact challenge inconnu |
| [M07](M07/RAPPORT.md) | Challenges Duel / De la même lignée | Tutoriels historiques transcrits + guides ; attribution acteur/cible | Comptage compagnons/invocations 3.7 non confirmé |
| [M08](M08/RAPPORT.md) | Quatuor Pandawa/Éliotrope/Féca/Iop | Tenmalexis 3.1 transcrit + modèles d'initiative | Rotation proposée, pas de combat reconstitué |
| [M09](M09/RAPPORT.md) | Féca, glyphes | Tutoriels Timtoobias/SABBAT transcrits + guides | Création/activation/fin de tour à distinguer |
| [M10](M10/RAPPORT.md) | Poussée, obstacles et collisions | Tutoriels INFI 3.7 transcrits | Formule de collision et ordre des effets non certifiés |
| [M11](M11/RAPPORT.md) | Gourlo, tonneaux, déverrouillage | Guides 3.7.12 et proposition de machine à états | Pas de tutoriel vidéo exploitable ni trace serveur |
| [M12](M12/RAPPORT.md) | Zobal, masques/boucliers | Métadonnées de vidéos dédiées ; guides | Transcripts bloqués IP, pas de parole attribuable |
| [M13](M13/RAPPORT.md) | Crâ, portée, ligne de vue, recul | Guides et vidéo de composition transcrite ; vidéos dédiées identifiées | Tutoriels dédiés bloqués, géométrie exacte non capturée |
| [M14](M14/RAPPORT.md) | Iop, charge, fenêtres de burst | Vidéos d'équipe transcrites et guides ; duo identifié | Duo dédié bloqué, relances et coûts à versionner |
| [M15](M15/RAPPORT.md) | Eniripsa, soins, buffs, bénéficiaires | Guides et métadonnées de tutoriels | Pas de transcript dédié récent ; durées/ordre inconnus |
| [M16](M16/RAPPORT.md) | Enutrof, retrait PM/esquive | Guides et métadonnées dédiées | Retrait tenté ≠ retrait appliqué ; transcripts bloqués |
| [M17](M17/RAPPORT.md) | Ecaflip, Main et aléas | Données de sorts 3.7.12, scénarios conditionnels | Pas de démonstration des tirages sur client cible |
| [M18](M18/RAPPORT.md) | Osamodas, invocations | Données 3.7.12, identité/maîtrise des invocations | IA, rangs et actions d'invocations non rejoués |
| [M19](M19/RAPPORT.md) | Huppermage, états/combinations | Xawpiya duo transcrit + sources 3.7.12 | Tutoriel individuel bloqué ; ordre des états à tester |
| [M20](M20/RAPPORT.md) | Steamer, tourelles/évolution | Guides 3.7.12 et scénarios multi-tours | Vie, contrôle et horloge tourelle non validés |
| [M21](M21/RAPPORT.md) | Sadida, arbres/poupées/Infecté | Guides 3.7.12 et propagation conditionnelle | État par cible et déclencheurs non rejoués |
| [M22](M22/RAPPORT.md) | Ouginak, Proie/Rage | Guides 3.7.12, HUZ duos transcrit | Conditions de Proie et compteurs exacts non confirmés |
| [M23](M23/RAPPORT.md) | Pandawa, portage/jet/Sobre/Saoul | Guides 3.7.12, Xawpiya/Tenmalexis transcrits, vidéos dédiées en métadonnées | Jets et Brassage non observés visuellement |
| [M24](M24/RAPPORT.md) | Forgelance, lance, états, zones | Guides 3.7.12, équipe transcrite, vidéos dédiées en métadonnées | Cellule de Lance, variantes et ordre des zones non testés |
| [M25](M25/RAPPORT.md) | Founoroshi, glyphes/portails | Notes de mise à jour 3.7 et guides | Application aux portails et tick à observer |
| [M26](M26/RAPPORT.md) | Koutoulou, Folie et coups alliés | Guides et scénario de machine à états | Réactions à coups alliés, compteurs et attribution à confirmer |
| [M27](M27/RAPPORT.md) | Torkélonia, cycles lunaires | Patch/guides 3.7 ; vidéo Deplai 3.6 metadata-only | Anciennes phases 3.6 obsolètes, transcript IP bloqué |
| [M28](M28/RAPPORT.md) | Solar, tours global/boss, Songes | Patch/guides 3.7 ; vidéo Deplai 3.6 metadata-only | Songes ≠ donjon, transcript IP bloqué, tick exact inconnu |
| [M29](M29/RAPPORT.md) | Challenges avancés | Guides et exercices d'attribution acteur/ordre | Exceptions invocations/compagnons non certifiées |
| [M30](M30/RAPPORT.md) | Krobes et Protozorreur | Patch 3.7/guides : conditions d'espèce, 3 puis 6 tours | Pas de vidéo authentifiée ; horloges et UID non rejoués |
| [M31](M31/RAPPORT.md) | Bethel, Monolithe, Dagon, Nécronyx | Patch 3.7/guides : boucle de phase et résurrection | Pas de vidéo authentifiée ; ordre et UID serveur inconnus |
| [M32](M32/RAPPORT.md) | Comparatifs solo/duo/trio/quatuor | Guide HUZ transcrit ; compos Xawpiya ; comparateurs fictifs | Les gains marginaux ne sont pas mesurés en combat |
| [M33](M33/RAPPORT.md) | Équipes 5–8, compagnons/invocations | M06/M07 et guides ; occupation, initiative et challenges | Configurations de fixture, pas un combat à 8 enregistré |

## Couverture des classes et modes d'équipe

| Classes et enquêtes spécialisées | Classes et enquêtes spécialisées |
| --- | --- |
| [Crâ](classes/cra.md) — [M13](M13/RAPPORT.md) | [Écaflip](classes/ecaflip.md) — [M17](M17/RAPPORT.md) |
| [Éliotrope](classes/eliotrope.md) — [M04](M04/RAPPORT.md) | [Eniripsa](classes/eniripsa.md) — [M15](M15/RAPPORT.md) |
| [Enutrof](classes/enutrof.md) — [M16](M16/RAPPORT.md) | [Féca](classes/feca.md) — [M09](M09/RAPPORT.md) |
| [Forgelance](classes/forgelance.md) — [M24](M24/RAPPORT.md) | [Huppermage](classes/huppermage.md) — [M19](M19/RAPPORT.md) |
| [Iop](classes/iop.md) — [M14](M14/RAPPORT.md) | [Osamodas](classes/osamodas.md) — [M18](M18/RAPPORT.md) |
| [Ouginak](classes/ouginak.md) — [M22](M22/RAPPORT.md) | [Pandawa](classes/pandawa.md) — [M23](M23/RAPPORT.md) |
| [Roublard](classes/roublard.md) — [M02](M02/RAPPORT.md) | [Sacrieur](classes/sacrieur.md) — [M05](M05/RAPPORT.md) |
| [Sadida](classes/sadida.md) — [M21](M21/RAPPORT.md) | [Sram](classes/sram.md) — [M01](M01/RAPPORT.md) |
| [Steamer](classes/steamer.md) — [M20](M20/RAPPORT.md) | [Xélor](classes/xelor.md) — [M03](M03/RAPPORT.md) |
| [Zobal](classes/zobal.md) — [M12](M12/RAPPORT.md) | 19 fiches, aucune étude exhaustive sort-par-sort |

- [Atlas et fiches des 19 classes](ATLAS_19_CLASSES.md) : rôles, sous-états, options solo, duos/trios/quatuors, partenaires et contre-exemples. [Atelier de 19 cas](ATELIER_19_CLASSES_CAS.md) : diagnostic pédagogique pour chaque classe, avec cinq cas composés développés.
- [Synergies duo/trio/quatuor](SYNERGIES_DUO_TRIO_QUATUOR.md), [M08](M08/RAPPORT.md), [M32](M32/RAPPORT.md) : les recommandations orales d'auteurs et les trios/quatuors extrapolés sont séparés ; l'ordre d'initiative et le gain contre une ligne de base indépendante restent à démontrer.
- [M33](M33/RAPPORT.md) : équipes 5–8, occupation de cases, durée, identité d'invocation/compagnon, conflits d'actions et limites de mode. [M06](M06/RAPPORT.md) et [M07](M07/RAPPORT.md) apportent Korbax/challenges. **Enquêtes approfondies indépendantes du worker-2** : [M32 comparatif](complements/M32/RAPPORT.md), [M33 équipes étendues](complements/M33/RAPPORT.md), avec [note de conservation](complements/LIRE_LES_COMPLEMENTS.md), sans remplacer les dossiers M32/M33 principaux.
- [Guide des matrices](LIRE_MATRICES.md) : [171 duos](MATRICE_DUOS_171.csv), [969 trios](MATRICE_TRIOS_969.csv), [3 876 quatuors](MATRICE_QUATUORS_3876.csv). Aucune fréquence de victoire, affinité moyenne ou score universel n'a été mesuré.

## Sources, preuves et limites de version

- [Index des sources YouTube interrogées](INDEX_SOURCES_TUTORIELS.md) : Yossat Éliotrope (320 segments), Xawpiya équipes de 4 (294), Xawpiya duos Huppermage (309), HUZ duos 19 classes (1 203), et plusieurs transcriptions de missions. **`declare-video` ≠ `observe-video`**.
- Les tutoriels de Pandawa/Forgelance (M23/M24) et Torkélonia/Solar (M27/M28) ont parfois **métadonnées/langues vérifiées** mais **transcriptions bloquées par IP**. Une description, un titre et un chapitre affiché ne prouvent pas les paroles, les cases, ni l'exécution d'un combo.
- Les guides **3.7.12** et patch **3.7** permettent un recoupement écrit ; [OpenDofusDB dispose aussi d'un journal de données 3.7.13.17 extrait le 3 novembre 2026](https://openddb.dev/changelog), **pas encore audité sort-par-sort dans ce laboratoire**. La cible exacte doit être reliée à un manifeste/version locale et aux véritables données client. Les tutos **3.1/3.6/2022/2.x** restent historiques quand un sort ou une phase a changé.
- **Aucune capture du plateau** authentifiée, **aucun replay serveur**, **aucune exécution xUnit** ou combat réel dans cette mission. Les labels `valide-test`, `valide-live` et `observe-video` ne sont pas attribués. Une règle non tranchée doit rester `Unknown`, `contradictoire` ou `obsolete-pour-cible`.

## Particularités de boss 3.7 à ne pas mélanger

Le [bestiaire 3.7](BESTIAIRE_ET_BOSS_3_7.md) renvoie aux dossiers détaillés. [M27 Torkélonia](M27/RAPPORT.md) sépare l'ancienne mécanique des phases de lune des nouvelles valeurs Premier/Dernier quartier ; [M28 Solar](M28/RAPPORT.md) distingue le **tour global en Songes** du **tour propre du boss en donjon** ; [M30 Krobes/Protozorreur](M30/RAPPORT.md) décrit **trois validations de condition pour Krobe** et **six tours d'Infection pour Protozorreur**, contre les délais historiques ; [M31 Bethel](M31/RAPPORT.md) inclut le **retour Monolithe+Dagon ennemis si Dagon allié meurt**, et l'évolution de Nécronyx/résurrections. [M25 Founoroshi](M25/RAPPORT.md) et [M26 Koutoulou](M26/RAPPORT.md) couvrent glyphes/portails et Folie. Les déclencheurs fins restent à observer.

## Passage développeur et contrat de prudence

Lire [BASE_MECANIQUES_TRANSVERSES.md](BASE_MECANIQUES_TRANSVERSES.md), [CONTRAT_INTEGRATION_IA_COMBAT.md](CONTRAT_INTEGRATION_IA_COMBAT.md) et [PRIORITES_INTEGRATION.md](PRIORITES_INTEGRATION.md), ainsi que le `PASSAGE_DEVELOPPEUR.md` de chaque mission. Le code a été **lu en lecture seule** à la date indiquée, ses références/SHA sont consignées dans le contrat ; il pourrait évoluer depuis.

1. **État fiable** : identifiant réel de combattant, camp, propriétaire, cellule, ressources par acteur, initiative, buffs, états, invocations, portails, marques, monstre/boss et phase ; ne pas substituer intention/prédiction à une confirmation.
2. **Ordre événementiel** : distinguer pose/activation/retrait d'une marque ; bombe vivante/mur dérivé/explosion ; pré/post-position Téléfrag ; portail ouvert/sortie ; tirage Ecaflip ; contrôle et tour d'une invocation.
3. **Temps** : déclencheur avant/après attaque, début/fin de tour du bon acteur ou tour global ; effet différé, expiration et cooldown ; plusieurs cycles de boss ; refuser le replay aveugle si résultat serveur inconnu.
4. **Légalité** : portée et ligne de vue, terrain et case occupée, déplacement forcé/collision, variantes exclusives, niveau/rang/PA, boss invulnérable, challenges, identité du bénéficiaire et du lanceur.
5. **Validation** : scénario positif et contre-exemple modifiant UNE condition, rapport d'observation avec source-version horodatée, test offline discriminant puis validation réelle séparément autorisée.

## Contrôle qualité et frontière de livraison

Le [procès-verbal de vérification](CONTROLE_QUALITE_2026-11-04.md) consigne le contrôle final : **33/33 dossiers**, **19 fiches**, **140 Markdown non vides sur 144 fichiers**, **0 UTF-8 invalide**, **0 caractère U+FFFD**, **0 lien local cassé**, **0 doublon ID/composition** et **171 / 969 / 3 876 entrées CSV**. Les nombres ne sont pas une preuve de justesse tactique. Aucun build, test xUnit, session de jeu, compte ou opération Git d'écriture n'a été effectué pour le laboratoire.

**Backlog ouvert, sans masquer la livraison documentaire** : audit sort-par-sort, rangs et variantes pour chaque classe ; acquisition des captures de plateaux aux timecodes ; états/cellules effectivement observés ; centaines de variantes d'initiative ; comparaison empirique des 5 016 compositions ; replays et tests dans une mission de développement distincte ; autres donjons, succès, compagnons, statistiques et équipements par tranche de niveau. Voir [plan de couverture](PLAN_RECHERCHE_COUVERTURE.md), [équipements/voies](EQUIPEMENT_STATS_ET_VOIES.md) et [parcours d'apprentissage](PARCOURS_APPRENTISSAGE.md).

