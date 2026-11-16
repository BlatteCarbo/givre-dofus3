using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Fenêtre d'interaction (banque / échange joueur / boutique PNJ) + actions d'inventaire de l'app =====================
// Actions UNITAIRES par UID (la ligne cliquée dans l'app = une pile précise, jamais « la 1re pile de ce gid ») et NON bloquantes :
// l'accusé arrive par les décodeurs (inventaire, coffre, échange) que l'UI relit à chaque tick. Aucune n'appelle
// SendAndWaitForCodeAsync -> un clic dans l'app n'écrase jamais l'attente d'un script en cours (un seul waiter par pid).
// Toutes passent par RelayForInject : identiques en MITM et en Socket (le dispatch route vers la socket du bot).
public sealed partial class LaunchService
{
    // Contenu de l'échange joueur vu des DEUX côtés (le registre _tradePlaced ne garde que nos poses, pour les retraits).
    private readonly ConcurrentDictionary<uint, TradeContent> _tradeContent = new();

    /// <summary>Échange ouvert pour ce perso (banque, échange joueur, boutique PNJ, dialogue de téléportation) ou None.</summary>
    public ExchangeKind CurrentExchange(uint pid) => InteractionLogic.Resolve(StorageOpen(pid), TradeOpen(pid), LotMOf(pid).ShopOpen, TeleportView(pid) is not null);

    // ---------------------------------------------------------------- téléportation (zaap, zaapi, zaap du havre-sac, prisme)
    /// <summary>Dialogue de téléportation ouvert : type de liste (0 zaap, 1 zaapi, 2 prisme, 3 havre-sac, 4 anomalie), destinations
    /// détaillées et carte d'ouverture (un changement de carte le périme : le jeu l'a fermé).</summary>
    public sealed record TeleportState(int Type, IReadOnlyList<TeleportReader.Destination> Destinations, long OpenedOnMap);
    private readonly ConcurrentDictionary<uint, TeleportState> _teleportOpen = new();

    private void NoteTeleportOpened(uint pid, int type, IReadOnlyList<TeleportReader.Destination> dests)
    {
        _teleportOpen[pid] = new TeleportState(type, dests, CurrentMap(pid) ?? 0);
        // Type 0 (zaap) et 3 (havre-sac) seulement : c'est la liste des zaaps du personnage pour la carte du monde.
        if (WorldMapZaapTravel.IsCharacterZaapList(type))
            RememberWorldMapZaaps(pid, type, CharacterId(pid), dests);
        if (type == ZaapiTravel.ListType)
            RememberZaapi(pid, CharacterId(pid), dests);
    }

    /// <summary>Dialogue de téléportation ouvert sur la carte courante, ou null (fermé, ou carte quittée).</summary>
    public TeleportState? TeleportView(uint pid)
        => _teleportOpen.TryGetValue(pid, out var t) && t.OpenedOnMap == (CurrentMap(pid) ?? 0) ? t : null;

