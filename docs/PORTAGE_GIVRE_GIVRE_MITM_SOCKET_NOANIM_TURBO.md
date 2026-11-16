# Portage Givre -> Givre : MITM / Socket hybride, bypass d'animations et Turbo combat

Date : 2026-10-05  
Cible de reference : Dofus `3.6.12.16`

Ce document decrit un portage **fonctionnel** des idees validees dans Givre vers Givre. Il couvre trois sujets qui doivent rester separes dans le code :

1. garder un client Dofus visible attache pendant qu'on remplace/reconnecte l'upstream de jeu ;
2. accelerer ou supprimer les animations **dans le client**, sans supprimer les evenements reseau qui portent l'etat autoritaire ;
3. ajouter un mode **Turbo combat** qui retire les attentes artificielles tout en conservant les barrieres serveur (`FightTurnEvent`, `SequenceEndEvent`, ACK, confirmation de mouvement, etc.).

Le point central est le suivant : **le serveur reste l'autorite**. Le client visible est une vue/presentation et une source eventuelle d'inputs manuels. Le moteur Givre garde l'etat, les decisions et la synchronisation protocolaire.

---

## 1. Etat actuel : ce qui existe deja dans Givre

Givre a deja presque toutes les briques de base. Il faut surtout les reunir sous une session hybride stable.

### 1.1 Mode MITM

Le compte memorise deja son mode dans :

`GivreClone.Engine/Accounts/Account.cs`

```csharp
public enum ConnectionMode { Mitm = 0, Socket = 1 }
```

Le chemin MITM visible utilise :

- `GivreClone.App/Services/Transport/MitmTransport.cs`
- `GivreLauncher.Core.Transport.HandshakeRelay` (repo voisin, non inclus dans cette copie partagee)
- `native/GivreKama/GivreKama.cpp`

`GivreKama.dll` intercepte `ws2_32!connect`. Quand Dofus tente de joindre un GameService sur le port `5555`, la DLL remplace la destination par `127.0.0.1:5658`, puis envoie au relais un handshake de 26 octets contenant l'adresse originale et le PID.

Le coeur du hook existe deja :

```cpp
if (mapped && ntohs(t->sin6_port) == 5555)
{
    Handshake h;
    memcpy(h.Address, a, 16);
    h.Addrlen   = namelen;
    h.Port      = ntohs(t->sin6_port);
    h.ProcessId = GetCurrentProcessId();

    // ::ffff:127.0.0.1:5658
    memset(&t->sin6_addr, 0, sizeof(t->sin6_addr));
    a[10] = 0xFF; a[11] = 0xFF;
    a[12] = 127; a[13] = 0; a[14] = 0; a[15] = 1;
    t->sin6_port = htons(PROXY_PORT);

    int r = RealConnect(s, name, namelen);
    send(s, (const char*)&h, sizeof(h), 0);
    return r;
}
```

Il n'est donc pas necessaire de refaire la redirection de connexion. Le portage doit agir **apres** cette adoption du socket visible, au niveau du relais.

### 1.2 Mode Socket clientless

Le mode Socket existe deja dans :

- `GivreClone.App/ViewModels/AccountsViewModel.Socket.cs`
- `GivreClone.App/Services/Transport/Socket/SocketProbe.cs`
- `GivreClone.App/Services/Transport/Socket/SocketGameClient.cs`

Il utilise un PID synthetique, ouvre lui-meme la connexion Ankama, selectionne serveur/personnage, publie ses frames au meme decodeur et expose un injecteur a `LaunchService`.

Le seam commun existe deja dans `LaunchService.cs` :

```csharp
private readonly ConcurrentDictionary<uint, Func<byte[], bool>> _socketInjectors = new();

public void RegisterSocketInjector(uint pid, Func<byte[], bool> inject)
    => _socketInjectors[pid] = inject;
```

Le moteur de combat n'a donc pas besoin de savoir si une action part par un client visible ou par un socket clientless.

### 1.3 Mods natifs / bypass visuel

Givre a deja le controle par PID de `GivreMods.dll` :

`GivreClone.App/Services/ModControl.cs`

```csharp
public static void WriteForPid(
    int pid,
    bool noAnim,
    bool tactical,
    bool collapse = false,
    bool instantMove = false)
{
    File.WriteAllText(FileFor(pid),
        $"skipper={(noAnim ? 1 : 0)}\r\n" +
        $"tactical={(tactical ? 1 : 0)}\r\n" +
        $"collapse={(collapse ? 1 : 0)}\r\n" +
        $"instantmove={(instantMove ? 1 : 0)}\r\n");
}
```

Les quatre options sont deja portees par `CombatProfile` et poussees dans `LaunchService.SetCombatProfile()`.

Le code source reel de `GivreMods.dll` est dans le repo voisin `GivreLauncher/native/GivreMods`, absent de `GivreShareWork`. Il faut donc appliquer la partie native de ce guide dans ce repo d'origine, pas inventer des offsets dans `GivreShareWork`.

### 1.4 Barrieres serveur deja presentes

Givre a deja des gardes solides dans :

`GivreClone.App/Services/LaunchService.Combat.cs`

- `FightTurnEvent` definit la vraie fenetre de tour ;
- `FightTurnStartPlayingEvent` complete le debut du tour ;
- `SequenceEndEvent` ferme les sequences ;
- `WaitMoveClear()` interdit les mouvements pendant une sequence encore ouverte ;
- apres un mouvement, Givre relit `SelfCell()` depuis les evenements serveur au lieu de predire la position ;
- un refus explicite interrompt l'attente rapidement.

Ces barrieres doivent rester actives en Turbo.

