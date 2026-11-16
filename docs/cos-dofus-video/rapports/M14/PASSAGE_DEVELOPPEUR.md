# M14 — Passage développeur : Iop Colère chargée et coût du contact

Livraison documentaire **non exécutée**. [Rapport](RAPPORT.md), [Fiche complète](FICHE_IOP_COLERE_CHARGEE_BURST.md). Vidéo pertinente **2025** transcrite : Tenmalexis [préparation à 05:13](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=313s) et [burst décrit à 07:00](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=420s) (version 3.1). Guide **2026** [XAWPIYA duos Iop](https://www.youtube.com/watch?v=d89eRr0ciHQ) : métadonnées/description accessibles, transcript **bloqué IP**, pas de paroles citées. [Guide quatre comptes 2026](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=157s) : rôle Iop cité dans transcription FR 294 fragments. Source de mécaniques [Dofus Grimoire 3.7.12](https://dofus-grimoire.fr/classes/iop/), cible **3.7.13.17 non contrôlée**.

## Trois règles priorisées

1. **M14-R01 — Fenêtre T1/T4.** [Colère de Iop 7 PA, 1 PO, relance 3 tours et retour chargé](https://dofus-grimoire.fr/classes/iop/). Première Colère T1 **confirmée** → préparer relance et charge ; retour T4 possible si calendrier d'initiative et disponibilité réels cohérents. Ne jamais créditer la charge après refus/ACK/timeout `Unknown`. Dégâts 81–100 puis 191–210 mentionnés comme **bases de sort dans guide 3.7.12**, pas comme dégâts d'une cible du bot.
2. **M14-R02 — Ressources et vrais bénéficiaires.** Avec 12PA/6PM par **tour du Iop** dans la fixture : **T1 Colère7→Pression3→Concentration2 = 12PA**, T4 **Puissance3 sur I→Massacre2 sur E→Colère7 sur E = 12PA**. [Puissance](https://dofus-grimoire.fr/classes/iop/) augmente I, [Massacre](https://dofus-grimoire.fr/classes/iop/) augmente dommages reçus par **E** pour les frappeurs légaux. Ne pas attribuer leurs coûts au Panda, Élio ou Féca.
3. **M14-R03 — Placement conditionne la légalité et le budget.** [Bond 4 PA, 1–5 PO, case libre](https://dofus-grimoire.fr/classes/iop/) + Colère7 = **11PA** et laisse 1PA sur un tour à 12 ; impossible d'ajouter Puissance3 et Massacre2 : total **16PA**. Une marche de 5 PM de I(0,0) vers (5,0) pour toucher E(6,0) garde les 12PA si chemin libre et tacle négocié. Si destination Bond (5,0) occupée et PM 0, aucune Colère à portée ne doit être prétendue légale.

## Entrées/transitions requises

`actorId`, `turnIndex`, `fightSession/epoch`, `spellVariantAndRank`, `cooldownHistory`, `chargeHistory`, `actorPA/PM/PV`, `cellOccupancy`, `lineOfSight`, `walkability`, `targetStates/PV/Resistance`, `buffRecipient`, `allyInitiativeOrder`, `challengeConstraints`, `serverActionResult`, `serverPositionResult`. Noms **proposés**, pas contrat d'API déjà existant.

Transitions : `firstColereCastRequested` (aucune garantie) → `firstColereResolved` (charge/cooldown possibles) → T2/T3 indisponible → `cooldownReady` T4 sous état confirmé → `PuissanceAppliedTo(I)`, `MassacreAppliedTo(E)`, `ColereResolvedAgainst(E)`. Ne pas reconnaître l'ACK transport comme effet serveur ; si refus ou `Unknown`, suspendre la suite dont la précondition est une Colère chargée. Protection/alliance appliquées seulement si leur tour précède le tour du Iop.

## Cas proposés, non exécutés

| Cas | Entrées/action | Sortie attendue |
| --- | --- | --- |
| **Positif M14-T+** | T1 I(0,0), E(1,0), 12PA/6PM, Colère7+Pression3+Concentration2 ; au T4 même contact, 12PA, Puissance3+Massacre2+Colère7 | 0 PA/6PM après chaque tour ; Colère T4 chargée **seulement après** première résolution et cooldown légal |
| **Négatif M14-T-refus** | Colère T1 refusée (état/range/variante) | Interdire un « retour chargé » T4, conserver état fiable de sort |
| **Négatif M14-T-bond** | I(0,0), E(6,0), (5,0) libre, Bond 4PA et Colère7 | 11PA engagés ; interdiction du combo Puissance+Massacre+Bond+Colère =16PA |
| **Négatif M14-T-block** | E=(6,0), I=(0,0), 0PM, (5,0) occupée | Aucun contact accessible par Bond rang haut ; ne pas tenter Colère à 1 PO |
| **Négatif M14-T-order** | Panda/Élio jouent après I, E avant placement à 6PO | Position future non utilisable dans tour actuel, Colère refusée si hors contact |
| **Négatif M14-T-cooldown** | Relance Colère T3, ou Fureur équipée | Refus de cast, aucune charge ni PA inventés |

## Priorité et preuves manquantes

**P1** : machine à états charge/relance par combat, positions/initiative certifiées, budget PA propre à l'acteur ; **P2** : score de survie et valeur des buffs, fenêtre alternatives de Bond/PM ; **P3** : calcul des dégâts par résistances, critiques, vulnérabilités et challenges. Restent **unknown** : charge exacte côté client **3.7.13.17**, ordre serveur de confirmation cooldown, sort non transcrit en 2026, cellules et valeurs des combats dans vidéos. Aucune exécution ; prochaines étapes : export client, replays de tests hors jeu, puis validation réelle autorisée dans un autre cadre. Aucun code ni Git pour cette mission.
