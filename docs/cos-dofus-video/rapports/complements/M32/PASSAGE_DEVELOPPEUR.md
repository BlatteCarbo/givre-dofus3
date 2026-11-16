# M32 — Passage développeur : compositions avec dépendances de tour

**Date :** 2026-11-04. **Périmètre utile au simulateur :** compositions d'un à quatre joueurs, avec un tour concret `Pandawa→Enutrof→(Féca|Eniripsa)→Crâ` et comparaison Hupper/Steamer/Sadida/Zobal. Le choix d'une classe n'est qu'une **intention** ; les effets sont des transitions avec une cible, une position et une phase vérifiées.

## Trois règles prioritaires

| ID | Comportement à préserver | Preuves exactes |
| --- | --- | --- |
| **M32-R03/R04 — placement + entrave** | Le Pandawa porte/jette M1, **puis** l'Enutrof tente de retirer des PM à M1 ; le résultat enregistré est la **case actuelle** et les **PM perdus confirmés**, jamais les valeurs demandées. | [Grimoire 3.7.12 Pandawa](https://dofus-grimoire.fr/classes/pandawa/) : Karcham 1+1 PA ; [Enutrof](https://dofus-grimoire.fr/classes/enutrof/) : Obsolescence 3 PA, Maladresse 1 PA ; [HUZ 05:14–06:14](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=314s) pour l'avis Crâ–Enu. |
| **M32-R05/R06 — support ciblé et ordonné** | Féca Barricade protège **une cible** contre la mêlée si lancé avant l'attaque, Eni Stimulant augmente les PA du **bénéficiaire**, si ces PA sont actifs avant son prochain tour. Aucun effet rétroactif. | [Grimoire Féca 3.7.12](https://dofus-grimoire.fr/classes/feca/) et [Eniripsa 3.7.12](https://dofus-grimoire.fr/classes/eniripsa/), plus [fiche M15](../../M15/FICHE_ENIRIPSA.md) ; activation précise côté serveur reste inconnue. |
| **M32-R07/R08/R09/R10 — alternatives conditionnelles** | Huppermage : états élémentaires/combo ; Steamer : tourelle/case ; Sadida : infection/arbres/PM utilisés ; Zobal : masque/bouclier/zone. Vérifier **préconditions propres** au lieu d'appliquer le rôle « soutien » uniformément. | [Grimoire Hupper](https://dofus-grimoire.fr/classes/huppermage/), [Steamer](https://dofus-grimoire.fr/classes/steamer/), [Sadida](https://dofus-grimoire.fr/classes/sadida/), [Zobal](https://dofus-grimoire.fr/classes/zobal/) ; [HUZ à 21:25–28:20](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1285s), [31:25–33:35](https://www.youtube.com/watch?v=X9gMQA5rhVw&t=1885s) **avis 2025** ; [MAJ 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) prime sur ancien bonus portail du Sadida. |

## Contrat d'entrées à réunir
- **Combat** : `combatId`, `versionSource`, `versionRules`, `turnEpoch`, `initiativeOrder[]`, phase actuelle, fenêtres déjà passées, équipe et formation 1/2/3/4.
- **Entités** : `entityId`, `ownerAccount` si connu, `teamId`, `class`, `positionCellId`, `isAlive`, `isCarried`, PV/résistances, `isTargetable`, état LoS, invocations/cellules libres ; toute identité ou position absente reste `Unknown`.
- **Ressources de chaque acteur** : PA/PM/PO courants, sorts appris et variantes exclusives, relances, rang, cooldown/portée/LoS ; aucun solde de PA global.
- **Effets** : `sourceActorId`, `targetEntityId`, famille, durée, `startPhase`, `expirePhase`, retrait PM **demandé** et **obtenu**, soin/Bouclier sur le bon bénéficiaire, glyphe de début/fin/aura, masque, rune, tourelle, infection.
- **Menace** : prochains monstres à agir, déplacement légal, sorts à distance/téléportation, cartes de danger selon les PM **restants**.

Événements **conceptuels** (pas noms de paquets protocolaires) : `CastRequested`, `CastAccepted/Refused/Unknown`, `CarryConfirmed`, `ThrowCellConfirmed`, `RetraitPMResolved`, `BuffTargetApplied`, `GlyphActivated`, `ActorTurnStart/End`, `SpellDamageApplied`, `DeathConfirmed`. **Un ACK d'envoi ne confirme aucun effet** ; en `Unknown`, ne pas présumer le lancer et ne pas relancer automatiquement une action à effet.

## Instance positive M32-P : tour abstrait

