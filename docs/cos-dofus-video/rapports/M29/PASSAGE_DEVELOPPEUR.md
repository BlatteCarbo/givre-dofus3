# M29 — Passage développeur : moteur de prédicats de challenges

Sources : [Les challenges](https://www.dofuspourlesnoobs.com/les-challenges.html) et [M07](../M07/RAPPORT.md). Les descriptions datent en partie de 2.68 ; la build cible 3.7.13.17 nécessite contrôle. Les statuts sont `corrobore-source` historique et `hypothese` pour comportements non reproduits.

**P1, historiser l'action réellement résolue :** `(eventId, actorId, targetId, actionKind, ownerId?, sourceType, round)`. Le kill et le soin nécessitent leur auteur effectif.

**P2, revalider la fin de tour :** `EndTurn(actorId, cell, knownPartyCells)`, évaluation à cet instant de l'alignement d'au moins un allié ou de tous selon le challenge.

**P3, préparer l'incertitude :** ne pas attribuer aveuglément au joueur les dégâts de son compagnon ou de son invocation pour Duel ; si la classification ingame reste inconnue, état du challenge = `Unknown` et pas de réussite automatique.

**Fixtures à exécuter après preuve** : Duel A/X puis B/Y (positif), A/X puis B/X (négatif), compagnon/X (classification à vérifier), Lignée avec un/aucun allié aligné, Miroir position de l'allié au kill, Infatigable 5/12 PA au kill, Sans-cœur soin allié. Ajouter simultanéité, refus et état obs. `TeamIntentBoard` consulté n'enregistre pas l'historique complet de ces prédicats ; l'intégration exige inspecter les callers et le réducteur de challenge.

Aucun résultat xUnit ou live dans ce dossier.

