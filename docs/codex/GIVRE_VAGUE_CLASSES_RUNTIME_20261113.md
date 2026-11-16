# GivreClone — vague classes, fiabilité et hors-combat (2026-11-13)

## Périmètre et traçabilité

- Worktree : `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024` ; HEAD initial `b4d28a8`.
- Client référencé par les fixtures : **DOFUS 3.7.13.17**, build daté 2026-11-03. Source : `docs/validation/roublard-bomb-contract-20261105.md`.
- Le dépôt contenait déjà des centaines d'entrées modifiées avant cette vague. Aucun `git reset`, `git clean`, commit global ou push n'a été effectué.
- **Validation hors jeu uniquement** : aucun client lancé, aucune commande ou ACK de jeu observé dans cette vague. Une assertion hors jeu n'est pas une preuve de performance ou de réussite live.

## Modifications concrètes

### Combat — correctifs d'autorité et de planification

1. **Portail ≠ rune Huppermage** : `LaunchService.Combat.cs` ne classe comme portails que les marques actives d'un auteur Éliotrope vérifié, sur une cellule, type 3/4. Vérification `EliotropePortalDispatchTests`.
2. **Cellule personnelle confirmée** : `BotWorldState.SelfReadyFor` exige `SelfCellConfirmed`; `GivreLuaHost.WaitSelfReady` refuse une géométrie ou cellule non confirmée.
3. **Rotation sûre** : `AutoSpellRotation.ShouldUsePlanner` vérifie toute la rotation et n'oublie pas un sort offensif inconnu placé après un sort modélisé ; un sort offensif connu immédiatement n'impose plus artificiellement le fallback. Un descripteur `null` reste fail-closed. Tests historiques corrigés selon cette règle, sans masquer un refus de sécurité.
4. **Portage de méthodes de classes** : `ClassMechanics.cs` contient les règles de bombes/murs, le routage par portails avec propriétaire contrôlé et les refus d'heuristiques Sacrieur arbitraires. L'ancien bonus de mur sans preuve active a été neutralisé plutôt que simulé sans base.
5. **Nouvelle reconnaissance séquentielle** : `ClassMechanics.VerifyObservedCombo`, `ClassMechanicsSequentialProofTests.cs` et `ClassMechanicsFalseCertificationTests.cs` définissent des preuves ordonnées et corrélées pour Roublard, Éliotrope, Xélor, Féca, Écaflip, Sacrieur. Conditions : identifiant de combat, génération, tour, événement serveur confirmé, classe, données de grade appris et source/version du client. Retrait de marque, de bombe ou d'état invalide la conclusion. Pour Roublard, un simple effet `13455/action99` ne suffit pas : une phase `DetonationConfirmed` distincte et causalement corrélée est requise. Pour Xélor, deux mouvements attribués et l'état Téléfrag doivent être dans l'ordre autorisé. Le vérificateur reste **historique et hors jeu**, pas un émetteur de sorts ; aucun adaptateur serveur ne produit encore automatiquement tous les témoins exigés.

### Carte — clic droit Attaquer

