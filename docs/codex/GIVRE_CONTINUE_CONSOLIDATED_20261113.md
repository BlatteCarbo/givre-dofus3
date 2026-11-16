# GivreClone — checkpoint de continuation, 13 novembre 2026

**Dépôt réel :** `/a/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`
(`GivreWork` parent est un agrégateur). **HEAD observé :** `b4d28a8`.
Arbre Git déjà massivement modifié avant cette vague ; aucun reset/clean,
commit, push, suppression de modifications inconnues ou lancement du jeu.
Client de référence documentaire : **DOFUS 3.7.13.17**, mais tous les
résultats ci-dessous sont des validations **hors jeu**.

## Changements réalisés

1. **Réseau, ordre de publication MZP1.**
   `MZP1GuardedEngineWiring` utilise une FIFO bornée à 1024,
   un seul drain hors verrou, purge à la révocation et rejet avec
   révocation en cas de débordement ; 5 tests de concurrence.
   Les callbacks déjà dépilés ne peuvent être interrompus rétroactivement,
   et l'ordre amont des deux flux réseau n'est pas garanti.
   Voir `GIVRE_CONTINUE_MZP1_ORDERED_FANOUT_20261113.md`.
2. **Prévisualisations HDV.**
   `HdvSellPreviewMath` sature les estimations, compteurs de lots et
   sommes affichées sans les convertir en vrais prix, rejette les
   piles à quantité invalide et signale les taux de taxe inconnus
   plutôt que de fabriquer un taux de 2 %. Libellés FR/EN/ES.
   Voir `GIVRE_CONTINUE_HDV_PREVIEW_OVERFLOW_20261113.md`.
3. **Prix réels HDV.**
   `HdvSalePriceMath` utilise des calculs `decimal` exacts et
   bornés pour planchers, dérivations et sous-cotes. Sur débordement,
   vente/repricing automatisés **refusés** plutôt qu'envoyés à
   des minima artificiellement bas. `HdvSalePriceGuard` refuse le
   dépassement du plafond de référence `decimal` et le garde
   de l'émission refuse un taux `float` non fini. Le projet tests
   lie explicitement le helper pur.
   Voir `GIVRE_CONTINUE_HDV_REAL_PRICE_SAFETY_20261113.md`.
4. **Observations de combos combat.**
   `ClassMechanics.ReadConfirmedFightMarkObservation` adapte les
   marques, activations, retraits et désactivations de
   `GameActionFightEvent`, sans fabriquer un cast, ACK, Téléfrag,
   mur Roublard ni explosion. Une marque désactivée invalide la
   preuve de combo correspondante. **Aucun hook runtime branché**
   dans `LaunchService` : il reste à établir identité, génération,
   ordre et causalité sur l'ingress authentifié.
   Voir `GIVRE_CONTINUE_COMBO_MARK_OBSERVATIONS_20261113.md`.
5. **Cycle de capture TCP.**
   `DofusCaptureHost` sérialise les phases `Stopped` /
   `Starting` / `Running` / `Stopping`, assure un rollback
   si un observateur lève après l'ouverture d'un listener, et
   évite un deadlock d'arrêt réentrant sous callback.
   Tests loopback isolés, sans jeu ni injection.
   Voir `GIVRE_CONTINUE_TCP_CAPTURE_LIFECYCLE_20261113.md`.
6. **Fermeture TCP avant première trame.**
   `SessionBinder.Forget` enregistre les IDs fermés même en
   absence de session, tandis que `Detach` révoque les IDs encore
   présents. `Attach` conserve ces tombstones pour un redémarrage
   du **même proxy**, mais pas pour une instance différente possédant
   son propre espace d'identifiants. Quatre tests en mémoire.
   Voir `GIVRE_CONTINUE_TCP_CLOSE_TOMBSTONES_20261113.md`.

7. **Réservations fiscales HDV partagées.**
   `HdvTaxReservations` regroupe, par PID et profil, les coûts
   prévisionnels des ventes manuelles, des lots Bulk et des repricings
   (manuels ou automatiques). Chaque mutation réserve sous `Gated`
   avant son éventuel envoi. Aucun delta kamas ni résultat incertain
   ne libère la réservation. Le registre reste en mémoire seulement.
   Voir `GIVRE_CONTINUE_HDV_SHARED_TAX_RESERVATIONS_20261113.md`.
8. **Fermetures inter-proxy.**
   `SessionBinder.ForgetFrom` contrôle la provenance de l'instance
   et `BotOrchestrator` transmet son proxy : un close tardif de A
   ne retire plus une session de B ayant réutilisé le même ID.
   Voir `GIVRE_CONTINUE_TCP_CLOSE_PROVENANCE_20261113.md`.

## Validation du checkpoint antérieur (avant la vague durable/générations)

| Suite | Résultat | Journal |
| --- | --- | --- |
| Tests ciblés fiscalité partagée + provenance TCP | **34/34 PASS** | `validation-20261113-hdv-tcp-provenance-targeted.log` |
| `GivreClone.Tests` hors Live | **5 870 PASS / 2 ignorés / 0 échec** | `validation-20261113-hdv-tcp-provenance-full-nonlive.log` |
| `GivreClone.PureTests` | **362/362 PASS** | `validation-20261113-hdv-tcp-provenance-puretests.log` |
| `CombatShadowComparison.Harness` | **65 assertions PASS** | `validation-20261113-hdv-tcp-provenance-shadow.log` |

Les quatre commandes ont retourné **exit 0**. Les journaux sont
sous `docs/codex/`. La progression mesurée par rapport au début de
la continuation est **5 815 → 5 870 tests réussis hors Live**
(les deux ignorés restent ignorés). Ne pas appeler les tests
`HdvLiveUiTests` une validation live : leur fixture est hors jeu.

## Nouvelle vague : réservations durables, transport, tactique, navigation et métiers

9. **HDV : récupération des taxes après fermeture ou crash.**
   `HdvTaxReservations` dispose désormais de `TryReserveOrder`
   avec écriture append-only exclusive, `Flush(true)`, somme des
   réservations par profil serveur/personnage à travers les PID/runs,
   identifiant stable d'intention (UID, taille, prix et stock pour
   une vente) et anti-réservation double. Le fichier compagnon
   `.anchor` protège le nombre d'enregistrements et l'empreinte
   finale ; corruption, suppression d'un seul fichier ou crash
   entre fsync et commit du témoin bloquent le prochain envoi.
   La prévisualisation reflète ces réservations et dispose de
   messages de refus FR/EN/ES. Aucune taxe n'est libérée par un delta
   global de kamas ou un résultat d'ordre ambigu.
   Voir `GIVRE_CONTINUE_HDV_DURABLE_TAX_JOURNAL_20261113.md`.
10. **TCP : génération issue du véritable transport.**
    La source permanente `GivreProxy/GivreCore/Mitm/TcpProxyServer.cs`
    attribue un `RunEpoch` à chaque listener et à ses liens,
    invalide cette génération à `Stop`, contrôle atomiquement
    les liens admis après un `ConnectAsync` tardif et refuse les
    événements ou `Replay` d'une génération retirée.
    `SessionBinder` lie les abonnements `MessageCaptured` et
    `ConnectionClosed` au même proxy/epoch et revérifie la
    provenance avant dispatch, état et ACK. Les fermetures
    numériques sans source ne peuvent plus révoquer une session
    d'un proxy déjà démarré.
    Tests interleavings dans `TcpProxyGenerationInterleavingTests`.
11. **Planification tactique multiclasse.**
    Nouveau `VerifiedTacticalSequence` : plans bornés jusqu'à
    deux étapes avec PA/PM, carte, portée, ligne de vue, obstacles,
    identité des cibles, positions/états connus, classe/grades,
    ordres causaux et revalidation après chaque action. Les six
    classes priorisées disposent de gardes propres (bombe,
    portail, Téléfrag, glyphe, carte, Souffrance).
    Une bombe ne cible pas une cellule déjà marquée sans preuve
    de compatibilité. Aucune action réellement envoyée par
    cette nouvelle classe seule ; aucune détonation déduite
    d'un effet générique. Voir
    `GIVRE_CONTINUE_MULTICLASS_SEQUENCE_20261113.md`.
12. **Ingress combat diagnostique borné.**
    `LaunchService.Packets` et `CombatComboIngressBuffer`
    relèvent pose/activation/retrait de marques avec PID/combat/
    carte/tour et snapshots immuables. Comme le décodeur
    `DecodedPacket` ne transmet pas la génération de transport,
    tous ses événements restent `ServerConfirmed=false`.
    Il est interdit de les traiter comme ACK ou preuves positives
    pour une tactique automatique. L'absence de ce raccordement
    est une **limite explicite de l'intégration**, pas une
    confirmation fabriquée.
13. **Navigation et métiers.**
    `MapPathfinder` remplace des états A* alloués par requête
    par des spans bornés et un voisinage immuable, en conservant
    l'ordre et la compression des chemins historiques ;
    tests différentiels et benchmark reproductible.
    `ProfessionPlannerEngine` limite désormais les acquisitions
    `MaxUnits` sur **toutes les branches d'un même plan**,
    propose un split déterministe entre sources comparables,
    refuse les doublons de provenance ou les besoins non
    intégralement couverts. Voir
    `GIVRE_CONTINUE_NAVIGATION_PROFESSION_20261113.md`.

### Validation ciblée de la nouvelle vague

Après liaison explicite du nouveau planner aux sources pures
du projet de tests, compilation et **75/75 tests ciblés PASS**,
`exit 0`. Journal
`docs/codex/validation-20261113-wave-targeted-third.log`.
Deux problèmes de fixtures/contrats ont été résolus sans
retirer d'assertion : fermeture source-scoped après Detach
au lieu d'un ancien handler désabonné, et refus d'une bombe
sur marque active ; `PlacesGroundMark` nullable est traité
conservativement. Les échecs des deux tentatives initiales
restent enregistrés dans `validation-20261113-wave-targeted*.log`.

La suite complète non-Live, PureTests, le shadow Release
et le benchmark A* sont exécutés **séquentiellement** ;
ne retenir leurs résultats que lorsque les processus
retournent effectivement leur code de sortie.

## Risques importants restant ouverts (après la nouvelle vague)

- **Transport :** lien et génération sont désormais vérifiés
  dans la source permanente, mais les deux tâches de pompe
  ne sont pas attendues jusqu'à leur fin par `Stop`.
  Un callback déjà entré peut achever sa publication après
  révocation ; le fallback ancien `obj/local-deps` ne donne
  pas la même preuve. Ne jamais assimiler une capture réseau
  à une confirmation serveur.
- **HDV :** les confirmations de ventes/repricing identifient les UIDs,
  mais ne fournissent **pas** de montant de taxe rattaché à l'ordre.
  Le delta de solde `KamasUpdateEvent` ne suffit pas à lever une
  réservation ; le journal la conserve désormais à travers
  les redémarrages de ce DataRoot. Aucun rapprochement causal
  par UID n'est disponible et les autres vues/scripts externes
  ne passent pas automatiquement par le ledger. Les deux
  fichiers supprimés ensemble ne peuvent être détectés par
  eux-mêmes ; une ancre externe serait nécessaire.
- **Combats :** l'ingress n'a pas de provenance de connexion
  certifiante dans `DecodedPacket` : il reste strictement
  diagnostique. Les séquences de deux actions ne remplacent
  pas encore un vrai planificateur multi-tours branché aux
  gardes autoritaires et aux combos avancés de toutes les classes.
- **Navigation et métiers :** optimisation A* à mesurer ;
  le plan métier demeure consultatif (`Executed=false`,
  `Acknowledged=false`). FM, craft, récolte, banque/pods,
  progression, donjons et ergonomie desktop doivent continuer
  à progresser.

## Prochaine étape sûre

Propager la vraie provenance (proxy, génération, connexion)
du transport vers `DecodedPacket` et la validation des sorts ;
ensuite seulement autoriser des preuves de combos positives et
brancher le planificateur tactique dans le combat réel.
Terminer le drain sûr des pompes au `Stop` avec tests
d'entrelacement et garder l'invariant zéro ACK inventé.
Pour l'HDV, permettre le rapprochement d'ordres à partir
de reçus serveur explicitement attribuables, ou demander un
rapprochement opérateur plutôt que de libérer une taxe ambiguë.
Chaque nouvelle vague doit reconstruire et tester hors jeu avant
une éventuelle preuve live strictement limitée à un processus
Givre explicitement autorisé.

## Continuation complémentaire : orchestration durable et interleavings (13 novembre)

**Nature : travaux de code nouveaux, distincts des 75 tests ciblés de la
vague précédente.** Le dernier état de ce dépôt ne bénéficie pas par
héritage des 5 870 tests hors Live / 362 PureTests / 65 assertions shadow
du checkpoint ancien. Ne jamais présenter ces nombres comme la validation
des modifications suivantes.

### Entrées applicatives HDV

- `HdvTaxedMutationFlow.ExecuteAsync` persiste le journal de l'intention,
  puis `Attempted` **avant** la délégation au véritable sender. Le callback
  lié aux corrélations UID/quantité/prix peut produire `Observed` ; `false`,
  timeout, exception ou annulation après la tentative entraînent `Uncertain`
  lorsque la transition est encore enregistrable. `Attempted` non résolu
  interdit les ordres plafonnés nouveaux du profil ; le même ordre est
  dédoublonné après relance. Aucune phase ne libère la taxe réservée.
- Les cinq points d'entrée UI utilisent désormais ce flux sous `Gated` :
  `SellSelectedCore`, `RunSellAsync` Bulk, `RepriceListing`,
  `UndercutListing`, `RunRepriceCycle` (bouton global / watcher).
  Un contrôle d'UID stock/prix et, où présent, d'epoch mutation est réévalué
  sous verrou juste avant réservation et envoi.
- Nouveau `HdvTaxedMutationFlowTests` : vrai ledger fichier et delegate
  sender factice, contrôle de phase avant callback, reprise après crash,
  résultats positifs/indéterminés, exceptions, annulation, PID/profil,
  corruption, deux instances concurrentes, prix/taux invalides et
  extrêmes. **N'est pas une validation du jeu** ni un reçu de taxe serveur.
- `MaxTaxSpend=0` garde volontairement l'ancienne absence de plafond ;
  les nouveaux contrôles de prix positifs restent actifs. Les appels
  directs `LaunchService` et les scripts Lua contournant le ViewModel
  ne sont pas automatiquement soumis au même plafond.

### Transports et captures

- Source permanente `TcpProxyServer` : snapshot des liens sous `_gate`
  dans `Stop` pour empêcher l'ancien stop de fermer un lien d'un
  démarrage réentrant ; révocation explicite et recontrôle après
  acquisition du verrou d'envoi, dans les pompes et le replay.
  `SessionBinder` revalide l'instance/génération avant chaque callback
  `SessionOpened` et `SessionAuthenticated`. Six nouveaux tests
  `TcpProxyEofStopDrainInterleavingTests` simulent Stop/Start,
  callbacks tardifs, EOF et écriture concurrente sans jeu.
- `CaptureDecoder` attribue désormais des `DecoderEpoch` et
  `DecoderOrder` **locaux** et les invalide sur `Forget(pid)` ; l'ingress
  vérifie ordre, type, sens, identité de callback et instantané combat.
  `ServerConfirmed=false` et `ServerProvenanceEstablished=false`
  restent obligatoires : MITM/socket n'apportent pas encore de token
  de transport au décodeur. Les `?.` de `_comboIngress` protègent
  les fixtures `FormatterServices` de Summon.

### Tactique, navigation et métiers

- `VerifiedTacticalExecutor` expose une étape consultative à la fois,
  avec états `Disabled/Advisory/Pending/Confirmed/Expired`, délais et
  preuves bornées, et `CanDispatch=false` inconditionnel. Il ne possède
  pas de sender et n'invente ni cast, ni ACK, ni continuité multi-tours.
  Le projet tests lie explicitement le nouveau source pur.
- `ResumableRoute` sauvegarde carte/génération/checkpoint, FIFO
  d'émission et invalidation obstacle, puis vérifie le suffixe A* exact
  avant réemploi ou recalcule depuis position réobservée ; limites
  d'essais/stagnation/revisites et refus de l'observation obsolète.
  APIs **pures** : non reliées encore à `LaunchService`.
- `ProfessionRecoveryWorkflow` et les API de
  `ProfessionPlannerEngine` exigent des snapshots complets et
  révisions croissantes de stock et capacités, empreintes stables
  de requête/ancien plan et replanification à partir d'observations
  fraîches. Pas de craft/recolte présumés confirmés ; UI/MCP non
  encore raccordés à ce workflow.

### Limites de cette continuation

Les revues et tests en mémoire ne certifient pas la causalité d'un ACK
pour un cast, un montant fiscal par UID, un vrai trajet repris ou un
craft exécuté dans le jeu. La prévisualisation HDV et les observations
de marques restent distinctes d'une mutation authentifiée. Une
écriture TCP déjà physiquement démarrée au moment d'un Stop ne peut
pas être retirée rétroactivement. La comparaison de performance A*
doit publier des chiffres mesurés et vérifier les routes/coûts.

### Validation de la continuation complémentaire

- Compilation et premier ciblage de `GivreClone.Tests` : **79 cas
  exécutés, 78 réussis, 1 échec**, exit 1. Journal
  `validation-20261113-wave-four-targeted.log`. Les 3 scénarios
  `SummonDispatchIntegrationTests` sont inclus dans les 78 réussites :
  l'ancienne NRE `FormatterServices` n'est plus reproduite après rebuild.
  Le cas rouge `CombatComboProvenanceDecoderTests.Reversed_order_and_direction_masks_are_refused_without_server_proof`
  signale que le test soumettait le véritable paquet admis avec un
  contexte de fight/map erroné, le consommant légitimement avant de
  tenter sa relecture positive. Revoir le test avec une nouvelle trame
  décodée, **sans autoriser le replay du paquet périmé** ; aucune
  conclusion de validation globale verte avant nouveau rebuild.
- Après correction de cette fixture par nouveau paquet décodé (le
  comportement `one-shot` fail-closed de production reste inchangé),
  **79/79 tests ciblés PASS, exit 0** sur rebuild ; journal
  `validation-20261113-wave-four-targeted-rebuild.log`.
- Benchmark observationnel ciblé `ResumableRouteBenchmarkTests` :
  **1/1 PASS, exit 0** ; journal
  `validation-20261113-wave-four-route-benchmark.log`.
  96 checkpoints déterministes × 3, nouveau calcul A* :
  **37 021 ticks, 251 320 octets alloués, 1 056 cellules encodées** ;
  suffixe repris : **8 786 ticks, 340 912 octets alloués,
  1 098 cellules encodées**. Le suffixe est 4,21× plus rapide dans
  ce microbench mais alloue davantage et peut emprunter d'autres
  virages exacts ; ce n'est pas un speedup général ou un substitut
  à la vérification d'équivalence des routes/coûts.

- Suite globale `GivreClone.Tests` hors Live après rebuild :
  **5 982 PASS / 2 ignorés / 0 FAIL** sur 5 984 au total, **exit 0** ;
  journal `validation-20261113-wave-four-full-nonlive.log`.
  Ce total dépasse de 112 le dernier checkpoint réussi, qui comptait
  5 870 PASS, mais il ne constitue pas une preuve d'exécution live.

- `GivreClone.PureTests` : **362/362 PASS, exit 0** ; journal
  `validation-20261113-wave-four-puretests.log`.
- `CombatShadowComparison.Harness` Release : **65 assertions exécutables
  PASS, exit 0** ; journal `validation-20261113-wave-four-shadow.log`.

La compilation ciblée, la régression globale hors Live, les PureTests,
le shadow et le microbenchmark ont été lancés séquentiellement sans
lancer DOFUS ni agir sur MizanBot. Les résultats ne démontrent ni
réception d'ACK serveur ni bon fonctionnement live sur 3.7.13.17.

## Vague d'intégration supplémentaire — 14 novembre 2026

Cette section complète le checkpoint ci-dessus, **sans remplacer ses
preuves ni attribuer ses nombres à de nouveaux binaires**. Tous les
travaux restent hors jeu et dans l'arbre Git existant.

### HDV : parcours réels, Lua et réconciliation consultable

- Les cinq senders `HdvViewModel` (`SellSelectedCore`, `RunSellAsync`
  Bulk, `RepriceListing`, `UndercutListing`, `RunRepriceCycle`) passent
  toujours par `Gated` puis `ExecuteTaxedMutationAsync`. Nouveau contrôle
  avant réservation de l'UID **et du GID**, de la quantité observée,
  du stock physique et de l'epoch de mutation ; reprice exige un UID
  vendeur présent avec GID/lot cohérents. Une session UI remplacée ne
  reçoit pas le statut de l'ancienne mutation après l'attente.
- **Nouveau chemin réellement raccordé des scripts Lua** :
  `sale:SellItem`, `EditPrice`, `EditPriceByGID`, `UpdateAllItems` et
  `repriceAll` appellent maintenant des méthodes
  `LaunchService.Hdv.ScriptTax` qui partagent le **même** journal
  `tax-reservations-v1.journal` et les mêmes clés anti-replay
  `HdvTaxOrderKeys` que l'UI. La méthode
  `HdvScriptTaxMutationBroker.ExecuteAsync` fige PID/profil
  serveur-personnage/plafond/taux, puis valide de nouveau cette
  politique après écriture durable `Attempted`, juste avant le
  sender. Repricing Lua par lots s'arrête au premier résultat ambigu
  ; `repriceAll` signale `-1` si l'opération globale est interrompue.
  **Le serveur n'émet pas de reçu de taxe par ordre** : les réservations
  ne sont jamais levées à partir d'un bool de listing ou de kamas.
- `HdvLuaOrderNumeric` protège les véritables entrées Lua : nombre fini,
  positif (ou zéro lorsque l'API autorise explicitement la valeur
  illimitée), entier exact IEEE-754 (≤ 2^53−1), UID/quantité dans
  `Int32`. Achat (`buyItem`) : un NaN ne peut plus devenir le plafond
  implicite `0`. La multiplication de moyenne par lot refuse un overflow.
- `HdvTaxedMutationFlow` préserve `MaxTaxSpend=0` comme **absence de
  plafond monétaire**, mais un ancien journal `Attempted/Uncertain`,
  corrompu ou un ordre identique déjà persisté ne peuvent plus être
  contournés en désactivant ensuite le plafond. Pour une opération
  neuve non plafonnée et sans journal antérieur, le comportement
  historique n'écrit pas de réservation : ce mode n'est **pas** un
  contrôle d'idempotence interprocessus complet.
- `HdvTaxReservations.ReadProfileSnapshot` expose au panneau HDV
  classique des états `Ready/NeedsReconciliation/Unavailable` et les
  réservations maintenues. L'ancre fiscale nouvelle version est
  préparée dans un fichier temporaire unique, `Flush(true)` puis
  remplacement atomique après fsync du journal. Une écriture partielle
  garde les octets et bloque les opérations suivantes sans réparation
  hasardeuse.
- Les APIs directes `LaunchService.HdvSellAsync`,
  `HdvRepriceAsync`, et les clients externes autres que les aliases
  Lua restent des entrées à traiter séparément pour éviter **une
  double réservation** des opérations de la vue. Les APIs d'achat,
  retrait et les anciennes opérations sans plafond n'acquièrent
  pas artificiellement un reçu serveur. Un PID seul ne prouve pas
  l'absence de changement de transport pendant un await.

### Réseau et preuve de transport

- Dans la **source permanente** `GivreProxy/GivreCore/Mitm/TcpProxyServer.cs`,
  `TransportLinkProvenance` lie instance du proxy, runEpoch,
  jeton de lien monotone, connectionId et PID. Les captures typées
  `TransportFrameObserved`, fermetures `TransportLinkClosed`,
  `IsCurrentTransportLink` et `Replay(provenance, ...)` interdisent
  les anciens liens et identifiants recyclés au point de publication
  et d'écriture. Le sink de `BotOrchestrator` est épinglé au lien
  pour l'interface hostBound, et `SessionBinder` conserve la
  provenance source lors des callbacks. FIFO MZP1 préservée.
- Le **véritable** lien produit maintenant une preuve côté moteur,
  mais le chemin de l'application principale
  `HandshakeRelay.OnData(pid, sens, bytes)` et Socket
  `FeedSocketFrame(pid, sens, wire)` ne la transportent pas jusqu'à
  `DecodedPacket`. Une API moteur ne suffit donc pas à affirmer
  `ServerConfirmed=true` dans `LaunchService` : cette autorité
  reste **strictement fausse**. `Replay(true)` reste une émission
  locale, jamais un accusé serveur.

### IA, parcours hors combat et métiers

- Le décodeur attribue l'identité locale des vrais objets paquet
  par `ConditionalWeakTable` et refuse leurs clones ; l'ingress
  diagnostique enrichit `ComboFrame` et les marques attestées
  (show de combattant, breed, tour, ordre) sans attribuer cast,
  Téléfrag, explosion, envoi ni ACK. Le rejet one-shot du test
  `Reversed_order_and_direction_masks...` reste inchangé.
- `LaunchService.Combat.TacticalAdvisor` relie le bridge à des
  événements de combat réels via `FightersUpdated`,
  `FighterStatsUpdated`, `MapCellsLoaded` ; opt-in par PID,
  statut et événement consommables par l'UI classique, uniquement
  sur données de sorts/grades, PA/PM, géométrie et positions observés.
  Sans provenance ACK de connexion, **CanDispatch=false** et
  aucune mutation combat émise par cette fonctionnalité.
- `GivreLuaHost.Route` fournit désormais
  `resumableRouteDiagnostic=true` sur un stage cellule existant.
  `ResumableTravelWorkflow` contrôle carte, génération, cellule
  serveur et obstacles avant de laisser le `GoToCell` historique
  agir ; pas de réémission automatique du mouvement sans
  ACK corrélé à la séquence. La persistance/réconciliation d'une
  vraie reprise active reste ouverte.
- `ProfessionMcpFacade` expose maintenant les opérations
  `recovery_status` et `recovery_preview`, activables seulement
  avec un `ProfessionMcpRecoveryContext` explicitement autorisé
  par l'hôte. `ProfessionRecoveryIntegration` revalide
  provenance, manifest, stock et caps complets, révisions,
  empreintes et reçu vérifié indépendamment par l'hôte après
  crash. Sans observateur/receipt verifier connecté, la réponse
  demeure diagnostique `UNKNOWN` ; aucun craft ni récolte
  prétendument confirmé.

### Nouvelles suites et preuves

Tests nouveaux : `HdvLuaTaxSharedLedgerTests`,
`HdvScriptTaxMutationBrokerTests`, `HdvLuaOrderNumericTests`,
compléments `HdvTaxDurableRecoveryTests` et
`HdvTaxedMutationFlowTests`, `TcpProxyTransportProvenanceTests`,
`CombatIngressPipelineTests`, `VerifiedTacticalWorkflowGuardTests`,
`ResumableTravelWorkflowTests`, `ProfessionRecoveryIntegrationTests`.
Ce sont des scénarios hors jeu ; la **validation du présent état**
est consignée uniquement après un build et les codes de sortie
observés dans les journaux `validation-20261114-wave-*.log`.

