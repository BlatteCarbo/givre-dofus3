# M28 — Passage au développeur : Solar 3.7, deux horloges

**Document de transfert d'une recherche, sans code ni test exécuté.** Voir [RAPPORT.md](./RAPPORT.md) et [FICHE_SOLAR.md](./FICHE_SOLAR.md). Sources 3.7 : [Dofus pour les Noobs, notes du 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) et [guide Tour de Solar, dernière MAJ 05/10/2026](https://www.dofuspourlesnoobs.com/tour-de-solar.html). La version client projet **3.7.13.17** n'a pas été observée directement.

## Trois règles prioritaires avec preuve

**P1 — Une machine de phase doit disposer de deux déclencheurs temporels selon le contexte.** DOFUS **3.7 Songes infinis** : « le changement des états de Solar se fait désormais au **tour global** et non plus au tour de Solar » ; le patch précise que cela ne concerne **que les Songes**. Source [notes 3.7, section « Solar »](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). En **donjon Tour de Solar**, la progression est décrite par des **tours de Solar** dans le [guide de donjon](https://www.dofuspourlesnoobs.com/tour-de-solar.html). N'utiliser ni le nombre d'acteurs déjà joués, ni les tours de Solar comme approximation du round global des Songes. C'est la principale non-régression 3.7 ; l'instant exact du bord de tour global doit être prouvé par log client.

**P2 — La légalité du burst sur Solar dépend de l'état et de Nécronyx, avec une modification d'action 3.7.** Etats **Aurore, Zénith, Crépuscule** rendent Solar invulnérable ; **Nadir** est la fenêtre de vulnérabilité. Le donjon décrit **Aurore T1–3, Zénith T4–6, Crépuscule T7–9, Nadir à partir de T10**, hors accélération. **Nécronyx sur Solar peut faire passer un état au prochain moment de transition** ; dans le guide du donjon, c'est décrit « au prochain tour de Solar ». Le patch 3.7 précise que **Nécronyx ne passe plus le tour de celui qui l'utilise**. Sources [guide Tour de Solar §Principe](https://www.dofuspourlesnoobs.com/tour-de-solar.html) et [notes 3.7 §Bethel/Solar](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). **Nadir seul est insuffisant pour un kill sûr** : il faut aussi Nécronyx sur le boss avant son décès selon le guide, sinon toute l'équipe peut mourir. Un effet seulement demandé/ACK ne vaut pas effet confirmé.

**P3 — Préserver les états de résurrection Pyrozomb et distinguer l'ancien verrou de la phase de Solar.** Le [guide du donjon §Pyrozomb](https://www.dofuspourlesnoobs.com/tour-de-solar.html) décrit résurrection des monstres, renvoi autour de la cible attaquée, dégâts au décès, et le rôle de Nécronyx comme suppression des effets lors des kills. Les [notes 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) suppriment **l'invulnérabilité d'un tour des monstres ressuscités**. Cela ne supprime **pas** l'invulnérabilité **par état Aurore/Zénith/Crépuscule** de Solar. Il faut des statuts distincts : solarInvulnerableByPhase, resurrectedUnitInvulnerable, pyrozombActive, necroActive et alive.

## Données et machine à états proposées

**Entrées à l'instant t :** modeCombat (TOUR_DE_SOLAR, SONGES_3_7, EXPEDITION_AUDACE, EXPEDITION_BRAVOURE, UNKNOWN), phaseSolar (Aurore/Zénith/Crépuscule/Nadir/Unknown), numéro du roundGlobal et événement source, numéro du tour propre de Solar et événement source, ordre d'initiative, boss id/HP/alive, phaseTicksConfirmed, état Nécronyx et source acteur/fin d'effet, durée/cooldown Nécronyx par personnage, ensemble d'ennemis vivants/ressuscités, Pyrozomb et effets par monstre, glyphes/cellules, membres vivants, ressources PA/PM, retour du serveur, révision de l'état.

**Transitions et gardes :**

1. **START_COMBAT** : initialiser Solar en état confirmé, pas en supposant toujours Aurore si lecture manquante. Distinction explicite des modes.
2. **GLOBAL_ROUND_BOUNDARY** en mode SONGES_3_7 : la phase est susceptible de changer à cette frontière. Le contrôle doit relire l'état de Solar et incrémenter le compteur seulement quand l'événement le confirme ; les effets de Nécronyx seront recalculés selon l'ordre de réception. Le point précis round-start/round-end reste Unknown.
3. **SOLAR_TURN_STARTED** en mode TOUR_DE_SOLAR : appliquer phase, soins/boosts et éventuel effet Nécronyx selon l'événement confirmé ; la version d'origine décrit une progression naturelle **tous les trois tours**. En mode Songes, ce seul événement ne permet pas de fabriquer une transition.
4. **NECRONYX_CONFIRMED(target)** : stocker l'effet réellement appliqué sur la cible et la source ; ne pas terminer artificiellement le tour du lanceur en 3.7 ; décrémenter PA d'après le retour réel. Sur Solar, **un passage d'état** peut être accéléré au déclencheur approprié, pas immédiatement trois états. Sur autres monstres, les effets Pyrozomb peuvent être bloqués.
5. **PHASE_CHANGED** : met à jour invulnérabilité propre de Solar ; réévalue les capacités de lancement, les PA/PM et les effets de glyphe ; invalide tout plan calculé sous une ancienne phase, même si le tour du Solar n'a pas commencé en Songes.
6. **MONSTER_DIED / RESURRECTED** : si la cible Pyrozomb ressuscite, ne pas créer l'ancien « invulnérable 1 tour » supprimé depuis 3.7. Une créature qui ressuscite n'implique pas que Solar perde son invu de phase ; conserver les sources d'effet distinctes.
7. **BOSS_KILL_PROPOSED / CONFIRMED** : vérifier **Nadir confirmé** et **Nécronyx sur Solar confirmé avant le coup final**. Sinon retourner unsafe/Unknown, pas victory=true. Sur un kill accepté, réconcilier les effets de fin de combat réels.
8. **TRANSPORT_ACK / REFUSED / UNKNOWN** : les changements hypothétiques restent séparés des états confirmés ; pas de réapplication destructrice de Necro ni double consommation de phase sur un simple ACK.

**Sorties du calcul** : contexte, horloge de phase correcte, état invulnérable, prochain événement de transition prévu, garantie ou non de fenêtre Nadir, état Necro à l'instant du kill, renvoi Pyrozomb pour cibles et alliés, disponibilité d'attaque par acteur et raison d'échec. Si un champ vital est absent, résultat **Unknown** plutôt qu'une transition supposée.

## Replay ciblé et deux contre-exemples minimaux

| ID | Contexte / scénario | Assertion testable |
| --- | --- | --- |
| **P-M28-1 Songes 3.7** | Etat Aurore/limite de phase, un **round global** avance alors que Solar n'a pas joué un nouveau tour propre ; état Solar avant/après capturé | Le changement est lu à la frontière **globale** et non déduit d'un SolarTurnStarted ; timing exact à déduire des logs |
| **N-M28-1 donjon ≠ Songes** | En Tour de Solar, round global progresse avant le prochain tour de Solar, phase inchangée | Le simulateur **ne doit pas** déclencher un changement de phase au seul round global de donjon |
| **N-M28-2 kill sans Necro** | Nadir confirmé, Solar mourrait du prochain coup, aucun Nécronyx effectif | Prévision « wipe de l'équipe » selon guide ; ne pas choisir ce kill comme sûr |
| **P-M28-2 kill sûr conditionnel** | Nadir confirmé, A avant B applique Nécronyx confirmé ; B a les dégâts requis et agit avant la transition | Conditions de sécurité respectées avant le kill ; résultat serveur attendu, pas victoire déduite |
| **N-M28-3 horloge périmée** | Songes : B lance Necro, un événement global modifie la phase avant C | La décision de C est invalidée, relire phase, Necro et invu |
| **N-M28-4 cast ancien patch** | En 3.7, A pose Nécronyx avec un autre sort disponible et assez de PA | A **peut continuer son tour** ; l'ancien modèle « passe automatiquement son tour » est refusé |
| **N-M28-5 ressuscité** | Pyrozomb ressuscite M1 après sa mort et un ennemi suivant agit | Ne pas donner d'**invu 1 tour** ancienne ; gérer l'effet Necro séparément |
| **N-M28-6 attaque hors Nadir** | Solar au Zénith confirmé, un sort cible Solar | Invulnérabilité de phase conservée malgré suppression de l'ancien verrou post-résurrection |

Tous ces tests sont **proposés, non exécutés** ; aucun log de turnId/cellId/PV n'est disponible. Les valeurs numériques de phases et sorts sont dans [FICHE_SOLAR.md](./FICHE_SOLAR.md) avec citations vers le guide de donjon.

## Un tour par taille d'équipe

- **Solo (A)** : conserver la disponibilité de Nécronyx pour un tour Nadir où A peut poser le statut puis attaquer, puisque 3.7 ne termine plus son tour à la pose. Si l'attaque seule ne tue pas, prévoir une autre fenêtre plutôt que tenter un kill sans l'effet.
- **Duo (A→B)** : A pose Necro avant B, B frappe Solar en Nadir. En inversion d'initiative B→A, le plan « Necro puis burst B » n'est **pas** réalisable dans le même round si B doit jouer avant A ; l'échec est un test discriminant.
- **Trio (A→B→C)** : A contrôle Moribombe/monstres, B pose Necro, C attaque. Une résurrection intermédiaire n'ajoute plus d'invu d'un tour mais peut encore imposer des risques de Pyrozomb. PA et cooldown par personnage.
- **Quatuor (A→B→C→D)** : A tank, B initie Necro, C/D frappeurs ; en Songes, un passage de **round global** peut précéder le tour suivant de Solar, donc vérifier l'état pour **chaque acteur**. L'expédition Audace/Bravoure est un autre mode et ne doit pas ajouter ses glyphes dans les Songes sans preuve.

## Problèmes de sources, horodatages et validation restante

**Vidéo V1 vérifiée** : Deplai, [« FARM 100 DONJONS SOLAR ... TEAM SADI 3.6 »](https://www.youtube.com/watch?v=3JEP1fidMkw), **15 juin 2026**, 9 min, boss [3:15](https://www.youtube.com/watch?v=3JEP1fidMkw&t=195s) **tiré de la description** de get_video_info ; transcript fr MCP échoué par IP YouTube, seule langue indiquée en auto. Aucune règle attribuée à une parole ni capture du plateau. Cette vidéo montre un **donjon de version 3.6** par son titre, pas Songes 3.7. Le déclencheur global en Songes provient uniquement du [patch 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html).

**Priorité d'intégration :** élevée. Une erreur d'horloge peut faire lancer un burst pendant l'invulnérabilité ; une erreur de condition Nécronyx peut mener au wipe. Vérifications bloquantes sur 3.7.13.17 : un enregistrement Songes et un donjon avec **mêmes états initiaux**, événements de roundGlobal et solarTurnId, Necro posé avant/après une transition, durée de Nadir, expiration de Necro, retour Aurore, et une résurrection. Prover toute relation précise entre fin de tour global et effet résolu plutôt que de la supposer. Sources 3.7 établissent le **type d'horloge**, pas un instant sous-seconde précis.

**Statuts :** corrobore-source et hypothese ; ancien comportement obsolete-pour-cible. Pas de declare-video, observe-video, valide-test ni valide-live.
