# M13 — Passage développeur : portée / LdV / recul Crâ

**Dossier de recherche uniquement.** Voir [RAPPORT](RAPPORT.md) et [FICHE_CRA_PORTÉE_LDV_RECUL](FICHE_CRA_PORTÉE_LDV_RECUL.md). Source de sorts affichée **3.7.12** ([Dofus Grimoire](https://dofus-grimoire.fr/classes/cra/)), évolution 3.6→3.7 ([Back from AFK](https://backfromafk.com/classes/cra/depuis-3-6/)), cible locale supposée **3.7.13.17**. Tutoriel équipe 2026 [XAWPIYA à 04:26](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=266s) transcrit ; tutoriels Crâ [2026 duo](https://www.youtube.com/watch?v=5yqgq2xnWF8) et [3.6 Air](https://www.youtube.com/watch?v=Xb3p31R1-J0) ont un blocage IP de transcript, pas de preuve de leurs paroles.

## Trois règles prioritaires

1. **M13-R01 : calculer la légalité de visée** avec `actorId`, `spellId/rank/variant`, `minPO`, `maxPO`, `poModifiers`, `castInLine`, `needsLineOfSight`, `obstacles`, `targetCell`, `cooldown`. [Harcelante sans LdV](https://dofus-grimoire.fr/classes/cra/) ; [Acuité Absolue 2 PA relance 4](https://dofus-grimoire.fr/classes/cra/) supprime LdV mais *augmente la portée minimale* — une cible au contact peut devenir illégale.
2. **M13-R02 : appliquer la poussée confirmée**, [Recul rang 3 : 3 PA / PO 1–8 / 2 cases poussées](https://dofus-grimoire.fr/classes/cra/), versus portée 6 en 3.6 ([diff patch](https://backfromafk.com/classes/cra/depuis-3-6/)). Attendre l'état serveur de position : E(5,0)→E(7,0) **seulement** si la voie après E est libre et l'effet confirmé. Consigner collision, immunité et conséquences sur tacle, danger allié, challenges.
3. **M13-R03 : budget et ordre du tour**, [Cinglante 2 PA / -2 PM candidat](https://dofus-grimoire.fr/classes/cra/), Persécutrice 4, Harcelante 3, Recul 3 = **12 PA**, **0 PM** de déplacement C, sans présumer du retrait. Variante avec pilier : Acuité2 + Cinglante2 + Harcelante3 + Recul3 = **10 PA** et **2 PA restants**, si minimum PO effectif, cible et version permettent tous les casts. Les PA des Énu/Panda/Féca/Sacrieur dans une composition duo, trio, quatuor restent propres à chacun et inconnus dans la vidéo.

## Tests proposés, non exécutés

- **Positif** : C(0,0), E(5,0), 12 PA/6PM, LdV vraie, E mobile, rang 3, (6,0)(7,0) libres. Après les quatre sorts validés, budget 0PA/6PM, E déplacé de deux cases, résolution de retrait PM conditionnelle.
- **Négatif LdV** : opaque (2,0), sans Acuité, pas de permission pour Recul par la LdV ; Harcelante reste une option. Contrôle additionnel : Acuité active E à 1 PO -> rejet si minPO accru.
- **Négatif collision** : obstacle derrière E à (6,0), ne jamais déclarer E(7,0) ; `Unknown` si déplacement sans état final. **Négatif ordre allié** : Crâ repousse E hors du tacle Pandawa -> le risque doit entrer dans l'évaluation, pas juste le DPS.

## Données, inconnues et intégration

État : acteurs distincts, initiatives/époques de combat, PA/PM/rangs, lignes de vue recalculées après chaque mouvement, PO min/max selon buffs, occupations de cases et obstacles destructibles, états de déplacement, chances de retrait PM, événements retardés de Persécutrice, relances, compétences alternatives (Sentinelle vs Acuité, Vendetta vs Repli), challenge courant. Les noms de champs sont des propositions, **pas une API de code existante**.

Arrêt sur `refused` et `unknown` : aucun cast ultérieur ne doit utiliser E(7,0) tant que la vraie position n'est pas certifiée. Avant clôture fonctionnelle, capturer un vrai exemple vidéo/plateau et vérifier les sorts contre client **3.7.13.17**, en particulier le minPO d'Acuité, collision, ordre de dégâts et recul et déclenchement différé. **Pas de code, pas de test/live réalisé.**
