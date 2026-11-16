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
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Hôtel de vente (HDV) : état d'ouverture, store, moteur achat / vente / reprice =====================
public sealed partial class LaunchService
{
    /// <summary>État HDV (mes lots en vente + prix moyen marché) par pid, alimenté par les events Exchange*/Bid*.</summary>
    public BidHouseStore Hdv { get; } = new();

    /// <summary>Instantané de prix demandés par lot, relevés pour ce PID pendant la session HDV.</summary>
    public readonly record struct HdvObservedLots(long Gid, long[] Prices, long AgeMs);

    /// <summary>
    /// Observation de marché en lecture seule pour le scan. Les prix moyens, les valeurs inconnues
    /// et les tableaux expirés ne constituent jamais des cotations exploitables.
    /// </summary>
    public async Task<HdvObservedLots?> HdvObserveLotsAsync(uint pid, long gid, int maxAgeMs,
        CancellationToken ct = default, bool forceRefresh = false)
    {
        if (gid <= 0 || gid > int.MaxValue || maxAgeMs <= 0 || !HdvAnyOpen(pid) ||
            !LicenseService.HasPermission(LicenseService.Perm.Hdv)) return null;

        long previousAge = Hdv.PriceAgeMs(pid, gid);
        if (forceRefresh || previousAge > maxAgeMs)
            await HdvGetMarketPriceAsync(pid, gid, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        long age = Hdv.PriceAgeMs(pid, gid);
        // Une revérification forcée doit réellement avoir reçu un nouveau relevé ;
        // HdvGetMarketPriceAsync peut rendre une moyenne mémorisée après timeout.
        if (!HdvAnyOpen(pid) || age < 0 || age > maxAgeMs ||
            (forceRefresh && age >= previousAge)) return null;
        var prices = Hdv.MarketPricesFor(pid, gid).ToArray();
        return prices.Any(p => p > 0) ? new HdvObservedLots(gid, prices, age) : null;
    }

    /// <summary>Levé quand l'état HDV change (ouverture vendeur/acheteur, lot ajouté/retiré, prix reçu, inventaire) -&gt; rafraîchit l'onglet HDV.</summary>
    public event Action<uint>? HdvUpdated;

    /// <summary>Levé quand un de MES lots est VENDU (event lqn) : (pid, montant net encaissé, gid, quantité). Alimente gains/stats.</summary>
    public event Action<uint, long, long, int>? HdvSold;
    /// <summary>Ventes faites hors ligne nouvellement inscrites au journal HDV : (pid, nombre de lots, total en kamas).</summary>
    public event Action<uint, int, long>? HdvOfflineSold;

    private readonly ConcurrentDictionary<uint, bool> _hdvOpen = new();
    /// <summary>Vrai si le HDV VENDEUR est ouvert (ExchangeStartedBidSellerEvent reçu, pas encore fermé par DialogLeave) : les
    /// requêtes prix/reprice ne marchent QUE dans cet état. Sinon l'onglet doit inviter à ouvrir le HDV vendeur en jeu.</summary>
    public bool HdvSellerOpen(uint pid) => _hdvOpen.TryGetValue(pid, out var v) && v;

    private readonly ConcurrentDictionary<uint, bool> _hdvAnyOpen = new();
    /// <summary>Vrai si le HDV est ouvert dans N'IMPORTE QUEL mode (acheteur kdw OU vendeur kby) -> on peut basculer en vendeur (iov action=5).</summary>
    public bool HdvAnyOpen(uint pid) => _hdvAnyOpen.TryGetValue(pid, out var v) && v;

    private readonly ConcurrentDictionary<uint, bool> _hdvBuyerOpen = new();
    /// <summary>Vrai si le HDV ACHETEUR est ouvert (ExchangeStartedBidBuyerEvent, pas basculé vendeur ni fermé) : la recherche
    /// ne renvoie les offres achetables (jzn) QUE dans ce mode -> l'achat par GID est gaté là-dessus (jamais en mode vendeur).</summary>
    public bool HdvBuyerOpen(uint pid) => _hdvBuyerOpen.TryGetValue(pid, out var v) && v;

    // Catégorie (type_id) actuellement SÉLECTIONNÉE en mode acheteur (ExchangeBidHouseTypeRequest [kdr]). Le client réel
    // n'envoie kdr QU'AU CHANGEMENT de catégorie ; RE-envoyer kdr pour une catégorie déjà active fait que le serveur ré-émet
    // kev mais AVALE la recherche kde qui suit (jzn jamais reçu -> achats suivants en timeout). On mémorise donc la catégorie
    // active et on ne renvoie kdr que lorsqu'elle change. 0 = aucune (réinitialisé à chaque (ré)ouverture / bascule / fermeture).
    private readonly ConcurrentDictionary<uint, int> _hdvBuyerType = new();
    // A second Bulk/Lua/UI read must not ClearOffer while the first buyer
    // search is awaiting its server observation on the same PID.
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _hdvBuyerSearchLocks = new();

    /// <summary>Stock HDV confirmé sac + coffre, ou sac et cache banque indicatif si la vue fusionnée n'est pas confirmée.</summary>
    public IReadOnlyList<(InventoryStore.Item Item, bool InBank)> HdvMergedItems(uint pid)
    {
        if (HdvBankInventoryReady(pid))
            return _hdvMergedInventory.Items(pid).Select(it => (it, Inventory.Get(pid, it.Uid) is null)).ToList();
        var list = new List<(InventoryStore.Item, bool)>();
        var bagUids = new HashSet<int>();
        foreach (var it in Inventory.Items(pid)) { list.Add((it, false)); bagUids.Add(it.Uid); }
        // Cache indicatif seulement : HdvSellStack refuse ces UID distants tant que le
        // serveur n'a pas publié une vue fusionnée fraîche. Ne jamais l'ajouter à cette vue.
        foreach (var it in Bank.Items(pid)) if (!bagUids.Contains(it.Uid)) list.Add((it, true));
        return list;
    }

    // Décode un champ repeated int64/int32 PACKED (varints concaténés) — ex les prix min par lot du HDV. Autonome (pas de dép).
    private static long[] ReadPackedVarints(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return System.Array.Empty<long>();
        var list = new List<long>();
        int pos = 0;
        while (pos < bytes.Length)
        {
            ulong v = 0; int shift = 0; bool ok = false;
            while (pos < bytes.Length)
            {
                byte b = bytes[pos++];
                v |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) { ok = true; break; }
                shift += 7;
                if (shift > 63) break;
            }
            if (!ok) break;
            list.Add((long)v);
        }
        return list.ToArray();
    }

    // HDV fermé (quelle que soit la façon) : plus d'achat par GID, l'onglet HDV stoppe ses fonctions.
    private void ResetHdvState(uint pid)
    {
        _hdvOpen[pid] = false; _hdvAnyOpen[pid] = false; _hdvBuyerOpen[pid] = false; _hdvBuyerType[pid] = 0;
        ClearHdvMergedInventory(pid);
        foreach (var key in _hdvSellLinks.Keys.Where(key => key.Pid == pid))
            _hdvSellLinks.TryRemove(key, out _);
        HdvUpdated?.Invoke(pid);
    }

    // ================================ HÔTEL DE VENTE (HDV) — moteur ================================
    // Forge + attente d'event, calqué sur BankDepositAllAsync + SendAndWaitForCodeAsync. Codes résolus PAR NOM via le
    // mapping (streamé en prod, confirmed.json en DEBUG) -> null si le bundle ne les porte pas encore (feature inerte,
    // comme un droit non accordé). Gaté Perm.Hdv AU POINT D'ACTION. ⚠️ Le HDV doit être OUVERT (iwo hôtelier) au bon mode.
    // Bornes de sécurité kamas côté APPELANT (prix max, quantité) — ces méthodes valident juste prix>0/qty>0/fonds.

    private string? HdvCode(string clearName) => _catalog?.CodeForName(clearName);

    // Les attentes de paquet sont réveillées AVANT les reducers. Attendre le signal
    // post-store plutôt que de considérer le seul paquet comme une ouverture effective.
    private async Task<bool> WaitForHdvSellerReadyAsync(uint pid, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Updated(uint changedPid)
        {
            if (changedPid == pid && HdvSellerOpen(pid)) ready.TrySetResult(true);
        }
        HdvUpdated += Updated;
        try
        {
            if (HdvSellerOpen(pid)) return true;
            try
            {
                return await ready.Task.WaitAsync(TimeSpan.FromMilliseconds(1500), ct).ConfigureAwait(false)
                    && HdvSellerOpen(pid);
            }
            catch (TimeoutException) { return false; }
        }
        finally { HdvUpdated -= Updated; }
    }

    private void SetHdvBuyerMode(uint pid)
    {
        _hdvOpen[pid] = false;
        ClearHdvMergedInventory(pid);
        _hdvAnyOpen[pid] = true;
        _hdvBuyerOpen[pid] = true;
        _hdvBuyerType[pid] = 0;
        HdvUpdated?.Invoke(pid);
    }