---

## 2. Ce qu'il faut reprendre de Givre

Les parties utiles de Givre ne sont pas un simple `if (mode == socket)`. Givre separe trois proprietaires :

```text
                      +----------------------------+
 Dofus visible  <---->|  downstream visible       |
 (presentation/input) |  PID + generation         |
                      +-------------+--------------+
                                    |
                          relay / ownership
                                    |
                      +-------------v--------------+
                      | upstream autoritaire       |
                      | generation N               |
                      +-------------+--------------+
                                    |
                              serveur Ankama

             moteur/decoder observe les frames autoritaires
```

Le downstream visible peut etre temporairement suspendu **sans tuer le process**, pendant que l'upstream est remplace.

Les references Givre utiles sont :

| Fonction | Fichier Givre | Idee a porter |
|---|---|---|
| `TrySuspendOwnedVisibleGameClientForUpstreamReconnect()` | `EasyBot/Sockets/SwitchSocket.vb` | figer le client visible exact avant de remplacer l'upstream |
| `BeginPreservedReconnectCatchupForServerPipeline()` | idem | rattacher le buffer a une generation upstream precise |
| `BufferPreservedReconnectInboundForCatchup()` | idem | buffer borne des frames du nouvel upstream |
| `FlushPreservedReconnectCatchupAndResume()` | idem | replay + revalidation des generations + reprise |
| `TryPassthroughClientGameFrame()` | `EasyBot/Sockets/MITM.vb` | envoyer une frame client brute sans la reencoder |
| `Reconnexionauto()` / `ReconnexionautoForScript()` | idem | suspendre le visible **avant** de reset l'etat monde/upstream |
| `hack957()` | `EasyBot/PacketAnalyser/PacketAnalyser.vb` | ACK immediat quand la sequence possedee se ferme en Turbo |
| `setCombatSpeedPreset("turbo")` | `.../BasicAI.vb` | tous les sleeps synthetiques a 0 |

Givre utilise actuellement un buffer de catch-up borne a `1024` frames et `16 MiB`. Ces valeurs sont raisonnables comme premiere valeur Givre ; elles doivent rester configurables et mesurees.

---

## 3. Architecture cible recommandee pour Givre

Le changement le plus important est de ne plus modeliser `MITM` et `Socket` comme deux mondes totalement exclusifs.

La session doit etre decomposable en :

```text
LogicalBotSession
  AccountId
  SessionGeneration
  GameState / decoder / Lua / combat

  AuthoritativeUpstream
    - RelayUpstream   (ancien MITM)
    - SocketUpstream  (SocketGameClient)

  OptionalVisibleClient
    - ProcessId
    - TcpClient/Stream gere par HandshakeRelay
    - VisibleGeneration
    - ForwardingState
```

Ainsi :

- un bot peut etre **Socket pur** : upstream actif, aucun client visible ;
- un bot peut etre **MITM classique** : client visible + relay upstream ;
- un bot peut etre **hybride** : `SocketGameClient` est l'upstream autoritaire et le client Dofus visible reste branche comme renderer/input ;
- remplacer l'upstream ne remplace pas le `GameState`, le script, la console, le profil de combat ni l'identite logique du bot.

Le PID Windows ne doit plus etre l'identite fondamentale d'une session. Garder le PID comme cle de compatibilite est possible dans un premier patch, mais ajouter au minimum :

```csharp
public readonly record struct BotSessionKey(
    string AccountId,
    long LaunchGeneration);
```

et conserver separement :

```csharp
public uint? VisibleProcessId { get; init; }
public uint TransportPid { get; init; } // PID reel ou PID synthetique legacy
```

Cela evite de perdre le bot quand on change de transport.

---

## 4. Phase A - exposer un controle fin du `HandshakeRelay`

Le point d'integration principal n'est pas `MitmTransport.cs`, qui ne fait qu'envelopper le relais. Il faut ajouter le controle dans le repo qui contient **`GivreLauncher.Core.Transport.HandshakeRelay`** ou dans `GivreCore`, selon l'endroit ou le socket downstream est effectivement possede.

Ajouter une interface de ce type :

```csharp
public enum VisibleForwardingState
{
    Forwarding,
    Suspended,
    CatchingUp,
    Retired,
}

public readonly record struct VisibleClientLease(
    uint ProcessId,
    long VisibleGeneration,
    long UpstreamGeneration);

public interface IVisibleClientBridge
{
    bool TrySuspend(uint pid, out VisibleClientLease lease);
    bool IsCurrent(in VisibleClientLease lease);

    bool TrySendRawUpstream(
        in VisibleClientLease lease,
        ReadOnlyMemory<byte> framedGameMessage);

    ValueTask<bool> TrySendRawVisibleAsync(
        in VisibleClientLease lease,
        ReadOnlyMemory<byte> framedGameMessage,
        CancellationToken ct = default);

    void Retire(in VisibleClientLease lease, string reason);
}
```

### Regles de generation

Deux compteurs sont necessaires :

```csharp
long _visibleGeneration;   // change si le downstream Dofus change
long _upstreamGeneration;  // change a chaque nouvelle connexion serveur
```

Chaque callback async capture ses generations au depart. Avant d'ecrire ou de valider une transition, il verifie qu'elles sont toujours courantes.

Exemple :

```csharp
private bool IsLeaseCurrent(VisibleClientLease lease)
{
    lock (_gate)
    {
        return _visible is not null
            && _visible.ProcessId == lease.ProcessId
            && _visibleGeneration == lease.VisibleGeneration
            && _upstreamGeneration == lease.UpstreamGeneration;
    }
}
```

