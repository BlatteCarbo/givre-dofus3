# Bilan de couverture et programme de vérification — 19 classes, équipes 1–8, PvM/PvP

**Bilan au 4 novembre 2026** : les [33 missions M01–M33](SYNTHESE.md), [19 fiches](ATLAS_19_CLASSES.md) et [5 016 combinaisons indexées](LIRE_MATRICES.md) sont **matériellement présentes**. Il reste à démontrer empiriquement les états, préconditions, transitions et synergies dans le client exact. Cette page est désormais le **registre des lacunes vérifiables**, pas un calendrier d'agents auxquels les dossiers seraient encore à confier.

## Inventaire mathématique des combinaisons

Avec 19 classes, il existe **171 duos**, **969 trios** et **3 876 quatuors** de classes distinctes (sans compter les variantes, niveaux, ordre d'initiative, doubles classes, invocations, compagnons ou cartes). On évite de fabriquer 5 016 recommandations sans preuves. La priorité est de tester les **types d'interactions** et des représentants de chaque classe, puis d'élargir suivant les différences observées.

| Domaine | Couverture initiale | Travail substantiel restant |
| --- | --- | --- |
| 19 classes | 19 fiches de cadrage dans `classes/` et M01–M24 : au moins un dossier mécanique par classe ; [atelier 19 cas](ATELIER_19_CLASSES_CAS.md) | Inventaire complet sorts/variantes/rangs dans la version exacte ; plusieurs mécaniques et contre-tests par classe |
| Duos | 171 entrées CSV indexées ; HUZ 19 chapitres transcrits et Xawpiya Hupper : 3 cas oraux | Mesures comparatives, positions réelles, changements d'initiative et équipement |
| Trios | 969 entrées CSV ; M32 et cas Panda/Enu/Crâ pédagogiques | Captures/replays réels et gain marginal du 3e acteur par boss |
| Quatuors | 3 876 entrées CSV ; M08 et Xawpiya : quatre compositions déclarées ; M32 | Reconstituer séquences sur plusieurs tours et alternatives en cas de refus |
| 5–8 personnages | [M33](M33/RAPPORT.md) et [M06](M06/RAPPORT.md) : occupation, compagnon, attribution, contre-exemples | Confirmer admissibilité par mode, identités et comportement en serveur |
| Compagnons et challenges | [M06](M06/RAPPORT.md), [M07](M07/RAPPORT.md), [M29](M29/RAPPORT.md) livrés | Compagnons multiples, chacun des challenges et attribution des invocations sur client 3.7 |
| Monstres et boss | [M11](M11/RAPPORT.md), [M25 à M28 et M30/M31](BESTIAIRE_ET_BOSS_3_7.md) documentés, changements 3.7 distingués | Autres boss, transitions horodatées, replays, ticks serveur et dépendance à l'initiative |
| Équipements | [Étude PA/PM/PO, résistances, voies et variantes](EQUIPEMENT_STATS_ET_VOIES.md) | Builds exacts par niveau, coûts d'opportunité et données vérifiées en client |
| Défense et économie de tours | [Cours](PARCOURS_APPRENTISSAGE.md), [atelier](ATELIER_19_CLASSES_CAS.md), comparateurs M32/M33 | Quantifier danger, dégâts évités, bénéfice marginal depuis logs |
| Intégration technique | [Contrat](CONTRAT_INTEGRATION_IA_COMBAT.md) et [priorités](PRIORITES_INTEGRATION.md) rédigés après lecture code | Tests xUnit / traces de replay / compatibilité client réel dans mission de développement distincte |

## Une fiche par mécanique et par combinaison

Pour chaque classe, constituer le dossier `classe/<nom>/` (ou la fiche actuelle dans `classes/`) :
- **maîtriser la classe** : rôle, voies, sorts/variantes accessibles, exemple T1/T2/T3 ;
- **mécaniques** : conditions, événements, compteurs, historique, effets secondaires, échecs ;
- **micro-combos** : 2–5 actions, coûts et cible/cellule après chaque étape ;
- **solo** : cartes ouvertes/fermées, cible contrôlable ou immunisée ;
- **duo** : au moins deux partenaires contrastés, initiative favorable/inverse ;
- **trio** : ajout d'un troisième acteur avec gain *marginal* mesurable ;
- **quatuor** : quatre rôles, déclenchements et possibilité de repli ;
- **donjons** : boss de deux paliers et contraintes de succès ;
- **tests** : positif, négatif obstacle, négatif état, négatif timing, négatif refus ;
- **preuves** : vidéo/source/date/version et état de couverture.

## Ordre du prochain **lot de preuves**, sans refaire M01–M33

1. **P0 — version locale** : obtenir le manifeste et les tables de la build réellement étudiée (référence historique `3.7.13.17`), vérifier coûts/variantes/rangs/portées/cooldowns pour chacune des 19 classes. La documentation 3.7.12 n'est pas un oracle pour une sous-build différente.
2. **P0 — événements autoritaires** : produire des replays **hors ligne** de marks, bombes, Téléfrags, portails, glyphes, déplacements forcés, statuts de mort, invocations et buffs ; rapprocher les cas `Unknown` d'événements serveur et pas d'ACK.
3. **P0 — boss à cycles** : reconstituer Torkélonia (8 phases), Solar (Songes vs donjon), Krobes/Protozorreur et Bethel (boucle Monolithe/Dagon), tester déclencheurs, timers et état de phase non déduit du round seul.
4. **P1 — audit de vidéos manquantes** : réessayer uniquement lorsque l'accès IP/captures fonctionne pour les tutoriels dédiés Pandawa, Forgelance, Zobal, Eniripsa, Enutrof, Torkélonia/Solar ; relever *les vraies images de grille* et un intervalle vocal, sans attribuer de phrases à la description.
5. **P1 — comparatifs** : choisir un échantillon couvrant les types d'interactions (placeur→zone, entrave→survie, invocateur→occupation, protection→burst, portail→accès) ; comparer solo/duo/trio/quatuor aux actions indépendantes et inversions d'initiative, pas à un score subjectif par classe.
6. **P1/P2 — étendre** : fiches de boss supplémentaires, challenges et compagnons par identité, 5–8 selon mode de combat, équipement par palier, voies, variantes, PvM/PvP et tests adversariaux.

**Frontière d'autorisation** : le corpus actuel est documentaire et ne lance ni bot, ni jeu, ni test xUnit, ni opération Git. Les étapes nécessitant du code ou du serveur constituent un autre chantier et ne sont pas implicitement exécutées ici.

## Critère de progression par fiche

- `indexee` : fiche créée, piste et source de découverte disponibles ;
- `source_lue` : auteur/date/version, passages réellement lus, au moins une règle candidate ;
- `corroboree` : preuve documentaire exacte et divergence documentée ;
- `reproductible` : état initial, actions, cases, coûts, résultats et scénario négatif ;
- `testee_offline` : vrai test/replay exécuté et résultat attaché ;
- `confirmee_live` : validation réelle datée, par la procédure autorisée distincte.

Ne jamais transformer automatiquement `source_lue` en `reproductible`. Une règle non prouvée conserve `Unknown`.

## Exemple de campagne exhaustive sans explosion combinatoire

Commencer par une matrice **rôle x mécanique x difficulté** : mêlée/distance, placement, dégâts différés, contrôle, soins, protection, invocations, modifications d'état, réactions adverses. Choisir des duos qui apportent de nouveaux arcs (placeur→zone, entrave→réseau, soin→survie, invocation→condition de sort, bouclier→tour de préparation), puis tester l'interaction sous un boss et un challenge. Ensuite remplacer une classe par une autre de même rôle pour observer *ce qui change réellement*.

## Collection et organisation

Chaque agent reçoit une mécanique/vidéo bornée et un dossier propre. Le coordinateur relit liens/horodatages, copie les rapports validés, retient un identifiant stable par règle et met à jour `SYNTHESE.md`. En fin de lot : couverture réelle, références manquantes, erreurs de versions, replays à produire et décisions que le code peut prendre sans inventer une action.