    // Bouton banque/havre-sac du client, repris de integ-f01-f16-20261025.
    // Uniquement au HDV vendeur ; attendre la publication effective du snapshot.
    public async Task<bool> SetMergedInventoryAsync(uint pid, bool bank, bool havenBag, CancellationToken ct = default)
    {
        var gate = _hdvSellLocks.GetOrAdd(pid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv) || !HdvSellerOpen(pid)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var sources = new List<int>();
        if (bank) sources.Add(2);
        if (havenBag) sources.Add(3);
        var packet = MoveForge.BuildMergedInventoryFramed(1, sources);
        if (packet.Length == 0) return false;
        bool merged = bank || havenBag;
        int before = merged ? _hdvMergedInventory.SeedVersion(pid) : Inventory.SeedVersion(pid);
        var ev = await SendAndWaitForCodeAsync(pid, () =>
            {
                if (!HdvSellerOpen(pid)) return false;
                // Un ancien snapshot ne doit pas apparaître « confirmé » pendant un rechargement.
                _hdvMergedInventory.Clear(pid);
                _hdvMergedRequested[pid] = merged;
                bool sent = relay.Inject(pid, packet);
                if (!sent) _hdvMergedRequested.TryRemove(pid, out _);
                return sent;
            },
            new[] { "InventoryContentEvent" }, 4000, ct).ConfigureAwait(false);
        long until = Environment.TickCount64 + 1500;
        int Version() => merged ? _hdvMergedInventory.SeedVersion(pid) : Inventory.SeedVersion(pid);
        while (ev is not null && Version() == before && Environment.TickCount64 < until && HdvSellerOpen(pid))
            await Task.Delay(10, ct).ConfigureAwait(false);
        return ev is not null && HdvSellerOpen(pid) && Version() != before;
        }
        finally { gate.Release(); }
    }

    /// <summary>Bascule le HDV OUVERT en mode VENDEUR : forge NpcGenericActionRequest (iov) action=5, npcId=-1 (capturé en jeu),
    /// attend ExchangeStartedBidSellerEvent (kby). PRÉREQUIS : le HDV est ouvert (le client a fait l'iwo sur le hôtelier) —
    /// à l'ouverture il est en mode ACHETEUR (kdw), d'où cette bascule pour repricer. npcMapId = la carte courante du hôtelier.</summary>
    public async Task<bool> HdvOpenSellerAsync(uint pid, long npcMapId, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return false;
        if (npcMapId <= 0 || CurrentMap(pid) != npcMapId ||
            CaptureHdvIdentitySession(pid) is not { } session) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var ev = await SendAndWaitForCodeAsync(pid,
            () => IsCurrentHdvIdentitySession(pid, session) && CurrentMap(pid) == npcMapId &&
                  relay.Inject(pid, MoveForge.BuildNpcBankOpenFramed(npcMapId, -1, 5)),
            new[] { "ExchangeBidSellerStartedEvent" }, 8000,
            p => p.Pid == pid && !p.ClientToServer && p.DecoderEpoch == session.DecoderEpoch &&
                 IsCurrentHdvIdentitySession(pid, session), ct).ConfigureAwait(false);
        bool opened = ev is not null && IsCurrentHdvIdentitySession(pid, session) &&
                      await WaitForHdvSellerReadyAsync(pid, ct).ConfigureAwait(false) &&
                      IsCurrentHdvMutationSession(pid, session);
        TransportLog.Line($"[hdv] pid {pid} : bascule mode vendeur (NpcGenericActionRequest action=5, map {npcMapId}) -> {(opened ? "OK" : "non confirmé")}");
        return opened;
    }

    /// <summary>LECTURE SEULE : demande le prix marché d'un gid (forge keh+kbz), attend l'event prix (kcq) qui remplit le
    /// store. Renvoie le prix moyen mémorisé (ou -1). Le HDV doit être ouvert.</summary>
    public async Task<long> HdvGetMarketPriceAsync(uint pid, long gid, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return -1;
        var relay = RelayForInject();
        string? search = HdvCode("ExchangeBidHouseSearchRequest");
        string? price = HdvCode("ExchangeBidHousePriceRequest");
        if (relay is null || search is null) return -1;
        await SendAndWaitForCodeAsync(pid, () =>
        {
            relay.Inject(pid, MoveForge.BuildBidHouseSearchFramed(search, (int)gid));
            if (price is not null) relay.Inject(pid, MoveForge.BuildBidHousePriceFramed(price, (int)gid));
            return true;
        }, new[] { "ExchangeBidPriceEvent" }, 6000, ct).ConfigureAwait(false);
        return Hdv.AverageFor(pid, gid);
    }

    /// <summary>Mise en vente : résultat incertain tant qu'une corrélation serveur propre à CETTE action manque.</summary>
    private readonly ConcurrentDictionary<uint, string> _hdvSellError = new();
    // Serialize seller-side operations for one PID, so a reprice or a removal
    // initiated by this engine cannot masquerade as a new sale's evidence.
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _hdvSellLocks = new();
    private readonly ConcurrentDictionary<uint, (int Uid, int Quantity, long Price)> _hdvSellNeedsReview = new();
    // A late SELL timeout must not put the next character (same recycled PID)
    // into the prior character's manual review, nor let that character clear it.
    private readonly ConcurrentDictionary<uint, string> _hdvSellUncertainProfile = new();
    // Per-session proof that a bag UID produced a specific seller listing UID.
    // Cleared on every HDV close; a matching price/GID alone never enters this table.
    private readonly ConcurrentDictionary<(uint Pid, int BagUid), int> _hdvSellLinks = new();

    public HdvSellDisposition? HdvSellLastDisposition(uint pid)
    {
        string profile = HdvCurrentMutationProfile(pid);
        bool owned = profile.Length > 0 &&
                     _hdvSellUncertainProfile.TryGetValue(pid, out var original) && original == profile;
        return (owned && _hdvSellNeedsReview.ContainsKey(pid)) ||
               (profile.Length > 0 && (_hdvDirectMutations?.NeedsReview(profile, "sell:") ?? true))
            ? HdvSellDisposition.NeedsReview
            : _hdvSellError.ContainsKey(pid) &&
              (!_hdvSellNeedsReview.ContainsKey(pid) || owned) ? HdvSellDisposition.Refused : null;
    }

    /// <summary>Libérer le verrou seulement APRÈS vérification humaine dans l'interface officielle
    /// (inventaire ET annonces), jamais à la réception d'un simple événement ni par un timer.</summary>
    public bool HdvSellManuallyReconciled(uint pid)
    {
        // The durable marker is independent of the transient PID. Resolve
        // only the currently observed original character's SELL order.
        string profile = HdvCurrentMutationProfile(pid);
        bool durablePending = profile.Length > 0 &&
                              (_hdvDirectMutations?.NeedsReview(profile, "sell:") ?? true);
        if (durablePending && _hdvDirectMutations?.TryReview(profile, "sell:") != true)
            return false; // corrupt/unavailable journal, active send or wrong order type
        if (!durablePending && profile.Length > 0 && _hdvDirectMutations?.NeedsReview(profile) != false)
            return false; // another kind is still pending: do not clear a PID-local stale latch
        if (HdvCurrentMutationProfile(pid) != profile) return false;
        bool hadMemory = _hdvSellUncertainProfile.TryGetValue(pid, out var original) &&
                         original == profile && _hdvSellNeedsReview.TryRemove(pid, out _);
        if (!hadMemory && !durablePending) return false;
        if (hadMemory) _hdvSellUncertainProfile.TryRemove(pid, out _);
        _hdvSellError.TryRemove(pid, out _);
        TransportLog.Line($"[hdv] pid {pid} : rapprochement opérateur déclaré ; aucune preuve serveur automatique.");
        try
        {
            var dir = MZP19LocalClone.DataRoot;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "hdv-sell-review.log"),
                $"{DateTime.UtcNow:O}\tpid={pid}\tRAPPROCHEMENT_MANUEL_DECLARE{Environment.NewLine}");
        }
        catch { /* journal transport conserve le constat */ }
        return true;
    }

    private bool HdvSellNeedsManualReview(uint pid, int uid, int qty, long price,
        string? observedEvent, string originalProfile)
    {
        _hdvSellUncertainProfile[pid] = originalProfile;
        _hdvSellNeedsReview[pid] = (uid, qty, price);
        const string why = "À vérifier dans le jeu : réponse serveur non corrélable à cette mise en vente. " +
                           "Contrôler annonces et inventaire puis confirmer le rapprochement avant toute nouvelle vente.";
        _hdvSellError[pid] = why;
        TransportLog.Line($"[hdv] pid {pid} : vente À VÉRIFIER uid={uid} x{qty} @ {price} event={observedEvent ?? "timeout"}; pas de retry.");
        try
        {
            var dir = MZP19LocalClone.DataRoot;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "hdv-sell-review.log"),
                $"{DateTime.UtcNow:O}\tpid={pid}\tuid-sac={uid}\tqty={qty}\tprice={price}\tevent={observedEvent ?? "timeout"}\tA_VERIFIER{Environment.NewLine}");
        }
        catch { /* journal transport reste accessible même si disque indisponible */ }
        return false;
    }

    /// <summary>Motif lisible du DERNIER échec de <see cref="HdvSellAsync"/> pour ce bot (texte du jeu si le serveur en a
    /// envoyé un), ou null après une réussite.</summary>
    public string? HdvLastSellError(uint pid) => _hdvSellError.TryGetValue(pid, out var s) ? s : null;

    public void HdvResetSellError(uint pid) => _hdvSellError.TryRemove(pid, out _);

    private bool HdvSellFail(uint pid, string reason)
    {
        _hdvSellError[pid] = reason;
        return false;
    }

    public async Task<bool> HdvSellAsync(uint pid, int objectUid, int quantity, long price, CancellationToken ct = default)
    {
        var gate = _hdvSellLocks.GetOrAdd(pid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        if (_hdvSellNeedsReview.ContainsKey(pid))
            return HdvSellFail(pid, "À vérifier : une précédente mise en vente est indécidable. Contrôle manuel obligatoire avant de réessayer.");
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return HdvSellFail(pid, Loc.T("L.Bots.Hdv.Why.NoRight"));
        if (price <= 0 || quantity <= 0) return HdvSellFail(pid, Loc.T("L.Bots.Hdv.Why.BadLot"));
        if (!HdvSellerOpen(pid)) { TransportLog.Line($"[hdv] pid {pid} : mise en vente ignorée (HDV vendeur fermé)."); return HdvSellFail(pid, Loc.T("L.Bots.Hdv.Why.NotSeller")); }
        if (CaptureHdvMutationSession(pid) is not { } session)
            return HdvSellFail(pid, "Vente bloquée : personnage, serveur ou session de décodage non confirmés.");
        string directProfile = HdvMutationProfile(session);
        if (_hdvDirectMutations?.NeedsReview(directProfile) != false)
            return HdvSellFail(pid, "Vente bloquée : ordre HDV durable indécidable ou journal inaccessible. Rapprochement requis.");
        var stack = HdvSellStack(pid, objectUid);
        if (stack is not { Gid: > 0, Position: 63 } || stack.Value.Qty < quantity)
            return HdvSellFail(pid, "UID du sac absent ou quantité non vérifiée dans l'inventaire serveur.");
        if (!ItemCanTrade(pid, objectUid)) return HdvSellFail(pid, "Objet invendable : " + GivreClone.Engine.Recovery.ItemBindingRules.Label(ItemBindingOf(pid, objectUid)) + ".");
        if (HdvSalePriceRefusal(pid, stack.Value.Gid, quantity, price) is { } refusal)
            return HdvSellFail(pid, refusal);
        var relay = RelayForInject();
        string? code = HdvCode("ExchangeObjectMovePricedRequest");
        if (relay is null || code is null) { TransportLog.Line($"[hdv] pid {pid} : mise en vente impossible (ExchangeObjectMovePricedRequest non mappé)."); return HdvSellFail(pid, Loc.T("L.Bots.Hdv.Why.NoLink")); }
        // Link the bag UID to the new listing only if BOTH the matching inventory debit
        // and a unique new seller listing are seen after this request.
        int[] originalListingUids = Hdv.MyListings(pid)
            .Select(x => x.Uid).OrderBy(x => x).ToArray();
        var correlation = new HdvSellCorrelation(objectUid, stack.Value.Gid, stack.Value.Qty,
            quantity, price, originalListingUids, FieldOf);
        MarkHdvAuto(pid);   // HDV Tracker : action du moteur (auto, sauf clic unitaire marqué manuel)
        DecodedPacket? ev;
        bool mayHaveSent = false;
        bool attempted = false;
        HdvDirectMutationLease directLease = default;
        string? admissionError = null;
        long textSeq = LastTextInfo(pid)?.Seq ?? 0;
        try
        {
            ev = await SendAndWaitForCodeAsync(pid, () =>
            {
                // The inventory and price can change while waiting for the
                // per-PID dispatch window. Re-check at the last actual send
                // border, not merely at UI preview. No local lease is an ACK.
                if (!IsCurrentHdvMutationSession(pid, session) ||
                    HdvSellStack(pid, objectUid) is not { Gid: > 0, Position: 63 } current ||
                    // The pre-await snapshot owns both the exact durable
                    // sell-key stock count and HdvSellCorrelation baseline.
                    // A changed count is NOT the same order even when enough
                    // units remain. Refuse without reserving/sending; require
                    // another explicit UI/production preflight instead.
                    current.Gid != stack.Value.Gid ||
                    current.Qty != stack.Value.Qty ||
                    current.Qty < quantity ||
                    // The original listing baseline is part of the local
                    // correlation witness. Another manual/Bulk order before
                    // this dispatch invalidates that baseline and can make
                    // the next listing appear to belong to the wrong sender.
                    !Hdv.MyListings(pid).Select(x => x.Uid).OrderBy(x => x)
                        .SequenceEqual(originalListingUids) ||
                    !ItemCanTrade(pid, objectUid) ||
                    HdvSalePriceRefusal(pid, current.Gid, quantity, price) is not null)
                    return false;
                byte[] wire = MoveForge.BuildBidHouseSellFramed(code, objectUid, quantity, price);
                if (_hdvDirectMutations is null ||
                    _hdvDirectMutations.TryBegin(directProfile, pid,
                        HdvTaxOrderKeys.Sell(objectUid, quantity, price, stack.Value.Qty),
                        out directLease) != HdvDirectMutationDecision.Allowed)
                {
                    admissionError = "Vente refusée : journal d'intention durable indisponible, déjà utilisé ou en quarantaine.";
                    return false;
                }
                attempted = true; // a failed attempt fsync leaves Started quarantined
                if (!_hdvDirectMutations.TryMarkAttempt(directLease)) return false;
                mayHaveSent = relay.Inject(pid, wire);
                TransportLog.Line(HdvUidDiagnostic.SaleRequest(pid, objectUid, quantity, mayHaveSent));
                return mayHaveSent;
            }, new[] { "ExchangeBidHouseItemAddedEvent", "ObjectQuantityEvent", "ObjectsQuantityEvent",
                       "ObjectDeletedEvent", "ObjectsDeletedEvent", "ExchangeErrorEvent", "ObjectErrorEvent" }, 6000,
                p =>
                {
                    if (p.Pid != pid || p.ClientToServer ||
                        p.DecoderEpoch != session.DecoderEpoch ||
                        !IsCurrentHdvMutationSession(pid, session)) return false;
                    if (p.Name == "ExchangeBidHouseItemAddedEvent")
                    {
                        int? listingUid = HdvUidDiagnostic.ListingUid(p.Payload, FieldOf);
                        TransportLog.Line(HdvUidDiagnostic.SaleListing(pid, listingUid));
                    }
                    return correlation.Observe(p.Name, p.Payload) != HdvSellDisposition.NeedsReview;
                },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (attempted)
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            return HdvSellNeedsManualReview(pid, objectUid, quantity, price, "annulation-après-envoi-possible", directProfile);
        }
        catch (Exception ex) when (attempted)
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            return HdvSellNeedsManualReview(pid, objectUid, quantity, price, "erreur-transport:" + ex.GetType().Name, directProfile);
        }

        if (attempted && !IsCurrentHdvMutationSession(pid, session))
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            return HdvSellNeedsManualReview(pid, objectUid, quantity, price,
                "identité-serveur-personnage-ou-epoch-modifiée", directProfile);
        }
        var disposition = ev is null ? HdvSellDisposition.NeedsReview :
            correlation.Observe(ev.Name, ev.Payload);
        TransportLog.Line(HdvUidDiagnostic.SaleResult(pid, objectUid, disposition, correlation.SellId, ev?.Name));
        if (disposition == HdvSellDisposition.Refused && !attempted)
            return HdvSellFail(pid, HdvSellRefusal(pid, ev, textSeq));
        if (disposition != HdvSellDisposition.Confirmed || correlation.SellId is not > 0)
        {
            if (attempted)
            {
                _hdvDirectMutations?.TryFinish(directLease, false);
                return HdvSellNeedsManualReview(pid, objectUid, quantity, price, ev?.Name, directProfile);
            }
            return HdvSellFail(pid, admissionError ?? "Mise en vente non envoyée : une autre attente utilise ce PID.");
        }
        if (!HdvSellerOpen(pid))
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            return HdvSellNeedsManualReview(pid, objectUid, quantity, price, "HDV fermé avant la résolution", directProfile);
        }
        if (!_hdvDirectMutations!.TryFinish(directLease, true))
            return HdvSellNeedsManualReview(pid, objectUid, quantity, price,
                "confirmation serveur observée mais journal durable non validé", directProfile);
        _hdvSellLinks[(pid, objectUid)] = correlation.SellId.Value;
        _hdvSellError.TryRemove(pid, out _);
        TransportLog.Line($"[hdv] pid {pid} : UID sac {objectUid} -> sell_id {correlation.SellId.Value} corrélé par débit inventaire + nouvelle annonce.");
        return true;
        }
        finally { gate.Release(); }
    }

    // Motif d'un refus de mise en vente : 1) message système d'erreur reçu depuis l'envoi (texte du jeu), 2) erreur d'échange
    // ou d'objet (numéro brut, le jeu ne livre pas de texte pour ces codes), 3) aucune réponse dans le délai.
    private string HdvSellRefusal(uint pid, DecodedPacket? ev, long textSeqBefore)
    {
        string? text = null;
        if (LastTextInfo(pid) is { } ti && ti.Seq > textSeqBefore && ti.MessageType != 0)
        {
            text = GameNames.InfoMessageText(ti.MessageType, ti.MessageId, ti.Params);
            if (string.IsNullOrWhiteSpace(text)) text = Loc.Fmt("L.Bots.Hdv.Why.GameMsgFmt", ti.MessageType, ti.MessageId);
        }
        // Numéro d'erreur lu UNIQUEMENT via le mapping streamé (champ inconnu -> « ? », jamais un numéro deviné).
        string ErrNum(string msg, string field) => FieldOf(msg, field) is int n ? PayloadReader.Int(ev!.Payload, n, 0).ToString() : "?";
        string? code = ev?.Name switch
        {
            "ExchangeErrorEvent" => Loc.Fmt("L.Bots.Hdv.Why.ExchangeErrFmt", ErrNum("ExchangeErrorEvent", "error_type")),
            "ObjectErrorEvent" => Loc.Fmt("L.Bots.Hdv.Why.ObjectErrFmt", ErrNum("ObjectErrorEvent", "reason")),
            _ => null,
        };
        if (text is not null) return code is null ? text : text + " (" + code + ")";
        return code ?? Loc.T("L.Bots.Hdv.Why.Timeout");
    }

    /// <summary>ACHÈTE un lot listé (forge kbm : uid du lot), attend le résultat (kcx). Vérifie les fonds (kamas>=price).</summary>
    // Un résultat sans preuve est indécidable : on fige tout nouvel achat de cette session après
    // un timeout. L'utilisateur doit réconcilier l'état du serveur avant de réessayer.
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _hdvBuyLocks = new();
    private readonly ConcurrentDictionary<uint, (int Uid, int Quantity, long Price, long Generation)> _hdvUncertainBuy = new();
    private readonly ConcurrentDictionary<uint, long> _hdvBuyGenerations = new();
    private readonly ConcurrentDictionary<uint, string> _hdvBuyUncertainProfile = new();

    /// <summary>The original server/character's pending buy, including after restart or PID reuse.</summary>
    public bool HdvBuyReviewPending(uint pid)
    {
        string profile = HdvCurrentMutationProfile(pid);
        return profile.Length > 0 &&
               ((_hdvDirectMutations?.NeedsReview(profile, "buy:") ?? true) ||
                HdvBuyUncertainForProfile(pid, profile));
    }

    private bool HdvBuyUncertainForProfile(uint pid, string profile)
        => _hdvUncertainBuy.ContainsKey(pid) &&
           _hdvBuyUncertainProfile.TryGetValue(pid, out var original) && original == profile;

    /// <summary>Only an explicit human reconciliation of this character's buy can release its local quarantine.</summary>
    public bool HdvAcknowledgeBuyReview(uint pid)
    {
        string profile = HdvCurrentMutationProfile(pid);
        if (profile.Length == 0) return false;
        bool durable = _hdvDirectMutations?.NeedsReview(profile, "buy:") ?? true;
        if (durable && _hdvDirectMutations?.TryReview(profile, "buy:") != true) return false;
        if (!durable && _hdvDirectMutations?.NeedsReview(profile) != false) return false;
        bool pending = HdvBuyUncertainForProfile(pid, profile);
        if (pending)
        {
            _hdvUncertainBuy.TryRemove(pid, out _);
            _hdvBuyUncertainProfile.TryRemove(pid, out _);
        }
        if (durable || pending)
        {
            TransportLog.Line($"[hdv] pid {pid} : rapprochement achat déclaré par l'opérateur ; aucune confirmation serveur inférée.");
            HdvUpdated?.Invoke(pid);
        }
        return durable || pending;
    }

    private bool TryObservedBuyQuote(uint pid, int uid, int quantity, long price, out long gid)
    {
        gid = 0;
        if (!HdvBuyerOpen(pid) || quantity is not (1 or 10 or 100 or 1000) || price <= 0) return false;
        gid = Hdv.GidForOfferUid(pid, uid);
        if (gid <= 0) return false;
        // An out parameter cannot be captured in a LINQ predicate. Pin the
        // server-observed value to a local before checking offer variants.
        long observedGid = gid;
        int slot = BidHouseStore.SlotOf(quantity);
        var variants = Hdv.BuyerOffers(pid, observedGid);
        if (variants.Count > 0)
            return variants.Any(x => x.Uid == uid && x.Gid == observedGid &&
                                     x.Prices.Length > slot && x.Prices[slot] == price);
        return Hdv.OfferFor(pid, gid) is { } offer && offer.Uid == uid &&
               offer.Prices.Length > slot && offer.Prices[slot] == price;
    }

    public async Task<bool> HdvBuyAsync(uint pid, int uid, int quantity, long maxPrice, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return false;
        if (maxPrice <= 0 || quantity <= 0 || uid <= 0) return false;
        long kamas = Kamas(pid);
        if (kamas >= 0 && kamas < maxPrice) { TransportLog.Line($"[hdv] pid {pid} : achat refusé (fonds {kamas} < {maxPrice})."); return false; }
        var relay = RelayForInject();
        string? code = HdvCode("ExchangeBidHouseBuyRequest");
        if (relay is null || code is null) { TransportLog.Line($"[hdv] pid {pid} : achat impossible (ExchangeBidHouseBuyRequest non mappé)."); return false; }
        var gate = _hdvBuyLocks.GetOrAdd(pid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return false;
            kamas = Kamas(pid);
            if (kamas >= 0 && kamas < maxPrice) return false;
            if (CaptureHdvMutationSession(pid, buyer: true) is not { } session ||
                !TryObservedBuyQuote(pid, uid, quantity, maxPrice, out long gid)) return false;
            string directProfile = HdvMutationProfile(session);
            if (_hdvDirectMutations?.NeedsReview(directProfile) != false || HdvBuyReviewPending(pid))
            {
                TransportLog.Line($"[hdv] pid {pid} : achat bloqué, journal durable indécidable ou en quarantaine.");
                return false;
            }
            // The fiscal smith budget can settle later, but it never clears an
            // already ambiguous direct buy. Only explicit review does that.
            if (_hdvUncertainBuy.TryGetValue(pid, out var pending) &&
                _hdvBuyUncertainProfile.TryGetValue(pid, out var original) && original == directProfile)
            {
                TransportLog.Line($"[hdv] pid {pid} : nouvel achat bloqué, achat précédent uid={pending.Uid} x{pending.Quantity} @ {pending.Price} génération={pending.Generation} INCERTAIN : reconciliation serveur nécessaire.");
                return false;
            }
            long generation = _hdvBuyGenerations.AddOrUpdate(pid, 1, (_, current) => current + 1);
            bool injected = false;
            bool attempted = false;
            HdvDirectMutationLease directLease = default;
            SmithSpendingOperation? smithSpend = null;
            void MarkUncertain()
            {
                _hdvUncertainBuy[pid] = (uid, quantity, maxPrice, generation);
                _hdvBuyUncertainProfile[pid] = directProfile;
                if (smithSpend is not null) _hdvUncertainSpend[pid] = smithSpend;
                HdvUpdated?.Invoke(pid);
            }
            DecodedPacket? ev;
            try
            {
                ev = await SendAndWaitForCodeAsync(pid, () =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (!LicenseService.HasPermission(LicenseService.Perm.Hdv) ||
                        !IsCurrentHdvMutationSession(pid, session, buyer: true) ||
                        !TryObservedBuyQuote(pid, uid, quantity, maxPrice, out long currentGid) ||
                        currentGid != gid) return false;
                    long availableKamas = Kamas(pid);
                    if (availableKamas >= 0 && availableKamas < maxPrice) return false;
                    // Enter the journal INSIDE the fiscal broker's authorized
                    // injection callback: a broker refusal leaves no fake send.
                    injected = SendSmithSpend(pid, "purchase", maxPrice, uid, quantity,
                        InSmithSpending ? gid : 0, 0,
                        () =>
                        {
                            if (!IsCurrentHdvMutationSession(pid, session, buyer: true) ||
                                !TryObservedBuyQuote(pid, uid, quantity, maxPrice, out long lastGid) ||
                                lastGid != gid ||
                                _hdvDirectMutations?.TryBegin(directProfile, pid,
                                    HdvDirectMutationJournal.BuyOrder(uid, gid, quantity, maxPrice, session.DecoderEpoch),
                                    out directLease) != HdvDirectMutationDecision.Allowed)
                                return false;
                            attempted = true; // a failed attempt fsync still quarantines Started
                            if (_hdvDirectMutations?.TryMarkAttempt(directLease) != true) return false;
                            return relay.Inject(pid, MoveForge.BuildBidHouseBuyFramed(code, uid, quantity, maxPrice));
                        }, out smithSpend);
                    if (injected)
                    {
                        MarkHdvAuto(pid);
                        HdvTrackBuyRequest(pid, uid, quantity, maxPrice);
                    }
                    return injected;
                }, new[] { "ExchangeBidHouseBuyResultEvent" }, 12000,
                    p => p.Pid == pid && !p.ClientToServer &&
                         p.DecoderEpoch == session.DecoderEpoch &&
                         IsCurrentHdvMutationSession(pid, session, buyer: true) &&
                         MZP26HdvBuyReceipt.Classify(p.Name, p.Payload, uid, FieldOf) != HdvBuyReceiptOutcome.Unrelated,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (attempted)
            {
                _hdvDirectMutations?.TryFinish(directLease, false);
                MarkUncertain();
                throw;
            }
            catch (Exception) when (attempted)
            {
                _hdvDirectMutations?.TryFinish(directLease, false);
                MarkUncertain();
                throw;
            }
            if (!attempted) return false;
            if (!injected || !IsCurrentHdvMutationSession(pid, session, buyer: true) || ev is null)
            {
                _hdvDirectMutations?.TryFinish(directLease, false);
                MarkUncertain();
                TransportLog.Line($"[hdv] pid {pid} : achat uid={uid} x{quantity} @ {maxPrice} génération={generation} INCERTAIN (transport/session/timeout), aucune répétition autorisée.");
                return false;
            }
            var result = MZP26HdvBuyReceipt.Classify(ev.Name, ev.Payload, uid, FieldOf);
            TransportLog.Line($"[hdv] pid {pid} : achat uid={uid} x{quantity} @ {maxPrice} génération={generation} -> {result}.");
            if (result != HdvBuyReceiptOutcome.Confirmed)
            {
                _hdvDirectMutations?.TryFinish(directLease, false);
                MarkUncertain(); // even 'bought=false' has no request correlation beyond UID
                return false;
            }
            bool exactWallet = false;
            if (smithSpend is not null)
            {
                try
                {
                    exactWallet = await smithSpend.Completion.WaitAsync(TimeSpan.FromSeconds(6), ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _hdvDirectMutations?.TryFinish(directLease, false);
                    MarkUncertain();
                    return false;
                }
                catch (OperationCanceledException)
                {
                    _hdvDirectMutations?.TryFinish(directLease, false);
                    MarkUncertain();
                    throw;
                }
                catch (Exception)
                {
                    _hdvDirectMutations?.TryFinish(directLease, false);
                    MarkUncertain();
                    throw;
                }
                if (!exactWallet)
                {
                    _hdvDirectMutations?.TryFinish(directLease, false);
                    MarkUncertain();
                    return false;
                }
            }
            // BuyResultEvent echoes only object_uid+bought. It does NOT echo
            // quantity/price; outside the smith path's exact wallet debit this
            // is an accepted buy but NOT enough to certify the complete order.
            bool proven = exactWallet && IsCurrentHdvMutationSession(pid, session, buyer: true);
            bool journalFinished = _hdvDirectMutations?.TryFinish(directLease, proven) == true;
            if (!journalFinished || !proven)
            {
                MarkUncertain();
                TransportLog.Line($"[hdv] pid {pid} : achat uid={uid} accusé, mais quantité/prix/débit exact non corrélés ; batch arrêté et rapprochement manuel requis.");
                return false;
            }
            NoteHdvAction(pid, "buy");
            return true;
        }
        finally { gate.Release(); }
    }

    /// <summary>Bascule le HDV OUVERT en mode ACHETEUR : forge NpcGenericActionRequest action=6 (valeur capturée en jeu ;
    /// 5=vendeur), npcId=-1 ; le CODE du message est résolu PAR NOM dans le forge. Attend ExchangeStartedBidBuyerEvent.
    /// Le mode acheteur est requis pour que la recherche renvoie les offres achetables (jzn) et pour acheter.</summary>
    public async Task<bool> HdvOpenBuyerAsync(uint pid, long npcMapId, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return false;
        if (npcMapId <= 0 || CurrentMap(pid) != npcMapId ||
            CaptureHdvIdentitySession(pid) is not { } session) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var ev = await SendAndWaitForCodeAsync(pid,
            () => IsCurrentHdvIdentitySession(pid, session) && CurrentMap(pid) == npcMapId &&
                  relay.Inject(pid, MoveForge.BuildNpcBankOpenFramed(npcMapId, -1, 6)),
            new[] { "ExchangeBidBuyerStartedEvent" }, 8000,
            p => p.Pid == pid && !p.ClientToServer && p.DecoderEpoch == session.DecoderEpoch &&
                 IsCurrentHdvIdentitySession(pid, session), ct).ConfigureAwait(false);
        // The generic waiter may complete before OnDecoded publishes the
        // buyer-mode flag. Never treat the event alone as ready to purchase.
        long deadline = Environment.TickCount64 + 1500;
        while (ev is not null && IsCurrentHdvIdentitySession(pid, session) &&
               !HdvBuyerOpen(pid) && Environment.TickCount64 < deadline)
            await Task.Delay(10, ct).ConfigureAwait(false);
        bool ready = ev is not null && IsCurrentHdvMutationSession(pid, session, buyer: true);
        TransportLog.Line($"[hdv] pid {pid} : bascule mode acheteur (action=6, map {npcMapId}) -> {(ready ? "OK" : "non confirmé")}");
        return ready;
    }

    /// <summary>Recherche FRAÎCHE d'un gid en mode ACHETEUR pour peupler l'offre jzn (uid du lot + prix par lot). Séquence
    /// CALQUÉE sur le client réel (capture achat manuel) : (kdr sélection catégorie si 1re fois/changement) PUIS kde
    /// unfollow/refollow=1 -> le serveur renvoie ExchangeTypesItemsExchangerDescriptionForUserEvent (jzn) peuplé. Renvoie
    /// true si une offre fraîche (uid>0) est mémorisée. Partagé par <see cref="HdvBuyByGidAsync"/> et <see cref="HdvGetLotPriceAsync"/>.
    /// PRÉREQUIS : HDV en mode ACHETEUR (la recherche ne renvoie les offres achetables qu'en mode achat). 100% PAR NOM.</summary>
    private async Task<bool> HdvEnsureBuyOfferAsync(uint pid, long gid, CancellationToken ct)
    {
        if (gid <= 0 || gid > int.MaxValue) return false;
        var searchGate = _hdvBuyerSearchLocks.GetOrAdd(pid, static _ => new SemaphoreSlim(1, 1));
        await searchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        if (CaptureHdvMutationSession(pid, buyer: true) is not { } session) return false;
        var relay = RelayForInject();
        string? search = HdvCode("ExchangeBidHouseSearchRequest");
        if (relay is null || search is null) { TransportLog.Line($"[hdv] pid {pid} : recherche gid={gid} impossible (code non mappé)."); return false; }
        // type_id résolu PAR NOM depuis le DB objets (ItemTypeId) ; kdr envoyé seulement si son code est mappé.
        string? typeCode = HdvCode("ExchangeBidHouseTypeRequest");
        int typeId = ItemTypeId(gid);
        // SÉCURITÉ : on invalide d'abord l'offre mémorisée -> interdit d'agir sur un uid/prix PÉRIMÉ si le jzn frais ne vient pas.
        Hdv.ClearOffer(pid, gid);
        // • 1er lot / CHANGEMENT de catégorie : kdr (catégorie) PUIS kde follow=1 (kdr remet l'abonnement à zéro -> 1 follow suffit).
        // • même catégorie déjà active : kde UNFOLLOW (follow=0) PUIS REFOLLOW (follow=1) — c'est la transition 0->1 qui force
        //   la ré-émission de la liste (un follow=1 seul alors qu'on suit déjà = aucun jzn). Re-kdr sur catégorie active AVALE
        //   la recherche -> on ne le renvoie qu'au changement de catégorie. cf _hdvBuyerType.
        bool sendType = typeCode is not null && typeId > 0 && _hdvBuyerType.GetValueOrDefault(pid) != typeId;
        if (sendType)
        {
            var category = await SendAndWaitForCodeAsync(pid,
                () => IsCurrentHdvMutationSession(pid, session, buyer: true) &&
                      relay.Inject(pid, MoveForge.BuildBidHouseTypeFramed(typeId)),
                new[] { "ExchangeTypesExchangerDescriptionForUserEvent" }, 6000,
                p => p.Pid == pid && !p.ClientToServer && p.DecoderEpoch == session.DecoderEpoch &&
                     IsCurrentHdvMutationSession(pid, session, buyer: true), ct).ConfigureAwait(false);
            if (category is null || !IsCurrentHdvMutationSession(pid, session, buyer: true)) return false;
            // Category selection succeeds even when the requested item has no offers.
            // Re-selecting an already active category can swallow the next follow.
            _hdvBuyerType[pid] = typeId;
        }
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOffers(uint source)
        {
            if (source == pid && IsCurrentHdvMutationSession(pid, session, buyer: true) &&
                Hdv.OfferFor(pid, gid) is { Uid: > 0 }) published.TrySetResult();
        }
        // The packet waiter completes before OnDecoded stores its variants. Subscribe
        // to publication before sending, so both async callers and Lua see fresh rows.
        HdvUpdated += OnOffers;
        try
        {
            long jznDeadline = Environment.TickCount64 + 6000;
            var jzn = await SendAndWaitForCodeAsync(pid, () =>
            {
                if (!IsCurrentHdvMutationSession(pid, session, buyer: true)) return false;
                if (!sendType) relay.Inject(pid, MoveForge.BuildBidHouseSearchFramed(search, (int)gid, follow: false)); // kde : UNFOLLOW (coupe l'abonnement en cours)
                return relay.Inject(pid, MoveForge.BuildBidHouseSearchFramed(search, (int)gid, follow: true));          // kde : (RE)FOLLOW=1 + gid -> jzn peuplé
            },
            new[] { "ExchangeTypesItemsExchangerDescriptionForUserEvent" }, 6000,
            p => p.Pid == pid && !p.ClientToServer && p.DecoderEpoch == session.DecoderEpoch &&
                 IsCurrentHdvMutationSession(pid, session, buyer: true), ct).ConfigureAwait(false);
            int left = (int)Math.Max(0, jznDeadline - Environment.TickCount64);
            if (!published.Task.IsCompleted && left > 0)
            {
                try { await published.Task.WaitAsync(TimeSpan.FromMilliseconds(left), ct).ConfigureAwait(false); }
                catch (TimeoutException) { /* No fresh matching offers were published. */ }
            }
        if (jzn is null || !IsCurrentHdvMutationSession(pid, session, buyer: true) ||
            Hdv.OfferFor(pid, gid) is not { Uid: > 0 })
        {
            TransportLog.Line(jzn is null
                ? $"[hdv] pid {pid} : gid={gid} : pas de réponse à la recherche (délai dépassé)."
                : $"[hdv] pid {pid} : gid={gid} : aucune offre pour ce gid (réponse sans l'objet).");
            return false;
        }
            return true;
        }
        finally { HdvUpdated -= OnOffers; }
        }
        finally { searchGate.Release(); }
    }

    /// <summary>One fresh buyer search supplies every quoted lot size. Never reuses an old offer after a timeout.</summary>
    public async Task<long[]> HdvGetBuyerLotPricesAsync(uint pid, long gid, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv) || !HdvBuyerOpen(pid)
            || !await HdvEnsureBuyOfferAsync(pid, gid, ct).ConfigureAwait(false)) return Array.Empty<long>();
        return Hdv.OfferFor(pid, gid) is { } offer ? offer.Prices.ToArray() : Array.Empty<long>();
    }

    /// <summary>Prix d'un LOT précis d'un gid (LECTURE SEULE, API Lua sale:GetPriceItem). En mode ACHETEUR : le prix d'ACHAT
    /// du lot lu de l'offre jzn (recherche fraîche) — le seul chemin qui porte les prix PAR LOT en mode achat (l'event vendeur
    /// jzs n'y renvoie que la moyenne). Sinon (mode vendeur/défaut) : le prix marché MIN du lot (jzs). Renvoie -1 si inconnu.</summary>
    public async Task<long> HdvGetLotPriceAsync(uint pid, long gid, int quantity, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return -1;
        int slot = BidHouseStore.SlotOf(quantity);
        if (HdvBuyerOpen(pid))   // en mode ACHAT : prix par lot depuis l'offre achetable (jzn), pas la moyenne vendeur
        {
            if (!await HdvEnsureBuyOfferAsync(pid, gid, ct).ConfigureAwait(false)) return -1;
            return Hdv.OfferPriceFor(pid, gid, slot);
        }
        await HdvGetMarketPriceAsync(pid, gid, ct).ConfigureAwait(false);   // mode vendeur/défaut : prix marché min par lot
        return Hdv.MarketFor(pid, gid, slot);
    }

    /// <summary>ACHÈTE par GID (API Lua sale:buyItem). En mode ACHETEUR ouvert : recherche fraîche du gid (jzn = uid du lot +
    /// prix par lot), lit l'uid et le prix du lot demandé, vérifie le plafond, puis <see cref="HdvBuyAsync"/>. 100% PAR NOM.</summary>
    /// <summary>Lua contract (sale:buyItem): maxPrice 0 means no ceiling; otherwise a lot priced above it is refused, the exact price is allowed.</summary>
    public static bool ExceedsBuyCeiling(long lotPrice, long maxPrice) => maxPrice > 0 && lotPrice > maxPrice;

    public async Task<bool> HdvBuyByGidAsync(uint pid, long gid, int quantity, long maxPrice, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return false;
        if (quantity <= 0) return false;
        // Gaté MODE ACHETEUR (pas juste "HDV ouvert") : la recherche ne renvoie les offres achetables (jzn) qu'en mode achat.
        if (!HdvBuyerOpen(pid)) { TransportLog.Line($"[hdv] pid {pid} : achat gid={gid} ignoré (HDV pas en mode ACHAT — ouvre-le via goToHdvBuy)."); return false; }
        // 1) recherche fraîche EXIGÉE (jzn + offre uid>0). Interdit d'acheter sur un uid/prix périmé.
        if (!await HdvEnsureBuyOfferAsync(pid, gid, ct).ConfigureAwait(false)) return false;
        if (Hdv.OfferFor(pid, gid) is not { } offer || offer.Uid == 0) return false;
        // 2) prix du lot demandé (slot 1/10/100/1000) depuis l'offre jzn.
        int slot = BidHouseStore.SlotOf(quantity);
        long price = Hdv.OfferPriceFor(pid, gid, slot);
        if (price <= 0) { TransportLog.Line($"[hdv] pid {pid} : achat gid={gid} x{quantity} : prix du lot inconnu (l'offre ne cote pas ce lot)."); return false; }
        if (ExceedsBuyCeiling(price, maxPrice)) { TransportLog.Line($"[hdv] pid {pid} : achat gid={gid} refusé (prix {price} > plafond {maxPrice})."); return false; }
        // 3) achat du lot (uid + prix résolus) : HdvBuyAsync forge kei {price,quantity,uid}, attend le résultat, garde fonds.
        return await HdvBuyAsync(pid, offer.Uid, quantity, price, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BidHouseStore.BuyerOffer>> HdvGetBuyerOffersAsync(uint pid, long gid, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv) || !HdvBuyerOpen(pid)) return Array.Empty<BidHouseStore.BuyerOffer>();
        if (!await HdvEnsureBuyOfferAsync(pid, gid, ct).ConfigureAwait(false)) return Array.Empty<BidHouseStore.BuyerOffer>();
        return Hdv.BuyerOffers(pid, gid);
    }

    public async Task<bool> HdvBuySelectedOfferAsync(uint pid, long gid, int uid, int quantity, long expectedPrice, CancellationToken ct)
    {
        if (expectedPrice <= 0 || quantity is not (1 or 10 or 100 or 1000)) return false;
        var inspected = Hdv.BuyerOffers(pid, gid).FirstOrDefault(x => x.Uid == uid);
        if (inspected is null) return false;
        var fresh = await HdvGetBuyerOffersAsync(pid, gid, ct).ConfigureAwait(false);
        var selected = fresh.FirstOrDefault(x => x.Uid == uid);
        int slot = BidHouseStore.SlotOf(quantity);
        if (selected is null || selected.Prices.Length <= slot || selected.Prices[slot] != expectedPrice
            || !selected.Effects.SequenceEqual(inspected.Effects)) return false;
        return await HdvBuyAsync(pid, uid, quantity, expectedPrice, ct).ConfigureAwait(false);
    }

    // A reprice is a priced removal + insertion. Unlike a generic send, its
    // effect can remain ambiguous after a timeout, reentrant callback or Stop.
    // This safety latch is independent from (and does not reserve) the durable
    // tax journal already used by capped/unlimited manual, Bulk and Lua calls.
    // The profile-scoped half survives ForgetHdv/pid reuse within this app,
    // unlike the ephemeral PID-facing UI half. It is NOT durable across an
    // application restart: direct unbrokered calls still require a common
    // persistent pre-send order journal.
    private readonly ConcurrentDictionary<uint, (int OldUid, int Quantity, long Price)> _hdvRepriceNeedsReview = new();
    private readonly ConcurrentDictionary<(int ServerId, long CharacterId),
        (int OldUid, int Quantity, long Price)> _hdvRepriceProfileNeedsReview = new();

    public bool HdvRepriceReviewPending(uint pid)
    {
        if (_hdvRepriceNeedsReview.ContainsKey(pid)) return true;
        string profile = HdvCurrentMutationProfile(pid);
        if (profile.Length > 0 && (_hdvDirectMutations?.NeedsReview(profile, "reprice:") ?? true))
            return true;
        if (_hdvRepriceProfileNeedsReview.IsEmpty) return false;
        int server = ServerIdOf(pid);
        long character = CharacterId(pid);
        return server > 0 && character > 0 &&
            _hdvRepriceProfileNeedsReview.ContainsKey((server, character));
    }

    public bool HdvAcknowledgeRepriceReview(uint pid)
    {
        string profile = HdvCurrentMutationProfile(pid);
        bool durablePending = profile.Length > 0 &&
                              (_hdvDirectMutations?.NeedsReview(profile, "reprice:") ?? true);
        if (durablePending && _hdvDirectMutations?.TryReview(profile, "reprice:") != true)
            return false;
        if (!durablePending && profile.Length > 0 && _hdvDirectMutations?.NeedsReview(profile) != false)
            return false;
        bool acknowledged = durablePending || _hdvRepriceNeedsReview.TryRemove(pid, out _);
        // Remove the transient latch even if the durable marker was the first
        // source of truth. No cross-profile marker is cleared.
        if (durablePending) _hdvRepriceNeedsReview.TryRemove(pid, out _);
        if (!_hdvRepriceProfileNeedsReview.IsEmpty)
        {
            int server = ServerIdOf(pid);
            long character = CharacterId(pid);
            if (server > 0 && character > 0)
                acknowledged = _hdvRepriceProfileNeedsReview.TryRemove((server, character), out _) || acknowledged;
        }
        if (acknowledged)
        {
            TransportLog.Line($"[hdv] pid {pid} : rapprochement reprice déclaré par l'opérateur ; aucune confirmation réseau inférée.");
            HdvUpdated?.Invoke(pid);
        }
        return acknowledged;
    }

    private void FlagHdvRepriceReview(uint pid, int exchangeUid, int quantity, long price)
    {
        _hdvRepriceNeedsReview[pid] = (exchangeUid, quantity, price);
        HdvUpdated?.Invoke(pid);
    }

    private void FlagHdvRepriceReviewForSession(uint pid, HdvMutationSession session,
        int exchangeUid, int quantity, long price)
    {
        // The original PRE-SEND identity is retained, not the character that
        // happens to own the PID when a late timeout/cancellation returns.
        _hdvRepriceProfileNeedsReview[(session.ServerId, session.CharacterId)] =
            (exchangeUid, quantity, price);
        FlagHdvRepriceReview(pid, exchangeUid, quantity, price);
    }

    /// <summary>REPRICE un lot en vente (forge kch : uid EXCHANGE courant), confirme retrait
    /// exact + nouvelle annonce de même objet/quantité/prix. Une issue inconnue est journalisée.</summary>
    public async Task<bool> HdvRepriceAsync(uint pid, int exchangeUid, int quantity, long newPrice, CancellationToken ct = default)
    {
        var gate = _hdvSellLocks.GetOrAdd(pid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        // All paths reach this same actual dispatch border: manual, Bulk,
        // script, production and direct LaunchService callers. Even a new
        // exchange UID must not bypass an unresolved previous reprice.
        if (HdvRepriceReviewPending(pid))
        {
            TransportLog.Line($"[hdv] pid {pid}: reprice refusé, ordre précédent sans résultat attribuable ; rapprochement opérateur requis.");
            return false;
        }
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return false;
        if (newPrice <= 0 || quantity <= 0) return false;
        if (!HdvSellerOpen(pid)) { TransportLog.Line($"[hdv] pid {pid} : reprice ignoré (HDV vendeur fermé)."); return false; }
        if (CaptureHdvMutationSession(pid) is not { } session)
        {
            TransportLog.Line($"[hdv] pid {pid} : reprice refusé, identité/epoch de session indisponible.");
            return false;
        }
        string directProfile = HdvMutationProfile(session);
        if (_hdvDirectMutations?.NeedsReview(directProfile) != false)
        {
            TransportLog.Line($"[hdv] pid {pid} : reprice refusé, journal durable en quarantaine ou indisponible.");
            return false;
        }
        if (Hdv.MyListings(pid).FirstOrDefault(x => x.Uid == exchangeUid) is not { Gid: > 0 } listing
            || listing.Qty != quantity)
        {
            TransportLog.Line($"[hdv] pid {pid} : reprice refusé (UID vendeur absent ou quantité périmée).");
            return false;
        }
        if (HdvSalePriceRefusal(pid, listing.Gid, quantity, newPrice) is { } refusal)
            return HdvSellFail(pid, refusal);
        var relay = RelayForInject();
        string? code = HdvCode("ExchangeObjectModifyPricedRequest");
        if (relay is null || code is null) return false;
        MarkHdvAuto(pid);
        var receipt = new HdvRepriceCorrelation(exchangeUid, listing.Gid, quantity, newPrice, FieldOf);
        bool injected = false;
        bool attempted = false;
        HdvDirectMutationLease directLease = default;
        DecodedPacket? ev;
        try
        {
            ev = await SendAndWaitForCodeAsync(pid, () =>
            {
                if (!IsCurrentHdvMutationSession(pid, session) ||
                    !Hdv.MyListings(pid).Any(x => x.Uid == exchangeUid &&
                        x.Gid == listing.Gid && x.Qty == quantity) ||
                    HdvSalePriceRefusal(pid, listing.Gid, quantity, newPrice) is not null)
                    return false;
                // Mark the emission boundary BEFORE invoking an untrusted
                // transport delegate: a throw or partial write need not set
                // injected=true, yet could already have reached the server.
                byte[] wire = MoveForge.BuildBidHouseModifyFramed(code, exchangeUid, quantity, newPrice);
                if (_hdvDirectMutations is null ||
                    _hdvDirectMutations.TryBegin(directProfile, pid,
                        HdvTaxOrderKeys.Reprice(exchangeUid, quantity, newPrice),
                        out directLease) != HdvDirectMutationDecision.Allowed)
                    return false;
                attempted = true; // Started remains pending if attempt fsync fails
                if (_hdvDirectMutations.TryMarkAttempt(directLease) != true) return false;
                injected = relay.Inject(pid, wire);
                return injected;
            }, new[] { "ExchangeBidHouseItemRemovedEvent", "ExchangeBidHouseItemAddedEvent",
                       "ExchangeErrorEvent", "ObjectErrorEvent" }, 6000,
                p =>
                {
                    if (p.Pid != pid || p.ClientToServer ||
                        p.DecoderEpoch != session.DecoderEpoch ||
                        !IsCurrentHdvMutationSession(pid, session)) return false;
                    var disposition = receipt.Observe(p.Name, p.Payload);
                    TransportLog.Line($"[hdv] pid {pid} : preuve reprice attendu uid={exchangeUid} gid={listing.Gid} qty={quantity} price={newPrice} : {receipt.Describe(p.Name, p.Payload)} -> {disposition}");
                    return disposition != HdvSellDisposition.NeedsReview;
                }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (attempted)
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            FlagHdvRepriceReviewForSession(pid, session, exchangeUid, quantity, newPrice);
            TransportLog.Line($"[hdv] pid {pid} : reprice uid={exchangeUid} interrompu après envoi ; résultat inconnu.");
            throw;
        }
        catch (Exception) when (attempted)
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            FlagHdvRepriceReviewForSession(pid, session, exchangeUid, quantity, newPrice);
            TransportLog.Line($"[hdv] pid {pid} : reprice uid={exchangeUid} interrompu après envoi ; résultat inconnu.");
            throw;
        }
        var result = ev is null ? HdvSellDisposition.NeedsReview :
            receipt.Observe(ev.Name, ev.Payload);
        if (attempted && !(injected && result == HdvSellDisposition.Confirmed &&
                           IsCurrentHdvMutationSession(pid, session)))
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            // ExchangeErrorEvent alone is not an UID-attributable proof of a
            // refusal. Never silently re-arm even when the shared tax cap is
            // unlimited (= 0) or the caller bypassed the ViewModel broker.
            FlagHdvRepriceReviewForSession(pid, session, exchangeUid, quantity, newPrice);
            TransportLog.Line($"[hdv] pid {pid} : reprice uid={exchangeUid} non attribuable ({result}); aucune réémission automatique.");
        }
        TransportLog.Line($"[hdv] pid {pid} : reprice uid={exchangeUid}, nouveau uid={receipt.NewUid?.ToString() ?? "?"} -> {result}");
        if (injected && result == HdvSellDisposition.Confirmed &&
            IsCurrentHdvMutationSession(pid, session))
        {
            if (_hdvDirectMutations?.TryFinish(directLease, true) != true)
            {
                FlagHdvRepriceReviewForSession(pid, session, exchangeUid, quantity, newPrice);
                return false;
            }
            NoteHdvAction(pid, "reprice");
            return true;
        }
        return false;
        }
        finally { gate.Release(); }
    }

    // A timeout after send could mean that a listing was in fact withdrawn.
    // Require manual reconciliation before another withdrawal by this PID.
    private readonly ConcurrentDictionary<uint, (int Uid, int Quantity)> _hdvRemoveNeedsReview = new();
    private readonly ConcurrentDictionary<uint, HdvSellDisposition> _hdvRemoveLastDisposition = new();
    private readonly ConcurrentDictionary<uint, string> _hdvRemoveUncertainProfile = new();
    public HdvSellDisposition? HdvRemoveLastDisposition(uint pid)
    {
        string profile = HdvCurrentMutationProfile(pid);
        if (profile.Length > 0 &&
            ((_hdvDirectMutations?.NeedsReview(profile, "remove:") ?? true) ||
             (_hdvRemoveNeedsReview.ContainsKey(pid) &&
              _hdvRemoveUncertainProfile.TryGetValue(pid, out var original) && original == profile)))
            return HdvSellDisposition.NeedsReview;
        if (!_hdvRemoveLastDisposition.TryGetValue(pid, out var state)) return null;
        // A delayed completion from a prior PID owner must not taint the next
        // character's display or be cleared under the next identity.
        if (state == HdvSellDisposition.NeedsReview &&
            _hdvRemoveUncertainProfile.TryGetValue(pid, out var oldProfile) && oldProfile != profile)
            return null;
        return state;
    }
    public bool HdvAcknowledgeRemoveReview(uint pid)
    {
        string profile = HdvCurrentMutationProfile(pid);
        if (profile.Length == 0) return false;
        bool durable = _hdvDirectMutations?.NeedsReview(profile, "remove:") ?? true;
        if (durable && _hdvDirectMutations?.TryReview(profile, "remove:") != true) return false;
        if (!durable && _hdvDirectMutations?.NeedsReview(profile) != false) return false;
        bool removed = _hdvRemoveNeedsReview.ContainsKey(pid) &&
                       _hdvRemoveUncertainProfile.TryGetValue(pid, out var original) && original == profile;
        if (removed) _hdvRemoveNeedsReview.TryRemove(pid, out _);
        if (removed || durable)
        {
            _hdvRemoveUncertainProfile.TryRemove(pid, out _);
            _hdvRemoveLastDisposition.TryRemove(pid, out _);
            TransportLog.Line($"[hdv] pid {pid} : rapprochement du retrait déclaré par l'opérateur ; aucune confirmation réseau inférée.");
            HdvUpdated?.Invoke(pid);
        }
        return removed || durable;
    }

    private void FlagHdvRemoveReview(uint pid, string profile, int uid, int quantity)
    {
        _hdvRemoveUncertainProfile[pid] = profile;
        _hdvRemoveNeedsReview[pid] = (uid, quantity);
        _hdvRemoveLastDisposition[pid] = HdvSellDisposition.NeedsReview;
        HdvUpdated?.Invoke(pid);
    }

    /// <summary>RETIRE un lot de la vente (forge kcr : quantité négative + uid EXCHANGE), attend le retrait (ken).</summary>
    public async Task<bool> HdvRemoveAsync(uint pid, int exchangeUid, int quantity, CancellationToken ct = default)
    {
        var gate = _hdvSellLocks.GetOrAdd(pid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        if (HdvRemoveLastDisposition(pid) == HdvSellDisposition.NeedsReview)
        {
            TransportLog.Line($"[hdv] pid {pid} : retrait bloqué, précédent résultat incertain ; reconciliation manuelle nécessaire.");
            return false;
        }
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return false;
        if (!HdvSellerOpen(pid)) { TransportLog.Line($"[hdv] pid {pid} : retrait ignoré (HDV vendeur fermé)."); return false; }
        if (quantity <= 0 || exchangeUid <= 0) { TransportLog.Line($"[hdv] pid {pid} : retrait refusé (lot sans quantité)."); return false; }
        if (CaptureHdvMutationSession(pid) is not { } session) return false;
        string profile = HdvMutationProfile(session);
        if (_hdvDirectMutations?.NeedsReview(profile) != false) return false;
        if (Hdv.MyListings(pid).FirstOrDefault(x => x.Uid == exchangeUid) is not { Gid: > 0 } listing ||
            listing.Qty != quantity || listing.Price <= 0) return false;
        var relay = RelayForInject();
        string? code = HdvCode("ExchangeObjectMoveRequest");
        if (relay is null || code is null) return false;
        _hdvRemoveLastDisposition.TryRemove(pid, out _);
        MarkHdvAuto(pid);
        // ItemRemoved only echoes sell_id. It may confirm this request if that sell_id
        // was previously linked to the bag UID by independent inventory/listing evidence.
        bool linked = _hdvSellLinks.Any(x => x.Key.Pid == pid && x.Value == exchangeUid);
        bool injected = false;
        bool attempted = false;
        HdvDirectMutationLease directLease = default;
        DecodedPacket? ev;
        try
        {
            ev = await SendAndWaitForCodeAsync(pid, () =>
            {
                if (!IsCurrentHdvMutationSession(pid, session) ||
                    !Hdv.MyListings(pid).Any(x => x.Uid == exchangeUid && x.Gid == listing.Gid &&
                                                   x.Qty == quantity && x.Price == listing.Price))
                    return false;
                byte[] wire = MoveForge.BuildBidHouseRemoveFramed(code, exchangeUid, quantity);
                if (_hdvDirectMutations?.TryBegin(profile, pid,
                        HdvDirectMutationJournal.RemoveOrder(exchangeUid, listing.Gid,
                            quantity, listing.Price, session.DecoderEpoch), out directLease)
                    != HdvDirectMutationDecision.Allowed) return false;
                attempted = true; // a failed attempt fsync must quarantine Started
                if (_hdvDirectMutations?.TryMarkAttempt(directLease) != true) return false;
                injected = relay.Inject(pid, wire);
                TransportLog.Line(HdvUidDiagnostic.RemoveRequest(pid, exchangeUid, quantity, injected, linked));
                return injected;
            }, new[] { "ExchangeBidHouseItemRemovedEvent", "ExchangeBidHouseInListRemovedEvent", "ExchangeErrorEvent", "ObjectErrorEvent" },
                6000, p => p.Pid == pid && !p.ClientToServer &&
                           p.DecoderEpoch == session.DecoderEpoch &&
                           IsCurrentHdvMutationSession(pid, session) &&
                           HdvRemoveEvidence.Classify(p.Name, p.Payload, exchangeUid, linked, FieldOf) != HdvSellDisposition.NeedsReview,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (attempted)
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            FlagHdvRemoveReview(pid, profile, exchangeUid, quantity);
            throw;
        }
        catch (Exception) when (attempted)
        {
            _hdvDirectMutations?.TryFinish(directLease, false);
            FlagHdvRemoveReview(pid, profile, exchangeUid, quantity);
            throw;
        }
        var result = ev is null ? HdvSellDisposition.NeedsReview :
            HdvRemoveEvidence.Classify(ev.Name, ev.Payload, exchangeUid, linked, FieldOf);
        TransportLog.Line(HdvUidDiagnostic.RemoveResponse(pid, ev?.Name, exchangeUid, result, linked));
        if (attempted)
        {
            if (!injected || !IsCurrentHdvMutationSession(pid, session) ||
                result != HdvSellDisposition.Confirmed ||
                _hdvDirectMutations?.TryFinish(directLease, true) != true)
            {
                _hdvDirectMutations?.TryFinish(directLease, false);
                FlagHdvRemoveReview(pid, profile, exchangeUid, quantity);
                return false;
            }
            _hdvRemoveLastDisposition[pid] = HdvSellDisposition.Confirmed;
        }
        TransportLog.Line($"[hdv] pid {pid} : retrait uid={exchangeUid} -> {result}");
        if (!attempted || result != HdvSellDisposition.Confirmed) return false;
        foreach (var key in _hdvSellLinks.Where(x => x.Key.Pid == pid && x.Value == exchangeUid).Select(x => x.Key))
            _hdvSellLinks.TryRemove(key, out _);
        NoteHdvAction(pid, "remove");
        return true;
        }
        finally { gate.Release(); }
    }

    /// <summary>MET EN VENTE par GID (API Lua sale:SellItem) : résout le GID -> object_uid du SAC (à défaut de la banque),
    /// puis <see cref="HdvSellAsync"/>. Renvoie false si aucun objet du gid avec une quantité suffisante.</summary>
    public async Task<bool> HdvSellByGidAsync(uint pid, long gid, int quantity, long price, CancellationToken ct = default)
    {
        var pick = HdvMergedItems(pid)
            .Where(x => x.Item.Gid == gid && x.Item.Position == 63 && x.Item.Qty >= quantity && ItemCanTrade(pid, x.Item.Uid))
            .OrderBy(x => x.InBank ? 1 : 0)   // sac d'abord (l'uid du sac est celui attendu par kge)
            .Select(x => (int?)x.Item.Uid).FirstOrDefault();
        if (pick is not int uid) { TransportLog.Line($"[hdv] pid {pid} : vente Lua gid={gid} x{quantity} : objet introuvable."); return false; }
        return await HdvSellAsync(pid, uid, quantity, price, ct).ConfigureAwait(false);
    }

    /// <summary>REPRICE par GID (API Lua sale:EditPriceByGID) : retrouve MON lot du gid+quantité (uid EXCHANGE courant) puis
    /// <see cref="HdvRepriceAsync"/>. Renvoie false si aucun lot correspondant en vente.</summary>
    public async Task<bool> HdvRepriceByGidAsync(uint pid, long gid, int quantity, long price, CancellationToken ct = default)
    {
        if (Hdv.MyListingByGid(pid, gid, quantity) is not { } l) { TransportLog.Line($"[hdv] pid {pid} : reprice Lua gid={gid} x{quantity} : lot introuvable."); return false; }
        return await HdvRepriceAsync(pid, l.Uid, quantity, price, ct).ConfigureAwait(false);
    }

    /// <summary>REPRICE de TOUS mes lots (gid, quantité) à `price` (API Lua sale:EditPriceByGID: les lots
    /// déjà à ce prix sont sautés). Snapshot des UID d'abord : un reprice ne change l'UID QUE de ce lot-là.
    /// Renvoie (trouvés, tentés, réussis). Le délai entre deux reprices vient du réglage de vente en masse
    /// du personnage (BulkDelayMinMs/BulkDelayMaxMs, 0 ms autorisé) — plus de 400 ms figés.
    /// ⚠ Ne pas confondre avec <see cref="HdvRepriceByGidAsync"/> (UN lot).</summary>
    public async Task<(int Matched, int Attempted, int Succeeded)> HdvRepriceEveryByGidAsync(uint pid, long gid, int quantity, long price, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return (0, 0, 0);
        var mine = Hdv.MyListings(pid).Where(l => l.Gid == gid && l.Qty == quantity).ToList();
        if (mine.Count == 0) { TransportLog.Line($"[hdv] pid {pid} : reprice Lua gid={gid} x{quantity} : lot introuvable."); return (0, 0, 0); }
        var todo = mine.Where(l => l.Price != price).Select(l => l.Uid).ToList();
        int ok = 0;
        int attempted = 0;
        for (int i = 0; i < todo.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (i > 0)
            {
                int delay = ResolveRepriceDelay(pid);
                if (HdvRepricePacing.ShouldWait(delay)) await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            attempted++;
            if (await HdvRepriceAsync(pid, todo[i], quantity, price, ct).ConfigureAwait(false)) ok++;
            else break; // one uncertain/denied UID stops the entire multi-lot emission batch
        }
        return (mine.Count, attempted, ok);
    }

    /// <summary>SOUS-COTE en LOT (API Lua sale:repriceAll) : pour CHAQUE lot en vente, rafraîchit le prix marché de son gid
    /// et, si mon prix est STRICTEMENT au-dessus du marché min, me repositionne à (marché − undercut). ⚠️ GARDE anti-auto-sous-cote
    /// OBLIGATOIRE : on ne repositionne JAMAIS un lot déjà ≤ marché — MarketFor inclut MON propre lot, donc sans ça le bot se
    /// sous-coterait lui-même à chaque passage. Renvoie le nb de lots repositionnés. Exige le HDV en mode VENDEUR (goToHdv).</summary>
    public Task<int> HdvRepriceAllAsync(uint pid, long undercut = 1, CancellationToken ct = default)
        => HdvRepriceAllCoreAsync(pid, undercut, ct, HdvRepriceAsync);

    // The same repricing policy supports Lua's durable tax sender without
    // changing the direct LaunchService API used by the desktop ledger.
    private async Task<int> HdvRepriceAllCoreAsync(uint pid, long undercut,
        CancellationToken ct, Func<uint, int, int, long, CancellationToken, Task<bool>> reprice,
        bool failureAsNegative = false)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) return 0;
        if (!HdvSellerOpen(pid)) { TransportLog.Line($"[hdv] pid {pid} : repriceAll ignoré (HDV vendeur fermé — goToHdv d'abord)."); return 0; }
        if (undercut < 1) undercut = 1;
        // snapshot figé (uid, gid, qty, prix). Un reprice ne change l'uid EXCHANGE QUE du lot repositionné (retrait+ré-ajout
        // serveur) : les AUTRES uid du snapshot restent valides -> on repositionne CHAQUE lot par SON uid. (Re-résoudre via
        // MyListingByGid retrouvait toujours le MÊME lot en cas de doublons gid+qty : il baissait à chaque appel, l'autre jamais.)
        var lots = Hdv.MyListings(pid).Select(l => (l.Uid, l.Gid, l.Qty, l.Price)).ToList();
        int repriced = 0;
        var groups = HdvRepricePacing.GroupLotsByGid(lots, l => l.Gid);
        foreach (var (gid, groupLots) in groups)
        {
            if (ct.IsCancellationRequested) break;
            await HdvGetMarketPriceAsync(pid, gid, ct).ConfigureAwait(false);            // rafraîchit le marché de ce gid (remplit MarketFor par lot)
            foreach (var (uid, _, qty, oldPrice) in groupLots)
            {
                if (ct.IsCancellationRequested) break;
                if (!Hdv.MyListings(pid).Any(l => l.Uid == uid)) continue;                   // lot vendu/retiré entre-temps
                int slot = BidHouseStore.SlotOf(qty);
                long min = Hdv.MarketFor(pid, gid, slot);
                if (min <= 0) continue;                                                       // marché inconnu -> ne pas toucher
                if (oldPrice <= min) continue;                                                // GARDE : je suis déjà au/sous le marché -> ne pas me sous-coter moi-même
                // GARDE doublons : si le min du marché est l'un de MES AUTRES lots (même gid, même tranche), on s'ALIGNE dessus au
                // lieu de le sous-coter (sinon chaque lot identique sous-cote le précédent : fuite de kamas à chaque passage).
                bool minIsMine = Hdv.MyListings(pid).Any(l => l.Uid != uid && l.Gid == gid && BidHouseStore.SlotOf(l.Qty) == slot && l.Price == min);
                long target = minIsMine ? min : min - undercut;
                if (target < 1) target = 1;
                if (target >= oldPrice) continue;                                             // aucune amélioration -> skip
                if (await reprice(pid, uid, qty, target, ct).ConfigureAwait(false)) repriced++;
                else return failureAsNegative ? -1 : repriced; // script must not mistake partial success for full completion
                int delay = ResolveRepriceDelay(pid);
                if (HdvRepricePacing.ShouldWait(delay)) await Task.Delay(delay, ct).ConfigureAwait(false);   // anti-spam inter-lot (0 ms = aucune attente)
            }
        }
        TransportLog.Line($"[hdv] pid {pid} : repriceAll -> {repriced}/{lots.Count} lot(s) repositionné(s).");
        return repriced;
    }

    internal int ResolveRepriceDelay(uint pid)
    {
        string? charName = SelfCharacterInfo(pid)?.Name;
        int serverId = ServerIdOf(pid);
        var trade = HdvProfileStore.GetScoped(charName, serverId)?.Trade;
        return HdvRepricePacing.ResolveDelay(trade);
    }
}