Risques restants prioritaires : transporter le token de connexion
exact jusque dans les reducers et le dispatch du client principal,
obtenir des ACK de sorts/mouvements causaux avant d'autoriser les
séquences ou reprises actives, généraliser le journal fiscal aux
clients directs **sans double charge**, brancher les vrais
readers/receipts métier au MCP avec CAS après relance, puis
développer les routines FM, banque/pods et progression à partir
de données observées. Aucune validation live n'a eu lieu.

### Vérification incrémentale de cette vague (14 novembre)

- Premier build ciblé : erreur de compilation CS1503 sur `out _`
  intercepté par les paramètres des lambdas Lua/ThreadPool et erreur
  CS1061 sur `FighterStats?`. Corrigés sans affaiblir le contrat de
  provenance, d'action ou de résultat.
- Rebuild ciblé ensuite **189/189 tests PASS, exit 0** : journal
  `validation-20261114-wave-targeted-rebuild1.log`.
- Première suite générale hors Live sur ce build :
  **6 040 PASS / 2 ignorés / 2 FAIL sur 6 044, exit 1**,
  `validation-20261114-wave-full-nonlive.log` ; deux anomalies :
  `LuaApiInventoryTests.Inventory_covers_the_current_binding_sources`
  (empreinte ancienne des bindings modifiés) et
  `TeamJoinWaitPolicyTests.Production_wait_stops_when_the_expected_follower_never_arrives`
  (fixture utilisant `Stores.World` statique concurrent).
- L'exécution isolée détaillée prouve que l'échec Lua concernait
  **exactement** `GivreLuaHost.Bindings.cs`, et que l'attente
  d'équipe donne la sortie attendue sur la fenêtre sans progrès (8 s).
  L'inventaire a été régénéré avec le script de référence
  `python docs/portal/lua-f07-inventory.py` : **1 018 bindings,
  0 signatures documentées absentes** ; tests isolés des modules
  inventaire/équipe ensuite **13/13 PASS, exit 0**, journal
  `validation-20261114-inventory-team-repeat.log`.
- L'annotation `[Collection(... DisableParallelization = true)]`
  isole désormais le test de production d'attente d'équipe des
  autres collections modifiant le singleton `Stores.World`, sans
  modifier les délais réels de 8 s / 20 s ni assouplir l'assertion.

Cette suite globale précédente **n'était donc pas verte** ; les
reconstructions et nouveaux résultats doivent être consignés
explicitement avant d'annoncer un état sans régression.

## Reprise de la vague fonctionnelle — 14 novembre, intégration hors jeu

Cette section est un **nouveau checkpoint**, postérieur aux résultats précédents.
Les cinq workers tactique, route/récolte, profession, ingress et BotHost avaient
terminé leurs éditions avant la compilation PRIME. Leur résultat a été intégré
sans lancement de DOFUS, sans toucher MizanBot et sans supprimer de dirty Git.

### Entrées applicatives et protections opérationnelles

- **Tactique classique** : option `CombatProfile.TacticalAdvisorDiagnostic`
  persistée par personnage, bouton et état compact dans `BotsView`, liaison des
  publications `LaunchService.Packets` au conseiller. La sélection réelle
  `VerifiedTacticalExecutor.PrepareObservedDecision` est appelée avec les
  grades/sorts observés, bornes d'exploration et réinspection de l'état. La
  projection hypothétique n'est **jamais** un ACK : le snapshot d'autorité
  demeure `ServerConfirmed=false`, `CanDispatch=false` et ne lance aucun cast.
- **Atelier réel** : `MainWindow` fournit le personnage sélectionné, les ports
  de fabrication et la recette à `ItemCraftPlannerViewModel`. Le runner
  `ItemCraftProductionRun.RunAsync` traverse le journal durable
  `ItemCraftProductionAttemptJournal` et les ports
  `ItemCraftSessionBoundActions`; les runs incomplets, la reconnexion et les
  observations insuffisantes restent à réconcilier, sans rejeu automatique.
  `ProductionRecoveryStatus` et le contrôle de lecture sont visibles dans
  l'atelier. Le résultat tardif (succès ou erreur) ne peut plus écraser l'UI
  d'un autre personnage après changement de session.
- **Profession MCP guidée** : l'entrée `production_next_action` utilise le
  vrai port de production seulement si un hôte apporte les inventaires,
  capacités, révisions, attestations et un journal CAS indépendants. La
  réponse `nextAction` expose désormais les champs JSON `kind`, `stepId`,
  `itemGid`, `quantity`, `recipeId`, `observationRevision` en camelCase.
  Elle décrit uniquement la **prochaine action manuelle**, jamais une
  fabrication, un ACK ou une exécution automatique. Son journal est séparé
  de l'admission durable du runner et ne doit pas être appliqué deux fois.
- **Route et récolte** : `activityRecovery=true` par étape ou
  `ACTIVITY_RECOVERY=true` active un préflight observé et un seul segment
  Lua. Après émission potentielle, la fenêtre est quarantainée tant qu'aucun
  ACK de séquence/connexion exact n'existe ; ni carte changée, ni stock modifié
  ne suffit à autoriser le prochain segment. Pas de reprise multi-étapes
  prétendue.
- **Transport** : `BotHost` et `BotHostService` publient des observations de
  frame typées avec `TransportLinkProvenance` revalidée ; callbacks anciens et
  abonnements après Stop/Start sont révoqués. `Directory.Build.targets` donne
  désormais à `GivreClone.App` la même dépendance `GivreCore` source que
  le moteur et les tests, évitant l'assemblage local obsolète dépourvu de ces
  types. `dotnet restore` explicite effectué, exit 0. Le lien prouve au plus
  la connexion locale du proxy ; il **n'atteint pas encore** le chemin
  `HandshakeRelay`/Socket/`ComboIngress` du client principal et n'est pas
  une confirmation du serveur. FIFO MZP1 maintenue.
- **HDV** : trois chemins de revente indirecte (relisting Trade, atelier
  ItemCraft et `LotMGame`) passent par le broker fiscal script commun et
  les paramètres persistants du profil serveur/personnage. Les vérifications
  GID, prix et caps sont revalidées à la frontière d'émission ; aucune variation
  globale de kamas ne libère les taxes attribuées à un ordre.
- **`MaxTaxSpend=0`** : conserve sa signification historique **sans plafond
  monétaire**, mais n'exempte plus les mutations du journal durable.
  `HdvTaxedMutationFlow` effectue aussi `Reserved -> Attempted ->
  Observed/Uncertain` pour `cap=0`, avec fsync avant le sender, collision UID
  refusée, crash/concurrence et incertitude bloquants. `HdvTaxReservations`
  accepte le cap zéro via `HdvTaxBudget.CanReserve`, sans plafond caché ; le
  cumul purement informatif des frais sature à `long.MaxValue` plutôt que de
  provoquer un overflow en lecture. Les frais ne sont jamais libérés sans
  reçu serveur attribuable. Un journal indisponible interdit l'émission.

### Vérifications réellement exécutées sur ce checkpoint

| Contrôle | Résultat | Journal |
|---|---|---|
| Ciblé recompilé combat/activité/production/réseau/HDV | **137/137 PASS**, exit 0 | `validation-20261114-wave-two-targeted.log` |
| Ciblé recompilé fiscal/production après mode illimité | **60/60 PASS**, exit 0 | `validation-20261114-wave-three-fiscal-targeted.log` |
| Suite `GivreClone.Tests` hors Live, premier passage | **6 104 PASS, 2 ignorés, 1 FAIL** (empreinte Lua), exit 1 | `validation-20261114-wave-three-full-nonlive.log` |
| Inventaire de référence Lua régénéré avec le script officiel | **1 018 liaisons, 0 signature documentée absente** | `docs/portal/lua-f07-inventory.json` |
| `LuaApiInventoryTests` après régénération | **4/4 PASS**, exit 0 | `validation-20261114-wave-three-lua-inventory.log` |
| Suite `GivreClone.Tests` hors Live finale, `--no-build` | **6 105 PASS, 2 ignorés, 0 FAIL** / 6 107, exit 0 | `validation-20261114-wave-three-full-nonlive-final.log` |
| `GivreClone.PureTests` | **362/362 PASS**, exit 0 | `validation-20261114-wave-three-puretests.log` |
| `CombatShadowComparison` Release | **65 assertions PASS**, exit 0 | `validation-20261114-wave-three-shadow.log` |

Le premier build a détecté un conflit entre les copies source et locale de
`GivreCore`; la référence App et le restore l'ont résolu sans retirer de
contrat transport. Des erreurs de syntaxe `from with` et de nommage JSON
`Kind` ont ensuite été corrigées ; aucun test négatif n'a été supprimé.
Le test ancien qui interdisait `cap=0` comme si zéro était une erreur a été
remplacé par une vérification de cap négatif, tandis que de nouveaux tests
positifs/négatifs certifient le journal illimité, la saturation, les collisions,
le crash et les tentatives concurrentes.

Ces chiffres ne sont **pas** des essais live ni des mesures universelles de
latence. Aucun changement du coeur A* n'a eu lieu ; les anciennes mesures
ne sont donc pas présentées comme nouvelles. Le coût réel du fsync fiscal
et la latence des ports de craft/réseau doivent être mesurés sur un banc
isolé avant toute conclusion de débit.

### Lacunes non closes

Le vrai token de connexion n'est toujours pas propagé jusqu'aux événements
`HandshakeRelay`/Socket et à la confirmation causale `ComboIngress` ; sans
ACK corrélé, **aucune exécution tactique ou reprise d'activité en chaîne**.
Réconciliation serveur des taxes par UID/ordre et réconciliation d'une
production incertaine restent à implémenter. Les capacités métiers et les
états de classes non attestés demeurent consultatifs ; ni FM de bout en bout,
ni banque/pods complet, ni progression autonome générale ne sont certifiés.
Le dépôt reste très dirty : aucun reset/clean/commit/push, aucun client lancé.

Contrôle final : `HEAD=b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
**693 entrées** `git status --short`. Le `git diff --check` limité aux
fichiers déjà suivis et édités dans la reprise est **exit 0**. Le contrôle
global est **exit 2** uniquement pour quatre défauts de whitespace
préexistants, préservés sans modification dans
`GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`,
`TurboFightPolicy.cs:391` et `GivreClone.App.csproj:184`.
La première série de 137 PASS et la fiscale de 60 PASS sont des sélections
distinctes, non des sous-totaux à ajouter aux 6 105 PASS de la suite finale.

## Checkpoint supplémentaire — vague 4, reprise durable et reçus (14 novembre)

Cette vague réutilise les raccordements établis précédemment. Les workers
`worker-9` (activité) et `worker-11` (ingress marques) ont gelé leurs fichiers
avant validation PRIME. Le worker production n'a pas livré de test final ; PRIME
a corrigé le runner et ajouté ses tests dans un fichier distinct, sans attendre
un résultat non confirmé. Les éditions antérieures des autres agents restent
intactes et le build est resté **strictement séquentiel**.

### Fonctions réellement reliées

- **Route/récolte Lua, opt-in uniquement** : `ActivityRecoveryDurableStore`
  crée un marqueur unique `FileMode.CreateNew`, `FileShare.None`,
  `WriteThrough` et `Flush(true)` sous
  `LocalApplicationData/GivreClone/ActivityRecoveryDurable/v1`, isolé par
  **serveur/personnage**, jamais par PID. `ReserveRecovery` vérifie identité
  observée, carte/cellule/obstacles puis persiste la tentative **avant**
  `GoToCell`, `ChangeMap`, `UseInteractive` ou `SendHarvest`. Crash juste
  avant/après émission, marqueur tronqué/corrompu, verrou concurrent, identité
  absente et changement de session refusent toute réémission. Le marqueur
  n'est jamais purgé automatiquement et `RecoveryForMap` ne le perd plus
  au prochain `_runGen`. Un blocage notifie le planning via le chemin d'arrêt
  déjà existant, sans redémarrage aveugle. Le script legacy hors opt-in n'est
  pas changé. La limite demeure **un seul segment potentiellement émis** :
  aucun ACK de connexion et séquence n'est disponible dans LuaHost.
- **UI desktop classique** : nouveau partial
  `BotsViewModel.ActivityRecovery.cs`, statut compact dans la console Lua,
  bouton `État reprise` qui lit explicitement le journal du personnage
  sélectionné via `RefreshDurableActivityRecoveryStatus()`. Le heartbeat
  Avalonia ne lit que des snapshots immuables publiés avec `Volatile.Read` :
  aucune boucle I/O disque ni action de jeu. Même un stock ou une position
  modifiés ne réarment rien ; consultation seulement.
- **Ingress combat au vrai décodage** : `LaunchService.Packets.OnDecoded`
  appelle l'unique `ObserveRuntimeEvent` **avant**
  `PacketDecoded?.Invoke`, empêchant un observateur réentrant d'invalider
  la watermark locale avant traitement du paquet initial. Le dispatcher
  `CombatComboIngressBuffer` raccorde `FightStartingEvent`,
  `FightTurnEvent`, `FightFighterShowEvent`, `GameActionFightEvent` et
  clôtures au reader réel `ClassMechanics.ReadConfirmedFightMarkObservation`,
  one-shot, avec époque locale, tour et ordre `DecoderOrder` réels.
  Les marques posées, activées, désactivées et retirées sont exposées comme
  **diagnostic**, `ServerConfirmed=false` et `CanAuthorizeCast=false` :
  `DecoderOrder` n'est PAS une preuve d'identité TCP. Aucun ACK,
  Téléfrag, mur activé, explosion ou cast n'a été fabriqué.
- **Vrai runner de fabrication** : `ItemCraftProductionRun.CraftOnceAsync`
  n'accepte désormais que le `ResultGid` **exact et positif** de la requête,
  un nombre d'objets strictement positif et cohérent avec le rendement
  documenté (ou le reste si le rendement est inconnu). Un reçu avec
  `GID=0`, GID différent, quantité impossible, ou reçu revenant après
  annulation n'incrémente plus `Produced` et n'autorise pas
  `QuantityReached`/la clôture durable `Completed`. Un retrait de banque
  `got<0` ou `got>ask` n'est plus silencieusement rabattu à une valeur
  plausible : le runner s'arrête avant achat/craft/listing sur état
  indécidable. Le chemin positif déjà validé reste intact.

### Validation PRIME, preuves horodatées par journal

| Exécution | Résultat réellement observé | Log |
|---|---|---|
| Rebuild ciblé route durable / ingress marques / UI | **38/38 PASS**, exit 0 | `validation-20261114-wave-four-durable-mark-targeted.log` |
| Rebuild ciblé craft + durabilité + combat + régressions | **60/60 PASS**, exit 0 | `validation-20261114-wave-four-craft-targeted.log` |
| Suite complète `GivreClone.Tests`, filtre non-Live, `--no-build` | **6 134 PASS, 2 ignorés, 0 FAIL**, total 6 136, exit 0 | `validation-20261114-wave-four-full-nonlive.log` |
| `GivreClone.PureTests` recompilés | **362/362 PASS**, exit 0 | `validation-20261114-wave-four-puretests.log` |
| `CombatShadowComparison.Harness` Release | **65 assertions PASS**, exit 0 | `validation-20261114-wave-four-shadow.log` |

Les sélections ciblées se recouvrent : ne pas les ajouter aux 6 134 tests
généraux. Ce checkpoint est **hors Live**, sans lancement de DOFUS et sans
processus MizanBot modifié. Aucun benchmark de performance n'a été exécuté
sur cette vague : le coeur A* n'a pas été modifié et les coûts de `fsync`
durable, de décodeur et d'UI restent à mesurer sur un banc représentatif.

### État Git et risques résiduels

`HEAD=b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`, **704 entrées**
`git status --short` au contrôle final. Aucune opération destructive,
`reset`, `clean`, commit ni push. `git diff --check` des fichiers suivis
édités ici : **exit 0** ; scans de trailing whitespace sur les fichiers
nouveaux : aucune erreur. Le contrôle `git diff --check` global reste
**exit 2** pour quatre anomalies préexistantes, non modifiées :
`GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`,
`TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184`.

L'autorité de `TransportLinkProvenance` reste confinée au moteur et ne
traverse pas encore `HandshakeRelay`/Socket/`FeedSocketFrame` jusqu'à
`ComboIngress`. Aucun cast tactique n'est autorisé sans provenance TCP et
confirmation serveur causale. Les leases durables de route/craft et les
réservations fiscales incertaines nécessitent **réconciliation indépendante
attribuable** ; il n'existe pas d'auto-clear sûr. Banque/pods complète,
production FM, donjons, progression et couverture avancée de toutes les
classes restent des chantiers non certifiés, sans revendication de complétude.

## Checkpoint supplémentaire — vague 5, liaisons source, rapprochement et refus finaux (14 novembre)

### Situation et discipline

La vague part du worktree **réel** à `HEAD=b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`
avec 704 entrées `git status --short` avant les nouvelles modifications.
Pas de `reset`, `clean`, stash, commit, push, lancement du client ou intervention
sur des processus étrangers. PRIME seul a compilé, après le gel des ownerships :
worker-12 (Socket), worker-11 (CaptureDecoder/port diagnostic), worker-8
(décision tactique), worker-9 (reprise route/récolte), worker-10 (runner craft).
PRIME a relié le relais local, l'entrée LaunchService, l'UI et le garde prix HDV.

### Réseau : véritables identités de connexions, pas de fausse autorité

- **MITM local réel** : `LocalHandshakeRelay` attribue une identité
  `GameRelayLink(RelayInstanceId, OwnerPid, RunEpoch, TunnelId)` émise par la
  source TCP, non reconstituée à partir du PID ou de `DecoderEpoch`. L'événement
  `OnGameData(link, chunkSequence, direction, bytes)` suit les lectures du tunnel
  *courant*, séquencées par lien et sens, et `OnGameLinkClosed` révoque exactement
  le lien clos, même quand un ancien callback survient après remplacement.
  `Start`/`Stop`, socket acceptée puis obsolète, connexion upstream asynchrone,
  PID/owner revalidés, EOF, remplacement de tunnel, injection via le writer
  verrouillé et callbacks réentrants sont bornés par la génération du relais.
  `MitmRelayAdmission` est réinitialisé au nouveau cycle `Start`, sans toucher
  aux flux des autres bots. Les trames ne sont pas réordonnées pour le MZP1.
- **Décodage MITM réellement câblé** : `LaunchService.cs` abonne le même
  `LocalHandshakeRelay.OnGameData` à `CaptureDecoder.FeedLinkScoped` avec
  `IsCurrentGameLink` de cette **même** source. Le décodeur conserve des buffers
  `(link, sens)`, epoch et ordre indépendants, rejette ordre rejoué, source
  retirée, enveloppe forgée et PID recyclé, et fournit `LinkedPacket` et
  `TryConsumeLinkedPacket` one-shot. La fermeture déclenche `RevokeLink` par
  identité exacte. Le `Feed` métier historique du même PID reste alimenté
  séparément pour ne pas casser carte, combat, inventaire ou scripts ; ses
  buffers sont oubliés lors d'un changement avéré de tunnel, sans revendiquer
  qu'il est certifié. Le login et les relais hérités continuent leur chemin
  legacy. Une anomalie du canal d'observation typé ne doit jamais interrompre
  le forwarding réseau ni le feed métier.
- **Socket clientless** : `SocketGameClient` crée `SocketLinkProvenance` seulement
  après dial réussi, observe des copies de frames ordonnées en FIFO dans une
  queue bornée, vérifie encore le lien avant callback et refuse les anciens
  injecteurs après EOF/Stop/remplacement. `SocketProbe` attribue un lease par
  PID synthétique, révoque la session antérieure et interdit à son callback
  tardif de publier/dépublier l'injecteur du successeur. `FeedFrame` classique
  est toujours une API PID-only : `FeedObserved` est un port optionnel distinct
  dont les consommateurs doivent vérifier la source à l'instant d'admission.
  Le `BotHostService.TransportFrameObserved` headless existant provient d'un
  autre transport et reste un **diagnostic sans jonction par PID ou hash**.
- **Frontière de preuve** : la présence d'un lien TCP, sa direction inbound,
  `Sent`, sa séquence de fragments, les objets `DecodedPacket` et les epochs
  locaux ne sont pas des reçus d'action serveur. `ComboIngress` et le port de
  marques restent `ServerConfirmed=false`, `CanAuthorizeCast=false`; aucun
  cast, mur, portail, Téléfrag ni ACK n'a été affirmé sans causalité observée.
  L'ancien `HandshakeRelay` hors édition locale ne transporte pas encore
  cette identité jusqu'aux reducers ; ne pas y fusionner la source headless.

### Combats, reprise, production, HDV et interface reliés

- `VerifiedTacticalSequence.IndexCastBoard/PrepareIndexed/NextIndexed` réutilise
  l'index de plateau de 560 cellules **pour un snapshot exact**, positions
  d'acteurs vivants et occupation incluses, au lieu de reconstruire un
  dictionnaire pour chaque proposition. `VerifiedTacticalExecutor` conserve
  une recherche déterministe bornée, refuse un changement de PA/PM, position,
  LoS, obstacles, états, marques, sorts ou epochs ; rétrograde toute
  confirmation **synthétique** lorsqu'on entre dans le mode diagnostic. Les
  branches Roublard, Éliotrope et Xélor ont une continuation déterministe
  testée ; Féca/Écaflip/Sacrieur et autres classes restent consultatifs en
  l'absence de jonctions d'effets et sorts authentiques. Aucune nouvelle
  permission d'émission n'a été activée.
- `ActivityRecoveryDurableStore.Reconcile` lit le **même** marqueur fsync
  serveur/personnage de la vague 4 et le compare à la carte, génération,
  position, élément interactif et inventaire actuellement observables. Il
  distingue observation de déplacement, ressource épuisée, stock non prouvé,
  marqueur corrompu/occupé ou identité insuffisante. Une observation de stock
  accru ou de carte atteinte n'autorise **ni clear ni second send** sans
  rapprochement attribuable. Lua opt-in `ACTIVITY_RECOVERY_RECONCILE=true`
  ou `activityRecoveryReconcile=true` effectue une inspection read-only avant
  le prochain geste ; l'automate planifié est stoppé si quarantaine, sans
  changer les scripts legacy non opt-in. `BotsViewModel.ActivityRecovery`
  expose cette preuve dans le panneau Lua classique, à la demande seulement,
  sans I/O disque à chaque heartbeat ou réarmement implicite.
- `ItemCraftProductionRun` conserve GID/quantité exacts, refus des retraits
  incohérents, et refuse désormais toute clôture sur coffre non réellement
  fermé, retour de craft/achat/listing après Stop et succès générique sans
  preuve de produit. La reprise durable de production et la corrélation
  serveur à chaque transaction restent incomplètes ; ces garde-fous ne les
  simulent pas.
- À la **frontière effective** `LaunchService.HdvSalePriceRefusal` commune
  aux ventes, repricings et listings, le minimum `HalfAverageFloor` est
  maintenant comparé **au prix réellement demandé** (pas seulement `>0`).
  Sous-prix manuel, script ou production et plancher non représentable sont
  refusés. Les tests couvrent GID/profil étrangers et débordement, sans prix
  saturé ni borne fictive. Le plafond des ventes et le journal fiscal durable
  existants restent inchangés ; `MaxTaxSpend=0` désigne toujours l'absence de
  plafond monétaire historique, **pas** une confirmation d'ACK ni un journal
  d'idempotence universel sans opt-in.
- Le générateur officiel `docs/portal/lua-f07-inventory.py` a régénéré
  `lua-f07-inventory.json` afin que les empreintes de sources et la liste
  des APIs reflètent le worktree actuel, sans supprimer de parcours Lua.

### Validation PRIME séquentielle, hors jeu uniquement

| Vérification | Résultat | Journal sous `docs/codex/` |
|---|---|---|
| Ciblés recompilés (nouveaux points d'entrée + régressions) | **121 PASS, 0 FAIL**, exit 0 | `validation-20261114-wave-five-targeted.log` |
| Première suite non-Live | **6 194 PASS, 4 FAIL, 2 ignorés**, exit 1 | `validation-20261114-wave-five-full-nonlive.log` |
| Reproduction diagnostique des échecs | 3 défauts de contrat documentaire ; FM intermittent, non reproduit isolément | `validation-20261114-wave-five-failures-details.log` |
| Contrats réparés et recompilés | **35 PASS, 0 FAIL**, exit 0 | `validation-20261114-wave-five-repaired-targeted.log` |
| Coûts ciblés recompilés | **2 PASS, 0 FAIL**, exit 0 | `validation-20261114-wave-five-cost-metrics.log` |
| **Suite finale** `GivreClone.Tests` non-Live | **6 199 PASS, 2 ignorés, 0 FAIL**, total **6 201**, exit 0 | `validation-20261114-wave-five-full-nonlive-repaired.log` |
| `GivreClone.PureTests` | **362 PASS, 0 FAIL**, exit 0 | `validation-20261114-wave-five-puretests.log` |
| `CombatShadowComparison.Harness` Release | **65 assertions exécutables PASS**, exit 0 | `validation-20261114-wave-five-shadow.log` |

Les tests ciblés font partie de la suite non-Live et **ne sont pas à ajouter**
aux 6 199 PASS. Le test FM `Preparation_uses_local_skill_compatibility...`
avait échoué sur `job=64,level=9` uniquement dans la première série globale ;
il passe isolément ainsi que dans la série globale finale. Possibilité d'une
interférence intermittente entre fixtures, sans preuve suffisante pour éditer
la production FM ; surveiller de nouveau sur prochaines exécutions.

### Mesures reproductibles du vrai code modifié

- `CombatTacticalContinuationTests`, 192 vérifications candidats avec plateau
  et sorts en fixture offline, after warm-up : **legacy 24,613 ms /
  2 675 616 B** d'allocations, **index partagé 1,283 ms / 221 472 B**.
  Environ **91,7 % d'allocations évitées** dans ce cas. Les temps sont des
  échantillons locaux, non des garanties en jeu. Le test valide la parité et
  n'impose pas de seuil temporel fragile.
- `LocalRelayEpochIsolationTests`, vrai code de validation de lien local sur
  serveur TCP loopback factice : **50 000 `IsCurrentGameLink` en 13,205 ms,
  40 B** alloués sur le thread. Le trafic du vrai jeu, le débit/latence du
  nouveau reframeur `FeedLinkScoped`, `fsync` du journal durable, le coût
  temporel de navigation et le parcours A* **n'ont pas été benchmarkés**.
  Ne pas extrapoler ces valeurs au client ou aux combats Live.

### Limites prioritaires avant toute « clôture complète »

Lier **le même lien source** aux véritables événements serveur faisant foi
sur les mutations (action UID, ACK attribuable, coût PA/PM et effet cible),
sans adapter `DecoderEpoch` ou `Sent` en substituts. Fermer les transitions
crash/restart et fiscalité sans double taxe sur l'ensemble des chemins directs
et non-plafonnés. Automatiser seulement les stocks/matériaux confirmés pour
les métiers, FM et craft, puis les routes banque/pods et les reprises de
progression et donjons. Les combinaisons multi-tours et toutes les classes
ne sont **pas** certifiées ou autonomes. Aucune opération Live effectuée.

### Contrôle Git de sortie, après validation

- `git diff --check` sur les **fichiers suivis touchés par cette vague** :
  **exit 0**. Contrôle de fin de ligne des nouveaux tests, services et de ce
  journal : pas de trailing whitespace constaté.
- `git diff --check` **global** : **exit 2** uniquement pour les **quatre
  anomalies préexistantes** (inchangées) :
  `GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`,
  `TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184`.
  Les avertissements Git LF/CRLF ne sont pas des corrections effectuées.
- `git rev-parse HEAD` :
  `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`, identique à l'entrée.
  `git status --short` : **723 entrées** à ce contrôle (704 à l'entrée ;
  nouveaux sources/tests/logs/report conservés). Aucun reset, commit, push,
  nettoyage ou suppression du dirty existant. Aucun processus `dotnet`,
  `testhost` ou `VBCSCompiler` encore actif. Les preuves sont conservées dans
  `validation-20261114-wave-five-*.log`.

## Checkpoint supplémentaire — vague 6, passage source Socket et garde HDV final (14 novembre)

### Point de départ et ownerships

Reprise du worktree **existant**, non recréé : `HEAD` initial
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`, 723 entrées
`git status --short`, aucun processus `.NET` actif. Les workers de la vague 5
étaient en sommeil. Leur reprise a été refusée par l'orchestrateur pendant
le transfert de conversation (`PRIME_TRANSFER_IN_PROGRESS`) ; **PRIME a seul
édité les fichiers de cette vague**, sans prétendre que les anciens workers
ont contribué. Aucun build, test ou client DOFUS concurrent, aucune intervention
sur MizanBot, aucun reset/clean/stash/commit/push. Les corrections v4/v5,
notamment `_comboIngress?.`, one-shot, reprise durable route et production,
journal fiscal et TCP, ont été conservées.

