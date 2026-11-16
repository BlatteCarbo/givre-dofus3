using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.LotM;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Lot M : succès, anomalies, téléportation, donjon, documents, boutiques PNJ, historique de prix =====================
// État par bot écrit par le thread réseau (OnLotMPacket, appelé depuis OnDecoded) et lu par le thread du script via LotMGame.
// Les envois attendent le message de réponse nommé (SendAndWaitForCodeAsync) ; un refus ou un silence est rapporté, jamais inventé.
public sealed partial class LaunchService
{
    internal IReadOnlyList<PlaceDoor> WorldPlaceDoors(long mapId)
        => PlaceDoor.FromTransitions(_worldGraph?.Transitions((int)mapId) ?? Array.Empty<MapTransition>());

    internal sealed class LotMState
    {
        public readonly AchievementLedger Ledger = new();
        public readonly AchievementLoginCapture AchievementCapture = new();
        public readonly ConcurrentDictionary<int, AnomalyState> Anomalies = new();
        public readonly ConcurrentDictionary<int, ShopVisit> Shops = new();
        public volatile OfferState? Offer;
        public volatile int DocumentId;
        public volatile bool ShopOpen;
        public long ShopNpc;                       // id modèle du PNJ dont la boutique est ouverte (0 = inconnu)
        public IReadOnlyList<LotMReader.ShopItem> ShopItems = Array.Empty<LotMReader.ShopItem>();
        public volatile bool AutoClaim = true;   // vrai par défaut : relevé sur deux bots du GivreClone d'origine (isAutoClaim)
        public long AutoClaimAt;                   // TickCount64 de la prochaine réclamation automatique (0 = rien de planifié)
        public readonly ConcurrentDictionary<int, AlterationInfo> Alterations = new();   // altérations actives (id -> expiration)
        public volatile bool AlterationsKnown;     // liste complète reçue (AlterationsEvent) : une absence est fiable
        public readonly object Gate = new();
        public readonly List<TaskCompletionSource<OfferState>> OfferWaiters = new();
    }

    private readonly ConcurrentDictionary<uint, LotMState> _lotM = new();
    private LotMState LotMOf(uint pid) => _lotM.GetOrAdd(pid, _ => new LotMState());

    private volatile GameDb? _gameDb;
    private long _gameDbNextAttempt;
    public GameDb? ReloadGameData()
    {
        lock (this) _gameDbNextAttempt = 0;
        return GameData;
    }
    /// <summary>Base de données du jeu (noms, succès, sous-zones, documents, recettes, donjons, quêtes) ; null si introuvable.</summary>
    public GameDb? GameData
    {
        get
        {
            if (_gameDb is { } loaded) return loaded;
            lock (this)
            {
                if (_gameDb is not null || Environment.TickCount64 < _gameDbNextAttempt) return _gameDb;
                _gameDbNextAttempt = Environment.TickCount64 + 5000;
                var path = GameDb.Locate(MZP19LocalClone.DataRoot, new[] { MZP19LocalClone.DataRoot, AppContext.BaseDirectory });
                _gameDb = path is null ? null : GameDb.TryOpen(path);
                return _gameDb;
            }
        }
    }

    private NpcSightingStore? _sightings;
    private long _sightingsSavedAt;
    private static string SightingsPath => Path.Combine(MZP19LocalClone.DataRoot, "npc-sightings.json");
    public NpcSightingStore NpcSightings
    {
        get
        {
            if (_sightings is not null) return _sightings;
            string? json = null;
            try { if (File.Exists(SightingsPath)) json = File.ReadAllText(SightingsPath); } catch { }
            return _sightings ??= NpcSightingStore.FromJson(json);
        }
    }

    internal string LotMGameVersion => GameData is { } d ? (Path.GetFileName(Path.GetDirectoryName(d.Path)) ?? "?") : "?";