- Nouveau `MonsterAttackTargetLease.cs`, connecté aux **vraies actions** `BotRowViewModel.RequestAttack` et `GivreLuaHost.Combat.FightOne`, toutes deux passant par `LaunchService.AttackMonster` puis le dispatch après `MapMovementConfirmResponse`. Le callback de preuve est désormais obligatoire ; une commande de combat sans lease est refusée.
- Capture de la référence d'acteur serveur et de l'époque carte ; obligation de retrouver exactement le même groupe de monstres, non déplacé, sans ID contextuel doublé.
- Revalidation de la carte au départ et à l'arrivée ; absence d'attaque si le groupe disparaît/remplace, si une autre carte remplace le snapshot, si la requête est annulée ou si le déplacement est trop ancien (>45 s).
- **Course ACK corrigée** : `MoveTo.beforeInject(seq)` arme l'action avant l'injection. Le dispatch exige `moveSeq == serveurEchoSeq == seq courant`, la bonne carte, et une cellule personnelle **confirmée par le serveur**, exactement à destination et adjacente à la cible. Un ACK étranger ne consomme plus l'action en attente ; l'absence d'écho corrélé provoque un refus prudent. Le retour d'injection en échec retire sa propre action sans effacer un successeur.
- `BotWorldStore.MapGeneration` révoque les leases lors des changements de carte, pertes de connexion et snapshots complets d'acteurs. `ActorPublicationSequence` (pair = snapshot stable, impair = écriture en cours) évite une lecture composite lors de la publication concurrente d'acteurs/mouvements.
- **Limite** : le store n'effectue pas de transaction atomique commune avec l'injection réseau ; une mutation serveur intervenant entre la dernière vérification et les octets envoyés n'est pas totalement exclue. Un ACK de déplacement ne prouve PAS que le combat a démarré, et le protocole `MapActor` n'expose pas d'instance de groupe autoritative distincte de son ID contextuel. Aucun résultat live revendiqué.

### Fiabilité, économie et ergonomie

- `BehaviorHost` sérialise les remplacements afin de ne pas exécuter deux comportements simultanément. `SessionBinder` protège le mapping PID/connexion avec invalidation des callbacks tardifs ; fermeture TCP réelle `Forget` à raccorder.
- `EcoStores` : chargement et sauvegardes atomiques, backup sain préservé, quarantaine des octets JSON corrompus et accès sérialisés dans le processus. La fixture ambiguë confondant `.bad-*` avec sa sidecar `.bad-*.bak` est rectifiée avec comparaison des octets.
- `HdvTaxBudget` et `HdvViewModel.Bulk.cs` : réservation prévisionnelle des taxes avant ordres de vente, tenant compte d'une confirmation de kamas différée. L'estimation n'est pas une garantie de plafond fiscal réel tant que les taux/arrondis serveur ne sont pas démontrés.
- `BotsViewModel.Expedition.cs` : restauration exacte des préférences de poursuite après défaite et limite de défaites.
- `BotRowViewModel.Fight.cs` : suppression des répétitions de propositions de défis, reducer d'identité de combat et garde contre clics/callbacks tardifs.
- `WorldAtlasViewModel.cs` : fraîcheur des snapshots, génération/sessions et sélections périmées ; l'atlas reste consultatif, la vraie attaque carte est désormais protégée séparément comme décrit plus haut.
- `MZP12SocketVersionBinding` : correspondance **exacte** de build avant injection Socket non restreinte ; décodage/diagnostic et mode de mouvements explicitement limités sont distincts.
- `tools/CombatShadowComparison` : comparateur offline décisions/observations, sans connexion encore au moteur de replay.

## État des tests connus

| Lot | Résultat | Portée et réserve |
| --- | --- | --- |
| EliotropePortalDispatchTests | 32/32 PASS | Fixtures, pas client live (validation antérieure à la reprise) |
| Filtre 90 régressions nouvelles | **90/90 PASS** | Compilation réelle .NET10 ; `docs/codex/validation-20261113-regressions-90.log` |
| Lot étendu historique et nouveaux gardes | **253/253 PASS** | Session après correction d'une attente historique sur descripteur `null` ; `docs/codex/validation-20261113-extended-diagnostic2.log` |
| WorldAtlasSnapshotFreshness + MonsterAttackTargetLease | **24/24 PASS** | Ciblage pur et identité snapshot, pas ordre live |
| ClassMechanicsSequentialProofTests | **24/24 PASS** | Exigences des combos séquentiels, événements synthétiques |
| Durcissement ciblé : lease, Engine, combos et anti-fausse certification | **93/93 PASS** | `docs/codex/validation-20261113-later-hardening.log`, après raccordement ACK et nouveaux tests |
| Régressions complètes hors tests nommés `Live` | **5 756 PASS / 2 ignorés / 0 échec**, 5 758 cas | `docs/codex/validation-20261113-full-tests-final.log` ; exit 0, .NET 10 |
| Inventaire Lua + nettoyage d'arrivée + refus de mouvement | **13/13 PASS** | Révision des anciennes fixtures du tuple d'attaque différée et de deux hashes existants ; `docs/codex/validation-20261113-full-failures-fix.log` |
| CombatShadowComparison harness | **65 assertions PASS** | Relancé hors jeu, `docs/codex/validation-20261113-shadow-final.log` |
| PureTests | **362/362 PASS** | Nouvelle relance, `docs/codex/validation-20261113-puretests-final.log` |