### Raccordements fonctionnels effectivement installés

**Socket clientless → vrai décodeur lié à la source** :

- `CaptureDecoder.cs` ajoute au `CaptureLinkIdentity` existant un jeton
  `ConnectionToken` facultatif pour préserver la compatibilité MITM quatre
  arguments. Le chemin Socket utilise au contraire le jeton **complet** de
  `SocketGameClient.SocketLinkProvenance(ClientInstance,RunEpoch,ConnectionToken,
  SyntheticPid)`, produit sur la **vraie connexion après dial réussi**.
- `LaunchService.Packets.cs` contient le nouvel ingress applicatif réel
  `FeedSocketObservedFrame(observation, IsCurrentSocketLink)` : vérifications
  d'identité, séquence, taille, émission dans le `CaptureDecoder.FeedLinkScoped`
  **de cette même application**, revalidation avant et après, fermeture exacte
  via `RevokeSocketObservedLink`. Il ne fabrique pas de métadonnées à partir du
  PID. `SocketProbe.cs` raccorde son **vrai callback** `FeedObserved` à ce port
  et révoque exactement le dernier lien à EOF, Stop ou remplacement du lease.
  Les callbacks externes facultatifs et le `FeedFrame` legacy restent en place.
- Ces observations sont un second flux séparé : `LinkedPacket` et ses
  invalidations ne sont **pas** joints au `PacketDecoded`/reducers historiques
  ni promus à `ComboIngress` certifié. `SourceInstanceId`, jeton Socket, ordre
  de fragments, `DecoderEpoch`, PID et direction inbound démontrent au mieux
  provenance/ordre LOCAL de la socket ; **ils ne démontrent pas un ACK corrélé,
  un cast serveur ni un effet**. `ServerConfirmed=false`, `CanAuthorizeCast=false`
  et l'interdiction de réémission restent maintenus. Les anciens relayeurs
  sans identifiant de connexion restent sans preuve forte.

**Vente et repricing HDV au véritable point d'émission** :

- `LaunchService.Hdv.Session.cs` capture pour un ordre la vraie identité
  **serveur + characterId + epoch LOCAL du décodeur**, ainsi que le mode HDV
  vendeur et le statut non suspendu du lien. `HdvSellAsync` et
  `HdvRepriceAsync` revérifient cette identité et les valeurs UID/GID/stock,
  quantité, tradabilité ou listing, prix/plancher au dernier delegate d'envoi
  après les attentes de sérialisation. Réponses retardées ou d'une autre epoch
  ne peuvent confirmer l'opération. L'epoch est un *fence* anti-stale, **pas
  l'identité TCP ni un ACK**.
- Le garde `_hdvRepriceNeedsReview` existait mais n'était pas activé depuis
  les résultats incertains du **vrai** `HdvRepriceAsync`. À présent tentative
  d'émission avant appel du transport, annulation, exception, réponse serveur
  non attribuable ou session remplacée activent ce garde et bloquent toute
  nouvelle tentative, y compris sous `MaxTaxSpend=0` (valeur historique :
  pas de plafond monétaire). Une réponse `ExchangeErrorEvent` générique ne
  devient pas un reçu d'ordre UID. Un succès ne peut être publié sans
  confirmation exacte du retrait + nouvelle annonce.
- Un défaut de reprise a été détecté en relisant `ForgetHdv` : son nettoyage
  de PID supprimait ce blocage avant tout rapprochement du même personnage.
  `HdvRepriceReviewPending` consulte désormais aussi
  `_hdvRepriceProfileNeedsReview[(serverId,characterId)]`, basé sur la
  **photo d'identité prise AVANT émission** et conservé après `ForgetHdv`/PID
  réutilisé au cours du même processus. Un autre personnage ou un autre
  serveur ne récupère pas ce verrou ; le vrai personnage sur un autre PID
  reste bloqué jusqu'à `HdvAcknowledgeRepriceReview` opérateur. Le panneau
  HDV classique, qui lisait déjà ce statut, bénéficie de ce raccordement.
- **Limite importante** : cette seconde quarantaine mémoire n'est PAS un
  journal durable au redémarrage de l'application. Le journal fiscal commun
  sous profil, déjà branché pour manuel/Bulk/Lua, ne couvre toujours pas
  atomiquement tous les appels directs non-brokés. Ne pas conclure à une
  garantie d'idempotence interprocessus, à une taxe acquittée ou à un ACK.

### Preuves et tests sur vrais points d'entrée, non Live

| Campagne PRIME séquentielle | Résultat réellement observé | Journal `docs/codex/` |
|---|---|---|
| Premiers ciblés compilés (HDV, Socket source, receipt/provenance existants) | **65 PASS, 1 FAIL**, test nouveau incorrect sur fragment TCP incomplet | `validation-20261114-wave-six-targeted.log` |
| Reproduction de l'échec de test | Assertion erronée : trois octets varint incomplets sont conservables sans événement | `validation-20261114-wave-six-targeted-diagnostic.log` |
| Ciblés corrigés, recompilés | **42 PASS, 0 FAIL** | `validation-20261114-wave-six-targeted-repaired.log` |
| Ciblés provenance, règles HDV, métrique compilés | **52 PASS, 0 FAIL** | `validation-20261114-wave-six-final-targeted.log` |
| Ciblés avec nouvelle quarantaine profil | **18 PASS, 0 FAIL** | `validation-20261114-wave-six-scoped-reprice-targeted.log` |
| Première suite complète non-Live | **6 204 PASS, 4 FAIL, 2 ignorés**, total 6 210 ; les quatre échecs concernaient les anciennes fixtures `HdvRepricePacingTests` | `validation-20261114-wave-six-full-nonlive.log` |
| Réparation du harnais de cadence puis recompilation | **20 PASS, 0 FAIL** | `validation-20261114-wave-six-pacing-repaired-targeted.log` |
| **Suite complète finale** `GivreClone.Tests` non-Live, `--no-build` | **6 208 PASS, 0 FAIL, 2 ignorés**, total **6 210**, exit **0** | `validation-20261114-wave-six-full-nonlive-repaired.log` |
| `GivreClone.PureTests`, seul .NET actif | **362 PASS, 0 FAIL**, exit **0** | `validation-20261114-wave-six-puretests.log` |
| `CombatShadowComparison.Harness` Release | **65 assertions exécutables PASS**, exit **0** | `validation-20261114-wave-six-shadow.log` |

Ne pas additionner ciblés et complets : les sélections se recouvrent.
`HdvRepricePacingTests` créait un `LaunchService` par reflection sans identité
personnage, sans `_decoder` ni epoch. Le **harnais seulement** a été complété
par une epoch de décodeur explicitement **synthétique**, par la bonne
identité serveur/personnage et par des `DecodedPacket` de test marqués avec
cette epoch. Ces fixtures simulent les retours pour mesurer la cadence ;
elles **ne sont pas une preuve serveur Live**. Aucun garde métier n'a été
affaibli pour corriger ces tests. Les tests ajoutés `SocketSourceLinkedIngressTests`
et `HdvRepriceDirectQuarantineTests` utilisent l'entrée réelle du service
et couvrent source exacte, one-shot, replay, arrêt/changement de PID,
appartenance serveur/personnage, plafond zéro et réconciliation explicite.

### Coût observé sur le véritable chemin modifié

`SocketSourceLinkedIngressTests.Real_socket_linked_ingress_measures_decode_cost...`
fait exécuter `LaunchService.FeedSocketObservedFrame` → `CaptureDecoder.FeedLinkScoped`
sur 256 trames protobuf encadrées de test après préchauffage, avec un vrai
validator de source de fixture : **9,872 ms pour 256 trames, 1 154 872 octets
alloués sur le thread** dans cette mesure ponctuelle, sans seuil de temps
dans le test. Ces octets et cette durée incluent l'assemblage des enveloppes
et assertions de test. Pas de réseau réel, pas de client DOFUS, pas de
latence ou débit certifié en jeu. Les coûts `fsync` de la reprise et du
journal fiscal, la navigation et A* n'ont pas été remesurés dans cette vague.

### Risques restant ouverts

Transport lié à la **vraie** socket mais pas encore joint à une succession
causale d'ACK/effets admissibles dans le reducer et le conseiller tactique.
`ComboIngress`, Roublard/Éliotrope/Xélor/Féca/Écaflip/Sacrieur et autres classes
restent consultatifs pour les nouveaux combos faute de preuve transport et
action corrélée. L'autorisation `Sent` ≠ ACK reste interdite. Il manque encore
le journal durable **sans double réservation** pour les émissions HDV directes
hors broker, la réconciliation phase par phase production/métiers/FM/banque,
les transitions complètes route/ressources et progression/donjons basées sur
observations attribuables. Les anciennes validations Live ne certifient pas
la version de ce worktree. GivreClone n'est pas déclaré terminé.

### Contrôles Git de sortie vague 6

`git diff --check` sur les cinq fichiers **suivis** modifiés dans cette
vague : **exit 0**. Contrôle de trailing whitespace des deux nouveaux
tests, du nouveau partial `LaunchService.Hdv.Session.cs` et du présent
rapport : **0 anomalie**. `git diff --check` **global** : **exit 2**,
exactement les quatre anomalies déjà préexistantes et non touchées :
`GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`,
`TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184`.

`git rev-parse HEAD` : `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`
(inchangé). `git status --short` : **738 entrées** au contrôle final
(723 avant la vague ; les fichiers de tests/journaux et modifications
préexistantes sont conservés). Journaux Git :
`validation-20261114-wave-six-git-diff-check.log` et
`validation-20261114-wave-six-git-status.log`. Aucun processus
`dotnet`, `testhost` ou `VBCSCompiler` résiduel ; aucune action destructive
ni réseau jeu effectuée par PRIME.

## Checkpoint supplémentaire — vague 7, journal HDV direct durable et reprises fail-closed (14 novembre 2026)

### Point de départ et ownership

Reprise **sans reconstruction** au HEAD `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
738 entrées `git status --short`, sans processus .NET actif. Tous les
changements non committés préexistants ont été préservés. PRIME a seul
développé l'entrée commune HDV, le journal et ses tests, ainsi que réparé
deux harnais temporels. Les trois workers réactivés ont livré des scopes
distincts, puis gelé sans build, jeu, commit, reset ni clean :

- **worker-10 production** : `Production/ItemCraftSessionBoundActions.cs`
  et nouveau `ItemCraftProductionDurabilityV7Tests.cs`. Un ancien wrapper
  ayant observé une perte d'identité ne redevient plus valide si son
  prédicat de session rebascule sur `true` (ABA/reconnexion) :
  latch `_revoked` sticky/interlocked, revérification autour des lectures
  et des mutations async, cancellation post-await. Sept tests P/N. Le
  `ItemCraftProductionAttemptJournal.cs` est resté inchangé.
- **worker-9 activité** : `ActivityRecovery.Durable.cs` et nouveau
  `ActivityRecoveryCrashBoundaryV7Tests.cs` (13 tests). `ReadMarker`
  n'assimile plus un `File.Exists=false` dû à une erreur de chemin/ACL
  à une absence certaine de marqueur ; ouverture directe, UTF-8 strict,
  Busy/Corrupt/Unavailable préservés, sans effacement ni auto-unlock.
  L'admission durable `CreateNew` était déjà fail-closed : le défaut
  prouvé concernait l'affichage et la lecture de reprise, **pas** une
  double émission observée.
- **worker-11 transport causal, lecture seule** : l'injection
  `SocketGameClient.Inject=true` prouve l'admission dans une file,
  pas l'écriture TCP ni un ACK. Les writes du drain `_outbound`
  (`SocketGameClient.cs` vers 963–995) n'alimentent pas actuellement
  `PublishObserved`, contrairement à certaines requêtes internes et
  aux RX. `SequenceEndEvent` et l'ACK client sortant n'attribuent pas
  un sort individuel. `GameActionFightEvent` atteste certains faits
  serveur sans UID de requête/cellule/cible corrélables. Aucune
  promotion `LinkedPacket` → ACK/cast/effet n'a été autorisée.

### HDV : admission persistante au véritable point d'envoi

PRIME a ajouté `Services/HdvDirectMutationJournal.cs`, un journal
**non fiscal** spécifique aux mutations directes, stocké sous
`MZP19LocalClone.DataRoot/hdv/direct-mutations-v1.journal`. Il
conserve un historique append-only hash-chain, un anchor fsync,
des transitions `Started → Confirmed/Uncertain`, puis une résolution
`Reviewed` **explicitement opérateur**. L'anchor est réécrit
après flush durable du record ; corruption, troncature, échec disque,
anchor divergent et restart entre record et anchor bloquent, sans
effacer d'octets. Chaque profil `server:<id>:character:<id>`
possède un verrou de fichier OS retenu pendant l'action en vol ;
une autre instance ne peut pas reconnaître manuellement le marker
tant que l'émetteur original détient ce lock. L'arrêt du processus
libère le handle, mais pas l'intention enregistrée.

`HdvSellAsync` et `HdvRepriceAsync` passent tous deux par la
réservation **après les derniers contrôles de session/UID/GID/
stock/quantité/prix et avant l'injection transport**. Le code
n'envoie rien si le journal refuse. Une sortie non attribuable,
une annulation post-tentative ou une exception donne un marker
`Uncertain` ou laisse `Started` ; aucune nouvelle émission
du profil ne passe. `Confirmed` n'est écrit qu'après la
corrélation existante de vente (débit sac + UID annonce) ou de
reprice (retrait exact + nouvelle annonce), et vérification de
session. Les écrans reprice et disposition vente consultent aussi
la quarantaine sur disque après `ForgetHdv`, nouveau PID,
nouvelle instance, même acteur sur le même serveur.

`HdvSellManuallyReconciled` et `HdvAcknowledgeRepriceReview`
ne peuvent lever que le type d'ordre en attente du personnage
actuellement identifié, uniquement via une opération de
réconciliation explicite et durable. L'ancienne mémoire PID
reste complémentaire. Les clés locales
`HdvTaxOrderKeys.Sell/Reprice` ne sont pas des IDs serveur.
Le ledger fiscal `HdvTaxReservations` existant reste **seul**
responsable des réservations de taxes et plafonds ; ce nouveau
journal n'ajoute **aucune taxe** et ne relâche aucun reçu fiscal.
Le harnais de `HdvRepricePacingTests` possède maintenant son
propre journal temporaire, sans toucher le profil HDV réel.

Nouveau fichier `HdvDirectMutationDurabilityV7Tests.cs` : neuf
tests couvrant restart/crash avant send, réemploi exact, différentes
commandes du profil, succès confirmé vs incertain, opérateur
pour le bon type, lock cross-process en vol, PID réutilisé,
autre personnage/serveur, corruption préservée, anchor absent,
admission concurrente et statut du vrai LaunchService.

**Limites explicites** : ce journal couvre désormais les vrais
points d'émission **sell/reprice**. Il ne certifie pas encore
les mutations directes achat/retrait, ni l'attribution causale
d'une dépense fiscale. Une opération tax-brokérisée peut conserver
sa réservation conservatrice même si le second garde d'intention
refuse avant envoi ; aucune double charge fiscale n'est faite,
mais le rapprochement peut rester nécessaire. Les confirmations
de cette vague utilisent les corrélateurs existants, pas de
nouvelles preuves TCP/ACK de DOFUS 3.7.13.17.

### Chaîne de validation PRIME uniquement

Tous les appels ont été séquentiels et **hors jeu**, jamais deux
`dotnet test` en parallèle :

| Exécution | Résultat | Journal |
|---|---|---|
| Première compilation/ciblés V7 HDV + production + activité + anciens | **77 PASS, 0 FAIL** | `validation-20261114-wave-seven-targeted.log` |
| Première suite globale non-Live, binaire compilé | **6 236 PASS, 1 FAIL, 2 skipped** sur 6 239, échec `StrictTeamWaitTests.Server_end_of_placement...` | `validation-20261114-wave-seven-full-nonlive.log` |
| Diagnostic strict team isolé sans rebuild | **1 PASS, 0 FAIL** en 312 ms | `validation-20261114-wave-seven-strict-team-diagnostic.log` |
| Deuxième suite globale sans changement production | **6 234 PASS, 3 FAIL, 2 skipped**, tests `MZP1OrderedPublicationTests` avec callbacks volontairement bloqués | `validation-20261114-wave-seven-full-nonlive-recheck.log` |
| Diagnostic MZP1 isolé | **5 PASS, 0 FAIL** | `validation-20261114-wave-seven-mzp1-diagnostic.log` |
| Réparation des harnais, compilation ciblée | **58 PASS, 0 FAIL** | `validation-20261114-wave-seven-flaky-harness-repair.log` |
| **Suite globale finale non-Live** | **6 237 PASS, 0 FAIL, 2 skipped** sur **6 239**, 1 min 40 s, exit 0 | `validation-20261114-wave-seven-full-nonlive-fixture-repaired.log` |
| PureTests | **362 PASS, 0 FAIL**, exit 0 | `validation-20261114-wave-seven-puretests.log` |
| CombatShadowComparison Release | **65 assertions exécutables PASS**, exit 0 | `validation-20261114-wave-seven-shadow.log` |

Le premier test `StrictTeamWaitTests` a été conservé
avec son exigence `false`, mais son délai de harnais est
passé de 5 à 15 s pour supporter la contention du runner.
Les trois tests de `MZP1OrderedPublicationTests` qui
bloquent volontairement un subscriber utilisent désormais
un thread de test dédié (`LongRunning`) au lieu d'occuper
un worker ThreadPool. **Aucune sémantique de transport,
FIFO, revoke ou autorisation n'a été relâchée.** Les sorties
initiales rouges sont conservées, non masquées.

### Restant ouvert pour la vague suivante

La publication des writes sortants Socket avec leur vraie
provenance après `WriteAsync/FlushAsync` reste à réaliser
**en mode consultatif** avant toute corrélation causale
vers effets, PA/PM ou ACK. Les appels HDV buy/remove et
leurs mutations indécidables ne disposent pas encore
du même journal universel. Réconciliation production,
banque, craft, récolte, navigation et combos multiclasse
demeurent non certifiées automatiquement. La validation
Live DOFUS 3.7.13.17 n'a pas eu lieu dans cette vague.
**GivreClone reste un projet en cours, non fini.**

### Contrôles Git et processus de fin vague 7

`git diff --check` limité aux fichiers suivis modifiés par la vague 7 :
**exit 0**. Le scan des fins de ligne sur tous les fichiers nouveaux,
suivis et rapport touchés par cette vague ne détecte **aucun trailing
whitespace**. Le contrôle global `git diff --check` reste en **exit 2**
pour exactement les quatre anomalies **préexistantes et non modifiées** :
`GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`,
`TurboFightPolicy.cs:391` et `GivreClone.App.csproj:184`.
Les avertissements Git de future conversion LF/CRLF sont distincts
de ces quatre erreurs de whitespace.

HEAD final inchangé :
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`.
`git status --short` : **752 entrées** (738 au départ), modifications
précédentes préservées. `DOTNET_ACTIVE=0` : aucun `dotnet`,
`testhost` ou `VBCSCompiler` actif à la sortie. Pas de lancement
de client DOFUS ou processus étranger, ni commit/push/reset/clean.

## Checkpoint supplémentaire — vague 8, parcours intégrés et provenance post-écriture (14 novembre 2026)

### Périmètre utilisateur et reprise

La demande de vague 8 ne se limitait plus à des audits : raccordements
réels transport→diagnostic, HDV→journal durable, activité→Lua route/
gather, production→VM/stock/journal, combat multiclasse→conseiller
existant. L'objectif final reste un bot complet, performant, sécurisé et
personnalisable. Départ réellement contrôlé : HEAD
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
**752 entrées Git non commitées**, vague 7 documentée,
zéro processus `dotnet`/`testhost`/`VBCSCompiler`.
Ni reset/stash/clean, ni commit/push, ni client DOFUS/jeu.

Workers 8, 9, 10 et 12 réactivés, worker 13 lancé sur HDV,
scopes disjoints et tests/builds interdits aux workers. Le worker 14
MITM/Engine **n'a pas démarré** ; ne lui attribuer aucun patch.
PRIME a intégré les raccordements source MITM **local** et les
ViewModels/écrans classiques ; cela ne remplace pas un développement
non réalisé de `TcpProxyServer/SessionBinder/BotOrchestrator`.
Les workers actifs ont gelé leurs fichiers avant les compilations PRIME.

### Fonctionnalité 1 — vraie écriture Socket observée après Flush

`Transport/Socket/SocketGameClient.cs` a maintenant un canal
`SocketTransportWriteObservation` (source exacte
`ClientInstance,RunEpoch,ConnectionToken,SyntheticPid`, ordinal local,
copie immuable des octets, instant local). `Inject=true` ne signifie
qu'une **mise en file** ; seul le drain réel de `_outbound` crée
l'observation **après WriteAsync ET FlushAsync réussis**, sous
validation de la génération/connexion. EOF, Stop, source remplacée,
échec de Flush ou callback tardif interdisent toute observation de
write acceptée hors de sa session. La file FIFO existante reste en
place. `SocketProbe.RunAsync` a un callback additif facultatif,
qui raccorde ce reçu **local** à
`LaunchService.FeedSocketWrittenObservation` après vérification du
lease puis de la source exacte, sans toucher au `FeedFrame` legacy.

`SocketSourceAuthorityLedger.cs` est un état borné en mémoire,
verrouillé par source exacte et PID synthétique, enregistrant les
fragments entrants et les écritures moteur post-Flush avec compteur
et volumes, et révoquant exclusivement la connexion fermée.
`LaunchService.Packets.cs` garde strictement
`PacketDecoded`, `LinkedPacket`, `ComboIngress` et les reducers
séparés. `ServerAckAttributed=false` et
`GameplayAuthorizationAvailable=false` restent constants.
Cette observation **ne prouve pas** que le serveur a reçu,
accepté ou exécuté l'action.

### Fonctionnalité 2 — MITM local et diagnostic classique

Le chemin préexistant `LocalHandshakeRelay.OnGameData` produit
réellement `RelayInstanceId,OwnerPid,RunEpoch,TunnelId,sequence` ;
PRIME a raccordé ce **vrai callback local** à
`LaunchService.ObserveLocalMitmRelayFrame` et à la même logique
read-only, après `FeedLinkScoped` et revérification de la source.
Sur `OnGameLinkClosed`, la révocation ne retire pas la source
qui l'a remplacée. Le mode MITM legacy PID-only, les instances
externes et `HandshakeRelay` ne sont **pas** magiquement
authentifiés. L'extension `BotsViewModel.SocketAuthority.cs`
ajoute une ligne dense « Source TCP », un bouton de rafraîchissement
**lecture seule** et des champs `socketAuthority`/
`localMitmAuthority` au rapport de santé. Le texte indique
explicitement que la source locale et le write post-Flush ne sont
pas un ACK individuel et n'autorisent pas de cast.

### Fonctionnalité 3 — HDV vente, reprice, achat ET retrait

`HdvDirectMutationJournal` V7 conserve son unique journal
non fiscal fsynced + anchor/hash-chain, verrou OS de profil.
La vague 8 ajoute les types `buy:` et `remove:` canoniques,
avec UID/GID/quantité/prix, stock vendeur observé ou
`stock-unknown` pour l'offre acheteur, époque **locale**,
et quarantaine typée pour l'affichage. Le verrou global au
vrai point d'émission s'applique **à tous les types** :
une mutation incertaine interdit une autre transaction du
même serveur/personnage, même via un autre PID/canal.
`HdvBuyAsync` et `HdvRemoveAsync` écrivent `Started`
avant l'injecteur, avec revalidation des identités,
UID/GID, offre, prix/fonds ou annonce vendeur ;
après interruption, timeout, rejet non attribuable,
changement de session ou absence de reçu exact,
ils conservent `Started/Uncertain`, sans réémettre.

`ExchangeBidHouseBuyResultEvent` donne un UID et un booléen,
pas la quantité/le prix. Hors débit **exactement corrélé**
avec le journal Smith existant, le succès d'achat n'est pas
promu au journal : l'achat s'arrête, retourne false et exige
un rapprochement. Le journal fiscal et `MaxTaxSpend=0`
gardent leur sémantique ; le journal direct ne réserve
pas une seconde taxe. `ClearSettledHdvBuy` ne retire
plus les quarantaines directes lorsqu'un débit Smith est
soldé : seuls les budgets Smith peuvent alors se solder.
Les API `HdvBuyReviewPending`/
`HdvAcknowledgeBuyReview`, les vues vendeurs et les
rapprochements retirent seulement le **type** en attente
du profil réellement identifié. Aucune levée automatique.

Dans `HdvViewModel.Trade.cs`, l'achat automatique, le
sniper et le choix manuel s'arrêtent avant une nouvelle
mutation indécidable ; les boutons STOP restent accessibles.
Un panneau classique affiche la quarantaine persistante
et une confirmation humaine explicite. Tous les dialogs
Sell/Reprice/Remove/Buy revalident PID **et**
serveur/personnage au retour du clic (pas de reprise
après PID reuse). Le budget acheteur et l'estimation des
lots saturent au lieu d'overflow/under-evaluation.
`BotReadiness` et `BotsViewModel.ControlHealth`
exposent aussi l'incertitude `HdvBuyReview`.

### Fonctionnalité 4 — trajet, récolte, journal de production

Le véritable `GivreLuaHost.RunRoute` détecte les
options `ACTIVITY_RECOVERY`, `activityRecovery`,
`ACTIVITY_RECOVERY_RECONCILE` et
`activityRecoveryReconcile` **avant** tout chemin
legacy de rejoin/off-graph/HavenBag. La route/récolte
opt-in utilise le store durable existant, position
et ressource observées, et bloque les pas non attestés.
Un objectif gather épuisé sans suite évite la boucle
muette par un `StuckStop` borné. L'opt-in demeure
enregistrable dans les scripts Lua de profil, pas un
auto-redémarrage global. Quatorze tests MoonSharp du
**vrai RunRoute** avec interception seulement au dernier
send hors jeu couvrent l'admission avant émission,
crash/restart, source remplacée, marqueur corrompu,
reconcile consultatif, ressources absentes et arrêt.

