# MZP2 — admission du client de test propre et connexion au host

**Livraison : coordinateur réel côté App, tests intégrés hors ligne. Statut production : BLOQUÉ tant qu'un appelant ne peut pas établir indépendamment identité et réponse positive serveur et tant que l'adaptateur MZP2 n'utilise pas le contrat hôte/sink attesté. Aucun client, processus ou identifiant n'a été ouvert durant le développement.**

## Ce qui est livré

`GivreClone.App/Services/MZP2OwnedHostAdmission.cs` définit :

- `MZP2OwnedHostAdmission` : état d'onboarding (`Dormant`, `Denied`, `AwaitingOwnedCapture`, `SessionAdmitted`, `ReconciliationRequired`, `Faulted`, `Stopped`), diagnostics uniquement catégoriels, démarrage **explicite** et arrêt de son propre hôte ; pas de recherche globale de clients ni d'actions implicites.
- `MZP2TestClientAdmissionRequest` : approbation explicite, vrai `MZP1OwnedClientPermit` (PID, ticks de naissance, Dofus `3.7.10.10`, chemin EXE exact et SHA-256), ports Engine réels, répertoire de journal dédié absolu et **sélection obligatoire** de `MZP2IsolatedTestHostConfiguration`. **Aucun compte, mot de passe, personnage ou frame n'est accepté.**
- `MZP2IsolatedTestHostConfiguration` : approbation supplémentaire spécifique au relais/stockage de test, port opérateur explicite entre 1024 et 65535 sauf 5657/5658, chemin JSON absolu strictement descendant de `%LOCALAPPDATA%/GivreReplica/TestRuns/`. Le chemin n'est ni déduit, ni créé, ni lu : il est uniquement validé puis transmis à `BotHostService.Start(permit, factory, testRelayPort, testAccountStorePath)`. Les répertoires déjà existants et le fichier sont refusés s'ils sont des liens de réanalyse Windows.
- `MZP2TrustedEnginePorts` : `ProtocolCatalog`, `IFrameSink` de la connexion, `IPacer` et les deux autorités distinctes `IMZP0AuthenticatedSessionSource` et `IMZP0PositiveResponseVerifier`. Le type transporte des dépendances ; il **ne fabrique ni authentification ni preuves**.
- `IMZP2OwnedHostPort` : adaptateur de cycle de vie explicite de **l'hôte détenu par ce coordinateur**. `MZP2DedicatedBotHostPort` contient une **nouvelle instance privée** du vrai `BotHostService` ; le coordinateur n'accède jamais au service global de GivreClone.

`StartOwnedTestHost(request)` refuse avant tout démarrage si : approbation absente (**y compris celle de la configuration de test**), permit mal formé, port réservé/hors plage, chemin de compte absent, relatif, hors racine `TestRuns`, à extension non JSON, via lien de réanalyse, origine PID-naissance/connexion/version non attestée ou discordante, autorités identiques, journal indisponible, journal comportant une opération encore `Prepared`, `DispatchWindowOpened` ou `Uncertain`, ou liaison hôte/sink non vérifiable. L'annulation par `StopOwnedTestHost()` ne vise que le port dont le démarrage a effectivement été obtenu par cette instance ; un échec d'arrêt conserve le verrou du journal et reste visible comme `Faulted`.

La configuration opérateur est conservée **uniquement pendant la durée de vie** de l'hôte propre et effacée à l'arrêt. `MZP2AdmissionDiagnostics` expose seulement `SelectedTestRelayPort` et le booléen `IsolatedAccountStoreSelected` ; il n'expose pas le chemin local, l'identité PID ni une éventuelle donnée de compte. Aucun chemin `%APPDATA%/GivreClone/accounts.json` ni port 5658 n'est jamais choisi par défaut.

La clé du journal est stable et déterministe par PID-naissance, connexion, version client et SHA EXE, via SHA-256. Aucun champ de compte ni trame n'y est écrit. Le fichier `.journal.json` est loué exclusivement via `MZP0RecoveryJournal`. Un redémarrage avec opérations non résolues donne **`ReconciliationRequired` sans lancement ni replay**, même si un envoi avait retourné `Sent`. Seule une réconciliation explicite hors de ce coordinateur pourra autoriser une nouvelle session ; le coordinateur n'infère ni ACK ni absence d'effet à partir d'un timeout.

Dans le seul harness **hors ligne**, un `FakeHost` qui possède ses propres ports attestés fournit la liaison et reçoit les paramètres `(permit, factory, testConfiguration)`. Son callback `Func<CapturedMessage,MZP1GuardedEngineWiring>` revérifie la source indépendamment du contenu de la capture. La capture doit correspondre au PID et à la connexion autorisés ; sinon elle est rejetée sans activer de session. Il appelle ensuite la **véritable** `MZP1GuardedEngineWiringFactory.Create(...)`, et exige `Admission == Ready`. Aucun repli vers `ActionSender` non protégé. Ce succès synthétique n'est pas une preuve de source live.

## Maillon de production réellement manquant (PRIME)

Les signatures sources actuelles sont :