Ne jamais rechercher un autre `Dofus.exe` pour sauver une session suspendue. Le lease appartient a **un PID et une generation exacts**.

---

## 5. Phase B - suspendre le visible avant tout remplacement upstream

L'ordre est critique.

Mauvais ordre :

```text
reset monde
fermer upstream
suspendre visible
```

Entre les deux premieres lignes, le relais peut fermer le downstream et la fenetre Unity disparait.

Ordre correct :

```text
1. prendre un lease du client visible exact
2. desactiver server -> visible
3. bloquer/filtrer client -> server selon l'ownership courant
4. seulement ensuite invalider l'etat transitoire upstream
5. fermer/remplacer l'upstream
6. armer le buffer de catch-up pour la nouvelle generation
7. reconstruire la session serveur
8. atteindre un snapshot autoritaire
9. rejouer le catch-up vers le visible
10. reprendre le forwarding
```

Pseudo-code :

```csharp
async Task<bool> ReplaceUpstreamAsync(BotRuntime bot, CancellationToken ct)
{
    VisibleClientLease? lease = null;

    if (bot.VisiblePid is uint pid &&
        bot.VisibleBridge.TrySuspend(pid, out var held))
    {
        lease = held;
    }

    // A partir d'ici seulement on touche a l'ancien upstream.
    long nextUpstreamGen = bot.BeginUpstreamReplacement();
    bot.ResetTransientTransportState();

    var socket = await ConnectNewGameUpstreamAsync(bot, ct);
    if (!bot.TryPublishUpstream(nextUpstreamGen, socket))
    {
        socket.Dispose();
        return false;
    }

    if (lease is { } l)
        bot.Catchup.Arm(l, nextUpstreamGen);

    return true;
}
```

La suspension doit etre idempotente. Deux erreurs upstream successives pendant la meme reconnexion ne doivent pas incrementer arbitrairement le lease ni perdre le visible deja conserve.

---

## 6. Phase C - catch-up borne puis reprise sur snapshot autoritaire

Pendant que le visible est suspendu, le nouvel upstream doit continuer a :

1. alimenter le decodeur Givre ;
2. reconstruire `GameState` ;
3. stocker les frames **wire brutes, dans l'ordre** pour le client visible.

Structure possible :

```csharp
public sealed class ReconnectCatchup
{
    public const int MaxFrames = 1024;
    public const int MaxBytes = 16 * 1024 * 1024;

    private readonly List<byte[]> _frames = new();
    private int _bytes;
    private bool _overflowed;

    public bool TryAdd(ReadOnlySpan<byte> frame)
    {
        if (_overflowed) return false;
        if (_frames.Count >= MaxFrames || _bytes + frame.Length > MaxBytes)
        {
            _frames.Clear();
            _bytes = 0;
            _overflowed = true;
            return false;
        }

        var copy = frame.ToArray();
        _frames.Add(copy);
        _bytes += copy.Length;
        return true;
    }
}
```

Le buffer doit etre lie a :

```text
VisibleGeneration
UpstreamGeneration
```

### Snapshot qui autorise une reprise

Ne pas reprendre sur « n'importe quelle frame recue ». Utiliser un evenement qui prouve que l'etat de la nouvelle session est coherent.

Ordre de preference :

- pre-world/reconnexion avant selection : `CharacterListEvent` autoritaire ;
- chargement monde : `ContextCreationEvent` + snapshot/carte autoritaire ;
- reconnexion en combat : snapshot de combat complet (`FightJoinEvent`/fighter sync + carte/positions/turn state selon le flux courant) ;
- retour monde apres combat : snapshot monde frais.

Dans Givre, la reprise pre-world sur `CharacterListEvent` a ete ajoutee precisement parce qu'attendre un snapshot monde etait un deadlock : on ne peut pas atteindre le monde tant que le client visible n'a pas pu selectionner son personnage.

### Flush transactionnel

Le replay doit copier le batch sous lock, ecrire **hors lock**, puis revalider les generations avant d'activer le forwarding.

```csharp
async Task<bool> FlushAndResumeAsync(
    VisibleClientLease lease,
    long expectedUpstreamGen,
    CancellationToken ct)
{
    byte[][] batch;

    lock (_gate)
    {
        if (!ValidateBeforeFlush(lease, expectedUpstreamGen))
            return false;

        batch = _catchup.Frames.Select(x => x.ToArray()).ToArray();
    }

    try
    {
        foreach (var frame in batch)
            if (!await _visible.WriteAsync(frame, ct))
                throw new IOException("visible write failed");
    }
    catch
    {
        Retire(lease, "catchup-write-failed");
        return false;
    }

    lock (_gate)
    {
        if (!ValidateAfterFlush(lease, expectedUpstreamGen, batch))
        {
            RetireLocked(lease, "generation-changed-after-catchup");
            return false;
        }

        _forwarding = VisibleForwardingState.Forwarding;
        _catchup.Reset();
        return true;
    }
}
```

Si une ecriture partielle vers Dofus a pu se produire et qu'ensuite une generation change, la liaison devient ambigue. Dans ce cas, retirer ce downstream exact plutot que le raccrocher a une autre session.

---

## 7. Phase D - definir qui possede chaque requete client

Un client visible et un moteur Socket ne doivent jamais envoyer deux fois la meme requete.

Le plus simple est une matrice d'ownership explicite :