`ItemCraftPlannerViewModel` passe par le journal
`ItemCraftProductionAttemptJournal` au départ,
réutilise l'adaptateur `ItemCraftSessionBoundActions`
révoqué irréversiblement après perte d'identité,
et renseigne dans le panneau classique les états
PRÊT/TERMINÉ/BLOQUÉ, sans auto-clear. Dans le
`ItemCraftProductionRun` réel, les reçus banque,
achat, craft, listing sont comparés aux variations
GID/quantité de l'inventaire observé et à l'identité
du run ; un callback local, une différence de stock
non attribuable ou un résultat partiel ne vaut
pas certificat de succès. Huit tests de parcours
VM/runner/journal ont été ajoutés.

### Fonctionnalité 5 — tactique multiclasse réellement appelée

`VerifiedTacticalExecutor.PrepareObservedDecision`, déjà
appelé par `LaunchService.Combat.TacticalAdvisor.cs`,
produit maintenant des conseils bornés à deux étapes
pour Éliotrope (portail observé puis impact par Rayon)
et Xélor (échange observé + Téléfrag réel puis Gelure),
avec versions de sorts documentées 3.7.13.17 et
réévaluation PA/PM, grades connus, tour, génération,
cible, position, portée, LoS, obstacle, marque/état
exact et expiration. Roublard reste une seule
étape faute de reçu de détonation ; Féca, Écaflip
et Sacrieur n'inventent aucun état ou coefficient
si le catalogue/grade n'a pas de jointure exacte.
`CanDispatch=false` même pour un conseil simulé
positivement : aucun ACK request→cast→effet exploitable
n'a été prouvé dans la version courante, donc
aucun cast automatique nouveau n'est armé.
Le nouvel ensemble `CombatMulticlassIntegratedV8Tests`
vérifie les P/N et un coût alloué borné hors jeu.

### Chronologie de vérification PRIME — tests hors Live

Les tests ont toujours été compilés/exécutés exclusivement
par PRIME, un processus partagé à la fois, en conservant
les journaux des passages rouges :

| Campagne | Résultat | Fichier |
|---|---|---|
| Première compilation ciblée V8 | **3 erreurs CS** (namespace VM x2 ; paramètre `out` capturé en LINQ) ; corrigées | `validation-20261114-wave-eight-targeted.log` |
| Ciblés recompilés | **105 PASS, 3 FAIL**, 108 total ; deux fixtures HDV et fixture Stop Lua | `validation-20261114-wave-eight-targeted-recompiled.log` |
| Trois échecs reproduits individuellement avec traces | **3 FAIL** : verrou OS actif correct (=Unavailable), bannière REPRICE typée, LaunchService fixture Stop incomplète | `validation-20261114-wave-eight-targeted-diagnostic.log` |
| Ciblés corrigés et recompilés | **108 PASS, 0 FAIL** | `validation-20261114-wave-eight-targeted-repaired.log` |
| Première suite globale | **6 303 PASS, 4 FAIL, 2 skipped**, total 6 309 | `validation-20261114-wave-eight-full-nonlive.log` |
| Quatre échecs isolés | **4 FAIL** : deux contrats Socket cherchant l'ancien code inline, inventaire Lua F07 non régénéré, ancienne fixture Smith de clear automatique devenue non sûre | `validation-20261114-wave-eight-full-failures-diagnostic.log` |
| Réparation ciblée recompilée | **76 PASS, 0 FAIL** : contrats source du post-Flush, nouveau snapshot de l'inventaire Lua via son générateur existant et distinction budget Smith/quarantaine d'achat | `validation-20261114-wave-eight-global-failures-repaired.log` |

Les comptes ciblés et globaux se recouvrent ; ne pas les
additionner. La génération `python docs/portal/lua-f07-inventory.py`
reconstruit `docs/portal/lua-f07-inventory.json` pour les
**1 018 liaisons statiquement trouvées**, **0 documentée
absente** ; cette preuve de présence d'API n'est pas
un reçu de jeu ni une preuve de bon fonctionnement.

### Résultat global final — vague 8 VERTE hors Live

| Suite PRIME séquentielle après réparation | Résultat exact | Journal |
|---|---|---|
| **GivreClone.Tests non-Live**, binaire recompilé, `--no-build -m:1` | **6 307 réussis, 0 échec, 2 ignorés, total 6 309**, 1 min 39 s, exit 0 | `validation-20261114-wave-eight-full-nonlive-repaired.log` |
| **GivreClone.PureTests** `--no-restore -m:1` | **363 réussis, 0 échec**, exit 0 | `validation-20261114-wave-eight-puretests.log` |
| **CombatShadowComparison.Harness Release** | **65 assertions exécutables PASS**, exit 0, hors jeu | `validation-20261114-wave-eight-shadow.log` |
| **Mesure ciblée Source Socket**, `--no-build`, logger détaillé | **1 PASS**, exit 0 ; 256 observations post-Flush synthétiques sur le véritable ingress LaunchService | `validation-20261114-wave-eight-source-cost.log` |

La mesure source après préchauffage est **0,522 ms pour 256**
observations locales, **282 664 octets** alloués sur le thread appelant
(environ 1 104 octets/observation dans **ce test**) via
`SocketAuthorityIntegratedV8Tests.Local_projection_cost_is_measured_on_actual_launch_ingress_without_latency_claims`.
Le test inclut la construction d'enveloppes de réflexion, les appels
LaunchService et les assertions ; il **exclut** les écritures TCP
réelles, le réseau, le serveur DOFUS et toute mesure de latence
ou de débit Live. Aucun seuil fragile n'a été imposé, et les
benchmarks A* précédents ne sont pas extrapolés à ce chemin.
Les tests d'allocations du conseiller combat sont déterministes,
sans allégation de coût Live.

### Ce que la vague 8 NE certifie PAS

- Le callback d'écriture post-Flush prouve une écriture locale,
  pas une réception/acceptation serveur. Le FIFO, le token Socket,
  l'époque de décodeur, l'ordinal, l'inbound et le PID ne prouvent
  aucun lien causal d'un cast individuel avec un effet.
- Le relay MITM **local** est lié à sa source authentique, mais
  ni le chemin legacy PID-only ni `TcpProxyServer`/Engine et
  `HandshakeRelay` externes ne sont universellement raccordés
  aux consommateurs. La tâche worker MITM/Engine n'a pas démarré.
- Les réceptions `BuyResultEvent(uid,bought)` sont insuffisantes
  sans quantité/prix/débit exact, donc certains achats qui
  pourraient avoir été acceptés restent bloqués volontairement
  pour rapprochement humain ; aucune taxe n'est créditée ou
  relâchée automatiquement sur cette observation seule.
- Des mutations hors HDV, notamment banque/pods/FM et certaines
  recettes/composants multi-phases, manquent encore de reçus
  attribuables. Le conseil Roublard/Féca/Écaflip/Sacrieur n'est
  pas certifié autonome et `CanDispatch` des nouveaux combos
  reste faux. Les scripts de route/récolte hors mode opt-in
  ne bénéficient pas rétroactivement des preuves manquantes.
- Les vues, profils, mappings, interactions et combats réels
  de DOFUS **3.7.13.17** n'ont pas été validés Live pendant cette
  vague. Les validations hors jeu ne prouvent pas que le bot
  soit fonctionnel de bout en bout sur le serveur officiel.

**GivreClone reste inachevé**. La prochaine frontière
substantielle est de capter, vérifier et attribuer une corrélation
réelle demande→réponse serveur pour les actes autorisés ; puis
étendre les journaux et reçus aux parcours hors HDV tout en
maintenant les étapes incertaines interdites à la réémission.

### Contrôle de sortie vague 8 — fichiers et processus

`git diff --check` sur les fichiers **suivis** de cette vague :
**exit 0** ; scan des fichiers réellement modifiés par la vague :
aucune nouvelle erreur de whitespace. Le scan de fichier entier
signale un ancien espace terminal à
`GivreClone.App/Views/BotsView.axaml:3293` ; cette ligne
est inchangée dans le diff V8, le contrôle scoped reste vert.
`git diff --check` **global** : **exit 2**, exactement les
quatre anomalies déjà constatées au checkpoint vague 6/7,
et non modifiées pendant la vague 8 :

- `GivreLuaHost.SnowMap.cs:24` — trailing whitespace ;
- `GivreLuaHost.Soul.cs:134` — trailing whitespace ;
- `TurboFightPolicy.cs:391` — new blank line at EOF ;
- `GivreClone.App.csproj:184` — trailing whitespace.

Les avertissements Git de normalisation LF→CRLF ne constituent
pas de nouvelles erreurs de `diff --check`.
HEAD final **inchangé** :
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`.
`git status --short` après écriture des journaux :
**781 entrées non commitées** (752 à l'entrée) ;
toutes les modifications conservées. Aucun
`dotnet`, `testhost` ou `VBCSCompiler` actif
(`DOTNET_ACTIVE=0`). Journaux de sortie :
`validation-20261114-wave-eight-scoped-diff-check.log`,
`validation-20261114-wave-eight-global-diff-check.log`
et `validation-20261114-wave-eight-git-status.log`.
Ni client DOFUS, ni MizanBot ni processus étrangers
manipulés ; aucun commit, push, stash, reset ou clean.

## Vague 9 — parcours applicatifs et gardes de transport/activité, 14 novembre 2026

### Point d'entrée et discipline de travail

Checkpoint V8 **réellement revérifié AVANT modifications** :
`git rev-parse HEAD=b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
**781 entrées** `git status --short`, rapport V8 présent,
aucun `dotnet`, `testhost`, `VBCSCompiler`. Les 6 workers
ont été attribués à des domaines exclusifs : worker-12 Socket,
worker-15 Engine/MITM (dont un fichier dans le `GivreProxy`
frère non versionné par Git de ce worktree), worker-13 HDV,
worker-16 activité Lua, worker-10 production guidée, worker-17
conseiller combat. Tous ont gelé leur périmètre avant les builds ;
**PRIME seul** compile et teste. Les fichiers sales antérieurs sont
conservés. Aucun client DOFUS, processus étranger ni MizanBot
ne doit être lancé ou modifié pendant la validation.

### Transport : vraie admission en file, tentative, post-Flush et EOF

`SocketGameClient.TryInject` a été raccordé à sa **vraie**
file bornée `_outbound` sous le verrou source ; un Stop,
le retrait du lease ou le remplacement de connexion ne peuvent
plus s'insérer entre la validation de session et
`TryWrite` pour produire un `Inject=true` sur la source
révoquée. Une observation d'**admission en file** possède
un ordinal local distinct de l'ordinal de tentative
`WriteAsync` et du reçu d'écriture **après**
`WriteAsync` et `FlushAsync`. La trame est copiée et
reste attachée à `ClientInstance,RunEpoch,ConnectionToken,Pid`.
Les compteurs de diagnostic sont bornés : queued, attempted,
post-flush, discarded-before-write et ambiguous-write-failure.
Les échecs d'écriture partielle restent **ambigus** et ne
déclenchent jamais de réémission automatique. `SocketProbe`
revalide le propriétaire, la génération et le lease avant
de transmettre un reçu d'écriture. FIFO et API V8 existants
préservés. `LaunchService.FeedSocketObservedFrame` effectue
une validation finale de la source également **après**
`CaptureDecoder.FeedLinkScoped`, révoque exactement l'ancien
lien et retourne false si le producteur a changé pendant les
callbacks. Aucune publication de `PacketDecoded` legacy,
aucune conversion de write local en ACK.

Le worker MITM a relié les identités réelles
`TcpProxyServer` → `SessionBinder` →
`BotOrchestrator` → `BotHostService` pour la voie
headless lorsque la source possède effectivement
`TransportLinkProvenance`. Un owner révoqué, un callback
de factory tardif, EOF et une réutilisation de PID
ne peuvent plus conserver le lien courant. Le port
`BotHostService.TransportLinkClosed`, les observations
`TransportFrameObserved` et les vérifications
`IsCurrentTransportLink`/ `TryGetCurrentTransportLink`
peuvent être consommés par PRIME. **Ils ne sont PAS**
forcés dans le flux de capture `LocalHandshakeRelay`
indépendant par égalité de PID ou de charge protobuf.
Le contrat de permission `MZP1OwnedClientPermit`
reste actuellement calibré sur 3.7.10.10, et le
`StartBound` headless ne dispose pas d'un upstream
autorisé installé pour 3.7.13.17. Ce point reste
**bloqué/fail-closed**, non présenté comme Live.

### Économie : tentative durable et tickets d'identité des vues classiques

Le journal `HdvDirectMutationJournal` conserve le
format V7 et ajoute `Attempted` :
`Started → Attempted → Confirmed|Uncertain → Reviewed`
uniquement selon le même profil serveur/personnage et
le même lease journal fsynced/anchor. Les **quatre**
frontières bot de `HdvSellAsync/HdvBuyAsync/
HdvRepriceAsync/HdvRemoveAsync` écrivent désormais
`TryMarkAttempt` **AVANT** la vraie injection relay ;
échec disque, collision de profil, source/epoch
remplacée ou incertitude après tentative refusent
une émission ou une répétition. UID, GID disponible,
stock observé, quantité, prix et génération sont
revalidés aux frontières vérifiables, avec
`MaxTaxSpend=0` inchangé (« plafond monétaire
désactivé »). Les achats basés seulement sur
`BuyResultEvent(uid,bought)` ne prouvent toujours
ni quantité ni prix et conservent la quarantaine.
Les requêtes de changement de mode et les recherches
buyer sont maintenant liées au profil/epoch, et
le repricing Lua en masse cesse après le premier
échec plutôt que continuer avec une ambiguïté.
L'ancien helper de clear HDV sur budget Smith soldé,
non utilisé, a été supprimé. Le journal fiscal
reste séparé, et aucun reçu ou taxe n'est fabriqué.

Côté vraie UI `HdvViewModel`, les boutons manuels
sell/reprice/undercut/remove et les batches
buy/sniper/choix d'offre capturent un ticket
`PID+ServerId+CharacterId+_mutationEpoch`
au **premier clic** et le revalident après chaque
`await`, avant la file Gated et au retour de tout
dialogue différé. Double-clic vente : même objet,
GID/lot/prix et même identité doivent encore
être sélectionnés ; sinon le bouton **réarme**
au lieu d'émettre. Le retrait capture et revalide
le tuple exact UID/GID/quantité/prix de l'annonce
`Hdv.MyListings`. Les notifications différées
HdvUpdated/BankUpdated/HdvSold/OfflineSold ne créditent
plus la vue d'un personnage qui a remplacé l'ancien
PID. Le panneau classique `Journal des opérations HDV`
commande explicitement une lecture **sans émission**
des quatre statuts durables et affiche les types
bloqués, la réconciliation locale déclarée par l'opérateur
et la mise en garde contre toute réémission d'ordre
incertain. Le bouton de lecture **n'autorise**
aucune mutation et ne fait pas office d'ACK ;
les commandes de rapprochement manuel historiques
conservent leurs gardes type/profil. Cette projection
ne prétend pas journaliser les demandes effectuées
directement par un humain dans l'autre client MITM :
elles ne passent pas par les injecteurs bot.

### Parcours activité → ressources et production vérifiée

`ActivityRecovery` conserve durablement le type
ressource, l'UID de skill, l'ID du skill, la map/cell
et l'identité du personnage **avant** la tentative
de trajet/récolte. Dans `RunRoute` et le gather
opt-in, la portée, le type, l'identité exacte du
skill et la présence de ressource sont revérifiés
avant/après fsync. Ressource épuisée, obstacle,
cellule de sortie non valide, ancien persona ou
retour tardif conduisent au blocage borné, non
à une boucle de ré-envoi. Les étapes bancaires
composites sans reçu corrélé restent bloquées.

PRIME a ajouté un mode réellement raccordé au
`GivreLuaHost.RunLoopCore` : en Lua persistable
par personnage,
`ACTIVITY_RECOVERY_TABLE_ONLY=true`,
`ACTIVITY_RECOVERY=true` et
`ACTIVITY_RECOVERY_ROUTE={{map=...,activityRecovery=true,...}}`.
Le mode exige un tableau non vide avant le démarrage
de la boucle ; sans identité/carte confirmée, il
attend **sans envoyer**. Dans ce mode, les callbacks
impératifs `move()`, `bank()`, les automatisations
pods et `onFightEnd` ne sont plus exécutés
**avant** l'admission de la route durable.
Pods au plafond : arrêt/pause sûr plutôt que
dépôt multi-send non certifié. Les scripts hérités
continuent d'utiliser leur parcours historique :
aucun opt-in forcé. **Limitation cruciale :
le code top-level DoString du script est évalué
avant la sélection du mode**, donc cet opt-in
ne constitue pas une sandbox pour du code Lua
arbitraire ; il ne faut exécuter que des scripts
de confiance. L'identité et le dépôt bancaire
restent sans ACK de séquence prouvé.

`ItemCraftPlannerViewModel` lance une prélecture
réelle recette/atelier/stock/approvisionnement
**avant** admission dans le journal de production,
puis revalide la session et l'adaptateur sticky-revoked.
`ItemCraftProductionRun` compare pour chaque
craft les quantités du produit fini et la
consommation exacte de chaque ingrédient avec
les reçus existants, au plus 9 observations
de 75 ms sans réémettre de recette, et préserve
la quarantaine si la source se perd ou si
les observations divergent. Un Stop avant
publication de CTS ne peut plus laisser
le run partir après l'arrêt. Le panneau
classique expose les refus de préflight ;
aucune clôture automatique d'un achat/craft ambigu.
Les snapshots de stock sont des corroborations
locales, **pas** une preuve serveur indépendante.

### Tactique multiclasse dans les limites du transport

`VerifiedTacticalExecutor` conserve au moment
de l'admission de l'Éliotrope la topologie
des portails **réellement observés** (ID,
owner, cellule et activation) et expire le
plan en cas de suppression, désactivation
ou remplacement d'un portail préexistant.
Un nouveau portail exige à l'historique
un cast identiquement attribué spell/grade/
cible/corrélation et l'effet ordonné.
Une transition d'état Écaflip reçue après
cast exige la même source, acteur, état
connu et corrélation, puis expire si l'état
disparaît ; les tests Félix/Féca/Sacrieur
avec IDs de fixture synthétiques ne sont
**pas** des preuves de sorts installés.
Règles PA/PM, portée, LoS, tour/génération,
observations tronquées et ancien cast
inconnu restent fail-closed. Le conseiller
opt-in et les séquences multi-actions
demeurent **consultatifs** (`CanDispatch=false`) :
ni `Sent`, ni `LinkedPacket`, ni reçu post-Flush,
ni ordre du décodeur ne prouvent la causalité
d'une demande de cast avec son effet serveur.

### Première validation et corrections en cours

Les workers n'ont exécuté **aucun build/test**.
PRIME a conservé les premières tentatives
ciblées en échec :

| Passage | Résultat | Journal |
|---|---|---|
| Compilation ciblée initiale | 2 erreurs C# de variable `uid/price` redéclarée dans `HdvDirectMutationJournal.ValidOrder`; correction de noms uniquement | `validation-20261114-wave-nine-targeted.log` |
| Ciblés recompilés | 172 réussis, 2 échecs, 174 tests : fixture legacy du portail et seuil d'invalidation source dans un test V9 | `validation-20261114-wave-nine-targeted-recompiled.log` |
| Diagnostic des 2 échecs sans build | 2 échecs isolés reproduits et détaillés | `validation-20261114-wave-nine-two-failures-diagnostic.log` |
| Recompilation de ces cas | 14 réussis, 1 échec ; le second assert du même test V8 exigeait encore `Pending` quand le nouveau plan doit `Expired` | `validation-20261114-wave-nine-two-failures-repaired.log` |

L'ancien test du budget Smith a été adapté à la
suppression de son helper mort : `ReconcileSmithHistory()`
peut clore **son propre** débit sans effacer la
quarantaine directe d'achat. Le contrat F07
`docs/portal/lua-f07-inventory.json` a été
régénéré avec l'outil existant après les
changements Lua : 1 018 liaisons statiques,
0 documentée absente. Le nombre final de tests
verts, Pure et Shadow sera ajouté **après**
leur exécution séquentielle, sans recycler les
chiffres de la vague 8.

### Passage ciblé V9 final et régressions globales contrôlées

Après reprise des tests V8 de portails : le statut
`Expired` remplace `Pending` pour une évolution
de portail non attribuée ou un `Sent` non confirmé.
Le refus demeure strictement `CanDispatch=false`.
Le test d'admission de source V9 cesse de dépendre
du nombre d'appels internes au prédicat du décodeur :
il provoque effectivement une révocation réentrante
et vérifie l'absence de snapshot/ACK, sans exiger
une disposition d'implémentation non contractuelle.

| Contrôle PRIME | Verdict | Journal |
|---|---|---|
| **Ciblés V9 finaux recompilés** (transport/Engine/HDV/activité/production/combat + V8/V7 touchés) | **174 PASS, 0 FAIL, 0 skipped**, exit 0 | `validation-20261114-wave-nine-targeted-final.log` |
| Première suite non-Live globale | **6 357 PASS, 10 FAIL, 2 skipped**, 6 369 total | `validation-20261114-wave-nine-full-nonlive.log` |
| Dix échecs reproduits avec diagnostic détaillé | contrats statiques Socket, quatre validations source et cinq fixtures HDV (reprice/search) | `validation-20261114-wave-nine-global-failures-diagnostic.log` |
| Réparation initiale de ces régressions | **39 PASS, 1 FAIL** : une source initialement refusée révoquait incorrectement son propre décodeur | `validation-20261114-wave-nine-global-regression-repaired.log` |
| Réparation du service, ciblés recompilés | **42 PASS, 0 FAIL**, exit 0 ; return source-linked conserve la réussite du décodeur, indépendamment des compteurs read-only | `validation-20261114-wave-nine-global-regression-final.log` |
| Suite globale non-Live suivante | **6 366 PASS, 1 FAIL, 2 skipped**, 6 369 total, 2m31 ; test fiscal de concurrence uniquement | `validation-20261114-wave-nine-full-nonlive-final.log` |
| Test fiscal `HdvTaxDurableRecoveryTests.Concurrent_instances_enforce_cap_by_locking_the_same_journal` isolé | **PASS** sur le même binaire sans recompilation, exit 0 ; seul passage rouge en suite complète non reproduit isolément | `validation-20261114-wave-nine-tax-concurrency-diagnostic.log` |

Les deux correctifs qui ont changé le comportement du
`LaunchService.FeedSocketObservedFrame` ont des rôles distincts :
*après* une vraie acceptation, source révoquée = retour false
et révocation du lien exact ; *avant* la première
admission, une trame rejetée sans lease autorisé n'a
pas le droit de révoquer à tort une future observation
valide de la même source. L'absence de
`SocketSourceAuthorityLedger` (projection facultative)
ne transforme pas le retour de `FeedLinkScoped` en
ACK : le retour reste strictement consultatif.
Les anciens tests de source du client Socket ont été
mis à jour pour reconnaître la mise en file `Outgoing`
sous verrou plutôt que la disposition textuelle d'un
`TryWrite` inline. Les fixtures de repricing et
recherche acheteur initialisent désormais le mode HDV
`_hdvAnyOpen`, les verrous PID et le
server/character/decoderEpoch nécessaires à la
**vraie** frontière V9 ; aucun bypass n'a été
introduit dans le service.

Le test fiscal de concurrence n'a pas été supprimé,
ni ses assertions de plafond relâchées ; son passage
isolé vert ne prouve pas l'absence d'un risque sous
charge du runner global. Les tentatives rouges sont
conservées. Une seconde suite globale, exécutée
sans aucune modification du code ou des assertions
du journal fiscal, est **VERTE**. Cela confirme
une instabilité de test/charge à suivre, sans
prouver que toutes les contentions futures
seront éliminées. MaxTaxSpend=0 reste conforme
au comportement historique.

### Vague 9 — validation PRIME finale, hors Live

| Contrôle | Résultat réellement exécuté | Fichier de journal |
|---|---|---|
| **Non-Live global**, même binaire après réparation du SourceLink et des fixtures | **6 367 réussis, 0 échec, 2 ignorés**, total **6 369**, **2 min 19 s**, exit 0 | `docs/codex/validation-20261114-wave-nine-full-nonlive-recheck.log` |
| `GivreClone.PureTests` avec compilation séquentielle | **363 PASS, 0 FAIL, 0 ignored**, exit 0 | `docs/codex/validation-20261114-wave-nine-puretests.log` |
| `CombatShadowComparison.Harness.csproj`, Release | **65 assertions exécutables PASS**, exit 0, exclusivement offline | `docs/codex/validation-20261114-wave-nine-shadow.log` |
| Test de coût Source Socket `LaunchService.FeedSocketWrittenObservation` réel, données de fixture | **1 PASS**, **256 observations synthétiques**, **1,185 ms**, **307 240 octets alloués sur le thread du test** | `docs/codex/validation-20261114-wave-nine-measurements.log` |
| Test de coût du chemin tactique `PrepareObservedDecision→Inspect`, conseiller réel de V9 | **1 PASS**, **48 inspections stables**, **12,445 ms**, **565 168 octets alloués sur le thread du test** | `docs/codex/validation-20261114-wave-nine-tactical-cost.log` |

Les campagnes se recouvrent : **ne pas additionner**
174 ciblés + 42 régressions + 6 367 non-Live +
363 Pure + 65 Shadow comme s'il s'agissait d'autant
de tests indépendants. Les deux mesures sont des
témoins **ponctuels** exécutés sur cette machine :
elles incluent le harnais/reflection/assertions,
et excluent TCP réel, réseau, timing du serveur,
latence d'un client DOFUS et performances Live.
Ne pas extrapoler les anciens benchmarks A* à ces
chemins. Les allocs observées ne sont pas un budget
de production certifié.

### Frontières encore bloquées et suites de l'objectif global

Le nouveau chemin Socket connaît mieux les étapes
`queued`, `WriteAsync attempted`, `FlushAsync complete`,
mais le serveur n'a **pas** émis un identifiant causal
de requête individuel exploitable pour l'ACK/effet.
`LinkedPacket`, `PID`, `DecoderEpoch`,
`DecoderOrder`, `UUID local`, `Sent` et
`inbound` ne permettent pas de franchir cette
frontière. Les nouvelles séquences Roublard,
Éliotrope, Xélor, Féca, Écaflip et Sacrieur
ne sont **pas** certifiées pour un cast autonome
multitour ; le conseiller reste `CanDispatch=false`.

La voie MITM/Engine a une provenance améliorée,
mais aucun join artificiel avec le relay local
ou `ComboIngress` n'a été ajouté. De plus, le
permit headless 3.7.10.10 et l'absence d'un
upstream autorisé 3.7.13.17 empêchent honnêtement
d'appeler la voie headless « opérationnelle Live ».
Dans le client visible, des requêtes **humaines**
directes échappent au journal des mutations émises
par Givre ; c'est une frontière d'observation,
pas un motif pour bloquer un processus étranger.

