# GivreClone — journal fiscal HDV résistant aux redémarrages

Continuation du 13 novembre 2026, travail strictement hors jeu.
Départ vérifié : `b4d28a8`, suite antérieure 5 870 réussites hors Live,
2 ignorés, 362 PureTests, 65 assertions shadow.

## Changements dans le code réel

- `HdvTaxReservations.TryReserveOrder` offre une réservation liée à
  l'intention locale **profil serveur+personnage, PID observé, UID de
  vente/reprix, quantité, prix et stock observé pour une vente**. Un
  token de run local est inscrit dans chaque ligne pour le diagnostic ;
  il n'est jamais confondu avec une session serveur ni un ACK.
- Le journal `<DataRoot>/hdv/tax-reservations-v1.journal` utilise une
  écriture append-only et `FileStream.Flush(flushToDisk: true)` avant
  que le ViewModel puisse appeler `HdvSellAsync` ou
  `HdvRepriceAsync`. Chaque ligne possède une empreinte SHA-256.
  Le contrôle de capacité lit **tous** les coûts réservés à ce profil,
  y compris d'autres PID/runs et les sessions précédentes, sous
  ouverture exclusive du fichier ; deux processus locaux ne peuvent
  pas consommer simultanément le même budget.
- Le fichier `.anchor` est créé avant la première émission et
  recense le nombre de lignes et l'empreinte finale après chaque
  synchronisation du journal. Une troncature de ligne, une suppression
  de l'un des deux fichiers, une divergence d'empreinte ou un
  enregistrement invalide font **refuser l'émission**, sans effacer
  les octets corrompus et sans fallback en mémoire.
- Le même orderKey est reconnu à la reprise : aucune nouvelle taxe
  n'est réservée, et **l'envoi est bloqué**. Une nouvelle vente de la
  même pile doit avoir un stock différent observé ; un nouveau
  repricing doit disposer d'un UID/prix de lot distinct. Un doute
  reste bloquant jusqu'à vérification indépendante.
- `HdvViewModel` utilise ce chemin pour la vente manuelle, le
  repricing manuel, la sous-cote et le watcher. `HdvViewModel.Bulk`
  y réserve après relecture UID/quantité sous `Gated`.
- La prévisualisation du plafond tient compte des réservations
  durables et signale un journal indisponible dans la langue de
  l'interface (FR/EN/ES). Elle reste indicative ; le contrôle
  d'émission relit le journal sous verrou.

## Sémantique fail-closed

Un timeout, une réponse ambiguë, un échec d'envoi après persistance ou
une hausse du compteur `TotalTax` ne libèrent **aucune** réservation.
Il n'existe aucun reçu fiscal serveur attribuable à l'UID qui permette
aujourd'hui de chiffrer une libération. Un crash après écriture,
avant émission, peut donc immobiliser une partie du plafond : c'est
volontaire et conservateur, pas un ACK inventé.

La configuration historique `MaxTaxSpend=0` reste illimitée et ne
réserve pas de frais ; l'utilisateur garde la possibilité explicite
de la choisir. Le contrat strict vaut lorsque le plafond est positif.
Un profil sans serveur/niveau de preuve suffisant est refusé, sans
deviner la portée d'un nom de personnage.

## Scénarios de tests

`GivreClone.Tests/HdvTaxDurableRecoveryTests.cs` couvre notamment :
redémarrage après réservation sans émission, déduplication UID,
changement de PID et séparation serveur, plafonds et réservations
agrégées, corruption de checksum, écriture partielle, perte du
journal/de l'ancre, troncature à une **ligne complète**, crash entre
ancre et premier enregistrement, permission d'écriture refusée,
concurrence entre instances, et invalidité des entrées.

## Limites non résolues

- L'ancre et le journal protègent les interruptions usuelles et la
  perte **d'un** fichier, pas l'effacement simultané et coordonné des
  deux ni la corruption physique silencieuse qui réécrirait les deux
  de façon cohérente. Une preuve serveur externe serait requise.
- Aucune libération automatique n'est possible sans reçu de taxe
  causal. Le journal grossit ; au-delà de 16 MiB ou en cas de
  contradiction il se bloque plutôt que de l'élaguer sans preuve.
- Les ordres envoyés par un autre outil, une autre vue ou un script
  sans passage par ce ViewModel ne sont pas couverts.
- Les GUID sont uniquement des identifiants de journal client.
  Ni état serveur, ni cours HDV, ni réponse protocolaire fabriqués.
- Le système est local au `MZP19LocalClone.DataRoot`; un changement
  intentionnel de répertoire de données représente un nouveau
  périmètre. Aucun jeu ni MizanBot n'a été lancé.

## Validation

À compléter uniquement après les sorties effectives des nouvelles
suites de tests et le build de l'ensemble des workers. Les résultats
5 870/2, 362 et 65 sont le point de départ **antérieur**.