/// <summary>Cadence et regroupement des requêtes HDV pour les opérations de réajustement des prix.</summary>
public static class HdvRepricePacing
{
    /// <summary>
    /// Résout la cadence de reprice selon les réglages utilisateur de vente en masse (0 ms = aucun délai).
    /// </summary>
    public static int ResolveDelayMs(int minMs, int maxMs)
    {
        if (maxMs <= 0) return 0;
        var (min, max) = HdvPacing.Resolve(minMs, maxMs);
        if (max <= 0) return 0;
        return max > min ? Random.Shared.Next(min, max + 1) : min;
    }

    /// <summary>
    /// Résout le délai depuis les paramètres de commerce HDV du personnage.
    /// </summary>
    public static int ResolveDelay(HdvTradeSettings? settings)
    {
        if (settings is null) return 0;
        return ResolveDelayMs(settings.BulkDelayMinMs, settings.BulkDelayMaxMs);
    }

    /// <summary>Indique si un délai doit être attendu (faux si 0 ms ou négatif).</summary>
    public static bool ShouldWait(int delayMs) => delayMs > 0;

    /// <summary>
    /// Regroupe les lots par GID distinct pour ne faire qu'une seule interrogation de marché par GID.
    /// </summary>
    public static IReadOnlyList<(long Gid, IReadOnlyList<TLot> Lots)> GroupLotsByGid<TLot>(
        IEnumerable<TLot> lots, Func<TLot, long> gidSelector)
    {
        return lots.GroupBy(gidSelector)
            .Select(g => (g.Key, (IReadOnlyList<TLot>)g.ToList()))
            .ToList();
    }
}
