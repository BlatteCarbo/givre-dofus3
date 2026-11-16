# Fiche M27 — Torkélonia : machine à états lunaire 3.7

## Identité, sources et limites

- **ID :** M27-TORK-20261104 ; sujet : cycle, glyphe, ouverture de protection, bonus après kill et mort du finisseur. Jeu : DOFUS Unity **3.7**, client projet 3.7.13.17 **non testé**.
- **Référence versionnée S1 :** [Dofus pour les Noobs, notes MAJ 3.7 du 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), paragraphe « Torkélonia et Crocodailles ».
- **Guide S2 :** [Dofus pour les Noobs, Sanctuaire de Torkélonia](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), texte mis à jour avec mentions « Depuis la 3.7 ».
- **Conflit S3 :** [Dofuserie, page marquée octobre 2026](https://www.dofuserie.com/donjons/sanctuaire-de-torkelonia/) garde certains effets **avant 3.7** : résistance T3, bouclier T7 et invuln post-kill.
- **Tutoriel vidéo V1 réel :** Deplai, [FARM 100 DONJONS TORKELONIA ... TEAM SADI 3.6](https://www.youtube.com/watch?v=JXC57dZBYy4), **17/09/2026**, durée annoncée **6 min**, chapitre boss [3:38](https://www.youtube.com/watch?v=JXC57dZBYy4&t=218s) **tiré de la description**. MCP info/langues OK ; transcription fr refusée (IP bloquée par YouTube). Aucune parole, aucune case vérifiée ; la 3.6 ne prouve pas la 3.7.

## Regles atomiques

| ID | Règle | Source, statut |
| --- | --- | --- |
| M27-01 | Une rotation à **8 phases** : Nouvelle lune, Premier croissant, Premier quartier, Gibbeuse montante, Pleine lune, Gibbeuse descendante, Dernier quartier, Dernier croissant. | [S2](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source |
| M27-02 | T3 Premier quartier : **+500 PB** ; le +10 % de résistances ancien est **obsolète**. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), corrobore-source 3.7 |
| M27-03 | T7 Dernier quartier : **+50 dommages** ; l'ancien +500 PB est **obsolète**. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), corrobore-source 3.7 |
| M27-04 | Kill Nouvelle / Pleine lune : **soin/reconstitution** des monstres survivants, **sans** nouvelle invulnérabilité d'un tour depuis 3.7. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) et [S2](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source |
| M27-05 | Autres effets : premier croissant dommages subis x0,8 ; gibbeuse montante +2 PM ; pleine lune renvoi 200 ; gibbeuse descendante +2 PA ; dernier croissant +20 % dommages finaux ; nouvelle lune renvoi 20 % à 2 cases du frappeur. | [S2](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source, arrondis unknown |
| M27-06 | Torkélonia et les ennemis protégés par la Lunorbe deviennent vulnérables, **pour un acteur donné durant son tour**, après une première ligne de dégâts lancée **depuis le glyphe bleu** sur la cible. Cette première ligne ne fait pas les dégâts attendus. | [S2 §« Comment rendre »](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source |
| M27-07 | Quitter le glyphe ou terminer son tour remet la cible sous protection ; l'acteur suivant doit recommencer le délock. | [S2](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source |
| M27-08 | Une ligne de dommages sur Lunorbe accélère la phase ; un cycle complet déclenche **Parade lunaire**, retrait permanent de 1 PA ou 1 PM d'après résultat reçu ; la boucle accélérée peut temporairement supprimer le glyphe. | [S2](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source pour existence, horloge/choix exact unknown |
| M27-09 | Un monstre tué transmet son bonus actuel aux autres de façon **persistante** ; la phase ne se déduit donc pas à elle seule de leurs boucliers/bonus. | [S2](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source |
| M27-10 | Coup final sur Torkélonia : mort du **personnage** finisseur sauf en Nouvelle/Pleine lune ; exception **invocation** décrite. Après décès du boss, Lunorbe/glyphe disparaissent et les autres ennemis ne requièrent plus le délock. | [S2](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html), corrobore-source |
| M27-11 | Un guide indiquant invulnérabilité 1 tour post-kill, T3 +10 % résistances ou T7 +500 PB doit être refusé pour cible 3.7. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) contre [S3](https://www.dofuserie.com/donjons/sanctuaire-de-torkelonia/), contradictoire / obsolete-pour-cible |

**Aucune preuve en état réel :** ni image observée, ni transcript MCP, ni valide-test/valide-live.

## Fixture reproductible

- **Entités** : Lunorbe L (centrale), boss T, monstres X/Y, personnages A/B/C/D, invocation S. Coordonnées exactes non disponibles : fournir des **cellId réels** à l'exécution. Glyphe G = ensemble de cellules observées pour le tour.
- **État initial** : L vivante ; T/X/Y avec verrou Lunorbe ; début de round = phase Nouvelle lune confirmée ; A sur G avec un sort BAS à **une ligne de dégâts** et un sort FORT. Coûts PA/PO, dégâts, LoS et statistiques **inconnus** jusqu'à fixture client.
- **Temps** : round global, initiative actuelle, acteur actif, événement de changement de phase, position du glyphe, actions de Lunorbe ; ne pas forcer la phase par le round si une rotation manuelle est intervenue.
- **Buffs** : phase actuelle, effets temporaires et hérités, boucliers, résistances, renvois, PV et états de soin séparés pour chaque monstre ; auteur du coup final (personnage ou invocation).

## Modèle de machine à états proposé

**Cycle** : P0 Nouvelle → P1 Premier croissant → P2 Premier quartier → P3 Gibbeuse montante → P4 Pleine → P5 Gibbeuse descendante → P6 Dernier quartier → P7 Dernier croissant → P0. Cette progression est une **description de séquence** ; le déclencheur précis naturel/accéléré se lit du serveur.

**Verrou par acteur et cible** : LOCKED → UNLOCKED_FOR(A, target, actorTurnId) après **première ligne BAS de A depuis G** ; retour LOCKED si A quitte G ou termine son tour. B n'hérite pas du délock. Le verrou initial n'est pas l'ancien état « invulnérable 1 tour après kill » supprimé en 3.7.

**Transitions de mort** : MONSTER_KILLED(X, phase=P0/P4) → appliquer soin/reconstitution aux survivants + éventuels bonus permanents de la phase, **sans ajouter un verrou post-mort de 1 tour** ; TORKELONIA_KILLED → supprimer L/G, calculer la sanction du finisseur selon type/phase, retirer le verrou Lunorbe des survivants.

**Rotation forcée** : DAMAGE_LINE_TO_LUNORBE validée → phase suivante, mise à jour des bonus sur ennemis et des cellules G ; Parade éventuelle sur boucle complète. Les effets ne sont **pas** appliqués sur un simple ACK d'envoi de commande.

## Séquence d'un tour et ressources

| Étape | Acteur | Action | Préconditions et coût | Effet attendu |
| --- | --- | --- | --- | --- |
| 0 | système | Charger phase P0, L, G, A, X, Y | Lecture état serveur | A n'a déverrouillé aucune cible ; X/Y protégés |
| 1 | A | BAS sur X | A sur glyphe, une ligne de dégâts, PA/PO/LoS confirmés ; **coût inconnu** | Première ligne enlève invuln X pour A ; pas de dégâts normaux |
| 2 | A | FORT sur X | A sur glyphe, X déverrouillé, PA suffisants ; coût inconnu | Dégâts selon sort, potentiel kill confirmé |
| 3 | moteur | X tué en P0 | Mort reçue, Y vivant | Y soigné, bonus de phase éventuellement transmis, **aucun ancien verrou d'un tour** |
| 4 | A | BAS puis attaque sur Y | A toujours sur G, ressources et placement légaux | Y peut être ouvert à son tour et attaqué si soin compensé ; pas de kill garanti |
| 5 | B | Début tour B | A a fini | B ne peut exploiter le délock de A et doit réitérer |
| N1 | A | Quitte G avant FORT | A hors glyphe | Protection de X revient ; pas de FORT considéré dommageable |
| N2 | C | Achève T en phase P2 | C finisseur **personnage** | Mort du finisseur C selon S2 ; L/G disparaissent après boss |

## Scénarios par taille d'équipe

| Taille | Proposition | Risque / échec discriminant |
| --- | --- | --- |
| **Solo** | A seul se place sur G, ouvre X et frappe ; alternative avec une invocation S effectuant le coup final sur Torkélonia. | Sortir de G avant frappe, invocateur mort ou invocation incapable d'agir ; ne pas supposer l'éligibilité de S au glyphe sans test. |
| **Duo** | A tank/placeur puis B attaquant jouant **après** A et montant sur G pour ses propres lignes. | Si B joue avant A, l'action de A ne peut préparer rétroactivement le tir de B ; le délock de A n'est jamais transféré. |
| **Trio** | A tient T, B tue X en P2, C frappe Y au tour suivant. | Y peut conserver **500 PB** issus du kill P2 ; remplacer par +10 % résistances fausse le calcul. |
| **Quatuor** | A tank, B placeur, C attaque, D soutien/invocateur ; chacun doit tracer son tour, son glyph et ses PA. | Si D accélère la Lunorbe avant C, la phase et les buffs de la cible changent ; annuler tout plan calculé sur l'ancienne phase. |

## Tests discriminants proposés (aucun exécuté)

| Test | Entrées et action | Résultat observable attendu |
| --- | --- | --- |
| P01 | Passage naturel à P2/T3 en 3.7, aucun autre buff | **+500 PB** de phase et pas +10 % résistances (S1) |
| N01 | Passage naturel à P6/T7 en 3.7, aucun autre buff | **+50 dommages** de phase et pas +500 PB (S1) |
| N02 | P0, X tué, Y vivant, A peut encore agir depuis G | Y reconstitué mais **aucun ancien invuln 1 tour** ; nouveau délock de Y possible (S1/S2) |
| N03 | P4, X tué, Y vivant | Idem N02 malgré renvoi 200, à observer (S1/S2) |
| N04 | A ouvre X puis quitte G avant FORT | X redevient protégé (S2) |
| N05 | A ouvre X puis finit ; B attaque X sans délock | B ne bénéficie pas d'ouverture de A (S2) |
| N06 | C finit boss en P2, puis S invocation finit dans une autre fixture | Mort de C contre exception S, et suppression Lunorbe/glyphe après mort du boss (S2) |
| N07 | P2, kill de X, Y survit jusqu'à P3 | Bonus hérités distincts : PB de P2 et PM de P3 ; cumul réel et timer à mesurer (S2) |
| N08 | Infliger ligne à Lunorbe sans changer de round global | Phase change, glyph et bonus recalculés, pas de déduction par (round mod 8) |

## Intérêt tactique, inconnues, reprise

Le coût du délock et les cases du glyphe déterminent la faisabilité du burst ; la transmission d'un bouclier **permanent** change le coût d'un kill prématuré. Les phases P0/P4 peuvent permettre de finir plusieurs monstres au même tour depuis 3.7, malgré leur soin (voir [S2 section « Lunatique »](https://www.dofuspourlesnoobs.com/sanctuaire-de-torkelonia.html)). L'usage de l'état **Insoignable** est documenté comme tactique de repli mais son sort, sa durée et son applicabilité doivent être chargés du client.

Inconnus : topologie/rotation native du glyphe, ordre des lignes de dégâts d'un sort multilignes, timing exact de phase et de Parade, montant exact du soin, probabilité PA/PM, bonus persistant et arrondi, capacités des invocations/compagnons et règles de challenge. Reprise : fixture 3.7.13.17 montrant T3, T7, deux kills P0 dans un seul round, puis coup final en P2 par personnage/invocation. Toutes les observations restent à faire.
