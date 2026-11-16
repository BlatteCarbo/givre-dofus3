# Fiche M25 — Founoroshi 3.7 / portail Éliotrope / glyphe

## Règle atomique prioritaire

Le texte de [la mise à jour 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) signale que les glyphes de feu de Founoroshi **ne désactivent plus** les portails de l'Éliotrope. Statut `corrobore-source` pour la documentation, `hypothese` pour toute transition serveur encore inconnue.

## Plateau à instrumenter

- `bossId`, cellule du boss, états de déplacement/tacle, `bossTurn`.
- `portalId`, cellule, propriétaire, ordre et statut réellement observé.
- `glyphId`, cellules, origine, moment de création et d'application.
- `allies` : positions, PV, protections et tour suivant.
- `globalTurn` et `lastConfirmedServerEvent`.

## Reconstitution de tour proposée

1. Avant le tour : un portail P ouvert se trouve dans une zone susceptible d'être recouverte par G.
2. Événement serveur : G est créé, le plateau est réobservé.
3. Événement serveur : P est explicitement marqué actif/inactif ou son statut reste Unknown ; seule une preuve autoritaire permet de trancher.
4. Un allié tente son plan de route en utilisant P **si** le statut et la géométrie ont été confirmés ; sinon repli de déplacement classique.
5. À l'action du boss, comparer le danger réel et le déclencheur « bloqué pendant un tour entier » au risque historique.

## Deux contre-scénarios

**N01** : G créé mais P n'est pas confirmé ; route de portail refusée par prudence (pas de sortie inventée). **N02** : initiative du placeur après celle du tireur ; aucun avantage de placement anticipé. **N03** : vidéo 3.6 relatant fermeture de portail ; obsolescence possible en 3.7, conserver divergence.

## Enseignement pour les compositions

Le duo Éliotrope/Féca peut préserver une issue ; trio Éliotrope/Pandawa/Crâ utilise placement et portée ; quatuor ajoute soins. Pour chacun, le bénéfice n'est pas donné par le nom des classes : il dépend des sorties observées et du danger évité. Aucun nombre de PA/PO/dégâts n'est fixé par cette fiche.