```csharp
// GivreClone.App/Services/BotHostService.cs
void Start(MZP1OwnedClientPermit? ownedClient,
    Func<CapturedMessage, MZP1GuardedEngineWiring>? guardedSessionFactory,
    int testRelayPort = 0, string? testAccountStorePath = null);

void StartBound(MZP1OwnedClientPermit? ownedClient,
    Func<CapturedMessage, ProtocolCatalog, IFrameSink, IPacer,
        MZP1GuardedEngineWiring>? guardedFactory,
    int testRelayPort, string? testAccountStorePath);

// GivreClone.Engine/Recovery/MZP1GuardedEngineWiring.cs
MZP1GuardedEngineWiringFactory.Create(int expectedConnectionId,
    string requiredProtocolVersion, ProtocolCatalog catalog,
    IFrameSink sink, IPacer pacer, MZP0RecoveryJournal? journal,
    IMZP0AuthenticatedSessionSource? identityAuthority,
    IMZP0PositiveResponseVerifier? responseAuthority);
```

La chaîne legacy `BotHostService.Start` crée **privément** son `BotHost`, lequel crée `ProtocolCatalog` et `TcpProxyServer`; son ancien callback ne reçoit que `CapturedMessage`. Un `IFrameSink` fourni de l'extérieur pourrait provenir d'une autre instance et ne prouve jamais à lui seul la propriété de la connexion. **PRIME a depuis ajouté `BotHostService.StartBound` et la route `BotHost`/`BotOrchestrator` à quatre arguments**, capables de fournir le vrai catalogue, le vrai sink de connexion et le pacer issus de l'hôte qui les possède. Ce contrat est préparé côté MZP2 par `MZP2HostBoundSessionFactory`, de signature strictement concordante. Il ne prouve toutefois ni l'identité indépendante du producteur, ni un ACK serveur.

**Par conséquent, `MZP2DedicatedBotHostPort.CanBindProtectedEngine(...)` retourne actuellement `false` de manière délibérée.** Même avec un permit et des autorités *déclarées*, il retourne `HostTransportBindingUnavailable`, avant ouverture du journal, lecture du compte ou `BotHostService.Start()`. La simple présence d'une nouvelle surcharge `StartBound` ne certifie pas la véracité d'autorités externes. MZP2 ne se présente **pas** comme prêt pour une session live.

**Prochaine jonction sous un futur scope PRIME, sans toucher aux instances en cours :** adapter le coordinateur au vrai chemin `BotHostService.StartBound` **exclusivement**, en récupérant les ports host-bound au moment de l'admission et en vérifiant identité du host, du proxy et de la connexion ainsi que le permit, jamais uniquement les types, noms ou valeurs d'un sink fourni par l'appelant. Les deux autorités doivent être indépendamment vérifiées. L'instance hôte contrôlée reste propriétaire de son cycle de vie. Cette activation est **hors périmètre du présent lot** ; aucun `BotHostService`, `BotHost`, `BotOrchestrator`, `SessionBinder`, `BotSession`, `DofusCaptureHost` ou `.csproj` existant n'a été édité par worker-2.

Le même futur scope devra fournir de véritables producteurs pour **deux preuves indépendantes** : (1) naissance PID, chemin/SHA et bind transport/connexion pour le permit, (2) réponse positive réelle provenant du serveur, liée à la génération et l'UID. `MZP1OwnedClientPermit.IsWellFormed` valide seulement la forme ; même sa valeur exacte n'est **pas** une preuve autonome de propriété. Les garanties de naissance/path/SHA présentes dans `DofusCaptureHost.Poll` ne remplacent pas l'identité de réponse serveur. Il n'existe encore aucune preuve de gameplay, d'ACK live ni de parité binaire de GivreClone v1.3.1.

## Test hors ligne (pas de client/proxy/compte)

```powershell
dotnet test tools/MZP2HostAdmissionHarness/MZP2HostAdmissionHarness.csproj -c Debug
dotnet test tools/MZP2HostAdmissionHarness/MZP2HostAdmissionHarness.csproj -c Release
```

Le harness lie directement les **sources de production** `BotHostService` (y compris `StartBound`), MZP2, MZP1, MZP0, `ActionSender`, `FrameSink`, `PacketBus`, `BotContext`, `BehaviorHost`, protocole et permit. **Seuls** les types externes `GivreCore` et la construction de `BotHost` sont remplacés par des substituts **sous `tools/` exclusivement** ; le `BotHost` substitut jette une exception s'il était construit, empêchant toute session live. Un `FakeHost : IMZP2OwnedHostPort` appartenant uniquement aux tests permet de vérifier le contrat de raccord MZP1, les cas négatifs, la propagation du port/chemin exclusivement sélectionnés, le journal exclusif, le refus de reprise après une tentative non résolue, les captures de mauvais PID ou connexion, la révocation et les échecs d'arrêt partiel. Ce port de test n'est **pas** une preuve de transport ou un provider de production.

Ce test de contrat n'implique **aucune** injection ou connexion et ne confirme ni l'existence d'un sink live utilisable ni des ACK réels. Seule l'obtention de la dépendance `GivreCore` et d'autorités indépendantes pourra permettre la prochaine validation du host complet, avec autorisation explicite de son client de test.