| Famille | MITM classique | Hybride / upstream Socket | Action |
|---|---|---|---|
| action injectee par l'IA | moteur | moteur | le visible n'envoie rien |
| `GameActionAcknowledgementRequest` | client visible ou moteur, **un seul** | moteur | filtrer le doublon visible |
| `FightTurnReadyRequest` | client visible ou moteur, **un seul** | moteur | filtrer le doublon visible |
| ping / latency / sequence number | client visible | client visible ou moteur selon implementation | si visible choisi : raw passthrough |
| selection perso / bootstrap UI | client visible | client visible pendant bootstrap | raw passthrough exact |
| action manuelle utilisateur autorisee | client visible | client visible | raw passthrough + observation decodeur |
| frame inconnue mais valide en phase GameService | client visible | selon politique | preferer raw passthrough a decode/re-encode si elle appartient au visible |

Givre fait deja quelque chose de tres utile ici : une `CharacterSelectionRequest` du client visible est envoyee upstream en **raw**, avec son enveloppe et son UID exacts, au lieu d'etre reconstruite.

Ajouter dans Givre un equivalent :

```csharp
public bool TryPassthroughVisibleFrame(
    VisibleClientLease lease,
    ReadOnlyMemory<byte> framed)
{
    if (!IsLeaseCurrent(lease)) return false;
    if (_phase != ProtocolPhase.GameService) return false;
    return _currentUpstream.TryWriteRaw(framed);
}
```

Ne jamais utiliser un chemin de raw passthrough GameService pour des frames de login/connexion serveur. La phase protocolaire doit faire partie de la garde.

---

## 8. Phase E - le vrai bypass d'animations : client-side, par PID, combat-only

Givre a teste une approche reseau tres restrictive : supprimer uniquement `PlaySpellScriptEvent` (`jnm`) lorsque le combat est actif.

Cette approche n'est pas suffisante : sur les combats testes, les sorts joueur n'ont pas emis ce wire. Cela confirme que la bonne couche est celle que Givre utilise deja : **`GivreMods.dll` dans le processus client**.

### Ce qu'il ne faut pas faire

Ne pas supprimer :

- `SequenceStartEvent` ;
- `SequenceEndEvent` ;
- les evenements de degats / PA / PM ;
- `FightTurnEvent` ;
- les mouvements autoritaires ;
- les snapshots de combattants ;
- `FightEndEvent`.

Ces messages servent au protocole et a l'etat. Les enlever pour rendre l'image plus rapide finit par desynchroniser le client ou le bot.

### Evolution de `ModControl`

Conserver les cles actuelles et ajouter une garde explicite `combat_only` :

```csharp
public static void WriteForPid(
    int pid,
    bool noAnim,
    bool tactical,
    bool collapse = false,
    bool instantMove = false,
    bool combatOnly = true)
{
    File.WriteAllText(FileFor(pid),
        $"skipper={(noAnim ? 1 : 0)}\r\n" +
        $"tactical={(tactical ? 1 : 0)}\r\n" +
        $"collapse={(collapse ? 1 : 0)}\r\n" +
        $"instantmove={(instantMove ? 1 : 0)}\r\n" +
        $"combat_only={(combatOnly ? 1 : 0)}\r\n");
}
```

Le fichier est deja par PID, donc plusieurs clients peuvent avoir des profils differents.

### Cote `GivreMods.dll`

Le source natif exact n'est pas dans cette copie. La regle a appliquer dans le repo `GivreLauncher/native/GivreMods` est :

```cpp
struct RuntimeMods {
    bool skipper;
    bool collapse;
    bool instantMove;
    bool combatOnly;
};

static bool AllowCombatAcceleration(const RuntimeMods& m)
{
    if (!m.combatOnly) return true;
    return IsAuthoritativelyInFight(); // a relier au vrai contexte/scene IL2CPP
}

void OnCombatAnimationStart(Animation* a)
{
    if (!g_mods.skipper || !AllowCombatAcceleration(g_mods))
        return CallOriginal(a);

    if (IsLoopingOrPersistentEffect(a))
        return CallOriginal(a);

    CompleteAnimationImmediately(a);
}
```

Pour `instantMove`, le hook doit modifier **la presentation** du trajet : appliquer directement la position finale que le client vient de recevoir, sans inventer une cellule differente de celle du serveur.

```cpp
void OnFightMovementReceived(FightMovement* move)
{
    if (!g_mods.instantMove || !AllowCombatAcceleration(g_mods))
        return CallOriginal(move);

    const auto destination = move->ServerDestinationCell();
    RenderActorAt(destination);
    CompleteMovementPresentation();
}
```

Pour `collapse`, reduire les timers/attentes **de presentation** de la sequence. Ne pas forcer dans le client une transition de tour que le serveur n'a pas envoyee.

### Variante speedhack combat-only

Si `GivreMods` possede deja un hook de time scale Unity/IL2CPP, la variante la plus propre est :

```cpp
float EffectiveCombatTimeScale()
{
    if (!g_mods.combatOnly || IsAuthoritativelyInFight())
        return g_mods.combatSpeed; // ex. 4.0f, 8.0f, ou valeur mesuree
    return 1.0f;
}
```

Mais un changement global de `Time.timeScale` peut affecter l'UI, les coroutines et des timers non visuels. Pour un produit robuste, preferer des hooks cibles : animation de sort, deplacement de combat et attentes de presentation. Le `timeScale` global peut rester un mode experimental.

---

## 9. Phase F - ajouter un vrai mode `Turbo` au moteur Givre

Givre a actuellement :

```csharp
public enum CombatSpeed
{
    Instant,
    Fast,
    Slow,
}
```

`Instant` conserve encore plusieurs delais : cast/step ~150 ms et barrieres de move 140/700 ms. Pour reproduire la philosophie Turbo Givre sans casser les profils existants, ajouter un **nouveau** mode plutot que changer silencieusement `Instant`.

