using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Décodage des paquets : initialisation du décodeur, trames socket, positions de cartes, dispatch OnDecoded =====================
public sealed partial class LaunchService
{
    private bool _decoderInit;

    // Charge le catalogue (data/) une seule fois et branche le decodeur. Silencieux si data absente : le relais
    // continue de router le trafic (anti-fuite), seul le decode est desactive.
    private CaptureDecoder? EnsureDecoder()
    {
        if (_decoderInit) return _decoder;
        _decoderInit = true;
        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            // Mapping STREAMÉ du serveur (déchiffré EN MÉMOIRE, jamais sur disque). En prod il n'y a PAS de repli disque :
            // pas de session valide => pas de catalogue => décodage désactivé (propriété défensive « pas de session ⇒ pas
            // de décode »). Le relais continue de router le trafic (anti-fuite d'IP), seul le décodage est off.
            _catalog = MappingCatalog.GetEffective();
            if (_catalog is null)
            {
                TransportLog.Line("[mapping] AUCUN mapping actif (pas de session serveur) -> décodage OFF ; le relais route quand même.");
                return _decoder;   // null : relais seul, aucun décodeur
            }
            TransportLog.Line($"[mapping] source={(MappingCatalog.IsReady ? "serveur" : "local (DEBUG)")} : " +
                              $"{_catalog.MappingCount} codes, {_catalog.MessageDefCount} messages");
            // Chemin EMISSION : les forges resolvent desormais leur code 3 lettres PAR NOM CLAIR, ici.
            // Sans ce branchement elles retombent sur les replis de capture ; avec lui, elles suivent le
            // mapping streame et survivent au prochain patch sans recompilation.
            MoveForge.UseCatalog(_catalog.CodeForName, _catalog.FieldNumber);
            // Champ absent du mapping (aucun numéro figé dans le client) : signalé une fois dans le journal de transport.
            FieldMiss.Log = TransportLog.Line;
            // Couche ENVELOPPE (GameMessage/Request/Event/Response) : ses slots sont AUSSI dans le mapping
            // (matching_current.json code hea/heb/hee/hdy ; fields.json GameMessage.{request,event,response}...) ->
            // on les resout par nom comme le reste. Un patch qui repermute le oneof (cf 3.6.11.12) se corrige alors
            // par un simple update du bundle, sans recompiler. Sans ce branchement : replis = numeros du client actuel.
            EnvelopeBuilder.UseCatalog(_catalog.FieldNumber);
            // Meme logique cote LECTURE : les numeros IMBRIQUES (InteractiveElement / StatedElement / ...) sont
            // renumerotes par chaque patch au meme titre que les champs de premier niveau.
            MapInteractivesReader.UseCatalog(_catalog.FieldNumber);
            TeleportReader.UseCatalog(_catalog.FieldNumber);
            LotEEventsReader.UseCatalog(_catalog.FieldNumber);   // Lot E : demandes reçues, options de combat, respawn, variantes, popup
            MapFightReader.UseCatalog(_catalog.FieldNumber);   // dispositions de combat (kkr) : numéros de champ du mapping
            FightStatsReader.UseCatalog(_catalog.FieldNumber); // stats/équipe/dégâts/variations de combat : porteurs par nom
            FightVisibilityReader.UseCatalog(_catalog.FieldNumber);
            FightPrepReader.UseCatalog(_catalog.FieldNumber);  // proposition de défi (challenge_proposals=1, challenge_id=5)
            FightResultReader.UseCatalog(_catalog.FieldNumber);  // bilan de combat (FightEndEvent.results imbriqués) : Lot C2
            InventoryReader.UseCatalog(_catalog.FieldNumber);      // inventaire/coffre (isb/isa/isf) : sous-messages ObjectItem
            QuestReader.UseCatalog(_catalog.FieldNumber);          // Lot I : quêtes (QuestActive/Details/QuestObjective/Completion par nom)
            LotMReader.UseCatalog(_catalog.FieldNumber);           // Lot M : succès, anomalies, offres de téléportation, document
            HouseReader.UseCatalog(_catalog.FieldNumber);          // Lot J : maisons de la carte (House/HouseOnMap/HouseInstance/AccountTag par nom)
            CharacterInfoReader.UseCatalog(_catalog.FieldNumber);  // liste de perso (ksw) : CharacterBasicInformation/CharacterLook
            CharacterSpellsReader.UseCatalog(_catalog.FieldNumber);// sorts (hnl) : SpellItem{spell_id,spell_level}
            MapActorsReader.UseCatalog(_catalog.FieldNumber);      // acteurs roleplay (jpo/jon) : ActorInformation/RolePlayActor
            PartyReader.UseCatalog(_catalog.FieldNumber);          // Lot G : événements de groupe (membres, chef, party_id)
            // Positions de carte : cache instantane, puis rafraichi en tache de fond DIRECTEMENT depuis le client
            // installe (bundle Unity) -> toujours a jour au patch, sans dependre du fichier embarque.
            var cachePath = Path.Combine(dataDir, "map_positions.json");
            _mapPositions = MapPositions.Load(cachePath);
            RefreshMapPositionsFromClient(cachePath);
            // World-graph (transitions inter-cartes) : cache instantane puis extraction runtime du client en tache de fond.
            // v3 : chaque arete porte en plus son CRITERE (m_criterion, ex "Qf=230") pour les portes/PNJ conditionnels ->
            // routables en repli si la quete est faite. v4 : + carte d'ARRIVEE (m_to) des ScrollAction, dont l'id envoye est un
            // voisin theorique sans carte reelle. v3 (sans arrivee) est ignore : nouveau nom = re-extraction propre.
            // v5 : + ZONES (secteurs) de départ/arrivée et interactif/skill des portes. Démarrage à froid sans v5 : le v4 sert
            // (zones inconnues -> planification au niveau carte, comme avant) jusqu'à la ré-extraction de fond.
            var wgCache = Path.Combine(dataDir, "worldgraph5.json");
            _worldGraph = WorldGraphExtractor.LoadCache(wgCache) ?? WorldGraphExtractor.LoadCache(Path.Combine(dataDir, "worldgraph4.json"));
            _ = Task.Run(() =>
            {
                var wg = WorldGraphExtractor.TryExtract();
                if (wg is { Count: > 0 })
                {
                    _worldGraph = new WorldGraph(wg);
                    try { WorldGraphExtractor.SaveCache(wgCache, wg); } catch { /* cache best-effort */ }
                    TransportLog.Line($"[map] world-graph extrait du client : {wg.Count} cartes avec transitions");
                }
            });
            // Icones de CLASSE (avatar du perso) : petit bundle, resultat visible tout de suite -> propre tache pour
            // etre pret AU PLUS VITE (avant le login), et NON coince derriere le chargement lourd des noms/icones de
            // monstres. Sinon l'identification du perso battait ce chargement -> icone nulle jamais retentee.
            _ = Task.Run(() =>
            {
                Stores.ClassIcons.EnsureLoaded();
                TransportLog.Line($"[classe] bundle symboles charge : {Stores.ClassIcons.SymbolCount} symbol_<N> indexes (0 = bundle absent ou renomme par un patch).");
            });
            // Données de SORTS du moteur de combat : leur propre tâche, en parallèle du chargement complet (le 1er tour n'attend qu'elles).
            _ = Task.Run(_gameNames.EnsureCombatLoaded);
            // Tables noms monstres + PNJ, puis icones de monstres (client), en tache de fond.
            _ = Task.Run(() =>
            {
                _gameNames.EnsureLoaded();
                Stores.MonsterIcons.Init(_gameNames.MonsterGfx);
                Stores.MonsterIcons.EnsureLoaded();
                Stores.InteractiveIcons.Init(sk => _gameNames.InteractiveIconId(sk) is int i && i > 0 ? i : (int?)null);
                Stores.InteractiveIcons.EnsureLoaded();
                Stores.UiIcons.EnsureLoaded();   // icones UI du jeu (onglets, categories d'HDV, caracteristiques) par nom
                TransportLog.Line($"[icones] pretes. Test : skill 68 (Sauge) -> iconId {_gameNames.InteractiveIconId(68)} (0 = donnees non chargees)");
            });
            // Prechauffe l'index map->bundle (les ~3 s de scan) EN AVANCE -> le 1er changement de carte ne le paie plus.
            _ = Task.Run(_cellReader.Prewarm);
            _decoder = new CaptureDecoder(_catalog);
            _decoder.Packet += OnDecoded;
            // MITM OnRelayClosed currently calls decoder.Forget(pid). The
            // Socket FeedFrame delegate exposes no per-session close token,
            // so it cannot claim that its teardown called Forget. Whenever
            // Forget does occur, retire only that LOCAL epoch's diagnostics;
            // an older close must not erase a same-PID new window. No TCP
            // identity/ACK is inferred from this lifecycle callback.
            _decoder.LocalEpochRevoked += (pid, epoch) =>
            {
                _comboIngress?.EndLocalEpoch(pid, epoch);
                QueueCombatTacticalAdvisoryRefresh(pid);
            };
        }
        catch { _decoder = null; }
        return _decoder;
    }

    /// <summary>
    /// MODE SOCKET (clientless) : injecte une trame WIRE (length-prefixed, telle qu'elle circule sur le fil) dans le MEME
    /// decodeur que le MITM, pour le pid SYNTHETIQUE du bot socket. Le flux <see cref="PacketDecoded"/> qui en resulte
    /// peuple EXACTEMENT comme un bot MITM : carte, niveau, serveur, pods, elements interactifs, activite... — tout est
    /// indexe par pid, donc un pid synthetique cohabite sans collision avec les vrais pid des clients MITM.
    /// Best-effort : sans catalogue (pas de session serveur) -> no-op silencieux. Ne leve jamais (ne casse pas le socket).
    /// </summary>
    public void FeedSocketFrame(uint pid, bool clientToServer, byte[] wire)
    {
        // Le nom sort dans OnDecoded, levé de façon synchrone par Feed. Le miroir copie là les trames Socket.
        // Les octets du client visible ne passent pas par ici : ils ne doivent pas piloter le bot.
        try { EnsureDecoder()?.Feed(pid, clientToServer, wire); }
        catch (Exception e) { SocketLog.Info(pid, $"[decode] trame ignoree ({e.GetType().Name})"); }
    }

    // Strictly consultative Socket source/progress projection. The legacy
    // gameplay reducer remains PID-only and has no causal ACK authority.
    private readonly SocketSourceAuthorityLedger _socketSourceAuthority = new();
    private readonly SocketSourceAuthorityLedger _localMitmSourceAuthority =
        new(requiresConnectionToken: false);
    // V20 observes only unique positive Request/Response envelope UIDs from
    // this SAME Socket producer. No event, post-Flush or UID=-1 is a gameplay
    // receipt, and the projection is never dispatched to a gameplay reducer.
    private readonly SocketUidEnvelopeCorrelationV20 _socketUidEnvelopes = new();

    public SocketUidEnvelopeCorrelationV20Snapshot? SocketUidEnvelopeEvidenceOf(uint pid)
        => _socketUidEnvelopes?.Read(pid);

    /// <summary>
    /// Operator-facing state of actual source-stamped local Socket traffic.
    /// A missing result means no currently established linked observation,
    /// not that the session or protocol has been verified.
    /// </summary>
    public SocketSourceAuthoritySnapshot? SocketSourceAuthorityOf(uint pid)
        => _socketSourceAuthority?.Read(pid);

    /// <summary>
    /// Actual LOCAL MITM relay source-issued tunnel traffic, strictly
    /// separate from the unscoped legacy HandshakeRelay and headless adapter.
    /// Captured tunnel != accepted gameplay action or server receipt.
    /// </summary>
    public SocketSourceAuthoritySnapshot? LocalMitmSourceAuthorityOf(uint pid)
        => _localMitmSourceAuthority?.Read(pid);

    internal bool ObserveLocalMitmRelayFrame(
        LocalLaunch.LocalHandshakeRelay.GameRelayLink link,
        long sequence, bool clientToServer, int length,
        Func<LocalLaunch.LocalHandshakeRelay.GameRelayLink, bool>? isCurrent)
    {
        if (isCurrent is null || link.RelayInstanceId == Guid.Empty ||
            link.OwnerPid == 0 || link.RunEpoch <= 0 || link.TunnelId <= 0 ||
            sequence <= 0 || length is <= 0 or > 1_048_576) return false;
        bool Current()
        {
            try { return isCurrent(link); }
            catch { return false; }
        }
        if (!Current()) return false;
        var identity = new CaptureLinkIdentity(
            link.RelayInstanceId, link.OwnerPid, link.RunEpoch, link.TunnelId);
        bool observed = _localMitmSourceAuthority?.ObserveFramedChunk(identity,
            sequence, clientToServer, length) == true;
        if (!Current())
        {
            _localMitmSourceAuthority?.RevokeExact(identity);
            return false;
        }
        return observed;
    }

    /// <summary>
    /// The REAL SocketGameClient's immutable, post-dial framed observation is
    /// joined to the very same SocketProbe lease that owns the socket. Its
    /// ConnectionToken GUID is kept intact: neither synthetic PID nor a hash
    /// of that token constitutes source identity. This second decoder channel
    /// is strictly diagnostic and does not publish into PacketDecoded, game
    /// reducers, ComboIngress authorization or a correlated gameplay ACK.
    /// Legacy FeedSocketFrame continues to update the world model separately.
    /// </summary>
    internal bool FeedSocketObservedFrame(
        Transport.Socket.SocketTransportFrameObservation? observation,
        Func<Transport.Socket.SocketLinkProvenance, bool>? isCurrentSource)
    {
        if (observation is null || isCurrentSource is null ||
            observation.Source.ClientInstance == Guid.Empty ||
            observation.Source.ConnectionToken == Guid.Empty ||
            observation.Source.SyntheticPid == 0 ||
            observation.Source.RunEpoch <= 0 || observation.Sequence <= 0 ||
            observation.WireFrame is not { Length: > 0 and <= 1_048_576 })
            return false;
        var source = observation.Source;
        var link = SocketCaptureLink(source);
        var previousSource = _socketSourceAuthority?.CurrentLink(link.Pid);
        bool IsSameSource(CaptureLinkIdentity candidate)
        {
            if (candidate != link) return false;
            try { return isCurrentSource(source); }
            catch { return false; }
        }
        try
        {
            bool admitted = EnsureDecoder()?.FeedLinkScoped(link, observation.Sequence,
                observation.ClientToServer, observation.WireFrame, IsSameSource) == true;
            if (!admitted)
            {
                // A missing source BEFORE decoder admission must not poison
                // that source forever. CaptureDecoder already revokes its
                // own state on malformed/duplicate accepted sequences; a
                // transiently denied first chunk has never owned a decoder
                // lifetime. Do not fabricate an irreversible revoke here.
                _socketSourceAuthority?.RevokeExact(link);
                _socketUidEnvelopes?.RevokeExact(link);
                return false;
            }
            if (!IsSameSource(link))
            {
                // The producer may revoke or replace the connection while
                // the synchronous diagnostic decoder invokes subscribers.
                // An old source cannot report admission, even though bytes
                // were temporarily parsed earlier in this callback.
                _decoder?.RevokeLink(link);
                _socketSourceAuthority?.RevokeExact(link);
                _socketUidEnvelopes?.RevokeExact(link);
                return false;
            }
            _socketSourceAuthority?.ObserveFramedChunk(
                link, observation.Sequence, observation.ClientToServer,
                observation.WireFrame.Length);
            if (!IsSameSource(link))
            {
                _decoder?.RevokeLink(link);
                _socketSourceAuthority?.RevokeExact(link);
                _socketUidEnvelopes?.RevokeExact(link);
                return false;
            }
            if (!observation.ClientToServer && _catalog is { } responseCatalogue)
                _socketUidEnvelopes?.ObserveServerFrame(link, observation.Sequence,
                    observation.WireFrame, responseCatalogue.FieldNumber);
            if (!IsSameSource(link))
            {
                _decoder?.RevokeLink(link);
                _socketSourceAuthority?.RevokeExact(link);
                _socketUidEnvelopes?.RevokeExact(link);
                return false;
            }
            // A new CURRENT source has replaced a prior Socket connection
            // under the same synthetic PID. Its new bytes must not allow an
            // old multiclasse advisory window to continue or be redispatched.
            if (previousSource is { } oldSource && oldSource != link)
                RevokeTacticalAdvisorOnSocketSourceLoss(link.Pid,
                    "new connection superseded old exact Socket link");
            // The decoder's source-scoped framing admission remains the
            // return contract. Diagnostic projection may be absent in
            // stripped test hosts or decline a duplicate counter; neither
            // case is evidence that the decoder accepted a server ACK.
            return true;
        }
        catch
        {
            // Never let this optional diagnostic observer break the owning
            // clientless TCP connection or force a legacy decoder resend.
            _decoder?.RevokeLink(link);
            _socketSourceAuthority?.RevokeExact(link);
            _socketUidEnvelopes?.RevokeExact(link);
            return false;
        }
    }

    /// <summary>
    /// Called ONLY by the real SocketGameClient outbound drain after both TCP
    /// WriteAsync and FlushAsync returned normally. The receipt remains local
    /// evidence of a completed write, NEVER a server receipt or gameplay ACK.
    /// No PacketDecoded, ComboIngress or gameplay reducer is invoked here.
    /// </summary>
    internal bool FeedSocketWrittenObservation(
        Transport.Socket.SocketTransportWriteObservation? observation,
        Func<Transport.Socket.SocketLinkProvenance, bool>? isCurrentSource)
    {
        if (observation is null || isCurrentSource is null ||
            observation.Source.ClientInstance == Guid.Empty ||
            observation.Source.ConnectionToken == Guid.Empty ||
            observation.Source.SyntheticPid == 0 ||
            observation.Source.RunEpoch <= 0 ||
            !observation.LocalWriteCompleted || observation.ServerAcknowledged ||
            observation.Ordinal <= 0 ||
            observation.WireLength is <= 0 or > 1_048_576 ||
            observation.Wire.IsDefault ||
            observation.Wire.Length != observation.WireLength)
            return false;
        var source = observation.Source;
        bool Current()
        {
            try { return isCurrentSource(source); }
            catch { return false; }
        }
        var link = SocketCaptureLink(source);
        if (!Current())
        {
            _socketSourceAuthority?.RevokeExact(link);
            _socketUidEnvelopes?.RevokeExact(link);
            return false;
        }
        if (!Current())
        {
            // The source changed between admission checks. A previously
            // recorded fact from this same link must not survive the race.
            _socketSourceAuthority?.RevokeExact(link);
            _socketUidEnvelopes?.RevokeExact(link);
            return false;
        }
        bool noted = _socketSourceAuthority?.ObserveEngineWrite(
            link, observation.Ordinal, observation.WireLength) == true;
        if (noted && _catalog is { } requestCatalogue)
            _socketUidEnvelopes?.ObservePostFlush(link, observation.Ordinal,
                observation.CopyWireFrame(), requestCatalogue.FieldNumber);
        if (!Current())
        {
            _socketSourceAuthority?.RevokeExact(link);
            _socketUidEnvelopes?.RevokeExact(link);
            return false;
        }
        return noted;
    }

    /// <summary>Revokes only the exact completed/superseded Socket link.</summary>
    internal void RevokeSocketObservedLink(Transport.Socket.SocketLinkProvenance source)
    {
        if (source.ClientInstance == Guid.Empty ||
            source.ConnectionToken == Guid.Empty || source.SyntheticPid == 0 ||
            source.RunEpoch <= 0) return;
        var link = SocketCaptureLink(source);
        _decoder?.RevokeLink(link);
        _socketUidEnvelopes?.RevokeExact(link);
        bool oldWasCurrent = _socketSourceAuthority?.TryRevokeExact(link) == true;
        if (oldWasCurrent)
            RevokeTacticalAdvisorOnSocketSourceLoss(source.SyntheticPid,
                "current connection stopped or EOF was observed");
    }

    private static CaptureLinkIdentity SocketCaptureLink(
        Transport.Socket.SocketLinkProvenance source)
        => new(source.ClientInstance, source.SyntheticPid, source.RunEpoch,
            source.RunEpoch, source.ConnectionToken);

    /// <summary>
    /// Read-only typed diagnostic adapter for a separately owned headless
    /// BotHostService.TransportFrameObserved event. The caller supplies the
    /// ACTUAL emitting BotHostService and must only inspect this result while
    /// source.IsCurrentTransportLink(link) still holds. This method does not
    /// subscribe to another bot's host, launch anything, or feed _decoder;
    /// headless raw bodies are not causally related to MITM/Socket packets.
    /// PRIME integration must not use this port to authorize casts/ACK or
    /// to populate the ComboIngress server-confirmed observation stream.
    /// </summary>
    public TransportCaptureIngressDiagnostic? InspectHeadlessTransportDiagnostic(
        BotHostService? source, TransportFrameObservation? observation)
        => source is null || observation is null ? null
            : _decoder?.InspectTypedTransportFrame(observation, source.IsCurrentTransportLink);

    // Extraction runtime (bundle client) en tache de fond : si elle aboutit, on remplace la table et on rafraichit
    // le cache. Sinon on garde le cache deja charge (aucune regression). Le swap de reference est atomique.
    private void RefreshMapPositionsFromClient(string cachePath)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var full = MapDataExtractor.TryExtractFull();
                if (full is not { Count: > 0 }) return;
                _mapSubArea = full.ToDictionary(k => k.Key, k => k.Value.SubAreaId);   // Lot I : map_id -> sous-zone (même extraction)
                var xy = new Dictionary<long, (int X, int Y, int WorldMap)>(full.Count);
                foreach (var kv in full) xy[kv.Key] = (kv.Value.X, kv.Value.Y, kv.Value.WorldMap);   // worldMap = verrou de calque (Incarnam vs Astrub)
                _mapPositions = MapPositions.FromDictionary(xy);
                try { MapPositions.SaveCache(cachePath, xy); } catch { /* cache best-effort */ }
                // TABLE PANEL (carte du monde du dashboard web) : { "<mapId>": [posX, posY, worldMap] } REGENEREE a chaque
                // lancement -> se remet a jour a chaque patch Dofus (les positions bougent). Deposee a cote du cache ; le
                // dev web la recopie sous web/public/mapdata/. Best-effort (n'impacte jamais le fonctionnement du bot).
                try
                {
                    var dir = Path.GetDirectoryName(cachePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        MapDataExtractor.WritePanelTable(Path.Combine(dir, "map-positions.json"), full, includeSubArea: false);
                        // Noms de zone (subAreaId -> nom localisé fr/en/es) pour les labels au survol de la carte du panel.
                        var subs = SubAreaExtractor.TryExtract();
                        if (subs.Count > 0)
                        {
                            SubAreaExtractor.WritePanelTables(dir, subs);
                            TransportLog.Line($"[map] noms de zone extraits : {subs.Count} sous-zones (subarea-names.json + i18n)");
                        }
                    }
                }
                catch { /* export panel best-effort */ }
                TransportLog.Line($"[map] positions extraites du client : {full.Count} cartes (cache + table panel map-positions.json)");
            }
            catch { /* extraction best-effort : le cache reste la source */ }
        });
    }

    private void OnDecoded(DecodedPacket p)
    {
        // The legacy decoder delivers packets after releasing its framing
        // lock. Never run ANY gameplay reducer for an already-retired stamped
        // epoch (Stop/reconnect/PID reuse). This is local-epoch hygiene only,
        // not TCP source identity or an independently correlated server ACK.
        // Direct synthetic fixture packets (epoch 0) retain their legacy test
        // contract; production CaptureDecoder stamps positive epochs.
        if (p.DecoderEpoch > 0 && _decoder?.IsCurrentLocalEpoch(p) != true)
            return;
        // Diagnostic-combo provenance is strictly decoder-local. The relay's
        // OnData callback carries no TCP connection token (MITM or Socket).
        // Reject old callbacks after CaptureDecoder.Forget and out-of-order
        // callback delivery; never promote this stamp to a server ACK.
        bool comboPacketAdmitted = _comboIngress?.Admit(p,
            _decoder?.IsCurrentLocalEpoch(p) == true) == true;
        // Snapshot diagnostic mark/turn/start events BEFORE PacketDecoded and
        // other public observers can synchronously re-enter OnDecoded. Otherwise
        // an unrelated nested callback advances the admission watermark and
        // drops a genuine earlier mark, or creates a turn-less diagnostic window.
        // This is only decoder-local; no TCP connection token/cast ACK exists.
        // Do not let any of these reads drive live combat sends.
        if (comboPacketAdmitted && !p.ClientToServer && p.PayloadDecoded)
            _comboIngress?.ObserveRuntimeEvent(p, CurrentFightId(p.Pid),
                _serverMapId.GetValueOrDefault(p.Pid), CharacterId(p.Pid),
                IsInFight(p.Pid), FieldOf);
        // Pid AMBIANT : toutes les lignes TransportLog.Line(msg) émises pendant le traitement de CE paquet (et les Task
        // lancés dans ce contexte : combat, MP...) sont attribuées à ce bot -> Console par-bot, sans toucher chaque appel.
        GivreLauncher.Core.Transport.TransportLog.Scope.Value = p.Pid;
        // Map authority must change even if a downstream observer aborts decoding.
        if (p.ClientToServer && p.Name == "MapChangeRequest")
            NativeTurboRuntime.RevokeTacticalMapReady((int)p.Pid);
        if (p.ClientToServer && p.Name == "MapInformationRequest")
            NativeTurboRuntime.RequestTacticalMapInformation((int)p.Pid);
        if (p.ClientToServer && p.Name == "ContextReadyRequest")
            PublishNativeMapReady(p.Pid, _serverMapId.GetValueOrDefault(p.Pid));
        if (!p.ClientToServer && p.Name == "MapCurrentEvent")
        {
            long cur = _catalog?.FieldNumber("MapCurrentEvent", "map_id") is int mcf
                ? PayloadReader.Int(p.Payload, mcf, 0) : 0;
            ObserveServerMap(p.Pid, cur);
            if (comboPacketAdmitted) _comboIngress?.MapObservedFromPacket(p, cur);
        }
        try { PublishMirrorServerFrame(p); }
        catch { /* le miroir n'interrompt pas le décodage du Socket */ }
        NoteBankSession(p);
        SnowObserve(p);   // compat tiers: dernier paquet par nom, burst de connexion, question PNJ mémorisée AVANT les attentes
        if (!p.ClientToServer && p.Name == "TextInformationEvent" && LastTextInfo(p.Pid) is { } textInfo)
            NoteTurboCastRejection(p.Pid, textInfo.MessageId, textInfo.Params);
        PacketDecoded?.Invoke(p);
        // An observer can synchronously reenter Forget; do not continue into
        // LotM, movement, combat or HDV reducers with a revoked stamped packet.
        if (p.DecoderEpoch > 0 && _decoder?.IsCurrentLocalEpoch(p) != true)
            return;
        OnLotMPacket(p);
        // CAPTURE DÉCOUVERTE (toggle Réglages, OFF par défaut) : dump l'arbre de champs des messages non-mappés et
        // d'échange/vente -> transport-*.log, pour relever codes + numéros de champ réels en reversant le HDV.
        // Outil de reverse (codes, trames brutes) : licence DÉVELOPPEUR uniquement, même si le réglage est resté sur On.
        if (AppConfig.Current.PacketDump && LicenseService.IsDeveloper && DiscoveryDumpStillAllowed() && IsDiscoveryTarget(p)) DumpDiscoveryPacket(p);
        // Horodate le dernier paquet de combat reçu du serveur -> sert à WaitMoveClear (attendre que le flux se calme).
        if (!p.ClientToServer && _inFight.TryGetValue(p.Pid, out var _inf) && _inf) _lastFightPkt[p.Pid] = System.Environment.TickCount64;
        // Accusé du client visible (MITM) : noté comme « action accusée » (la barrière Turbo l'accepte même s'il précède la fin de
        // séquence décodée) et mesuré. En Turbo-ack, le bot accuse lui-même ; le doublon du client est retiré du flux (DropClientAck).
        if (p.ClientToServer && p.Name == "GameActionAcknowledgementRequest" && TurboTrackingEnabled(p.Pid))
        {
            long now = TurboNow();
            long ackAction = FieldOf("GameActionAcknowledgementRequest", "action_id") is int ackActionF
                ? PayloadReader.Int(p.Payload, ackActionF, 0) : 0;
            long seqToAck = _turboLastSelfSeqEnd.TryGetValue(p.Pid, out var seq)
                && seq.ActionId != 0 && seq.ActionId == ackAction ? now - seq.At : -1;
            _turboLastAck[p.Pid] = (ackAction, now);
            NoteTurboAcked(p.Pid, ackAction);
            PulseTurboBarrier(p.Pid);
            TransportLog.Line($"[turbo-metric] ack_tx action={ackAction} t={now} sequence_end_to_ack_ms={(seqToAck >= 0 ? seqToAck : -1)}");
        }
        // Debloque une sequence banque/PNJ en attente d'un event entrant precis (ios/iwb...).
        // Le waiter ne se termine QUE par AcceptCodePacket. OnLotMPacket a déjà vu le paquet : un
        // AchievementRewardResultEvent qui ne matche pas l'id attendu met quand même son succès à jour.
        if (!p.ClientToServer && p.Name is not null && _codeWait.TryGetValue(p.Pid, out var cw) &&
            cw.Names.Contains(p.Name) && AcceptCodePacket(cw.Accept, p) &&
            ((ICollection<KeyValuePair<uint, (HashSet<string> Names, Func<DecodedPacket, bool>? Accept,
                TaskCompletionSource<DecodedPacket> Tcs)>>)_codeWait).Remove(
                new KeyValuePair<uint, (HashSet<string> Names, Func<DecodedPacket, bool>? Accept,
                    TaskCompletionSource<DecodedPacket> Tcs)>(p.Pid, cw)))
            cw.Tcs.TrySetResult(p);
        // Capture la VRAIE trame /whoami du client (lqb sortant) -> rejeu exact ensuite (auto + bouton), 100% fiable.
        if (p.ClientToServer && (p.Name == "WhoAmIRequest") && !ReferenceEquals(p.RawFrame, _whoAmiTemplate))
        {
            _whoAmiTemplate = p.RawFrame;
            TransportLog.Line($"[GivreClone] WhoAmI : trame reelle CAPTUREE ({p.RawFrame.Length}o) -> rejeu fiable actif");
        }
        // DEPLACEMENT - machine a etats alignee sur un bot tiers (client dual-brain PRESENT):
        //  - jqi (C->S) : requête locale du client, jamais une réponse serveur ni une résolution du trajet.
        //  - jrw (C->S) : deplacement SORTANT (injecte OU manuel du client) -> on recale la position self sur sa destination.
        //  - jsq (S->C) : réponse sans identifiant individuel -> diagnostic uniquement, aucun état/acte ne dépend d'elle.
        //  - jqt (S->C) : REFUS -> on recale la position sur la cellule REELLE (verite serveur) + on libere la garde.
        if (p.ClientToServer && (p.Name == "MapMovementConfirmRequest"))
        {
            // Observed client request prevents our fallback from sending a
            // duplicate jqi, but it cannot retire the move ticket. The server
            // response has no individual request UID and remains uncorrelated.
            if (_botMoveConfirm.TryGetValue(p.Pid, out var pendingConfirm))
                _moveConfirmSubmitted[p.Pid] = pendingConfirm.Seq;
            TransportLog.Line("[move-v21] MapMovementConfirmRequest client observée (requête locale seulement) ; trajet conservé en attente d'une résolution attribuable");
        }
        else if (p.ClientToServer && (p.Name == "MapMovementRequest"))
        {
            _movingUntil[p.Pid] = Environment.TickCount64 + MoveWindowMs;   // déplacement (manuel ou bot) en cours -> activité "Déplacement" (le client n'émet un jrw que pour SON perso)
            // Deplacement MANUEL du client : on pilote le marqueur du perso sur le meme chemin (deroule).
            // v4 : MapMovementRequest.key_cells = #1 (le #2 = cautious, un bool) -> lire le chemin packé au #1.
            var path = MapPathfinder.ExpandPath(MapMovementReader.Cells(p.Payload, FNum("MapMovementRequest", "key_cells")));
            if (path.Count > 0)
            {
                SelfMoved?.Invoke(p.Pid, path);
                TransportLog.Line($"[move] MapMovementRequest sortant ({p.RawFrame.Length}o) -> perso pilote jusqu'a {path[path.Count - 1]}"
                    + (MoveDiag ? $" ; chemin CLIENT (manuel)=[{string.Join(">", path)}] ({path.Count} cases)" : ""));
            }
        }
        else if (!p.ClientToServer && (p.Name == "MapMovementConfirmResponse"))
        {
            // V21: the S->C response does NOT contain the individual request
            // UID, movement sequence or source ticket. A delayed response to
            // move A can be received while map/harvest continuation B is
            // pending under the SAME PID, map and actor. Not even a matching
            // self MapMovementEvent proves that THIS confirmation belongs to
            // B. Preserve the pending continuation and V9 recovery quarantine;
            // do not send a map change/use, clear a confirm or settle B.
            if (TryReleaseOwnConfirmedMove(p.Pid)) return;
            bool mapPending, harvestPending, attackPending;
            lock (ArrivalActionGate)
            {
                mapPending = _pendingMapChange.ContainsKey(p.Pid);
                harvestPending = _pendingHarvest.ContainsKey(p.Pid);
                attackPending = _pendingAttack.ContainsKey(p.Pid);
            }
            string map = mapPending
                ? InspectMapConfirmAttributionV21(p.Pid, harvest: false, p) : "none";
            string harvest = harvestPending
                ? InspectMapConfirmAttributionV21(p.Pid, harvest: true, p) : "none";
            string move = _botMoveConfirm.TryGetValue(p.Pid, out var pendingMove)
                ? $"seq={pendingMove.Seq}" : "none";
            // This event has no per-request UID. Even a matching actor/path
            // echo cannot identify which confirm it answers when an earlier
            // response is delayed or replayed. Keep ALL move/action state
            // untouched: no guard release, SettleMove, map/harvest send, or
            // pending attack consumption. Position remains sourced from the
            // independent MapMovementEvent reducer.
            TransportLog.Line($"[move-v21] pid {p.Pid} : MapMovementConfirmResponse non attribuable individuellement; " +
                $"move={move}, map={map}, harvest={harvest}, attack={(attackPending ? "pending" : "none")}; " +
                "diagnostic uniquement, aucun état/action/settlement modifié");
        }
        else if (!p.ClientToServer && (p.Name == "MapMovementRefusedEvent"))
        {
            NoteMoveRefusedForBreaker(p.Pid);   // même trajet refusé en boucle -> ne plus le renvoyer (spam serveur)
            ApplyMovementRefusal(p.Pid, MapMovementRefusedReader.Read(p.Payload,
                FNum("MapMovementRefusedEvent", "cell_x"), FNum("MapMovementRefusedEvent", "cell_y")));
        }
        // Recolte : StatedElementUpdatedEvent (iwf) -> reflet live de l'etat d'un interactif (dispo -> epuise).
        if ((p.Name == "StatedElementUpdatedEvent") && MapInteractivesReader.ReadStatedUpdate(p.Payload) is { } upd)
            MapInteractiveUpdated?.Invoke(p.Pid, upd.ElementId, upd.Cell, upd.State);

        // Recolte : InteractiveElementUpdatedEvent (iwm) -> les SKILLS changent (actif -> desactive quand epuise/en cours) :
        // on met a jour le losange (vert dispo -> gris epuise). Best-effort : no-op si la trame ne matche pas la forme attendue.
        if ((p.Name == "InteractiveElementUpdatedEvent") && MapInteractivesReader.ReadElementSkillsUpdate(p.Payload) is { } su)
        {
            MapInteractiveSkillsUpdated?.Invoke(p.Pid, su.ElementId, su.Skills);
            if (su.Skills.Any(s => s.SkillId == ZaapiTravel.SkillId && s.Enabled) && CurrentMap(p.Pid) is long curMap && curMap > 0)
                ZaapiRegistry.Register(curMap, SubAreaOf(curMap));
            TransportLog.Line($"[interactifs] maj skills element {su.ElementId} : " +
                string.Join(", ", su.Skills.Select(s => $"skill {s.SkillId}={(s.Enabled ? "actif" : "desactive")}")));
        }

        // Pods : InventoryWeightEvent (iun) -> poids actuel (#1) / max (#3). Message PERSONNEL (envoye a moi seul).
        if (p.Name == "InventoryWeightEvent")
        {
            int cur = (int)PayloadReader.Int(p.Payload, FNum("InventoryWeightEvent", "inventory_weight"), 0), max = (int)PayloadReader.Int(p.Payload, FNum("InventoryWeightEvent", "weight_max"), 0);
            if (max > 0) { _lastPods[p.Pid] = (cur, max); PodsUpdated?.Invoke(p.Pid, cur, max); }
        }

        // ÉQUIPE : invitation de groupe REÇUE -> le VM auto-accepte si elle vient de NOTRE chef (champs par nom via le mapping streamé).
        if (!p.ClientToServer && p.Name == "PartyInvitationEvent")
        {
            long fromId = PayloadReader.Int(p.Payload, FNum("PartyInvitationEvent", "from_player_id"), 0);
            long partyId = PayloadReader.Int(p.Payload, FNum("PartyInvitationEvent", "party_id"), 0);
            string fromName = PayloadReader.Str(p.Payload, FNum("PartyInvitationEvent", "from_player_name")) ?? "";
            TransportLog.Line($"[équipe] invitation REÇUE pid {p.Pid} de {fromName}({fromId}) -> party {partyId}");
            PartyInvited?.Invoke(p.Pid, fromId, fromName, partyId);
        }
        // Lot G : état du groupe (membres / chef / party_id) pour team:isInParty / partyId / partyMembers.
        OnPartyPacket(p);
        ObserveNativeFollow(p);
        // Lot D : ÉCHANGE joueur (demande reçue / ouvert / contenu / prêt / fin) -> état par pid + mode mule d'équipe.
        if (!p.ClientToServer && p.Name is not null && p.Name.StartsWith("Exchange", StringComparison.Ordinal)) OnExchangePacket(p);
        // Lot E : options de combat, respawn zaap, défis reçus, variantes de sort, popup de modération (lecture + refus auto).
        OnLotEPacket(p);
        // Lot F : PV hors combat (UpdateLifePoints), taux de régénération, emotes (assis) jouées / refusées.
        OnLotFPacket(p);
        // Combat de GROUPE : un membre entre en combat -> le follower peut le REJOINDRE (auto-join). L'event porte fight_id
        // (#7) + member_id (#6 = le combattant/chef), donc on n'a PAS besoin de la carte courante du chef (qui devient
        // l'instance de combat pendant le combat -> l'ancien test d'égalité de carte échouait toujours).
        // Résolu PAR NOM uniquement (mapping streamé). Les anciens replis par code obfusqué (ijr, kti, jqt…) ont été retirés le
        // 03/10 : les codes changent à chaque patch, et sur le mapping v5 plusieurs désignaient un AUTRE message (collisions).
        if (!p.ClientToServer && (p.Name == "PartyMemberInFightEvent"))
        {
            long memberId = PayloadReader.Int(p.Payload, FNum("PartyMemberInFightEvent", "member_id"), 0);
            long fightId = PayloadReader.Int(p.Payload, FNum("PartyMemberInFightEvent", "fight_id"), 0);
            if (memberId != 0 && fightId != 0) PartyMemberInFight?.Invoke(p.Pid, memberId, fightId);
        }
        // MP PRIVÉ entrant : ChatChannelMessageEvent (kti) sur le canal PRIVÉ (10). Champs LUS DU MAPPING v4 :
        // content=#1, sender_name=#5, sender_character_id=#6, channel=#9. ⚠ L'ancien code lisait content au #7
        // (=from_admin), le nom au #4 (=sender_account_id) et l'id au #5 (=sender_name) -> MP illisibles depuis le
        // patch. On EXCLUT l'écho de nos propres MP (kth, autre code -> ne passe jamais ici).
        if (!p.ClientToServer && (p.Name == "ChatChannelMessageEvent")
            && PayloadReader.Int(p.Payload, FNum("ChatChannelMessageEvent", "channel"), -1) == 10)
        {
            string sender = PayloadReader.Str(p.Payload, FNum("ChatChannelMessageEvent", "sender_name")) ?? "";
            string content = PayloadReader.Str(p.Payload, FNum("ChatChannelMessageEvent", "content")) ?? "";
            long senderId = PayloadReader.Int(p.Payload, FNum("ChatChannelMessageEvent", "sender_character_id"), 0);
            if (sender.Length > 0 && content.Length > 0) PrivateMessageReceived?.Invoke(p.Pid, sender, content, senderId);
        }
        // Recolte : ObjectHarvestedEvent (itn) -> objet (#1 gid) + quantite (#2). PERSONNEL -> c'est forcement MA recolte
        // (le serveur ne l'envoie qu'au recolteur, pas d'id proprietaire a verifier). On log "recolte de X".
        if (p.Name == "ObjectHarvestedEvent")
        {
            int gid = (int)PayloadReader.Int(p.Payload, FNum("ObjectHarvestedEvent", "object_gid"), 0), qty = (int)PayloadReader.Int(p.Payload, FNum("ObjectHarvestedEvent", "quantity"), 0);
            if (gid != 0)
            {
                string item = _gameNames.ItemName(gid) ?? $"objet {gid}";
                string pods = _lastPods.TryGetValue(p.Pid, out var w) ? $" (pods {w.Cur}/{w.Max})" : "";
                TransportLog.Line($"[recolte] +{qty} {item}{pods}");
                ResourceHarvested?.Invoke(p.Pid, gid, qty, _gameNames.ItemIconId(gid), item);
            }
        }
        // Recolte AVEC BONUS : ObjectHarvestedWithBonusEvent (iwc). Champs v4 : object_gid(#2), quantity(#3),
        // bonus_quantity(#4). ⚠ L'ancien code lisait le bonus au #1 (=fris, PAS le bonus) -> total faux. N'emet PAS
        // d'itn -> sans ce handler la recolte bonus n'etait pas comptee. Total = qty + bonus.
        if (p.Name == "ObjectHarvestedWithBonusEvent")
        {
            int bonus = (int)PayloadReader.Int(p.Payload, FNum("ObjectHarvestedWithBonusEvent", "bonus_quantity"), 0);
            int gid = (int)PayloadReader.Int(p.Payload, FNum("ObjectHarvestedWithBonusEvent", "object_gid"), 0);
            int qty = (int)PayloadReader.Int(p.Payload, FNum("ObjectHarvestedWithBonusEvent", "quantity"), 0);
            int total = qty + bonus;
            if (gid != 0 && total > 0)
            {
                string item = _gameNames.ItemName(gid) ?? $"objet {gid}";
                string pods = _lastPods.TryGetValue(p.Pid, out var w) ? $" (pods {w.Cur}/{w.Max})" : "";
                TransportLog.Line($"[recolte] +{total} {item} (bonus {bonus}){pods}");
                ResourceHarvested?.Invoke(p.Pid, gid, total, _gameNames.ItemIconId(gid), item);
            }
        }
        // ===== INVENTAIRE (modele uid->(gid,qty) pour getInventoryItemCount / useInventoryItem / dropItem).
        // Le wire ne porte que l'UID -> ce store convertit GID->UID. Numeros de champ confirmes capture (InventoryReader).
        NoteItemEffects(p);   // effets (caractéristiques) de chaque exemplaire, sac et coffre -> infobulles de l'inventaire
        ObserveHdvMergedInventory(p);
        if (p.Name == "InventoryContentEvent")        // ivx : snapshot complet au login -> (re)seed
        {
            SeedHdvOrBagInventory(p);
            PruneItemEffects(p.Pid);
            if (InventoryReader.ReadKamas(p.Payload) is { } kamas) SetKamas(p.Pid, kamas);
            TransportLog.Line($"[inventaire] pid {p.Pid} : snapshot recu -> {Inventory.StackCount(p.Pid)} stacks, {(_kamas.TryGetValue(p.Pid, out var kk) ? kk.ToString() : "?")} kamas");
            HdvUpdated?.Invoke(p.Pid);   // le sac+banque a changé (ex : afficher la banque dans le HDV envoie un ivx) -> rafraîchit l'onglet HDV
        }
        else if (p.Name == "ObjectAddedEvent")        // iua : nouvel objet / butin / ramassage
        {
            if (InventoryReader.ReadAdded(p.Payload) is { } it
                && (!HdvBankInventoryReady(p.Pid) || Inventory.Get(p.Pid, it.Uid) is not null || it.Position != 63))
                Inventory.Upsert(p.Pid, new InventoryStore.Item(it.Uid, it.Gid, it.Qty, it.Position));
            NotifyHdvInventory(p.Pid);
        }
        else if (p.Name == "ObjectQuantityEvent")     // ivj : maj quantite d'un stack existant (quantité absente = 0 = pile vidée)
        {
            if (InventoryReader.ReadQuantity(p.Payload) is { } q) Inventory.ApplyQuantity(p.Pid, q.Uid, q.Qty);
            PruneItemEffects(p.Pid);
            NotifyHdvInventory(p.Pid);
        }
        else if (p.Name == "ObjectDeletedEvent")      // ium : stack vide/retire
        {
            if (InventoryReader.ReadDeletedUid(p.Payload) is { } u) Inventory.Remove(p.Pid, u);
            PruneItemEffects(p.Pid);
            NotifyHdvInventory(p.Pid);
        }
        else if (p.Name == "ObjectModifiedEvent")     // objet modifié sur place (forgemagie, objet vivant...) : effets relus par NoteItemEffects
        {
            NotifyHdvInventory(p.Pid);
        }
        else if (p.Name == "ObjectMovementEvent")     // iuf : objet équipé/déséquipé -> position (63 = sac). proto3 : absent = 0 (= amulette), pas « inconnu ».
        {
            if (InventoryReader.ReadMovement(p.Payload) is { } mv) Inventory.SetPosition(p.Pid, mv.Uid, mv.Position);
            NotifyHdvInventory(p.Pid);
        }
        // Versions EN LOT des événements du sac : un dépôt/retrait banque PAR LISTE (bank:putAllItemsExcept, putAllItems,
        // exchange:getAllItems) vide/remplit le sac par ObjectsDeleted/ObjectsQuantity/ObjectsAdded, PAS par les unitaires.
        // Sans ces branches le modèle restait PÉRIMÉ jusqu'au prochain snapshot complet : getInventoryItemCount comptait encore
        // les objets déposés (un script « avant/après craft » concluait à tort « rien obtenu »). Lecture PAR NOM (fields.json).
        // Une ligne de journal par lot (événements rares : opérations de banque) -> validation en jeu sans capture brute.
        else if (p.Name == "ObjectsDeletedEvent")
        {
            var uids = InventoryReader.ReadDeletedUids(p.Payload);
            foreach (var u in uids) Inventory.Remove(p.Pid, u);
            PruneItemEffects(p.Pid);
            TransportLog.Line($"[inventaire] pid {p.Pid} : lot -> {uids.Count} pile(s) retirée(s) du sac ({Inventory.StackCount(p.Pid)} restantes)");
            NotifyHdvInventory(p.Pid);
        }
        else if (p.Name == "ObjectsQuantityEvent")
        {
            var qs = InventoryReader.ReadQuantities(p.Payload);
            foreach (var q in qs) Inventory.ApplyQuantity(p.Pid, q.Uid, q.Qty);   // absent = 0 (proto3) = pile vidée
            PruneItemEffects(p.Pid);
            TransportLog.Line($"[inventaire] pid {p.Pid} : lot -> {qs.Count} quantité(s) de pile mise(s) à jour");
            NotifyHdvInventory(p.Pid);
        }
        else if (p.Name == "ObjectsAddedEvent")
        {
            var added = InventoryReader.ReadAddedList(p.Payload);
            foreach (var it in added)
                if (!HdvBankInventoryReady(p.Pid) || Inventory.Get(p.Pid, it.Uid) is not null || it.Position != 63)
                    Inventory.Upsert(p.Pid, new InventoryStore.Item(it.Uid, it.Gid, it.Qty, it.Position));
            TransportLog.Line($"[inventaire] pid {p.Pid} : lot -> {added.Count} pile(s) ajoutée(s) au sac ({Inventory.StackCount(p.Pid)} au total)");
            NotifyHdvInventory(p.Pid);
        }

        // ===== BANQUE / COFFRE : contenu du coffre (StorageInventoryContentEvent iwb, objets répétés au champ #1) reçu à
        // l'ouverture de la banque -> alimente l'onglet Banque. StorageObjectRemovedEvent (itc) retire un stack.
        // Lot J : un COFFRE À CODE (maison) ouvert par lockedStorage émet les mêmes Storage* que la banque -> on ne touche PAS
        // le store Bank (bank:items/count/getKamas + onglet Banque restent ceux de la banque). Couvre les 6 branches ci-dessous.
        else if (!p.ClientToServer && p.Name is { } stName && stName.StartsWith("Storage", StringComparison.Ordinal) && _chestSession.ContainsKey(p.Pid))
        {
            TransportLog.Line($"[coffre] pid {p.Pid} : {p.Name} (coffre à code, banque non modifiée)");
        }
        else if (p.Name == "StorageInventoryContentEvent")   // iwb : contenu complet du coffre à l'ouverture
        {
            ApplyStorageContent(p);   // no-op si BankOpenAsync l'a déjà appliqué (même paquet)
            PruneItemEffects(p.Pid);
        }
        else if (p.Name == "StorageObjectRemovedEvent")      // itc : un stack retiré du coffre
        {
            int u = (int)PayloadReader.Int(p.Payload, FNum("StorageObjectRemovedEvent", "object_uid"), 0);
            if (u != 0) { Bank.Remove(p.Pid, u); BankUpdated?.Invoke(p.Pid); }
            PruneItemEffects(p.Pid);
        }
        // Lot D : mises à jour INCRÉMENTALES du coffre ouvert (dépôt/retrait partiel) -> bank:count/items restent justes sans
        // rouvrir la banque. Champs résolus par nom (ObjectItemInventory lu comme le snapshot).
        else if (p.Name == "StorageObjectUpdateEvent")
        {
            if (InventoryReader.ReadEntryAt(p.Payload, FNum("StorageObjectUpdateEvent", "object")) is { } it)
            { Bank.Upsert(p.Pid, new InventoryStore.Item(it.Uid, it.Gid, it.Qty, it.Position)); BankUpdated?.Invoke(p.Pid); }
        }
        else if (p.Name == "StorageObjectsUpdateEvent")
        {
            foreach (var it in InventoryReader.ReadContentAt(p.Payload, FNum("StorageObjectsUpdateEvent", "objects")))
                Bank.Upsert(p.Pid, new InventoryStore.Item(it.Uid, it.Gid, it.Qty, it.Position));
            BankUpdated?.Invoke(p.Pid);
        }
        else if (p.Name == "StorageObjectsRemovedEvent")
        {
            foreach (var u in InventoryReader.ReadUids(p.Payload, FNum("StorageObjectsRemovedEvent", "objects_uid"))) Bank.Remove(p.Pid, u);
            PruneItemEffects(p.Pid);
            BankUpdated?.Invoke(p.Pid);
        }
        // Kamas du COFFRE mis à jour à chaque dépôt/retrait (StorageKamasUpdateEvent) : le NOUVEAU total du coffre au
        // champ #1. Complète le snapshot iwb (#2) reçu à l'ouverture -> l'onglet Banque reste à jour sans rouvrir la banque.
        else if (p.Name == "StorageKamasUpdateEvent")        // ivo
        {
            // proto3 OMET le zéro : coffre vidé à 0 kamas -> le paquet arrive SANS le champ #1. Absent = 0, pas "inconnu".
            long bk = PayloadReader.Int(p.Payload, FNum("StorageKamasUpdateEvent", "kamas"), 0);
            if (bk != BankKamas(p.Pid))
            {
                _bankKamas[p.Pid] = bk;
                TransportLog.Line($"[banque] pid {p.Pid} : {bk} kamas (StorageKamasUpdateEvent)");
                BankUpdated?.Invoke(p.Pid);
            }
        }

        // ===== MÉTIERS : niveau par métier. JobExperiencesUpdateEvent porte les JobExperience{job_id, job_level} du perso
        // (maj après récolte/craft) ; JobLevelUpEvent annonce une montée. Numéros résolus par nom (fields.json). Sert getJobLevel.
        else if (p.Name == "JobExperiencesUpdateEvent")   // iqk-famille : maj d'XP/niveau de MES métiers
        {
            int expF = FNum("JobExperiencesUpdateEvent", "experiences");
            int jidF = FNum("JobExperience", "job_id"), jlvlF = FNum("JobExperience", "job_level");
            int xpF = FNum("JobExperience", "job_xp");
            int floorF = FNum("JobExperience", "job_xp_level_floor");
            int nextF = FNum("JobExperience", "job_xp_next_level_floor");
            var jobs = _jobLevels.GetOrAdd(p.Pid, static _ => new());
            var progress = _jobProgress.GetOrAdd(p.Pid, static _ => new());
            foreach (var f in p.Payload)
                if (expF > 0 && f.Field == expF && f.Message is { } je)
                {
                    int jid = (int)PayloadReader.Int(je, jidF, 0);
                    int level = (int)PayloadReader.Int(je, jlvlF, 0);
                    if (jid <= 0 || level <= 0) continue;
                    jobs[jid] = level;
                    // Les champs protobuf de valeur 0 peuvent être omis ; seul le
                    // plafond XP du niveau suivant permet d'admettre une progression.
                    long? xp = nextF > 0 && je.Any(x => x.Field == nextF) && xpF > 0
                        ? PayloadReader.Int(je, xpF, 0) : null;
                    long? floor = xp.HasValue && floorF > 0 ? PayloadReader.Int(je, floorF, 0) : null;
                    long? next = xp.HasValue ? PayloadReader.Int(je, nextF, 0) : null;
                    progress[jid] = new JobProgressSnapshot(level, xp, floor, next);
                    if (_smithProgressRevisions is not null) NoteSmithJobProgress(p.Pid, jid);
                }
        }
        else if (p.Name == "JobLevelUpEvent")   // iqv : { new_level:1, job:2 (JobDescription -> job_id) }
        {
            int lvl = (int)PayloadReader.Int(p.Payload, FNum("JobLevelUpEvent", "new_level"), 0);
            var jobMsg = p.Payload.FirstOrDefault(f => f.Field == FNum("JobLevelUpEvent", "job"))?.Message;
            int jid = jobMsg is null ? 0 : (int)PayloadReader.Int(jobMsg, FNum("JobDescription", "job_id"), 0);
            if (jid != 0 && lvl > 0)
            {
                _jobLevels.GetOrAdd(p.Pid, static _ => new())[jid] = lvl;
                _jobProgress.GetOrAdd(p.Pid, static _ => new()).AddOrUpdate(jid,
                    _ => new JobProgressSnapshot(lvl), (_, old) => old.WithLevelUp(lvl));
                JobLeveledUp?.Invoke(p.Pid, jid, lvl);
            }
        }
        // ===== DIALOGUE PNJ : mémorise le dernier NpcDialogQuestionEvent (réponses visibles) pour replyNpc ; oublie à la fermeture.
        else if (p.Name == "NpcDialogQuestionEvent") _lastNpcDialog[p.Pid] = p.Payload;
        else if (p.Name == "DialogLeaveEvent") _lastNpcDialog.TryRemove(p.Pid, out _);

        // ===== HÔTEL DE VENTE (HDV) : prix marché + MES lots en vente (observabilité read-only, alimente l'onglet HDV).
        // Codes/champs capturés en jeu (docs/hdv-protocol-capture.md) ; noms résolus par le mapping (streamé / confirmed.json DEBUG).
        else if (p.Name == "ExchangeBidBuyerStartedEvent")     // kdw : HDV ouvert ACHETEUR (mode par défaut à l'ouverture du hôtelier)
        {
            SetHdvBuyerMode(p.Pid);   // ferme aussi le vendeur et invalide son stock fusionné
        }
        else if (p.Name == "ExchangeTypesItemsExchangerDescriptionForUserEvent")   // jzn : offres ACHETEUR pour un gid recherché
        {
            // Preserve every exemplar, with its own selling prices and actual effects.
            int itemsF = FNum("ExchangeTypesItemsExchangerDescriptionForUserEvent", "item_descriptions");
            int gidF = FNum("BidExchangerObject", "object_gid");
            int uidF = FNum("BidExchangerObject", "object_uid");
            int pricesF = FNum("BidExchangerObject", "prices");
            var offers = new List<BidHouseStore.BuyerOffer>();
            foreach (var f in p.Payload)
                if (f.Field == itemsF && f.Message is { } desc)
                {
                    long gid = PayloadReader.Int(desc, gidF, 0);
                    int uid = (int)PayloadReader.Int(desc, uidF, 0);
                    if (gid <= 0 || uid <= 0) continue;
                    var prices = new List<long>();
                    foreach (var price in desc.Where(x => x.Field == pricesF))
                        if (price.Bytes is { Length: > 0 } packed) prices.AddRange(ReadPackedVarints(packed));
                        else if (price.Wire == GivreCore.Protobuf.WireType.Varint) prices.Add(unchecked((long)price.Varint));
                    offers.Add(new(uid, gid, prices.ToArray(), InventoryReader.ReadEffects(desc, "BidExchangerObject")));
                }
            foreach (var group in offers.GroupBy(x => x.Gid)) Hdv.SeedBuyerOffers(p.Pid, group.Key, group);
            HdvUpdated?.Invoke(p.Pid);
        }
        else if (p.Name == "ExchangeBidHouseBuyResultEvent")   // résultat d'un achat (object_uid, bought) -> HDV Tracker « buy »
        {
            int uid = (int)PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseBuyResultEvent", "object_uid"), 0);
            bool bought = PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseBuyResultEvent", "bought"), 0) != 0;
            HdvTrackBuyResult(p.Pid, uid, bought);
        }
        else if (p.ClientToServer && p.Name == "ExchangeBidHouseBuyRequest")   // achat MANUEL dans le client (MITM) : lot visé
        {
            HdvTrackBuyRequest(p.Pid, (int)PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseBuyRequest", "object_uid"), 0),
                               (int)PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseBuyRequest", "quantity"), 0),
                               PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseBuyRequest", "price"), 0));
        }
        else if (p.Name == "ExchangeBidPriceEvent")   // v4 : object_gid(#2), average_price(#3), bid_price_for_seller(#1){ #1 = prix min par lot packed }
        {
            // ⚠ v4 : l'ancien code lisait gid au #3 (=average_price), avg au #4 (=fwux), le conteneur au #5 (absent) ->
            // prix HDV faux/vides. Numéros résolus du mapping.
            long gid = PayloadReader.Int(p.Payload, FNum("ExchangeBidPriceEvent", "object_gid"), 0);
            long avg = PayloadReader.Int(p.Payload, FNum("ExchangeBidPriceEvent", "average_price"), 0);
            if (gid != 0)
            {
                Hdv.SetAverage(p.Pid, gid, avg);
                if (avg > 0) MarketPriceUpdated?.Invoke(gid, avg);   // -> persistance prix pour l'estimation valeur ressources
                int containerF = FNum("ExchangeBidPriceEvent", "bid_price_for_seller");
                var nested = p.Payload.FirstOrDefault(f => f.Field == containerF)?.Message;   // BidPriceForSeller
                var minField = nested?.FirstOrDefault(g => g.Field == FNum("BidPriceForSeller", "minimal_prices"));   // repeated int64 packed
                long[]? mins = null;
                if (minField?.Bytes is { Length: > 0 } packed)                          // repeated int64 PACKED = les prix min
                {
                    mins = ReadPackedVarints(packed);
                    if (mins.Length > 0) Hdv.SetMarketPrices(p.Pid, gid, mins);
                }
                if (avg > 0 || mins is { Length: > 0 }) EmitHdv("price", p.Pid, gid, avg: avg, min: mins);   // HDV Tracker (1 / serveur+gid / 15 min)
                HdvUpdated?.Invoke(p.Pid);
            }
        }
        else if (p.Name == "ExchangeBidSellerStartedEvent")    // kby : HDV ouvert vendeur + MES lots en cours
        {
            // 100% mapping-driven : les sous-messages HDV SONT dans fields.json sous leur vrai nom proto (extraction auto).
            //   ExchangeBidSellerStartedEvent.objects (#2, repeated) -> ItemToSellInBid{ object 1 (BidItem), price 2, unsold_delay 3 }
            //   BidItem{ uid 1, gid 2, quantity 3, effects 5 }
            // L'ancien code lisait l'objet à entry.#2 (= le PRIX) -> info=null -> « 0 lot en vente ». Résolu par nom.
            var listings = new List<BidHouseStore.Listing>();
            int hidden = 0;
            int objectsF = FNum("ExchangeBidSellerStartedEvent", "objects");
            int objF = FNum("ItemToSellInBid", "object"), priceF = FNum("ItemToSellInBid", "price");
            int uidF = FNum("BidItem", "uid"), gidF = FNum("BidItem", "gid"), qtyF = FNum("BidItem", "quantity");
            foreach (var f in p.Payload)
                if (f.Field == objectsF && f.Message is { } entry)
                {
                    var info = entry.FirstOrDefault(g => g.Field == objF)?.Message;   // BidItem
                    if (info is null) continue;
                    var l = new BidHouseStore.Listing(
                        (int)PayloadReader.Int(info, uidF, 0), PayloadReader.Int(info, gidF, 0),
                        (int)PayloadReader.Int(info, qtyF, 0), PayloadReader.Int(entry, priceF, 0));
                    // Entrée SANS quantité ni prix (vue en jeu : « Blé x0 », uid minuscule, retrait sans effet) : ce n'est pas un lot
                    // vendable -> jamais affichée ni touchée (retrait/reprix), mais comptée comme place PEUT-ÊTRE occupée.
                    if (l.Uid <= 0 || l.Gid <= 0 || l.Qty <= 0 || l.Price <= 0) { hidden++; continue; }
                    listings.Add(l);
                }
            Hdv.SeedMine(p.Pid, listings);
            HdvTrackSeed(p.Pid, listings);   // HDV Tracker : lots déjà en vente = leur propre lot d'origine
            _hdvHiddenLots[p.Pid] = hidden;
            _hdvOpen[p.Pid] = true; _hdvAnyOpen[p.Pid] = true; _hdvBuyerOpen[p.Pid] = false; _hdvBuyerType[p.Pid] = 0;   // bascule VENDEUR -> plus en mode achat
            HdvUpdated?.Invoke(p.Pid);
            TransportLog.Line($"[hdv] pid {p.Pid} : vendeur ouvert -> {listings.Count} lot(s) en vente"
                              + (hidden > 0 ? $" (+{hidden} entrée(s) sans quantité ni prix ignorée(s))" : ""));
        }
        else if (p.Name == "ExchangeBidHouseInListUpdatedEvent")   // kes/kef : un lot ajouté/mis à jour dans MA vente
        {
            // v4 donne ce message À PLAT (mapping fields.json : object_gid=1, object_uid=2, object_type=3, prices=4).
            // Tout est résolu PAR NOM (plus AUCUN numéro de champ en dur ici). Le reprice/vente est déjà couvert
            // AUTORITAIREMENT par le chemin Item* (kda/kco) ; ce handler est SECONDAIRE -> on FUSIONNE (jamais écraser
            // un prix/qty valide par 0) et on n'upsert que si uid ET gid sont présents. `prices` est un repeated
            // (prix par lot) : on lit le 1er (prix unitaire) ; la quantité n'est pas portée par cet event (reprise du lot existant).
            int uidF = FNum("ExchangeBidHouseInListUpdatedEvent", "object_uid");
            int gidF = FNum("ExchangeBidHouseInListUpdatedEvent", "object_gid");
            int priceF = FNum("ExchangeBidHouseInListUpdatedEvent", "prices");
            int uid = (int)PayloadReader.Int(p.Payload, uidF, 0);
            long gid = PayloadReader.Int(p.Payload, gidF, 0);
            if (uid != 0 && gid != 0)
            {
                var prev = Hdv.MyListings(p.Pid).FirstOrDefault(l => l.Uid == uid);   // struct par défaut (tout à 0) si absent
                long price = PayloadReader.Int(p.Payload, priceF, 0);
                if (price <= 0) price = prev.Price;             // ne pas écraser un prix connu par 0
                int qty = prev.Qty;                             // qty non portée par cet event -> conserver celle du lot
                // Lot INCONNU (qty jamais vue) : cet event ne porte pas la quantité -> on ne crée pas de ligne « x0 » (le
                // chemin Item* autoritaire l'ajoutera avec sa vraie quantité).
                if (qty > 0 && price > 0)
                {
                    Hdv.UpsertMine(p.Pid, new BidHouseStore.Listing(uid, gid, qty, price));
                    HdvUpdated?.Invoke(p.Pid);
                }
            }
        }
        else if (p.Name == "ExchangeBidHouseInListRemovedEvent")   // ken : un lot retiré de MA vente
        {
            // ⚠ v4 : object_uid=#2 (le #1 = object_gid) ; l'ancien code lisait #1 -> mauvais uid retiré. Résolu du mapping.
            int uid = (int)PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseInListRemovedEvent", "object_uid"), 0);
            if (uid != 0)
            {
                foreach (var key in _hdvSellLinks.Where(x => x.Key.Pid == p.Pid && x.Value == uid).Select(x => x.Key))
                    _hdvSellLinks.TryRemove(key, out _);
                Hdv.RemoveMine(p.Pid, uid); HdvUpdated?.Invoke(p.Pid);
            }
        }
        else if (p.Name == "ExchangeBidHouseItemAddedEvent" && HdvSellerOpen(p.Pid))   // kda : v4 -> MON lot (ré-)ajouté (reprice/vente)
        {
            // Vérifié sur trame vendeur : v4 confirme reprice/vente par Item*, PAS InList*. Mapping-driven :
            //   ExchangeBidHouseItemAddedEvent{ unsold_delay 2, item 3 (BidItem), price 5 } ; BidItem{ uid 1, gid 2, quantity 3 }.
            //   ⚠ prix = #5 (petit) ; #2 = unsold_delay (~2.4M = 28j en s), à NE PAS confondre avec le prix.
            // Gaté HDV vendeur ouvert : en mode vendeur on ne voit que SES lots (jamais ceux d'autrui) -> upsert sûr.
            int itemF = FNum("ExchangeBidHouseItemAddedEvent", "item");
            var info = p.Payload.FirstOrDefault(f => f.Field == itemF)?.Message;   // BidItem
            if (info is not null)
            {
                int uidF = FNum("BidItem", "uid"), gidF = FNum("BidItem", "gid"), qtyF = FNum("BidItem", "quantity");
                var added = new BidHouseStore.Listing(
                    (int)PayloadReader.Int(info, uidF, 0), PayloadReader.Int(info, gidF, 0),
                    (int)PayloadReader.Int(info, qtyF, 0), PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseItemAddedEvent", "price"), 0));
                // Un vrai lot a toujours une quantité et un prix : jamais de ligne « x0 » (retrait / reprix impossibles dessus).
                if (added.Uid > 0 && added.Gid > 0 && added.Qty > 0 && added.Price > 0) { HdvTrackAdded(p.Pid, added); Hdv.UpsertMine(p.Pid, added); }
                HdvUpdated?.Invoke(p.Pid);
            }
        }
        else if (p.Name == "ExchangeBidHouseItemRemovedEvent" && HdvSellerOpen(p.Pid))   // kco : v4 -> MON lot retiré (ancien uid au reprice / retrait)
        {
            // ExchangeBidHouseItemRemovedEvent{ sell_id 1 } = l'uid EXCHANGE du lot retiré (au reprice, c'est l'ANCIEN uid).
            int uid = (int)PayloadReader.Int(p.Payload, FNum("ExchangeBidHouseItemRemovedEvent", "sell_id"), 0);
            if (uid != 0)
            {
                foreach (var key in _hdvSellLinks.Where(x => x.Key.Pid == p.Pid && x.Value == uid).Select(x => x.Key))
                    _hdvSellLinks.TryRemove(key, out _);
                HdvTrackRemoved(p.Pid, Hdv.MyListings(p.Pid).FirstOrDefault(l => l.Uid == uid));
                Hdv.RemoveMine(p.Pid, uid); HdvUpdated?.Invoke(p.Pid);
            }
        }

        // HDV FERMÉ : DialogLeaveRequest du CLIENT (C->S), ou fermeture vue du SERVEUR (ExchangeLeaveEvent) / changement de
        // carte. Avant, seul le C->S du vrai client remettait l'état à zéro : nos propres DialogLeave injectés ne passent pas
        // par le décodeur -> en SOCKET (aucun vrai client) le HDV restait « ouvert » à vie et CHAQUE trajet suivant partait
        // précédé d'un DialogLeaveRequest sans dialogue ouvert (signature). LeaveDialog le remet aussi à zéro.
        if (HdvAnyOpen(p.Pid) && ((p.ClientToServer && (p.Name == "DialogLeaveRequest"))
                                  || (!p.ClientToServer && p.Name is "ExchangeLeaveEvent" or "MapCurrentEvent")))
            ResetHdvState(p.Pid);

        // VENTE ABOUTIE (notification TextInformationEvent [log], S->C) : un de MES lots s'est vendu -> crédit banque.
        // Message générique multi-usages -> on matche par NOM (mapping streamé) + message_id 65 (« vendu en HDV »).
        // ⚠ Un patch a renuméroté ce message EN SILENCE : message_id #2->#1, parameters #4->#3 (et le code lqn->log) ->
        // l'ancien code lisait message_type(#2)==65 (jamais vrai, #2 omis) et parameters au #4 codé en dur -> ventes NON
        // comptées. Tout est désormais résolu par le mapping (p.Name + FNum), zéro champ codé en dur. Alimente le cumul des
        // gains + l'event HdvSold (stats/UI). parameters (répété) = [montant_net, gid, gid, quantité].
        if (!p.ClientToServer && p.Name == "TextInformationEvent"
            && PayloadReader.Int(p.Payload, FNum("TextInformationEvent", "message_id"), -1) == 65)
        {
            var prm = PayloadReader.Strs(p.Payload, FNum("TextInformationEvent", "parameters"));   // [montant, gid, gid, qty]
            if (prm.Count >= 4 && long.TryParse(prm[0], out long amount) && amount > 0
                && long.TryParse(prm[1], out long soldGid) && int.TryParse(prm[3], out int soldQty))
            {
                Hdv.AddSale(p.Pid, amount);
                // HDV vendeur FERMÉ seulement : ouvert, le serveur retire l'uid EXACT du lot vendu (ExchangeBidHouseItemRemovedEvent).
                // Sinon on retirait en plus un 2e lot identique encore en vente (lots en masse = mêmes objet/taille/prix).
                var soldClosed = !HdvSellerOpen(p.Pid) ? Hdv.RemoveOneMineSold(p.Pid, soldGid, soldQty) : null;
                HdvTrackSold(p.Pid, soldGid, soldQty, amount, soldClosed);   // HDV Tracker : sold (+ lot si connu)
                NoteHdvAction(p.Pid, "sold");
                HdvSold?.Invoke(p.Pid, amount, soldGid, soldQty);
                HdvUpdated?.Invoke(p.Pid);
                TransportLog.Line($"[hdv] pid {p.Pid} : vente aboutie gid={soldGid} x{soldQty} -> +{amount} kamas");
            }
        }

        // VENTES HORS LIGNE (S->C, à la connexion) : lots vendus pendant l'absence du perso. Lecture 100 % mapping streamé
        // (matching_current.json + fields.json) : ExchangeBidHouseOfflineSoldItemsEvent.bid_house_items -> ObjectGidWithQuantity
        // {quantity, price_date_effect, object_gid} -> ObjectPriceDateEffect. Ses champs n'ont que des noms OBFUSQUÉS dans le
        // fields.json (le client ne les nomme pas) : gbza = prix du lot, gbzc = date de vente (secondes epoch). Sens validé sur
        // 2 captures (03/10 : 8 lots x100 à 459 991 ; 04/10 : x100 gid 7652 à 174 909, date = historique du jeu).
        // Un champ inconnu du mapping = message ignoré (journalisé une fois), jamais deviné.
        if (!p.ClientToServer && p.Name == "ExchangeBidHouseOfflineSoldItemsEvent"
            && FieldOf("ExchangeBidHouseOfflineSoldItemsEvent", "bid_house_items") is int offItemsF
            && FieldOf("ObjectGidWithQuantity", "quantity") is int offQtyF && FieldOf("ObjectGidWithQuantity", "price_date_effect") is int offPdeF
            && FieldOf("ObjectGidWithQuantity", "object_gid") is int offGidF
            && FieldOf("ObjectPriceDateEffect", "gbza") is int offPriceF && FieldOf("ObjectPriceDateEffect", "gbzc") is int offDateF)
        {
            var sales = new List<(long Gid, int Qty, long Price, long SoldAt)>();
            foreach (var f in p.Payload)
            {
                if (f.Field != offItemsF || f.Message is not { } it) continue;
                var pde = it.FirstOrDefault(x => x.Field == offPdeF)?.Message;
                long gid = PayloadReader.Int(it, offGidF, 0), date = pde is null ? 0 : PayloadReader.Int(pde, offDateF, 0);
                int qty = (int)PayloadReader.Int(it, offQtyF, 0);
                long price = pde is null ? 0 : PayloadReader.Int(pde, offPriceF, 0);
                if (date is > 0 and < 100_000_000_000L) date *= 1000;   // secondes -> ms (déjà en ms au-delà)
                if (gid > 0 && qty > 0 && price > 0) sales.Add((gid, qty, price, date));
            }
            TransportLog.Line($"[hdv] pid {p.Pid} : ventes hors ligne -> {sales.Count} lot(s), {sales.Sum(s => s.Price)} kamas");
            if (sales.Count > 0) HdvTrackOfflineSales(p.Pid, sales);
        }

        // Kamas : total mis à jour à CHAQUE changement (dépense zaap/banque, gain de combat...). KamasUpdateEvent (ivf).
        // ⚠ v4 : le total est au champ quantity=#2 (le #1 = frpe) ; l'ancien code lisait #1 -> kamas faux. Complète le
        // kub (qui ne refire pas sur une simple dépense). Champ résolu par le mapping.
        if (p.Name == "KamasUpdateEvent")
        {
            long k = PayloadReader.Int(p.Payload, FNum("KamasUpdateEvent", "quantity"), 0);   // proto3 : absent = 0 kamas (perso vidé), pas "inconnu"
            if (k != Kamas(p.Pid)) { SetKamas(p.Pid, k); TransportLog.Line($"[kamas] pid {p.Pid} : {k} (KamasUpdateEvent)"); }
        }

        // Quêtes (critères de franchissement conditionnel « Qf=230 » + API quest:) : snapshot au login (QuestsEvent) puis
        // mises à jour incrémentales (Validated/Started/StepStarted/…), + ObjectErrorEvent (équipement). Lecture pure. Lot I.
        OnLotIPacket(p);
        // Lot J : session de coffre à code (ouverte sur ExchangeStartedWith*Storage / contenu, fin sur ExchangeLeaveEvent).
        OnLotJPacket(p);
        // Lot K : défis ACTIFS du combat (liste / ajout / cibles), trace des résultats, écho du statut joueur.
        OnLotKPacket(p);

        // Récolte MANUELLE (le joueur clique une ressource) : le client envoie InteractiveUseRequest pour SON perso ; si
        // l'élément est une VRAIE ressource de la carte courante (pas zaap/porte/PNJ), on affiche « En récolte » tout de
        // suite (fermé à l'iwi/iwq comme la récolte du bot). Le bot, lui, arme via SendInteractiveUse (injection non tapée).
        if (p.ClientToServer && p.Name == "InteractiveUseRequest"
            && _gatherElements.TryGetValue(p.Pid, out var gEls)
            && gEls.Contains((int)PayloadReader.Int(p.Payload, FNum("InteractiveUseRequest", "element_id"), 0)))
        { _gatherUntil[p.Pid] = Environment.TickCount64 + GatherWindowMs; RaiseActivity(p.Pid); }

        // Fin d'utilisation d'un interactif (iwi InteractiveUseEndedEvent) : l'animation de recolte est TERMINEE -> le perso
        // est de nouveau libre. Signal de completion UNIVERSEL (couvre la recolte avec bonus, qui n'emet pas d'itn)..
        if (p.Name == "InteractiveUseEndedEvent") { _gatherUntil.TryRemove(p.Pid, out _); InteractiveUseEnded?.Invoke(p.Pid, InteractiveElementId(p.Payload)); }
        // Erreur d'utilisation d'un interactif (iwq InteractiveUseErrorEvent) : la recolte a ECHOUE (deja utilise, hors portee,
        // occupe...) -> on arrete d'attendre tout de suite au lieu de patienter jusqu'au timeout.
        if (p.Name == "InteractiveUseErrorEvent") { _gatherUntil.TryRemove(p.Pid, out _); InteractiveUseFailed?.Invoke(p.Pid, InteractiveElementId(p.Payload)); }
        // Utilisation d'un interactif CONFIRMÉE (InteractiveUsedEvent) : le serveur accepte l'usage (zaap/porte/levier/…,
        // aussi émis au DÉBUT d'une récolte). Sert la primitive map:useById pour les actions sans iwi/itn. fields.json peut
        // être faux (leçon CharacterLifeStatusEvent, cf phoenix-auto-revive) -> « c'est moi » décidé PAR VALEUR (charId).
        if (p.Name == "InteractiveUsedEvent" && !p.ClientToServer)
        {
            long me = CharacterId(p.Pid);
            bool mine = me == 0 || p.Payload.Any(f => f.Wire == WireType.Varint && (long)f.Varint == me);
            if (mine)
            {
                int elem = (int)PayloadReader.Int(p.Payload, FNum("InteractiveUsedEvent", "element_id"), 0);
                if (elem == 0)   // repli : plus grand varint hors entity (même heuristique qu'InteractiveElementId)
                    elem = (int)p.Payload.Where(f => f.Wire == WireType.Varint && (long)f.Varint != me).Select(f => (long)f.Varint).DefaultIfEmpty(0).Max();
                int skill = (int)PayloadReader.Int(p.Payload, FNum("InteractiveUsedEvent", "skill_id"), 0);
                long durRaw = PayloadReader.Int(p.Payload, FNum("InteractiveUsedEvent", "duration"), 0);
                int duration = durRaw is > 0 and <= 600 ? (int)durRaw : 0;   // borne 60 s : une valeur aberrante (champ mal mappé) = ignorée
                TransportLog.Line($"[interactif] pid {p.Pid} : utilisation confirmée élément {elem} (skill {skill}, durée {duration})");
                InteractiveUsedBySelf?.Invoke(p.Pid, elem, skill, duration);
            }
        }

        // Ces requêtes ne font pas autorité sur MapCurrentEvent. La recharge au
        // FightStartingEvent couvre aussi ContextReady émis AVANT l'entrée en combat.
        if (p.ClientToServer && IsInFight(p.Pid) && p.Name is ("ContextReadyRequest" or "MapInformationRequest"))
            _ = RefreshCombatMapAsync(p.Pid);

        // Combat : on marque l'entree/sortie de combat (pour lire kmk comme des positions de combattants).
        if (p.Name == "FightStartingEvent")
        {
            ClearFighterStates(p.Pid);
            _fighterVisibility[p.Pid] = new FightVisibilityReducer();
            _fightMarks[p.Pid] = new FightMarks();
            _ = StartCombatMapAsync(p.Pid);
            _turnOrder.TryRemove(p.Pid, out _); _prefightRan.TryRemove(p.Pid, out _); _challengeGate.TryRemove(p.Pid, out _); _castLedgers.TryRemove(p.Pid, out _); _fightTurnNo.TryRemove(p.Pid, out _);
            _idleTurns.TryRemove(p.Pid, out _); _totalIdleTurnsInFight.TryRemove(p.Pid, out _); _actedThisTurn.TryRemove(p.Pid, out _);
            ResetTurboMetrics(p.Pid);
            // Entrer en combat ANNULE toute récolte/déplacement en cours (l'interactif n'émettra pas d'iwi/iwq) : on ferme
            // leurs fenêtres d'ACTIVITÉ pour que l'indicateur ne retombe pas sur « Récolte »/« Déplacement » périmés à la
            // FIN du combat (ActivityOf : Combat masque, mais les fenêtres survivraient jusqu'à leur cap 12 s/6 s).
            _gatherUntil.TryRemove(p.Pid, out _); _movingUntil.TryRemove(p.Pid, out _);
            _fightId[p.Pid] = PayloadReader.Int(p.Payload, FNum("FightStartingEvent", "fight_id"), 0);   // combat rejoignable par les followers d'équipe (repli #2 = fields.json ; #4 était attacker_id -> fight_id faux si le catalogue manquait le champ)
            // The diagnostic fight window was opened before external packet
            // callbacks; never consume the same FightStartingEvent twice.
            if (comboPacketAdmitted) QueueCombatTacticalAdvisoryRefresh(p.Pid);
            // Remise à zéro de TOUT l'état séquence/tour : sinon les compteurs et seuils d'un combat précédent faussent le gate
            // de front jyj (et rendent les logs illisibles) dès le combat suivant.
            _isqCount.TryRemove(p.Pid, out _); _jyjCount.TryRemove(p.Pid, out _); _jyjConsumed.TryRemove(p.Pid, out _);
            _seqArmIsq.TryRemove(p.Pid, out _); _myTurnOpen.TryRemove(p.Pid, out _); _turnEndedAt.TryRemove(p.Pid, out _); _lastTurnPlay.TryRemove(p.Pid, out _); _jzcSeen.TryRemove(p.Pid, out _); _turnEpoch.TryRemove(p.Pid, out _); _moveRefused.TryRemove(p.Pid, out _);
            ResetScriptFightState(p.Pid);   // état des hooks de combat Lua (tour/placement/invocations) du combat précédent
            // Lot C2 : défis (propositions / demande / choix / résultats du combat précédent) + chrono et bilan du nouveau combat.
            _challengeProps.TryRemove(p.Pid, out _); _challengeRequested.TryRemove(p.Pid, out _); _challengeChosen.TryRemove(p.Pid, out _);
            _challengeResults.TryRemove(p.Pid, out _); _placementOver.TryRemove(p.Pid, out _);
            ForgetChallengeFight(p.Pid);   // défis : sorts lancés, élément, cibles, niveaux, morts du combat précédent
            _bonusReannounced.TryRemove(p.Pid, out _);
            OnLotKFightStarting(p.Pid);   // Lot K : défis ACTIFS du combat précédent (lisibles après le combat jusqu'ici)
            _fightStartAt[p.Pid] = Environment.TickCount64; _fightResultStored.TryRemove(p.Pid, out _);
            MarkLifeStaleForFight(p.Pid);   // Lot F : PV hors combat périmés jusqu'à la prochaine fiche/krk (sinon graine en fin de combat)
            ResetFightOptions(p.Pid);   // Lot E : options de combat (état reçu + bascules envoyées) du combat précédent
            // Un script à hooks bloqué sur l'attente d'un déplacement hors combat (jsq qui ne viendra plus) : on le débloque
            // tout de suite pour que son thread reprenne vite la pompe des hooks. Seulement si un hook est déclaré.
            if (_scriptTurnHook.ContainsKey(p.Pid) || _scriptPlacementHook.ContainsKey(p.Pid)) SettleMove(p.Pid, MoveOutcome.Superseded);
            if (_combat is not null && _combat.TryGetValue(p.Pid, out var fightProfile))
                TransportLog.Line($"[combat] politique active : {fightProfile.PolicySummary()}");
            FightStarted?.Invoke(p.Pid);
            _ = ApplyFightLocksAsync(p.Pid, CurrentFightId(p.Pid));   // Lot E : FIGHT_LOCK_* du script en cours (tâche, jamais de Lua)
        }
        else if (p.Name == "FightEndEvent") { DecodeFightOutcome(p.Pid, p.Payload); StoreFightResult(p.Pid, p.Payload); EndFight(p.Pid); }

        // AUTONOMIE PRÉ-COMBAT : sur les cases de placement possibles (kba), placer selon le style puis se mettre prêt
        // (async). Une seule fois par combat. Le prêt AUTO du CLIENT Dofus doit être DÉSACTIVÉ.
        // Lecture PURE pour le script (combat:placementCells / isPlacement) — aucun effet moteur.
        if (p.Name == "FightPlacementPossiblePositionsEvent" && !p.ClientToServer && FightPrepReader.ReadPlacementCells(p.Payload) is { } pc0)
        {
            _placementCells[p.Pid] = pc0.Challengers;
            _placementOpen[p.Pid] = true;
            // Spectateurs (0) et groupe (1) dès l'ouverture, avant RunPreFight et avant le délai de l'option 2.
            SendSpectatorAndPartyLocks(p.Pid, CurrentFightId(p.Pid));
        }
        // Hook onPlacement : le placement est confié au script (branche testée EN PREMIER : l'autre a un effet de bord,
        // _prefightRan.TryAdd). Sans hook -> branche d'origine inchangée.
        if (p.Name == "FightPlacementPossiblePositionsEvent" && ScriptOwnsPlacement(p.Pid))
        {
            if (_prefightRan.TryAdd(p.Pid, 0) && FightPrepReader.ReadPlacementCells(p.Payload) is { } sp)
            { _scriptPlacementPending[p.Pid] = sp.Challengers; TransportLog.Line("[combat] placement délégué au script (onPlacement)"); }
        }
        else if (p.Name == "FightPlacementPossiblePositionsEvent" && !ManualCombatOn(p.Pid)
            && _combat.TryGetValue(p.Pid, out var ppf) && ppf.AutoPreFight
            && _prefightRan.TryAdd(p.Pid, 0)
            && FightPrepReader.ReadPlacementCells(p.Payload) is { } placePos)
            _ = RunPreFight(p.Pid, placePos.Challengers);

        // Challenge proposé (kwx) : on SÉLECTIONNE+VALIDE (kwv/kwj) le défi ACCEPTÉ au meilleur gain moyen (chance de réussite
        // × bonus, cf PickChallenge / ChallengeCatalog), puis prêt. Le choix du défi n'a AUCUNE incidence sur XP-vs-Butin
        // (fixé à part, cf protocol-wire-map). Aucun défi accepté -> on n'en valide aucun (gate libérée -> prêt).
        // ⚠ Un FOLLOWER d'équipe ne sélectionne JAMAIS de challenge (le CHEF décide) -> on ignore les propositions chez la mule.
        // Lot C2 : les propositions sont TOUJOURS mémorisées (lecture pure pour combat:challengeProposals) ; le mode « script »
        // (ChallengeByScript) laisse le choix au script au lieu de valider le 1er. Profils existants : bloc d'origine inchangé.
        if (p.Name == "ChallengeProposalEvent")
        {
            var props = FightPrepReader.ReadChallengeProposals(p.Payload);
            _challengeProps[p.Pid] = props;
            _challengePropsAt[p.Pid] = Environment.TickCount64;
            if (!ManualCombatOn(p.Pid) && _combat.TryGetValue(p.Pid, out var cpf) && cpf.AutoPreFight && !IsTeamFollower(p.Pid))
            {
                if (cpf.AutoChallenge && !cpf.ChallengeByScript)
                {
                    // Chaque proposition (une par emplacement de défi) est tranchée pour elle-même, comme avant.
                    if (props.Count > 0 && PickChallenge(p.Pid, cpf, props) is int pick)
                    {
                        // Placement délégué au script (onPlacement) : RunPreFight ne tourne pas -> attente d'équipe ici d'abord.
                        _ = ScriptOwnsPlacement(p.Pid) ? AutoSelectChallengeAfterTeamAsync(p.Pid, pick) : RunChallengeSelect(p.Pid, pick);
                    }
                    else
                    {
                        if (props.Count > 0) ChallengeDeclined?.Invoke(p.Pid, props.Select(x => x.Id).ToList());
                        if (_challengeGate.TryGetValue(p.Pid, out var gate)) gate.TrySetResult(true);   // rien à valider -> prêt
                    }
                }
                else if (cpf.ChallengeByScript)
                {
                    if (props.Count == 0) { if (_challengeGate.TryGetValue(p.Pid, out var g0)) g0.TrySetResult(true); }   // rien à choisir -> prêt
                    else TransportLog.Line($"[combat] pid {p.Pid} : challenges proposés [{string.Join(", ", props.Select(x => x.Id))}] -> choix laissé au script");
                }
            }
        }
        // Lot C2 : résultat d'un défi (réussi/échoué) -> combat:challengeResult / bilan de combat.
        if (p.Name == "ChallengeResultEvent" && !p.ClientToServer && FightPrepReader.ReadChallengeResult(p.Payload) is { } cr)
        {
            _challengeResults.GetOrAdd(p.Pid, static _ => new())[cr.Id] = cr.Success;
            ChallengeResolved?.Invoke(p.Pid, cr.Id, cr.Success);
        }
        // Bonus des challenges CONFIRMÉ par le serveur (0 XP, 1 butin ; proto3 : XP = champ omis) -> journal « Challenge choisi ».
        if (p.Name == "ChallengeBonusChoiceSelectedEvent" && !p.ClientToServer)
            _challengeBonusKind[p.Pid] = (int)PayloadReader.Int(p.Payload, FNum("ChallengeBonusChoiceSelectedEvent", "challenge_bonus"), 0);
        // MITM : le client Dofus annonce à l'entrée en combat le bonus qu'IL a mémorisé (capture 05/10 : 2 annonces vides = XP).
        // Si le profil en veut un autre, GivreClone ré-annonce le sien juste APRÈS (cf ReannounceChallengeBonusAsync). Notre
        // propre annonce repasse ici avec la bonne valeur -> aucune boucle. Mode socket : le client socket annonce déjà le bon.
        if (p.Name == "ChallengeBonusChoiceRequest" && p.ClientToServer && !IsSocketPid(p.Pid)
            && _combat.TryGetValue(p.Pid, out var cbp) && cbp.AutoPreFight && cbp.AutoChallenge)
        {
            int sent = (int)PayloadReader.Int(p.Payload, FNum("ChallengeBonusChoiceRequest", "challenge_bonus"), 0);
            if (sent != (cbp.ChallengeLoot ? 1 : 0) && _bonusReannounced.AddOrUpdate(p.Pid, 1, static (_, n) => n + 1) <= 4)
                _ = ReannounceChallengeBonusAsync(p.Pid, cbp.ChallengeLoot);
        }
        // Lot C2 : phase de placement terminée -> plus de choix de défi possible (fail-open si ce message n'arrive jamais).
        if (p.Name == "FightStartEvent" && !p.ClientToServer) _placementOver[p.Pid] = 1;

        // DEBUG : dump complet (arbre + valeurs signees + IDs de caracteristiques) des paquets de combat -> retrouver
        // empiriquement PA/PM/PV/degats dans les VRAIS paquets. A desactiver via CombatDebug une fois le mapping fige.
        if (CombatDebug && IsCombatDebugTarget(p)) DumpCombatPacket(p);
        if (!p.ClientToServer && IsInFight(p.Pid)) ObserveTreasureFighters(p.Pid, p.Name, p.Payload);

        // Combat : positions AUTORITAIRES des combattants -> EntitiesDispositionEvent (kkr) { Dispositions #1 ->
        // EntityDisposition }. C'EST le paquet que ReadDispositions décode. Émis à
        // CHAQUE (re)placement AVANT le 1er tour + aux resync -> recale ma case après un re-placement de la phase de
        // placement. AVANT (bug) : le handler visait "FightSynchronizeEvent" (jul), dont les combattants sont au #3
        // IMBRIQUÉ (structure ≠) -> ReadDispositions rendait 0 -> `_fighters` restait sur la case du juo/jxg initial
        // (ex 285) alors que le serveur m'avait placé ailleurs (ex 373) -> SelfCell périmée -> TOUS les tirs refusés
        // (« vous visez à 9 ») + aucun déplacement (« je crois être au contact »). NB : ancien pin kmk=FightSynchronizeEvent
        // MORT (kmk=FriendDeleteRequest depuis le patch) ; match par CODE kkr + repli NOM (pas de collision sur ce nom).
        if ((p.Name == "EntitiesDispositionEvent") && _inFight.TryGetValue(p.Pid, out var inF) && inF)
        {
            var fighters = MapFightReader.ReadDispositions(p.Payload);
            if (fighters.Count > 0)
            {
                lock (_fightersLock) _fighters[p.Pid] = fighters;   // set autoritaire (placement) - sous le meme verrou
                if (_fighterVisibility.TryGetValue(p.Pid, out var positionsVisibility))
                    foreach (var fighter in fighters) positionsVisibility.ConfirmPosition(fighter.ActorId);
                _blockedCells.TryRemove(p.Pid, out _);   // re-sync autoritaire des positions -> les anciennes cases bloquées ne valent plus
                FightersUpdated?.Invoke(p.Pid, fighters);
                TransportLog.Line($"[combat] {fighters.Count} combattant(s) : " +
                    string.Join(", ", fighters.Take(8).Select(x => $"cell {x.Cell}={(x.ActorId >= 0 ? "joueur" : "monstre")}")));
            }
        }

        // Combat : action de MORT (GameActionFightEvent jwe, sous-action Death au champ #4 { target_id #1 }) -> on retire
        // le combattant tue de la carte. Distinct de #20 (PA/PM) et #40 (degats) -> plus de faux retraits.
        if (p.Name == "GameActionFightEvent" && _inFight.TryGetValue(p.Pid, out var inFd) && inFd
            && MapFightReader.ReadKilledFighter(p.Payload) is long deadId)
        {
            // défi Conquérant : case du mort AVANT son retrait
            if (_fighters.TryGetValue(p.Pid, out var fdead) && fdead.FirstOrDefault(f => f.ActorId == deadId) is { ActorId: not 0 } dead)
                NoteChallengeDeath(p.Pid, deadId, dead.Cell);
            // retirer le mort du MODELE de combat (sinon l'auto-combat le cible encore) - ATOMIQUE (cf MutateFighters)
            MutateFighters(p.Pid, ff => ff.Any(f => f.ActorId == deadId) ? ff.Where(f => f.ActorId != deadId).ToList() : null);
            if (_fighterVisibility.TryGetValue(p.Pid, out var visibility)) visibility.Remove(deadId);
            if (_fighterBreeds.TryGetValue(p.Pid, out var breeds)) breeds.TryRemove(deadId, out _);
            FighterKilled?.Invoke(p.Pid, deadId);
            TransportLog.Line($"[combat] combattant {deadId} tue -> retire de la carte");
        }

        if (IsInFight(p.Pid))
        {
            if (p.Name == "FightFighterShowEvent" && FightStatsReader.ReadShownSummon(p.Payload) is { } shownSummon)
                OnSummoned(p.Pid, shownSummon, null);
            if (p.Name == "FightFighterShowEvent" && FightStatsReader.ReadShowFighterBreed(p.Payload) is { } breed)
            {
                // The decoder-local diagnostic breed join occurred before
                // PacketDecoded, independent of the combat reducer.
                if (comboPacketAdmitted) QueueCombatTacticalAdvisoryRefresh(p.Pid);
                _fighterBreeds.GetOrAdd(p.Pid, _ => new ConcurrentDictionary<long, int>())[breed.FighterId] = breed.Breed;
            }
            var visibility = _fighterVisibility.GetOrAdd(p.Pid, _ => new FightVisibilityReducer());
            if (p.Name == "FightFighterShowEvent" && FightVisibilityReader.ReadInitial(p.Payload) is { } initial)
                visibility.Observe(initial, FightStatsReader.ReadShowFighterCell(p.Payload) is >= 0);
            if (p.Name == "GameActionFightEvent")
            {
                if (!p.ClientToServer && p.PayloadDecoded && _catalog is { } castCatalog
                    && new RawStateMessageReader(MappingCatalog.SessionVersionBinding?.MappingBuild, castCatalog)
                        .ReadSpellCast(p.Code, p.Payload) is { SourceId: long source } cast
                    && IsEnemy(p.Pid, source))
                    _observedEnemySpellLevels.GetOrAdd(p.Pid, _ => new())
                        .GetOrAdd(source, _ => new())[cast.SpellId] = cast.SpellLevelId;
                if (FightVisibilityReader.ReadChange(p.Payload) is { } change) visibility.Observe(change);
                if (MapFightReader.ReadRelocation(p.Payload) is { Kind: "InvisibleDetected" } detected)
                    visibility.Locate(detected.Actor);   // cellule exacte donnée par le serveur : confirme un combattant Visible
            }
            // A source seen since an empty full sync is no longer known absent.
            // Unknown changes discard provenance rather than guessing a source.
            if (!p.ClientToServer && p.Name is ("FightBoostAddEvent" or "FightBoostRemoveEvent"
                or "FightBoostUpdateStateEvent" or "FightBoostUpdateEvent"))
            {
                _emptyStatBuffInventory.TryRemove(p.Pid, out _);
                if (p.Name == "FightBoostAddEvent" && p.PayloadDecoded && _catalog is { } buffCatalog
                    && new RawStateMessageReader(MappingCatalog.SessionVersionBinding?.MappingBuild, buffCatalog)
                        .ReadSourceSpellId(p.Code, p.Payload) is >= 0 and var sourceSpell)
                {
                    if (_observedStatBuffSources.TryGetValue(p.Pid, out var sources))
                        _observedStatBuffSources[p.Pid] = sources.Add(sourceSpell);
                }
                else _observedStatBuffSources.TryRemove(p.Pid, out _);
            }
            // États de combat (juh FightBoostAddEvent / juu FightBoostRemoveEvent)
            if (!p.ClientToServer && p.PayloadDecoded && _catalog is { } triggerCatalog)
            {
                if (p.Name == "FightBoostUpdateStateEvent" && p.Code == "jun"
                    && _distributionTriggers.TryGetValue(p.Pid, out var previousTriggers))
                    previousTriggers.Clear();
                var triggerReader = new RawStateMessageReader(MappingCatalog.SessionVersionBinding?.MappingBuild, triggerCatalog);
                if (triggerReader.ReadTimedDamage(p.Code, p.Payload) is { } timed
                    && _teamIntentRound.TryGetValue(p.Pid, out int timedRound))
                    _distributionTriggers.GetOrAdd(p.Pid, _ => new Combat.DistributionTriggerTracker())
                        .ObserveTimed(timed, timedRound);
                if (triggerReader.ReadTriggeredSubcast(p.Code, p.Payload) is { } trigger
                    && _teamIntentRound.TryGetValue(p.Pid, out int triggerRound))
                    _distributionTriggers.GetOrAdd(p.Pid, _ => new Combat.DistributionTriggerTracker())
                        .Observe(trigger, triggerRound);
                if (p.Name == "FightBoostRemoveEvent"
                    && triggerReader.Read(p.Code, p.Payload) is RawStateRemoveObservation removed
                    && _distributionTriggers.TryGetValue(p.Pid, out var tracker)) tracker.Remove(removed);
            }
            // jun is an authoritative buff inventory (official eao.vfg). Only an
            // entirely empty inventory needs no decoding of individual effect rows.
            if (p.Name == "FightBoostUpdateStateEvent" && p.Code == "jun" && !p.ClientToServer
                && MZP0ProtocolAdmission.Supports37Family(MappingCatalog.SessionVersionBinding?.MappingBuild)
                && p.PayloadDecoded && p.Payload.Count == 0)
            {
                ClearFighterStates(p.Pid);
                _emptyStatBuffInventory[p.Pid] = true;
                _observedStatBuffSources[p.Pid] = System.Collections.Immutable.ImmutableHashSet<int>.Empty;
                long self = CharacterId(p.Pid);
                if (self != 0)
                {
                    SetFighterState(p.Pid, self, 76, false);
                    // An empty authoritative inventory proves the absence of the
                    // states checked by learned spells and their sub-spells.
                    var pendingStates = new Queue<(int Spell, int Grade)>();
                    foreach (int spellId in _charSpells.GetValueOrDefault(p.Pid) ?? Array.Empty<int>())
                        pendingStates.Enqueue((spellId, LearnedSpellLevel(p.Pid, spellId)));
                    var inspectedStates = new HashSet<(int Spell, int Grade)>();
                    while (pendingStates.TryDequeue(out var spell))
                    {
                        if (!inspectedStates.Add(spell)) continue;
                        foreach (var criterion in StateCriterion.Parse(_gameNames.SpellStatesCriterion(spell.Spell, spell.Grade)))
                            if (criterion.Atom.IsStateCheck)
                                SetFighterState(p.Pid, self, criterion.Atom.Value, false);
                        var descriptor = _gameNames.GetSpellDescriptor(spell.Spell, spell.Grade);
                        if (descriptor is null || descriptor.Effects.IsDefault) continue;
                        foreach (var effect in descriptor.Effects)
                        {
                            if (effect.ActionId is 1160 or 2960 or 2794 or 792 or 1017 or 1019 or 2160)
                                pendingStates.Enqueue((effect.DiceNum != 0 ? effect.DiceNum : effect.Min, Math.Max(1, effect.DiceSide)));
                            foreach (string token in (effect.TargetMask ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                            {
                                string atom = token.TrimStart('*');
                                if (atom.Length > 1 && atom[0] is 'E' or 'e' && int.TryParse(atom.AsSpan(1), out int state))
                                    SetFighterState(p.Pid, self, state, false);
                            }
                        }
                    }
                }
                TransportLog.Line($"[état] inventaire serveur vide confirmé pour pid {p.Pid}, personnage {self}");
            }
            if (p.Name is "FightBoostAddEvent" or "FightBoostRemoveEvent" && !p.ClientToServer && _catalog is { } stateCatalog
                && new RawStateMessageReader(MappingCatalog.SessionVersionBinding?.MappingBuild, stateCatalog)
                    .Read(p.Code, p.Payload) is { } stateObservation)
            {
                if (stateObservation is RawStateAddObservation add && add.Actor is long actorId && add.StateId is int stateId)
                {
                    // 951 removes the state itself, including all of its tracked instances.
                    // Its event instance is not a new active state instance.
                    if (add.ActionId == 951)
                    {
                        foreach (var entry in _fighterStateInstances.Where(kv => kv.Key.Pid == p.Pid
                            && kv.Key.FighterId == actorId && kv.Value == stateId))
                            _fighterStateInstances.TryRemove(entry.Key, out _);
                    }
                    SetFighterState(p.Pid, actorId, stateId, add.ActionId != 951);
                    if (add.ActionId != 951 && add.Instance is int inst && inst >= 0)
                    {
                        _fighterStateInstances[(p.Pid, actorId, inst)] = stateId;
                    }
                    TransportLog.Line($"[état] {(add.ActionId == 951 ? "-" : "+")}{stateId}={(_gameNames.StateName(stateId)) ?? "?"} sur {actorId} (instance {add.Instance}, action {add.ActionId}, source {add.SourceId}, fin {add.End})");
                }
                else if (stateObservation is RawStateRemoveObservation remove && remove.Actor is long remActorId)
                {
                    int removedStateId = 0;
                    if (remove.NetworkInstanceId is int remInst && _fighterStateInstances.TryRemove((p.Pid, remActorId, remInst), out var sid))
                    {
                        removedStateId = sid;
                        SetFighterState(p.Pid, remActorId, sid, false);
                    }
                    TransportLog.Line($"[état] Remove sur {remActorId} (instance {remove.NetworkInstanceId}){(removedStateId != 0 ? $" [state {removedStateId}]" : "")}");
                }
                else
                {
                    TransportLog.Line(stateObservation is RawStateAddObservation addFallback
                        ? $"[état] +{addFallback.StateId}={(addFallback.StateId is int sidFallback ? _gameNames.StateName(sidFallback) : null) ?? "?"} sur {addFallback.Actor} (instance {addFallback.Instance}, action {addFallback.ActionId}, source {addFallback.SourceId}, fin {addFallback.End})"
                        : $"[état] {stateObservation.GetType().Name.Replace("RawState", "").Replace("Observation", "")} sur {stateObservation.ActorId} (instance {stateObservation.NetworkInstanceId})");
                }

                long me = CharacterId(p.Pid);
                if (me != 0)
                {
                    int n = FighterActiveStates(p.Pid, me).Count;
                    TransportLog.Line($"[etat] pid {p.Pid} : {n} etat(s) actif(s) suivi(s) pour le perso");
                }
            }
        }

        // Combat : INVOCATION en cours de combat (GameActionFightEvent jwe, action_id 181). Le combattant invoqué N'arrive
        // PAS par jxg -> il faut l'ajouter au suivi ICI, sinon sa case est vue "libre" (bot marche dessus / tire à travers
        // -> refus serveur "obstacle gêne la vue", puis mort). C'était LA cause racine du blocage sur invocation.
        if (p.Name == "GameActionFightEvent" && _inFight.TryGetValue(p.Pid, out var inFsm) && inFsm)
            foreach (var sm in FightStatsReader.ReadSummons(p.Payload))
                OnSummoned(p.Pid, sm, FightStatsReader.ReadActionSource(p.Payload));

        // Combat : FightTeamUpdateEvent (kae) = roster d'ÉQUIPE. Un COMPAGNON/invocation (id NÉGATIF) apparaît comme MEMBRE
        // de MON équipe (le roster qui me contient, propriétaire = moi au #7.#4). C'est le signal FIABLE que le jxg
        // per-fighter ne donne PAS pour un compagnon pré-placé (aucune team lue -> "team=?" -> attaqué par le repli signe).
        // -> tout membre NÉGATIF de MON roster est un ALLIÉ, jamais ciblé (ex : archiduk compagnon).
        if ((p.Name == "FightTeamUpdateEvent"))
        {
            var members = FightStatsReader.ReadTeamMemberIds(p.Payload);
            // MON identité = l'id AUTHENTIFIÉ du login (_charId), fiable EN COMBAT COMME HORS COMBAT. Surtout PAS le repli
            // « max des ids positifs du roster » : hors de MON combat (SelfActorId=0), il désignait le MENEUR d'un roster
            // ÉTRANGER (combat d'un autre joueur croisé en approche) comme « moi » -> ce roster était classé « mon équipe »
            // et son -1 parasite latché en allié ; en rejoignant ce combat, le monstre (actorId -1) était déjà « allié »
            // -> 0 ennemi classé -> tours passés / « vous visez à 9 ». Si _charId pas encore capté -> me=0 -> on ne classe
            // AUCUN allié d'équipe (échec SÛR : pas d'empoisonnement ; mes invocations restent captées par propriétaire).
            long me = _charId.TryGetValue(p.Pid, out var mc) ? mc : 0;
            bool mine = me != 0 && members.Contains(me);
            // DIAG "Log tout" : trace la décision kae (roster reçu, mon id, est-ce le mien) -> permet de VOIR si un kae du
            // roster ENNEMI est pris pour le mien (cause racine du "a attaqué puis stoppé" : latch d'un monstre en allié).
            TransportLog.Line($"[combat] équipe (FightTeamUpdateEvent) roster=[{string.Join(",", members)}] me={me} -> {(mine ? "MON équipe" : "PAS mon équipe (ignoré)")}");
            if (mine)
            {
                var set = _allies.GetOrAdd(p.Pid, static _ => new ConcurrentDictionary<long, byte>());
                foreach (var id in members)
                    if (id < 0)
                    {
                        // GARDE anti-aveuglement : ne JAMAIS reclasser en allié un ennemi DÉJÀ confirmé (attaqué). Un kae
                        // mal lu ne peut plus faire cesser l'attaque d'un monstre que le bot combat déjà.
                        if (IsConfirmedEnemy(p.Pid, id))
                            TransportLog.Line($"[combat] équipe : {id} listé allié mais DÉJÀ confirmé ennemi (attaqué) -> reste ciblé");
                        else if (set.TryAdd(id, 0))
                            TransportLog.Line($"[combat] allié d'équipe {id} (membre de MON équipe) -> jamais ciblé");
                    }
            }
        }

        // Combat : deplacement FORCE (poussee/attirance, GameActionFightEvent action_id 5, sous-action #38) -> on met a jour
        // la cellule du combattant deplace dans le modele, sinon l'auto-combat vise/attaque sa case AVANT poussee.
        if (p.Name == "GameActionFightEvent" && _inFight.TryGetValue(p.Pid, out var inFsl) && inFsl
            && MapFightReader.ReadSlide(p.Payload) is { } sl)
        {
            int dest = -1, was = -1;
            MutateFighters(p.Pid, fsl =>
            {
                if (fsl.All(f => f.ActorId != sl.Actor)) return null;
                was = fsl.First(f => f.ActorId == sl.Actor).Cell;   // destination = la case qui n'est PAS l'actuelle
                dest = was == sl.CellA ? sl.CellB : (was == sl.CellB ? sl.CellA : sl.CellB);
                return fsl.Select(f => f.ActorId == sl.Actor ? (dest, f.ActorId) : f).ToList();
            });
            if (dest >= 0)
            {
                _fighterVisibility.GetValueOrDefault(p.Pid)?.ConfirmPosition(sl.Actor);
                TransportLog.Line($"[combat] combattant {sl.Actor} pousse/attire {sl.CellA}<->{sl.CellB} -> case {dest} (etait {was})");
            }
        }

        // Combat : autres déplacements FORCÉS (téléportation sur la carte, lancer/poser d'un porté, porté qui rejoint son
        // porteur, invisible révélé). Sans ça, un sort qui téléporte un monstre (ou mon propre Bond) laissait une case
        // périmée dans le modèle -> coups dans le vide, case « occupée » fantôme, chemin bloqué.
        if (p.Name == "GameActionFightEvent" && _inFight.TryGetValue(p.Pid, out var inFrl) && inFrl
            && MapFightReader.ReadRelocation(p.Payload) is { } rl)
        {
            int was = -1;
            var moved = MutateFighters(p.Pid, fsl =>
            {
                if (fsl.All(f => f.ActorId != rl.Actor)) return null;
                was = fsl.First(f => f.ActorId == rl.Actor).Cell;
                return was == rl.Cell ? null : fsl.Select(f => f.ActorId == rl.Actor ? (rl.Cell, f.ActorId) : f).ToList();
            });
            if (moved is not null) TransportLog.Line($"[combat] combattant {rl.Actor} déplacé ({rl.Kind}) -> case {rl.Cell} (était {was})");
            if (rl.Kind != "InvisibleDetected") _fighterVisibility.GetValueOrDefault(p.Pid)?.ConfirmPosition(rl.Actor);
        }

        // Combat : marques au sol (glyphes, pièges, runes). Identité pose/retrait journalisée ;
        // effets et déclenchements à confirmer avant toute décision d'évitement.
        if (p.Name == "GameActionFightEvent" && _inFight.TryGetValue(p.Pid, out var inFmk) && inFmk)
        {
            // The decoder-local ComboObservation was consumed ONCE at ingress,
            // before public observers. Legacy FightMarks stays independent.
            if (comboPacketAdmitted) QueueCombatTacticalAdvisoryRefresh(p.Pid);
            var marks = _fightMarks.GetOrAdd(p.Pid, _ => new FightMarks());
            marks.Observe(p.Payload);
            if (MapFightReader.ReadMark(p.Payload) is { } mk)
                TransportLog.Line($"[combat] marque posée id={mk.Id} type={mk.Type} auteur={mk.Author} active={mk.Active} cases=[{string.Join(",", mk.Cells)}] présentes={marks.Snapshot.Count}");
            else if (MapFightReader.ReadUnmark(p.Payload) is { } um)
                TransportLog.Line($"[combat] marque retirée id={um} présentes={marks.Snapshot.Count}");
            else if (MapFightReader.ReadMarkActivation(p.Payload) is { } activation)
                TransportLog.Line($"[combat] marque activation id={activation.Id} active={activation.Active}");
        }

        // Combat : ÉCHANGE DE PLACES (Transposition…). Le serveur donne les cases du lanceur et de la cible : ce sont les
        // NOUVELLES cases ; si elles égalent exactement les cases actuelles du modèle, ce sont les anciennes -> on permute.
        if (p.Name == "GameActionFightEvent" && _inFight.TryGetValue(p.Pid, out var inFex) && inFex
            && MapFightReader.ReadExchangePositions(p.Payload) is { } ex)
        {
            int newC = ex.CasterCell, newT = ex.TargetCell;
            var swapped = MutateFighters(p.Pid, fsl =>
            {
                int wasC = fsl.FirstOrDefault(f => f.ActorId == ex.Caster) is { } fc && fc.ActorId == ex.Caster ? fc.Cell : -1;
                int wasT = fsl.FirstOrDefault(f => f.ActorId == ex.Target) is { } ft && ft.ActorId == ex.Target ? ft.Cell : -1;
                if (wasC == ex.CasterCell && wasT == ex.TargetCell) { newC = wasT; newT = wasC; }
                if (wasC < 0 && wasT < 0) return null;
                return fsl.Select(f => f.ActorId == ex.Caster ? (newC, f.ActorId) : f.ActorId == ex.Target ? (newT, f.ActorId) : f).ToList();
            });
            if (swapped is not null) TransportLog.Line($"[combat] échange de places : {ex.Caster} -> case {newC}, {ex.Target} -> case {newT}");
            if (swapped is not null && _fighterVisibility.TryGetValue(p.Pid, out var exchangedVisibility))
            {
                exchangedVisibility.ConfirmPosition(ex.Caster);
                exchangedVisibility.ConfirmPosition(ex.Target);
            }
        }

        // Combat : DÉBUT DE TOUR. On se base sur FightTurnEvent, qui porte le `character_id` du combattant dont c'est le
        // tour = signal FIABLE. On NE se base PLUS sur jto: son
        // `fighter_id` (id de COMBAT) peut valoir un sentinel -1 (0xFFFFFFFFFFFFFFFF) qui ne matche jamais mon perso -> le
        // bot restait AFC (« il ne détecte pas mon tour »). FightTurnStartPlayingEvent (jyj) est VIDE -> inutilisable seul.
        // ⭐ Résolution PAR LE MAPPING (`p.Name`, pas le code brut) : après un patch Dofus le code obfusqué de FightTurnEvent
        // change, mais tant que le mapping (streamé du serveur) le re-mappe (auto-matcher + confirmed.json), ce handler
        // continue de marcher SANS rebuild — c'est la façon patch-proof, comme FightStartingEvent/FightEndEvent/etc.
        // On joue notre personnage et les combattants dont le serveur nous a explicitement donné le contrôle.
        // Un allié ou une invocation non contrôlée ne donne jamais droit à jouer. RunFightTurn est anti-réentrant (_turnRunning),
        // donc un event répété/en rafale ne redémarre pas un 2e tour. IsFightTurnOf est aussi PATCH-PROOF côté CHAMP (teste
        // tous les varints, sans figer le n° de champ, car Dofus décale/ajoute des champs à chaque MAJ).
        // (03/10 : l'ancien repli « par code » sur `jwi` est supprimé — ce code n'existe plus dans le mapping v5, ni en jeu.)

        // BARRIÈRES DE SÉQUENCE : jyj (FightTurnStartPlayingEvent) = la mise en place du tour est finie, on peut jouer ;
        // isq (SequenceEndEvent) = une séquence d'action s'est fermée. Elles débloquent l'attente dans RunFightTurn pour ne
        // PAS injecter un déplacement pendant une séquence ouverte (sinon rejet serveur jqt). Résolus par le mapping (p.Name).
        // ── AUTORITÉ DU TOUR : FightTurnEvent (début de tour), résolu PAR NOM ─────────────────────────────────────────
        // Une action est ACCEPTÉE si et seulement si elle part DANS ma propre fenêtre de tour ET avant ma fin de tour ; sinon
        // le serveur la jette EN SILENCE. Un FightTurnEvent qui nomme QUELQU'UN D'AUTRE ferme ma fenêtre -> les actions encore
        // en vol s'arrêtent au lieu d'être injectées dans le vide. (03/10 : les anciens tests par code `jzc`/`jwi` sont retirés :
        // sur le mapping v5 `jzc` = un autre message et `jwi` n'existe plus ; FightTurnEvent = `jwd`, vu en jeu le 30/09.)
        // Les noms de variables `_jzcSeen`/`jzcWasSeen` désignent désormais « FightTurnEvent vu dans ce combat ».
        // Turbo agressif : réponse immédiate au marqueur « tour prêt » du serveur + relances ; un FightTurnEvent annule les relances.
        if (p.Name == "FightIsTurnReadyEvent" && !p.ClientToServer && IsInFight(p.Pid) && TurboEagerReadyOwned(p.Pid))
        { _turboDedupe.NoteReadyMarker(p.Pid); InjectTurboReady(p.Pid, "marqueur du serveur"); }
        if (p.Name == "FightControlEvent" && !p.ClientToServer && _catalog is not null)
        {
            var control = FightControlReader.Read(p.Payload, _catalog.FieldNumber);
            TransportLog.Line($"[combat-control] pid={p.Pid} master-field={_catalog.FieldNumber("FightControlEvent", "fvxb")} actor-field={_catalog.FieldNumber("FightControlEvent", "fvwx")} " +
                $"scalars=[{string.Join(",", p.Payload.Where(f => f.Wire == WireType.Varint).Select(f => $"{f.Field}:{f.AsSignedVarint}"))}] " +
                $"decoded={control is not null} master={control?.MasterId} actor={control?.ActorId} spells={control?.Spells.Count} character={CharacterId(p.Pid)} fight={IsInFight(p.Pid)}");
            if (control is not null) ObserveControlledFighter(p.Pid, control);
        }
        if (p.Name == "FightFreeControlEvent" && !p.ClientToServer
            && p.Payload.Any(f => f.Wire == WireType.Varint && f.AsSignedVarint == CharacterId(p.Pid)))
        {
            foreach (var field in p.Payload.Where(f => f.Wire == WireType.Varint))
                RevokeControlledFighter(p.Pid, CharacterId(p.Pid), field.AsSignedVarint);
        }
        if (p.Name == "FightTurnEvent" && !p.ClientToServer)
        {
            if (FieldOf("FightTurnEvent", "round") is int teamRoundField
                && PayloadReader.Int(p.Payload, teamRoundField, 0) is > 0 and var teamRound)
                _teamIntentRound[p.Pid] = (int)teamRound;
            // The diagnostic turn was already admitted at the head of OnDecoded.
            ObserveServerFightTurn(p.Pid, FightStatsReader.TurnActor(p.Payload));
            if (comboPacketAdmitted) QueueCombatTacticalAdvisoryRefresh(p.Pid);
            _controlledTurnStarted.TryRemove(p.Pid, out _);
            // Any new server turn invalidates an async run from the previous fighter.
            if (_serverTurnActor[p.Pid] != SelfActorId(p.Pid)) _turnEpoch.AddOrUpdate(p.Pid, 1, static (_, n) => n + 1);
            if (IsInFight(p.Pid)) ObserveTurnOrder(p.Pid, FightStatsReader.TurnActor(p.Payload));
            // Preserve evidence of an outer sequence spanning the turn event before resynchronizing ACK tracking.
            _turnStartedInSequence[p.Pid] = _turboSeqDepth.GetValueOrDefault(p.Pid) > 0;
            _turboSeqDepth[p.Pid] = 0;   // resynchronise l'imbrication à chaque tour (comme le client Socket) : un début/fin manqué ne coupe plus les accusés
        }
        if (p.Name == "FightTurnEvent" && !p.ClientToServer
            && _inFight.TryGetValue(p.Pid, out var inFz) && inFz)
        {
            _placementOpen[p.Pid] = false;   // le 1er tour clôt la phase de placement (lecture script seulement)
            long me = SelfActorId(p.Pid);
            if (me != 0)
            {
                bool jzcWasSeen = _jzcSeen.GetValueOrDefault(p.Pid);
                _jzcSeen[p.Pid] = true;   // autorité disponible sur ce serveur -> `jwi` repasse en simple repli
                // PATCH-PROOF sur le N° DE CHAMP: `character_id` est en #1 sur ce build, mais un bot tiers l'a vu en #3 —
                // les numéros tournent à chaque patch. On cherche donc N'IMPORTE QUEL varint == mon id : aucun autre champ
                // ne peut entrer en collision (durées 360/290/10, remaining_time 165/240, index 1..3, round 1..5 — tous
                // de petits entiers, face à un id de perso ~4,6e10). Aucun varint == moi => c'est le tour de QUELQU'UN D'AUTRE.
                bool mine = FightStatsReader.IsFightTurnOf(p.Payload, me);
                if (mine && !_myTurnOpen.GetValueOrDefault(p.Pid)) CaptureFightTurnStartCell(p.Pid);
                _myTurnOpen[p.Pid] = mine || HasControlledTurn(p.Pid);
                if (mine && EngineAutoPlays(p.Pid))   // ≡ profil.AutoFight sans hook script ni override (condition d'origine)
                {
                    long round = FieldOf("FightTurnEvent", "round") is int rf ? PayloadReader.Int(p.Payload, rf, -1) : -1;
                    TransportLog.Line($"[combat] début de tour : MOI (round {round})");
                    int epochZ = _turnEpoch.AddOrUpdate(p.Pid, 1, static (_, v) => v + 1);
                    ArmTurnPlay(p.Pid); _ = RunAutomaticFightTurn(p.Pid, epochZ, journalRound: (int)round);
                }
                else if (mine && ScriptDrivesTurns(p.Pid) && !jzcWasSeen && ActiveScriptTurn(p.Pid) is { ViaJyj: true } vj && vj.Ended == 0)
                {
                    // 1er jzc ARRIVÉ APRÈS la réclamation par front jyj (> grâce de 500 ms, sous charge) : c'est le MÊME tour
                    // serveur -> on le réutilise (pas de nouvelle époque, pas de 2e onTurn, n° de tour inchangé).
                    vj.ViaJyj = false;
                    TransportLog.Line("[combat] début de tour tardif : tour déjà pris en charge par le script");
                }
                else if (mine && ScriptDrivesTurns(p.Pid))
                {
                    // Tour TENDU au script (hook onTurn) : jeton d'époque réclamé par le thread du script (TryClaimScriptTurn).
                    int ep = _turnEpoch.AddOrUpdate(p.Pid, 1, static (_, v) => v + 1);   // périme tout run antérieur
                    _scriptTurnPending[p.Pid] = ep;
                    TransportLog.Line(p.Pid, $"[combat] début de tour : MOI (round {_teamIntentRound.GetValueOrDefault(p.Pid)}) -> tour délégué au script");
                    ArmScriptClaimTimeout(p.Pid, ep);
                }
            }
        }
        if (p.Name == "FightTurnEvent" && !p.ClientToServer) TryStartControlledTurn(p.Pid);
        // Fin de la phase de placement (lecture script : combat:isPlacement / placementCells).
        if (p.Name == "FightStartEvent" && !p.ClientToServer) _placementOpen[p.Pid] = false;
        if (p.Name == "FightTurnStartPlayingEvent")
        { if (!_jzcSeen.GetValueOrDefault(p.Pid)) CaptureFightTurnStartCell(p.Pid); _jyjCount.AddOrUpdate(p.Pid, 1, static (_, n) => n + 1); _lastTurnPlay[p.Pid] = System.Environment.TickCount64; _myTurnOpen[p.Pid] = true; if (_turnPlayTcs.TryGetValue(p.Pid, out var tp)) tp.TrySetResult(true); TryStartControlledTurn(p.Pid); }
        // FIN DE SÉQUENCE = SequenceEndEvent, PAR NOM uniquement (v5 : `jqy`, vu en jeu le 30/09). Les anciens tests par code
        // (`isq` = ObjectFeedRequest sur v5, garde `jxh` = FightRefreshCharacterStatsEvent sur v5) sont retirés le 03/10.
        // Le serveur confirme qu'un buff a modifié un de MES sorts (portée, PA, lancers…) : sert à valider les modificateurs
        // déduits des données du jeu (cf CommitCastModifiers). L'acteur est lu par nom, comme le reste du combat.
        if (p.Name == "ApplySpellModifierEvent" && !p.ClientToServer
            && FieldOf("ApplySpellModifierEvent", "actor_id") is int smActorF && PayloadReader.Int(p.Payload, smActorF, 0) is long smActor and not 0)
            OnSpellModifierEvent(p.Pid, smActor);
        if (p.Name == "SequenceStartEvent" && !p.ClientToServer && TurboTrackingEnabled(p.Pid))
        {
            int depth = _turboSeqDepth.AddOrUpdate(p.Pid, 1, static (_, n) => n + 1);
            if (depth == 1) _turboSeqOuterStartAt[p.Pid] = TurboNow();
        }
        if (p.Name == "SequenceEndEvent")
        {
            _isqCount.AddOrUpdate(p.Pid, 1, static (_, n) => n + 1);
            bool outerSeqEnd = true;
            if (TurboTrackingEnabled(p.Pid))
            {
                int left = _turboSeqDepth.AddOrUpdate(p.Pid, 0, static (_, n) => Math.Max(0, n - 1));
                outerSeqEnd = left == 0;
            }
            if (outerSeqEnd && TurboTrackingEnabled(p.Pid) && FieldOf("SequenceEndEvent", "author_id") is int authorF)
            {
                long author = PayloadReader.Int(p.Payload, authorF, 0);
                if (author != 0 && (author == SelfActorId(p.Pid) || (HasControlledTurn(p.Pid) && author == _serverTurnActor.GetValueOrDefault(p.Pid))))
                {
                    long now = TurboNow();
                    long actionId = FieldOf("SequenceEndEvent", "action_id") is int actionF
                        ? PayloadReader.Int(p.Payload, actionF, 0) : 0;
                    long startAt = _turboSeqOuterStartAt.TryGetValue(p.Pid, out var st) ? st : 0;
                    _turboLastSelfSeqEnd[p.Pid] = (actionId, now, startAt);
                    _turboLastSelfSeqType[p.Pid] = FieldOf("SequenceEndEvent", "sequence_type") is int stF ? PayloadReader.Int(p.Payload, stF, -1) : -1;
                    // Turbo-ack : accusé immédiat par le bot (comme en Socket), sauf si le client visible a déjà accusé cette action.
                    // action_id is reused by subsequent casts and turns. A historical
                    // dictionary entry is not an ACK for this request/outer sequence.
                    long? acknowledgedAt = _turboAcked.TryGetValue((p.Pid, actionId), out var priorAck) ? priorAck : null;
                    if (TurboAckOwned(p.Pid) && TurboSequenceAckPolicy.ShouldInject(actionId, startAt,
                            _turboLastRequestAt.GetValueOrDefault(p.Pid), acknowledgedAt))
                    {
                        var ackRelay = RelayForInject();
                        byte[] ack = MoveForge.BuildActionAckFramed(actionId);
                        if (ackRelay is not null && ack.Length > 0)
                        {
                            _turboDedupe.NoteBotAck(p.Pid, actionId, now);
                            if (ackRelay.Inject(p.Pid, ack))
                            {
                                _turboLastAck[p.Pid] = (actionId, now);
                                NoteTurboAcked(p.Pid, actionId);
                                PulseTurboBarrier(p.Pid);
                                TransportLog.Line($"[turbo-ack] accusé envoyé par le bot action={actionId} (sequence_end->ack 0 ms)");
                            }
                            else _turboDedupe.UndoBotAck(p.Pid, actionId);
                        }
                    }
                    // Turbo agressif : « tour prêt » anticipé, une fois par combat, juste après l'accusé de NOTRE séquence de début de combat (type 8).
                    if (TurboEagerReadyOwned(p.Pid) && FieldOf("SequenceEndEvent", "sequence_type") is int seqTypeF
                        && TurboFightPolicy.ShouldSendEarlyReady(true, author, author, PayloadReader.Int(p.Payload, seqTypeF, -1),
                                                                 !_jzcSeen.GetValueOrDefault(p.Pid), _turboEarlyReady.ContainsKey(p.Pid)))
                    {
                        _turboEarlyReady[p.Pid] = true;
                        InjectTurboReady(p.Pid, "anticipé (début de combat)", early: true);
                    }
                    PulseTurboBarrier(p.Pid);
                    long reqToSeq = _turboLastRequestAt.TryGetValue(p.Pid, out var reqAt) ? now - reqAt : -1;
                    TransportLog.Line($"[turbo-metric] sequence_end_self action={actionId} t={now} request_to_sequence_end_ms={(reqToSeq >= 0 ? reqToSeq : -1)}");
                }
                else if (TurboAckAllOwned(p.Pid))
                {
                    // « Accuser les actions de tous les combattants » : séquence EXTERNE d'un monstre/allié accusée dès sa fermeture.
                    long foreignAction = FieldOf("SequenceEndEvent", "action_id") is int foreignF ? PayloadReader.Int(p.Payload, foreignF, 0) : 0;
                    if (TurboFightPolicy.ShouldAckForeignSequence(true, author, SelfActorId(p.Pid), foreignAction))
                        InjectTurboAckFor(p.Pid, foreignAction, $"séquence de {author}");
                }
            }
            if (_seqEndTcs.TryGetValue(p.Pid, out var se)) se.TrySetResult(true);
        }

        // VAGUE suivante (FightNewWaveEvent puis FightSynchronizeEvent) : les monstres de la vague n'arrivent PAS par
        // FightFighterShowEvent. Live 06/11 20:33 et 20:48 : -4/-5/-6 jamais suivis -> « 0 ennemi classé », 45 tours vides en
        // Turbo -> suspension serveur 5 min. Chaque entrée `fighters` a la forme de FightFighterShowEvent.information : on la
        // rejoue comme un ShowFighter ; l'équipe est rafraîchie même pour les combattants déjà suivis.
        if (p.Name == "FightSynchronizeEvent" && IsInFight(p.Pid) && _catalog?.FieldNumber("FightSynchronizeEvent", "fighters") is int syncF
            && _catalog.FieldNumber("FightFighterShowEvent", "information") is int infoF)
        {
            int added = 0;
            foreach (var entry in p.Payload.Where(f => f.Field == syncF && f.Message is not null))
            {
                var shown = new List<ProtoField> { new() { Field = infoF, Wire = WireType.Len, Bytes = entry.Bytes, Message = entry.Message } };
                // La présence dans le suivi de positions ne garantit pas une équipe à jour. Lire le spawn
                // avant les gardes stats/case : une sync partielle peut aussi corriger l'équipe (0 omis en proto3).
                if (FightStatsReader.ReadFighterTeam(shown) is { } st)
                    _fighterTeam.GetOrAdd(p.Pid, static _ => new ConcurrentDictionary<long, int>())[st.JoinId] = st.Team;
                var syncVis = FightVisibilityReader.ReadInitial(shown);
                if (syncVis is { } visObs)
                    _fighterVisibility.GetOrAdd(p.Pid, static _ => new FightVisibilityReducer())
                        .Observe(visObs, FightStatsReader.ReadShowFighterCell(shown) is >= 0);
                if (FightStatsReader.ReadShowFighter(shown) is not { } sf || FightStatsReader.ReadShowFighterCell(shown) is not int sc || sc < 0) continue;
                UpsertFighterStats(p.Pid, sf.FighterId, sf);
                bool known;
                lock (_fightersLock) known = _fighters.TryGetValue(p.Pid, out var cur) && cur.Any(f => f.ActorId == sf.FighterId);
                if (known)
                {
                    if (syncVis is { RawState: 2 })
                        MutateFighters(p.Pid, cur => cur.Select(f => f.ActorId == sf.FighterId ? (sc, sf.FighterId) : f).ToList());
                    continue;
                }
                if (FightStatsReader.ReadShowFighterLevel(shown) is { } slv) NoteFighterLevel(p.Pid, slv.FighterId, slv.Level);
                if (_catalog is not null)
                    foreach (var monster in FightMonsterReader.Read("FightFighterShowEvent", shown, _catalog.FieldNumber))
                        _fightMonsters.GetOrAdd(p.Pid, static _ => new())[monster.ActorId] = monster;
                IReadOnlyList<(int Cell, long ActorId)> snap;
                lock (_fightersLock)
                {
                    var cur = _fighters.TryGetValue(p.Pid, out var c0) ? c0 : (IReadOnlyList<(int Cell, long ActorId)>)System.Array.Empty<(int Cell, long ActorId)>();
                    snap = cur.Append((sc, sf.FighterId)).ToList(); _fighters[p.Pid] = snap;
                }
                FightersUpdated?.Invoke(p.Pid, snap);
                added++;
                TransportLog.Line($"[combat] vague : combattant {sf.FighterId} ajouté -> case {sc} ({(IsEnemy(p.Pid, sf.FighterId) ? "ENNEMI" : "allié")})");
            }
            if (added > 0) TransportLog.Line($"[combat] FightSynchronizeEvent : {added} nouveau(x) combattant(s) suivi(s) (nouvelle vague)");
        }

        // Stats de combattant : sync (GameFightShowFighter jxg) -> PA/PM/PV/maxPV par combattant (moi + ennemis).
        if ((p.Name == "FightFighterShowEvent") && FightStatsReader.ReadShowFighter(p.Payload) is { } fs)
        {
            // Reconnexion EN COMBAT (24/10 17:01) : le serveur replace le perso dans son combat sans sélection captée -> perso
            // inconnu, aucun tour joué jusqu'au retour en jeu de rôle. Un combattant dont l'id est un perso DU COMPTE est le mien.
            if (fs.FighterId > 0 && CharacterId(p.Pid) == 0 && AccountCharacterIds(p.Pid).Contains(fs.FighterId))
                AdoptCharacterId(p.Pid, fs.FighterId, "combattant du compte vu en combat (reconnexion en combat)");
            UpsertFighterStats(p.Pid, fs.FighterId, fs);
            TransportLog.Line($"[stats] combattant {fs.FighterId} : PA={fs.Pa} PM={fs.Pm} PVmax={fs.MaxLife}");
            if (FightStatsReader.ReadShowFighterLevel(p.Payload) is { } flv) NoteFighterLevel(p.Pid, flv.FighterId, flv.Level);   // défis Cruel / Ordonné
            // ÉQUIPE (spawn_information.team) -> ennemi = équipe ≠ la mienne. Clé alignée sur _fighters (perso par id réel,
            // créature par actor_id de combat). Corrige alliés (invoc/compagnon team = mienne) vs monstres/Archiduk ennemi.
            if (FightStatsReader.ReadFighterTeam(p.Payload) is { } ft)
                _fighterTeam.GetOrAdd(p.Pid, static _ => new ConcurrentDictionary<long, int>())[ft.JoinId] = ft.Team;
            // Combattant NOUVEAU -> l'AJOUTER au suivi de positions, sinon sa case est vue "libre" (move refusé / LdV).
            // ⭐ FIX PLACEMENT (2026-09-03) : pendant la PHASE DE PLACEMENT, les jxg arrivent AVANT le 1er kmk -> `_fighters[pid]`
            // n'existe pas encore et `MutateFighters` bailait (return null) -> la position ENNEMIE était perdue -> placement par
            // défaut (le poll HasEnemies restait faux). On CRÉE donc la liste si absente ici, pour capturer les positions dès le
            // placement. Le kmk autoritaire (plus bas) écrase ensuite proprement. (Couvre aussi les invocations mid-combat.)
            if (_inFight.TryGetValue(p.Pid, out var inFj) && inFj && FightStatsReader.ReadShowFighterCell(p.Payload) is int fcell && fcell >= 0)
            {
                IReadOnlyList<(int Cell, long ActorId)>? snap = null;
                lock (_fightersLock)
                {
                    var cur = _fighters.TryGetValue(p.Pid, out var c0) ? c0 : (IReadOnlyList<(int Cell, long ActorId)>)System.Array.Empty<(int Cell, long ActorId)>();
                    if (!cur.Any(f => f.ActorId == fs.FighterId)) { var next = cur.Append((fcell, fs.FighterId)).ToList(); _fighters[p.Pid] = next; snap = next; }
                }
                if (snap is not null) { FightersUpdated?.Invoke(p.Pid, snap); TransportLog.Line($"[combat] combattant {fs.FighterId} vu -> case {fcell} (placement/invocation)"); }
            }
            // AUTO-KICK (CHEF uniquement) : un JOUEUR non-membre qui rejoint MON combat -> expulsé (garde le combat de groupe
            // privé). Discriminé par NOM (named_fighter) -> monstres/invocations = pas de nom = JAMAIS kickés;
            // les mules sont dans TeamMembers -> jamais kickées. Anti-spam par combat. Seul le chef (IsTeamLeader) agit.
            if (IsTeamLeader(p.Pid) && FightStatsReader.ReadShowFighterName(p.Payload) is { } fname
                && !TeamMembers(p.Pid).Any(m => string.Equals(m.Name, fname, StringComparison.OrdinalIgnoreCase)))
            {
                var kicked = _kickedThisFight.GetOrAdd(p.Pid, static _ => new ConcurrentDictionary<long, byte>());
                if (kicked.TryAdd(fs.FighterId, 0))
                {
                    TransportLog.Line($"[équipe] chef pid {p.Pid} : intrus '{fname}' (id {fs.FighterId}) dans mon combat -> expulsion");
                    SendKickFromFight(p.Pid, fs.FighterId);
                }
            }
        }

        // Mes stats de base HORS combat (CharacterCharacteristicsEvent kub) -> onglet Infos meme sans combat en cours.
        if (p.Name == "CharacterCharacteristicsEvent")
        {
            if (FightStatsReader.ReadSelfCharacteristics(p.Payload) is { } cc)
            {
                UpsertFighterStats(p.Pid, 0, new FighterStats(0, cc.Pa, cc.Pm, cc.MaxLife, cc.MaxLife, cc.Pa, cc.Pm));
                _selfRange[p.Pid] = cc.Range;   // Portée totale -> allonge des sorts rangeCanBeBoosted en combat
                TransportLog.Line($"[stats] caractéristiques (base) : PA={cc.Pa} PM={cc.Pm} PVmax={cc.MaxLife} Portée={cc.Range}");
            }
            // Kamas : kub porte le total (#2 -> #10) et refire souvent -> maj hors login (combat, refresh...). L'onglet
            // Inventaire relit Kamas(pid) au tick. (Les dépenses zaap/banque instantanées nécessitent le KamasUpdateEvent
            // dédié, code non encore capturé.)
            if (FightStatsReader.ReadKamas(p.Payload) is { } kb && kb != Kamas(p.Pid))
            {
                SetKamas(p.Pid, kb);
                TransportLog.Line($"[kamas] pid {p.Pid} : {kb} (caractéristiques)");
            }
            // Expérience TOTALE (conteneur #2 -> #6) : sert à l'échantillonnage XP/heure des stats. (Le CharacterExperienceGainEvent
            // kru ne porte QUE le GAIN d'un combat, pas le total -> inutilisable pour un net ; on lit donc le total ici.)
            if (FightStatsReader.ReadExperience(p.Payload) is { } xpTotal && xpTotal > 0) _charXp[p.Pid] = xpTotal;
            // Lot F : PV COURANTS réels (0+11+97), énergie, paliers d'XP. APRÈS l'upsert de la clé 0 ci-dessus (même PV max,
            // vrais PV). Ignoré en combat (les PV du combat font foi).
            OnSelfVitals(p);
        }

        // Perte de PV (GameActionFightEvent jwe, sous-message #40) -> maj des PV courants du combattant touche.
        if (p.Name == "GameActionFightEvent" && _inFight.TryGetValue(p.Pid, out var inFll) && inFll
            && FightStatsReader.ReadLifeLossDetails(p.Payload) is { } ll)
        {
            ApplyObservedLifeLoss(p.Pid, ll);
            // Diagnostic: which enemy dealt how much to an ally (a live 06/11 death was unexplainable without it).
            if ((ll.Amount > 0 || ll.ShieldLoss > 0) && !IsEnemy(p.Pid, ll.Target))
                TransportLog.Line($"[combat] dégâts subis : {ll.Target} -{ll.Amount} PV (bouclier -{ll.ShieldLoss}, permanents {ll.PermanentDamage})"
                    + $" par {(FightStatsReader.ReadActionSource(p.Payload) is long by ? by.ToString() : "?")}"
                    + (_fighterStats.TryGetValue(p.Pid, out var hpById) && hpById.TryGetValue(ll.Target, out var hp) ? $" -> {hp.Life}/{hp.MaxLife}" : ""));
            if (ll.Amount > 0 || ll.ShieldLoss > 0)
                if (FightStatsReader.ReadActionSource(p.Payload) is long attacker)
                    NoteChallengeDamage(p.Pid, attacker, ll.Target);
            // Menace RÉELLE par ennemi : plus gros coup observé de cet auteur ce combat (remplace l'estimation fixe du planificateur).
            if (FightStatsReader.ReadActionSource(p.Payload) is long src && src != ll.Target && IsEnemy(p.Pid, src) && !IsEnemy(p.Pid, ll.Target))
                NoteEnemyHit(p.Pid, src, ll.Amount + ll.ShieldLoss);
        }

        if (p.Name == "GameActionFightEvent" && IsInFight(p.Pid)
            && FightStatsReader.ReadLifeGain(p.Payload) is { } gain)
        {
            TransportLog.Line($"[combat] soin observé : {gain.Target} +{gain.Amount} PV");
            ApplyObservedLifeGain(p.Pid, gain.Target, gain.Amount);
        }

        if (p.Name == "GameActionFightEvent" && IsInFight(p.Pid)
            && FightStatsReader.ReadPointVariation(p.Payload) is { } pointChange)
            TransportLog.Line($"[DEBUG-walk-points] delta actor={pointChange.Target} ap={pointChange.IsPa} value={pointChange.Delta}");

        // MAJ EN DIRECT PA/PM (GameActionFightEvent jxw, forme detaillee base+modif) -> PA/PM COURANTS du combattant.
        // C'est la source qui manquait : les PV bougeaient (jwe #40) mais pas PA/PM. On ne suit que PA(1)/PM(23).
        if (p.Name == "FightRefreshCharacterStatsEvent")
        {
        var updates = FightStatsReader.ReadCharacteristicUpdates(p.Payload).ToArray();
        foreach (var cu in updates)
        {
            if (cu.CharId is FightStatsReader.IdPa or FightStatsReader.IdPm)
                TransportLog.Line($"[DEBUG-walk-points] absolute actor={cu.Actor} characteristic={cu.CharId} value={cu.Value}");
            if (_fighterStats.TryGetValue(p.Pid, out var characteristicStats)
                && characteristicStats.TryGetValue(cu.Actor, out var observedStats))
                characteristicStats[cu.Actor] = observedStats with
                {
                    Characteristics = observedStats.Characteristics.SetItem(cu.CharId, cu.Value)
                };
            if (cu.CharId == FightStatsReader.IdPa || cu.CharId == FightStatsReader.IdPm)
                ApplyPointUpdate(p.Pid, cu.Actor, cu.CharId == FightStatsReader.IdPa, cu.Value);
            // PORTÉE COURANTE (carac #19) de MON perso en combat : un monstre peut retirer/ajouter de la PO -> on met à jour
            // _selfRange pour que effRange (portée effective des sorts boostables) reflète la vraie PO -> plus de tir hors portée.
            else if (cu.CharId == FightStatsReader.IdRange && cu.Actor == SelfActorId(p.Pid))
            {
                _selfRange[p.Pid] = cu.Value;
                TransportLog.Line($"[combat] Portée courante -> {cu.Value} (FightRefreshCharacterStatsEvent #19 : malus/bonus PO en combat)");
            }
        }
        foreach (var actor in updates.GroupBy(cu => cu.Actor))
        {
            // Only same-packet components are authoritative together; stored deltas may predate damage.
            var values = new Dictionary<int, int>();
            foreach (var update in actor) values[update.CharId] = update.Value;
            if (values.TryGetValue(FightStatsReader.IdLife, out int basis)
                && values.TryGetValue(FightStatsReader.IdVitality, out int vitality)
                && values.TryGetValue(FightStatsReader.IdCurLifeDelta, out int delta)
                && _fighterStats.TryGetValue(p.Pid, out var lifeStats)
                && lifeStats.TryGetValue(actor.Key, out var previous))
            {
                int max = (int)System.Math.Clamp((long)basis + vitality, 0L, int.MaxValue);
                int life = (int)System.Math.Clamp((long)max + delta, 0L, max);
                UpsertFighterStats(p.Pid, actor.Key, previous with { Life = life, MaxLife = max });
            }
        }
        }

        // Sorts APPRIS du perso (SpellsEvent hms, a la connexion ou apres level up) : alimente le picker de l'onglet Combat avec les VRAIS
        // sorts du perso (au lieu de tout le repertoire de la classe) et leurs niveaux (grades).
        if (p.Name == "SpellsEvent")
        {
            var spells = CharacterSpellsReader.ReadLearnedSpells(p.Payload);
            if (spells.Count > 0)
            {
                _charSpells[p.Pid] = spells;
                _charSpellLevels[p.Pid] = CharacterSpellsReader.ReadLearnedSpellLevels(p.Payload);
                CharacterSpellsUpdated?.Invoke(p.Pid, spells);
                TransportLog.Line($"[combat] {spells.Count} sorts appris détectés : {string.Join(",", spells)}");
                // Diagnostic dégâts/élément (lu de spelllevelsdataroot) : vérifie le mapping élément avant de câbler l'IA.
                foreach (var id in spells)
                {
                    int g = LearnedSpellLevel(p.Pid, id);
                    if (_gameNames.GetSpellDamage(id, g) is { } dmg)
                    {
                        var z = _gameNames.SpellZone(id, g);   // diagnostic zone AoE : 'X' r2 si chargée, sinon "—" (champ non lu)
                        TransportLog.Line($"[sort] {_gameNames.SpellName(id) ?? id.ToString()} (grade {g}) : {dmg.Min}-{dmg.Max} dégâts, élément {dmg.Element}={GameNameResolver.ElementName(dmg.Element)}, {dmg.MaxPerTurn}/tour {dmg.MaxPerTarget}/cible{(z is { } zz ? $", zone '{(char)zz.Shape}' r{zz.Size}" : ", zone: —")}");
                    }
                    // Critère d'états (statesCriterion) rendu LISIBLE via StateName -> vérifie parseur + table d'états en jeu.
                    if (_gameNames.SpellStatesCriterion(id, g) is { Length: > 0 } crit)
                    {
                        string human = GivreClone.Protocol.StateCriterion.Describe(crit, s => _gameNames.StateName(s));
                        TransportLog.Line($"[sort] {_gameNames.SpellName(id) ?? id.ToString()} : critère d'états « {crit} »{(human.Length > 0 ? $" = lançable {human}" : " (non interprété)")}");
                    }
                }
            }
        }

        // Variante de sort activée (SpellVariantActivationEvent) : mise à jour des sorts appris
        if (p.Name == "SpellVariantActivationEvent")
        {
            var (spellId, effective) = LotEEventsReader.ReadSpellVariantResult(p.Payload);
            if (effective && spellId > 0)
            {
                ApplySpellVariant(p.Pid, spellId);
            }
        }

        // Changement de NIVEAU (CharacterLevelUpEvent, #1 = nouveau niveau) -> maj de la fiche du bot (niveau + "nom (lvl)").
        // Resolu PAR NOM uniquement. Le repli `p.Code == "kua"` a ete retire : sur 3.6.11.12, `kua` est
        // ChallengeNumberEvent { string #1 ; int32 challenge_number #2 ; int32 #3 } (arrive DEUX fois a l entree en combat,
        // #2 = 1) -- rien a voir avec un niveau. Un code fige survit au patch avec un autre sens : jamais de code en dur ici.
        if (p.Name == "CharacterLevelUpEvent"
            && p.Payload.FirstOrDefault(f => f.Field == FNum("CharacterLevelUpEvent", "new_level") && f.Wire == WireType.Varint) is { } lvF && (int)lvF.Varint is > 0 and <= 250)
        {
            SetCharacterLevel(p.Pid, (int)lvF.Varint);   // maj du niveau -> le cap licence (offre gratuite) mord dès le niveau 31
            CharacterLeveledUp?.Invoke(p.Pid, (int)lvF.Varint);
            InvalidateSpellCaches(p.Pid);
            lock (_autoLevelLock) { _autoStatSuspended.Remove(p.Pid); _autoStatRequests[p.Pid] = new object(); } // la prochaine fiche amorce la dépense, pas cet événement précoce
        }

        // ÉTAT DE VIE (CharacterLifeStatusEvent { phoenix_map_id #1, state #2 }, enum 0=vivant/1=tombe/2=fantôme) -> pilote
        // la résurrection auto au Phénix (lue par la boucle de script). 100% par nom (FNum), aucun code/champ en dur.
        if (p.Name == "CharacterLifeStatusEvent")
        {
            // Ce message ne porte QUE 2 varints : state (enum LifeStatus 0=vivant/1=tombe/2=fantôme, PETIT) et phoenix_map_id
            // (id de carte, GRAND). ⚠ Le NUMÉRO de ces 2 champs est CONTRADICTOIRE dans les sources : fields.json dit
            // {state:1, phoenix_map_id:2} alors que le proto de réf ET un bot tiers (qui tourne en jeu) disent {phoenix_map_id:1,
            // state:2}. Comme FNum est catalog-first, un décode par numéro lirait les deux CROISÉS. On désambiguïse donc par
            // la VALEUR (robuste au numéro, aucune capture requise) : 0..9 = state (l'enum), > 9 = phoenix_map_id.
            int state = 0; long pmap = 0;
            foreach (var f in p.Payload)
            {
                if (f.Wire != WireType.Varint) continue;
                long v = (long)f.Varint;
                if (v <= 9) state = (int)v; else pmap = v;
            }
            int prev = _lifeStatus.GetValueOrDefault(p.Pid, 0);
            _lifeStatus[p.Pid] = state;
            if (pmap > 0) _phoenixMap[p.Pid] = pmap;
            if (state != prev)
            {
                TransportLog.Line($"[mort] pid {p.Pid} : état de vie = {(state == 0 ? "vivant" : state == 1 ? "tombe" : "fantôme")}{(pmap > 0 ? $" (Phénix map {pmap})" : "")}");
                RaiseActivity(p.Pid);   // maj immédiate de l'activité (Mort <-> reprise) sans attendre le poll UI
                OnLifeStatusChangedLotF(p.Pid, prev, state);   // Lot F : horloge de régénération (mort/fantôme = pas de régénération)
                LifeStatusChanged?.Invoke(p.Pid, prev, state);    // journal du bot
            }
        }

        // La répartition est confirmée sur CharacterCharacteristicsEvent : l'événement de résultat
        // n'est pas envoyé lors de la dépense officielle observée le 24/10.

        // Liste des persos (CharacterListEvent, nom v5) : nom + niveau + classe + sexe.
        if (p.Name == "CharacterListEvent")
        {
            // Même une liste vide (premier personnage) place le client sur l'écran de sélection.
            if (!p.ClientToServer) _characterListAt[p.Pid] = Environment.TickCount64;
            // kvi = LISTE des persos du compte (chacun { base #1, characterId #2 }). On NE peut PAS prendre le 1er : le
            // perso JOUÉ est choisi via kvw (CharacterSelectionRequest). Repli : afficher le 1er tout de suite, corrigé par kvw.
            var all = CharacterInfoReader.ExtractAll(p.Payload);
            if (all.Count > 0)
            {
                _characters[p.Pid] = all;
                if (all.Count == 1)   // 1 seul perso -> on l'identifie tout de suite ; PLUSIEURS -> on ATTEND kvw (sinon on
                {                     // afficherait un perso non joué, ex le 1er de la liste avant même la sélection).
                    var only = all[0];
                    _charId[p.Pid] = only.Id;
                    _selfInfo[p.Pid] = (only.Id, only.Info);                 // identité du perso joué (character: Lua) — AVANT le niveau (nom dans la fenêtre du cap)
                    SetCharacterLevel(p.Pid, only.Info.Level ?? 0);   // cap de niveau licence (per-perso)
                    if (!string.IsNullOrEmpty(only.Info.Name)) CharacterIdentified?.Invoke(p.Pid, only.Info);
                }
                TransportLog.Line($"[perso] pid {p.Pid} : {all.Count} perso(s) du compte -> {(all.Count == 1 ? "identifié" : "attente de la sélection du perso")}");
                TransportLog.Line($"[perso] pid {p.Pid} : liste = " + string.Join(", ", all.Select(c => $"{c.Info?.Name} (niv. {c.Info?.Level}, {c.Info?.Breed})")));
                TryAutoPickCharacter(p.Pid, all);
            }
        }
        // Sélection RÉUSSIE (CharacterSelectionEvent, serveur -> client) : { success #1 { character #1 { basic #2 { level #4 }, id #3 } } },
        // même disposition qu'une entrée de la liste. Seconde source du niveau : couvre toute variante de sélection (remodelage,
        // perso créé en session, sélection forcée, requête de sélection non captée).
        if (p.Name == "CharacterSelectionEvent" && !p.ClientToServer
            && PayloadReader.Field(p.Payload, FNum("CharacterSelectionEvent", "success"))?.Message is { } selOk
            && CharacterInfoReader.ExtractSelectedId(selOk) is long selId and > 0)
        {
            MarkCharacterLoading(p.Pid);
            AdoptCharacterId(p.Pid, selId, "CharacterSelectionEvent (sélection confirmée par le serveur)",
                CharacterInfoReader.ExtractSelected(selOk));
        }
        // Sélection du perso joué (kvw CharacterSelectionRequest, #1 = characterId) : parmi la liste kvi, identifie le BON
        // perso (compte multi-perso) + fixe le charId (owner havre-sac). SORTANT (client -> serveur).
        // La toute première sélection d'un personnage fraîchement créé passe par CharacterFirstSelectionRequest (même champ character_id).
        if (p.ClientToServer && p.Name == "ContextCreationRequest")
            NoteCharacterContext(p.Pid);
        if (p.Name is "CharacterSelectionRequest" or "CharacterFirstSelectionRequest")
        {
            // A character switch invalidates every market dialog and its listing UIDs,
            // even when the OS keeps the same PID. Keep unresolved removal reviews
            // blocked: resetting the visible HDV state is not a server receipt.
            if (p.ClientToServer && HdvAnyOpen(p.Pid)) ResetHdvState(p.Pid);
            long sel = PayloadReader.Int(p.Payload, FNum(p.Name, "character_id"), 0);
            if (p.ClientToServer && sel > 0)
            {
                _clientSelectedAt[p.Pid] = Environment.TickCount64;
                MarkCharacterLoading(p.Pid);
            }
            if (p.ClientToServer && sel > 0 && _charId.TryGetValue(p.Pid, out var oldChar) && oldChar != sel)
            {
                // Character switch on the SAME Dofus.exe PID: do not present
                // old bag, bank or effects as belonging to the next character.
                Inventory.Clear(p.Pid); Bank.Clear(p.Pid); ForgetItemEffects(p.Pid); ForgetCharacterSheet(p.Pid, preserveListLooks: true);
                _kamas.TryRemove(p.Pid, out _); _bankKamas.TryRemove(p.Pid, out _);
                _storageOpenMap.TryRemove(p.Pid, out _); _storageContentPkt.TryRemove(p.Pid, out _);
                _openTeleportDests.TryRemove(p.Pid, out _); _knownZaaps.TryRemove(p.Pid, out _);
                _directZaaps.TryRemove(p.Pid, out _); _worldMapZaaps.TryRemove(p.Pid, out _);
                _openTeleportType.TryRemove(p.Pid, out _); _teleportDialogSeq.TryRemove(p.Pid, out _);
                _zaapListSeq.TryRemove(p.Pid, out _); _teleportOpen.TryRemove(p.Pid, out _);
                HdvUpdated?.Invoke(p.Pid); BankUpdated?.Invoke(p.Pid);
            }
            if (sel != 0 && _characters.TryGetValue(p.Pid, out var chars))
            {
                var match = chars.FirstOrDefault(c => c.Id == sel);
                if (match.Info is not null)
                {
                    _charId[p.Pid] = sel;
                    _selfInfo[p.Pid] = (sel, match.Info);                // identité du perso joué (character: Lua) — AVANT le niveau (nom dans la fenêtre du cap)
                    SetCharacterLevel(p.Pid, match.Info.Level ?? 0);   // cap de niveau licence (per-perso)
                    if (!string.IsNullOrEmpty(match.Info.Name)) CharacterIdentified?.Invoke(p.Pid, match.Info);
                    TransportLog.Line($"[perso] pid {p.Pid} : perso sélectionné = {match.Info.Name} lvl {match.Info.Level} classe {match.Info.Breed} (id {sel})");
                }
            }
        }
        else if (!p.ClientToServer && p.Name == "MapComplementaryInformationEvent" && _catalog is not null)
        {
            // Decoder-local diagnostic window was already closed before the
            // external observers (roleplay fallback still handles main state).
            // This is readiness for the MapCurrent transition, not a new transition.
            lock (_combatMapGate) _combatMapTickets.TryRemove(p.Pid, out _);
            // FILET DE SÉCURITÉ fin de combat : un jss (carte de JEU DE RÔLE) n'arrive JAMAIS pendant un combat (les
            // cartes de combat n'émettent pas de MapComplementaryInformationEvent, cf jru plus haut). Donc s'il survient
            // alors qu'on se croit encore « en combat », c'est que le FightEndEvent n'a pas été reçu/mappé (codes
            // shuffles après une MAJ Dofus) : on clôt le combat ICI pour que la boucle de récolte REPRENNE (sinon le bot
            // reste figé « en combat » et ne fait plus rien après le combat, comme observé).
            if (EndFight(p.Pid, preserveTacticalTransition: true)) TransportLog.Line($"[combat] pid {p.Pid} : fin de combat déduite du retour en jeu de rôle -> reprise");
            OnGameMapLoaded(p.Pid);   // carte de jeu reçue = session de jeu VIVANTE (filet si l'acceptation du ticket n'a pas été décodée)

            // Auto-detection serveur : ancree ICI (le perso est VRAIMENT en jeu sur une carte), pas sur
            // CharacterInformation (trop tot -> serveur muet). Retry interne jusqu'a reponse. 1x/pid (garde interne).
            // Bot SOCKET : serveur connu dès l'étape A, jamais de WhoAmI ; seul son NOM peut rester à résoudre.
            if (IsSocketPid(p.Pid)) TryResolveSocketServerName(p.Pid);
            else TryForgeWhoAmI(p.Pid);

            // Numero de champ de map_id lu DATA-DRIVEN depuis fields.json (numero wire confirme = #2 sur le client
            // actuel ; le .proto beta dit #5 -> tags derives). Rien de code en dur : si le champ n'est pas confirme,
            // on ne lit pas (au lieu de deviner un mauvais champ). Confirme en jeu via /mapid : 191104002.
            OnMapSubArea(p.Pid, p.Payload);   // Lot I : sous-zone live (subarea_id résolu par nom ; non résolu = non lu)
            object? roleplayTicket = null;
            if (_catalog.FieldNumber("MapComplementaryInformationEvent", "map_id") is int fn)
            {
                long mapId = PayloadReader.Int(p.Payload, fn, 0);
                if (mapId != 0)
                {
                    roleplayTicket = BeginRoleplayMap(p.Pid, mapId);
                    PublishNativeMapReady(p.Pid, mapId);
                    // Position monde (x, y) resolue via la table statique (le paquet ne la porte pas).
                    var pos = _mapPositions?.Get(mapId);
                    NoteSmithArrival(p.Pid, mapId);
                    MapChanged?.Invoke(p.Pid, mapId, pos?.X, pos?.Y);

                    // Republish even for the same roleplay map: the UI may still
                    // display the protector arena. Reject late loads by context.
                    {
                        var ticket = roleplayTicket;
                        var pid = p.Pid;
                        _ = Task.Run(() =>
                        {
                            var cells = _cellReader.LoadCells(mapId);
                            var exits = _cellReader.LoadExits(mapId);
                            PublishRoleplayMap(pid, mapId, ticket, () =>
                            {
                                if (cells is { Length: > 0 }) MapCellsLoaded?.Invoke(pid, mapId, cells);
                                if (exits is not null) MapExitsLoaded?.Invoke(pid, exits);
                            });
                        });
                    }
                }
            }
            // Acteurs de la carte (cellId + type + nom) : champ "actors" data-driven (fields.json), noms de monstres
            // resolus via la table du client (genericId -> nom).
            if (_catalog.FieldNumber("MapComplementaryInformationEvent", "actors") is int af)
            {
                var mapActors = MapActorsReader.Extract(p.Payload, af, _gameNames.MonsterName, _gameNames.NpcName);
                MapActorsDecoded?.Invoke(p.Pid, mapActors);
                _postFightMapPending.TryRemove(p.Pid, out _);
                RecordNpcSightings(p.Pid, mapActors);
                DetectArchMonsters(p.Pid, mapActors);
            }
            OnLotJMapInfo(p);   // Lot J : maisons de la carte + « dans une maison » ; fin de toute session de coffre à code
            OnLotKMapInfo(p);   // Lot K : accusé de map:sendMapRequest (APRÈS le décodage des acteurs)

            // Elements interactifs (ressources/zaaps/...) : decode protocole (type/skills/etat) + cellule depuis la
            // mapData (positions element_id->cell) pour capter AUSSI les interactifs SANS StatedElement (zaaps).
            // En tache de fond (lecture bundle). p.Payload deja deballe.
            var iPayload = p.Payload;
            var iPid = p.Pid;
            long iMap = _catalog.FieldNumber("MapComplementaryInformationEvent", "map_id") is int mf ? PayloadReader.Int(p.Payload, mf, 0) : 0;
            var interactiveTicket = roleplayTicket;
            _ = Task.Run(() =>
            {
                var positions = iMap != 0 ? _cellReader.LoadInteractivePositions(iMap) : null;
                // Repli : cellule d'une PORTE connue du world-graph par son element_id (m_id de la transition). Certaines portes
                // (type -1, skill 184, sans StatedElement) sont absentes des positions de la mapData -> l'interactif était jeté
                // (« porte introuvable en cellule 559 », carte 178785286 : élément 510369, invisible aussi sur la carte interactive).
                var wgTrs = iMap != 0 ? _worldGraph?.Transitions((int)iMap) : null;
                // Un interactif SANS cellule connue reste inaffichable sur la grille, mais il existe quand même côté serveur :
                // l'hôtelier d'un HDV (type 313..381) est dans ce cas sur plusieurs cartes et le client l'ouvre à distance.
                // On les garde donc à part pour l'ouverture directe au lieu de les perdre (« hôtelier introuvable »).
                var placeless = new List<MapInteractive>();
                var raw = MapInteractivesReader.Extract(iPayload,
                    eid => positions is not null && positions.TryGetValue(eid, out var c) ? c
                         : wgTrs?.FirstOrDefault(t => t.ElementId == eid) is { } wt ? wt.CellId : (int?)null,
                    droppedNoCell: (eid, type, skills) =>
                    {
                        placeless.Add(new MapInteractive(-1, eid, type, 0, skills, null));
                        TransportLog.Line(
                            $"[interactifs] carte {iMap} : élément {eid} (type {type}, skills {string.Join(",", skills.Select(s => s.SkillId))}) sans cellule : gardé pour l'ouverture directe");
                    });
                _placelessInteractives[iPid] = (iMap, placeless);
                var interactives = raw.Count == 0 ? new List<MapInteractive>() : raw.Select(x =>
                {
                    // ActionName = verbe du skill (Couper/Pecher/Utiliser/Entrer...). Name (du + specifique au - specifique) :
                    //  1) texte du PANNEAU directionnel (par element_id : "Atelier des sculpteurs", "Fermé"...) ;
                    //  2) nom de TYPE (Frene, Zaap, Panneau directionnel) ; 3) verbe du skill (portes/escaliers, type -1) ;
                    //  4) repli "Interactif (type N)".
                    string? action = x.GatherSkill is { } gs ? _gameNames.SkillName(gs.SkillId) : null;
                    string? sign = _gameNames.SignText(x.ElementId);
                    string? typeName = _gameNames.InteractiveName(x.ElementTypeId);
                    typeName ??= GameData?.InteractiveTypeName(x.ElementTypeId);   // base du jeu locale : « Plan de Travail » au lieu de « Interactif (type 97) »
                    return x with { Name = sign ?? typeName ?? action ?? $"Interactif (type {x.ElementTypeId})", ActionName = action };
                }).ToList();
                // Ensemble des element_id qui sont de VRAIES ressources (pour reconnaître une récolte MANUELLE) : capté au
                // décodage (skills encore actifs) et gardé même quand un skill se désactive en cours de récolte.
                var gset = new HashSet<int>();
                foreach (var it in interactives) if (IsGatherResource(it)) gset.Add(it.ElementId);
                foreach (var it in interactives.Concat(placeless))
                {
                    if (ZaapiTravel.IsUsable(it.ElementTypeId, it.Skills.Select(s => (s.SkillId, s.Enabled))))
                        ZaapiRegistry.RegisterInteractive(iMap, SubAreaOf(iMap), it.ElementTypeId, it.Skills.Select(s => (s.SkillId, s.Enabled)), name: it.Name);
                }
                if (interactiveTicket is null) return;
                PublishRoleplayMap(iPid, iMap, interactiveTicket, () =>
                {
                    _interactivesDecodedMap[iPid] = iMap;
                    _gatherElements[iPid] = gset;
                    MapInteractivesDecoded?.Invoke(iPid, interactives);
                });
                if (interactives.Count > 0)
                    TransportLog.Line($"[interactifs] {interactives.Count} sur la carte : " + string.Join(", ",
                        interactives.Take(12).Select(x => $"cell {x.CellId}={x.Name} [id {x.ElementId}]" + (x.Harvestable ? "" : "(non recoltable)"))));
            });
        }
        else if (p.Name == "TeleportDestinationsEvent")   // résolu du mapping STREAMÉ (pas de code brut)
        {
            // Liste des zaaps téléportables (envoyée à l'ouverture d'un zaap / du havre-sac). Numéros de champ
            // RENUMÉROTÉS par le patch 3.6.11.12 : la dérivation et la preuve sont dans TeleportReader.
            var (maps, spawn) = TeleportReader.ReadDestinations(p.Payload);
            // Une liste de ZAAPIS (1), de PRISMES (2) ou d'ANOMALIES (4) n'est PAS la liste des zaaps du personnage :
            // elle ne doit pas l'écraser. Le type 1 est quand même gardé à part (RememberZaapi via NoteTeleportOpened).
            int listType = TeleportReader.ReadListType(p.Payload);
            bool zaapList = listType is not (1 or 2 or 4);
            if (maps.Count > 0)
            {
                NoteTeleportOpened(p.Pid, listType, TeleportReader.ReadDestinationDetails(p.Payload));   // fenêtre d'interaction « Téléportation »
                _openTeleportDests[p.Pid] = maps;                          // dialogue ouvert (tout type) -> cibles hjc valides
                _openTeleportType[p.Pid] = listType;                       // AVANT le seq : le seq sert de barrière de publication
                _teleportDialogSeq.AddOrUpdate(p.Pid, 1, (_, s) => s + 1); // « un NOUVEAU dialogue de téléport (tout type) est arrivé »
            }
            if (zaapList && spawn != 0) _respawnMap[p.Pid] = spawn;   // Lot E : point de respawn (AVANT le seq de liste : barrière de publication)
            if (maps.Count > 0 && zaapList)
            {
                _knownZaaps[p.Pid] = maps;                             // snapshot des zaaps connus (choix du zaap)
                _zaapListSeq.AddOrUpdate(p.Pid, 1, (_, s) => s + 1);   // « une NOUVELLE liste de zaaps est arrivée »
            }
            string kept = listType == ZaapiTravel.ListType ? " -> zaapi"
                : zaapList ? "" : " -> ignorée : pas une liste de zaaps";
            TransportLog.Line($"[zaap] {maps.Count} destinations (type {listType}{kept}, spawn {spawn})");
        }
        else if (p.Name == "ZaapKnownListEvent")
        {
            // Zaaps DÉBLOQUÉS du perso, envoyés à la connexion (capture 23/10 : #1 = liste compacte d'ids de carte, 187 octets).
            // Base de la téléportation DIRECTE (sans zaap au sol ni havre-sac, TeleportRequest source_type 5).
            var maps = ZaapKnownListReader.Read(p.Payload, FNum("ZaapKnownListEvent", "destinations"));
            if (maps.Count > 0)
            {
                _directZaaps[p.Pid] = maps;
                _knownZaaps.TryAdd(p.Pid, maps);   // la liste d'un dialogue de zaap (plus fraîche) reste prioritaire
                RememberUnlockedZaaps(p.Pid, maps); // menu de l'en-tête : noms via les données du jeu, coût encore nul
            }
            TransportLog.Line($"[zaap] {maps.Count} zaap(s) débloqué(s) (liste de connexion) -> téléportation directe possible");
        }
        else if (p.Name == "GameRolePlayShowActorsEvent" && _catalog is not null)
        {
            // Acteurs qui apparaissent sur la carte (jsn) : memes acteurs (ljx) qu'au map event, au champ #1.
            if (_catalog.FieldNumber("GameRolePlayShowActorsEvent", "actors") is int af)
            {
                var actors = MapActorsReader.Extract(p.Payload, af, _gameNames.MonsterName, _gameNames.NpcName);
                if (actors.Count > 0) { MapActorsAdded?.Invoke(p.Pid, actors); DetectArchMonsters(p.Pid, actors); }
            }
        }
        else if (p.Name == "ReconnectToGameServerEvent" && _catalog is not null)
        {
            // Serveur de jeu (izs) : #3 = server_id -> nom via la table serversdataroot + i18n.
            if (_catalog.FieldNumber("ReconnectToGameServerEvent", "server_id") is int sf)
            {
                int serverId = (int)PayloadReader.Int(p.Payload, sf, 0);
                if (serverId != 0 && _gameNames.ServerName(serverId) is { } name) { MarkServerKnown(p.Pid); RememberServerIdentity(p.Pid, serverId, name); }
            }
        }
        else if (p.Name == "WhoIsEvent")
        {
            // Serveur courant (lqe) : reponse a un /whois. Le serverId y figure (ex. 294 -> Hell Mina) ; on prend
            // la valeur qui resout en nom de serveur. Methode d'un bot tiers (il forge un /whois sur son propre perso).
            int sid = WhoIsReader.ServerId(p.Payload, _gameNames.ServerName);
            if (sid != 0 && _gameNames.ServerName(sid) is { } name) { MarkServerKnown(p.Pid); RememberServerIdentity(p.Pid, sid, name); }
        }
        else if (p.Name == "ContextRemoveElementEvent" && _catalog is not null)
        {
            // Un acteur (ou element) quitte la carte : #2 = son contextualId (kmu).
            if (_catalog.FieldNumber("ContextRemoveElementEvent", "element_id") is int rf)
            {
                long id = PayloadReader.Int(p.Payload, rf, 0);
                if (id != 0) MapActorRemoved?.Invoke(p.Pid, id);
            }
        }
        else if (p.Name == "MapMovementEvent" && _catalog is not null)
        {
            // Deplacement d'un acteur : actorId (#5) + cellules du chemin (#1, points de virage).
            int cellsF = FNum("MapMovementEvent", "cells");
            int actorF = FNum("MapMovementEvent", "character_id");
            if (MapMovementReader.Read(p.Payload, cellsF, actorF) is { } mv)
            {
                var path = MapPathfinder.ExpandPath(mv.Cells);
                MapActorMoved?.Invoke(p.Pid, mv.ActorId, path);
                bool inFightNow = _inFight.TryGetValue(p.Pid, out var inf) && inf;
                if (!p.ClientToServer && !inFightNow)
                    NoteObservedOwnMoveEcho(p.Pid, mv.ActorId, path);
                // HORS COMBAT : cet echo serveur (jsj) correspond-il a NOTRE move injecte (meme case de destination) ? Si oui,
                // on programme le jqi de SECOURS : le vrai client ne confirmera pas ce trajet -> sans commit, le serveur laisse
                // le perso a l'origine et refuse le jqk. ConfirmMoveFallbackAsync ne l'enverra que si rien n'a resolu avant.
                // Écho de NOTRE perso uniquement (identité par charId) : un AUTRE acteur finissant sur la même case déclenchait
                // la confirmation au rythme de SON trajet (trop tôt -> rollback possible). Id inconnu -> comportement d'avant.
                bool mineEcho = !_charId.TryGetValue(p.Pid, out var meId) || meId == 0 || mv.ActorId == meId;
                if (!inFightNow && mineEcho && path.Count > 1 && _botMoveConfirm.TryGetValue(p.Pid, out var pend))
                    _ = ConfirmMoveFallbackAsync(p.Pid, pend.Seq, pend.Dest, pend.Cells, path);   // path = chemin ACCEPTÉ par le serveur (cadence socket)
                // En COMBAT : mettre a jour la CELLULE du combattant (moi OU un monstre) dans le modele de combat, sinon
                // l'auto-combat vise/repart de la cellule de PLACEMENT (bug : re-deplacement + cast sur case de depart).
                if (path.Count > 0 && inFightNow)
                {
                    int dest = path[path.Count - 1];
                    // On met à jour la case de TOUT combattant suivi — Y COMPRIS MOI. Le jsj est la position AUTORITAIRE du
                    // serveur. Avant, MA position venait UNIQUEMENT de la maj optimiste + correction jqt : or un déplacement
                    // TRONQUÉ par le serveur (ex 2 cases au lieu de 3, sans jqt) laissait ma position périmée -> les tours
                    // suivants je tirais hors portée/alignement (refus « lqn »). L'ancien risque de « décodage vers la case
                    // de départ » est levé (ExpandPath fiabilisé + octets bruts du chemin désormais toujours dispo).
                    long movedId = mv.ActorId;
                    _fighterVisibility.GetValueOrDefault(p.Pid)?.ConfirmPosition(movedId);
                    var updated = MutateFighters(p.Pid, fs =>
                    {
                        if (fs.All(f => f.ActorId != movedId)) return null;                       // acteur pas (encore) suivi
                        if (fs.First(f => f.ActorId == movedId).Cell == dest) return null;         // déjà à la bonne case -> pas de churn
                        return fs.Select(f => f.ActorId == movedId ? (dest, f.ActorId) : f).ToList();
                    });
                    if (updated is not null)
                    {
                        if (movedId < 0) TransportLog.Line($"[combat] ennemi {movedId} déplacé -> case {dest} (déplacement suivi)");
                        else TransportLog.Line($"[combat] MA position resync -> case {dest} (serveur autoritaire)");
                    }
                    // Combat tour-par-tour : un deplacement diffuse = le mien s'est resolu -> LIBERE la garde (le combat
                    // n'emet pas de jsq). Sans ca, un tour suivant rapide serait bloque par la garde de 4s encore armee.
                    _moveGuardUntil[p.Pid] = 0;
                }
            }
        }
        // End-of-reducer boundary for the REAL admitted ComboIngress stream:
        // update the classic multiclasse advisory immediately after a
        // spell/turn/portal/fighter observation, rather than waiting for a
        // separate fighter-stat change or the UI polling interval. The
        // observer coalesces one pending job per PID and revalidates all
        // fight/map/turn/grade facts. This is diagnostic ONLY: a decoder-local
        // sequence, cast-like event or inbound direction is not an ACK and
        // never dispatches a cast/move or advances a confirmed combo stage.
        if (comboPacketAdmitted && !p.ClientToServer && p.PayloadDecoded &&
            p.Name is ("GameActionFightEvent" or "FightTurnEvent"
                or "FightFighterShowEvent" or "FightEndEvent"
                or "FightStartingEvent" or "MapMovementEvent"))
            QueueCombatTacticalAdvisoryRefresh(p.Pid);
    }

    /// <summary>Purge les caches dépendant des niveaux de sorts (modificateurs, cooldowns/refus) au gain de niveau.</summary>
    private void InvalidateSpellCaches(uint pid)
    {
        ForgetSpellModifiers(pid);
        _castLedgers.TryRemove(pid, out _);
    }
}