Les contrats de banque/pods, fabrication à plusieurs
étapes, forgemagie, donjons, expéditions et progression
manquent encore, selon les flux, de reçus de bout
en bout exactement attribuables. L'opt-in Lua
`ACTIVITY_RECOVERY_TABLE_ONLY` bloque les envois
non journalisés **dans sa boucle**, mais ne
sandboxe pas le top-level d'un script de confiance
à charger. Aucun retry automatique après incertitude.
Le journal des taxes et des mutations maintient
leur incertitude jusqu'au rapprochement opérateur
ou à une preuve réellement attribuable ; l'exécution
de tests non-Live ne valide pas le fonctionnement
contre DOFUS 3.7.13.17.

Prochain développement substantiel : relier, seulement
si possible et avec une vraie clé causale, la demande
locale typée à la réponse/effet serveur pour une
mutation précise ; améliorer ensuite les parcours
banque/craft/forgemagie et progression en tenant
leurs journaux, avant de considérer un nouveau
cast autonome. **GivreClone n'est pas fini**.

### Checkpoint Git, workers et processus de la vague 9

Après gel des six workers, PRIME a exécuté le
contrôle `git diff --check` ciblé sur les fichiers
suivis touchés par la vague 9 : **exit 0** ;
scan des fichiers V9 nouvellement créés :
**0 trailing whitespace**. Le source externe
`GivreProxy/GivreCore/Mitm/TcpProxyServer.cs`,
dans un **dossier frère dépourvu de .git**, a été
inspecté et conservé en place : **0 trailing
whitespace**, mais son diff n'est **pas** inclus
dans le statut Git du worktree. Le contrôle global
`git diff --check` reste **exit 2**, exactement
les quatre anomalies historiques **inchangées** :
`GivreLuaHost.SnowMap.cs:24`,
`GivreLuaHost.Soul.cs:134`,
`TurboFightPolicy.cs:391`,
`GivreClone.App.csproj:184`. Les warnings
LF→CRLF Git n'ajoutent pas d'anomalies.

`git rev-parse HEAD` :
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
**inchangé**. `git status --short` :
**812 entrées non commitées à la sortie** contre 781
au départ. Les entrées sales initiales sont préservées
et les nouveaux fichiers code/tests/journaux/rapport
de la vague 9 ont été ajoutés sans clean/stash/reset.
`DOTNET_ACTIVE=0` en sortie, zéro process
`dotnet`/`testhost`/`VBCSCompiler` encore
actif. Rapport et logs :
`validation-20261114-wave-nine-scoped-diff-check.log`,
`validation-20261114-wave-nine-global-diff-check.log`,
`validation-20261114-wave-nine-git-status.log`.
Ni client DOFUS, ni MizanBot ni processus étranger
touché. Aucun commit ou push.

## Vague 10 — intégration de parcours utilisateur et lectures autoritatives locales, 14 novembre 2026

### Checkpoint vérifié et organisation

Entrée vérifiée **avant les modifications** : HEAD
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`, **812**
entrées Git sales, rapport V9 présent, aucun processus
`dotnet`/`testhost`/`VBCSCompiler`. Les six agents V9 étaient
`sleeping`, mais trois tentatives distinctes de reprise
`agents action=message` ont échoué avec
`PRIME_TRANSFER_IN_PROGRESS`. Aucun agent n'a été autorisé à
modifier un fichier ou à lancer un build V10 ; PRIME a réalisé
les raccordements ci-dessous, sans démarrer de client ou toucher
un processus étranger. Cette restriction de coordination est
explicitée plutôt que présentée comme une exécution par six
workers.

### Parcours 1 : préparation guidée d'un objectif Lua strict dans l'UI

`BotsViewModel.ActivityRecovery.cs` et le panneau classique
`BotsView.axaml` exposent maintenant une véritable commande
`PrepareStrictActivityRouteCommand` avec cellule cible, seuil
pods et option de récolte. Le VM ne prend l'objectif qu'avec
un personnage sélectionné, connecté et non suiveur, une
identité `serverId/characterId` positive, une carte/cellule/
géométrie publiées et un acteur `Player` correspondant
sur la cellule exacte sous `ActorPublicationGate`.
La cible doit être marchable. Un ticket immuable épingle
la source, le personnage, la carte, la génération de carte,
la sélection UI et la cellule. Le dialogue de confirmation
revalide tous ces champs après son retour pour refuser les
cas `A→B→A`, PID recyclé et mouvement de carte pendant la
confirmation.

Après confirmation explicite, la commande écrit dans le
**script per-personnage existant**, via
`PlanningStore.Get(characterName).Script` et
`PlanningStore.Save()`, un parcours one-shot Lua réellement
consommé par `GivreLuaHost.RunLoopCore` :
`ACTIVITY_RECOVERY=true`,
`ACTIVITY_RECOVERY_TABLE_ONLY=true`,
`ACTIVITY_RECOVERY_ROUTE={{map=<carte observée>,
activityRecovery=true,cell=<cible>,gather=true?}}`.
Les paramètres numériques sont bornés et formatés en
InvariantCulture ; `MAX_PODS` est borné entre 1 et 100.
La commande ne lance pas de script, ne fait pas
`move()`/`bank()` et ne réarme pas un journal
`ActivityRecovery` incertain. Les actions manuelles
et les scripts historiques sont préservés. L'ancien
planificateur peut ultérieurement utiliser le script
persisté selon ses réglages existants : la préparation
n'est donc **pas** une sandbox ni un système autonome
de certitude pour la banque et le craft.

L'inspection `InspectActivityRecoveryCommand` est
désormais **asynchrone** : lecture disque durable sur
`Task.Run`, capture préalable du vrai LuaHost,
revalidation après `await` de l'identité, de la
référence sélectionnée et de la génération de
sélection. Un résultat tardif ne peut plus afficher
la quarantaine d'un autre personnage. Les erreurs
de lecture sont projetées en `StorageUnavailable`,
jamais en succès.

### Parcours 2 : journal de transactions HDV consultable ordre par ordre

`HdvDirectMutationJournal.ReadSnapshot(profile,maxRows)`
reconstruit en lecture seule le **vrai journal fsynced**
et sa chaîne de checksums + anchor, avec une opération
par `Started` (phases mises à jour en place, sans
multiplier les lignes `Attempted`/`Confirmed`).
Projection bornée à 1–128 opérations et filtrée
sur l'identité serveur/personnage exacte ; phase
`Started`, `Attempted`, `Uncertain`, `Confirmed`
ou `Reviewed`, clé locale de type sell/buy/reprice/
remove, PID d'origine et ID de run purement local.
Une lecture corrompue, incohérente, non ancrée ou
un dossier journal inaccessible retourne
`StorageUnavailable`, jamais une fausse liste
vide nette. Le cas « journal absent et aucun anchor »
reste distingué comme installation vierge : ce
fait local n'atteste **aucun** état distant.

`LaunchService.HdvReadMutationSnapshot(pid)`
épingle et revalide serveur/personnage avant et
après IO ; un PID recyclé ou inconnu est refusé.
`HdvViewModel.JournalV9.cs` ajoute une collection
observable de lignes réelles `HdvJournalOperationRow`
et affiche UID, GID si disponible, quantité, prix,
type d'opération et statut de rapprochement dans
`BotsView.axaml`. L'opérateur déclenche explicitement
l'inspection et une réponse tardive est ignorée si
le personnage a changé. **Aucune transition,
réservation fiscale, suppression de quarantaine,
injection réseau ou ACK n'est réalisée par la lecture.**
`MaxTaxSpend=0` et les guards d'émission V9 sont
inchangés.

### Parcours 3 : réévaluation tactique au bout du vrai réducteur

`LaunchService.Packets.OnDecoded` déclenche
`QueueCombatTacticalAdvisoryRefresh` **après**
le traitement de `GameActionFightEvent`,
`FightTurnEvent`, `FightFighterShowEvent`,
`FightStartingEvent`, `FightEndEvent` et
`MapMovementEvent`, uniquement quand le même
paquet passe `_comboIngress?.Admit` en diagnostic,
avec direction serveur et décodage du payload.
Le refresh est déjà coalescé à au plus un job
par PID et appelle le chemin exact
`ReadCombatTacticalAdvisoryStatus`, qui
revalide avant de projeter la proposition.
Cette liaison réduit la dépendance à la cadence
UI et aux seuls `FightersUpdated` sans provoquer
de second décodage, de cast ou de confirmation.
`CanDispatch=false` reste absolu. Aucun effet
multiclasse Roublard/Élio/Xélor/Féca/Éca/Sacri
ne devient un ACK sur `LinkedPacket`, ordre
décodeur, PID ou événement inbound seul.

### Tests V10 nouveaux et incidents d'intégration

Deux nouveaux fichiers d'entrée applicative :
`StrictActivityRouteDesktopJourneyV10Tests.cs`
(validation numérique positive/négative,
bindings ViewModel/panel, et vrai
`GivreLuaHost.Start` du script produit
en mode hors jeu, aucun send sans identité)
et `HdvDurableOperatorJourneyV10Tests.cs`
(journal disque en phases, crash/reopen,
UID+profil, autre serveur, clé rejouée,
corruption intacte, concurrence avec sender
actif, cap de lignes, dossier absent et mesure
du coût de relecture).
Le test de câblage desktop historique
`ActivityRecoveryDesktopWiringTests.cs` a été
adapté à la nouvelle inspection asynchrone
et vérifie désormais explicitement les
gardes de génération et identité. Les tests
antérieurs ne sont pas effacés.

Première compilation ciblée V10 :
`validation-20261114-wave-ten-targeted-first.log`,
erreur CS0236 dans la **fixture** : propriété
`Fixture.Path` masquait `System.IO.Path`.
Corrigée sans modification du journal.
Deuxième passe :
`validation-20261114-wave-ten-targeted-recompiled.log`,
**80 PASS, 1 FAIL**, test statique V8 qui
exigeait la déclaration synchrone de
`GetOrCreateHost`. Diagnostic isolé dans
`validation-20261114-wave-ten-activity-ui-diagnostic.log` ;
test adapté aux nouvelles assertions d'IO asynchrone,
sans retirer le refus de fake ACK.
Ensuite `validation-20261114-wave-ten-targeted-final.log` :
**81 PASS, 0 FAIL**, recompilé, exit 0.
Après ajout des négatifs de dossier journal et du
benchmark réel disque, nouvelle passe
`validation-20261114-wave-ten-targeted-final-v2.log` :
**83 PASS, 0 FAIL**, recompilé, exit 0.

**Validation globale, PureTests, Shadow, métriques
définitives et contrôles Git** : consignés dans
le checkpoint de sortie ci-dessous après exécution.
Ne jamais présenter les 6 367/363/65 de V9
comme de nouveaux résultats V10.

### Limites de preuve et travaux nécessaires

Le mode Lua one-shot nécessite toujours une
provenance serveur/acteur vérifiable et les
observations métier exactes pour récolte,
banque et craft ; le chargement du top-level
Lua de confiance n'est pas sandboxé. La
projection HDV utilise un journal local
checksumé, pas un reçu serveur. Une écriture
TCP `WriteAsync/FlushAsync` reste inférieure
à un ACK individuel corrélé et à un effet métier
observé ; un UID d'offre seul ne prouve ni
quantité ni prix de l'achat. Le couplage
transport→combat n'a pas de clé causale
attestable et conserve `CanDispatch=false`.
Les garanties hors Live ne couvrent ni les
latences d'un client DOFUS ni la validation
contre 3.7.13.17. Aucun lancement de DOFUS,
intervention sur MizanBot, processus étranger,
commit, push, stash, reset ni clean n'a été
effectué pendant ces développements.

### V10 — validation PRIME définitive (hors Live)

Les contributions V10 ont été gelées avant les suites globales :
aucun worker n'a compilé ni lancé de jeu. Toutes les campagnes
ci-dessous sont **de nouvelles exécutions V10**, distinctes
des chiffres de V9 :

| Campagne | Verdict réellement exécuté | Journal |
|---|---|---|
| Ciblés V10 recompilés après les deux nouveaux cas de stockage | **83 réussis, 0 échec, 0 ignoré**, 6 s, exit 0 | `docs/codex/validation-20261114-wave-ten-targeted-final-v2.log` |
| `GivreClone.Tests` globale **hors Live** | **6 384 réussis, 0 échec, 2 ignorés**, 6 386 au total, 2 min 24 s, exit 0 | `docs/codex/validation-20261114-wave-ten-full-nonlive.log` |
| `GivreClone.PureTests` avec build PRIME séquentiel | **363 réussis, 0 échec**, 1 s, exit 0 | `docs/codex/validation-20261114-wave-ten-puretests.log` |
| `tools/CombatShadowComparison/CombatShadowComparison.Harness.csproj` Release | **65 assertions exécutables PASS**, exit 0 | `docs/codex/validation-20261114-wave-ten-shadow.log` |
| Lecture du journal HDV fsynced, checksum+anchor, 8 opérations dans la fixture | **1 test PASS**, 48 snapshots en **114,735 ms**, **6 276 192 octets alloués sur le thread appelant**, exit 0 | `docs/codex/validation-20261114-wave-ten-hdv-journal-cost.log` |

Les campagnes se **recouvrent** : ne pas additionner
83 ciblés + 6 384 globaux comme des tests indépendants.
La mesure de 48 lectures couvre le chemin de parsing
du **vrai journal durable local** et son harnais xUnit,
soit environ 2,39 ms par lecture pour les huit ordres de
la fixture ; elle ne mesure ni charge concurrente du
jeu, ni latence serveur, ni trafic client ou performances Live.
La borne de 128 lignes de projection limite les sorties,
mais le parser relit/valide toujours le journal complet
(max 16 Mio) : la lecture est réservée à la commande
manuelle en arrière-plan, pas au heartbeat UI.

L'ancienne instabilité intermittente
`HdvTaxDurableRecoveryTests.Concurrent_instances_enforce_cap_by_locking_the_same_journal`
observée en V9 ne s'est **pas manifestée** dans
la suite globale V10, sans modification de ses assertions.
Son absence sur une passe ne démontre pas que tout
aléa de scheduling est définitivement corrigé.

Les protections de base restent inchangées :
**FIFO MZP1**, source/run/connexion exacts, journal
HDV `Started→Attempted→(Confirmed|Uncertain|Reviewed)`,
`MaxTaxSpend=0` sans plafond monétaire, quarantine
one-shot `ActivityRecovery`, reçus de craft exacts,
et `_comboIngress?.` strictement diagnostique.
Cette validation hors jeu ne constitue **aucune**
preuve Live pour DOFUS 3.7.13.17. L'autonomie banque/
craft/donjon et les combos multiclasse réellement
autorisés nécessitent encore des relations causales
de demande→réponse/effet non inventées.

### Checkpoint de sortie V10 : Git, processus et workers

Contrôle ciblé `git diff --check` sur les fichiers
suivis modifiés par V10 : **exit 0**. Contrôle des deux
nouveaux fichiers V10 de tests non encore suivis :
**0 whitespace en fin de ligne**. Contrôle global
`git diff --check` : **exit 2**, uniquement les
quatre erreurs préexistantes, inchangées :
`GivreLuaHost.SnowMap.cs:24`,
`GivreLuaHost.Soul.cs:134`,
`TurboFightPolicy.cs:391`,
`GivreClone.App.csproj:184`. Fichiers de preuves :
`docs/codex/validation-20261114-wave-ten-scoped-diff-check.log`,
`validation-20261114-wave-ten-global-diff-check.log` et
`validation-20261114-wave-ten-git-status.log`.

`git rev-parse HEAD` :
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
**inchangé**, sans commit/push/reset/clean/stash.
`git status --short` : **826 entrées locales sales**
à la sortie, contre **812 à l'entrée** ; toutes
préservées, y compris les anciennes modifications
non liées et le `GivreProxy` frère qui n'entre
pas dans le diff Git de ce worktree. Aucun
`dotnet`/`testhost`/`VBCSCompiler` actif après
les suites (`DOTNET_ACTIVE=0`). Statut final agents :
six workers disponibles mais toujours `sleeping`,
aucun worker V10 ayant effectué un build ou un edit,
à cause de `PRIME_TRANSFER_IN_PROGRESS`.
Ni client DOFUS, ni MizanBot ni processus tiers
touchés. **V10 validée hors Live**, projet global
encore **incomplet**.

## Vague 11 — objectif activité→production, journal HDV rapproché par ordre exact

### Vérification d'entrée / ownership

Entrée réelle dans le même worktree : HEAD
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
`git status --short` **826 entrées non commitées**, aucune
compilation .NET/testhost active. Contrôle V10 : présents
`BotsViewModel.ActivityRecovery.PrepareStrictActivityRoute`,
`LaunchService.HdvReadMutationSnapshot` avec le panneau HDV
classique et `QueueCombatTacticalAdvisoryRefresh` au réducteur
de paquets. Rapport consolidé et preuves V10 présents.

Six workers de la vague précédente apparaissent en état
`sleeping`, soit six slots déclarés disponibles, mais la
tentative de réactivation par `agents action=message` avec
six **ownerships disjoints** a été rejetée
`PRIME_TRANSFER_IN_PROGRESS`. Aucun worker n'a modifié
le dépôt ni lancé de build V11. PRIME a donc travaillé
seul ; aucun changement n'a attendu un worker ni réinitialisé
un fichier hérité.

### Workflow 1 : parcours utilisateur durable Lua→objectif→atelier

Nouveau vrai service
`Services/Production/GuidedActivityProductionStore.cs` :
`GuidedActivityProductionGoal` épingle
`serverId`, `characterId`, carte source, génération de carte,
cellule, **GID résultat exact**, quantité, seuil de pods et
opt-in de récolte. Document local par personnage checksumé
SHA-256 + ancre dédiée, écriture temporaire+fsync
et verrou OS `.writer-lock` entre processus. Une paire
document/ancre déchirée, corrompue ou illisible retourne
`Uncertain`/bloqué plutôt qu'un ordre inventé ;
un profil étranger ne peut pas obtenir le même but.
La sauvegarde est une **intention utilisateur durable**
et ne représente aucun ACK de déplacement ou de récolte.

Le VM **existant** `BotsViewModel.ActivityRecovery` conserve
sa route Lua one-shot V10 ; V11 ajoute la saisie optionnelle
GID/quantité au même panneau classique
`BotsView.axaml`. L'entrée vérifie la recette réelle
avec `WorkshopLocator.ReadRecipe(GameData,gid)`, quantité,
carte et acteur serveur du ticket V10, puis **revalide
identité/sélection/génération et champs** à la confirmation.
Si le GID est présent, le journal `ActivityRecoveryDurableStore`
doit être `Clear` avant de sauver l'objectif et de remplacer
le script per-personnage ; un journal `Pending/Corrupt/
StorageUnavailable/Busy` refuse, sans auto-clear. Si le
GID est absent, la route V10 sans atelier reste utilisable.
Le bouton « État objectif » inspecte le document et le
marqueur réel ; `Ready`, `AwaitingObservation`,
`Blocked`, `Uncertain` décrivent exactement la
connaissance **locale**. Aucun « stock gagné » ni
« métier terminé » n'est déduit d'une animation, carte,
delta non attribuable ou de ce document.

« Vers atelier » appelle réellement
`BotsViewModel.OpenGuidedProductionCommand` et
`GuidedProductionHandoffRequested`. `MainWindow`
valide une seconde fois personnage et journal puis
appelle `ItemCraftPlannerViewModel.TryImportGuidedActivity`.
Ce chemin utilise les **vraies** commandes existantes
`AddSelectionCommand` et `RefreshPlanCommand`, un
catalogue de source locale vérifiée et une cible GID
exacte, au lieu de fabriquer un objectif dans le runner.
Le planificateur, son budget explicite, l'option coffre,
le plancher de vente, les recettes/ingrédients et les
actions manuelles restent inchangés. L'import n'appelle
**jamais** `RunProduction` ni une injection réseau ;
le départ réel reste le bouton `Lancer` de l'atelier.

Le **vrai** `MainWindow.ItemCraftPlannerHost.ViewModel.ProductionActions`
enveloppe désormais les actions existantes
`ItemCraftProductionLaunch` dans le
`ItemCraftSessionBoundActions` sticky, pour
`BagCount`, coffre, achat HDV, craft et éventuel listing.
À chaque action et après tout `await`, la liaison vérifie
l'original du goal, le GID/quantité cible **toujours**
identiques, serveur/personnage courant et la
quarantaine `ActivityRecovery` durable ; la première
révocation reste sticky, sans réémission. Le runner
`ItemCraftProductionRun` et son propre journal durable
`Started/Uncertain/Completed` demeurent les gardes
authoritatifs pour préflight de stock/atelier et
**reçus craft GID/quantité exacts**. V11 ne les remplace
ni ne fabrique leurs reçus.

Fermeture d'un contournement découvert pendant la
validation : détacher l'objectif guidé, ouvrir un
atelier **manuel** ou redémarrer ne suffit plus à
passer outre un marqueur activité incertain.
`CurrentManualProductionAuthority` à la même vraie
entrée `ProductionActions` exige également un
profil/source/décodeur courants et
`ActivityRecoveryDurableState.Clear`, avec garde
session-bound par opération. Un script externe qui
ne passe pas par cette entrée de l'atelier n'est pas
présenté comme couvert.

### Workflow 2 : HDV sélection et réconciliation exacte

La lecture fsynced par-opération V10 reste la base ;
V11 ajoute au `BotsView.axaml` une sélection
`ListBox` de `HdvJournalOperationRow`, dont
l'identité inclut le **profil original**,
l'intégralité de la clé locale et le `LocalRunId`.
`HdvViewModel.ReconcileSelectedDurableOrderCommand`
demande explicitement une confirmation opérateur
spécifique, avec informations UID/GID/quantité/prix.
Après confirmation, le VM vérifie que la sélection,
la collection, le ticket serveur/personnage et
l'époque de mutation n'ont pas changé.

`LaunchService.HdvReviewExactOperatorOrder` recontrôle
le profil actuel et l'état `HdvReadMutationSnapshot`,
puis demande au **vrai** journal
`HdvDirectMutationJournal.TryReviewExact` la transition
fsynced. L'opération ne passe qu'avec **profil,
Order, RunId tous égaux**, pas de sender encore vivant,
hash/anchor cohérents et identité toujours courante,
en maintenant le verrou fichier de profil. Une
confirmation concurrente/rejouée, un crash, une
corruption ou une connexion remplacée refuse
sans deuxième transition. Les anciens
chemins manuels d'interface restent en place :
la nouvelle entrée exacte n'est pas prétendue
une certification de toutes les formes historiques
de rapprochement par type.

La transition `Reviewed` est une **déclaration humaine**,
pas un reçu ni un résultat automatique d'achat/vente.
Seules les quarantaines **éphémères du même profil et
du même type** sont éventuellement détachées après
la transition locale ; le journal fiscal, les
taxes réservées et `MaxTaxSpend=0` ne sont ni modifiés
ni remboursés sur cette déclaration. Les interfaces
Lua, Bulk, manuel, production restent sur le gate
HDV d'émission V9 ; cette vague ne revendique
aucune corrélation supplémentaire de prix/UID serveur.

### Transport / combat, limitations opérationnelles

Les vraies frontières `TcpProxyServer → SessionBinder →
BotOrchestrator → BotHostService`, Socket et MITM, les
observations post-`WriteAsync/FlushAsync` et le
`_comboIngress?.` ont été vérifiées sans réécrire
leur logique V8–V10 : elles ne fournissent toujours pas
de clé d'ACK serveur individualisée attestable pour
un sort ou un craft. Une écriture locale n'est ni
une confirmation d'effet ni une permission d'action.
Le conseiller de combat multiclasse reste opt-in,
`CanDispatch=false` faute de causalité et de séquences
de preuve authentifiées. Pas de nouveau cast,
combo multiclasse autorisé, animation accélérée,
simulation d'ACK ou modification du client.

Les API d'activité, production, HDV et transport ne
sont pas une promesse d'une autonomie fermée :
banque/pods et récolte durable attendent toujours
des reçus corrélables pour progresser après une
tentative incertaine ; donjons, expéditions, PvM
multiclasse, forgemagie et niveaux n'ont **pas été
élargis fonctionnellement** dans cette vague.
Les actions manuelles et le look desktop classique
sont préservés, les boutons d'import et de
rapprochement sont réellement reliés aux services.

### Tests V11 (journal de validation)

Nouveaux tests d'entrées réelles :
`GuidedActivityProductionHandoffV11Tests.cs` pour
but sauvegardé→reprise après redémarrage→fence
`ActivityRecovery`→import réel VM/catalogue/
commandes sans auto-craft, identité différente,
quantité/GID modifiés, données invalides,
corruptions et profil étranger ; et
`HdvExactOperatorReviewJourneyV11Tests.cs` pour
`TryBegin/Attempted/Uncertain` réels, crash,
review exact sous verrou, in-flight interdit,
profil/clés/run erronés, douze confirmations
concurrentes, replay après review interdit.
Tests V10/V9 ciblés inclus en régression.

Première exécution compilée :
`validation-20261114-wave-eleven-targeted-first.log`,
**77 PASS / 1 FAIL**. La seule erreur était une
`AmbiguousMatchException` du nouveau test
de réflexion `GetMethod` sur deux surcharges de
`TryImportGuidedActivity`; aucun échec applicatif
ou C# n'a été identifié. Le test vérifie désormais
les **deux** signatures précisément.
Recompilation suivante
`validation-20261114-wave-eleven-targeted-recompiled.log` :
**78 PASS / 0 FAIL**.
Après ajout du garde contre objectif GID/quantité
modifié et du test HDV multi-confirmations :
`validation-20261114-wave-eleven-targeted-final.log`
**80 PASS / 0 FAIL**. Première suite globale nonLive :
`validation-20261114-wave-eleven-full-nonlive.log`
**6 408 PASS / 0 FAIL / 2 SKIPPED** (6 410 au total),
**2 min 21 s**. Cette passe est **intermédiaire** :
elle précède la fermeture du bypass atelier manuel,
donc ne sert PAS de validation finale de l'état V11.

La recompilation ciblée **après** le gate atelier
manuel, la nouvelle suite globale, PureTests,
Shadow, les mesures locales et les contrôles Git
sont consignés dans le checkpoint V11 final
ci-dessous, après leur exécution. Ne jamais
substituer les chiffres V10 aux preuves V11.

### V11 — validation finale PRIME sur les derniers binaires

La fermeture du bypass atelier manuel a été **recompilée**
et ses six nouveaux négatifs ont été ajoutés au vrai
test `GuidedActivityProductionHandoffV11Tests` :
`validation-20261114-wave-eleven-targeted-post-manual-gate.log`,
**86 PASS / 0 FAIL / 0 SKIPPED**, 7 s, exit 0.
Il ne faut pas confondre cette campagne avec
la précédente à 80 tests (elle lui succède).

Après ce gel de code, PRIME a exécuté
**séquentiellement**, sans autre build/worker :

| Nouvelle campagne V11 | Résultat constaté | Log |
|---|---|---|
| `GivreClone.Tests` **hors Live**, dernière révision | **6 414 réussis, 0 échec, 2 ignorés**, total 6 416, **2 min 28 s**, exit 0 | `validation-20261114-wave-eleven-full-nonlive-final.log` |
| `GivreClone.PureTests`, build unique séparé | **363 réussis, 0 échec**, 916 ms, exit 0 | `validation-20261114-wave-eleven-puretests.log` |
| `tools/CombatShadowComparison/CombatShadowComparison.Harness.csproj` Release | **65 assertions exécutables PASS**, exit 0 | `validation-20261114-wave-eleven-shadow.log` |
| Relecture réelle, checksum + ancre, du document d'objectif local sur disque | **1 test PASS**, 48 lectures en **27,990 ms**, **1 041 032 octets alloués** sur le thread appelant, exit 0 | `validation-20261114-wave-eleven-guided-objective-cost.log` |

Les 86 ciblés sont un **sous-ensemble** de la
suite globale et ne doivent pas être additionnés
comme des cas indépendants. Les 48 lectures
du petit objectif sous xUnit représentent environ
**0,583 ms par lecture**, allocations
caller-thread moyennes environ **21,7 Ko** :
mesure **hors jeu** avec données de test, sans
latence client DOFUS, TCP ni serveur, et sans
généralisation à un gros journal. Le fichier
objectif est relu sur demande de l'opérateur
et avant les actes du vrai runner ; aucun
benchmark A* ni métrique synthétique n'est
présenté comme du Live.

Les tests des anciennes fonctions transport,
réducteur combat et fiscalité demeurent dans
la suite globale. L'instabilité ponctuelle
du test fiscal de concurrence V9 n'a pas été
reproduite dans ces deux passages nonLive V11 ;
ses assertions n'ont pas été affaiblies.
Cela ne démontre pas une absence définitive
d'aléas de scheduling.

**Limites restantes :** il n'existe toujours pas
de clé de causalité TCP→réponse serveur permettant
d'autoriser la continuation multiclasse, les
actions composites récolte→banque→craft
ne peuvent pas s'auto-réarmer après une issue
de récolte non attribuable, et le journal de
vente ne dispose pas d'un reçu fiscal exact
pour libérer des réservations. `MaxTaxSpend=0`
reste sans plafond monétaire, aucune fausse
valeur UID/GID, taxe ou cast n'est introduite.
Aucun test Live ou client DOFUS 3.7.13.17
n'a été exécuté dans V11.

### V11 — checkpoint de sortie Git/processus/agents

Dernière vérification PRIME après la suite finale :
`git diff --check` **ciblé exit 0** sur les fichiers
suivis V11 ; **0 whitespace** dans les six nouveaux
fichiers code/test V11 non encore suivis.
`git diff --check` **global exit 2** uniquement
sur les quatre défauts **préexistants**
`GivreLuaHost.SnowMap.cs:24` et
`GivreLuaHost.Soul.cs:134` (espaces fin de ligne),
`TurboFightPolicy.cs:391` (ligne vide EOF) et
`GivreClone.App.csproj:184` (espaces fin de ligne).
Les logs conservés sont
`validation-20261114-wave-eleven-scoped-diff-check.log`,
`validation-20261114-wave-eleven-global-diff-check.log`
et `validation-20261114-wave-eleven-git-status.log`.

`git rev-parse HEAD` est exactement
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
**inchangé**. `git status --short` :
**844 entrées sales préservées** à la sortie,
contre 826 au checkpoint d'entrée. Ce comptage
inclut les journaux de validation et le log final,
ne signifie ni 844 fichiers de production modifiés
ni 844 fichiers contrôlés séparément. Aucun
`dotnet`/`testhost`/`VBCSCompiler` actif
(`DOTNET_ACTIVE=0`).
Six agents toujours `sleeping`, aucun agent V11
n'a été lancé : `PRIME_TRANSFER_IN_PROGRESS`
sur la tentative de réveil. Pas de build parallèle,
DOFUS/MizanBot/processus étranger touché,
commit/push/reset/clean/stash ni suppression
de modifications préexistantes.

**V11 fonctionnelle validée hors jeu, projet global
non terminé**. Les prochaines priorités concrètes
restent (1) corrélation d'une demande précise à
un reçu serveur authentifié et attribuable avant
auto-continuer en combat ou après récolte,
(2) rapprochement exact du stock banque et des
ingrédients avec reçus craft, (3) couverture
sans échappatoire des chemins économiques,
(4) enchaînements multiclasse/donjons/expéditions
sur preuves client 3.7.13.17. Aucun de ces
résultats manquants n'est déclaré validé.

## Vague 12 — orchestration activité→stock→production, HDV inspecté, révocation tactique source-liée

### Checkpoint d'entrée, ownership et discipline

Worktree conservé sans repartir de zéro. Vérifications PRIME
au début V12 : HEAD exact
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`,
**844 entrées Git non commitées**, zéro
`dotnet`/`testhost`/`VBCSCompiler` actif.
V11 réellement présent dans le source : objectif
`GuidedActivityProductionStore` et
`MainWindow.ProductionActions` (V11 route→atelier),
et `HdvDirectMutationJournal.TryReviewExact` avec
sélection et bouton de confirmation sur le vrai
panneau HDV. Dernières sections V11 lues et conservées.