```csharp
public enum CombatSpeed
{
    Turbo,
    Instant,
    Fast,
    Slow,
}
```

Politique centralisee :

```csharp
public readonly record struct CombatTiming(
    int CastDelayMs,
    int StepDelayMs,
    int MoveHumanDelayMs,
    int MoveQuietMs,
    int MoveBarrierTimeoutMs,
    bool SimulateClientAnimations);

public static class CombatTimings
{
    public static CombatTiming For(CombatSpeed speed) => speed switch
    {
        CombatSpeed.Turbo => new(
            CastDelayMs: 0,
            StepDelayMs: 0,
            MoveHumanDelayMs: 0,
            MoveQuietMs: 0,
            MoveBarrierTimeoutMs: 700,
            SimulateClientAnimations: false),

        CombatSpeed.Instant => new(150, 150, 0, 140, 700, true),
        CombatSpeed.Fast    => new(600, 450, 800, 250, 1100, true),
        _                   => new(1500, 1000, 1800, 350, 1500, true),
    };
}
```

Les valeurs Fast/Slow exactes peuvent rester aleatoires comme aujourd'hui ; l'interet de cette classe est surtout que **Turbo = zero sleep artificiel**.

### Important : Turbo ne signifie pas `WaitMoveClear = off`

En Turbo :

- si un `SequenceEndEvent` est attendu, on attend ce signal ;
- si le mapping ne fournit pas le signal, le repli sur quiet-window peut etre tres court mais pas remplacer un opener de sequence encore connu comme ouvert ;
- une fois le mouvement injecte, on attend toujours que `SelfCell()` change ou qu'un refus arrive ;
- on relit la case de destination juste avant l'injection ;
- on ne joue jamais hors de `_myTurnOpen`.

Une version plus propre de `WaitMoveClear` est d'eviter un `quietMs` fixe pour Turbo :

```csharp
private async Task WaitMoveClear(
    uint pid,
    CombatSpeed speed,
    CancellationToken ct = default)
{
    int startEnds = _isqCount.GetValueOrDefault(pid);
    int need = _seqArmIsq.GetValueOrDefault(pid) + 1;
    long until = Environment.TickCount64 +
        (speed == CombatSpeed.Turbo ? 700 : 1500);

    while (Environment.TickCount64 < until)
    {
        int ends = _isqCount.GetValueOrDefault(pid);

        if (ends > startEnds || ends >= need)
            return;

        // Seulement si aucun SequenceEnd n'est disponible dans ce combat.
        if (ends == 0)
        {
            int quiet = speed == CombatSpeed.Turbo ? 20 : 140;
            if (Environment.TickCount64 - LastFightPacket(pid) >= quiet)
                return;
        }

        await Task.Delay(speed == CombatSpeed.Turbo ? 1 : 15, ct);
    }
}
```

Ici, `1 ms` est un poll local ; ce n'est pas un sleep de gameplay. Une implementation event-driven avec `TaskCompletionSource` est encore meilleure.

---

## 10. Phase G - Turbo en mode Socket : ACK immediat, toujours present

C'est ici que le plus gros gain est possible.

Aujourd'hui `SocketGameClient` simule les temps d'animation du vrai client :

```csharp
int anim = AnimMs(seqType, _seqMoveSteps);
long due = Math.Max(now, _ackChainDue) + anim;
```

Puis l'outbound du moteur attend :

1. l'echo de la sequence ;
2. l'envoi de l'ACK ;
3. les delais d'animation simules.

Pour Turbo, garder 1 et 2, supprimer 3.

Ajouter une politique runtime au `SocketGameClient` :

```csharp
public interface ICombatTransportPolicy
{
    CombatSpeed SpeedFor(uint pid);
}
```

ou plus simplement dans `SocketGameParams` :

```csharp
public Func<uint, CombatSpeed>? CombatSpeedFor { get; init; }
```

Puis :

```csharp
void ScheduleOwnAck(long actionId, long seqType)
{
    long now = Environment.TickCount64;
    var speed = p.CombatSpeedFor?.Invoke(_logPid) ?? CombatSpeed.Fast;

    int anim = speed == CombatSpeed.Turbo
        ? 0
        : AnimMs(seqType, _seqMoveSteps);

    _lastSelfAckBase = speed == CombatSpeed.Turbo
        ? now
        : Math.Max(now, _ackChainDue);

    long due = _lastSelfAckBase + anim;
    _ackChainDue = due;

    _lastSelfAck = React(
        "GameActionAcknowledgementRequest",
        new List<ProtoField>
        {
            VF(FN("GameActionAcknowledgementRequest", "valid"), 1),
            VF(FN("GameActionAcknowledgementRequest", "action_id"), actionId),
        },
        delayMs: (int)Math.Max(0, due - now),
        isAck: true,
        label: $"ACK action {actionId} ({speed})");
}
```

Le `SequenceEndEvent` reste le declencheur. On ne forge pas un ACK avant que la sequence possedee ne soit fermee.

### `FightTurnReadyRequest`

Les attentes de `1.6-2.4 s` qui simulent les animations d'autres combattants sont inutiles en Turbo clientless.

En Turbo :

```csharp
int d = speed == CombatSpeed.Turbo
    ? (int)Math.Max(0, _ackChainDue - now)
    : ExistingHumanTiming(...);
```

Le tour pret part donc des que :

- les sequences pertinentes sont closes ;
- les ACK dont Givre est proprietaire sont partis.

Il ne faut pas supprimer le `FightTurnReadyRequest` lui-meme : sans lui, le serveur peut attendre avant de donner la main.

### File outbound

Conserver la barriere :

