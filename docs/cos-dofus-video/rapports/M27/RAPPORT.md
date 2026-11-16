# M27 — Torkélonia : cycle lunaire en DOFUS 3.7

Recherche documentaire du 4 novembre 2026. Périmètre : **les huit phases de la Lunorbe, les changements de 3.7, vulnérabilité par le glyphe, bonus persistant après une mort, coup fatal sur Torkélonia** et adaptation solo/duo/trio/quatuor. Source projet de référence annoncée : client 3.7.13.17 ; aucune exécution sur ce client ni preuve d'une sous-version exacte.

## Hiérarchie des sources et méthode

- **S1 — règle versionnée 3.7 décisive** : [Dofus pour les Noobs, « Mise à jour 3.7 », section « Torkélonia et Crocodailles de Crocuzko »](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), mis en ligne le **4 octobre 2026** : suppression de l'**invulnérabilité d'un tour** sur mort pendant Nouvelle Lune (tour 1) ou Pleine Lune (tour 5) ; Premier quartier (tour 3) = **500 PB** au lieu de 10 % résistances ; Dernier quartier (tour 7) = **50 dommages** au lieu de 500 PB. Documentation communautaire détaillant le patch 3.7, **pas** une trace serveur. Les valeurs ont préséance sur les fiches pré-3.7.
- **S2 — guide de donjon mis en cohérence 3.7** : [Dofus pour les Noobs, « Sanctuaire de Torkélonia »](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), initialement mis en ligne plusieurs années avant 2026, contenu actuellement **édité pour 3.7** (ex. explicite « Depuis la 3.7, il est possible d'éliminer plusieurs monstres pendant le même tour de Nouvelle Lune »). Détaille huit phases, Lunorbe, déclenchement du premier dommage depuis le glyphe bleu, récupération à fin du tour d'acteur, bonus transmis à la mort, parade et coup fatal.
- **S3 — recoupement version 2026 à utiliser avec prudence** : [Dofuserie, « Sanctuaire de Torkélonia »](https://www.dofuserie.com/donjons/sanctuaire-de-torkelonia/), marqué mis à jour **octobre 2026** mais présente encore **premier quartier +10 % résistances**, **dernier quartier +500 PB** et **mort Nouvelle/Pleine lune → invulnérabilité des survivants 1 tour**. Ces passages sont **contradictoires** avec S1 : **obsolete-pour-cible** pour ces règles, même avec date de mise à jour récente. La date seule ne garantit pas les données mécaniques.
- **S4 — archive historique** : [Dofus-portals, « Sanctuaire de Torkélonia »](https://dofus-portals.fr/donjons/sanctuaire-de-torkelonia/), [Guidedofus/v2](https://guidedofus.com/v2/sanctuaire-de-torkelonia) et [JeuxOnline 2019](https://dofus.jeuxonline.info/article/14746/sanctuaire-torkelonia) : confirment la **structure** du cycle et du glyphe mais détaillent les **anciens chiffres** T3/T7 et l'ancienne invulnérabilité après une mort. Ne pas recopier ces valeurs dans un modèle 3.7.
- **S5 — vidéo authentique 2026, ancienne version** : **Deplai**, [« FARM 100 DONJONS TORKELONIA 💎 TOUR 1 - COMBIEN ÇA RAPPORTE ? | TEAM SADI 3.6 »](https://www.youtube.com/watch?v=JXC57dZBYy4), **17 septembre 2026**, durée annoncée **6 minutes** ; chapitre **« SALLE BOSS » à [3:38](https://www.youtube.com/watch?v=JXC57dZBYy4&t=218s)**, issu de la **description** get_video_info. get_available_languages a retourné fr-FR auto, en auto, de-DE/hi/id/it/pt-BR/es-US auto. get_timed_transcript(lang=fr) a **échoué** (IP bloquée par YouTube). **0 segment oral, 0 capture du plateau inspectés**. Vidéo 3.6 antérieure à S1, **aucune règle 3.7 tirée de la vidéo**.

**Statuts :** règles de S1/S2 : corrobore-source ; analogies et modèles informatiques : hypothese ; règles anciennes contradictoires de S3/S4/S5 : obsolete-pour-cible. Aucun declare-video, observe-video, valide-test ou valide-live. Les chapitres de vidéo ne sont pas des transcriptions.

## Table versionnée des huit phases (ordre cyclique)

La documentation S1 désigne les **tours 1, 3, 5, 7**, et S2 les huit positions de la Lunorbe. Un enchaînement naturel sans accélération suit l'ordre 1→2→3→4→5→6→7→8→1 ; la formule **(round-1) mod 8** est une **hypothèse de fixture** et doit être contrôlée contre l'état effectivement reçu. Si la Lunorbe est frappée, le round ne détermine plus seul la phase.

| Position | Nom | Effet décrit dans S2 pour DOFUS 3.7 | Conséquence d'une mort dans cette phase |
| --- | --- | --- | --- |
| 1, T1/T9… | **Nouvelle lune** | Renvoie sur les alliés du frappeur à proximité (S2 : **20 % des dommages infligés**, cercle **2 cases** autour du frappeur) | **Reconstitution** des autres monstres encore en vie ; **plus d'invulnérabilité d'un tour** depuis 3.7 (S1). Conserver l'inconnue du timing exact du soin. |
| 2, T2/T10… | **Premier croissant** | Dommages subis multipliés par **0,8** (= réduction 20 %) | Le bonus courant du monstre tué est attribué aux autres **sans limite de durée** selon S2 ; effets des dommages subis à préserver par identifiant. |
| 3, T3/T11… | **Premier quartier** | **+500 points de bouclier** dans 3.7 (**pas** +10 % résistances) | Transmission du bonus à la mort selon S2 ; cumul avec shields existants **unknown**. |
| 4, T4/T12… | **Gibbeuse montante** | **+2 PM** | Transmission persistante selon S2. |
| 5, T5/T13… | **Pleine lune** | **Renvoi de 200 dommages** | **Reconstitution** des survivants ; **plus d'invulnérabilité d'un tour** depuis 3.7 (S1) ; bonus de renvoi attribué aux survivants selon S2. |
| 6, T6/T14… | **Gibbeuse descendante** | **+2 PA** | Transmission persistante selon S2. |
| 7, T7/T15… | **Dernier quartier** | **+50 dommages** en 3.7 (**pas** +500 PB) | Transmission persistante selon S2. |
| 8, T8/T16… | **Dernier croissant** | **+20 % de dommages finaux occasionnés** | Transmission persistante selon S2. |

**Non prouvé par ces textes** : si les valeurs de PB se réappliquent au début de chaque tour en cas de conservation d'un ancien buff, l'ordre de réduction de dégâts/critique/résistance/renvoi, l'effet exact d'une source de dommages multiples sur Lunorbe, l'arrondi de 20 %, l'heure précise du tick de phase et le délai d'activation de la transmission. Les testeurs devront exploiter les événements d'état et de bouclier du client 3.7.13.17.

## La Lunorbe, le glyphe, le verrou et le final

1. **Verrou initial** (S2) : Torkélonia et ses monstres sont initialement invulnérables grâce à la **Lunorbe**, entité centrale décrite comme invulnérable et indéplaçable. Un **glyphe bleu rotatif** correspond à une ligne/diagonale du cadran. La Lunorbe est **un objet/acteur distinct** des ennemis ; la frapper peut modifier le cycle (S2).
2. **Délock par acteur** (S2) : pour endommager un monstre, l'acteur se place **sur le glyphe**, dirige une première **ligne de dommages** vers le monstre ciblé ; **ce premier impact retire le verrou mais ne lui inflige pas les dégâts attendus**. Tant que l'acteur reste sur le glyphe durant son tour, les lignes suivantes peuvent frapper la cible déverrouillée. Si l'acteur quitte le glyphe ou **termine son tour**, le verrou est restauré ; un autre personnage doit recommencer. Une action sur une autre cible ne la rend pas vulnérable par défaut.
3. **Rotation naturelle / forcée** (S2) : la phase avance naturellement d'une position par **tour du cycle**, mais une **ligne de dégâts sur Lunorbe** peut avancer la phase prématurément. Plusieurs états appliqués dans un même tour peuvent produire des **bonus cumulés**, susceptibles d'être transmis comme permanents si un monstre meurt. Le calendrier de synchronisation exact d'une ligne de dégâts et du mouvement du glyphe demande la trace d'effet.
4. **Parade lunaire** (S2) : lorsque la Lunorbe achève un cycle complet, un retrait **permanent** de **1 PA ou 1 PM** est décrit. S2 dit **aléatoire** ; un vieux guide suggère alternance. Modéliser le **résultat réellement reçu** et conserver random/alternance comme point non résolu. L'accélération jusqu'à boucler le cadran au même tour peut faire **disparaître temporairement le glyphe** ; l'état devient impropre à toute déduction naïve à partir du round.
5. **Fin de Torkélonia** (S2) : un personnage qui porte le coup fatal au boss **meurt** sauf si la mort a lieu en **Nouvelle lune ou Pleine lune** ; l'exception **coup final par invocation** est documentée. Ces exceptions concernent **le boss**, et ne dispensent pas de tester les retours de dommages et le challenge actif.
6. **Après mort du boss** (S2) : Lunorbe et glyphe disparaissent, les autres monstres deviennent vulnérables sans le délock ordinaire. Cela change immédiatement le modèle de risque, de ciblage et les coûts de PA ; éviter de conserver le verrou initial.

**Changement 3.7 absolument discriminant** : un monstre éliminé pendant Nouvelle/Pleine lune **continue à déclencher le soin** (S2), mais le patch **a supprimé l'invulnérabilité temporaire** des survivants (S1). De ce fait le vieux conseil « obligatoirement attendre huit tours entre deux éliminations » n'est plus une contrainte dure en 3.7 : plusieurs kills peuvent être possibles au même tour, notamment si la guérison est empêchée par un état **Insoignable** (S2). La faisabilité reste conditionnée par dégâts, PA, glyphe et cibles.

## Machine à états proposée (strictement un modèle de tests)

**Etat :** {mode: combat normal / victoire, phase ∈ [0..7] ou Unknown, cycleCount, glyphe {cellules, actif, orientation, revision}, lunorbeVivant, monstres map[id] {alive, invulnLunorbe, invulnTemporaire, bonusPhaseCourant, bonusPermanents, shields, PV}, turn {roundGlobal, acteur, indexInitiative}, unlockedInActorTurn: Set(targetId), actorOnGlyph, sourceDernierCoup, étatsInsoignable, bonusParade[id]}. Identités et révisions provenant du serveur, jamais inventées.

| Evénement | Garde | Transition attendue | Niveau de preuve |
| --- | --- | --- | --- |
| COMBAT_STARTED / PHASE_CONFIRMED | Lunorbe vivante | Phase et glyphe sont lus ; état initial invulnérable sur ennemis | corrobore-source (S2) ; heure exacte unknown |
| ROUND_OR_CYCLE_ADVANCED | effet confirmé | phase=(phase+1) mod 8 ; déplacer glyphe selon effet reçu | corrobore-source (S2) ; mapping du trigger reste à étalonner |
| DAMAGE_LINE_TO_LUNORBE | ligne acceptée | avance d'une phase ; cumule potentiellement effets de plusieurs phases | corrobore-source (S2) ; nombre d'événements à confirmer |
| ACTOR_DAMAGE_LINE(target) | acteur sur glyphe, cible invulnLunorbe | cible ajoutée aux déverrouillées **de ce seul tour** ; première ligne absorbée par délock | corrobore-source (S2) |
| ACTOR_LEAVES_GLYPH / ACTOR_TURN_ENDED | cible déverrouillée | rétablir verrou de cette cible pour cet acteur/au prochain acteur | corrobore-source (S2) |
| MONSTER_KILLED(target, phase) | mort serveur confirmée | transmettre bonus de phase aux survivants ; si phase=1/5, déclencher reconstitution **sans invulnérabilité temporaire en 3.7** | corrobore-source S1+S2 |
| TORKELONIA_KILLED | deathConfirmed | si auteur est personnage et phase hors 1/5, mort du finisseur prévue ; tuer Lunorbe/glyphe ; autres monstres vulnérables | corrobore-source S2 ; séquencement exact à vérifier |
| EIGHTH_STEP_COMPLETED | tour cadran confirmé | appliquer retrait permanent 1 PA/1 PM d'après événement réel | corrobore-source S2 ; tirage aléatoire non simulé sans source |
| ACK_ONLY / REJECT / UNKNOWN | aucune confirmation | ne pas actualiser phase, bouclier, mort ou délock comme **confirmés** | règle de sûreté du planificateur |

## Stratégies et tests par taille d'équipe

**Solo** : un personnage autonome avec une attaque de faible coût (pour ouvrir le verrou depuis le glyphe) et une attaque suivante ; si le personnage frappe en phase 1, il doit éviter de regrouper des alliés/invocations proches, compte tenu du renvoi documenté. Tester un coup fatal de Torkélonia hors phases 1/5 pour observer la **mort du finisseur**, puis le même coup final confié à une **invocation** ; ne jamais déclarer ce dernier sûr par hypothèse si l'invocation est morte/contrôlable inconnue.

**Duo** : un placeur/tank **A** canalise le boss et un DPT **B** exploite le glyphe. Si B joue avant A, A ne peut pas agir rétroactivement sur le placement de B ; B doit attendre une occasion de délock. Le changement de phase pendant le tour de A peut invalider le plan de B. Chaque joueur doit **déverrouiller à son propre tour**, sauf données contraires confirmées.

**Trio** : **A** verrouille un ou deux monstres, **B** utilise le glyphe pour lever le verrou d'une cible, **C** prépare les dommages après que **son propre** délock est effectué. Si l'ennemi meurt en phase 3, les survivants acquièrent le nouveau **bouclier** permanent associé (pas +10 % résistances). Faire deux tests en ordre inversé pour vérifier que l'initiative change l'opportunité, pas la règle.

**Quatuor** : **A** tank, **B** placeur, **C** damage dealer, **D** soutien (ou finisseur invoquant). Coordonner les quatre identités : actions/PA distincts, cases du glyphe recalculées après chaque phase, éclatement si le renvoi de Nouvelle lune menace les alliés autour du frappeur. En phase 1/5, deux morts successives **peuvent** être permises en 3.7 si les survivants sont encore achevables après reconstitution ; ne jamais substituer l'ancien verrou d'un tour.

## Contre-exemples requis et tests de non-régression

| Cas | Préconditions et séquence | Résultat attendu / séparation | Statut |
| --- | --- | --- | --- |
| **N1 — faux +10 % rés. au T3** | Phase **Premier quartier** 3.7, un ennemi vivant, aucune autre source de résistance ; lire ses effets après changement | **+500 PB**, pas un effet « +10 % résistance ». Si les PV protégés ne sont pas visibles, état Unknown jusqu'au log | proposé, non exécuté ; S1 |
| **N2 — faux 500 PB au T7** | Phase **Dernier quartier** 3.7, relever effets sans buffs antérieurs ; lire bonus | **+50 dommages**, pas +500 PB à ce titre | proposé, non exécuté ; S1 |
| **N3 — kill Nouvelle lune sans ancien verrou** | Phase 1, deux monstres X/Y ; X meurt, Y a PV restants ; un autre lancer légal sur Y au même tour | Y subit **reconstitution** mais **ne gagne pas « invulnérable 1 tour »** ; besoin d'un délock ordinaire sur glyphe par acteur selon situation | proposé, non exécuté ; S1/S2 |
| **N4 — kill Pleine lune** | Même état que N3 en phase 5 | **Pas d'invulnérabilité d'un tour** nouvelle ; rétablissement et renvoi de 200 / bonus persistant sont à vérifier | proposé, non exécuté ; S1/S2 |
| **N5 — sortie du glyphe** | Acteur A sur glyphe délock X avec attaque peu chère, quitte glyphe, frappe X | La protection Lunorbe revient ; attaque suivante ne doit pas supposer X vulnérable | proposé, non exécuté ; S2 |
| **N6 — acteur B après A** | A délock X puis termine son tour ; B attaque X depuis n'importe où sans délock | X protégé pour B ; chaque acteur doit rouvrir le verrou selon S2 | proposé, non exécuté ; S2 |
| **N7 — boss fatal** | Joueur A achève Torkélonia hors phases 1/5 ; exécution alternative par invocation | Mort du finisseur A dans le premier cas ; exception invoquée dans le second (à confirmer serveur), les monstres restants deviennent vulnérables | proposé, non exécuté ; S2 |
| **N8 — rotation accélérée** | Phase confirmée, une ligne de dégâts valide sur Lunorbe, tuer X pendant bonus cumulés | Ne pas inférer phase de round, stocker effets cumulés ; les survivants peuvent hériter des bonus actifs | proposé, non exécuté ; S2 |
| **N9 — boucle complète** | Avancer le cadran huit positions, observer états de l'acteur et glyphe | Parade lunaire sur événement confirmé ; PA/PM permanent exact lu, pas deviné | proposé, non exécuté ; S2 |

Tous les cas sont **scénarios de validation futurs**, et non des combats réalisés. Aucun numéro de cellule, ordre d'initiative réel, montant de dégâts ou statut de challenge n'a été inventé.

## Données bloquantes et prochaine preuve

Pour le client 3.7.13.17, il faut un replay d'états pour **T3** et **T7** ; deux kills successifs en Nouvelle lune avec un acteur sur glyphe et un monstre encore vivant ; une ligne de dégâts sur Lunorbe confirmant la phase et le glyphe ; un cas finisseur personnage/invocation en phase non sûre. Relever exactement le round global, acteur et événement qui font tourner la phase, la récupération de délock après sortie et fin de tour, l'expiration des effets hérités et la résurrection/soin.

**Blocage vidéo** : le connecteur MCP a confirmé vidéo Deplai 3.6 (6 min, 17/09/2026), langues disponibles, mais la transcription horodatée n'a pas été fournie par YouTube. Aucune affirmation sur le visuel du tour 1 ne repose sur le seul intitulé du tutoriel. Les source 3.7 textuelles sont déterminantes.

Livrables connexes : [FICHE_TORKELONIA.md](./FICHE_TORKELONIA.md) et [PASSAGE_DEVELOPPEUR.md](./PASSAGE_DEVELOPPEUR.md).
