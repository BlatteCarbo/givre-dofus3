# N01 — Passage développeur : données client et ordonnanceur

**Livraison :** données brutes réelles DOFUS 3.7.13.17, 4 classes, 176 sorts, 364 grades, 2 040 effets normal/critique, 88 variantes, fichiers JSON+CSV sous ce dossier. **Aucun changement de code ni test live**. [RAPPORT](RAPPORT.md), [preuves](PREUVES.md), [source manifeste](CLIENT_SOURCES.json).

## Trois règles d'intégration prioritaires

| Priorité | Contrat concret | Référence de preuve |
| --- | --- | --- |
| **N01-R01 : ID et grade** | Lier `SpellVariantData.breedId,spellIds[]` → `SpellData.id` → `SpellLevelData.spellId + grade` ; `SpellLevelData.id` identifie l'enregistrement de niveau, ce n'est pas le `spellId`. | Les 176 entrées de [SORTS_3.7.13.17.json](SORTS_3.7.13.17.json), les 364 entrées de [LEVELS](LEVELS_3.7.13.17.csv), vérifications `missingSpells=[]` / `danglingLevelIds=[]`. |
| **N01-R02 : effets séparés** | Pour chaque grade conserver **l'ordre de tous** les effets normaux et critiques, chacun avec `effectUid`, `baseEffectId`, `actionId`, `targetMask`, `zoneDescr`, `delay`, `duration`, `triggers`, `effectElement`, dés. Ne pas inventer un unique effet/masque/zone par sort. | [EFFECTS](EFFECTS_3.7.13.17.csv), exemple Roublard Explobombe actionIds 1008/406/951, Sram Mortel et Répulsif actionId=400 avec **spellId distinct**. |
| **N01-R03 : données ≠ résultat** | Appliquer `apCost`, `range`, `m_flags`, `statesCriterion`, cooldown/limites au **grade équipé** et à l'état réel ; confirmer séparément lancer et effets serveur. Un champ de base ne garantit pas le coût final d'un sort dynamique ni la résolution d'un piège. | [LEVELS](LEVELS_3.7.13.17.csv), `GameNameResolver.cs:374-449,556-705` et [Grimoire Roublard](https://dofus-grimoire.fr/classes/roublard/) pour nuance des bombes. |

## Plan de raccordement, sans implémentation

Charger un `ClientSpellSnapshot` versionné (Version/BuildDate/SHA256 des bundles). Refuser toute fusion si le manifeste du disque ne correspond pas ou si le `spellId/grade` n'appartient pas au mapping des variantes. Utiliser les CSV pour diagnostic ; le JSON brut conserve tous les champs natifs et doit rester l'oracle d'extraction.

Préparer une projection structurée `SpellId, Grade, SpellLevelId, CastRules, NormalEffects[], CriticalEffects[], Variants[]`. Les clés d'identité des effets doivent être explicitement distinguées : `effectUid` et `actionId` **ne sont pas des IDs de sort**. La clé `effectIndex` est seulement l'ordinal dans la liste du grade et de la branche. Une comparaison d'effets entre versions doit conserver l'ordre et regarder aussi les identifiants, sans présumer leur immutabilité.

Le code existant `GameNameResolver.cs` lit le grade et plusieurs champs de manière efficace, mais agrège certains effets pour un choix tactique ; une branche Roublard ou Sram ne peut pas perdre les **autres zones**. `SpellDamageInfo` est une synthèse de dommages et n'est pas une image de l'ensemble du script de sort. Les `scriptId` et les règles spécifiques des bombes, des pièges et des portails doivent continuer à être traités comme des comportements à prouver.

## Tests hors jeu à concevoir (non exécutés)

| Test | Entrée | Oracle attendu |
| --- | --- | --- |
| P1 grade exact | Sram 12914 grades 1/2/3 | 2 PA chacun, portées max 5/6/7 ; pas de portée d'un grade supérieur utilisée pour grade inférieur. |
| N1 identités | Sram 12914 et 12921 ont `actionId=400` au grade1 | Deux `spellId` distincts ; aucune collision entre leur état/cooldown/effets malgré catégorie d'action commune. |
| N2 cible multiple | Explobombe 13444 grade1, 4 effets normaux avec `actionId=1008,406,951,951` | Chaque effet garde son propre masque, zone et ordinal ; pas de conversion en un unique dégât Air ou Feu. |
| N3 données manquantes | Archive avec un grade ou `spellId` absent/invalide | Refuser le snapshot ou propager `Unknown`, ne pas inventer un niveau/effet. |
| P2 build | Exports de 3.7.12.12 présentés comme 3.7.13.17 | Échec de validation de provenance ; ne pas déclarer la compatibilité de ses sorts. |

**Aucun de ces tests n'a été exécuté.** Le script d'extraction a été exécuté avec code retour **0**, ce qui ne teste ni le combat ni la simulation.

## Points à compléter

La comparaison [N07](../N07/RAPPORT.md) distingue les champs effectivement inchangés de ceux encore à comparer. Ajouter un oracle **serveur** pour cast/ACK/effets/marks/retours de refus, les masques (allié/ennemi/soi), la période de cooldown, les effets conditionnels et les modifications dynamiques de PA. Le modèle doit conserver `Unknown` sur toute règle absente, y compris les formules de mur Roublard et la sortie de portail Éliotrope. Les quatre classes ont des données extraites ; cela ne valide **aucune victoire en jeu**.