```csharp
while (_inFight &&
       Environment.TickCount64 < echoUntil &&
       Volatile.Read(ref _selfSeqEnds) == echoBase)
{
    await Task.Delay(1, tk);
}

while (_inFight && Volatile.Read(ref _pendingAcks) > 0)
{
    await Task.Delay(1, tk);
}
```

Le gain vient du fait que `_pendingAcks` tombe presque immediatement apres `SequenceEndEvent`, pas du fait qu'on ignore la barriere.

---

## 11. Phase H - Turbo en MITM / hybride : proprietaire unique de l'ACK

Dans un MITM classique, Dofus visible peut produire lui-meme :

- `GameActionAcknowledgementRequest` ;
- `FightTurnReadyRequest`.

Si le moteur Givre commence lui aussi a les emettre en Turbo, il faut choisir un proprietaire.

Mode conseille :

```text
MITM vanilla / vitesse normale
    visible client owns ACK + turn-ready

Hybrid Socket authoritative / Turbo
    Givre owns ACK + turn-ready
    visible duplicate requests are consumed locally
```

Exemple de filtre C2S :

```csharp
bool ShouldForwardVisibleRequest(string clearName, SessionOwnership owner)
{
    if (owner != SessionOwnership.Engine)
        return true;

    return clearName switch
    {
        "GameActionAcknowledgementRequest" => false,
        "FightTurnReadyRequest"             => false,
        _                                   => true,
    };
}
```

Ne pas filtrer par code obfusque fige. Utiliser le nom clair du mapping courant.

Givre suit deja cette regle : dans son chemin visible synchronise, les ACK du client sont consommes tandis que le moteur envoie l'ACK attendu apres fermeture de sequence.

---

## 12. Phase I - API de switch haut niveau

Une fois les seams precedents presents, exposer une API qui ne manipule pas directement les sockets dans la ViewModel.

```csharp
public enum UpstreamMode
{
    Relay,
    Socket,
}

public sealed class HybridGameSession
{
    public BotSessionKey Key { get; }
    public UpstreamMode UpstreamMode { get; private set; }
    public uint? VisiblePid { get; private set; }

    public Task<SwitchResult> SwitchUpstreamAsync(
        UpstreamMode target,
        CancellationToken ct = default);

    public Task<SwitchResult> AttachVisibleAsync(
        uint pid,
        CancellationToken ct = default);

    public void DetachVisible(string reason);
}
```

### `SwitchUpstreamAsync(Socket)`

```text
guard: session vivante, pas de switch deja en vol
take/suspend visible lease si present
increment upstream generation
retire ancien upstream apres suspension
ouvre SocketGameClient sur le meme compte/personnage
publie son injecteur pour la MEME session logique
bufferise ses S->C pour le visible
attend snapshot autoritaire
flush catch-up
resume visible
commit UpstreamMode.Socket
```

### Retour vers Relay

Le retour vrai vers un upstream pilote directement par Dofus est plus delicat car le client visible et le moteur peuvent avoir des UIDs/etats de bootstrap differents.

Premiere version recommandee : une fois la session passee en upstream Socket, garder cet upstream jusqu'a la prochaine reconnexion complete. Le bouton « MITM » peut alors signifier « relancer proprement une session Relay » a un boundary de connexion, et non voler un TCP en plein tour.

Cela donne deja l'essentiel de Givre : le visible peut rester attache pendant les reconnexions/replacements d'upstream et la session logique ne meurt pas.

---

## 13. Integration UI / profil de combat

Ajouter dans `CombatProfile` :

```csharp
public CombatSpeed Speed { get; set; } = CombatSpeed.Fast;
public bool NoAnim { get; set; }
public bool Collapse { get; set; }
public bool InstantMove { get; set; }
public bool CombatOnlyAcceleration { get; set; } = true;
```

L'UI peut presenter :

```text
Vitesse combat
  Normal / Lent
  Fast
  Instant
  Turbo

Affichage combat
  Bypass animations
  Deplacements instantanes
  Collapse attentes visuelles
  Acceleration uniquement en combat [ON]
```

Le preset Turbo peut activer par defaut :

```text
Speed = Turbo
NoAnim = true           si GivreMods est disponible
Collapse = true         si GivreMods est disponible
InstantMove = true      si GivreMods est disponible
CombatOnlyAcceleration = true
```

Les options natives restent best-effort : l'absence de `GivreMods.dll` ne doit pas casser le protocole Turbo. Le bot combattra juste avec des animations visibles normales.

---

## 14. Ordre d'implementation concret

### Lot 1 - centraliser la politique Turbo

Fichiers :

- `GivreClone.App/Services/CombatProfile.cs`
- `GivreClone.App/Services/LaunchService.Combat.Turn.cs`
- `GivreClone.App/Services/LaunchService.Combat.Script.cs`
- `GivreClone.App/Services/LaunchService.Combat.PreFight.cs`
- `GivreClone.App/Services/LaunchService.SnowPlacement.cs`

Actions :

1. ajouter `CombatSpeed.Turbo` ;
2. centraliser tous les delais dans `CombatTimings` ;
3. Turbo -> sleeps synthetiques a 0 ;
4. garder les awaits serveur ;
5. ne pas dupliquer la logique dans les chemins moteur/script.

### Lot 2 - rendre `SocketGameClient` Turbo-aware

Fichiers :

- `GivreClone.App/Services/Transport/Socket/SocketGameClient.cs`
- `GivreClone.App/Services/Transport/Socket/SocketProbe.cs`
- `GivreClone.App/Services/LaunchService.cs`

Actions :