    /// <summary>Téléporte vers une destination du dialogue OUVERT. Zaap au sol : source omise. Havre-sac : source 3.
    /// Zaapi : source_type 1 et destination_type 1, coût annoncé exigé. Les autres types gardent (0, type).</summary>
    public bool TeleportTo(uint pid, long mapId)
    {
        if (TeleportView(pid) is not { } t || !CanTeleportTo(pid, mapId)) return false;
        // The current catalog identifies type 4 as "anomaly", but no owned
        // session has proved the TeleportRequest wire contract for that tab.
        // An observed destination is not sufficient proof to forge its action.
        if (t.Type == 4)
        {
            TransportLog.Line($"[anomalie] pid {pid} : téléportation refusée, contrat réseau de l'onglet Anomalie non confirmé.");
            return false;
        }
        var arrival = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TextInfo? refusal = null;
        long textSeq = LastTextInfo(pid)?.Seq ?? 0;
        void Observe(DecodedPacket packet)
        {
            if (packet.Pid != pid || packet.ClientToServer) return;
            if (CurrentMap(pid) == mapId) arrival.TrySetResult();
            if (packet.Name == "TextInformationEvent" && LastTextInfo(pid) is { } text && text.Seq > textSeq)
                Interlocked.CompareExchange(ref refusal, text, null);
        }
        PacketDecoded += Observe;
        bool sent = t.Type switch
        {
            0 => Teleport(pid, mapId),
            ZaapiTravel.ListType => TrySendZaapi(pid, mapId),
            3 => Teleport(pid, mapId, sourceType: 3),
            _ => Teleport(pid, mapId, sourceType: 0, destinationType: t.Type),
        };
        if (!sent)
        {
            PacketDecoded -= Observe;
            Toasts.Post(t.Type == ZaapiTravel.ListType ? ZaapiTravel.SendFailedReason : WorldMapZaapTravel.SendFailedReason, success: false);
        }
        else
        {
            if (CurrentMap(pid) == mapId) arrival.TrySetResult();
            _ = ReportArrivalAsync();
        }
        return sent;

        async Task ReportArrivalAsync()
        {
            try
            {
                await arrival.Task.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                var destination = t.Destinations.FirstOrDefault(d => d.MapId == mapId);
                if (destination is { CostKnown: true, Cost: >= 0 })
                    Toasts.Post(t.Type == ZaapiTravel.ListType
                        ? ZaapiTravel.ArrivedReason(mapId, destination.Cost)
                        : WorldMapZaapTravel.ArrivedReason(mapId, destination.Cost));
            }
            catch (TimeoutException)
            {
                string reason = WorldMapZaapTravel.UnconfirmedReason(mapId);
                if (Volatile.Read(ref refusal) is { } text)
                {
                    reason = GameNames.InfoMessageText(text.MessageType, text.MessageId, text.Params);
                    if (string.IsNullOrWhiteSpace(reason))
                        reason = $"message serveur {text.MessageType}/{text.MessageId}"
                            + (text.Params.Count > 0 ? " : " + string.Join(", ", text.Params) : "");
                }
                Toasts.Post(reason, success: false);
            }
            finally { PacketDecoded -= Observe; }
        }
    }

    /// <summary>Sous-zone du havre-sac (fixe dans les données du jeu) : sert à savoir si le perso y est.</summary>
    private const int HavenBagSubArea = 851;
    public bool InHavenBag(uint pid) => CurrentSubArea(pid) == HavenBagSubArea;

    /// <summary>Entre dans le havre-sac ou en sort (la requête du jeu est un aller-retour). Ferme d'abord tout dialogue ouvert,
    /// sinon le serveur refuse. Refusé en combat.</summary>
    public bool ToggleHavenBag(uint pid)
    {
        if (IsInFight(pid)) return false;
        if (OpenInteraction(pid) is not null) LeaveDialog(pid);
        return EnterHavenBag(pid);
    }

    /// <summary>Contenu de l'échange joueur ouvert (null = aucun). Lecture seule, mis à jour par le décodeur.</summary>
    public TradeContent? TradeView(uint pid) => TradeOpen(pid) && _tradeContent.TryGetValue(pid, out var c) ? c : null;

    /// <summary>Boutique PNJ ouverte : (id modèle du PNJ, objets en vente). Items vide si aucune.</summary>
    public (long NpcId, IReadOnlyList<LotMReader.ShopItem> Items) ShopView(uint pid)
    {
        var st = LotMOf(pid);
        return st.ShopOpen ? (st.ShopNpc, st.ShopItems) : (0, Array.Empty<LotMReader.ShopItem>());
    }

    // Modèles de banquier connus (mêmes que npc:npcBank côté Lua) ; repli sur les sentinelles de la capture.
    private static readonly int[] BankerTemplates = { 522, 520, 100 };

    /// <summary>OUVRE la banque depuis l'app : banquier visible sur la carte s'il est reconnu, sinon les sentinelles capturées
    /// (-20000 puis -20001). Réutilise un coffre déjà ouvert. Gaté Perm.Gather (dans BankOpenAsync).</summary>
    public async Task<bool> BankOpenHereAsync(uint pid, IReadOnlyList<MapActor>? actors, CancellationToken ct = default)
    {
        if (actors?.FirstOrDefault(a => a.Kind == MapActorKind.Npc && BankerTemplates.Contains(a.NpcId)) is { } banker)
            return await BankOpenAsync(pid, 0, true, ct, banker.ActorId).ConfigureAwait(false);
        return await BankOpenAsync(pid, 0, true, ct).ConfigureAwait(false)
            || await BankOpenAsync(pid, 0, true, ct, -20001).ConfigureAwait(false);
    }

