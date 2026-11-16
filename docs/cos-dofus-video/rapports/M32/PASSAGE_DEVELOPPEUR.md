# M32 — Passage développeur : synergie conditionnelle

**Préconditions d'une action collective** : acteur précédent a résolu son action, bénéficiaire vivant, cible encore accessible et identifiée, ressources/relances suffisantes, état de boss compatible, challenge préservé. Une intention de `TeamIntentBoard` est une **prévision**, à réconcilier avec les événements du combat et le prochain snapshot.

Trois règles P0/P1 :
1. Chaque plan d'équipe porte un `planVersion`, `round`, les `actorId` et les cellules attendues ; invalider au premier refus/déplacement contradictoire.
2. Le bonus doit être consommé par le bon bénéficiaire **après** son application ; ne pas « prêter » des PA/PM à un allié.
3. L'hypothèse du retrait PM/portail/état peut rester `Unknown` ; ne pas réserver un kill ni un mouvement impossible sur cette seule hypothèse.

**Scénarios à tester :** Panda→Enu→Crâ→Féca positif conditionnel ; ordre Crâ→Panda négatif ; retrait PM esquivé négatif ; mort du boss avant sort final ; refus du placement ; challenge incompatible ; quatre acteurs ciblant une même case. Aucun xUnit exécuté dans cette mission.

**Provenance** : [vidéo Xawpiya Team 4 2026](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=265s) `declare-video`, [M08](../M08/RAPPORT.md), [M13](../M13/RAPPORT.md), [M14](../M14/RAPPORT.md), [M16](../M16/RAPPORT.md), [M07](../M07/RAPPORT.md) règles recoupées 3.7.12, client 3.7.13.17 non vérifié.

