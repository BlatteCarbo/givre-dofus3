# M02 — Passage au développeur GivreClone (spécification documentaire)

Date 2026-11-04. Sous-mécanique : Roublard, mur Feu formé par deux Explobombes, charge T1→T2 et explosion ; branche alternative Poudre/destruction. Les données ci-dessous sont des *propositions de transitions* à confronter aux messages serveur de la version cible 3.7.13.17. Aucun code, test ou combat effectué.

## Trois règles prioritaires
| ID | Règle utile au moteur | Niveau de preuve | Provenance exacte |
| --- | --- | --- | --- |
| M02-R01/R02 | Les bombes ont une identité/position/propriétaire, gagnent +2 Combo au **début du tour de leur propriétaire** selon la base 3.7.12 ; au moins deux vivantes de même type alignées constituent un mur. | corrobore-source pour 3.7.12, cible non validée | [Grimoire, passif / À savoir](https://dofus-grimoire.fr/classes/roublard/) ; [V25, 14:39–15:42, préparation 2 tours](https://www.youtube.com/watch?v=8ebZ9D3prsA&t=879s) |
| M02-R03/R04/R05 | Un déplacement peut former le mur ; la vidéo affirme « dégâts du mur puis +1 Combo par Aimantation » ; Détonateur donne +1 à la bombe **ciblée**, qui explose et déclenche d'autres bombes selon portée/type, sans bonus direct sur les secondaires. | ordre Aimantation = declare-video/hypothese ; propagation = corrobore-source 3.7.12 | [V26 06:40–07:05](https://www.youtube.com/watch?v=zzdrk6gUG58&t=400s) ; [Grimoire, réaction en chaîne](https://dofus-grimoire.fr/classes/roublard/) |
| M02-R06 | Distinguer « destruction par dégâts » et « explosion par déclencheur ». Sous Poudre, la mort par dégâts peut charger +2 et provoquer l'explosion ; Détonateur ne déclenche pas ce +2 ; un transfert de Poudre peut également charger l'ancienne bombe. | declare-video + corrobore-source 3.7 (fan data) | [V26 02:01–02:24](https://www.youtube.com/watch?v=zzdrk6gUG58&t=121s) ; [Grimoire, Poudre](https://dofus-grimoire.fr/classes/roublard/) ; [Back from AFK, diff 3.6→3.7](https://backfromafk.com/classes/roublard/depuis-3-6/) |

## État minimal demandé
**Combat** : ownerActorId et turnEpoch (éviter +2 à chaque tour d'un autre acteur), actorId courant, phaseStartOfTurn, cycle d'actions, version protocole/règles, positions libres, obstacles, acteurs/camps.

**Bombe** : entityId serveur, ownerActorId exact, element/type, cellId, alive, PV, comboValue *si autoritativement connu*, status Poudre, protection, lastAppliedByActionId, lastMotionId, eventCause (normal-damage-death / triggered-explosion / withdrawal / other), queuedExplosionId et règle de propagation.

**Mur dérivé** : clés (owner,type, bombIdA,bombIdB), cellules segmentées à recalculer après pose/mouvement/disparition, phase de déclenchement et entités affectées. Ne pas stocker un mur figé indépendamment de ses bombes. Interdiction de partager un mur entre joueurs différents sur simple voisinage de bombes.

**Source d'autorité** : spell cast demandé ≠ accepté ≠ effet observé. Ne valider pose, +Combo, perte de PV, disparition, explosion, remboursement PA ou mur que lorsque les événements/états serveur correspondants sont disponibles. Si écho absent, différé, contradictoire ou refusé : état Unknown/Refused ; annuler ou réduire les plans dépendants, sans replay automatique d'une action à effet.

## Ordre proposé à explorer, sans prétendre l'avoir validé
1. T1/R : poser B1 Feu en (0,0), B2 Feu en (0,2), E ennemi en (0,1). Vérifier coût d'Explobombe B1=2 PA, B2=3 PA dans la source 3.7.12 ; le moteur reçoit les deux IDs de bombes puis dérive le mur.
2. Tour adverse : confirmer que B1 et B2 survivent ; si elles meurent ou bougent, recalcul immédiat ; le Combo ne doit pas être incrémenté lors d'un début de tour ennemi.
3. T2/début du tour de R : source 3.7.12 = +2 Combo à chacune des deux bombes survivantes.
4. R/Plombage : sort de 3 PA dans la base, augmenter +1 Combo sur les deux bombes reliées si lancement confirmé ; si les cibles ne sont plus reliées, aucun bonus présumé.
5. R/Détonateur B1 : 1 PA remboursé selon base, +1 Combo sur B1, puis son explosion ; B2 répond si règle du rayon ou de l'alignement vérifiée, sans +1 propre à Détonateur. Après chaque explosion, retirer les bombes et recalculer le mur. Si plusieurs effets simultanés, conserver un ordre **inconnu** jusqu'à replay documenté.
6. Branche alternative sans Plombage : si B1 sous Poudre est détruite par une attaque, vérifier l'événement destruction et l'explosion avec +2 Combo ; si B1 est volontairement détonée, vérifier que le bonus Poudre n'a pas été ajouté. Ne pas déduire la cause du seul message « entité disparue ».

### Exemple de contrat d'entrée (abstrait, pas de code intégré)
- Input : state(version, actor R, activeTurnOwner, 12 PA, B1/B2 IDs, positions, types, Combo/états connus), action(requestedSpell, targetId/cellId, actionId).
- Output : transitions typées « BombSpawnConfirmed », « OwnerTurnComboApplied », « BombMovedConfirmed », « WallTopologyChanged », « WallEffectResolved », « ComboApplied », « BombDestroyedByDamage », « BombDetonationTriggered », « ExplosionResolved », « APRefundConfirmed » et « Unknown/Refused » si preuve serveur indisponible.
- Branches : garde de version et de propriétaire ; précondition de cellule libre, portée, ligne de vue, sort/variante, PA restants, cooldown, cooldown d'invocation, bombes encore présentes. Ne pas inventer coût exact des autres sorts, plafond d'invocations ou IDs protocolaires.
- Formules de dégâts, multiplicateur Combo, ordre atomique des impacts et propagation transitive restent inconnus pour l'implémentation cible. Les montants vus dans une vidéo illustrent un équipement, jamais une formule générale.

## Tests/replays à préparer (tous non exécutés)
| Cas | Préparation reproductible | Vérification et échec discriminant |
| --- | --- | --- |
| P1, positif mur + âge | R/E/R, deux Explobombes Feu alignées entourant E et survivantes, Plombage puis Détonateur B1. Positions relatives (0,0) / (0,1) / (0,2), validation de cases client à faire. | Mur à l'arrivée de B2, +2 seulement en T2 R, +1 Plombage B1+B2, +1 Détonateur B1 seulement, B2 explose avec son Combo ; état post explosion sans mur. Aucun dommage chiffré attendu. |
| N1, négatif mort de bombe | Même départ, mais E tue B2 entre T1 et T2. | Aucun mur à deux bombes ni charge T2 sur B2 ; rejet d'un plan conditionné par B2 ; aucune explosion B2 au T2. |
| P2/N2, négatif de cause | Deux exécutions isolées, Poudre active sur B1 : P2 = attaque ennemie tue B1 ; N2 = Détonateur B1. | Différence dans la source « mort par dégâts » vs « activation volontaire » et +2 conditionnel. B1 ne doit pas recevoir deux fois +2. |
| N3, ordre déplacement | Bombes non alignées ; Aimantation les aligne en traversant zone de E. | Traces distinctes avant/après +1 Combo et éventuel dégât du mur ; confirmer ou infirmer l'ordre oral V26, priorité critique. |
| N4, chaîne mixte | B1 Feu, B2 Eau à 2 cases, B3 Terre éloignée hors cercle et pas de même type aligné. | B2 se déclenche via cercle, B3 non ; vérifier géométrie, obstacle et propagation avant création d'oracle ferme. |

## Critères de fermeture et questions ouvertes
1. Obtenir captures de plateau V26 à [06:40–07:05](https://www.youtube.com/watch?v=zzdrk6gUG58&t=400s) puis log serveur des conséquences exactes d'Aimantation (choc du mur vs bonus de Combo).
2. Obtenir description officielle Ankama des sorts pour la 3.7.13.17 (site [officiel](https://www.dofus.com/fr/mmorpg/actualites/maj) inaccessible au collecteur par contrôle anti-bot) ; la vidéo [officielle 3.7, 17 septembre 2026](https://www.youtube.com/watch?v=bndnxhqhczY) prouve la communication de version seulement.
3. Vérifier le conflit « Combo 0 initial » (base 3.7.12) / « état 1 » (V26), les sorts et variantes, la ligne de vue, les cases réellement couvertes, l'ordre d'explosion des secondaires, les obstacles, les sorts d'ennemi et les PA rendus.
4. Test hors jeu sur un fixture expurgé puis replay de combat serveur expurgé avant tout statut valide-test/valide-live. Les tests proposés n'ont pas été exécutés ; seul le contenu documentaire est livré.

La fiche source complète est [FICHE_ROUBLARD.md](FICHE_ROUBLARD.md). Aucun fichier SYNTHESE.md modifié : elle appartient au coordinateur.

