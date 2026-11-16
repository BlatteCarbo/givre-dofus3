# N07 — Passage développeur : différences de versions strictement versionnées

**Livraison complète :** comparaison hors jeu du client local **3.7.13.17** avec des exports publics identifiés **3.7.12.12**. Les quatre tables pour Sram/Roublard/Xélor/Éliotrope sont **comparées et identiques** : **88 variantes, 176 sorts, 364 grades et 2040 effets**, zéro champ ajouté/supprimé/modifié, toutes les archives anciennes vérifiées SHA256. [Rapport](RAPPORT.md), [preuves](PREUVES.md) et [manifeste machine](SOURCE_COMPARAISON.json).

## Trois invariants prioritaires

| ID | Règle d'intégration | Justification / preuve |
| --- | --- | --- |
| **N07-R01** | Une donnée ne porte la version `3.7.13.17` que si les octets du client installé correspondent au manifeste [N01](../N01/CLIENT_SOURCES.json) ; l'ancien jeu doit être sourcé séparément, avec sa release et ses hashes. | [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json) et [release 3.7.12.12](https://github.com/ledouxm/dofus-sqlite/releases/tag/v6.0_3.7.12.12). |
| **N07-R02** | Clés de comparaison **techniques**, jamais noms : `breedId,variantId`, `spellId`, `spellId,grade`, `spellId,grade,branch,effectUid` pour effets (repli ordinal marqué si UID absent). Le `effectUid` et le `actionId` restent deux champs distincts. | [DIFF CSV](DIFF_3.7.12.12_VS_3.7.13.17.csv), [compare_spell_versions.py](compare_spell_versions.py), [N01/EFFECTS](../N01/EFFECTS_3.7.13.17.csv). |
| **N07-R03** | Un résultat `blocked` dans `SOURCE_COMPARAISON.json` signifie **absence de comparaison**, pas zéro delta. Toutes les catégories de cette exécution finale portent `compared`, avec **0 delta**. Cela ne certifie jamais comportement, cast, durée ou action serveur identiques. | [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json) et [PREUVES.md](PREUVES.md) : trois hashes anciens validés et code retour 0. |

## Usage concret des fichiers

Lire [SOURCE_COMPARAISON.json](SOURCE_COMPARAISON.json) puis filtrer le [DIFF_3.7.12.12_VS_3.7.13.17.csv](DIFF_3.7.12.12_VS_3.7.13.17.csv) sur `entity` et `stableKey`. Si un champ `statesCriterion,apCost,range,maxCastPerTurn,initialCooldown,actionId,targetMask,zoneDescr` change, créer un cas de non-régression **par grade**, mais pas une correction automatique sans tester la signification de l'effet. Un changement `spellName/nameId` relève d'abord de l'affichage.

Garder les règles courantes de `GameNameResolver.cs:374-449,556-705` sur rang, ciblage et conditions comme consommateurs possibles de données ; aucune modification de ce fichier n'a été faite ici. Leurs données dérivées demandent une provenance et des SHA256 associés ; toute ancienne valeur cache sans preuve doit être rejetée ou classée `Unknown`.

## Tests proposés (aucun exécuté)

| Cas | Entrée | Résultat attendu |
| --- | --- | --- |
| P1 vérification des sources | trois tables anciennes téléchargées avec tailles/digest de publication | `comparison.variant/spell/level/effect.status=compared`, 88+176+364+2040 lignes comparées ; zéro différence de champ. **Exécution réelle du comparateur réussie**, test moteur non exécuté. |
| N1 téléchargement tronqué | `spelllevelsdata.3.7.12.12.json` tronqué avant sa taille attendue 200736289 | `comparison.level/effect.status=blocked`, aucune affirmation « aucun changement de grade ». |
| N2 nom modifié | même `spellId`, texte I18n différent | aucune suppression/ajout de sort ; comparaison par ID, distinguer affichage. |
| N3 effet décalé | ajout d'effet au début d'un tableau d'un grade | diff marque l'ajout d'UID et le déplacement d'ordinal des effets existants, au lieu de les comparer exclusivement par index ; vérifier toute renumérotation d'UID avant d'affirmer une transformation. |
| P2 cycle serveur | même ligne de données avant/après version | oracle combat à obtenir séparément ; `compared` de données ≠ `valide-live`. |

**État de cette mission :** extraction et diff des données **terminés et reproductibles**, sans commande en jeu, bot, Git, compte ou processus du jeu. Le prochain contrôle utile concerne l'évolution possible des **scripts du moteur** et le comportement serveur malgré les tables identiques. Les validations du modèle (offline) et du combat (séparées) restent non exécutées ; une absence de delta dans les tables étudiées ne saurait les remplacer.

