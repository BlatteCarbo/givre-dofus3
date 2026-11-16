# Fiche M02 — Roublard Feu, mur et explosion en deux tours

## Identité
- ID stable : M02-ROUBLARD-MUR-EXPLOSION-v1 ; volet secondaire M02-POUDRE-MORT.
- Sujet : préparation Feu, vieillissement des Explobombes, mur, charge, déclenchement, propagation ; solo ou coopération selon position des acteurs.
- Jeu source V25 (juin 2025) / V26 (juin 2026) : DOFUS 3 annoncé dans leurs descriptions ; numéros de client exacts inconnus. Référence de données Dofus Grimoire 3.7.12 ; cible projet 3.7.13.17 à confirmer localement, donnée du [journal OpenDofusDB, 3 novembre 2026](https://openddb.dev/changelog). Aucune mécanique vidéo automatiquement transposée à la cible.
- Analyse documentaire : worker M02, 2026-11-04.
- V26 : [LES COMBOS DU ROUBLARD](https://www.youtube.com/watch?v=zzdrk6gUG58), Titiloo, 2026-06-07, 42 min ; V25 : [MAÎTRISER LE ROUBLARD](https://www.youtube.com/watch?v=8ebZ9D3prsA), Titiloo, 2025-06-29, 18 min.
- FR auto-généré pour les deux vidéos (get_available_languages), transcription horodatée complète disponible : V26 1 492 segments, V25 362 ; fenêtres lues dans [RAPPORT.md](RAPPORT.md). Une ouverture navigateur à 06:42 a confirmé le temps lecteur, mais aucune image du plateau n'a pu être inspectée : toute la colonne « visible » reste non vérifiée.

## Règles atomiques / preuves
| ID | Affirmation | Statut | Preuve exacte | Limites |
| --- | --- | --- | --- | --- |
| M02-R01 | Au début du tour du Roublard, chaque bombe du lanceur encore présente reçoit +2 Combo. | corrobore-source | [Grimoire 3.7.12, « La Ruse du Roublard »](https://dofus-grimoire.fr/classes/roublard/) | Client 3.7.13.17 non exécuté ; l'horloge « début de tour » doit être liée à l'acteur. |
| M02-R02 | Deux bombes vivantes de même type alignées engendrent un mur ; une pose d'Explobombe coûte 2 PA, puis +1 PA par Explobombe du propriétaire déjà en jeu. | corrobore-source | [Grimoire, « À savoir »](https://dofus-grimoire.fr/classes/roublard/) ; [OpenDofusDB Explobombe](https://openddb.dev/spells/13444-explobombe) | Définition exacte des cases du mur et conditions d'interruption par obstacle inconnues. |
| M02-R03 | Le tutoriel signale qu'une Aimantation qui rapproche les bombes crée le mur et que ses dégâts arrivent avant le +1 Combo dû au déplacement. | declare-video, hypothese-pour-cible | [V26 06:40–07:05](https://www.youtube.com/watch?v=zzdrk6gUG58&t=400s) | Le tableau et les nombres n'ont pas été inspectés ; ordre serveur non démontré. |
| M02-R04 | Plombage augmente de +1 Combo les bombes reliées en ligne ; Détonateur apporte +1 à la cible initiale puis la fait exploser et rend son coût de 1 PA. | declare-video, corrobore-source | [V26 01:00–01:20](https://www.youtube.com/watch?v=zzdrk6gUG58&t=60s), [V25 09:13–09:19](https://www.youtube.com/watch?v=8ebZ9D3prsA&t=553s), [Grimoire, exemple Feu](https://dofus-grimoire.fr/classes/roublard/) | Plombage 3 PA dans la base 3.7.12 ; portée et LoS exactes à vérifier sur rang de sort et serveur. |
| M02-R05 | Explosion déclenchée : rayon de 2 cases pour les bombes du lanceur, indépendamment de leur type ; en ligne jusqu'à 7 cases pour même type ; une bombe déclenchée en chaîne n'obtient pas le +1 de Détonateur. | corrobore-source | [Grimoire, réaction en chaîne](https://dofus-grimoire.fr/classes/roublard/) | Propagation transitive, ordre de la file, intersection et obstacle non validés ; distinguer dégâts d'explosion et activation de bombe. |
| M02-R06 | Poudre rend la bombe Indéplaçable ; si elle est tuée par dégâts, +2 Combo puis explosion ; Détonateur volontaire n'est pas ce déclencheur. Le transfert de Poudre sur une autre bombe peut donner +2 à la première. | declare-video, corrobore-source | [V26 02:01–02:24](https://www.youtube.com/watch?v=zzdrk6gUG58&t=121s) ; [Grimoire, Poudre](https://dofus-grimoire.fr/classes/roublard/) ; [diff 3.6→3.7](https://backfromafk.com/classes/roublard/depuis-3-6/) | Destruction : origine dégâts et moment du retrait d'effet à confirmer avec message serveur. |
| M02-R07 | Dernier Souffle ajoute temporairement +3 Combo, perdus deux tours plus tard selon V26 ; réduction de vitalité maximale du Roublard. | declare-video, corrobore-source-partiel | [V26 01:45–02:00](https://www.youtube.com/watch?v=zzdrk6gUG58&t=105s) ; [Grimoire, Dernier Souffle](https://dofus-grimoire.fr/classes/roublard/) | Date exacte d'expiration par acteur, stacking et valeur de perte de vitalité non établis en serveur ; test nécessaire. |
| M02-R08 | Si un ennemi ou son allié l'amène dans le mur avant le tour du Roublard, le mur peut être réactivé pendant ce dernier tour ; un combat long peut privilégier le mur à l'explosion. | declare-video | [V25 08:38–08:49](https://www.youtube.com/watch?v=8ebZ9D3prsA&t=518s) et [V25 17:05–17:34](https://www.youtube.com/watch?v=8ebZ9D3prsA&t=1025s) | Pas de nombre maximal de déclenchements par tour ; formulation orale seulement. |
| M02-R09 | À la pose, V26 parle de « état 1 », là où Grimoire indique « Combo 0 ». | contradictoire, hypothese | [V26 06:42–06:51](https://www.youtube.com/watch?v=zzdrk6gUG58&t=402s) ; [Grimoire, « À savoir »](https://dofus-grimoire.fr/classes/roublard/) | Possible différence rang visuel/compteur interne : aucune conversion présumée. |

## État initial reproductible proposé
Scénario principal **deux tours** ; ce sont des entrées de tests à construire, pas la reconstitution des cases montrées par Titiloo.
- Acteur R = Roublard Feu de l'équipe A, son tour T1 puis T2 ; cible E = ennemi de l'équipe B ; variantes d'Explobombe et Détonateur disponibles, pas de variante Résiliente ni Étoupille.
- Grille relative : B1=(0,0), E=(0,1), B2=(0,2), R sur une case à distance de lancer légale inconnue. Cases libres (0,0), (0,2) à l'instant de la pose ; aucune autre Explobombe du propriétaire. Cette géométrie **propose** un mur aligné avec la cible entre deux bombes ; le moteur doit vérifier la topologie réelle isométrique du client.
- Budget T1 au moins 5 PA pour B1 et B2, T2 au moins 4 PA pour Plombage + Détonateur avant remboursement ; Exemple pédagogique à 12 PA si souhaité (le reste est hors séquence).
- E survit et ne bouge pas, B1/B2 restent vivantes et ne subissent aucun déplacement jusqu'au T2 ; PV, résistances, intelligence, dommages fixes et points de bouclier non définis. Tous dommages numériques laissés inconnus.
- État bombe : bombeId unique, owner=R, element=Feu, position, alive, combo, creationTurn, lastGainSource, poudreActive=false, explosionQueued=false ; effet de mur dérivé. Aucun plafond d'invocation chiffré inféré.
- Préconditions globales : sort appris au niveau/rang voulu ; coût confirmé au lancement, portée/ligne de vue/case libre et absence de refus serveur ; identité de l'acteur propriétaire. La source OpenDofusDB indique 1–6 PO pour Explobombe et deux usages/tour, mais pas toutes les conditions booléennes validées.

## Séquence T1–T2 : état et transitions
| Étape | Acteur | Action / cible | Préconditions | Coût attesté côté base 3.7.12 | Transition attendue | Preuve |
| --- | --- | --- | --- | --- | --- | --- |
| T1.1 | R | Poser Explobombe B1 sur (0,0) | case libre, lancement légal, 0 Explobombe déjà en jeu | 2 PA | B1 vivante, combo initial **inconnu sur cible** (la base dit 0), mur absent | M02-R02/R09 |
| T1.2 | R | Poser Explobombe B2 sur (0,2) | emplacement libre, alignement B1/B2, E entre | 3 PA | B2 vivante ; recalculer le mur Feu et son segment ; E dans segment proposé | M02-R02 |
| T1.3 | E ou ennemis | Tour intermédiaire | E/B1/B2 vivants | sans objet | Bombes peuvent être frappées, déplacées ou tuées ; dans le cas positif elles survivent ; ne pas les charger à chaque tour d'ennemi | M02-R01/R06 |
| T2.0 | R | Début du prochain tour de R | B1 et B2 vivantes au début du tour | 0 PA | +2 Combo à chacune suivant base 3.7.12 ; chronologie T2 confirmée par acteur | M02-R01 |
| T2.1 | R | Plombage sur ligne des bombes | alignement et paramètres du sort légaux | 3 PA | +1 Combo B1 et B2, dégâts directs du sort potentiels, mur conservé | M02-R04 |
| T2.2 | R | Détonateur sur B1 | B1 toujours vivante, ciblable, pas de variante Étoupille | 1 PA puis +1 PA rendu selon base | +1 Combo à B1 ; première explosion de B1 ; enchaînement des bombes atteintes dont B2 si règle de propagation satisfaite ; B2 explose à son propre Combo, sans +1 indirect | M02-R04/R05 |
| T2.3 | Système | Résolution/fin | files d'événements stabilisées | aucun | Les explosions retirent les bombes concernées ; recalculer mur et occupances ; enregistrer événement source pour chaque dommage | M02-R05 |

Exemple **conditionnel aux valeurs internes Grimoire** : pose à Combo 0, T2.0 B1/B2=2, T2.1=3/3, T2.2=4 pour B1, 3 pour B2 ; ce n'est **pas** une valeur observée sur le client et ne doit pas servir de valeur oracle sans replay.

## Ordre d'Aimantation et perte du mur
Cas alternatif documentaire : deux bombes non alignées sont rapprochées par Aimantation. La parole V26 [06:40–07:05](https://www.youtube.com/watch?v=zzdrk6gUG58&t=400s) décrit « rapprochement → création/mise à jour du mur → application du dégât de mur avec Combo précédent → +1 Combo ». Conserver cet ordre **en hypothèse à vérifier** et le tester contre « +1 Combo → dégât du mur ». Si B1 meurt entre la préparation et la résolution, le modèle doit recalculer le mur à partir des entités restantes ; ne jamais utiliser un mur mémorisé sans validation. Le cas exact et la dernière case de déplacement ne sont pas disponibles sans preuve visuelle.

## Branche Poudre : deux causes de disparition qui ne s'équivalent pas
- Variante A (destruction par dégâts) : R applique Poudre à B1 (1 PA selon base 3.7.12), B1 devient Indéplaçable. Une attaque ennemie réduit ses PV à 0. V26 dit : explosion déclenchée et gain +2 Combo. La condition « morte sous dégâts » est distincte de Détonateur. Le journal serveur doit confirmer la cause du 0 PV, l'application du +2 et l'explosion avant de verrouiller le calcul.
- Variante B (détonation volontaire) : mêmes positions et Poudre active, R lance Détonateur sur B1 ; B1 explose, les effets de Poudre sont retirés, **pas de bonus +2 au seul motif de Détonateur** selon l'auteur et la description actuelle. Test négatif discriminant.
- Variante C (changement de bombe) : Poudre est transférée à B2, B1 reçoit +2 et perd l'état ; V26 02:16–02:24 le décrit, [texte 3.7](https://backfromafk.com/classes/roublard/depuis-3-6/) le maintient. Il faut différencier transfert explicite et simple expiration.

## Intérêt tactique
Le plan vise les dégâts de mur pendant la préparation et les explosions au tour suivant, au prix de 2 invocations vulnérables et de plusieurs PA. En solo, V25 [05:50–06:08](https://www.youtube.com/watch?v=8ebZ9D3prsA&t=350s) avertit du danger de rester près des bombes et de leur mort ; en équipe, un placeur peut introduire un ennemi entre elles et en tirer profit si l'ordre des tours le permet. Un allié qui fait entrer E dans le mur avant le tour de R ne dispose pas du budget PA de R. Si B2 tombe ou que E s'éloigne, repli : dégâts directs/placement légal disponibles ; le « gros burst » n'est plus estimable par la séquence à deux bombes.
La formule d'explosion, le multiplicateur et la séquence exacte des dommages face à résistances/boucliers/états sont **inconnus pour la cible**. Les exemples de valeurs élevées de V26 sont des cas d'équipement ; aucun chiffre de dégâts n'est utilisé comme formule. Les glyphes, les pièges, les challenges et l'effet exact sur alliés/compagnons ne sont pas déduits d'une analogie avec les murs.

## Scénarios de tests proposés (non exécutés)
| Cas | Entrées | Actions | Attendu observable / discriminant | Statut |
| --- | --- | --- | --- | --- |
| Positif mur + âge + chaîne | R 12 PA, B1/B2 Feu aux positions relatives indiquées, E dans le segment, tours R/E/R, B1/B2 survivantes | T1 pose B1 et B2 ; T2 Plombage puis Détonateur B1 | Naissance du mur après B2 ; +2 uniquement au début de T2 de R ; +1 Plombage sur les deux ; +1 Détonateur seulement sur B1 ; disparition des bombes atteintes et du mur. Dégâts à lire dans log, **sans nombre prédit**. | proposé, non exécuté |
| Négatif destruction du mur | Même état, mais B2 est détruite par E à T1 | T1 B1/B2 ; E tue B2 ; T2 Plombage/Détonateur tentés | B2 absente : pas de mur à deux bombes, pas de charge sur B2 ; la routine doit invalider les actions dépendantes. | proposé, non exécuté |
| Négatif Poudre non déclenchée | Une B1 sous Poudre, même Combo et PV ; deux branches indépendantes | (A) E tue B1 par dégâts ; (B) R Détonateur B1 | (A) chemin « destruction » avec +2 et explosion ; (B) chemin « déclenchement volontaire » sans +2 Poudre ; source d'événement différente. | proposé, non exécuté |
| Comparaison d'ordre | Deux bombes non alignées de même type, Aimantation provoque leur alignement, cible dans le futur mur | Déplacer par Aimantation, consulter log d'ordre et Combo | Distinguer dégâts du mur au Combo avant +1 et après +1 ; aucune hypothèse figée sans capture/replay. | proposé, non exécuté |
| Négatif propagation mixte | B1 Explobombe, B2 Bombe à Eau proche (≤2 cases), B3 Sismobombe éloignée (>2 cases) et non de même type aligné | Déclencher B1 | Attendre B2 touchée par cercle, B3 non déclenchée selon source 3.7.12 ; valider portée/topologie en replay avant code définitif. | proposé, non exécuté |

## Passage au développeur / reprise
Voir [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md). Il faut ensuite une capture nette de V26 à 06:40–07:05 avec ordre affiché, un journal de combat expurgé T1/T2, des descriptions officielles 3.7.13.17, les règles de portée/obstacle et les effets précis des explosions sur les cibles. Les tests restent proposés. Aucune validation locale ou live.