1. passer un provider de `CombatSpeed` par PID ;
2. `ScheduleOwnAck` : 0 ms d'animation en Turbo ;
3. `ScheduleTurnReady` : supprimer les delais de simulation visuelle en Turbo ;
4. conserver l'ordre `SequenceEnd -> ACK -> action suivante` ;
5. ajouter de la telemetrie `sequenceEndAt / ackSentAt / nextActionAt`.

### Lot 3 - renforcer `GivreMods` en combat-only

Fichiers :

- `GivreClone.App/Services/ModControl.cs`
- `GivreLauncher/native/GivreMods/...` dans le repo d'origine

Actions :

1. ajouter `combat_only=1` ;
2. verifier le contexte combat dans chaque hook ;
3. garder `skipper`, `collapse`, `instantmove` independants ;
4. si un speed multiplier global existe, le gater au combat et le laisser optionnel.

### Lot 4 - ajouter le seam visible au `HandshakeRelay`

Repo voisin : `GivreLauncher.Core.Transport.HandshakeRelay` / GivreCore.

Actions :

1. exposer downstream exact par PID ;
2. visible generation ;
3. upstream generation ;
4. suspend/resume ;
5. raw upstream/raw visible ;
6. notifications de fermeture qui indiquent quel cote a ferme.

### Lot 5 - catch-up / preservation

Creer par exemple :

```text
GivreClone.App/Services/Transport/Hybrid/
  VisibleClientLease.cs
  HybridGameSession.cs
  ReconnectCatchup.cs
  CombatRequestOwnership.cs
```

Actions :

1. suspend before reset ;
2. buffer 1024 frames / 16 MiB ;
3. snapshot boundaries ;
4. replay ;
5. revalidation generations ;
6. fail-closed du binding ambigu.

### Lot 6 - ownership C2S

Ajouter un routeur clair par nom de message :

```csharp
public enum RequestOwner
{
    VisibleClient,
    Engine,
    Passthrough,
    SuppressedDuplicate,
}
```

Le routeur doit etre teste independamment du transport.

### Lot 7 - switch haut niveau et UX

Une fois les lots precedents stables, brancher les commandes ViewModel. La ViewModel ne doit jamais acceder a `TcpClient`, `NetworkStream` ou aux buffers de catch-up.

---

## 15. Tests indispensables avant live

### 15.1 Tests unitaires transport hybride

Ajouter `GivreClone.Tests/HybridTransportTests.cs` :

1. suspend conserve le meme PID/generation ;
2. une deuxieme suspension est idempotente ;
3. upstream generation N+1 invalide les callbacks N ;
4. overflow du catch-up interdit la reprise ;
5. buffer vide n'autorise pas une reprise ;
6. changement de visible generation pendant le flush retire le lease ;
7. changement upstream pendant le flush retire le lease ;
8. replay respecte exactement l'ordre des frames ;
9. aucune autre connexion Dofus n'est adoptee a la place.

### 15.2 Tests Turbo protocole

Ajouter `GivreClone.Tests/CombatTurboTimingTests.cs` :

- `SequenceEnd(self)` produit exactement un ACK ;
- `SequenceEnd(other)` ne produit aucun ACK ;
- Turbo : ACK delay = 0 ;
- Instant/Fast/Slow gardent leur politique actuelle ;
- action N+1 ne part pas avant la fermeture/ACK de N ;
- `FightTurnReadyRequest` reste envoye ;
- mouvement Turbo attend toujours confirmation serveur/refus ;
- aucune action n'est injectee hors de `_myTurnOpen`.

### 15.3 Tests ownership

Exemples :

```text
Hybrid + Engine owner + GameActionAcknowledgementRequest -> suppress duplicate
Hybrid + Visible owner + GameActionAcknowledgementRequest -> passthrough
Hybrid + raw CharacterSelectionRequest -> preserve exact bytes
Suspended + login-phase frame -> reject GameService passthrough
```

### 15.4 Tests `ModControl`

- fichier par PID ;
- `combat_only=1` par defaut ;
- OFF remet `skipper/collapse/instantmove` a 0 ;
- suppression du fichier au teardown.

---

## 16. Validation live par etapes

Ne pas valider toutes les nouveautes dans le meme run. L'ordre suivant permet d'isoler les regressions.

### Run A - MITM actuel, aucune acceleration native

Attendu : fonctionnement identique a la baseline.

Mesurer :

```text
visible pid
visible generation
upstream generation
server frames rx
client frames tx
disconnects
```

### Run B - Turbo reseau uniquement

`GivreMods` laisse le rendu normal.

Mesurer par action :

```text
t_decision
t_request_tx
t_sequence_start
t_sequence_end
t_ack_tx
t_next_request_tx
```

KPIs :

```text
SequenceEnd -> ACK             objectif : quelques ms / scheduler local
ACK -> prochaine action        objectif : quelques ms si le serveur autorise
move request -> server confirm objectif : RTT/traitement serveur, sans sleep fixe
```

### Run C - no-anim / instantmove combat-only

Verifier visuellement :

- hors combat : vitesse normale ;
- combat : animation de sort terminee immediatement ;
- mouvement visuel arrive directement sur la destination serveur ;
- UI/menus/chat continuent de fonctionner ;
- aucun evenement reseau autoritaire n'est filtre.

### Run D - reconnect avec visible preserve

Sur une carte stable :

1. client visible attache ;
2. suspendre ;
3. remplacer l'upstream ;
4. atteindre le snapshot monde ;
5. flush/reprise.

Attendu :

```text
PID Dofus identique
fenetre visible toujours vivante
visible generation identique
upstream generation incrementee
catchup non vide
forwarding repris une seule fois
```

