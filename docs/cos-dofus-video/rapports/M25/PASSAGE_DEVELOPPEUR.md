# M25 — Passage développeur

**Trois règles à rendre testables** :

1. L'état d'un portail est une donnée observée et persistée ; un glyphe adverse ne le ferme pas implicitement. Source : [correctif 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). État cible 3.7.13.17 à confirmer.
2. La traversée d'un portail implique l'ID et la cellule de sortie ; l'environnement G peut changer le danger tactique même sans fermer le portail.
3. Un déclencheur lié à « bloqué pendant un tour complet » demande une durée, un statut fiable et un événement du boss ; une simple tentative de tacle n'en établit pas l'issue.

**État** : `Portal(id,owner,cell,open,order)`, `Glyph(id,source,cells,trigger)`, `Boss(bossId,turn,blockedSince)`, `CombatEvent(source,time,confirmed)`.

**Tests proposés** : (P) entrée P ouverte, glyphe ajouté, portail encore ouvert selon source 3.7 et nouveau statut confirmé ; (N1) statut de P inconnu → ne pas utiliser un raccourci non prouvé ; (N2) version historique différente → ne pas transposer ; (N3) dernier déplacement refusé → nouveau plan depuis l'ancienne cellule ; (N4) deux tours d'initiative différents → route replanifiée. Aucune exécution de tests ni trace serveur.

L'équipe doit disposer de données du client 3.7.13.17, de 2–3 captures aux mêmes cellules et d'un replay expurgé montrant les événements glyphe/portail. Ne jamais conclure `valide-test` ou `valide-live` sans artefact.

