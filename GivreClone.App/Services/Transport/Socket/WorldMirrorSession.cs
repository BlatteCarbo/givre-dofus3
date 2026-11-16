using System;
using System.Collections.Generic;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Identité du catalogue et du scope choisi par la session Socket. Le miroir n'écrit aucune trame
/// tant que les deux ne sont pas la même chaîne non vide. Un scope vide ou différent refuse l'armement.
/// </summary>
public static class WorldMirrorScope
{
    // Toute la ligne 3.7 partage les mêmes protobufs : deux versions lisibles de la même famille majeur.mineur
    // (ex. mapping 3.7.11.11, jeu 3.7.13.17) sont le même scope, comme pour l'admission de la session Socket.
    public static bool Same(string? catalogIdentity, string? runtimeScope)
        => !string.IsNullOrEmpty(catalogIdentity)
           && (string.Equals(catalogIdentity, runtimeScope, StringComparison.Ordinal)
               || MZP10MappingVersionAdmission.IsSameProtocolFamily(catalogIdentity, runtimeScope));
}

/// <summary>Copie figée des dernières trames serveur, une par nom. Les octets ne sont pas réécrits ici.</summary>
public sealed class WorldMirrorSnapshot
{
    public static WorldMirrorSnapshot Empty { get; } = new(new Dictionary<string, byte[]>(StringComparer.Ordinal), new Dictionary<string, long?>(StringComparer.Ordinal));

    private readonly Dictionary<string, byte[]> _frames;
    private readonly Dictionary<string, long?> _mapIds;

    private WorldMirrorSnapshot(Dictionary<string, byte[]> frames, Dictionary<string, long?> mapIds)
    {
        _frames = frames;
        _mapIds = mapIds;
    }

    public static WorldMirrorSnapshot Create(IReadOnlyDictionary<string, byte[]> frames, IReadOnlyDictionary<string, long?> mapIds)
    {
        var copy = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pair in frames)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value is null) continue;
            copy[pair.Key] = (byte[])pair.Value.Clone();
        }
        var maps = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var pair in mapIds)
            if (!string.IsNullOrEmpty(pair.Key)) maps[pair.Key] = pair.Value;
        return new WorldMirrorSnapshot(copy, maps);
    }

    public bool TryGet(string name, out byte[] bytes)
    {
        if (_frames.TryGetValue(name, out var found) && found is { Length: > 0 })
        {
            bytes = found;
            return true;
        }
        bytes = Array.Empty<byte>();
        return false;
    }

    /// <summary>Vrai seulement si un parse de map_id a réussi. Un échec de parse laisse la clé absente ou nulle.</summary>
    public bool TryParsedMapId(string name, out long mapId)
    {
        if (_mapIds.TryGetValue(name, out var value) && value is long id && id > 0)
        {
            mapId = id;
            return true;
        }
        mapId = 0;
        return false;
    }
}