    // Envoie une trame forgée par l'action `what` ; false (et journal) si non forgeable ou non injectée.
    private bool InjectAction(uint pid, byte[] pkt, string tag, string what, bool exempt = false)
    {
        var relay = RelayForInject(exempt);
        if (relay is null) return false;
        if (pkt.Length == 0) { TransportLog.Line($"[{tag}] pid {pid} : {what} NON forgé ({MoveForge.LastForgeError})."); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[{tag}] pid {pid} : {what} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    private InventoryStore.Item? BagStack(uint pid, int uid)
        => Inventory.Get(pid, uid) is { Position: 63, Qty: > 0 } it ? it : null;

    // ---------------------------------------------------------------- banque (coffre OUVERT)

    /// <summary>Dépose <paramref name="qty"/> (&lt;= 0 = toute la pile) d'une pile du SAC dans le coffre ouvert.</summary>
    public bool BankDepositUid(uint pid, int uid, int qty)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !StorageOpen(pid)) return false;
        if (BagStack(pid, uid) is not { } st || !ItemCanBank(pid, uid)) return false;
        int q = InteractionLogic.ClampQty(qty, st.Qty);
        return InjectAction(pid, MoveForge.BuildExchangeObjectMoveFramed(uid, q), "banque", $"dépôt uid {uid} (gid {st.Gid}) x{q}");
    }