**Ordre** P→E→S→C→M1, deux monstres M1/M2, 4 joueurs distincts, cibles légales, P Sobre, M1 mobile et adjacent, case candidate Z libre, C ligne de vue sur Z après jet. À 3 personnages, omettre S, puis exécuter exactement les mêmes actions légales ; à 2 Enu+Crâ, omettre le placement Panda.

1. P **Karcham** porte M1 (1 PA), puis le jette sur Z (1 PA) ; réponse serveur : M1 `cell=Z`, les cases libérées et occupées actualisées.
2. E **Obsolescence(M1)** (3 PA, `-20 Esquive PM` si appliqué), puis **Maladresse(M1)** (1 PA, `-2 PM tentés`) ; réponse : retrait **réel** `0..2` et état M1 mis à jour.
3. S **Féca** : Barricade(P) 2 PA si prochaine attaque de M1 est mêlée ; réponse : statut sur P, début/fin valide. S **Eni** alternative : Stimulant(C) 2 PA si C joue encore, ou soin ciblé si PV P bas ; réponse : identité du bénéficiaire, pas de PA sur E.
4. C : choisir un sort du deck **réel** avec portée/LoS sur Z après actualisation, PA réels ; réponse : cible/dégâts ; **aucun coût de sort Crâ inventé**.
5. M1/M2 : rejouer l'IA seulement avec états confirmés ; la mort d'une entité, un statut de téléportation ou un mouvement non anticipé invalide les prévisions restantes.

Mesures à comparer **sur le même combat initial** : (i) solo Crâ, (ii) duo Enu–Crâ, (iii) trio P–Enu–Crâ, (iv) quatuor +Féca, (v) quatuor +Eni, (vi) variante Hupper/Steamer/Sadida/Zobal. Les groupes n'ont pas forcément les mêmes monstres en donjon modulaire : **reconstruire la fixture** avant comparaison, ne pas déduire un gain causal d'un combat naturellement plus facile.

## Négatifs à rejouer
| Cas | Modification isolée | Interdit / attendu après événement |
| --- | --- | --- |
| **N32-INIT** | C joue, puis M1, **puis** P/E/S. | Interdire buff PA ou placement appliqué au tir déjà achevé ; le plan de mitigation « avant M1 » est devenu tardif. |
| **N32-LOS** | Après jet P, Z se trouve derrière un obstacle depuis C. | Abandonner l'attaque Crâ qui exige LoS ; aucun résultat de frappe inventé. |
| **N32-ESQUIVE** | Esquive M1 élevée, retrait Maladresse = 0 reçu. | M1 conserve son budget PM ; prévision de fuite/menace recalculée. |
| **N32-SADI** | S=Sadida, effet sous condition de PM utilisés ; M1 ne dépense aucun PM. | Pas de dommages par PM consommés présumés ; vérifier la condition du sort dans la version client. |
| **N32-ZOBAL** | Masque incompatible ou cible hors cercle du Plastron. | Pas de bouclier crédité au tank avant confirmation de statut. |

## Replanification et décision
À chaque effet confirmé/refusé, recalculer uniquement le suffixe des tours à venir. Une action S déjà jouée n'est **pas** une option de sauvegarde instantanée. Dans l'ordre M1→S, le soin S est disponible **après** l'impact, la protection S ne peut pas l'avoir prévenu. Sur un monstre qui esquive, comparer défense/placement légaux à retrait PM répété ; sur un Crâ sans LoS, chercher un autre tir ou attendre le repositionnement. Une « meilleure composition » est un choix d'**objectifs et contraintes**, pas de damage formula.

## Statuts et reprise
**Vérifié oralement :** la transcription MCP FR automatique de [HUZ duos 05/12/2025](https://www.youtube.com/watch?v=X9gMQA5rhVw), 1 203 segments sans page restante ; passages 04:45–06:15, 08:25–10:50, 18:10–20:28, 21:10–33:35. **Vérifié comme description seulement :** [Tenmalexis 30/08/2026](https://www.youtube.com/watch?v=U-L4-3toUGE), langue et métadonnées OK, transcript IP bloquée. **Version 3.7 :** Grimoire affiche 3.7.12 ; client cible 3.7.13.17 non exécuté ; [révisions 3.7 du 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). **Manquants :** captures plateau du round, sorts Crâ choisis et coût, logs d'initiative, réponse d'esquive, phases de protection, efficacité chiffrée des variantes, combat boss/compatibilité règle 3.7. Aucun code, Git, jeu, test, déploiement ou SYNTHESE.md.