/// <summary>
/// Dernière trame serveur par nom, pour un pid Socket. Une trame arrivée pendant un combat ne remplace rien.
/// </summary>
public sealed class WorldMirrorFrameCache
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, Dictionary<string, byte[]>> _frames = new();
    private readonly Dictionary<uint, Dictionary<string, long?>> _mapIds = new();
    private readonly Dictionary<uint, (string? Catalog, string? Runtime)> _scope = new();
    private readonly Dictionary<uint, int> _generation = new();
    private readonly Dictionary<uint, Dictionary<string, byte[]>> _login = new();

    public void Observe(uint pid, string name, byte[] bytes, long? mapId, bool inFight)
    {
        if (inFight || string.IsNullOrEmpty(name) || bytes is null) return;
        lock (_gate)
        {
            var frames = Bucket(_frames, pid);
            frames[name] = (byte[])bytes.Clone();
            if (name is "MapCurrentEvent" or "MapComplementaryInformationEvent")
                Bucket(_mapIds, pid)[name] = mapId;
        }
    }

    public void RememberLogin(uint pid, string slot, byte[] framed)
    {
        if (pid == 0 || framed is not { Length: > 0 }) return;
        if (slot is not ("IdentificationSuccess" or "SelectServerSuccess")) return;
        lock (_gate) Bucket(_login, pid)[slot] = (byte[])framed.Clone();
    }

    public byte[]? LoginFrame(uint pid, string slot)
    {
        lock (_gate)
            return _login.TryGetValue(pid, out var slots) && slots.TryGetValue(slot, out var frame) ? frame : null;
    }

    public void NoteScope(uint pid, string? catalogIdentity, string? runtimeScope)
    {
        lock (_gate) _scope[pid] = (catalogIdentity, runtimeScope);
    }

    public void NoteGeneration(uint pid, int generation)
    {
        lock (_gate) _generation[pid] = generation;
    }

    public int Generation(uint pid)
    {
        lock (_gate) return _generation.TryGetValue(pid, out var generation) ? generation : 0;
    }

    public (string? Catalog, string? Runtime) Scope(uint pid)
    {
        lock (_gate) return _scope.TryGetValue(pid, out var scope) ? scope : default;
    }

    public WorldMirrorSnapshot Copy(uint pid)
    {
        lock (_gate)
        {
            var frames = _frames.TryGetValue(pid, out var found) ? found : new Dictionary<string, byte[]>();
            var maps = _mapIds.TryGetValue(pid, out var ids) ? ids : new Dictionary<string, long?>();
            return WorldMirrorSnapshot.Create(frames, maps);
        }
    }

    public void Forget(uint pid)
    {
        lock (_gate)
        {
            _frames.Remove(pid);
            _mapIds.Remove(pid);
            _scope.Remove(pid);
            _generation.Remove(pid);
            _login.Remove(pid);
        }
    }

    private static Dictionary<TKey, TValue> Bucket<TKey, TValue>(Dictionary<uint, Dictionary<TKey, TValue>> all, uint pid)
        where TKey : notnull
    {
        if (!all.TryGetValue(pid, out var bucket))
            all[pid] = bucket = new Dictionary<TKey, TValue>();
        return bucket;
    }
}

public enum WorldMirrorArmStatus
{
    Armed,
    AlreadyArmed,
    InFight,
    ScopeMismatch,
    MissingFrame,
    StaleMap,
    MissingInventory,
    UpstreamDown,
    IdentityMismatch,
}

public sealed record WorldMirrorArmRequest(
    long CharacterId,
    long WorldCharacterId,
    long LiveMapId,
    int PipelineGeneration,
    long AttemptStartedMs,
    bool InFight,
    bool UpstreamWritable,
    string? CatalogIdentity,
    string? RuntimeScope,
    WorldMirrorSnapshot Snapshot,
    int NotedGeneration = 0);

public sealed class WorldMirrorLive
{
    public long NowMs { get; init; }
    public bool InFight { get; init; }
    public long MapId { get; init; }
    public long CharacterId { get; init; }
    public int PipelineGeneration { get; init; }
    public bool UpstreamWritable { get; init; }
    public string? CatalogIdentity { get; init; }
    public string? RuntimeScope { get; init; }
    public WorldMirrorSnapshot Snapshot { get; init; } = WorldMirrorSnapshot.Empty;
}

public enum WorldMirrorClientStatus
{
    Ignored,
    Waiting,
    Rejected,
    Served,
    Forwarded,
    Dropped,
    BudgetExpired,
}

public sealed record WorldMirrorClientRequest(string Name, long? CharacterId, byte[] RawFrame, WorldMirrorLive Live);

public sealed record WorldMirrorClientResult(WorldMirrorClientStatus Status, byte[][] ToClient, byte[]? ToUpstream)
{
    public static WorldMirrorClientResult Empty(WorldMirrorClientStatus status)
        => new(status, Array.Empty<byte[]>(), null);
}