Les runs intermédiaires montrent également une collision `CS2012` lorsque deux compilations simultanées ont tenté d'écrire le même DLL ; cette défaillance d'infrastructure a été éliminée en sérialisant les builds. Le premier lot étendu a trouvé une assertion historique devenue incorrecte, corrigée ensuite. La première exécution de 5 758 cas comportait six échecs : cinq fixtures réflexives utilisaient l'ancien tuple `_pendingAttack`, et l'inventaire Lua contenait deux empreintes périmées. La validation finale est **0 échec** sur le filtre hors `Live`. Aucune validation live n'en découle.

## Matrice classes DOFUS 3.7.13.17

**Légende** : `contrat` = preuve diagnostique ordonnée testée hors jeu ; `partiel` = règles/sorts connus mais aucune IA exhaustive ; `à établir` = source/effet/passif/replay encore nécessaires.

| Classe | Couverture vérifiée aujourd'hui | Manque prioritaire pour IA réelle |
| --- | --- | --- |
| Roublard | Contrat : bombes *3112*, mur *2*, effet d'explosion *13455/action99*, propriété et chaîne observée | Cycle/charges, pousse dans mur, boost/détonation avec AP/dégâts et ACK réels |
| Éliotrope | Contrat : poses/activation/propriétaire des portails, projection, effet, mouvement cible distinct | Téléportation/redirection multi-paires, ties serveur, trajectoires et sort/impact |
| Xélor | Contrat : échange, deux mouvements et état Téléfrag corrélé | Historique multi-tours, autres sorts, ressources et chaînes à PA exacts |
| Féca | Contrat fail-closed glyphe pose/activation/effet | Catalogue de glyphes/états/version, TP, boucliers et résolution temporelle |
| Écaflip | Contrat fail-closed carte/état/consommation | IDs/grade/passif et probabilités observés ; aucun tirage ou état inventé |
| Sacrieur | Contrat fail-closed PV/Souffrance/placement/dégâts | États et paliers confirmés, PV/protection/charges et scénarios multi-tours |
| Iop | Partiel, moteur tactique générique | Boost/précipitation/placements et séquences avec cooldown |
| Cra | Partiel, moteur distance générique | Tir/PO, poussée, kiting, lignes de vue par sort |
| Eniripsa | Partiel, moteur soutien générique | Prédiction soin/PA/états alliés avec timing et cibles confirmées |
| Enutrof | Partiel, moteur générique | Retrait PM, invocations, butin et états |
| Osamodas | Partiel, invocations génériques | IA/ordres/invocateur/acteurs et capacités par invocation |
| Sadida | Partiel, moteur générique | Arbres, poupées, infection, déclencheurs et relais |
| Sram | Partiel, pièges et marqueurs génériques | Réseau de pièges, invisibilité réelle et chemins ennemis |
| Pandawa | Partiel, déplacements forcés génériques | Porter/jeter, états ivre/sobre, placements multi-étapes |
| Steamer | Partiel, invocations et zones génériques | Tourelles, évolution, cibles alliées et zones |
| Huppermage | Protection rune ≠ portail Éliotrope | Combinaisons élémentaires, états/runes et déclencheurs |
| Ouginak | Partiel, moteur générique | Proie, transformations et rage/charges observées |
| Zobal | Partiel, moteur générique | Masques, coût de transition et protection multi-tours |
| Forgelance | Partiel, moteur zones/armes génériques | Lance et zones/placements, états de récupération réels |

