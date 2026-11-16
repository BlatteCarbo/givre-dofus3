# N02 — Incidents historiques : compagnon, buffs, déplacements

**4 novembre 2026 — inspection effective de résumés de combat existants, sans combat neuf.** L'analyseur [scan_incidents.py](scan_incidents.py) a lu uniquement les journaux privés sous `%LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/combat` ; il n'exporte **aucune ligne brute**, identité de personnage, PID, compte, adresse ou nom.

## Résultat prioritaire

L'analyse exécutée (code de sortie **0**) a parcouru **3 348 fichiers de journal** : **411** contiennent la chaîne `Prudent`, **37** mentionnent un compagnon, et un cas où `Prudent` est mentionné **dans le combat** a été retrouvé avec un tour `COMPAGNON`.

| Question | Résultat directement observé dans le résumé | Statut exact |
| --- | --- | --- |
| Compagnon Prudent avec tour sans sort | Dans un journal daté 2026-11-04 identifié par son SHA, **début de tour compagnon à +7,734 s, ligne 83**, sortie de la fenêtre inspectée à **+7,871 s** ; **0 ligne de lancement de sort reconnue**, **3 lignes de déplacement** dans cet intervalle | **Journal compatible avec un tour vide**, mais pas preuve complète de l'absence d'un cast si l'instrumentation manque ; le mot `Prudent` peut décrire la politique, pas l'identité du compagnon |
| 11 PA / 6 PM | **Aucune ligne de ressource extraite** dans le segment sélectionné | **Non démontré** : les valeurs attendues ne doivent pas être inventées |
| PM par PM | **Trois comptes-rendus de mouvement** dans une fenêtre d'environ **137 ms** du même tour compagnon | **Fragmentation apparente documentée**, pas preuve qu'exactement 1 PM a été dépensé par mouvement ; il manque les variations PM et le lien à la commande |
| Buff sans bénéficiaire | Le scan voit des mentions `buff` dans **1 101 fichiers**, mais **aucune mention de buff dans ce tour compagnon sélectionné** et aucun lien strict buff→bénéficiaire→attaque utile reconstitué | **Cause inconnue** ; une mention lexicale ne prouve pas une action inutile |

Ces métriques sont **issues de lignes de journal dérivées**, pas des octets originaux de la réponse serveur. **Aucune cause de tour vide n'est prouvée** : blocage d'admission, indisponibilité de cible, contrôle non attribué, latence d'un tour, choix volontaire de mouvement sont autant de branches possibles. L'existence de ces cas justifie un test d'identité `actorId/ownerId` avec ordre de tour, PA/PM **effectivement reçus**, ensemble des sorts accessibles et `server result` du cast.

## Livrable et méthode

Les résultats anonymisés sont dans [incidents_anonymises.json](incidents_anonymises.json) : SHA-256 du journal de référence, date seulement, numéro de ligne et secondes relatives, profils catégorisés et champs manquants. Le script Python utilise uniquement des filtres réguliers sur des journaux historiques et ne livre aucun contenu brut de compte. Pour le cas Prudent le SHA de source est `c855496fbf462fd2d219ecc4993c0fdf45b12071a11a1932c72d6259c5eefc70`. L'extraction ne garantit pas une identité de combattant : il faut la récupérer dans un événement de contrôle serveur.

Le signal d'incident a été **retrouvé**, l'explication causale exacte **reste ouverte**, notamment les 11PA/6PM et le coût unitaire en PM. Lire [PREUVES.md](PREUVES.md) et [PASSAGE_DEVELOPPEUR.md](PASSAGE_DEVELOPPEUR.md) avant tout correctif.