    /// <summary>Retire <paramref name="qty"/> (&lt;= 0 = toute la pile) d'une pile du coffre ouvert (quantité négative).</summary>
    public bool BankWithdrawUid(uint pid, int uid, int qty)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !StorageOpen(pid)) return false;
        if (Bank.Get(pid, uid) is not { Qty: > 0 } st) return false;
        int q = InteractionLogic.ClampQty(qty, st.Qty);
        return InjectAction(pid, MoveForge.BuildExchangeObjectMoveSignedFramed(uid, -q), "banque", $"retrait uid {uid} (gid {st.Gid}) x{q}");
    }

    /// <summary>Retire TOUT le coffre ouvert (corps vide, comme le bouton du jeu).</summary>
    public bool BankWithdrawEverything(uint pid)
        => LicenseService.HasPermission(LicenseService.Perm.Gather) && StorageOpen(pid)
           && InjectAction(pid, MoveForge.BuildBankWithdrawAllFramed(), "banque", "retrait de tout le coffre");

    /// <summary>Dépose tout le sac (mode configuré dans l'onglet Banque : tout / déjà présents / objets visibles).</summary>
    public bool BankDepositConfigured(uint pid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !StorageOpen(pid)) return false;
        var (mode, pkt) = ConfiguredDepositPacket(pid);
        return InjectAction(pid, pkt, "banque", $"dépôt du sac (mode {mode})");
    }

    /// <summary>Kamas sac &lt;-&gt; coffre ouvert, sans attente : &gt; 0 dépôt, &lt; 0 retrait. L'accusé met à jour BankKamas.</summary>
    public bool BankMoveKamas(uint pid, long signedQty)
        => signedQty != 0 && LicenseService.HasPermission(LicenseService.Perm.Gather) && StorageOpen(pid)
           && InjectAction(pid, MoveForge.BuildExchangeMoveKamaFramed(signedQty), "banque", $"kamas {signedQty:+#;-#}");

    // ---------------------------------------------------------------- échange joueur

    /// <summary>Pose <paramref name="qty"/> (&lt;= 0 = toute la pile) d'une pile du SAC dans l'échange ouvert.</summary>
    public bool TradePutUid(uint pid, int uid, int qty)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !TradeOpen(pid)) return false;
        if (BagStack(pid, uid) is not { } st || !ItemCanTrade(pid, uid)) return false;
        int q = InteractionLogic.ClampQty(qty, st.Qty);
        var session = _tradeStartedPkt.GetValueOrDefault(pid);
        if (!SettleTradeStep(pid, timeoutMs: 0)) return false;
        return SendTradeItemMutation(pid, session, uid, q,
            () => InjectAction(pid, MoveForge.BuildExchangeObjectMoveSignedFramed(uid, q), "échange", $"pose uid {uid} (gid {st.Gid}) x{q}"));
    }

    /// <summary>Retire de l'échange <paramref name="qty"/> (&lt;= 0 = tout) d'un objet que CE perso y a posé.</summary>
    public bool TradeTakeBackUid(uint pid, int uid, int qty)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !TradeOpen(pid)) return false;
        var placed = TradeView(pid)?.Mine.FirstOrDefault(x => x.Uid == uid) ?? default;
        if (placed.Qty <= 0) return false;
        int q = InteractionLogic.ClampQty(qty, placed.Qty);
        var session = _tradeStartedPkt.GetValueOrDefault(pid);
        if (!SettleTradeStep(pid, timeoutMs: 0)) return false;
        return SendTradeItemMutation(pid, session, uid, -q,
            () => InjectAction(pid, MoveForge.BuildExchangeObjectMoveSignedFramed(uid, -q), "échange", $"retire uid {uid} (gid {placed.Gid}) x{q}"));
    }

    /// <summary>Fixe les kamas posés dans l'échange à <paramref name="total"/> (le serveur attend la variation).</summary>
    public bool TradeSetKamas(uint pid, long total)
    {
        lock (_tradeStartedPkt)
        {
            if (TradeView(pid) is not { } c) return false;
            long delta = Math.Max(0, total) - c.MyKamas;
            return delta != 0 && TradePutKamasCore(pid, delta, waitForContent: false);
        }
    }

    /// <summary>Refuse la demande d'échange reçue (DialogLeaveRequest, comme la croix du jeu) et l'oublie.</summary>
    public bool TradeRefuse(uint pid)
    {
        _tradePending.TryRemove(pid, out _);
        return InjectAction(pid, MoveForge.BuildDialogLeaveFramed(), "échange", "refus de la demande", exempt: true);
    }

    // Décodeur (thread réseau) : contenu des deux côtés + états « prêt ». Jamais d'exception.
    private void TrackTradeContent(DecodedPacket p)
    {
        try
        {
            if (!_tradeContent.TryGetValue(p.Pid, out var c)) return;
            switch (p.Name)
            {
                case "ExchangeObjectsAddedEvent":
                case "ExchangeObjectsModifiedEvent":
                {
                    string msg = p.Name;
                    bool remote = PayloadReader.Bool(p.Payload, FNum(msg, "remote"));
                    c.Upsert(remote, InventoryReader.ReadContentAt(p.Payload, FNum(msg, "objects")).Select(i => (i.Uid, i.Gid, i.Qty)));
                    break;
                }
                case "ExchangeObjectRemovedEvent":
                    c.Remove(PayloadReader.Bool(p.Payload, FNum(p.Name, "remote")),
                        new[] { (int)PayloadReader.Int(p.Payload, FNum(p.Name, "object_uid"), 0) });
                    break;
                case "ExchangeObjectsRemovedEvent":
                    c.Remove(PayloadReader.Bool(p.Payload, FNum(p.Name, "remote")), InventoryReader.ReadUids(p.Payload, FNum(p.Name, "objects_uid")));
                    break;
                case "ExchangeKamaModifiedEvent":
                    c.SetKamas(PayloadReader.Bool(p.Payload, FNum(p.Name, "remote")), PayloadReader.Int(p.Payload, FNum(p.Name, "quantity"), 0));
                    break;
                case "ExchangeReadyEvent":
                {
                    long cid = PayloadReader.Int(p.Payload, FNum(p.Name, "character_id"), 0);
                    long me = CharacterId(p.Pid);
                    if (cid != 0 && me != 0) c.SetReady(cid != me, PayloadReader.Bool(p.Payload, FNum(p.Name, "ready")));
                    break;
                }
            }
        }
        catch { /* affichage best-effort : ne casse jamais le décodage */ }
    }

    // ---------------------------------------------------------------- inventaire (pile précise)

    /// <summary>Utilise UN exemplaire d'une pile du sac (ObjectUseRequest).</summary>
    public bool UseItemUid(uint pid, int uid)
        => LicenseService.HasPermission(LicenseService.Perm.Gather) && BagStack(pid, uid) is { } st
           && InjectAction(pid, MoveForge.BuildObjectUseFramed(uid), "inventaire", $"utilise uid {uid} (gid {st.Gid})");

    /// <summary>Jette au sol <paramref name="qty"/> (&lt;= 0 = toute la pile) d'une pile du sac. Refusé en combat.</summary>
    public bool DropItemUid(uint pid, int uid, int qty)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || IsInFight(pid) || BagStack(pid, uid) is not { } st) return false;
        int q = InteractionLogic.ClampQty(qty, st.Qty);
        return InjectAction(pid, MoveForge.BuildObjectDropFramed(uid, q), "inventaire", $"jette uid {uid} (gid {st.Gid}) x{q}");
    }

    /// <summary>DÉTRUIT <paramref name="qty"/> (&lt;= 0 = toute la pile) d'une pile du sac (ObjectDeleteRequest, irréversible).</summary>
    public bool DeleteItemUid(uint pid, int uid, int qty)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || BagStack(pid, uid) is not { } st) return false;
        int q = InteractionLogic.ClampQty(qty, st.Qty);
        return InjectAction(pid, MoveForge.BuildObjectDeleteFramed(uid, q), "inventaire", $"détruit uid {uid} (gid {st.Gid}) x{q}");
    }

    /// <summary>Emplacement où équiper ce gid : 1er emplacement possible LIBRE, sinon le 1er possible (échange avec l'objet porté).
    /// -1 = pas un équipement (données du jeu).</summary>
    public int EquipTarget(uint pid, long gid)
    {
        var slots = _gameNames.EquipPositions((int)gid).Where(p => p is >= 0 and <= 15 or 28).ToList();
        if (slots.Count == 0) return -1;
        return slots.FirstOrDefault(p => Inventory.ItemAt(pid, p) is null, slots[0]);
    }

    /// <summary>Équipe une pile du sac à l'emplacement prévu (<see cref="EquipTarget"/>). false si pas un équipement.</summary>
    public bool EquipItemUid(uint pid, int uid)
        => BagStack(pid, uid) is { } st && EquipTarget(pid, st.Gid) is var pos && pos >= 0 && SendObjectSetPosition(pid, uid, pos);

    /// <summary>Emplacements possibles d'un objet (données du jeu) ; vide si ce n'est pas un équipement.</summary>
    public IReadOnlyList<int> EquipPositions(long gid) => _gameNames.EquipPositions((int)gid);

    /// <summary>Équipe une pile du sac à un emplacement PRÉCIS (sélecteur d'un emplacement, ex. anneau gauche / droit).</summary>
    public bool EquipItemUidAt(uint pid, int uid, int position)
        => BagStack(pid, uid) is { } st && EquipPositions(st.Gid).Contains(position) && SendObjectSetPosition(pid, uid, position);

    /// <summary>Déséquipe l'objet porté (retour au sac, position 63).</summary>
    public bool UnequipItemUid(uint pid, int uid)
        => Inventory.Get(pid, uid) is { } it && it.Position != 63 && SendObjectSetPosition(pid, uid, 63);

    // ---------------------------------------------------------------- éditeur de trajet (mini-carte du monde)
    private readonly ConcurrentDictionary<int, IReadOnlySet<(int X, int Y)>> _worldCoords = new();
    private MapPositions? _worldCoordsSource;

    /// <summary>Coordonnées des cartes d'un calque (1 = Monde des Douze), en cache tant que la table des positions ne change pas.</summary>
    public IReadOnlySet<(int X, int Y)> WorldCoords(int worldMap)
    {
        var src = _mapPositions;
        if (src is null) return new HashSet<(int X, int Y)>();
        if (!ReferenceEquals(src, _worldCoordsSource)) { _worldCoords.Clear(); _worldCoordsSource = src; }
        return _worldCoords.GetOrAdd(worldMap, w =>
        {
            var set = src.CoordsOf(w);
            // + cartes des données du jeu (relevés DofusDB) absentes de la table extraite du client : la mini-carte du monde n'est plus « vide » pour ces zones
            try
            {
                if (GameData is { } db)
                    foreach (var (_, p) in db.AllMapPositions()) if (p.World == w) set.Add((p.X, p.Y));
            }
            catch (Exception e) when (e is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { /* données illisibles : table client seule */ }
            return set;
        });
    }

    /// <summary>Nombre de cartes à pied (graphe du jeu, critères du perso) de a à b ; 0 si a == b ; -1 si aucun chemin.</summary>
    public int WalkHops(uint pid, long a, long b)
        => a == b ? 0 : WorldPath((int)a, (int)b, pid: pid) is { Count: > 0 } path ? path.Count : -1;

    private void ForgetInteraction(uint pid) { _tradeContent.TryRemove(pid, out _); _teleportOpen.TryRemove(pid, out _); }
}
