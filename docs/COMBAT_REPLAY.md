# Replay de combat

Pour comprendre une défaite, une perte de temps ou une décision de l'IA — en **MITM comme en Socket** (même flux de journal).

## Ce qui est enregistré
Givre lit les lignes `[combat]` que le moteur écrit déjà (`TransportLog`), découpe les combats (du premier ordre d'attaque / placement jusqu'au bilan, à la sortie ou à l'inactivité),
et classe chaque ligne : **tour**, **PV**, **sort**, **déplacement**, **sort sauté**, **fin de tour**, **défi**, **souci** (`!!!` : refus, annulation, ECHEC d'envoi, tour perdu…), **bilan**.
Une ligne `PV tour N : moi … | alliés […] | ennemis […]` est ajoutée par le moteur au début de chacun de VOS tours (PV absolus, case par combattant).

## Ce que dit le résumé
Issue (victoire / défaite / nul), durée, nombre de tours, sorts, déplacements, soucis, envois en ECHEC, déplacements refusés, tours sans sort, **plus longue pause** et ce qui la précédait, XP et butin, puis des **constats** :
aucun sort lancé du tout, tours sans attaque, « aucun sort castable » (portée / ligne de vue / PM / tacle), déplacements refusés, tours perdus, sorts sautés (conditions du profil), PV tombés sous 50 %, tours de plus de 20 s,
défis échoués, combat très long.

## Où le lire
- **App** : Outils > « Replay de combat · décisions de l'IA » (liste des combats, défaites en rouge, texte complet, copier, ouvrir le dossier).
- **Fichiers** : `…/userdata/combat/AAAA-MM-JJ/HHmmss_pid<N>_<issue>.log` (30 jours).
- **MCP** : `list_combat_journals(bot?, lostOnly?, limit?)` puis `get_combat_journal(id)`.
- **Téléphone** : onglet « Combats ».

## Limites
- Les décisions affichées sont celles que le moteur **journalise déjà** ; ce qu'il ne dit pas (pourquoi une cible plutôt qu'une autre) n'apparaît pas — on peut ajouter des lignes `[combat]` au fil des besoins.
- Un combat dont le bilan n'arrive pas est clos par inactivité (12 min) ou à l'arrêt de l'application, avec la mention correspondante.
- Les constats sont des **pistes** déduites des lignes, pas une preuve.
