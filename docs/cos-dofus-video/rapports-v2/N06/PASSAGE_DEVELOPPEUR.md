# N06 — Passage développeur : portails Éliotrope / Téléfrags Xélor

**Conclusion à transmettre :** **décodage de permutation générique disponible ; mécanique Éliotrope et passif Téléfrag non démontrés** par les artefacts de 3.7.13.17. Ne pas faire passer des scénarios M03/M04 pour des validations du client. Détails : [RAPPORT.md](RAPPORT.md), [PREUVES.md](PREUVES.md). Aucune édition du code, ni test, ni invocation de jeu/natif.

## Trois priorités

1. **N06-R01 — Conserver une observation d'échange et ses limites.** Champs réels du catalogue `data/fields.json:2431-2435,3348-3386` : `exchange_positions=4`, `source_id=36`, sous-types `target_id=1, caster_cell_id=2, target_cell_id=3`. Source `MapFightReader.cs:127-141`. L'appelant `LaunchService.Packets.cs:1106-1125` a une **heuristique** sur anciennes vs nouvelles cellules et met à jour deux positions ; aucun champ `telefrag`, `spellId`, `stateId` ou `AP refund` n'est associé ici. Sur résultat ambigu, **Unknown** plutôt que swap automatique dans un nouveau moteur. Attention : commentaire de test `CombatForcedMovesReplayTests.cs:281` avec numéros anciens ; corps du test `:282-287` résout les noms depuis le catalogue.
2. **N06-R02 — Refuser un calcul de routage à partir du mauvais « Portal ».** `data/fields.json:6271-6276` donne `PortalInformation.portal_id` et `PortalUseRequest.portal_id`. Aucun de ces deux messages n'établit l'identité, le centre, le propriétaire, l'âge ou le statut ouvert/fermé d'un portail Éliotrope. `SpellBoard.ThroughPortal` (source `SpellResolver.cs:18-21`) est un **booléen, pas un graphe**. Pour bâtir un graphe, il faut d'abord identifier une **source primaire de portails de combat** avec création, fermeture/Neutral, réouverture, remplacement, ordre de pose, position et provenance acteur. Les égalités/LoS de `M04/PASSAGE_DEVELOPPEUR.md:5-11,33-50` restent **calibrages proposés**, pas des lois binaires.
3. **N06-R03 — Ne pas convertir un échange en Téléfrag ni en +2 PA.** `SpellDescriptor.cs:61-75` : action ID **8/1023** représente un effet de `Swap`, **pas un sort Xélor**. `SpellResolver.cs:161-176` effectue un déplacement générique, sans application de passif. `docs/validation/combat-state-combo-20261103.md:54-61,112-128` indique l'absence de réduction fiable des états live. Pour attribuer un Téléfrag et PA, demander pour **le même combat/tour** le sort lanceur, l'événement de permutation confirmé, les positions avant/après, l'état sur chaque entité, la consommation éventuelle et `points_variation` de PA autoritatif. Le passif `+2` et son plafond par sort/tour de M03 ne peuvent être certifiés par un seul échange.

## Tests discriminants proposés (jamais exécutés ici)

| Test | Entrée | Résultat utile pour départager |
| --- | --- | --- |
| **N06-E1 tie-break** | Trois portails de combat observés, deux à distance égale d'entrée sous une métrique candidate, puis ordre d'âge inversé | Si sortie suit âge ou orientation, les deux règles divergent ; garder résultat **Unknown** sans sortie réellement observée. |
| **N06-E2 fermé vs obstrué** | B fermé confirmé, puis B ouvert avec LoS obstruée mais même géométrie | Différence entre **sélection du réseau** et **légalité du sort** ; ne pas modifier l'ensemble de portails seulement parce que le tir a été refusé. |
| **N06-X1 destination** | Téléportation sur case libre vs cas occupé, identité de sort/caster constants | Relever `exchange_positions`, état Téléfrag et delta PA ; pas de bonus spéculatif sur une téléportation libre. |
| **N06-X2 récompense unique** | Deux échanges même générateur/tour vs deux générateurs/tour | Comparer delta PA réellement observé ; vérifier la clé `acteur, sort, tour` avant toute politique de remboursement. |
| **N06-X3 retour sans historique** | Échange puis modification de destination précédente, sort de retour éventuellement légal | Aucun Téléfrag/récompense si retour sur cellule libre et sans échange, si la règle est confirmée ; sinon conserver l'incertitude. |

## Champs bloquants minimaux pour PRIME

**Portails** : un vrai **ID et un message de combat** de création/suppression/fermeture/ouverture, cellule et propriétaire, ordre d'âge, carte/conversion coordonnées, statut, entrée choisie, sortie effective, LoS et résultat du cast. `PortalUseRequest` tout seul ne répond à aucun de ces points.

**Téléfrag** : champ réel d'état cible et identité du sort source ; old/new cell autoritatifs, acteur et époque/tour, ordre exact de swap, `points_variation` (le nom existe `data/fields.json:3348-3386`) et résultat PA, retour/consommation de l'état, refus. Les flux de boost `jth/jun` sont **encore sémantiquement incertains** dans `docs/validation/official-client-371317-20261103.md:995-1019,1045-1062` ; ne pas les renommer Téléfrag sans lien indépendant.

Le critère de fin N06 de `VAGUE_2_HORS_YOUTUBE.md:38-39` est rempli pour le **symbole/événement manquant identifié** et pour un décodage de déplacement démontré dans le code. Ce n'est **pas** une validation du chemin réel, de la durée d'un Téléfrag ni du bonus. N06 n'a exécuté aucun test ; ne pas attribuer de `valide-test` ou `valide-live`.