/// <summary>
/// Machine à états du miroir : 0 arrêté, 1 armé, 2 personnage accepté, 3 retransmission.
/// Annuler le miroir ne ferme pas l'amont Socket.
/// </summary>
public sealed class WorldMirrorSession
{
    public const int StageOff = 0;
    public const int StageArmed = 1;
    public const int StageCharacterAccepted = 2;
    public const int StageForwarding = 3;
    public const int AttemptTimeoutMs = 300_000;
    public const bool PausesScript = false;
    public const string InventoryFrame = "InventoryContentEvent";
    public const string BudgetLog = "world-mirror-bootstrap-timeout:socket-session-preserved";
    public const string MapRejectedLog = "world-mirror-map-replay-rejected:world-changed";
    public const string LiveForwardLog = "world-mirror-live-forwarding:socket-session-preserved";
    public const string HudUnverifiedLog = "world-mirror-hud-not-verified:map-transport-ready-only";
    public const string SelectionRejectedLog = "world-mirror-character-selection-rejected";
    public const string InventoryReplayFailedLog = "world-mirror-inventory-replay-failed:socket-session-preserved";
    public const string ArmedLog = "world-mirror-armed";
    public const string LaunchLog = "world-mirror-launch:socket-session-preserved";
    public const string LoginMissingLog = "world-mirror-login-slot-missing:socket-session-preserved";
    public const string CancelledLog = "world-mirror-cancelled:socket-session-preserved";

    public static readonly string[] RequiredWorldFrames =
    [
        "CharacterListEvent",
        "CharacterSelectionEvent",
        "CharacterLoadingCompleteEvent",
        "MapCurrentEvent",
        "MapComplementaryInformationEvent",
    ];

    private static readonly string[] CharacterListFrames =
    [
        "QueueStatusEvent", "CharacterListEvent", "CharacterListWithRemodelingEvent", "GameActionItemListEvent",
    ];

    private static readonly string[] SelectionOptional =
    [
        "QueueStatusEvent", "CharacterCharacteristicsEvent", "HavenBagRoomUpdateEvent", "CharacterExperienceGainEvent",
        "NotificationsEvent", "ArenaUpdatePlayerInformationEvent", "AchievementsEvent", "TagStoragesRefreshEvent",
        "EmotesEvent", "JobDescriptionEvent", "JobExperiencesUpdateEvent", "JobCrafterDirectorySettingsEvent",
        "PrismListEvent", "ChatChannelsEnabledEvent", "ZaapKnownListEvent", "AlignmentRankUpdateEvent",
        "InventoryWeightEvent", "SequenceNumberEvent", "TextInformationEvent", "SpouseStatusEvent",
        "CharacterCapabilitiesEvent", "AlmanachCalendarDateEvent", "HouseAccountEvent", "ExchangeMoneyMovementLimitEvent",
        "HavenBagPackListEvent", "HavenBagRoomUpdateEvent", "StorageKamasUpdateEvent", "CharacterAppearanceCollectionEvent",
        "CosmeticInventoryLoadedEvent", "OutfitsLoadedEvent", "OutfitAuraChangedEvent", "OrnamentSelectErrorEvent",
        "TitleSelectErrorEvent", "OutfitEntityLookChangedEvent", "TitlesAndOrnamentsEvent", "HaapiSessionEvent",
        "GameActionItemListEvent", "QuestsEvent", "QuestsFollowedEvent",
    ];

    private static readonly string[] ContextOptionalBefore =
    [
        "ContextDestroyEvent", "ContextCreationEvent", "LifePointsRegenBeginEvent", "NpcsMapQuestStatusUpdateEvent",
    ];

    private static readonly string[] ContextOptionalAfter =
    [
        "TimeEvent", "CharacterCharacteristicsEvent", "SpellsEvent", "ShortcutBarContentEvent",
    ];

    private readonly object _gate = new();
    private readonly Action<string>? _log;
    private readonly List<string> _messages = new();
    private WorldMirrorSnapshot? _snapshot;
    private long _characterId;
    private int _pipeline;
    private long _attemptStarted;
    private string? _catalog;
    private string? _runtime;
    private int _stage;
    private Action<byte[]>? _toVisible;
    private Action? _closeVisible;