`agents action=status` : six workers
`worker-10/12/13/15/16/17` toujours
`sleeping` et déclarés réactivables, mais
`agents action=message` pour six ownerships
exclusifs a été refusé par
`PRIME_TRANSFER_IN_PROGRESS`.
**Aucun worker V12 n'a écrit ni compilé**.
PRIME a poursuivi seul et conduit toutes les
compilations/test séquentiellement ; aucun client
DOFUS ou programme MizanBot exécuté/touché.

### Parcours applicatif 1 — objectif durable, stock observé, vrai atelier et reprise

Ajout de
`Services/Production/GuidedProductionWorkflowLedger.cs`
au véritable parcours V11. Ce n'est **pas**
un second exécuteur : les émissions sont toujours
effectuées par `ItemCraftProductionRun` V9 et
ses ports `ItemCraftProductionLaunch`, gardes
`ItemCraftSessionBoundActions` et journal
`ItemCraftProductionAttemptJournal` V9.

Machine d'états durable par **serverId/characterId** :
`Absent`, `Ready`, `InProgress`,
`AwaitingObservation`, `Blocked`, `Uncertain`,
`Reconciled`. Le checkpoint contient
SHA-256 du **but V11 exact** et de la **requête V9
exacte** (recette, ingrédients, méthode d'achat,
budget, autorisation de coffre, quantité et
listing), ainsi que le **RunId réellement fsynced**
du journal V9. Écriture JSON base64 ancrée
SHA-256, étape/ancre fsync, lock OS par personnage.
Une écriture déchirée, corruption, concurrence,
stockage inaccessible, reprise d'un run inconnu
ou mismatch de profil refuse ; les preuves ne
sont pas reconstruites depuis un simple delta
d'inventaire. L'identifiant de run est
**strictement local** et n'est jamais appelé un
ACK ou une clé serveur.

`ItemCraftProductionAttemptJournal.ReadStatus`
expose désormais `LocalRunId` dans son snapshot
(`Guid.Empty` par défaut pour compatibilité) :
la phase `Completed` du vrai journal V9
ne peut être attribuée au workflow V12 qu'en
vérifiant **GoalHash, RequestHash, LocalRunId
tous exacts**. `TryCompleteFromActualProductionJournal`
consigne `Reconciled` uniquement lorsque le
runner V9 s'est terminé après ses contrôles
de reçus GID/quantité, consommation d'ingrédients,
delta de stock et éventuel listing. Si Stop,
crash, cancellation ou résultat partiel, l'état
reste `InProgress/Uncertain` et aucune
action n'est réémise.

`GuidedProductionStockPlanner.Evaluate`
fait le calcul borné et `checked` des ingrédients
requis pour chaque rendement de recette.
Il lit les **véritables ports d'observation**
`TryReadBagCount` et `ChestCount`,
respecte l'opt-in coffre et `BuyAllowed`,
expose les déficits par GID et distingue besoin
de coffre, acheteur et récolte. Si rendement,
recette ou observation de sac manquent, le
préflight **refuse** plutôt que d'inventer du
stock. Ces comptes ne prouvent pas une
récolte causale, même lorsqu'ils changent.

Le préflight de V12 possède un
`GuidedProductionReadOnlyActions` distinct.
Au point d'entrée réel
`MainWindow.ItemCraftPlannerHost.ViewModel.ProductionPreviewActions`,
la lecture de sac/coffre/interactifs vérifie
la session avant/après et devient révoquée
définitivement sur perte d'identité. **Tous**
les ports mutationnels (coffre, retrait,
achat, prix, craft et listing) sont
explicitement **non émetteurs** dans ce
port de préflight. Cela ferme une vraie
dépendance circulaire découverte pendant V12 :
le premier préflight ne doit PAS passer
par `ProductionActions` qui exige déjà
le checkpoint `Ready`.

Dans `ItemCraftPlannerViewModel.GuidedActivity.cs`,
les nouvelles commandes classiques
`InspectGuidedStockPreflightCommand` et
`InspectGuidedWorkflowCommand` contrôlent le
profil sélectionné, `ActivityRecovery`
(doit être Clear), journal V9 (pas Started/
Uncertain), GID/quantité et vrai préflight
du `ItemCraftProductionRun` avant de persister
`Ready` ou `Blocked`. Le panneau
`ItemCraftPlannerPanel.axaml` affiche une
table dense **GID / Besoin / Sac / Coffre /
Manque après coffre**, statut et actions
« Vérifier ressources » / « État workflow durable ».
Le bouton **Lancer existant** passe ensuite
une seconde fois les contraintes et
`RequestFingerprint`, écrit d'abord
`Started` dans V9, puis
`InProgress` dans V12 avec le RunId V9
*avant toute mutation du runner*. Si le
second journal est indisponible, le run V9
reste conservativement `Uncertain`,
aucune action envoyée par cette branche
ni retry autorisé.

Le vrai `MainWindow.ProductionActions`
appelle `GuidedWorkflowMayContinue` à
chaque action (et via adaptateur après
chaque await). Le chemin manuel est
préservé, mais `CurrentManualProductionAuthority`
refuse désormais de contourner un ancien
workflow V12 `InProgress/Uncertain/
AwaitingObservation`, même après
détachement de l'objectif ou restart.
`BotsViewModel.GuidedProduction` refuse
de remplacer un objectif non résolu et
projette V12 plus `ActivityRecovery` et
le journal V9 dans le vrai panneau Lua.
Les actions de trajet/récolte demeurent
one-shot V10/V9 ; sans ACK individuellement
attribuable aucune récolte ne se libère,
ni deuxième requête ni progression
automatique vers le craft.

### Parcours applicatif 2 — HDV preuve consultable avant review exacte

`LaunchService.Hdv.OperatorEvidenceV12.cs`
introduit `HdvInspectExactOperatorEvidenceV12`
pour **un** ordre issu de la lecture durable
V10/V11, avec vérification du même PID,
serveur/personnage, type, Order complet,
RunId et **époque locale du décodeur**
avant ET après la lecture. La vue expose
les informations effectivement visibles
dans le modèle local : GID/stock du sac
et annonce UID/quantité/prix pour buy/remove,
présence UID pour reprice. Les clés
sell/reprice ne comportant pas toujours GID,
la vue n'en déduit **jamais** un GID
en agrégeant stock ou quantité. Les deltas
ne sont ni un ACK réseau ni une preuve
d'achat/retrait attribuée au run.

Dans le vrai panneau `BotsView.axaml`,
« Inspecter preuves de l'ordre » relit
le journal et le modèle local de la
connexion courante, avec résultat explicite ;
la commande V11 « Rapprocher l'ordre
sélectionné » **refuse** maintenant si
aucune inspection du même ordre n'est
encore valide. La modal inclut la preuve
locale consultée et requiert toujours une
déclaration humaine. `HdvReviewFromExactEvidenceV12`
revalide une deuxième fois l'inspection
et sa source avant d'appeler
`HdvReviewExactOperatorOrderCore`,
qui teste **encore l'époque locale dans la
callback sous le verrou de
`HdvDirectMutationJournal.TryReviewExact`**.
Changement de caractère/session,
PID réutilisé, UID différent, source
périmée, corruption, double clic, run
déjà clos ou concurrent : aucun
auto-clear/replay. La compatibilité
du chemin V11 direct est conservée,
mais l'interface V12 exige la nouvelle
inspection préalable.

L'epoch et le run local ne constituent
pas le token authentique de la connexion
TCP, ni une corrélation causale du serveur.
La phase `Reviewed` reste une **déclaration
opérateur** ; les réservations fiscales
ne sont pas créditées ou libérées sur cette
base. `MaxTaxSpend=0` conserve le sens
historique **aucun plafond monétaire**.
La portée reste les quatre entrées déjà
gâtées par le journal V9
(buy/sell/remove/reprice, via Bulk/Lua
et listing de production) ; les clics
manuels réalisés **directement dans le
client DOFUS** ne deviennent pas
magiquement contrôlés par le bot.

### Parcours applicatif 3 — fermeture/rotation Socket → révocation du conseiller multiclasse

`SocketSourceAuthorityLedger` conserve la
vraie identité locale de source Socket
`CaptureLinkIdentity` avec le token de
connexion déjà acquis en V8/V9 ;
`TryRevokeExact` retourne vrai seulement
si cette **connexion exacte** était
encore propriétaire de l'état par PID.
`LaunchService.Packets.FeedSocketObservedFrame`
observe le remplacement d'un lien
source authentique ; sur succès d'une
nouvelle connexion source exacte, il
révoque immédiatement l'ancien conseil
opt-in. `RevokeSocketObservedLink`
expire l'avis UNIQUEMENT lorsque le lien
fermé était encore courant. Un vieux
callback EOF d'un PID réutilisé ne
peut pas révoquer le successeur.

`LaunchService.Combat.TacticalAdvisor`
révoque sticky l'exécuteur et la
candidate, publie `Expired` avec
raison lisible dans le statut du panneau
classique, et exige un **nouvel opt-in
explicite** après rotation de source.
L'événement UI ne peut casser le callback
réseau s'il lève une exception. Le
`CaptureDecoder` lié, le réducteur
legacy, la FIFO MZP1 et
`_comboIngress?.` restent
distincts/intacts. En l'absence de
preuve causale de sort/effet sur
DOFUS 3.7.13.17, aucun nouveau
cast ou combo multiclasse n'est
automatiquement autorisé.

### Validations exécutées par PRIME et mesures

Les échecs des premières compilations
V12 ont été corrigés **sans supprimer
ni affaiblir les assertions** :
`ItemCraftPlannerPanel.axaml` utilisait
`WrapPanel.Spacing` non reconnu par
Avalonia ; `LaunchService.Hdv.OperatorEvidenceV12`
déclarait deux `uid` dans le même
scope ; l'ancien test de reflet V11
attendait l'appel sans le nouveau
paramètre de garde V12. Aucune de
ces passes échouées n'est utilisée
comme validation du résultat gelé.

| Campagne V12 sur l'état gelé | Résultat exact | Journal |
|---|---|---|
| Ciblés recompilés, véritables UI/services/transport et régressions concernées | **79 PASS / 0 FAIL / 0 skipped**, exit 0 | `validation-20261114-wave-twelve-targeted-freeze.log` |
| `GivreClone.Tests` complet **hors Live**, mêmes binaires V12 | **6 434 PASS / 0 FAIL / 2 skipped**, total 6 436, **2 min 59 s**, exit 0 | `validation-20261114-wave-twelve-full-nonlive.log` |
| `GivreClone.PureTests` séparé | **363 PASS / 0 FAIL**, 826 ms, exit 0 | `validation-20261114-wave-twelve-puretests.log` |
| `tools/CombatShadowComparison/CombatShadowComparison.Harness.csproj` Release | **65 assertions PASS**, exit 0 | `validation-20261114-wave-twelve-shadow.log` |
| Relecture réelle du petit checkpoint SHA/ancre V12 sur disque | **32 lectures en 38,617 ms**, **767 384 octets alloués** sur le thread appelant, un test PASS, exit 0 | `validation-20261114-wave-twelve-workflow-read-cost.log` |

Le test de performance V12 mesure environ
**1,207 ms/lecture** et **23,98 Ko
d'allocations par lecture** avec un petit
document sous xUnit, **hors Live**. Il
ne mesure ni le temps réseau, ni les
reçus craft, ni le client ni l'A*.
Les ciblés sont un sous-ensemble de
la suite globale, pas des tests
indépendants à additionner.