Voir `docs/codex/CLASS_BUILD_RESEARCH_20261113.md` pour la matrice de recherche complète (19 classes), les besoins de caractéristiques, les conditions d'équipement et les pistes de métiers/kamas. Sources YouTube listées comme pistes et **non visionnées** : ne jamais transformer leurs titres en preuves serveur. Ne pas inventer de prix HDV, DPS, taux de drop ou revenu kamas.

### Recoupements externes du 13/11/2026 (NON probants pour le protocole)

- https://dofus-grimoire.fr/classes/roublard/ : descriptions communautaires de murs, charges, détonation et enchaînements ; **les nombres annoncés ne sont pas importés au moteur** sans même grade et effet du client versionné.
- https://backfromafk.com/classes/roublard/depuis-3-0/ : historique communautaire des descriptions de sorts modifiées de 3.0 à 3.7 ; source utile pour repérer les régressions de mappings/variantes, **pas une validation des événements réseau**.
- https://teckium.fr/rentabilite-craft/?jeu=dofus3 : méthode de coût des recettes, prix par serveur et marge nette après taxe. Les prix sont alimentés par des relevés et joueurs : exiger timestamp, serveur, quantité, lot, taxe réelle et coût des sous-crafts avant tout achat/revente automatique.
- https://geneka.net/guide : propositions de calcul de risque FM, marge HDV et sessions de farm. Ces résultats restent des **estimations externes**, et non des gains prouvés : l'appli doit mesurer dépenses, échecs, recettes et vente réelles.

Les guides peuvent suggérer de nouveaux scénarios négatifs et priorités de tests. Les événements, IDs et transitions autorisés restent ceux du client installé et des traces serveur corrélées.

## Prochains raccordements strictement nécessaires

1. **Contrats de combat vers moteur** : convertir le flux paquet en `ComboObservation` avec corrélation et événements vérifiés, greffer dans le journal/replay et faire échouer les heuristiques si la provenance/grade manque. Relier ensuite au planner uniquement les transitions réellement modélisées.
2. **Roublard** : remplacer les templates historiques `2831..2836` dans `CombatTurnPlanner.cs` par un test de bombes propriétaire/versionné, mais sans bonus de mur tant que l'activation du mur n'est pas observée par un événement non ambigu.
3. **Carte** : les chemins d'attaque GUI et Lua sont raccordés ; conserver l'exigence d'écho serveur corrélé et approfondir le test de races sur le menu multi-bot. L'instantané acteurs utilise désormais un séquenceur de publication pair/impair, **mais** la dernière vérification et l'injection réseau ne sont pas une transaction atomique. Capturer également PID/session au menu de clic droit pour refuser un changement de bot entre l'ouverture du menu et l'action.
4. **Runtime** : connecter `SessionBinder.Forget` à la fermeture effective TCP, corréler PID/connexion au dispatch, vérifier l'ordre des publications d'événements.
5. **Économie** : prouver taux/arrondis de taxes HDV, traiter les ordres à issue inconnue avec une réservation persistante, revoir concurrence entre Watcher et Bulk ; protéger sauvegardes inter-processus si usage multi-instance.
6. **Métiers/FM/expéditions** : priorité à replays de prix/stats, garanties d'UID et reprise idempotente après desync, sans supposer les rendements ni les montants.
7. **Validation finale** : régressions transversales, test live uniquement sur client strictement possédé et identifié, protection de MizanBot/processus étrangers, journal explicite des ACK réellement reçus.

**Conclusion** : les correctifs ont une preuve hors jeu, la recherche couvre toutes les classes, mais l'objectif utilisateur « meilleur bot ever » n'est **pas achevé**. Les contrats de six classes constituent une base prudente, non une IA complète et autonome pour ces classes.