### Run E - reconnect pre-world

Verifier explicitement le boundary `CharacterListEvent`, sinon on risque le deadlock deja observe dans Givre.

### Run F - reconnect en combat

Ne reprendre le forwarding que lorsque l'etat combat a ete reconstruit. Verifier :

- fighters/positions coherents ;
- proprietaire du tour coherent ;
- compteur de sequences coherent ;
- aucun double ACK ;
- le premier mouvement post-reconnect est accepte.

---

## 17. Telemetrie a ajouter

Le switcher est difficile a diagnostiquer sans generations explicites. Journaliser seulement les metadonnees utiles :

```text
hybrid-visible-suspended pid=1234 vg=8 ug=21
hybrid-upstream-opened ug=22 mode=socket
hybrid-catchup-armed vg=8 ug=22
hybrid-catchup-buffer frames=... bytes=...
hybrid-boundary world-snapshot ug=22
hybrid-catchup-flushed frames=... bytes=...
hybrid-visible-resumed pid=1234 vg=8 ug=22
```

Combat Turbo :

```text
combat-seq-end actor=self action=...
combat-ack-tx action=... deltaMs=...
combat-next-action deltaFromAckMs=...
combat-move-confirm deltaMs=...
```

Ne pas journaliser les payloads bruts de login, tokens ou contenus sensibles pour cette telemetrie.

---

## 18. Invariants a ne jamais casser

1. **Un upstream autoritaire par session logique.**
2. **Un seul proprietaire d'ACK/turn-ready a la fois.**
3. **Turbo retire les sleeps, pas les barrieres serveur.**
4. **No-animation agit sur la presentation, pas sur l'etat protocolaire.**
5. **Un client visible preserve reste lie a son PID + sa generation exacte.**
6. **Un buffer de catch-up appartient a une seule generation upstream.**
7. **Pas de reprise apres overflow ou ambiguite de generation.**
8. **Le raw passthrough preserve les octets/UIDs du client lorsqu'il est proprietaire de la requete.**
9. **Le changement d'upstream ne detruit pas `GameState`, Lua, profil, console ou identite logique du bot.**
10. **Une confirmation serveur reste obligatoire pour la position et la fenetre de tour.**

---

## 19. Patch minimal conseille pour obtenir rapidement un gain visible

Si l'objectif est d'obtenir d'abord le benefice de vitesse sans attendre tout le switcher hybride, faire dans cet ordre :

```text
1. ajouter CombatSpeed.Turbo
2. retirer les sleeps artificiels dans les chemins combat moteur/script
3. rendre SocketGameClient ACK/turn-ready Turbo-aware
4. activer NoAnim + Collapse + InstantMove via GivreMods en combat-only
5. mesurer 20+ combats Socket et MITM
6. ensuite porter le visible-client preservation / upstream switch
```

Cette premiere tranche est petite et exploite des seams deja existants. Le switcher hybride est ensuite une evolution de transport, pas une condition pour avoir le Turbo.

---

## 20. Fichiers de reference

### Givre

```text
GivreClone.Engine/Accounts/Account.cs
GivreClone.Engine/ActionSender.cs
GivreClone.Engine/Pacer.cs
GivreClone.App/Services/Transport/IGameTransport.cs
GivreClone.App/Services/Transport/MitmTransport.cs
GivreClone.App/Services/Transport/Socket/SocketGameClient.cs
GivreClone.App/Services/Transport/Socket/SocketProbe.cs
GivreClone.App/Services/LaunchService.cs
GivreClone.App/Services/LaunchService.Combat.cs
GivreClone.App/Services/LaunchService.Combat.Turn.cs
GivreClone.App/Services/LaunchService.Packets.cs
GivreClone.App/Services/CombatProfile.cs
GivreClone.App/Services/ModControl.cs
native/GivreKama/GivreKama.cpp
docs/GIVRE_MITM_FINDINGS_20261005.md
```

### Givre

```text
GivreActive/EasyBot/Sockets/SwitchSocket.vb
GivreActive/EasyBot/Sockets/MITM.vb
GivreActive/EasyBot/PacketAnalyser/PacketAnalyser.vb
GivreActive/EasyBot/PacketAnalyser/Function/Combat/Ia-Avancee/BasicAI.vb
GivreActive/EasyBot/Main/ModernUiRuntimeBridge.vb
```

---

## 21. Definition de fini

Le portage peut etre considere fonctionnel quand ces preuves existent ensemble :

```text
[ ] Turbo : 0 sleep artificiel dans le chemin combat selectionne
[ ] chaque sequence propre -> exactement un ACK
[ ] aucun ACK d'un autre combattant
[ ] aucun double ACK visible + moteur
[ ] mouvement suivant accepte apres SequenceEnd / confirmation serveur
[ ] no-anim n'altere aucun message d'etat serveur
[ ] no-anim / instantmove inactifs hors combat
[ ] MITM -> upstream replacement conserve le meme Dofus PID
[ ] catch-up rejoue les frames dans l'ordre
[ ] generation mismatch / overflow -> pas de reprise ambigue
[ ] CharacterList boundary couvre la reconnexion pre-world
[ ] snapshot monde couvre la reconnexion hors combat
[ ] snapshot combat couvre la reconnexion en combat
[ ] Lua / profil / GameState restent attaches a la meme session logique
```

Le resultat cible est donc un Givre ou **le moteur peut tourner en Socket a vitesse Turbo, avec ou sans client visible**, et ou l'ajout d'un client visible ne change plus l'autorite protocolaire : il devient une vue optionnelle, acceleree localement par `GivreMods` pendant les combats.
