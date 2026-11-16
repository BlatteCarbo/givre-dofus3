# Attente du premier déplacement — 24 octobre 2026

Signalement : certains premiers tours paraissent plus lents que les suivants. L'instance observée est toujours le binaire Dakal PID 6740 ; elle n'est pas redémarrée pour ce correctif.

## Observation

Lecture ciblée du journal courant, après 18:50 : 60 premiers tours et 292 tours suivants. Délai médian entre début de tour et première requête d'action : 1 ms dans les deux groupes. Durée médiane du tour propre : 135,5 ms au premier tour contre 80 ms ensuite ; les tours sans requête restent dans cette statistique de durée. Le chargement de carte/sorts et la construction initiale de rotation avant la ligne `TOUR` prennent 0–1 ms sur ces premiers tours : pas de chargement à froid démontré dans cet échantillon. Cela ne mesure pas toute la phase de placement ni le temps des autres combattants.

Cas précis à 19:21:04 : dernière séquence terminée à .081, tour 1 ouvert à .085, case de tir décidée à .086, déplacement envoyé à .391. Les ~305 ms entre décision et envoi correspondent au plafond Turbo de `WaitMoveClear` (300 ms, plus planification). D'autres premiers déplacements apparaissent à ~299–317 ms. Des tours suivants peuvent rencontrer la même attente ; elle n'est pas exclusive au round 1.

## Correction

La baseline de début de tour exigeait une nouvelle fin de séquence même si aucune séquence n'était ouverte. Le moteur peut maintenant utiliser la fenêtre de calme réseau existante (20 ms en Turbo) quand le serveur a ouvert notre tour et que la profondeur de séquence connue est zéro. Pas de raccourci sur une profondeur inconnue/ouverte, sans autorité de tour, hors de notre tour ou en vitesse normale.

La resynchronisation du compteur de séquences au `FightTurnEvent` pouvait masquer une séquence commencée avant cet événement. Son existence est mémorisée avant le reset ; dans ce cas, l'attente antérieure est conservée. Les conditions historiques de fin de séquence et de repli pour mapping absent restent inchangées.

Tests de la politique et de la vraie méthode `WaitMoveClear`, sans réseau : retour immédiat quand la séquence est déjà fermée et la fenêtre de calme satisfaite ; attente maintenue pour une séquence ouverte ; attente maintenue si le reset du tour masque une séquence préexistante ; reprise à réception de sa fin. Le gain en session sur le nouveau binaire reste à mesurer : aucun gain de 300 ms systématique à chaque combat n'est annoncé.

Validation finale : build Release App réussi (208 avertissements, zéro erreur), suite complète **2 811 réussites, zéro échec, deux ignorés**, résultat local `first-turn-turbo-final.trx`. Dictionnaires fr/en/es : 2 701 clés uniques chacun. `git diff --check` passe. Publication séparée : `%USERPROFILE%/Desktop/GivreClone-Turbo-Tour1-20261024/GivreClone.exe`.