    public WorldMirrorSession(Action<string>? log = null) => _log = log;

    public int Stage { get { lock (_gate) return _stage; } }
    public bool ForwardingEnabled { get { lock (_gate) return _stage == StageForwarding; } }
    public bool UpstreamClosed => false;
    public long AttemptStartedMs { get { lock (_gate) return _attemptStarted; } }
    public IReadOnlyList<string> Messages { get { lock (_gate) return _messages.ToArray(); } }

    public static bool IsBootstrapRequest(string name) => name is
        "IdentificationRequest" or "CharacterSelectionRequest" or "CharacterListRequest"
        or "AuthenticationTicketRequest" or "ServerSelectionRequest";

    public WorldMirrorArmStatus TryArm(WorldMirrorArmRequest request)
    {
        lock (_gate)
        {
            if (_stage != StageOff) return WorldMirrorArmStatus.AlreadyArmed;
            if (!WorldMirrorScope.Same(request.CatalogIdentity, request.RuntimeScope))
                return WorldMirrorArmStatus.ScopeMismatch;
            if (request.InFight) return WorldMirrorArmStatus.InFight;
            if (!request.UpstreamWritable) return WorldMirrorArmStatus.UpstreamDown;
            if (request.CharacterId <= 0 || request.WorldCharacterId != request.CharacterId || request.LiveMapId <= 0)
                return WorldMirrorArmStatus.IdentityMismatch;
            if (request.NotedGeneration != 0 && request.NotedGeneration != request.PipelineGeneration)
                return WorldMirrorArmStatus.IdentityMismatch;
            var snapshot = request.Snapshot ?? WorldMirrorSnapshot.Empty;
            foreach (var name in RequiredWorldFrames)
                if (!snapshot.TryGet(name, out _)) return WorldMirrorArmStatus.MissingFrame;
            if (!snapshot.TryParsedMapId("MapCurrentEvent", out var mapId) || mapId != request.LiveMapId)
                return WorldMirrorArmStatus.StaleMap;
            if (snapshot.TryParsedMapId("MapComplementaryInformationEvent", out var complementary) && complementary != request.LiveMapId)
                return WorldMirrorArmStatus.StaleMap;
            if (!snapshot.TryGet(InventoryFrame, out _)) return WorldMirrorArmStatus.MissingInventory;
            _snapshot = snapshot;
            _characterId = request.CharacterId;
            _pipeline = request.PipelineGeneration;
            _attemptStarted = request.AttemptStartedMs;
            _catalog = request.CatalogIdentity;
            _runtime = request.RuntimeScope;
            _stage = StageArmed;
            Note(ArmedLog);
            return WorldMirrorArmStatus.Armed;
        }
    }

    public void AttachVisible(Action<byte[]> write, Action closeVisible)
    {
        lock (_gate)
        {
            _toVisible = write;
            _closeVisible = closeVisible;
        }
    }

    public void ForwardServerFrame(byte[] wire)
    {
        Action<byte[]>? write;
        lock (_gate)
        {
            if (_stage != StageForwarding || wire is not { Length: > 0 }) return;
            write = _toVisible;
        }
        try { write?.Invoke(wire); } catch { /* la fenêtre visible est partie ; l'amont Socket reste */ }
    }

    public void Cancel(string reason)
    {
        Action? close;
        lock (_gate) close = CancelCore(reason);
        try { close?.Invoke(); } catch { /* fermer la fenêtre visible ne ferme pas l'amont */ }
    }

    public WorldMirrorClientResult HandleClientRequest(WorldMirrorClientRequest request)
    {
        Action? close;
        WorldMirrorClientResult result;
        lock (_gate) result = HandleCore(request, out close);
        try { close?.Invoke(); } catch { /* idem */ }
        return result;
    }