Tests V12 nouveaux :
`GuidedProductionOperationalV12Tests`
(inventaire et coffres, ports preview
non émetteurs, ABA, crash/restart,
corruptions, 16 doubles clics,
run exact de production, bypass manuel),
`HdvInspectedEvidenceJourneyV12Tests`
(égalité exacte source/ordre/run,
phase, revue fsynced sous callback
d'époque et UI) et
`CombatTransportRevocationV12Tests`
(vrai observer postFlush→EOF→révocation,
successeur même PID protégé, EOF
retardé/dupliqué). Les validations
V8–V11 concernées sont incluses.

### Limites qui restent ouvertes

V12 a **raccordé des workflows
persistants et des barrières sûres**,
pas une autonomie Live complète.
Un objet peut manquer et nécessiter
un trajet ou récolte réellement admis,
mais le modèle ne peut *pas* déduire
son acquisition d'un delta d'inventaire
sans corrélation serveur, ni réémettre
une collecte incertaine. Les snapshots
provenant de Lua/Socket/MITM/decoder
ne prouvent toujours pas un
ACK individuel de sort ou
demande d'achat. Les mécanismes de
Roublard/Éliotrope/Xélor/Féca/
Écaflip/Sacrieur conservent donc
les règles de révocation et de
blocage de l'exécuteur opt-in
sans cast ajouté non prouvé.

Il manque encore des reçus
authentifiés et attribuables
pour boucle full-auto récolte→banque
→craft, ainsi que des liens
causaux pour enchaîner des
combats multiclasse multi-tours,
la forgemagie, les donjons,
expéditions et la progression
sur DOFUS 3.7.13.17. Aucun
événement, identifiant, sort,
prix, coefficient ou ACK
n'a été inventé ; aucune
preuve hors jeu n'est dite Live.

### V12 — gel final Git, processus et workers

Après validations PRIME : `git diff --check`
sur les fichiers suivis effectivement
modifiés pendant V12 : **exit 0** ;
contrôle fin de ligne des nouveaux fichiers
V12 non suivis et fichiers V11 non suivis
édités dans V12 : **0 whitespace**.
Logs `validation-20261114-wave-twelve-scoped-diff-check.log`,
`validation-20261114-wave-twelve-global-diff-check.log`
et `validation-20261114-wave-twelve-git-status.log`
sous `docs/codex/`.

Contrôle global `git diff --check` :
**exit 2**, exactement les quatre
anomalies déjà présentes avant V12 :
`GivreLuaHost.SnowMap.cs:24`
(trailing whitespace),
`GivreLuaHost.Soul.cs:134`
(trailing whitespace),
`TurboFightPolicy.cs:391`
(new blank line at EOF) et
`GivreClone.App.csproj:184`
(trailing whitespace). V12 n'a pas
modifié ces quatre emplacements.

HEAD toujours
`b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`.
`git status --short` :
**860 entrées non commitées**
contre 844 en entrée, sans en
effacer ni en réinitialiser aucune.
`DOTNET_ACTIVE=0` au gel final
(`dotnet`, `testhost`,
`VBCSCompiler`). Statut workers
recontrôlé en fin : **six sleeping/
revivable**, six slots libres ;
aucune contribution V12 de worker
car réveil de la session refusé.
PRIME seul a exécuté ses builds,
tests et contrôles séquentiels.
Aucun client DOFUS, MizanBot,
processus tiers, commit, push,
reset, clean ou stash touché.

**V12 fonctionnelle validée hors
jeu ; objectif global encore
incomplet.** La prochaine reprise
doit prioriser les vrais reçus
récolte/banque/craft causalement
attribuables, puis l'authentification
d'étapes multiclasse, plutôt que
promouvoir des événements consultatifs
ou émettre une requête incertaine.

### V13 — réconciliation à l'entrée du cycle V14 (14 novembre 2026)

Réconciliation sans effacement : HEAD `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`, état dirty préexistant conservé. La campagne ciblée V13 recompliée indiquait **154/154 PASS**, exit **0** (`validation-20261114-wave-thirteen-targeted-final.log`). Sa campagne globale initiale était **6508 PASS, 1 FAIL, 2 ignorés** (`validation-20261114-wave-thirteen-full-nonlive.log`) : le défaut isolé concernait l'inventaire de l'API Lua, périmé après nouvelles liaisons. L'inventaire a été régénéré et son test dédié a ensuite passé **1/1**, exit **0** (`validation-20261114-wave-thirteen-lua-api-regenerated.log`). **Ne pas réécrire cette campagne V13 en succès global :** la démonstration globale avec l'inventaire actualisé est la campagne V14 décrite ci-dessous.

État V13 confirmé sur les vrais services au début V14 : `GuidedSupplyPlannerV13` calcule un plan GID/quantité à partir de la recette, du sac, du coffre opt-in, des candidats d'achat, des déficits non achetables, de la source de stock et de sa révision ; `GuidedProductionWorkflowLedger` conserve les checkpoints ; le chemin Atelier→Lua revalide avant l'entrée route ; `ItemCraftProductionRun` et le journal V9 demeurent seuls propriétaires des achats, retraits et reçus exacts de fabrication. La prévisualisation V13, les delta d'inventaire et la position courante ne sont jamais un reçu attribuable. Le V13 ne constituait ni une autonomie complète ni une preuve Live.

### V14 — raccordements fonctionnels réels et protection du dernier instant (14 novembre 2026)

1. **Approvisionnement exploitable, un GID de ressource à la fois.** Dans `ItemCraftPlannerViewModel.GuidedActivity.cs`, le déficit d'ingrédient non achetable est associé au GID exact, à la quantité totale requise, aux empreintes de l'objectif, de la requête et du plan, à la source/séance et à la révision d'inventaire. L'atelier affiche le GID sélectionné, les étapes, blocages et attentes ; la commande existante `PrepareStrictActivityRouteCommand` est sollicitée depuis `MainWindow.axaml.cs` avec confirmation humaine et revérification avant/après la modale. `BotsViewModel.ActivityRecovery.cs` génère `ITEMS_TO_GATHER={GID}` et les métadonnées `GUIDED_SUPPLY_*` dans la vraie table Lua one-shot (`ACTIVITY_RECOVERY_TABLE_ONLY`) ; **aucun déplacement de repli** en cas de ressource absente. Le mode manuel historique reste possible. Un déficit de plusieurs GID exige une sélection explicite d'un seul GID et une nouvelle observation avant l'étape suivante.

2. **Admission réelle de la récolte et reprise.** `ActivityRecovery.Host.cs`, `GivreLuaHost.Gather.cs`, `GivreLuaHost.Route.cs` et `GivreLuaHost.cs` utilisent les hôtes et leur journal de réservation durables EXISTANTS : contrôle GID filtre/skill/interactif à portée, état du personnage, pods attestés, empreintes V11/V13, lien Socket/MITM, génération décodeur/carte, source et révision d'inventaire, déficit avant réservation, après fsync et avant envoi. Si l'état change, refus ; si une écriture irréversible a déjà été réservée, la quarantaine persiste sans replay. Les sorties strictes ne doivent plus combiner MoveTo et changement de carte sous une seule lease ; usage de porte sous interaction unique et pas de phoenix/auto-résurrection sans réservation. **Correctif final :** la carte d'origine immuable (`Goal.SourceMap`) n'est pas comparée à la carte courante après un trajet : cette dernière est comparée à `ReadGuidedSupplySource` et aux hash de la source et de la session V13, en préservant acteur, génération et révision. Tests de parcours réels Lua/MoonSharp et tests négatifs présents ; aucune récolte Live prétendue.

3. **Continuité banque/pods, atelier et HDV classique.** L'atelier donne accès, par commandes opt-in, aux onglets banque/pods et HDV existants sous contrôle serveur/personnage et reprise durable, sans créer de moteur de transfert, sans envoyer de retrait/achat/vente depuis les boutons d'inspection. Les préflight relisent les stocks ; la relecture d'un checkpoint identique est consultative et ne fabrique pas une seconde écriture ; le runner V9 reste la seule voie de mutation sous lease. Une tentative d'activité/atelier incertaine garde son état durable et les panneaux affichent la quarantaine. `ItemCraftProductionLaunch.ListAsync` consulte `HdvCaptureProductionListingV14` AVANT le véritable appel puis `HdvAssessProductionListingV14` APRES son retour. En guidé, l'absence de capture exacte interdit le nouvel envoi ; le résultat doit correspondre à un UID sac unique, au journal hash-chain contenant `Attempted`, à un listing UID/GID/lot/prix exact et au vrai bool du MÊME appel. Une confirmation locale est indiquée comme **corrélation métier locale**, non comme ACK TCP ni libération des taxes. Les API HDV manuelles/Bulk/Lua et les quarantaines conservent le même émetteur V9/V13.

4. **Autorité Socket/MITM et combat multiclasse.** Dans Socket, l'observation de frame conserve sa copie immuable pour chaque consommateur et un injecteur reste lié à la lease/source après réentrance, EOF, Stop/Start et PID réutilisé ; le diagnostic explicite l'absence d'identifiant demande→réponse individuellement attribuable. Dans le proxy MITM frère, l'admission externe/upstream ne bloque plus toute la boucle d'acceptation et reste bornée à 16 admissions ; dans `SessionBinder`, une fabrique lente ne garde plus le verrou global, avec validation de provenance et refus des callbacks périmés. FIFO MZP1 conservé ; ni un post-flush, ni un paquet inbound, ni `LinkedPacket` ne devient ACK. `VerifiedTacticalExecutor`/`ClassMechanics` refusent les observations multiclasse contradictoires, expirées, de mauvaise cible/source/tour et les confirmations réordonnées sans chaîne de casts/effects ; conseiller opt-in sans cast supplémentaire non attesté.

**Campagnes PRIME séquentielles, hors DOFUS :**

| Vérification | Preuve exacte | Exit |
| --- | --- | --- |
| V14 ciblés recompilés, avant correctif carte | 210 PASS / 0 FAIL / 0 ignoré, `validation-20261114-wave-fourteen-targeted-first.log` | 0 |
| V14 global initial | 6581 PASS / 0 FAIL / 2 ignorés, `validation-20261114-wave-fourteen-full-nonlive.log` | 0 |
| PureTests | 363 PASS / 0 FAIL / 0 ignoré, `validation-20261114-wave-fourteen-pure-tests.log` | 0 |
| Shadow Release | 65 assertions exécutables PASS, `validation-20261114-wave-fourteen-shadow.log` | 0 |
| Source carte actuelle ≠ origine, recompilation ciblée | 93 PASS / 0 FAIL / 0 ignoré, `validation-20261114-wave-fourteen-targeted-after-map-fix.log` | 0 |
| Global FINAL sur derniers binaires V14 | **6582 PASS / 0 FAIL / 2 ignorés** (6584 total, 2 min 27 s), `validation-20261114-wave-fourteen-full-nonlive-final.log` | **0** |
| Générateur local V14, 128 brouillons dans un même processus | 61,304 ms au total ; 967992 octets alloués sur le thread appelant ; test 1/1 PASS, `validation-20261114-wave-fourteen-guided-route-metrics.log` | 0 |

Les mesures précédentes portent seulement sur les brouillons locaux, incluant les assertions du test ; elles ne démontrent ni débit serveur ni latence client Live. Le décompte Pure/Shadow n'a pas été extrapolé après le dernier patch limité à la garde de carte et au test de ce parcours.

**Git/état durable :** contrôles consignés dans `validation-20261114-wave-fourteen-final-git-check.log` et `validation-20261114-wave-fourteen-dirty-status.log`. HEAD inchangé `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660` ; contrôle whitespace suivi ciblé **exit 0**, six nouveaux fichiers de tests V14 vérifiés séparément, **0 défaut**. Contrôle global `git diff --check` **exit 2**, *exactement quatre anomalies historiques pré-V12* : `GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`, `TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184` ; aucune correction hors périmètre. À l'instant du contrôle avant ce rapport : 889 entrées dirty, `dotnet/testhost/VBCSCompiler=0`. Le source `GivreProxy/GivreCore/Mitm/TcpProxyServer.cs` est dans un dossier frère et a été modifié en place, sans Git destructif. Workers 12 terminé, 13/15/16/17 gelés ou en sommeil, 18 n'a pas pu démarrer ; PRIME seul a lancé les compilations et tests. Aucun DOFUS, MizanBot, processus étranger, reset, clean, stash, commit ou push.

**Limites et reprise prioritaire.** La chaîne complète n'est pas prouvée autonome : acquisition, banque, métiers/forgemagie, quêtes/donjons/expéditions demandent encore des admissions et des reçus individuellement attribuables selon les protocoles réellement observables. Une récolte strict one-shot reste en quarantaine après une issue de transport non corrélée, sans auto-clear, et un delta stock n'est pas un ACK. La clé SELL durable historique ne contient pas à elle seule GID + run de production : le rapprochement V14 nécessite un contexte supplémentaire du vrai appel, ne peut prouver un ACK réseau ou libérer le budget fiscal. Les combos multiclasse restent consultatifs en l'absence de cast/effet attribuable. **V14 validée hors jeu uniquement ; projet global non terminé.**


### V15 — transitions fonctionnelles, causalité et validation PRIME (14 novembre 2026)

**Portée et provenance.** Reprise exacte du worktree GivreClone pour DOFUS 3.7.13.17, sans lancement du jeu. HEAD inchangé : `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`. Les validations historiques V12 (79 ciblés/6434 nonLive/363 Pure/65 Shadow), V13 (154 ciblés, première globale avec échec d'inventaire Lua puis correctif) et V14 (93 ciblés après correctif carte ; 6582 nonLive/2 ignorés/363 Pure/65 Shadow) restent distinctes. Aucune suppression, réécriture ou remise à zéro des sections précédentes.

**1. Atelier classique : parcours V15 sur vrais raccordements.** PRIME a créé `GivreClone.App/Services/Production/GuidedSupplyContinuationV15.cs` : `GuidedSupplyContinuationPlannerV15.Inspect` consulte uniquement le but durable V11, le checkpoint `GuidedProductionWorkflowLedger`, `ActivityRecoveryDurableStatus`, l'état de tentative `ItemCraftProductionAttemptJournal` V9 et le plan source-bound V13. Les décisions typées indiquent : inspecter la reprise, inspecter la tentative V9, relire le stock, choisir un GID exact, préparer la route récolte one-shot, consulter banque/pods, utiliser le runner V9 existant, inspecter le listing déjà terminé. `MayAuthorizeGameSend=false`, `MayClearPendingRecovery=false`, `HasIndividuallyAttributedServerAck=false` pour toutes les branches ; source ou but remplacé, corruption, tentative ambiguë et absence de preuve restent fail-closed.

Dans `ItemCraftPlannerViewModel.GuidedActivity.cs` et `ItemCraftPlannerPanel.axaml` : boutons compacts `InspectGuidedSupplyContinuationCommand` et `PrepareGuidedSupplyNextStepCommand`, statut explicatif et ligne `SUITE V15`. Relecture réelle du propriétaire sélectionné, identité personnage/serveur, journaux durable recovery/workflow/tentative et checkpoint V13, puis second contrôle de mutation concurrente avant d'afficher la décision. L'opérateur retourne aux commandes EXISTANTES : `InspectGuidedStockPreflight`, `PrepareGuidedSupplyRoute` (GID explicitement choisi = déficit attesté), `InspectGuidedBankSupply`, `InspectGuidedHdvListing` ; la fabrication reste exclusivement `RunProduction` V9 via le bouton séparé `Lancer`, jamais un nouveau sender.

`MainWindow.axaml.cs` raccorde l'inspection de quarantaine à l'onglet Lua EXISTANT avec `BotsVM.InspectActivityRecoveryCommand` après contrôle du propriétaire/objectif durable. Une activité `Pending` ne devient pas `Clear` en raison d'un changement de carte, d'un delta d'inventaire ou des pods. L'inspection seule n'effectue ni transport, ni banque, ni craft, ni vente. Les changements restent visibles dans la présentation desktop classique, sans seconde interface ni moteur concurrent.

**2. Contributions V15 gelées, ownership disjoint.** Worker-16 : `GivreLuaHost.Route.cs`, `.Gather.cs`, `ActivityRecovery.Host.cs` et nouveaux `ActivitySupplyContinuityV15Tests.cs` ; vrai chemin MoonSharp RunLoop/RunRoute/GatherOne, pods désormais obligatoirement attestés en récolte stricte, ressources épuisées/inaccessibles ou carte changée suspendent l'activité, Stop/Start et callback obsolète ne remplacent pas le résultat de la nouvelle génération. API `RefreshActivitySupplyContinuityStatus(gid,required,maxPods)` retourne lecture durable + source, ressources et étape d'inspection conditionnelle, jamais un droit d'envoi/clear. Worker-13 : `HdvDirectMutationJournal.cs`, `LaunchService.Hdv.OperatorEvidenceV12.cs` et `.OperatorReconciliationV11.cs`, nouveau `HdvProductionNextStepV15Tests.cs` ; `ReadTransactionV15(profile,order,run)` et `HdvInspectProductionNextStepV15` donnent un verdict atomique journal hash+anchor + original PID/run/order, sans post-return replay ni fausse libération fiscale ; un journal tronqué avec ancien témoin actif est indisponible, `MaxTaxSpend=0` demeure sans plafond monétaire et non une admission d'ordre.

Worker-15 : `GivreProxy/GivreCore/Mitm/TcpProxyServer.cs` (dossier frère), `GivreClone.Engine/SessionBinder.cs`, nouveau `MitmLifecycleV15Tests.cs` ; borne 16 liens **actifs** en plus des admissions en cours, vérification OS propriétaire sortie du verrou global avec revalidation à la reprise, verrous de capture par attachement et cas PID réutilisé, arrêt/reconnexion et FIFO MZP1. Worker-17 : `VerifiedTacticalExecutor.cs`, `ClassMechanics.cs`, nouveau `CombatDecisionCausalityV15Tests.cs` ; preuve cast/target/effect/source/tour/expiration pour les enchaînements multiclasse, opt-in advisory lorsque corrélation causale manque, aucun cast inventé. Worker-20 : `ProfessionProductionWorkflow.cs`, `ProfessionProductionJournal.cs`, nouveau `ProfessionRecoveryV15Tests.cs` ; contrat `IProfessionProductionV9Evidence` et reçus de craft/acquisition indépendamment vérifiés (action/session/révision/recette/run/request hash/quantité), journal d'évidence versionné `V9_EXACT` et protection anti-downgrade au restart. Worker-19 n'a pas pris la tâche : PRIME a réalisé le raccordement Supply V15 sans toucher aux autres ownerships. Tous les workers contributions V15 ont été gelés avant compilation ; PRIME seul a lancé .NET.

**3. Régressions réellement corrigées pendant l'intégration.** (a) Les sources métier liées directement dans `GivreClone.Tests.csproj` ne voyaient pas le type réel du journal V9 : ajout d'un lien de compilation vers `ItemCraftProductionAttemptJournal.cs`, sans changer le contrat ni dupliquer le runtime. (b) Test HDV V15 « journal vierge » utilisait un dossier inexistant, qui doit être `StorageUnavailable` en V15 : préparation d'une racine réellement accessible avant attente `Unseen`, plus test négatif indépendant exigeant `StorageUnavailable` sur racine absente. (c) Les anciens parcours positifs récolte V8/V9/V13 utilisaient des fixtures sans pods attestés : ils simulent maintenant des pods observés valides ; test négatif ajouté imposant **absence de send** quand `PodsAuthoritative=false`. Le garde strict en production n'a pas été affaibli. (d) `python docs/portal/lua-f07-inventory.py`, exit 0 : inventaire documentaire Lua régénéré après changement des sources.

**4. Résultats .NET effectivement exécutés, toutes les compilations/test séquentiels.**

| Campagne | Résultat réel | Code sortie | Log `docs/codex/` |
| --- | --- | ---: | --- |
| Première compilation ciblée V15 | Erreurs de référence du journal métier V9, avant fix | 1 | `validation-20261114-wave-fifteen-targeted-final.log` |
| Ciblés recompilés après lien métier | 134 PASS, 1 FAIL (fixture HDV racine absente) | 1 | `validation-20261114-wave-fifteen-targeted-after-profession-link.log` |
| Ciblés recompilés après correctif HDV | **136 PASS / 0 FAIL / 0 ignoré** | **0** | `validation-20261114-wave-fifteen-targeted-after-root-fix.log` |
| Première globale V15 | 6629 PASS / 6 FAIL / 2 ignorés (inventaire Lua + 5 anciens parcours récolte sans pods) | 1 | `validation-20261114-wave-fifteen-full-nonlive.log` |
| Ciblés activité / Lua après fix | **42 PASS / 0 FAIL** | **0** | `validation-20261114-wave-fifteen-activity-lua-after-pods-fix.log` |
| Globale V15 intermédiaire | 6635 PASS / 1 FAIL / 2 ignorés (concurrence HDV) | 1 | `validation-20261114-wave-fifteen-full-nonlive-final.log` |
| Test concurrence HDV isolé | 1 PASS, puis 12 exécutions isolées consécutives PASS | 0 | `validation-20261114-wave-fifteen-hdv-concurrent-isolated.log`, `validation-20261114-wave-fifteen-hdv-concurrent-stress.log` |
| **Globale V15 finale après relecture** | **6636 PASS / 0 FAIL / 2 ignorés, total 6638, 2 min 4 s** | **0** | `validation-20261114-wave-fifteen-full-nonlive-recheck.log` |
| PureTests | **363 PASS / 0 FAIL** | **0** | `validation-20261114-wave-fifteen-pure-tests.log` |
| CombatShadowComparison Release | **65 assertions exécutables PASS** | **0** | `validation-20261114-wave-fifteen-shadow.log` |
| Métrique next-step V15 | 128 évaluations read-only : 10,987 ms total, 76 200 octets alloués sur thread appelant, 1 test PASS | **0** | `validation-20261114-wave-fifteen-next-step-metrics.log` |

**Intermittence conservée :** l'échec global unique du test `HdvCrossEntryTransactionJourneyV9Tests.Concurrent_bulk_lua_desktop_and_production_orders_have_one_profile_owner` a été constaté, puis le cas a passé isolément 12/12 et la campagne complète finale a passé. La cause d'ordonnancement/concurrence n'est pas attribuée ; ne pas affirmer qu'elle est définitivement réparée ni affaiblir les assertions. Aucune mesure de test ou d'allocation hors jeu ne préjuge du transport, de la latence en combat Live ou des ACK serveur.

**5. Contrôles Git et processus.** `validation-20261114-wave-fifteen-final-git-check.log` : HEAD inchangé ; au contrôle avant ce rapport 911 entrées dirty (891 au départ V15), sans reset/clean/stash/commit/push, aucune suppression des preuves ou quarantaines. `git -c core.safecrlf=false diff --check -- <fichiers V15 suivis>` exit **0** ; contrôle manuel des six fichiers de tests V15 non suivis, du nouveau service et du proxy MITM frère : 0 trailing whitespace ; AXAML XML valide. Diff global exit **2**, exactement quatre défauts **historiques pré-V12**, inchangés : `GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`, `TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184`. `dotnet/testhost/VBCSCompiler=0` au contrôle ; aucun jeu DOFUS, MizanBot ou processus étranger touché.

**6. Limites et prochaine priorité.** V15 est validée **hors jeu**, pas une preuve de bot autonome complet. ActivityRecovery peut rester durablement `Pending` après tentative potentiellement émise sans réponse individuellement attribuable ; ni InventaireRevision, source/link/decoder epoch, `Sent`, post-flush, `LinkedPacket`, map change, intervention humaine ou observation de pods ne sont un ACK. La clé SELL HDV historiquement fsynced ne porte pas le GID et run de production ; l'attribution locale V14/V15 ne constitue ni reçu serveur ni libération de taxes. Les métiers n'ont pas encore de port `IProfessionProductionCertifiedActions` réellement injecté depuis une source serveur indépendamment vérifiable, et les combos multiclasses restent consultatifs lorsque cast/effet attribuable absent. La chaîne approvisionnement→récolte→stock→banque/achats→craft→vente dispose de plusieurs transitions **accessibles**, mais ne s'exécute pas automatiquement au-delà d'un résultat ambigu. Priorités futures : corrélation vraie des réponses serveur, complétude métier/forgemagie et multiclasse, preuve Live ultérieure explicitement autorisée ; ne jamais inventer cette preuve.


### V16 — transitions exécutables, protections causales et validations hors Live (14 novembre 2026)

**Provenance et méthode.** Poursuite dans le worktree existant DOFUS 3.7.13.17, sans redémarrer le projet ni toucher au client. HEAD au début/final avant rapport : `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660` ; environ 912 modifications préexistantes à l'ouverture V16, 926 entrées dirty avant append. Les sections V12, V13, V14 et V15 demeurent séparées et inchangées : V12 79 ciblés/6434 nonLive/363 Pure/65 Shadow ; V13 154 ciblés, globale initiale non verte jusqu'à régénération API Lua ; V14 93 ciblés après carte/6582 nonLive+2 ignored/363 Pure/65 Shadow ; V15 136 ciblés, globale 6636 PASS+2 ignored, Pure 363/Shadow 65, validée hors jeu. Six workers V16 aux ownerships disjoints gelés avant toute compilation PRIME ; PRIME exclusivement VM/UI/wiring/tests séquentiels.

**1. Chaîne de production plus accessible depuis le vrai panneau classique.** Dans `ItemCraftPlannerViewModel.GuidedActivity.cs`, `ItemCraftPlannerPanel.axaml` et `MainWindow.axaml.cs` :
- `GuidedSupplyResumePlannerV16.InspectDurable` (nouveau `Services/Production/GuidedSupplyResumeV16.cs`) joint l'objectif GID/quantité V11 authentifié localement, la recette/requête V9, `GuidedSupplyPlanV13`, la révision et source host, `GuidedProductionWorkflowLedger` V12, `ActivityRecovery` et le journal de tentative V9. La commande `InspectGuidedV16ExactChoices` affiche les candidats GID/quantité **exacts** (coffre, achat à inspecter, récolte strict one-shot, runner V9) dans le panneau dense ; choix via ComboBox. `ChooseGuidedV16ExactStep` exige la sélection, relit la source + les quatre journaux et appelle `TryChooseOnce`, qui persiste **une seule** instruction sous PlanHash/SourceKey/SessionKey/ObservationRevision et CAS du ledger V12 ; cela n'est PAS une admission de transport ni un ACK. `ResumeGuidedV16RecordedChoice` relit ce choix après Stop/Start/crash, le compare à la source et réouvre UNIQUEMENT les commandes existantes si même plan/révision ; un choix récolte ou runner susceptible d'avoir été utilisé ne peut être écrasé sur un simple delta stock.
- `ExecuteGuidedSupplyV16RunnerCommand` offre une vraie transition vers `await RunProduction()` existant, sous checkbox d'opt-in ponctuel consommée dès l'entrée, CAS anti-double-clic, objectif/import/session/checkpoint V13 Ready/source/stock/hash réévalués, journal V9 non Started/Uncertain. Pour coffre, achat, craft exact et listing, **le seul émetteur est toujours `ItemCraftProductionRun` V9**, avec préflight, fsync Started, ports `ItemCraftSessionBoundActions` et reçus aux vraies frontières ; pas de sender de substitution. `RunProduction` conserve son vrai bouton Lancer séparé et ses gardes de génération/serveur/personnage/lien ; un échec d'admission après fsync reste incertain et non rejoué.
- `BotsViewModel.ActivityRecovery.cs::InspectGuidedSupplyV16Async` utilise réellement le `GivreLuaHost.RefreshActivitySupplyContinuityStatus(gid,required,maxPods)` sur hôte existant, via Task.Run et comparaison avant/après PID/serveur/personnage/source/génération/sélection ; `MainWindow.axaml.cs` revalide après await avant mise à jour de l'atelier. La reprise Lua est consultable malgré `ActivityRecovery.Pending`, mais ne libère jamais son marqueur ; après restart le GID persistant peut servir au sujet d'inspection seulement. Ressource épuisée/absente, changement de carte, pods non attestés et trajet impossible rendent l'état visible, jamais un reçu fictif.

**2. Workstreams V16 gelés et vrais défauts causaux.**
- **Activity/Lua (worker-22)** : `ActivityRecovery.Host.cs`, `GivreLuaHost.Route.cs`, `.Gather.cs` et `ActivitySupplyJourneyV16Tests.cs`; `RecoveryBlocksRoute(checkDurable:true,ownerToken:ct)` relié par PRIME au **vrai RunLoop** dans `GivreLuaHost.cs`. Arrêt limité au CTS propriétaire du run ; legacy move/gather ne contourne plus une quarantaine si opt-in retiré après Stop/Start. Pas d'auto-clear ni retry après tentative incertaine.
- **Supply (worker-21)** : nouveau `GuidedSupplyResumeV16.cs`, `GuidedSupplyPlanV13.cs`, `GuidedProductionWorkflowLedger.cs`, `GuidedProductionReadOnlyActions.cs`, `GuidedSupplyResumeV16Tests.cs`. Choix durable exact/fail-closed, relecture identités et checkpoint, limitations de quantité, pods, stock partiel, session et révision ; planificateur consultatif réutilisant le journal V12, pas de moteur indépendant.
- **HDV (worker-23 + PRIME)** : `HdvDirectMutationJournal.cs`, `LaunchService.Hdv.OperatorEvidenceV12.cs`, `HdvCrossEntryAuthorityV16Tests.cs`. Capture d'un **appel de production original** vers `HdvSellAsync`/TryBegin via contexte AsyncLocal et run local exact, inspection `HdvInspectProductionCrossEntryV16` ; le retour bool/listing d'un autre appel manuel/Bulk/Lua avec même UID/prix ne peut plus justifier le résultat du ListAsync original (`CrossEntryAttemptNotAttributed`). PRIME a ajouté dans la vraie dernière frontière `LaunchService.Hdv.cs::HdvSellAsync` un refus si quantité actuelle du stock diffère de la base capturée, ou si les UID annonces ont changé avant le send. `MaxTaxSpend=0` préserve l'absence de plafond monétaire ; pas d'ACK TCP, quittance fiscale ou taxe libérée depuis ces corrélations métier locales.
- **MITM (worker-15)** : `../GivreProxy/GivreCore/Mitm/TcpProxyServer.cs` (répertoire frère), `BotHostService.cs`, `MitmLifecycleV16Tests.cs`. Un diagnostic protobuf malformé ne coupe plus un pump/FIFO de frames brutes valides suivantes ; buffer ReplayCore cloné, admission/source revalidée sous gate avant écriture ; consultation d'autorité externe hors verrou global de host pour ne pas bloquer Stop/Start. Ni PID ni post-flush n'est une réponse serveur attribuable.
- **Combat (worker-17)** : `VerifiedTacticalExecutor.cs`, `ClassMechanics.cs`, `CombatMultiTurnCausalityV16Tests.cs`. `VerifyObservedCombo(...,observedAsOfTurn?)` peut contrôler un tour courant attesté, expiration/completion multi-tour bornées et cast corrélé indépendamment aux marks/summons/effets Roublard/Éliotrope/Féca, les autres classes restant sous contrat existant ; `LaunchService.Combat.TacticalAdvisor` diagnostic ingress ne certifie toujours aucun cast et ne peut autoriser une action supplémentaire.
- **Métiers (worker-20)** : `ProfessionProductionWorkflow.cs`, `ProfessionProductionJournal.cs`, `ProfessionRecoveryV16Tests.cs`. Liens V9 de receipts exacts avec quantité éventuellement partielle, identifiants de reçu et run consommés dans journal, anti-replay et revérification avant CAS ; `InspectExecutionAdmission` retourne `CanExecute=false` faute de port host indépendant certifiant effectivement l'ACK/mutation. Forgemagie reste consultative faute de preuves.

**3. Campagnes séquentielles exécutées PRIME (aucun Live) et réparations.**
| Suite | Résultat | Exit | Log sous `docs/codex/` |
| --- | --- | ---: | --- |
| Première compilation/ciblés V16 | 159 PASS, **2 FAIL** : inventaire Lua non régénéré et fixture V9 ayant recréé le journal à chaque lecture | 1 | `validation-20261114-wave-sixteen-targeted-first.log` |
| Correction documentaire | `python docs/portal/lua-f07-inventory.py`, 1018 liaisons, 0 manquante, JSON régénéré | 0 | générateur de l'inventaire `docs/portal/lua-f07-inventory.json` |
| Échec V9 isolé | `TryFinish` refusait correctement une lease lancée dans une autre instance de journal | 1 | `validation-20261114-wave-sixteen-resume-isolated-failure.log` |
| Ciblés recompilés après correction de fixture (instance V9 propriétaire conservée et test négatif d'une autre instance) | **161 PASS, 0 FAIL** | **0** | `validation-20261114-wave-sixteen-targeted-after-journal-fix.log` |
| Globale `GivreClone.Tests` hors Live sur derniers binaires | **6685 PASS / 0 FAIL / 2 ignorés**, total 6687, 2m26s | **0** | `validation-20261114-wave-sixteen-full-nonlive.log` |
| `GivreClone.PureTests` | **363 PASS / 0 FAIL** | **0** | `validation-20261114-wave-sixteen-pure-tests.log` |
| `CombatShadowComparison` Release | **65 assertions exécutables PASS** | **0** | `validation-20261114-wave-sixteen-shadow.log` |
| Mesure combo V16 isolée | 64 `Inspect` / 32 `VerifyObservedCombo` stricts ; **2 722 792 octets** caller-thread, **828 156 Stopwatch ticks** ; test 1 PASS | **0** | `validation-20261114-wave-sixteen-combat-metrics.log` |

Le correctif du test V9 n'affaiblit aucune assertion : `ItemCraftProductionAttemptJournal.TryFinish` reste interdit à une autre instance du journal (test négatif ajouté). Les métriques ci-dessus mesurent seulement un échantillon du calcul local hors jeu et ne préjugent ni latence réseau ni rythme Live. L'intermittence de concurrence HDV observée en V15 reste un risque à surveiller, même si V16 global est vert.

**4. Autorités manquantes et prochaine priorité.** La chaîne possède maintenant des transitions opérables par commandes réelles plus nombreuses, sans constituer une autonomie complète. Une tentative ActivityRecovery, banque, HDV ou V9 potentiellement émise et indécidable reste bloquante sans réponse **individuellement attribuée**. `Sent`, `LinkedPacket`, post-flush, PID, UUID local, `DecoderEpoch`, `DecoderOrder`, carte, delta stock, pods, capture d'appel AsyncLocal et parole humaine ne sont ni ACK ni reçus fiscaux. Les chaînes de casts multiclasse restent consultatives si connexion/cast/effet causalement attribuables manquent ; métiers/forgemagie/progression/donjons/expéditions restent incomplets. Aucun client DOFUS ni MizanBot lancé, aucun processus étranger touché, aucun git reset/clean/stash/commit/push, aucune édition préexistante effacée. **V16 validée hors Live uniquement et projet global non terminé.**


**Contrôle final V16 post-rapport (14 novembre 2026).** Voir `docs/codex/validation-20261114-wave-sixteen-final-git-check-reconciled.log` : HEAD `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660` inchangé ; `git status --porcelain` **927 entrées dirty** conservées, aucun reset/clean/stash/commit/push ; processus `dotnet/testhost/VBCSCompiler=0`. `git diff --check` ciblé V16 **exit 0** ; contrôle manuel des sept nouveaux tests V16, du nouveau service et du proxy frère : zéro nouvel espace final ; AXAML XML valide. Le journal consolidé non suivi contenait déjà **un** trailing space historique en ligne 1482 dans le préfixe V12-V15 ; la portion append-only V16 comporte **zéro** trailing space et son préfixe octet par octet est resté inchangé. Contrôle Git global **exit 2**, exactement quatre anomalies pré-V12 connues : `GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`, `TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184`. Six workers V16 (15, 17, 20, 21, 22, 23) tous gelés/sleeping, PRIME seul a compilé/testé. Les preuves V16 sont **hors Live** et le projet complet reste incomplet.

### V17 — intégration réelle, validation et réconciliation (14 novembre 2026 ; reprise)

**État vérifié.** HEAD `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660` inchangé. Les six contributions V17 (MITM worker-15, combat worker-17, métiers worker-20, supply worker-21, activité worker-22, HDV worker-23) sont gelées/sleeping ; aucun processus DOFUS/MizanBot/étranger lancé ou arrêté. Le worktree dirty préexistant a été conservé sans `reset`, `clean`, `stash`, `commit` ou `push`.

**Parcours accessible V17.** Le panneau atelier relit les journaux V9/V12/V13/V16 et propose les choix exacts ; `InspectBuy` et reprise durable transmettent un ticket source-bound à `MainWindow`, qui vérifie l'identité et ouvre le vrai `BotsVM.Hdv`. `HdvViewModel.Trade.TryStageGuidedSupplyBuy` ajoute une seule ligne réelle `BuyRows` sur GID et déficit net (sans déduire deux fois le stock), montre la vue achat et protège double-clic, changement d'identité, source révoquée, règle concurrente, dépassement de `int.MaxValue`, achat ambigu et budget/permissions. L'achat **n'est jamais envoyé par ce handoff** : seul le bouton HDV existant peut tenter l'envoi après ses propres rechecks. L'inspection listing post-production et la revue humaine du journal SELL original sont distinctes du staging BUY. `ActivityRecovery.Pending`, tentatives V9 indécidables et fiscalité HDV restent bloquants. Aucun ACK ou reçu serveur n'est inféré d'un état UI, d'un post-flush, d'un delta de stock ou d'une corrélation locale.

**Tests hors jeu documentés avant cette reprise.** `validation-20261114-wave-seventeen-targeted-with-listing-review.log` : 88 PASS, exit 0 (recompilation). `validation-20261114-wave-seventeen-full-nonlive-final.log` : 6755 PASS / 2 ignorés, exit 0 (binaire précédant la nouvelle correction de test). `validation-20261114-wave-seventeen-pure-tests.log` : 363 PASS, exit 0. `validation-20261114-wave-seventeen-shadow.log` : 65 assertions offline PASS, exit 0. `validation-20261114-wave-seventeen-combat-metrics.log` : 1 test métrique PASS, exit 0. Ces tests ne prouvent aucune exécution Live.

**Régression intermittente constatée, non masquée.** Nouvelle exécution globale `validation-20261114-wave-seventeen-full-nonlive-post-listing.log` : 6754 PASS / 1 FAIL (SocketOutboundWriteEvidenceV8Tests.Paused_flush...), 2 ignorés, exit 1. Isolé `validation-20261114-wave-seventeen-socket-flush-isolated-recheck.log` : 1 PASS, exit 0. Autre globale `validation-20261114-wave-seventeen-full-nonlive-confirmation.log` : 6752 PASS / 3 FAIL (ActivityRouteProductionV14Tests.Generated_guided_gid..., MitmAuthorityCycleV13Tests.Owner_loss..., HdvRepricePacingTests.Configured_delay...), 2 ignorés, exit 1. `validation-20261114-wave-seventeen-three-intermittent-isolated.log` : MITM et HDV passent, mais Activity échoue car `journey.Logs` était vide à la lecture après un délai fixe. Cause démontrée pour cette fixture : `ActivityRouteProductionV14Tests.cs` utilisait `Task.Delay(350)` avant d'exiger un diagnostic provenant d'un RunLoop asynchrone, donc dépendait de l'ordonnancement sous charge. Remplacé par une attente bornée de 5 s du diagnostic observé, **sans suppression ni assouplissement des assertions** : zéro gameplay send, aucun marqueur durable, message de refus d'approvisionnement. `validation-20261114-wave-seventeen-activity-diagnostic-recompiled.log` : recompilation et test ciblé PASS, exit 0. Les autres intermittences restent sans cause démontrée ; ne pas prétendre les avoir réparées.

**Limites durables.** Cette validation est exclusivement hors Live ; autorité serveur individuellement attribuable manquante pour beaucoup d'actions, tactiques multiclasse encore consultatives si provenance insuffisante, production/forgemagie non autonomes sans ports hôtes certifiés. Aucun relancement après tentative incertaine, aucun ACK/cast/listing/taxe inventé. Le projet global n'est pas terminé. Les quatre anomalies Git whitespace préexistantes restent `GivreLuaHost.SnowMap.cs:24`, `GivreLuaHost.Soul.cs:134`, `TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184` ; le contrôle ciblé des trois fichiers VM/UI V17 est clean, global diff-check exit 2 historique.
### V18 — stabilisation et raccordements applicatifs (14 novembre 2026, PRIME)

**État et ownerships.** Reprise du worktree existant, HEAD b4d28a80d71e6b98a95f6bb1d17c6dbead80b660 ; 961 entrées dirty au début de cette reprise, 975 au contrôle avant journal. Workers V18 20/21/22/23 ont livré et gelé, 15 n'a pas été réveillé V18, 24 n'a pas démarré. Aucun reset/clean/stash/commit/push, aucun jeu, aucun MizanBot ou processus étranger touché. Les travaux V16/V17 dirty sont conservés.

**Supply réel et choix durable.** API GuidedSupplyResumePlannerV16.RevalidateRecordedNextStepV18 intégrée à l'inspection banque et au staging HDV du vrai atelier ; V18 exige source/stock/révision/session/goal et pods Used/Capacity exacts. Legacy pods manquants restent consultables mais ne deviennent pas un staging BUY. V13 bloque les pods au seuil >= MaxPods. One-shot ne devient jamais un second moteur ou une admission de gameplay. Commande réelle ReinspectGuidedSupplyAfterExternalActionCommand et bouton classique permettent le retour banque/Lua/HDV -> relecture V9/V11/V12/V13/ActivityRecovery -> stock preflight -> choix V18. Banque/HDV navigation revalide avant et après, sans transfert/achat.

**Lua/activité.** Worker-22 a corrigé la fixture intermittente V14 en attendant le diagnostic réel OU la fin du RunLoop et a ajouté un preflight source guidée en entrée Route, sans supprimer les gates durables. API host RefreshActivityRecoveryTransitionsV18Status expose raison map/route/ressource/pods/Stop et checkpoint, toujours HasSegmentAttributedServerReceipt=false, MayAutoDispatch=false et MayAutoClear=false. PRIME l'a raccordée à InspectActivityRecovery du BotsViewModel, avec garde sélection/PID/character/map generation après await. Une fixture V18 avait un literal Lua interpolé avec accolades invalides (CS9006/CS1525/CS1073) ; corrigée. Le diagnostic après fin de run renvoyait RunInactive au lieu de PodsUnconfirmed lorsque les pods n'étaient ni publiés ni certifiés : reclassification consultative plus prioritaire ; aucun effet sur send ni journal durable.

**Métiers / FM.** Worker-20 bloque le nouveau checkpoint si tentative V9 Started ou Uncertain, protège CAS et snapshot immutable, fournit InspectHostProofGaps et décrit les preuves hôte absentes (bridge V9, identité PID/RunId, autorité receipt craft/acquisition/FM, lease). PRIME ajoute le bouton diagnostic en lecture seule au panneau ProfessionPlanner et affiche les codes/conditions manquantes ; planner manuel n'est pas promu en port certifié. Aucun craft/FM sender autonome.

**HDV et réseau.** Worker-23 revalide seller/source/UID/GID/stock juste avant TryBegin du journal production : capture obsolète refuse sans témoin .active neuf ; fiabilise la fixture pacing par profil isolé et timestamps de trames, sans augmenter le seuil wall-clock. MaxTaxSpend=0 reste illimité, taxes non relâchées sans reçu exact. PRIME corrige SocketGameClient pour revalider source et annulation immédiatement après FlushAsync et avant ordinal de post-flush ; ancien lien => ambiguous failure, aucun ACK synthétique, aucun replay. Les tests V18 SocketConcurrentDrain et HdvMutationCausality couvrent ces frontières hors jeu.

**Validation PRIME séquentielle, codes de sortie réels.** Première V18 recompilée: validation-20261114-wave-eighteen-targeted.log exit 1, syntaxe fixture Lua. Rebuild2: 48/49 PASS, exit 1, PodsUnconfirmed affiché RunInactive. Rebuild3: 49/49 PASS, exit 0. Régressions V17 ciblées: 40/40 PASS, exit 0 (validation-20261114-wave-eighteen-v17-regressions.log). Première full nonLive: 6802 PASS, 2 FAIL, 2 ignorés, exit 1 ; échecs de lecture statique du vieux chemin V17 et hash d'inventaire Lua. Test de câblage actualisé sur nouvelle API V18 sans supprimer les gardes ; régénération python docs/portal/lua-f07-inventory.py : 1018 liaisons, 0 absence documentée. Retest de ces deux familles: 6/6 PASS, exit 0. Deuxième full nonLive: 6803 PASS, 1 FAIL MITM lifecycle EOF/reconnect/FIFO, 2 ignorés, exit 1. Test isolé MITM: PASS exit 0. Troisième full nonLive avec TRX: 6804 PASS, 2 ignorés, exit 0 (validation-20261114-wave-eighteen-full-nonlive-third.log ; GivreClone.Tests/TestResults/wave-eighteen-full-third.trx). MITM ciblé répété 5 fois, 5 PASS, chaque exit 0, voir validation-20261114-wave-eighteen-mitm-repeat.log. PureTests: 363/363 PASS exit 0. Combat Shadow: 65 assertions exécutables PASS exit 0. Aucun de ces tests ne valide le jeu Live.

**Intermittence ouverte et limites.** Le FAIL full MitmLifecycleV15Tests.Host_service_preserves_exact_eof_reconnect_fifo_and_immutable_frame_bytes n'est pas reproduit isolément malgré cinq répétitions ; cause sous charge non démontrée, ne pas déclarer cette race éliminée. Les quatre flakies V17 sont passées dans un lot de 40, mais une seule passe ne prouve pas leur stabilité définitive. Les DTO, LinkedPacket, flush, inventaire, source locale et journal ne sont ni ACK ni reçus serveur individuels ; ActivityRecovery Pending, V9 Started/Uncertain, fiscalité HDV et casts multiclasse non attribuables restent bloqués. Combos, craft/FM, progression, donjons et expéditions ne sont pas entièrement Live validés. Aucun jeu lancé. Projet non terminé.

**Contrôle final V18 (après journal et logs).** HEAD b4d28a80d71e6b98a95f6bb1d17c6dbead80b660 ; 977 entrées dirty (incluant les logs V18 et journal) conservées. Contrôle ciblé diff --check des VM/UI/transport/Activity V18 exit 0. Contrôle global exit 2 uniquement sur les quatre anomalies historiques SnowMap.cs:24, Soul.cs:134, TurboFightPolicy.cs:391, GivreClone.App.csproj:184 ; aucune correction hors scope. XML des panneaux métiers et craft valide. Aucun processus dotnet/testhost/VBCSCompiler trouvé au contrôle, aucun build concurrent, aucun jeu. Agents V18 worker-20,21,22,23 sleeping/frozen; worker-15 sleeping sans travail V18 et worker-24 failed. Le préfixe du journal lors du premier append est resté octet pour octet inchangé (184257 octets initiaux). Validation Live absente, intermittent MITM non résolu ; pas de certification globale.

### V19 — correction causale du test MITM EOF/reconnexion/FIFO (14 novembre 2026)

**Répertoire et Live.** Le répertoire sélectionné `/a/.t3/worktrees/GivreWork` contient plusieurs worktrees et n'est pas lui-même un dépôt Git. La correction continue dans le sous-worktree V18 `fix-f01-f16-final-20261024`, HEAD `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660`. L'utilisateur a autorisé désormais les tests Live, connexions et déconnexions ; quatre personnages Socket étaient déjà en jeu dans l'application v1.3.3.11. Aucune déconnexion ni action de gameplay n'était nécessaire à l'isolement MITM. L'application observée n'est pas le binaire V19 recompilé.

**Deux races d'ordonnancement prouvées dans la fixture V15.** `TcpProxyServer.CloseLink` émet `TransportLinkClosed` avant les callbacks `ConnectionClosed` et le retrait complet `SessionBinder`. Attendre le témoin EOF seul ne certifie pas que le binder est vide. De plus, `TcpProxyServer.PumpAsync` émet `TransportFrameObserved` avant le décodage et `MessageCaptured`, qui déclenche l'admission `BotSession`. Le troisième témoin brut ne certifie donc pas que la nouvelle session est déjà créée. En exécution groupée, le TRX `wave-nineteen-mitm.trx` montre `Assert.IsType` obtenu `null` au lieu de `BotSession` dans `MitmLifecycleV15Tests.cs:461`, bien que l'isolé passe.

**Patch strict.** Seul `GivreClone.Tests/MitmLifecycleV15Tests.cs` est modifié : après EOF, attente bornée par le `Deadline` existant de `Binder.Find(ancien ConnectionId) == null`, puis vérification du refus de notification répétée ; après le troisième raw, attente bornée par le même `Deadline` de `Binder.Find(nouveau ConnectionId) != null` avant ses assertions exactes. Toutes les assertions FIFO, octets immuables, PID réutilisé avec identité/token distinct, rejet de l'ancien lien et Stop sont conservées. Aucun timeout élargi, aucun événement, ACK ou reçu créé, aucun ordre de production modifié.

**Validation séquentielle.** Premier rebuild après la seule barrière EOF : 11 PASS / 1 FAIL, exit 1. Test isolé : 1 PASS, exit 0. TRX groupé : échec au `BotSession` non encore admis, exit 1. Après deuxième barrière, recompilation et lot MITM/authority : **12/12 PASS, exit 0**, `wave-nineteen-mitm-two-races.trx`. Huit nouvelles répétitions du groupe de 12 : **96/96 PASS, toutes exit 0**, `validation-20261114-wave-nineteen-mitm-group-repeat.log`. Campagne complète hors Live recompilée : **6804 PASS / 0 FAIL / 2 ignorés**, exit 0, durée 2 min 31 s, `validation-20261114-wave-nineteen-full-nonlive.log` et `wave-nineteen-full-nonlive.trx`. Ceci valide la correction de fixture et non les performances Live ni toutes les courses possibles du transport.

**Protections et limites.** Aucune règle de reprise anti-replay modifiée : `ActivityRecovery.Pending`, V9 Started/Uncertain, taxes et ACK attribuables restent fail-closed. Aucun client ou compte étranger touché, aucune commande Git destructive ou publication. Pas de certification Live V19 ; le projet global reste incomplet.

**Contrôle final V19.** HEAD `b4d28a8` inchangé, 983 entrées dirty préservées (source/test/logs compris). `git diff --check` ciblé `MitmLifecycleV15Tests.cs` exit 0. Global exit 2 sur les quatre anomalies whitespace historiques inchangées (`SnowMap.cs:24`, `Soul.cs:134`, `TurboFightPolicy.cs:391`, `GivreClone.App.csproj:184`). Aucun processus `dotnet` ou `testhost` actif en fin de campagne. Le journal V19 a été ajouté à la fin sans réécrire les sections précédentes.

### V19 — publication et validation Live Socket/MITM contrôlée (14 novembre 2026)

**Autorisation et profil original.** L'utilisateur a autorisé explicitement les essais Live, déconnexions, arrêt des scripts et de l'ancienne application. Le profil initial comportait cinq comptes dont quatre actifs. Le contrôle IPC a observé quatre scripts en cours, puis emergency_stop a demandé leur arrêt; un second relevé a confirmé zéro script et zéro combat. Les quatre disconnect_bot ont été acceptés et le roster a confirmé zéro compte en ligne. Les applications GivreClone PID 9844 et premier QA PID 17800 ont été fermées proprement après vérification du chemin exécutable exact, sans tuer de processus étranger. Le fichier accounts.json du profil original est conservé avec cinq comptes, AutoConnectType 2,2,2,2,0 et AutoStartScript true,true,true,false,false.

**Publication et isolement.** dotnet publish Release win-x64 du code V19 a réussi avec exit 0, sans remplacer l'ancienne installation, dans artifacts/v19-isolated-live-smoke-20261114/publish. SHA256 du lanceur et de la DLL dans validation-20261114-v19-live-smoke.log. Un nouveau run-name v19-qa-empty-20261114 et un pipe dédié ont confirmé zéro compte et zéro autoconnect. Après fermeture du premier QA, une seule entrée de compte a été copiée dans ce profil de test privé, auto-connect=0 et auto-script=false; l'empreinte du fichier original a été vérifiée inchangée. Aucun secret n'est consigné dans ce journal.

**Socket Live.** Le QA PID 23524 a accepté la connexion Socket d'un seul personnage. La lecture indépendante a confirmé ingame, map 97255943, cellule 338, géométrie prête et inventaire 34 piles, sans combat ni script. La déconnexion a été confirmée puis une nouvelle connexion a créé une nouvelle identité virtuelle. Une lecture initiale après reconnect indiquait map 0 (transitoire), puis un relevé a confirmé map 97255943 et cellule 338. Aucune tentative ambiguë rejouée.

**MITM Live.** Après déconnexion du Socket, connect_bot mode mitm a lancé le vrai Dofus.exe PID 26768, dont la fenêtre affichait Dofus 3.7.13.17. Le premier relevé portait map 0, donc pas de preuve world-ready prématurée. Le choix du serveur Kourial dans le client a permis de rejoindre la Mine Istairameur. Givre V19 a alors confirmé séparément map 97255943, cellule 338, cellConfirmed true, cellsReady true, 8 interactifs et inventaire 33 piles, sans script ni combat. Les sondes purement consultatives probe_navigation sur cellules 324 et 352 ont chacune trouvé un trajet de 1 pas avec sent=false. Ce sont des observations de source, pas des reçus de commande.

**UI métiers et autorité.** La nouvelle interface V19 a exposé le vrai bouton Coach métiers - Diagnostiquer les preuves hôte V18 (lecture seule). Son activation a affiché HOST_PROOFS_MISSING_OR_UNVERIFIABLE et mutation interdite, notamment les lacunes V9, identité ActionKey, stock certifié, autorité indépendante de reçu, PID/RunId, reçu craft et lease de mutation. Le diagnostic MITM est resté local-tunnel-source-observed-only avec correlatedServerAck=false et canAuthorizeGameplay=false; clientVersionVerified=false au niveau de preuve hôte malgré le titre graphique versionné. Aucun cast, craft, achat, transfert bancaire, trajet automatisé ou ACK synthétique n'a été déclenché. Le bouton retour banque atelier avec objectif certifié n'a pas été accepté Live faute de preuve/objectif actuel.

**Clôture propre.** disconnect_bot MITM confirmé offline avec PID bot 0, processus Dofus.exe 26768 arrêté, puis application QA 23524 fermée par CloseMainWindow. La copie temporaire des identifiants du run QA a été supprimée après fermeture, sans changer le profil original. Contrôle final zéro GivreClone et zéro Dofus. Les quatre comptes initialement actifs restent déconnectés volontairement, aucun script n'est repris. La Release V19 reste sur disque. Ces preuves Live valident le login Socket, le reconnect, le lancement MITM visible, l'entrée en jeu et la lecture de source, mais PAS la causalité individuelle request-vers-ACK ou le parcours complet combat/métiers/HDV. Le projet n'est pas globalement terminé.

**Contrôles finaux V19 Live après extinction QA.** HEAD b4d28a80d71e6b98a95f6bb1d17c6dbead80b660, 985 entrées dirty préservées, diff --check ciblé exit 0, diff --check global exit 2 uniquement pour les quatre anomalies historiques SnowMap.cs:24, Soul.cs:134, TurboFightPolicy.cs:391 et GivreClone.App.csproj:184. Le reçu validation-20261114-v19-live-smoke.log existe (41 lignes), la DLL Release existe et possède SHA256 003D7BE7994049FCD25ED40164F14CE12C538B6691B4E5903FEEE7C1AFCB1D47. Zéro processus Dofus/GivreClone/dotnet/testhost au contrôle final, aucune copie temporaire de credentials QA, cinq comptes préservés dans le profil d'origine, aucun auto-redémarrage des scripts. Pas de certification de gameplay Live complète.
### V20 — correlation d'enveloppes UID Socket et retraite de session (14 novembre 2026)

**Perimetre et statut.** Continuation de V19 dans `fix-f01-f16-final-20261024`, HEAD `b4d28a80d71e6b98a95f6bb1d17c6dbead80b660` inchange. Toutes les modifications existantes restent dirty, sans reset/clean/stash/commit/push et sans interruption de Dofus/MizanBot/compte tiers. V20 a ete valide par tests hors jeu, et publie en Release isolee, mais **aucune preuve d'ACK gameplay Live V20** n'a ete collectee. Le binaire V19 demeure intact et distinct.

**Diagnostic V20 strictement consultatif.** Nouveau `GivreClone.App/Services/SocketUidEnvelopeCorrelationV20.cs`, raccorde aux callbacks reels `LaunchService.FeedSocketWrittenObservation` (apres ecriture+Flush local du producteur Socket) et `FeedSocketObservedFrame` (trame serveur sur la meme source). Le parseur lit un GameMessage protobuf length-prefixed unique, branche Request/Response exacte et `uid` int64 strictement positif, champ content present, catalogue de champs, sans event, UID absent, UID 0/-1, doublon, sequence ou ordinal rejoue, PID ou tunnel recycle. L'identite du lien inclut instance source, PID, run epoch, tunnel et connection token. Un UID reemploye invalide la correlation ; les projections sont bornees. `SocketUidEnvelopeEvidenceOf` et `socketEnvelopeUidV20` exposent les compteurs sans toucher au moteur, aux journaux, aux mutations ni a l'interface d'ACK historique. `GameplayOutcomeVerified=false`, `CanAuthorizeGameplay=false` et `correlatedGameplayAck=false` restent vrais en toute circonstance. Le matching de UID indique seulement une reponse enveloppe provenant de ce lien, jamais le succes d'un cast, deplacement, craft, achat, taxe ou transfert.

**P1 decoder : vieilles callbacks.** `CaptureDecoder.Feed` et `Forget` serialisent encadrement, delivery des handlers et retraite de l'epoch locale par verrou de livraison PID, avec revocation reentrante verifiee avant chaque abonne. `LaunchService.OnDecoded` refuse un DecodedPacket estampille dont l'epoch locale n'est plus courante, avant reducers et de nouveau apres PacketDecoded reentrant. L'epoch locale n'authentifie PAS l'identite TCP ; les chemins source-bound restent distincts. Tests directs `CaptureDecoderRetiredEpochV20Tests.cs` et `SocketUidEnvelopeCorrelationV20Tests.cs` ecrits sur de vrais frames protobuf synthetiques (non Live). Les nouvelles lectures du ledger dans `LaunchService.Packets.cs` ont des gardes null-safe pour les fixtures anciennes instanciees sans constructeurs.

**Regressions corrigees par preuves.** La toute premiere campagne globale V20 (ancien binaire) echouait sur 24 tests, dont NRE de fixtures non initialisees ; une recompilation du code source actuel a reduit le lot de regressions a 49/50 PASS. L'unique assertion statique restante dans `InstalledDofusVersionBindingTests.cs` attendait les anciennes chaines `RegisterInjector`/`UnregisterInjector` au lieu de la veritable admission `RegisterInjectorWithAdmission` sous `_sessionOwnership.TryRegister(lease,...)`, et de la liberation `_sessionOwnership.Release(lease, Stores.Launch.UnregisterSocketInjector)` ; actualisee en conservant toutes les gardes. Le rebuild suivant passe 63/63.

Trois intermittences de *fixture* ont ete observees lors des reprises globales, sans changement d'autorite metier :
- `SpeedHackCombatLeaseTests.Enhanced_preflight_failure_does_not_republish_native_state` : le process prive inerte `cmd.exe` copie sous le nom Dofus.exe avait quitte, mais Windows tenait encore brièvement son image ; `LeaseFixture.DisposeAsync` retente la suppression du seul dossier de fixture borne 20 fois / 25 ms, puis echoue si le verrou persiste. Assertions d'etat natives inchangees, 10/10 repetions isolees PASS.
- `MitmLifecycleV16Tests.Replay_sends_original_validated_snapshot_and_rechecks_permission(true)` : le test maintenait un semaphore TCP puis attendait que le thread de test se reveille avant que la production `gate.Wait(1500)` expire ; test redessine avec callback synchrone APRES la premiere decision de permission latchee et AVANT la seconde verification sous vrai gate. Validation du snapshot immuable, nouvelle permission, refus si revoked et absence de send conservee ; aucun timeout du produit augmente.
- `ActivityRouteProductionV14Tests` : `Generated_guided_gid...` pouvait demarrer son timer avant `MainScriptStarting` sous charge (`RunCompletion=WaitingForActivation`), et `Late_actor_republication...` pouvait lire la queue avant le callback would-send a cause de `Task.Delay(350)`. Les tests attendent desormais l'evenement reel `MainScriptStarting` et un `TaskCompletionSource` declenche dans le callback reel, puis verifient tous les logs, zero send / marker ou `Pending` selon scenario, sans faux ACK, sans delai arbitraire d'assertion. Familles MITM/Lua event-driven : 33/33 PASS et 8 series de stress PASS.

**Validation .NET sequentielle avec vrais codes retour :**
- UID/V14 initial : 20/20 PASS, exit 0, `validation-20261114-v20-uid-targeted.log`. Stale decoder premier build : erreur CS1003 fixture corrigee, exit 1 ; rebuild : 24/24 PASS exit 0, `validation-20261114-v20-epoch-rebuild2.log`.
- Premiere full avant rebuild final : 6891 PASS / 24 FAIL / 2 ignored, exit 1, `validation-20261114-v20-full-nonlive.log`; ne pas la masquer.
- Rebuild regressions : 49 PASS / 1 FAIL test statique, exit 1, `validation-20261114-v20-regressions-rebuild.log`. Apres fix : 63/63 PASS exit 0, `validation-20261114-v20-regressions-rebuild2.log`.
- Full recompilee initiale : 6915 PASS / 0 FAIL / 2 ignored, exit 0, `validation-20261114-v20-full-nonlive-rebuild2.log`.
- Confirmation full : 6914 PASS / 1 FAIL cleanup process fixture Windows / 2 ignored, exit 1, `validation-20261114-v20-full-nonlive-confirmation.log`. Apres fix : 40/40 ciblés PASS, exit 0, `validation-20261114-v20-speedgate-cleanup-targeted.log` et stress test 10/10 iterations PASS, `validation-20261114-v20-speedgate-cleanup-repeat.log`.
- Full suivante : 6912 PASS / 3 FAIL intermittents MITM/Lua / 2 ignored, exit 1, `validation-20261114-v20-full-nonlive-final.log`. Apres barrières de fixture : 33/33 ciblés PASS exit 0, `validation-20261114-v20-event-barriers-targeted.log`, 8/8 series de stress PASS exit 0, `validation-20261114-v20-event-barriers-stress.log`.
- **Full finale apres dernier rebuild** : **6915 PASS / 0 FAIL / 2 ignored, total 6917, duree 2m45, exit 0**, `validation-20261114-v20-full-nonlive-postbarriers.log`, TRX `GivreClone.Tests/TestResults/v20-full-nonlive-postbarriers.trx`.
- `GivreClone.PureTests` **363/363 PASS exit 0**, `validation-20261114-v20-pure-tests.log`; `CombatShadowComparison` **65 assertions hors jeu PASS exit 0**, `validation-20261114-v20-shadow.log`.

**Release V20 isolee.** `dotnet publish GivreClone.App/GivreClone.App.csproj -m:1 -c Release -r win-x64 --self-contained false -o artifacts/v20-isolated-uid-20261114/publish` a retourne **exit 0**. `GivreClone.exe` SHA256 `C9BE6103D1D1F7C63C77063890B2B5F0DA9597D3D50494CFF22D0C240C828FB4`; `GivreClone.dll` SHA256 `ABEEA11B154CF90312ACD09CB5A2FAC2BAD2BC122DE1663F866145C529784684` (contre V19 DLL `003D7BE7994049FCD25ED40164F14CE12C538B6691B4E5903FEEE7C1AFCB1D47`). Le recu `validation-20261114-v20-isolated-publish.log` conserve l'erreur du premier chemin de hash `GivreClone.App.dll` inexistant et sa correction vers `GivreClone.dll` ; le code de publication lui-meme est 0. Aucun binaire en production ni comptes utilisateur remplaces.

**Limites ouvertes, aucun ACK invente.** `data/bundle-build.json` decrit 3.7.11.11 et `data/compatibility-review.json` revoit 3.7.12.12 ; dernier client observe Live V19 affiche 3.7.13.17. Ni schema 3.7.13.17 exact ni reponse `Response.uid` positive a une commande gameplay identifiee ne sont attestes Live en V20. La plupart des commandes gameplay ont UID -1 ou n'exposent pas de cle de requete reprise par l'event : `GameActionAcknowledgementRequest` est C->S et n'est PAS ACK serveur ; `MapMovementConfirmResponse` n'a pas de ticket attribuable ; BuyResultEvent.object_uid est UID d'offre et non ID de tentative. `ActivityRecovery.Pending`, V9 Started/Uncertain, anti-replay, taxes et mutations professionnelles restent fail-closed. Les audits ont identifie d'autres P1 potentiels a traiter separement avec tests causaux : `LocalHandshakeRelay.PumpAsync` peut publier le chunk brut client alors que `UpstreamFrameGate` a retire des frames ; `MapMovementConfirmResponse` peut affecter des pending du meme PID sans ticket exact. Aucun de ces deux points n'est declare resolu par V20. Pas de smoke Live V20, aucune mutation gameplay attestee ; la validation V20 reste hors jeu et le projet global demeure incomplet.