    // ---------------------------------------------------------------- décodage
    private void OnLotMPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is not { } name) return;
        switch (name)
        {
            case "AchievementsEvent":
            {
                var st = LotMOf(p.Pid);
                st.Ledger.Seed(LotMReader.AchievedList(p.Payload).Select(a => a.Id));
                CaptureLoginAchievements(p, st);
                break;
            }
            case "AuthenticationTicketAcceptedEvent":
                LotMOf(p.Pid).AchievementCapture.OpenLogin();
                break;
            case "CharacterSelectionEvent" when PayloadReader.Field(p.Payload, FNum("CharacterSelectionEvent", "success")) is not null:
                LotMOf(p.Pid).AchievementCapture.OpenLogin();
                break;
            case "AchievementFinishedEvent":
                if (LotMReader.Finished(p.Payload) is { } fin)
                {
                    var st = LotMOf(p.Pid);
                    if (st.Ledger.OnFinished(fin.Id, fin.FinishedLevel)) ScheduleAutoClaim(p.Pid);
                }
                break;
            case "AlterationsEvent":   // liste complète (connexion) : remplace tout
            {
                var st = LotMOf(p.Pid);
                st.Alterations.Clear();
                foreach (var a in ReadAlterations(p.Payload, FNum("AlterationsEvent", "alterations"))) st.Alterations[a.Id] = a;
                st.AlterationsKnown = true;
                break;
            }
            case "AlterationsUpdateEvent":
            {
                var st = LotMOf(p.Pid);
                foreach (var a in ReadAlterations(p.Payload, FNum("AlterationsUpdateEvent", "alterations"))) st.Alterations[a.Id] = a;
                break;
            }
            case "AlterationAddedEvent":
            {
                var st = LotMOf(p.Pid);
                foreach (var a in ReadAlterations(p.Payload, FNum("AlterationAddedEvent", "alteration"))) st.Alterations[a.Id] = a;
                break;
            }
            case "AlterationRemovedEvent":
            {
                // champ « alteration » : message Alteration complet ou simple id selon la version -> les deux sont lus
                int f = FNum("AlterationRemovedEvent", "alteration");
                var st = LotMOf(p.Pid);
                foreach (var a in ReadAlterations(p.Payload, f)) st.Alterations.TryRemove(a.Id, out _);
                if (p.Payload.FirstOrDefault(x => x.Field == f && x.Message is null) is { } raw && raw.Varint > 0)
                    st.Alterations.TryRemove((int)raw.Varint, out _);
                break;
            }
            case "AchievementRewardResultEvent":
            {
                // accusé de TOUTE réclamation (UI, script, automatique) : la récompense quitte la liste en attente
                var (rid, ok) = LotMReader.RewardResult(p.Payload);
                if (rid != 0) LotMOf(p.Pid).Ledger.OnRewardResult(rid, ok);
                break;
            }
            case "AnomalyStateEvent":
            {
                var a = LotMReader.AnomalyState(p.Payload);
                var map = LotMOf(p.Pid).Anomalies;
                bool had = map.TryGetValue(a.SubareaId, out var prev);
                var now = new AnomalyState(a.SubareaId, a.Open, a.ClosingRaw, a.RewardRate, DateTime.UtcNow);
                map[a.SubareaId] = now;
                // Bascule réelle seulement (une anomalie inconnue reçue ouverte peut être un simple état à la connexion).
                if (had && prev!.Open != a.Open) RaiseAnomalyChanged(p.Pid, now, a.Open);
                break;
            }
            case "AnomalyOpenedEvent":
            {
                int sa = LotMReader.AnomalyOpenedSubarea(p.Payload);
                if (sa == 0) break;
                var map = LotMOf(p.Pid).Anomalies;
                bool wasOpen = map.TryGetValue(sa, out var prev) && prev.Open;
                var now = new AnomalyState(sa, true, prev?.ClosingRaw ?? 0, prev?.RewardRate ?? 0, DateTime.UtcNow);
                map[sa] = now;
                if (!wasOpen) RaiseAnomalyChanged(p.Pid, now, true);
                break;
            }
            case "AnomalySubareaInformationEvent":
                foreach (var a in LotMReader.AnomalySubareas(p.Payload))
                    LotMOf(p.Pid).Anomalies[a.SubareaId] = new AnomalyState(a.SubareaId, a.Open, a.ClosingRaw, a.RewardRate, DateTime.UtcNow);
                break;
            case "DocumentReadingBeginEvent":
                LotMOf(p.Pid).DocumentId = LotMReader.DocumentId(p.Payload); break;
            case "DialogLeaveEvent":
            case "ExchangeLeaveEvent":
            {
                _teleportOpen.TryRemove(p.Pid, out _);   // fenêtre « Téléportation » : le dialogue du zaap est fermé
                var st = LotMOf(p.Pid);
                st.DocumentId = 0; st.ShopOpen = false; st.ShopNpc = 0;
                break;
            }
            case "ExchangeNpcShopStartedEvent":
            {
                var (npc, items) = LotMReader.NpcShop(p.Payload);
                var st = LotMOf(p.Pid);
                st.ShopItems = items; st.ShopNpc = npc; st.ShopOpen = true;
                if (npc != 0 && npc <= int.MaxValue) st.Shops[(int)npc] = new ShopVisit(items, DateTime.UtcNow, LotMGameVersion);
                break;
            }
            default:
                if (LotMReader.TeleportOffer(name, p.Payload) is { } offer)
                {
                    var st = LotMOf(p.Pid);
                    var os = new OfferState(offer, DateTime.UtcNow);
                    st.Offer = os;
                    lock (st.Gate) { foreach (var w in st.OfferWaiters) w.TrySetResult(os); st.OfferWaiters.Clear(); }
                }
                else if (LotMReader.TeleportClosedKind(name) is not null) LotMOf(p.Pid).Offer = null;
                break;
        }
    }

    /// <summary>
    /// Une fois par connexion, si la capture de découverte est armée : journalise AchievementsEvent en entier.
    /// TransportLog.Line coupe à 4096 caractères (c'est le dump tronqué à ~239 lignes) ; on envoie donc des morceaux qui se recollent.
    /// </summary>
    private void CaptureLoginAchievements(DecodedPacket p, LotMState st)
    {
        if (!AppConfig.Current.PacketDump || !LicenseService.IsDeveloper || !DiscoveryDumpStillAllowed()) return;
        if (!st.AchievementCapture.TryTake()) return;
        string text = AchievementEventDump.Format(p.Code, p.Payload);
        foreach (string part in AchievementEventDump.Chunks(text))
            TransportLog.Line(part);
    }

    /// <summary>Mémorise où chaque PNJ a été vu (nom de modèle → carte/cellule/coordonnées) ; enregistre au plus toutes les 30 s.</summary>
    private void RecordNpcSightings(uint pid, IReadOnlyList<MapActor> actors)
    {
        if (CurrentMap(pid) is not long map) return;
        var npcs = actors.Where(a => a.Kind == MapActorKind.Npc && a.NpcId > 0).ToList();
        if (npcs.Count == 0) return;
        var xy = MapCoordOf(map);
        var now = DateTime.UtcNow;
        foreach (var a in npcs) NpcSightings.Record(a.NpcId, map, a.CellId, xy?.X ?? 0, xy?.Y ?? 0, now, LotMGameVersion);
        long t = Environment.TickCount64;
        if (t - _sightingsSavedAt < 30_000) return;
        _sightingsSavedAt = t;
        try { Directory.CreateDirectory(Path.GetDirectoryName(SightingsPath)!); File.WriteAllText(SightingsPath, NpcSightings.ToJson()); } catch { }
    }

    // ---------------------------------------------------------------- envois
    /// <summary>Réclame la récompense d'un succès ; vrai si le serveur confirme (AchievementRewardResultEvent success).</summary>
    public async Task<bool> AchievementClaimAsync(uint pid, int id, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildAchievementRewardFramed(id)),
            new[] { "AchievementRewardResultEvent" }, 5000,
            p => LotMReader.RewardResult(p.Payload).Id == id, ct).ConfigureAwait(false);
        if (ev is null) { TransportLog.Line($"[succès] pid {pid} : récompense {id} sans réponse du serveur."); return false; }
        var (rid, ok) = LotMReader.RewardResult(ev.Payload);
        LotMOf(pid).Ledger.OnRewardResult(rid != 0 ? rid : id, ok);
        TransportLog.Line($"[succès] pid {pid} : récompense {id} {(ok ? "reçue" : "refusée")}");
        return ok;
    }

    // Réglage « Succès » de chaque perso (onglet Paramètres), poussé par l'UI ; absent = activé (défaut d'origine).
    private readonly ConcurrentDictionary<uint, bool> _autoClaimSetting = new();
    public void SetAutoClaimSetting(uint pid, bool on) => _autoClaimSetting[pid] = on;

    /// <summary>Récupération automatique active : réglage du perso ET pas coupée par le script (achievement:setAutoClaim).</summary>
    internal bool AutoClaimOn(uint pid) => LotMOf(pid).AutoClaim && _autoClaimSetting.GetValueOrDefault(pid, true);

    /// <summary>Demande la récompense SANS attente bloquante : l'accusé est lu par OnLotMPacket. Sert à la récupération
    /// automatique, qui tourne pendant les scripts et ne doit jamais remplacer leur attente en cours (un seul waiter par pid).</summary>
    private bool AchievementClaimSend(uint pid, int id)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        bool ok = relay.Inject(pid, MoveForge.BuildAchievementRewardFramed(id));
        TransportLog.Line($"[succès] pid {pid} : récompense {id} demandée (auto) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    private void ScheduleAutoClaim(uint pid)
    {
        var st = LotMOf(pid);
        if (!AutoClaimOn(pid)) return;
        st.AutoClaimAt = Environment.TickCount64 + (long)AchievementLedger.AutoClaimDelay(Random.Shared).TotalMilliseconds;
        _ = Task.Run(async () =>
        {
            while (st.AutoClaimAt != 0)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (Environment.TickCount64 < st.AutoClaimAt) continue;
                if (IsInFight(pid)) { st.AutoClaimAt = Environment.TickCount64 + 1500; continue; }   // jamais pendant un combat
                st.AutoClaimAt = 0;
                foreach (var id in st.Ledger.AutoClaimable())
                {
                    if (!AutoClaimOn(pid) || IsInFight(pid)) break;
                    if (!AchievementClaimSend(pid, id)) break;
                    for (int i = 0; i < 25 && st.Ledger.HasReward(id); i++) await Task.Delay(200).ConfigureAwait(false);   // accusé (5 s max)
                    await Task.Delay(Random.Shared.Next(400, 900)).ConfigureAwait(false);
                }
            }
        });
    }

    /// <summary>Une anomalie s'ouvre (true) ou se ferme (false) pour ce perso (événements du serveur, pas l'état de connexion).
    /// Thread réseau : les abonnés repostent sur l'UI.</summary>
    public event Action<uint, AnomalyState, bool>? AnomalyChanged;
    private void RaiseAnomalyChanged(uint pid, AnomalyState st, bool opened)
    {
        try { AnomalyChanged?.Invoke(pid, st, opened); } catch (Exception e) { TransportLog.Line($"[anomalie] abonné en erreur : {e.Message}"); }
    }

    // ---------------------------------------------------------------- altérations (élixirs uchroniques, bonus / malus)
    private IEnumerable<AlterationInfo> ReadAlterations(IReadOnlyList<ProtoField> payload, int field)
    {
        int fId = FNum("Alteration", "alteration_id"), fType = FNum("Alteration", "expiration_type"), fVal = FNum("Alteration", "expiration_value");
        foreach (var e in payload)
        {
            if (e.Field != field || e.Message is null) continue;
            int id = (int)PayloadReader.Int(e.Message, fId, 0);
            if (id > 0) yield return new AlterationInfo(id, (int)PayloadReader.Int(e.Message, fType, 0), PayloadReader.Int(e.Message, fVal, 0));
        }
    }

    /// <summary>Altérations actives du perso (élixirs uchroniques 50/51/52, bonus, malus), triées par id.</summary>
    public IReadOnlyList<AlterationInfo> Alterations(uint pid) => LotMOf(pid).Alterations.Values.OrderBy(a => a.Id).ToList();

    /// <summary>Altération <paramref name="id"/> active ? null = inconnu (liste jamais reçue : client déjà connecté avant l'app).</summary>
    public bool? HasAlteration(uint pid, int id)
    {
        var st = LotMOf(pid);
        if (st.Alterations.ContainsKey(id)) return true;
        return st.AlterationsKnown ? false : null;
    }

    /// <summary>Historique de prix d'un objet (ObjectAveragePricesHistory) ; null si le serveur ne répond pas.</summary>
    public async Task<IReadOnlyList<LotMReader.PricePoint>?> PriceHistoryAsync(uint pid, long gid, CancellationToken ct)
    {
        var relay = RelayForInject(); if (relay is null) return null;
        var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildPriceHistoryFramed((int)gid)),
            new[] { "ObjectAveragePricesHistoryEvent" }, 5000, ct).ConfigureAwait(false);
        return ev is null ? null : LotMReader.PriceHistory(ev.Payload);
    }

    public async Task<(bool Ok, string Reason)> QuestStartAsync(uint pid, int questId, CancellationToken ct)
    {
        var relay = RelayForInject(); if (relay is null) return (false, "pas de relais");
        var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildQuestStartFramed(questId)),
            new[] { "QuestStartedEvent" }, 5000, ct).ConfigureAwait(false);
        return ev is null ? (false, "le serveur n'a pas démarré la quête (conditions non remplies ou pas de réponse)") : (true, "");
    }

    public async Task<(bool Ok, string Reason, long MapId)> DungeonExitAsync(uint pid, CancellationToken ct)
    {
        long here = CurrentMap(pid) ?? 0;
        if (IsInFight(pid)) return (false, "fight", here);
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return (false, "droit Récolte auto absent", here);
        int field = LotMReader.DungeonExitFieldNumber();
        if (field < 1) return (false, "champ de DungeonExitResponse absent du mapping", here);
        var relay = RelayForInject(); if (relay is null) return (false, "pas de relais", here);
        long from = CurrentMap(pid) ?? 0;
        bool sent = false;
        var ev = await SendAndWaitForCodeAsync(pid, () => sent = relay.Inject(pid, MoveForge.BuildDungeonExitFramed()),
            new[] { "DungeonExitResponse" }, 6000, ct).ConfigureAwait(false);
        if (ev is null) return (false, sent ? "pas de réponse" : "envoi impossible", CurrentMap(pid) ?? from);
        var answer = LotMReader.DungeonExit(ev.Payload, field);
        if (!answer.Success) return (false, answer.Reason, CurrentMap(pid) ?? from);
        long arrived = CurrentMap(pid) ?? 0;
        if (arrived == 0 || arrived == from) arrived = await WaitDifferentMapAsync(pid, from, 6000, ct).ConfigureAwait(false);
        var done = LotMReader.DungeonExitConfirmed(answer, from, arrived);
        return done;
    }

    /// <summary>Carte différente de <paramref name="from"/>, déjà connue ou annoncée par <see cref="MapChanged"/>.</summary>
    private async Task<long> WaitDifferentMapAsync(uint pid, long from, int timeoutMs, CancellationToken ct)
    {
        long seen = CurrentMap(pid) ?? 0;
        if (seen != 0 && seen != from) return seen;
        var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMap(uint p, long map, int? _, int? __)
        {
            if (p == pid && map != 0 && map != from) tcs.TrySetResult(map);
        }
        MapChanged += OnMap;
        try
        {
            seen = CurrentMap(pid) ?? 0;
            if (seen != 0 && seen != from) return seen;
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (done == tcs.Task) return await tcs.Task.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            seen = CurrentMap(pid) ?? 0;
            return seen != 0 && seen != from ? seen : 0;
        }
        finally { MapChanged -= OnMap; }
    }

    public bool TeleportAnswer(uint pid, bool accept)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var st = LotMOf(pid);
        if (st.Offer is not { } o) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        var pkt = MoveForge.BuildTeleportAnswerFramed(o.Offer.Kind, accept);
        if (pkt.Length == 0) return false;
        bool ok = relay.Inject(pid, pkt);
        if (ok) st.Offer = null;
        return ok;
    }

    public async Task<OfferState?> TeleportWaitOfferAsync(uint pid, int ms, CancellationToken ct)
    {
        var st = LotMOf(pid);
        if (st.Offer is { } cur) return cur;
        var tcs = new TaskCompletionSource<OfferState>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (st.Gate) st.OfferWaiters.Add(tcs);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(ms, ct)).ConfigureAwait(false);
        lock (st.Gate) st.OfferWaiters.Remove(tcs);
        return done == tcs.Task ? tcs.Task.Result : null;
    }

    /// <summary>Ouvre la boutique du PNJ (action 1 « Acheter/Vendre ») ; vrai seulement si le serveur a envoyé ExchangeNpcShopStartedEvent.</summary>
    public async Task<bool> ShopOpenAsync(uint pid, long actorId, CancellationToken ct)
    {
        LotMOf(pid).ShopOpen = false;
        return await NpcActionAsync(pid, actorId, 1, ct).ConfigureAwait(false) && LotMOf(pid).ShopOpen;
    }

    // ---------------------------------------------------------------- boutique de PNJ : achat / vente
    // Formes de requête PROUVÉES par capture passive du client officiel 3.7.11.11 (voir MoveForge.BuildNpcBuyFramed / BuildNpcSellFramed).
    // Une requête = quantité 1 (achat) ou une pile (vente) : c'est ce qui a été observé ; on ne tente pas des quantités inobservées en un seul envoi.
    private static readonly string[] _npcBuyAnswers = { "ExchangeBoughtEvent", "ExchangeErrorEvent" };
    private static readonly string[] _npcSellAnswers = { "ExchangeSoldEvent", "ExchangeErrorEvent" };
    private const int NpcAnswerMs = 4000;

    public async Task<(bool Ok, int Done, string Reason)> NpcBuyAsync(uint pid, int gid, int qty, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return (false, 0, "noperm");
        var st = LotMOf(pid);
        var item = st.ShopItems.FirstOrDefault(i => i.Gid == gid);
        if (!st.ShopOpen || item is null) return (false, 0, "notsold");
        if (Kamas(pid) >= 0 && Kamas(pid) < item.Price * qty) return (false, 0, "kamas");
        var relay = RelayForInject(); if (relay is null) return (false, 0, "refused");
        int done = 0;
        for (int i = 0; i < qty; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildNpcBuyFramed(gid, 1)), _npcBuyAnswers, NpcAnswerMs, ct).ConfigureAwait(false);
            if (ev is null) return (done > 0, done, "timeout");
            if (ev.Name != "ExchangeBoughtEvent") return (done > 0, done, "refused");
            done++;
            if (i + 1 < qty) await Task.Delay(120, ct).ConfigureAwait(false);
        }
        return (true, done, "");
    }

    public async Task<(bool Ok, int Done, string Reason)> NpcSellAsync(uint pid, int gid, int qty, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return (false, 0, "noperm");
        if (!LotMOf(pid).ShopOpen) return (false, 0, "noshop");
        // Seules les piles du SAC (position 63) : jamais l'exemplaire équipé.
        var stacks = Inventory.Items(pid).Where(x => x.Gid == gid && x.Qty > 0 && x.Position == 63).OrderBy(x => x.Qty).ToList();
        if (stacks.Sum(x => (long)x.Qty) < qty) return (false, 0, "noitem");
        var relay = RelayForInject(); if (relay is null) return (false, 0, "refused");
        int left = qty, done = 0;
        foreach (var stack in stacks)
        {
            if (left <= 0) break;
            ct.ThrowIfCancellationRequested();
            int take = Math.Min(left, stack.Qty);
            var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildNpcSellFramed(new[] { (stack.Uid, take) })), _npcSellAnswers, NpcAnswerMs, ct).ConfigureAwait(false);
            if (ev is null) return (done > 0, done, "timeout");
            if (ev.Name != "ExchangeSoldEvent") return (done > 0, done, "refused");
            done += take; left -= take;
        }
        return (left == 0, done, left == 0 ? "" : "refused");
    }

    /// <summary>Anomalies OUVERTES connues pour ce perso (son serveur), avec le temps restant en secondes (0 = inconnu).</summary>
    public IReadOnlyList<(AnomalyState State, double Remaining)> OpenAnomalies(uint pid)
    {
        var now = DateTime.UtcNow;
        return LotMOf(pid).Anomalies.Values.Where(a => a.Open)
            .Select(a => (a, AnomalyClock.RemainingSeconds(a.ClosingRaw, a.ReceivedUtc, now))).ToList();
    }

    // ---------------------------------------------------------------- accès pour LotMGame
    internal LotMState LotMStateOf(uint pid) => LotMOf(pid);
}