    private WorldMirrorClientResult HandleCore(WorldMirrorClientRequest request, out Action? close)
    {
        close = null;
        if (_stage == StageOff || string.IsNullOrEmpty(request.Name)) return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Ignored);
        if (request.Name == "CharacterSelectionRequest") return HandleSelection(request, out close);
        if (request.Name == "CharacterListRequest" && _stage == StageArmed)
            return ServePresent(CharacterListFrames);
        if ((request.Name is "YoshiRequest" or "ClientIdRequest") && _stage is StageArmed or StageCharacterAccepted)
            return ServePresent(new[] { "AccountSubscriptionElapsedDurationUpdateEvent" });
        if (request.Name == "ContextCreationRequest" && _stage == StageCharacterAccepted)
            return HandleContext();
        if (request.Name == "MapInformationRequest" && _stage == StageCharacterAccepted)
            return HandleMap(request, out close);
        if (_stage == StageForwarding)
        {
            if (IsBootstrapRequest(request.Name))
                return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Dropped);
            if (request.RawFrame is not { Length: > 0 })
                return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Ignored);
            return new WorldMirrorClientResult(WorldMirrorClientStatus.Forwarded, Array.Empty<byte[]>(), (byte[])request.RawFrame.Clone());
        }
        return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Ignored);
    }

    private WorldMirrorClientResult HandleSelection(WorldMirrorClientRequest request, out Action? close)
    {
        close = null;
        if (_stage == StageForwarding)
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Dropped);
        if (_stage != StageArmed)
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Ignored);
        if (request.CharacterId is not long id || id != _characterId)
        {
            Note(SelectionRejectedLog);
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Rejected);
        }
        var dest = new List<byte[]>();
        if (!Append(_snapshot, dest, "CharacterSelectionEvent"))
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Rejected);
        if (!Append(_snapshot, dest, InventoryFrame))
        {
            close = CancelCore(InventoryReplayFailedLog);
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Rejected);
        }
        foreach (var name in SelectionOptional) Append(_snapshot, dest, name);
        if (!Append(_snapshot, dest, "CharacterLoadingCompleteEvent"))
            return new WorldMirrorClientResult(WorldMirrorClientStatus.Rejected, dest.ToArray(), null);
        _stage = StageCharacterAccepted;
        return new WorldMirrorClientResult(WorldMirrorClientStatus.Served, dest.ToArray(), null);
    }

    private WorldMirrorClientResult HandleContext()
    {
        var dest = new List<byte[]>();
        foreach (var name in ContextOptionalBefore) Append(_snapshot, dest, name);
        if (!Append(_snapshot, dest, "MapCurrentEvent"))
            return new WorldMirrorClientResult(WorldMirrorClientStatus.Rejected, dest.ToArray(), null);
        foreach (var name in ContextOptionalAfter) Append(_snapshot, dest, name);
        return new WorldMirrorClientResult(WorldMirrorClientStatus.Served, dest.ToArray(), null);
    }

    private WorldMirrorClientResult HandleMap(WorldMirrorClientRequest request, out Action? close)
    {
        close = null;
        var live = request.Live;
        if (live.InFight)
        {
            if (live.NowMs - _attemptStarted >= AttemptTimeoutMs)
            {
                close = CancelCore(BudgetLog);
                return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.BudgetExpired);
            }
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Waiting);
        }
        if (!RefreshAccepts(live))
        {
            Note(MapRejectedLog);
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Rejected);
        }
        var snap = live.Snapshot;
        if (!snap.TryGet("MapCurrentEvent", out var current) || !snap.TryGet("MapComplementaryInformationEvent", out var complementary))
        {
            Note(MapRejectedLog);
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Rejected);
        }
        if (live.CharacterId != _characterId || live.PipelineGeneration != _pipeline || live.InFight || !live.UpstreamWritable)
        {
            close = CancelCore(MapRejectedLog);
            return WorldMirrorClientResult.Empty(WorldMirrorClientStatus.Rejected);
        }
        var dest = new List<byte[]> { current, complementary };
        if (snap.TryGet("AreaFightModifierUpdateEvent", out var area)) dest.Add(area);
        _snapshot = snap;
        _stage = StageForwarding;
        Note(LiveForwardLog);
        Note(HudUnverifiedLog);
        return new WorldMirrorClientResult(WorldMirrorClientStatus.Served, dest.ToArray(), null);
    }

    private bool RefreshAccepts(WorldMirrorLive live)
    {
        if (live.InFight || !live.UpstreamWritable) return false;
        if (live.CharacterId != _characterId || live.PipelineGeneration != _pipeline || live.MapId <= 0) return false;
        if (!WorldMirrorScope.Same(live.CatalogIdentity, live.RuntimeScope)) return false;
        if (!WorldMirrorScope.Same(live.CatalogIdentity, _catalog) || !WorldMirrorScope.Same(live.RuntimeScope, _runtime)) return false;
        var snap = live.Snapshot ?? WorldMirrorSnapshot.Empty;
        foreach (var name in RequiredWorldFrames)
            if (!snap.TryGet(name, out _)) return false;
        if (!snap.TryParsedMapId("MapCurrentEvent", out var mapId) || mapId != live.MapId) return false;
        if (snap.TryParsedMapId("MapComplementaryInformationEvent", out var complementary) && complementary != live.MapId)
            return false;
        return true;
    }

    private WorldMirrorClientResult ServePresent(IReadOnlyList<string> names)
    {
        var dest = new List<byte[]>();
        foreach (var name in names) Append(_snapshot, dest, name);
        return new WorldMirrorClientResult(WorldMirrorClientStatus.Served, dest.ToArray(), null);
    }

    private static bool Append(WorldMirrorSnapshot? snapshot, List<byte[]> dest, string name)
    {
        if (snapshot is null || !snapshot.TryGet(name, out var bytes)) return false;
        dest.Add(bytes);
        return true;
    }

    private Action? CancelCore(string reason)
    {
        _stage = StageOff;
        _snapshot = null;
        _toVisible = null;
        var close = _closeVisible;
        _closeVisible = null;
        Note(reason);
        return close;
    }

    private void Note(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        _messages.Add(line);
        try { _log?.Invoke(line); } catch { /* un journal ne casse pas le miroir */ }
    }
}

