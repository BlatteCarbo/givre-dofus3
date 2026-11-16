# M24 — Passage développeur Forgelance : Lance/zone/retrait conditionnel

**Livrable de recherche**, sans code, sans exécution, sans observation de combat. [Rapport](RAPPORT.md) et [Fiche](FICHE_FORGELANCE.md) donnent corpus et scénarios. Sources tiers [Dofus Grimoire Forgelance 3.7.12](https://dofus-grimoire.fr/classes/forgelance/), [Fer Rouge](https://openddb.dev/spells/23756-fer-rouge), [Pluie d'Airain](https://openddb.dev/spells/24219-pluie-d-airain), [Ydra](https://openddb.dev/spells/23723-ydra) ; cible du projet **3.7.13.17** restant à vérifier. 2026 tutoriels Timtoobias et Phiroshima authentifiés par métadonnées, **transcripts indisponibles (IP YouTube)**. Vidéo team [XAWPIYA 03:36–03:53](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=216s), 294 snippets FR auto, cite **Forgelance contrôle de zone** ; elle ne démontre aucune des cellules de la fiche.

## Trois règles prioritaires pour simulateur

1. **M24-R01 — Lance comme entité et ancre de lancer.** [Fer Rouge](https://openddb.dev/spells/23756-fer-rouge) **3 PA** et portée **0–5 PO** ne pose la Lance que si **case ciblée libre ou devenant libre**. Position réelle de l'objet, propriétaire, état **Armé/Désarmé** et source de zone doivent être suivis. **Effondrement**, **Pluie d'Airain** et **Ydra** en Désarmé ciblent la **Lance**, pas une cellule arbitraire. Aucune pose après ACK ou jet de dégâts sans confirmation de la présence de Lance.
2. **M24-R02 — Attrait et contrôle de zone sont conditionnels.** [Effondrement](https://dofus-grimoire.fr/classes/forgelance/) **3 PA**, étoile de 2, attire jusqu'à 3 cases si déplacement possible ; **recalculer toutes positions** après effet. [Pluie d'Airain](https://openddb.dev/spells/24219-pluie-d-airain) **2 PA** : zone carré de 2, **−40 Résistance Poussée 2 tours** sur ennemis affectés, **aucun retrait PM** ; un recul/dégât de collision ne survient que par un autre effet réel. [Charge Héroïque](https://openddb.dev/spells/23824-charge-heroique) **3 PA** peut repousser si le Forgelance **termine au contact** : ne pas considérer la poussée automatique.
3. **M24-R03 — Reprise de Lance et dépendance d'initiative.** [Ydra 4 PA, relance 2](https://openddb.dev/spells/23723-ydra), variante exclusive de **Muspel**, rappelle la Lance et peut rétablir **Armé/Garde** après confirmation ; si Ydra en relance, [Épilogue](https://dofus-grimoire.fr/classes/forgelance/) coûte **1 PA** si équipé (pas de remboursement gratuit en 3.7). L'initiative **P → F** rend E1 déplacé à (5,0) éligible à la zone L ; **F → P** laisse E1 (1,0), hors carré, pas de hit rétroactif.

## Séquence chiffrée proposée — non exécutée

Préétat commun [M23](../M23/FICHE_PANDAWA.md) : P(0,0), E1(1,0), E2(5,1), E3(4,2), F(2,3), case de réception R(5,0) libre, case Lance **L(4,1) libre**. P joue en premier et **confirme** E1→R avec Karcham, puis Brassage Sobre E1 ; P **8PA/3PM** après avoir engagé 4PA et 3PM (budget initial 12/6). F démarre **12PA/6PM propres**, Armé, variantes Fer Rouge/Effondrement/Pluie/Ydra et cooldown Ydra prêts.

| Action de F | Cible et condition | État PA/PM de F **après effet** | Changement exigé de l'état |
| --- | --- | --- | --- |
| **F1 Fer Rouge (3 PA)** | **L libre** (0–5 PO, règlement de cast confirmé) | **9PA/6PM** | Lance plantée en L, F Désarmé, zone carré1 sur E1/E2/E3 si touchés |
| **F2 Effondrement (3 PA)** | Lance réellement plantée L | **6PA/6PM** | Étoile2, dégâts + **attraction conditionnelle** ; positions E actualisées |
| **F3 Pluie d'Airain (2 PA)** | Lance encore L | **4PA/6PM** | Carré2 recalculé sur nouvelles positions, −40 Résistance Poussée si touché |
| **F4 Ydra (4 PA)** | Lance encore L et Ydra disponible | **0PA/6PM** | Étoile2, effet défense, Lance rappelée → F Armé si résolu |

**Total : 12 PA / 0 PM pour F.** Le coût P se calcule **séparément**. Bénéficiaires du placement : les attaques F après P ; bénéficiaire de Brassage P : l'ennemi E1 est affaibli, non F directement ; bénéficiaire réduction Ydra : F selon nombre d'ennemis touchés **sans formule chiffrée certifiée**.

## Cas proposés, non exécutés

| Cas | Entrée et séquence | Sortie observable attendue |
| --- | --- | --- |
| **M24-T+** | Préétat, P→F, F1–F4 légaux | Lance L existe après F1, F Désarmé pour F2/F3/F4, rappel après F4 ; 0PA/6PM F. Cibles réelles selon zones post-attraction. |
| **M24-T-occupied** | **L occupée**, sans libération possible après F1 | Ne pas attribuer une Lance fictive, interdire F2 ciblant Lance ; replanifier autre centre ou attaque Armé. |
| **M24-T-order** | **F→P** alors que E1=(1,0) hors carré L | E1 pas frappé par zones F avant son déplacement ultérieur ; pas de dommage rétroactif. |
| **M24-T-pull-block** | E3 état anti-déplacement / obstacle entre E3 et L | Ne pas créditer déplacement par Effondrement, F3/F4 relisent positions effectives. |
| **M24-T-Ydra-cd** | Ydra en relance, Épilogue choisi, 4PA restants après F3 | Ydra non lancée ; Épilogue rappel pour **1 PA**, F **3PA/6PM** si rappel confirmé ; sinon Désarmé persistante. |
| **M24-T-variant** | Muspel équipé à la place de Ydra | Ydra absente de l'ensemble de sorts actifs, refus d'admission sans consommation fictive. |

## Données minimales, sécurité et inconnues

- Un objet Lance identifié par **propriétaire/époque** ; les états `Armé`, `Désarmé`, `LanceCell`, `LanceState`, `sourceCell`, `AoEShape`, `lineOfSight`, `objectCellFree`, `spellVariant/cooldown`, `actorPA/PM/PV`, `initiative`, `targetsActualCells`, `pushResistance`, `movementImmunity` et `fightEpoch`. Aucun nom d'API réel promis.
- Distinguer `CastRequested`, `CastAccepted` (ACK transport), `EffectResolved`, `SpearPlaced`, `SpearRecalled`, `TargetsMoved` et `Unknown`. Un ACK ne certifie ni la case Lance ni les dommages/attractions, un timeout ne justifie pas un replay dangereux.
- **Besoins de preuve avant intégration** : table de sorts client 3.7.13.17, suivi événementiel de pose/rappel et collision, ordre dégâts/attraction, variantes, exacte mitigation Garde/Ydra, durée/combinaison des vulnérabilités, fenêtre de Ydra après CD, vidéos avec passages réellement inspectés. **Aucun `observe-video`, `valide-test`, `valide-live`.**
