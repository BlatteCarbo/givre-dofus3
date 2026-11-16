# M06 — Passage développeur : Korbax dans le planificateur

**Date :** 04/11/2026. **Statut :** documentation et tests proposés uniquement. **Cible :** client DOFUS 3.7.13.17, capacités Korbax **non validées** sur ce build. Aucun code/Git/jeu touché. Voir [RAPPORT.md](RAPPORT.md) et [FICHE_KORBAX.md](FICHE_KORBAX.md).

## Trois règles prioritaires

| ID | Action d'implémentation proposée | Preuves | État |
| --- | --- | --- | --- |
| **M06-R01** | Traiter Korbax comme un **fighter contrôlé distinct**, avec PA/PM/états/initiative et `spellbook` propres ; `controllerId` est un lien, pas l'identité d'acteur | [Dofus Pour Les Noobs — compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html), [Ouatdefok 21/12/2024 à 21:10–22:13](https://www.youtube.com/watch?v=ne-6NC0HF3Q&t=1270s) | **corrobore-source**, **declare-video** pour placement ; identité de paquets **unknown** |
| **M06-R02/R03/R05/R06** | Distinguer **dégâts Air**, vol de Fuite, permutation/téléportation, recul et dommages collision ; évaluer cible, distance, états et ordre d'effet au lieu d'un profil « Korbax attaque » | [Gamosaurus Korbax 2.67](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [INFI Bworker 02:52](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=172s), [05:50](https://www.youtube.com/watch?v=L8nxkgYvQTc&t=350s), [INFI Skeunk 05:10](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=310s) | **corrobore-source**, **declare-video**, chiffres 3.7 **unknown** |
| **M06-R04/R07/R09** | Modéliser marque Nimbostratus, bonus d'Envolée/passif et action de repli, avec destinataire et moment explicites | [Gamosaurus Korbax](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman), [INFI Skeunk 02:35](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=155s) | **corrobore-source** pour capacité historique ; déclencheur/durée exactes **unknown** |

## Contrat de données / transitions

`Fighter` a un identifiant stable par combat/session/époque et `teamId, controlledBy, companionOwner, type, spellRank, PA, PM, position, turnIndex, alive, buffs, states`. La donnée client versionnée définit `spellId` réel, coût, PO min/max, lancer en ligne/diagonale, LoS, exigences de cible, cast/cible/tour, cooldown, zone, effets, ordre de résolution. **Aucun `spellId` numérique inventé**.

Une **action candidate** a : acteur=Korbax, cible ID ou cellule, vérification de géométrie/états, effet attendu par bénéficiaire (Air, Fuite, déplacement, bonus, retrait PM), coût et risque. Puis `Requested → ServerAccepted/Refused/Unknown → Position/EffectsObserved → Resolved`. L'ACK n'est pas un effet serveur. Pour Nimbostratus, `MarkPlaced → Armed → Triggered → Removed` seulement après preuves de chaque étape. Pour le passif, l'événement confirmé de fin de tour du **lanceur Korbax** est distinct du prochain tour de chaque allié.

En duel, trio et quatuor, la synergie est déterminée par l'ordre `turnIndex`, les ressources **de chaque acteur** et la liste des alliés effectivement dans la zone au moment de l'effet. **Duos/trios/quatuors** incluent toutes classes et compagnons, mais les effets de leurs autres sorts ne sont pas inférés par cette fiche.

## Tests offline à créer, tous non exécutés

| Cas | Fixture | Oracle attendu | Danger détecté |
| --- | --- | --- | --- |
| **P01** | K et M en portée légale, cible ennemie, PA suffisants | Bec Aiguisé déclenché par K, dégâts Air M, Fuite transférée selon rang vrai ; aucun PA pris à A | Confusion propriétaire/acteur et simple `damageProfile` |
| **N01** | même M mais hors portée/état interdit ou cast indisponible | K ne peut pas lancer ; refus ne change pas M ni K sans événement | Cast fantôme / dégâts fictifs |
| **P02** | Envolée ou passif, A + Multiman B en zone avec ordre K→A→B | enregistrer exactement les buffs de A/B, leur durée et le tour où ils aident effectivement | Bonus attribué au mauvais allié |
| **N02** | A a déjà joué, K finit par booster A | Aucun « retour en arrière » pour améliorer l'attaque précédente de A | Synergie temporelle impossible |
| **P03/N03** | Nimbostratus + entrée en zone puis même entrée sans piège confirmé | effet uniquement en présence de marque armée, zone/retours serveur ; état Unknown en absence de confirmation | Déclenchement à la pose |
| **D01** | Korbax, ennemi M, obstacle devant M, Explumession vs terrain libre | comparer déplacement effectif et dégâts de collision réellement confirmés | Poussée / dommages extrapolés |
| **D02** | défi Duel : A a frappé M, Korbax souhaite Bec Aiguisé M | bloquer le plan conservateur jusqu'à oracle M07 d'attribution | Échec challenge attribué au contrôleur au lieu du fighter |

## Diagnostic d'un tour « sans attaque »

Collecter successivement l'identité K qui a réellement joué, son `spellbook`/PA/PM disponibles, les positions/cibles, portées et LoS, cooldown, état de l'ennemi, état challenge, bonus potentiels et événements de refus. Décision utile possible : Envolée pour le personnage qui joue **après** K, Explumession pour empêcher un ennemi d'atteindre l'équipe, ou déplacement préparatoire. En revanche, un tour vide **avec attaque sûre et légale prouvée** devient un **candidat d'anomalie**, seulement après lecture du journal de décisions.

L'exemple « rien à faire, inutile de taper » de [Skeunk 01:30–01:47](https://www.youtube.com/watch?v=y1qofhFD9NQ&t=90s) est décrit **par le Xélor** face à une poupée soignante ; c'est un exemple de *raison tactique*, **pas un tour Korbax audité**.

## Sources, blockers, suite

Vidéos transcrites : [Bworker, INFI, 16/02/2025](https://www.youtube.com/watch?v=L8nxkgYvQTc), [Skeunk, INFI, 04/01/2025](https://www.youtube.com/watch?v=y1qofhFD9NQ), [Short Capitemflam, 19/10/2026](https://www.youtube.com/shorts/g29wAwZ9USg). Trois transcriptions FR auto MCP intégrales, aucune frame inspectée. Source de sorts : [Gamosaurus refonte 2.67](https://www.gamosaurus.com/jeux/dofus/dofus-guide-de-korbax-compagnon-et-multiman) ; [Dofus Pour Les Noobs compagnons](https://www.dofuspourlesnoobs.com/les-compagnons.html).

**Blockers 3.7 :** identification de tous les effets exacts / rangs, PA/PM, zones et cooldown ; état Pesanteur et restrictions réelles ; ordre de téléportation/dégâts/poussée ; propriétaire du piège ; résistances de poussée ; statut spécifique des défis lorsque Korbax attaque ; IDs de paquets. Priorité suivante : fixture 3.7.13.17 anonymisée d'un tour où Korbax utilise **Bec Aiguisé et Explumession** puis, dans une autre scène, **Envolée/passif**. Les statuts `valide-test` et `valide-live` restent **absents**.

