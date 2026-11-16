# N06 — Matrice de preuves : portails et Téléfrags

**Chemins relatifs à `SRC/` = `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024/`.** Chaque citation ci-dessous indique des **lignes du fichier**, sauf le cas où un offset natif est explicitement indiqué. Aucune extraction de symboles client n'a été exécutée pour N06 ; les métadonnées anciennes ont été **lues**, les empreintes client recalculées.

## Sources vérifiables du build courant

| Type | Fichier:ligne ou offset | Observation bornée | Niveau de preuve |
| --- | --- | --- | --- |
| Client installé | `native/GivreTurboRuntime/TacticalAutostart/METADATA-3.7.13.17.md:8-17`; `docs/validation/official-client-371317-20261103.md:1092-1093` | metadata39, marker **3.7.13.17**, hash metadata `ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`, assembly `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591` | Hash **recalculé** `Get-FileHash SHA256` en lecture seule, pas de native opcode ou sort |
| Décodage exchange catalogue | `data/fields.json:2431-2435`, `:3348-3386` | `ExchangePositions.target_id=1`, `caster_cell_id=2`, `target_cell_id=3` ; `GameActionFightEvent.exchange_positions=4`, `source_id=36` | Contrat mapping actuellement livré ; pas une trame serveur Xélor |
| Décodage exchange | `GivreClone.Protocol/MapFightReader.cs:127-141` | `ReadExchangePositions` reconnaît le sous-message et renvoie 2 acteurs/2 cellules, via `F(name,field)`, entrée si coordonnées <560 | Prouvé **dans le code**, sérialisation exacte et statut de position ancienne/nouvelle **à recouper** avec observation |
| Câblage de l'échange | `GivreClone.App/Services/LaunchService.Packets.cs:1106-1125` | Si cellules du payload identiques aux cellules courantes, **permute** ; sinon les applique comme **nouvelles** ; remet à jour la visibilité | Heuristique du code, **pas** garantie par un type de message ni preuve d'un état Téléfrag |
| Fixture exchange | `GivreClone.Tests/CombatForcedMovesReplayTests.cs:278-293` | Encodage synthétique d'échange acteurs avec cases 396/381, rejet d'un slide pris pour un exchange ; test utilise le catalogue `F` | Valeurs de fixture, **aucune séquence live** et aucun passif |
| Commentaire obsolète | `GivreClone.Tests/CombatForcedMovesReplayTests.cs:281` | Dit `exchange_positions #39`, `source_id #11`, `caster_cell_id #1` alors que la **table présente** donne 4/36/2 | **Incohérence documentaire prouvée** ; utiliser les numéros du catalogue versionné, jamais le commentaire |
| Tactique générique | `GivreClone.App/Services/Combat/SpellResolver.cs:161-176` | Sur `swap=true`, déplace la cible et occupant si présent ; `Move` émet `MovementResolved` | Géométrie locale, pas un algorithme de Téléfrag |
| Types de sort et classe | `GivreClone.App/Services/Combat/SpellDescriptor.cs:15-25,48-75,78-97` | Types `Teleport`, `Swap` et action IDs (8/1023 pour Swap), marqueur `Conditional`, gardes de légalité ; commentaire `:61` « Action IDs are data identifiers, not spell IDs » | **Source-only** ; aucun ID de sort Iop/Xélor attribué par N06 |
| Portails génériques | `data/fields.json:6271-6276` | `PortalInformation.portal_id=1,area_id=3`, `PortalUseRequest.portal_id=1` | Champs de protocole existants, **aucun attribut Éliotrope, combat, état ouvert/fermé, cellule** |
| Alias portail hors contexte | `GivreClone.Protocol/LuaMessageAliases.cs:66,783,1155,1201` | `PortalDialogCreationMessage` et `PortalUseRequestMessage` comme alias ; leurs anciens nombres d'alias sont **non retenus pour combat** | La chaîne « Portal » ne prouve pas un portail de combat |
| Board planner | `GivreClone.App/Services/Combat/SpellResolver.cs:18-21,35-42` | `SpellBoard( ... ThroughPortal=false)` sans collection d'identités de portail ni temps/état ; effet différé conservé | **Limite du modèle lu** ; aucune formule ni route possible depuis un bool seul |
| Tactique à risque | `GivreClone.App/Services/Combat/CombatTurnPlanner.cs:30-44` | Contrat `ForcedMovementSafe` suppose **absence** de bloqueurs, portails, marques et triggers prouvée par l'appelant ; inconnu ne vaut pas absent | Garde de planification, pas preuve du monde |
| États et passifs | `docs/validation/combat-state-combo-20261103.md:54-61,112-128` ; `SpellResolver.cs:10-16` | États live non réduits ; modèle retient `StatesKnown`, pas de scheduler Téléfrag/historique/remboursement | **Unknown** live ; tests génériques ne comblent pas |
| Ancien rapport Xélor | `docs/cos-dofus-video/rapports/M03/FICHE_XELOR.md:15-24,44-56,67-76` | Recommandation Permutation/Gelure et +2 PA par générateur et par tour, **source communautaire et exercice** | Aucune preuve de la transition Téléfrag côté client 3.7.13.17 dans les sources lues |
| Ancien rapport Éliotrope | `docs/cos-dofus-video/rapports/M04/RAPPORT.md:23-50,52-59`, `M04/FICHE_ELIOTROPE.md:15-32` | Entrée choisie, proximité, départage horaire et Neutral/Sillage documentés depuis vidéos/guides 2025 | Distance, angle, LoS, statuts et calendrier non vérifiés par sortie client cible |