public enum WorldMirrorWaitResult { Idle, Busy, BudgetExpired }

/// <summary>Attente du fin de combat pilotée par une horloge et un délai injectés. Aucun sommeil propre.</summary>
public static class WorldMirrorWait
{
    public static async Task<WorldMirrorWaitResult> UntilAsync(
        Func<bool> inFight,
        Func<bool> occupied,
        Func<long> nowMs,
        long startedAtMs,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (occupied()) return WorldMirrorWaitResult.Busy;
            if (!inFight()) return WorldMirrorWaitResult.Idle;
            long now = nowMs();
            if (now - startedAtMs >= WorldMirrorSession.AttemptTimeoutMs)
                return WorldMirrorWaitResult.BudgetExpired;
            long remain = WorldMirrorSession.AttemptTimeoutMs - (now - startedAtMs);
            var slice = TimeSpan.FromMilliseconds(Math.Min(25, Math.Max(1, remain)));
            await delay(slice, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Liste des opérations de fermeture. Le miroir même personnage ne contient ni socket-cancel ni UnregisterSocketInjector.
/// La reconnexion explicite garde close puis launch.
/// </summary>
public static class HotMirrorPlan
{
    public const string SocketCancel = "socket-cancel";
    public const string UnregisterSocketInjector = "UnregisterSocketInjector";
    public const string Forget = "Forget";
    public const string ClearMap = "ClearMap";
    public const string Close = "close";
    public const string Launch = "launch";
    public const string PreserveUpstream = "preserve-upstream";
    public const string HotSessionDropped = "La session Socket à chaud a été abandonnée.";
    public const string SocketPreserved = "La session Socket reste connectée.";
    public const string WindowOpened = "Fenêtre de jeu ouverte. La session Socket reste connectée.";
    public const string FightBudget = "Le combat a dépassé le délai du miroir. La session Socket reste connectée.";
    public const string OtherCharacter = "Le miroir reste sur le personnage déjà en jeu. La session Socket n'est pas abandonnée.";
    public const string OpeningWindow = "Ouverture de la fenêtre de jeu…";

    public static string ArmMessage(WorldMirrorArmStatus status) => status switch
    {
        WorldMirrorArmStatus.ScopeMismatch => "Le miroir refuse ces trames : le catalogue et la version de la session ne correspondent pas. La session Socket reste connectée.",
        WorldMirrorArmStatus.MissingFrame => "Le miroir n'a pas toutes les trames du monde. La session Socket reste connectée.",
        WorldMirrorArmStatus.StaleMap => "La carte en cache ne correspond pas à la carte en jeu. La session Socket reste connectée.",
        WorldMirrorArmStatus.MissingInventory => "L'inventaire n'a pas été reçu. La session Socket reste connectée.",
        WorldMirrorArmStatus.UpstreamDown => "La session Socket n'est plus inscriptible. La session Socket reste connectée.",
        WorldMirrorArmStatus.InFight => FightBudget,
        WorldMirrorArmStatus.IdentityMismatch => OtherCharacter,
        WorldMirrorArmStatus.AlreadyArmed => "Une fenêtre de jeu est déjà en cours d'ouverture. La session Socket reste connectée.",
        _ => SocketPreserved,
    };

    public static bool UseHotPath(ConnectionMode source, ConnectionMode target, bool sameIdentity, bool explicitDrop)
        => !explicitDrop && sameIdentity && source == ConnectionMode.Socket && target == ConnectionMode.Mitm;

    public static IReadOnlyList<string> SourceDelegates(bool hot)
        => hot
            ? new[] { PreserveUpstream }
            : new[] { SocketCancel, UnregisterSocketInjector, Forget, ClearMap, Close, Launch };
}

/// <summary>Préfixe de longueur varint déjà utilisé par le décodeur et le client Socket. Pas un nouveau cadrage.</summary>
public static class WorldMirrorFraming
{
    public static byte[] Prefix(byte[] body)
    {
        body ??= Array.Empty<byte>();
        var len = RawProtobuf.EncodeVarint((ulong)body.Length);
        var framed = new byte[len.Length + body.Length];
        Buffer.BlockCopy(len, 0, framed, 0, len.Length);
        Buffer.BlockCopy(body, 0, framed, len.Length, body.Length);
        return framed;
    }
}

/// <summary>Lecture d'un identifiant déjà porté par le catalogue. Un champ absent ou nul n'est pas une valeur.</summary>
public static class WorldMirrorIds
{
    public static long? Positive(int? fieldNumber, IReadOnlyList<ProtoField> payload)
    {
        if (fieldNumber is not int number || number <= 0 || payload is null) return null;
        long id = PayloadReader.Int(payload, number, 0);
        return id > 0 ? id : null;
    }
}

public enum WorldMirrorFrameClass { Unknown, Login, Game }

public readonly record struct WorldMirrorInbound(WorldMirrorFrameClass Class, string Name, long? CharacterId, byte[] Wire)
{
    public static WorldMirrorInbound Unknown { get; } = new(WorldMirrorFrameClass.Unknown, "", null, Array.Empty<byte>());
    public static WorldMirrorInbound Login(string slot) => new(WorldMirrorFrameClass.Login, slot, null, Array.Empty<byte>());
    public static WorldMirrorInbound Game(string name, long? characterId, byte[] wire) => new(WorldMirrorFrameClass.Game, name, characterId, wire);
}

/// <summary>Seul un résultat qui a choisi l'amont est écrit sur l'injecteur. Une sélection refusée n'y passe pas.</summary>
public static class WorldMirrorDelivery
{
    public static void ForwardUpstream(WorldMirrorClientResult result, Action<byte[]> inject)
    {
        if (result.ToUpstream is { Length: > 0 } frame) inject(frame);
    }
}
