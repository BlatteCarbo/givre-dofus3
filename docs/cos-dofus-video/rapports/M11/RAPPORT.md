# M11 — Gourlo le Terrible : résistance et déverrouillage par Tonneau Pirate

**04/11/2026 — étude documentaire boss, sans vidéo ni replay serveur.** Ce premier exemple illustre une mécanique de donjon où l'initiative d'une invocation ennemie et l'ordre des cibles importent plus que les dégâts directs. Le correctif exact 3.7.13.17 n'est pas certifié sur la source.

## Sources réellement lues

- [Dofus Grimoire, boss](https://dofus-grimoire.fr/boss/), base affichée 3.7.12 du 20 octobre 2026 : Gourlo dispose de résistances initiales de 200 % partout ; tonneau adjacent au boss à son tour → perte de 200 % dans l'élément du tonneau, durant trois tours selon le texte de la fiche. Le tonneau soigne aussi, avec effets potentiels sur les personnages voisins.
- [OpenDofusDB, Gourlo](https://openddb.dev/monsters/1051-gourlo-le-terrible) : données du jeu répertoriées pour le monstre, cinq éléments à 200 % et sort d'invocation de Tonneau Pirate ; la version exacte de ce relevé doit être associée à l'extraction.
- [Dofus pour les Noobs, succès spéciaux](https://www.dofuspourlesnoobs.com/les-succes-speciaux.html) : le challenge spécial **Tonneaux truands** exige que tous les ennemis subissent au moins une fois les effets d'un tonneau pirate ; le guide mentionne une activation au troisième tour des tonneaux. Source communautaire dont la date affichée et la version doivent être reliées au correctif actuel.
- [Dofuserie, donjon et succès](https://www.dofuserie.com/donjons/cale-de-l-arche-d-otomai/) : liste de succès, « Premier » et « Tonneaux truands », avec des conditions d'ordre potentiellement opposées.
- [Next Stage, stratégie avril 2025](https://www.next-stage.fr/2025/04/guide-arche-dotomai-comment-vaincre-gourlo-terrible-dofus.html) : témoignage historique d'une fenêtre autour du troisième tour d'invocation ; ne fixe pas la version cible.

**Recherche vidéo :** la recherche web menée sur « Gourlo le Terrible tonneau DOFUS 3 2025 2026 tutoriel » n'a pas produit de lien YouTube précis et vérifiable dans cette passe. **Aucune transcription MCP, capture vidéo, trame serveur ni combat en direct pour M11.** Les chiffres proviennent des sources communautaires, pas d'un test de l'IA.

## Machine à états provisoire

```text
Boss / résistances inconnues → résistance élevée observée
               |
               | Tonneau T invoqué (ID, équipe, élément, cellule, tour)
               v
T vivant, compteur/tour prochain à confirmer
               |
               | tour du tonneau + adjacency avec boss E
               v
Effet sur E confirmé (élément, durée, source tonneau)
               |
               | E maintenant vulnérable à une voie donnée
               v
Dégâts alliés légaux, cible E, succès/ordre de kill respectés
               |
               | expiration / destruction T / retour de phase
               v
État de résistance réévalué d'après les observations
```

**Incertitudes majeures** : initiative des tonneaux, condition exacte « troisième tour », élément et état du tonneau, durée réelle, effets sur alliés et possibilité de cumul, influence des variantes de monstre ou patch. Le guide Grimoire et les sources plus anciennes ne suffisent pas à remplacer une trace serveur.

## Tests proposés

**P01 — ouverture par tonneau adjacent.** Boss E avec résistances initiales confirmées, tonneau T d'élément connu et vivant, T joue alors que E est adjacent. Attendu : effet daté attribué à T, valeur/durée d'état correspondant à l'événement réel, dégâts calculés seulement après la mise à jour. Test non exécuté.

**N01 — tonneau loin du boss.** Identique mais E éloigné au moment du tour de T. Attendu : aucune ouverture fictive. Test non exécuté.

**N02 — tonneau tué avant sa fenêtre.** Le tonneau disparaît avant sa prochaine action. Attendu : toute attaque planifiée « après délock » est annulée ou recalculée. Test non exécuté.

**N03 — succès Tonneaux truands.** Deux ennemis E1/E2 ; seul E1 a reçu l'effet de T. Attendu : la condition `affectedByBarrel[E2]` reste false ; ne pas achever E2 si la règle du succès le prohibe. Test non exécuté.

**N04 — succès Premier incompatible.** Un plan pensé pour « Tonneaux truands » garde Gourlo en dernier ; s'il faut valider « Premier », le plan doit changer d'objectif plutôt que de déclarer les deux succès acquis. Test non exécuté.

## Passage au développeur

Les réducteurs doivent suivre `summonId / owner / cell / alive / turn`, `resistanceByElement`, `statusEffectSource`, `statusExpireTurn`, `challengeTargetAttribution`. Événements de type invocation, début de tour d'entité, application d'effet, mort et changement de résistance à recouper sur une vraie trace. Une vidéo ou la description d'un boss ne donne ni une formule complète ni un identifiant serveur validé.