## Données client et moteur : ce que signifie « règle prouvée »

**Règle prouvée sur structure de code** : un `GameActionFightEvent.exchange_positions` décodé peut mettre à jour **deux positions** dans `LaunchService`, sous la convention actuelle du réducteur. C'est la **seule transition relevant directement de l'espace Éliotrope/Xélor audité** ici. Le sous-message est de **combat général** ; il peut provenir d'autres classes. En l'absence de `source spell`, **son occurrence n'établit pas Téléfrag**.

**Règle non prouvée** : la présence du mot `Portal` dans `PortalInformation` ou `PortalUseRequest` **n'établit pas** un portail Éliotrope. Ce couple a `portal_id` et parfois `area_id`, mais ni centre combat, ni auteur, ni état open/closed, ni horloge ; il serait abusif d'exécuter les routes M04 à partir de ce seul type.

**Règle non prouvée** : la mécanique de Téléfrag suppose une **application d'état de combat** (sur un ou plusieurs acteurs) et une **modification de PA** sous condition. Le résolveur générique enregistre seulement le déplacement et peut produire des événements `MovementResolved` ; il ne récupère pas un état passif du serveur. `SpellResolver.cs:29-35` définit un `StateResolved` de simulation, qui **ne vaut pas** état observé du client. `docs/validation/combat-state-combo-20261103.md:54-61` signale explicitement les payloads de buffs non résolus.

## Quatre oracles qui trancheraient les hypothèses

| Oracle | Deux interprétations opposées | Éléments bruts à comparer (aucun enregistrement inventé) |
| --- | --- | --- |
| **E1 distances/égalités** | « tie-break horaire » vs « ordre d'âge » | 3 portails réellement identifiés et ouverts, entrée choisie, sortie observée, même géométrie et ordre de pose inversé ; puis 1 portail décalé d'une case |
| **E2 fermeture vs LoS** | « fermé donc exclu du réseau » vs « ouvert mais sort sans ligne de vue » | État de portail **avant** et **après** Neutral/Sillage, raison de refus si exposée et ligne de vue de la sortie ; reprendre sortie prévue |
| **X1 case libre/occupée** | « toute téléportation donne Téléfrag » vs « permutation nécessaire » | Même mouvement et caster/cible, destination libre/occupée, old/new cells, état Téléfrag avant/après, vrai delta PA et provenance sort |
| **X2 gain limité** | « gain de PA sur chaque Téléfrag » vs « une fois par sort/tour » | Deux échanges même sort/tour et deux sorts différents, état et vrai delta PA, ancienne charge consommée puis recréée, sans confondre tour courant et différé |

Toutes ces configurations sont **scénarios proposés, non exécutés**. L'absence d'effet visible dans les rapports historiques ne doit pas être extrapolée en « l'effet n'existe pas » ; il manque précisément le schéma de données, l'identité de sort et la trace de la transition.

## Inventaire négatif borné

Recherche par `rg` dans `GivreClone.Protocol/`, `GivreClone.App/Services/`, `GivreClone.Tests/`, `data/`, `docs/validation/` et `offline-evidence/` : pas de gestion de **réseau de portails de combat Éliotrope** ni de transition **Téléfrag** dans les sources ciblées ; uniquement catégories de mouvement génériques et documentations historiques. Le balayage global des fichiers du dépôt a rencontré deux dossiers tiers refusés ; **pas d'affirmation d'absence du dépôt entier ou du client binaire**. Le dossier `docs/validation/official-client-371317-20261103.md` consigne des validations live d'autres mécaniques ; N06 n'a lancé aucun combat.
