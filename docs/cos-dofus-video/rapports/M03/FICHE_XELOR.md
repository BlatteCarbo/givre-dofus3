# Fiche M03-XELOR-TELEFRAG-001 — Permutation, consommation, retour et seconde création

## Identité

- **ID stable :** M03-XELOR-TELEFRAG-001 ; règles M03-R01 à M03-R07.
- **Sujet :** Xélor, mécanique Téléfrag, solo contre une cible ; transposable à une entité alliée sous réserve de légalité des sorts. **Pas de compagnon ni synergie d'équipe analysé.**
- **Jeu :** DOFUS moderne. Sources vidéo 2025 (post-refonte 3.0) et 2022 (ancienne), recoupements non officiels 3.7/3.7.12 (octobre 2026). **Version cible locale selon kit : 3.7.13.17 ; correspondance à confirmer.**
- **Analyse :** worker M03, 04/11/2026.
- **V1 :** [SAPEUH, « LE NOUVEAU XELOR EST MAGIQUE SUR DOFUS ! »](https://www.youtube.com/watch?v=5sGLA1FiT7M), 09/02/2025, 16 min, FR auto ; complète 00:00–16:05, passage primaire [01:48–02:08](https://www.youtube.com/watch?v=5sGLA1FiT7M&t=108s). Passage secondaire [04:14–04:36](https://www.youtube.com/watch?v=5sGLA1FiT7M&t=254s).
- **V2 :** [INFI, « TUTO XÉLOR : LES BASES »](https://www.youtube.com/watch?v=m28gccWeFGs), 09/01/2022, 7 min ; transcription retournée **en anglais traduit** (pas en français original) malgré lang=fr ; complète 00:05–06:19 ; extraits 00:05–00:15, 02:06–02:45 et 03:07–03:57.
- **Méthodes :** get_video_info, get_available_languages, get_timed_transcript (next_cursor nul), recherche et lecture web ; navigateur CoS : vraie scène PvP vue mais positions précises non validées. **Pas de lecture instrumentée de combat ni test.**

## Règles atomiques et preuves

| ID | Affirmation précise | Statut | URL horodatée / source | Justification et limite |
| --- | --- | --- | --- | --- |
| M03-R01 | Destination de téléportation occupée → permutation de deux entités et Téléfrag sur les deux ; case libre → pas de Téléfrag. | corrobore-source ; declare-video historique | [Guide 3.7](https://dofus-grimoire.fr/classes/xelor/) ; [INFI 00:05](https://www.youtube.com/watch?v=m28gccWeFGs&t=5s) | L'état est lié à l'**échange**, pas au simple mouvement ; ordre exact des événements client non observé. |
| M03-R02 | Gain de +2 PA par création, une fois **par sort générateur et par tour**. | corrobore-source | [Guide 3.7, passif](https://dofus-grimoire.fr/classes/xelor/) | Dans le simulateur, récompense conditionnelle par acteur/sort/tour. |
| M03-R03 | Permutation coûte 4 PA ; échange avec cible à 2 PO. | corrobore-source ; declare-video | [Guide 3.7, Xélor Eau](https://dofus-grimoire.fr/classes/xelor/) ; [évolution de PO](https://backfromafk.com/classes/xelor/depuis-3-0/) ; [SAPEUH 01:50–01:57](https://www.youtube.com/watch?v=5sGLA1FiT7M&t=110s) | Guide 3.7.12 ; conditions de lancer et portée réelle au sous-patch cible à valider. |
| M03-R04 | Gelure coûte 2 PA et renvoie la cible à sa **position précédente** ; retour vers cellule occupée peut créer une autre permutation/Téléfrag. | corrobore-source ; declare-video | [Fashionista, Gelure](https://dofusfashionista.gg/fr/spells_linked/XELOR/OTY5MzJFpcif/), [Guide 3.7, Xélor Eau](https://dofus-grimoire.fr/classes/xelor/) ; [SAPEUH 01:59–02:03](https://www.youtube.com/watch?v=5sGLA1FiT7M&t=119s) | Historique et ordre des retours non instrumentés ; les coordonnées ci-dessous sont proposées. |
| M03-R05 | Clepsydre coûte 4 PA, **consomme Téléfrag sur sa cible** et peut apporter +1 PA au lanceur **au prochain tour** (max cumul 3 dans guide). | corrobore-source | [Fashionista, Clepsydre](https://dofusfashionista.gg/fr/spells_linked/xelor/OTUxOTnBJ9Ig/), [Guide 3.7, combos eau](https://dofus-grimoire.fr/classes/xelor/) | Retrait PA adverse esquivable ; ne pas appliquer le bonus de prochain tour au solde immédiat. |
| M03-R06 | Pétrification coûte initialement 4 PA, consomme Téléfrag sur sa cible ; son prochain lancer est moins cher **après** une consommation, maximum 2 réductions cumulées. | corrobore-source ; declare-video | [Mise à jour des descriptions](https://backfromafk.com/classes/xelor/depuis-3-0/), [Fashionista](https://dofusfashionista.gg/fr/spells_linked/XELOR/OTY5MzJFpcif/), [SAPEUH 02:03–02:06](https://www.youtube.com/watch?v=5sGLA1FiT7M&t=123s) | Le coût du **lancer actuel** n'est pas remboursé. |
| M03-R07 | Retour temporel requiert un historique des positions (déplacements PM et sorts selon INFI 2022) ; conservation « deux tours » seulement **historique**. | declare-video ancien ; hypothese 3.7 | [INFI 02:06–02:45](https://www.youtube.com/watch?v=m28gccWeFGs&t=126s) | Les durées et détails du moteur moderne sont inconnus. |

**Toutes les affirmations de cette fiche sont des paroles ou des sources web.** Aucun état de cellule, jet de dégâts, identité de message ni coût réellement débités n'ont été contrôlés image par image ou par journal.

## État initial reproductible proposé

- **Acteurs :** X = Xélor, E = ennemi vivant. Tour de **X** ; aucune autre entité. L'adversaire n'a pas de réaction / déclencheur modifiant les positions dans ce scénario.
- **PA :** X = **12 PA disponibles** en début de tour ; X possède **Permutation, Clepsydre, Gelure, Pétrification** (et les variantes compatibles effectivement équipées). PM, PV, résistances et statistiques : **indifférents pour l'état de placement**, nécessaires au calcul de dégâts/retrait mais ici inconnus.
- **Repère relatif :** grille illustrative, cases régulières : **X = (0,0)** ; **E = (2,0)** ; **(1,0) libre**, pas de mur ; aucune autre case occupée. Les coordonnées **ne sont pas** des identifiants serveur.
- **États :** aucun Téléfrag initial, bonus de coût Pétrification initial = 0 ; compte de restitution PA vide pour le tour courant ; pas d'invocation.
- **Historique :** snapshots/positions précédentes initiaux neutres, puis **Permutation fournit les mouvements nécessaires au retour de Gelure**, à confirmer. **Aucun obstacle**, ligne de vue et ligne de lancer supposées valides à cette distance.
- **Prérequis non prouvés :** si Permutation et Gelure sont lancés légalement sur E selon les **données exactes 3.7.13.17**, si un échange insère les positions antérieures dans l'historique et si Clepsydre n'invalide pas ce retour.

Schéma de départ et résultat prévu après Permutation :

| Case | (0,0) | (1,0) | (2,0) |
| --- | --- | --- | --- |
| Avant | X | libre | E |
| Après Permutation | E (Téléfrag) | libre | X (Téléfrag) |
| Après Gelure | X (Téléfrag) | libre | E (Téléfrag) |

## Séquence et transitions prédites (non exécutée)

**Hypothèse de calcul :** restitution 2 PA par Permutation puis 2 PA par Gelure, chacune une fois sur ce tour ; un Clepsydre sur cible Téléfrag accorde +1 PA **au prochain tour** ; bonus Pétrification pour son prochain lancer. Cette reconstitution est **inspirée** du passage SAPEUH et du [tour eau du guide 3.7](https://dofus-grimoire.fr/classes/xelor/), ce n'est **pas** la transcription exacte d'une suite ininterrompue de 4 casts du vidéaste.

| Étape | Acteur | Action et cible | Préconditions | Coût et solde X | Effets immédiats prédits | Différé / ordre | Preuve |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | X | État départ | X(0,0), E(2,0), 12 PA, états nuls | 12 PA | Historique initial et ressources connus | — | État de test **proposé**, pas vidéo |
| 1 | X | **Permutation(E)** | E à **2 PO**, lancer légal, X/E vivants | -4 PA, puis **+2 PA** Téléfrag → **10 PA** | Échange atomique : **X(2,0), E(0,0)**. Téléfrag sur X/E. Enregistrer les deux positions antérieures ; marquer remboursement (tour, sort Permutation) | Ordre exact des événements client à prouver | M03-R01,R02,R03 |
| 2 | X | **Clepsydre(E)** | E est Téléfrag à (0,0), portée/ligne valides | -4 PA → **6 PA** | Consomme état Téléfrag **sur E** ; X reste dans son propre état (à confirmer par event). Retrait PA ennemi : résultat réel inconnu | Bonus **+1 PA au tour suivant** si condition validée ; ne pas ajouter au solde courant | M03-R05 |
| 3 | X | **Gelure(E)** | Position précédente E = (2,0), **occupée par X** ; historique encore valide | -2 PA, puis **+2 PA** d'un **autre sort** → **6 PA** | Le retour de E à (2,0) provoque échange avec X : **X(0,0), E(2,0)** ; Téléfrag présent sur X/E | Historique de chaque entité mis à jour après échange, ordre inconnu | M03-R01,R02,R04 |
| 4 | X | **Pétrification(E)** | E Téléfrag ; sort disponible | -4 PA → **2 PA** | Consomme Téléfrag de E ; dégâts et PA adverses réellement retirés inconnus | Réduit coût **du prochain** lancer de Pétrification selon plafond, **aucun +1 PA immédiat** | M03-R06 |

**Position finale prévue :** X (0,0), E (2,0) ; E non Téléfrag après consommation, X peut encore avoir son état suivant la résolution ; solde X = 2 PA ; bonus différé Clepsydre = +1 PA au tour suivant ; réduction future Pétrification = 1. **Tous ces résultats attendent un replay.** Si une action reçoit un refus serveur, n'appliquer ni coût, ni permutation, ni Téléfrag, ni bonus sans constat server-authoritative ; si confirmation ambiguë, conserver **Unknown** et resynchroniser sans rejouer aveuglément.

## Intérêt tactique

- Le Téléfrag transforme un **déplacement** en ressource et en condition d'effets : 4 PA de restitution théorique sur deux sorts, plus un déclenchement de Clepsydre et de Pétrification. La séquence exploite l'historique du déplacement pour revenir aux positions de départ.
- **Bénéficiaire du gain immédiat : X** ; **bénéficiaire du bonus différé : X au tour suivant** ; **cible des effets de dégâts/retrait : E**, sous esquive/résistances et effets du sort.
- **Préparation :** dans ce scénario, il faut une position E exactement à 2 cases et les sorts actifs. Cela peut nécessiter un tour de placement ; le coût de préparation, le risque d'exposition et les alternatives (attaque simple) restent inconnus.
- **Risques :** PO modifiée, ligne de vue, case d'arrivée bloquée, état de déplacement prohibé, autre déplacement entre les sorts, entité tuée, variante non équipée, Téléfrag consommé par un autre effet. Ne pas « anticiper » 2 PA sans création validée.
- **Challenges / compagnons :** si le cible est un compagnon/allié, identifier l'acteur et les effets de placement sur la condition du challenge. Aucune règle exacte de challenge n'est établie ici.
- **Formules :** aucun jet de la vidéo converti en formule ; dommage, vol de vie et retrait de PA non chiffrés. Seuls les coûts web et les gains conditionnels sont indiqués.

## Scénarios de test proposés

| Cas | Entrée / perturbation | Action | Résultat observable attendu | Distingue | Statut |
| --- | --- | --- | --- | --- | --- |
| **P1 — échange / retour / consommation** | X(0,0), E(2,0), 12 PA, ligne légale, historique de swap conservé | Permutation → Clepsydre → Gelure → Pétrification | Échanges (0↔2) puis (2↔0), Téléfrag E vrai après étapes 1 et 3, faux après 2 et 4 ; **PA X = 10→6→6→2**, bonus suivant +1 si établi | R01–R06 et gestion du même état par entité | **propose, non execute**, aucun artefact |
| **N1 — portée actuelle** | X(0,0), E(1,0), Permutation version 3.7 dont portée min=max 2 | Tentative Permutation(E) | Cast **illégal/refusé**, aucune permutation, aucun Téléfrag ni remboursement, aucune altération des ressources confirmées | R03 / différence ancien 1–3 PO | **propose, non execute** |
| **N2 — case de retour désormais libre** | P1 jusqu'à Permutation ; ensuite E(0,0) est déplacé à **(0,1)** par source extérieure ; (0,0) reste libre, X(2,0) | Gelure(E) | Si « précédente » = (0,0), retour sur **case libre**, aucun échange ni remboursement Téléfrag ; positions à confirmer | R04 / nécessité de conserver le **dernier mouvement**, pas position initiale fixe | **propose, non execute** |
| **N3 — second Téléfrag avec même sort** | Deux permutations légales distinctes dans un même tour, PA suffisants, cibles replacées pour créer deux échanges | Permutation puis Permutation | Deux échanges/Téléfrag possibles mais **un seul remboursement +2 PA** pour Permutation sur ce tour | R02 / clé (sort, tour, lanceur) | **propose, non execute** |
| **N4 — Gelure sans historique valide** | Cible non déplacée depuis la durée de conservation effective, sans état Téléfrag | Gelure(E) | Pas de retour exploitable ni de Téléfrag par collision ; coût du lancer si légal à vérifier | R07 / vieillissement historique | **propose, non execute**, durée cible inconnue |
| **N5 — mort / disparition** | E meurt ou Complice ciblé disparaît avant retour prévu | Gelure sur cible disparue | Cible/UID invalidé ; aucune permutation ni rétribution spéculative | Liveness / identité / déclenchement | **propose, non execute** |

## Passage au développeur

- **Entrées :** entités vivantes par ID, équipe, positions, obstacles, état Téléfrag par entité, historique ordonné des mouvements et échanges, source/acteur/phase de mouvement, compétences/variantes, portée et restrictions, PA actuels, PA différés, remboursements déjà donnés par sort/tour.
- **Transitions :** destination libre/occupée/bloquée ; swap atomique ; pos précédentes pour **chaque** entité ; création/consommation d'états ciblés ; remboursement plafonné ; coût modifié au **prochain** cast ; mise à disposition différée du bonus au tour suivant ; absence de mutation certifiée sur refus/Unknown.
- **Calcul possible à ce stade :** tester des **candidats** Permutation/Gelure selon topologie et coûts communautaires 3.7 ; conserver un statut **prédictif** pour l'ordre et les durées historiques.
- **À prouver avant implémentation active :** data/sorts client cible, schéma et durée exacts de l'historique, délai des effets, événements et ordre serveur, PO/ligne de vue, plafonds/sources de PA, variantes; cas terrain validé.
- **Priorité : haute** pour planification Xélor (positions, historique, PA influencent la légalité et plusieurs tours), mais ne pas prioriser une formule de dégâts non sourcée.

## Reprise

Les deux transcriptions entièrement consultées (next_cursor nul) ; V3 de Cody retirée. Dernière plage V1 : 04:14–04:36 ; V2 : 03:07–03:57. Prochaine vérification : replay 3.7.13.17 sur P1/N1/N2 avec UID des entités, old/new cell, Téléfrag et PA après **chaque** événement ; compléter ensuite les conditions précises de lancement et la durée d'historique. Aucun test exécuté, aucun état **valide-test** ou **valide-live**.
