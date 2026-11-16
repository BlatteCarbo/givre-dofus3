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

// ===================== Banque : store du coffre, verbes Lua « Lot 2 » (métier / dialogue PNJ / dépôt) et banque pilotée par script (Lot D) =====================
public sealed partial class LaunchService
{
    /// <summary>Contenu du COFFRE/banque (uid-&gt;gid,qty) par pid. Seedé à l'ouverture (iwb), maj sur itc. Vide tant que
    /// la banque n'a pas été ouverte cette session.</summary>
    public InventoryStore Bank { get; } = new();

    /// <summary>Levé quand le contenu du coffre change (ouverture iwb / retrait itc) -&gt; rafraîchit l'onglet Banque.</summary>
    public event Action<uint>? BankUpdated;

    // ============ VERBES LUA « Lot 2 » : MÉTIER / DIALOGUE PNJ / BANQUE (dépôt-retrait générique) ============

    /// <summary>Niveau connu d'un métier (job_id) pour ce perso, ou 0 si jamais reçu. Alimenté par JobExperiencesUpdateEvent.</summary>
    public int JobLevel(uint pid, int jobId) => _jobLevels.TryGetValue(pid, out var d) && d.TryGetValue(jobId, out var l) ? l : 0;

    /// <summary>Tous les métiers connus (job_id -&gt; niveau) pour ce perso — snapshot pour l'énumération (ex API Lua job:all).</summary>
    public IReadOnlyDictionary<int, int> JobLevels(uint pid) =>
        _jobLevels.TryGetValue(pid, out var d) ? new Dictionary<int, int>(d) : new Dictionary<int, int>();

    /// <summary>Progression XP authentiquement décodée lorsqu'elle est disponible ; copie immuable pour les vues.</summary>
    public IReadOnlyDictionary<int, JobProgressSnapshot> JobProgress(uint pid) =>
        _jobProgress.TryGetValue(pid, out var d)
            ? new Dictionary<int, JobProgressSnapshot>(d)
            : new Dictionary<int, JobProgressSnapshot>();

    // Banque : dépôt/retrait sur un coffre DÉJÀ OUVERT (putAllItems/getAllItems...). Best-effort : sans coffre ouvert, le
    // serveur ignore la requête. Codes/champs résolus PAR NOM (MoveForge), corps VIDE (« tout » / « déjà présents »).
    // Gestion de stock (dépôt/retrait coffre) = prolongement du workflow récolte -> gate Perm.Gather au POINT D'ACTION
    // (ferme le trou « toute session autorisée forge un dépôt/retrait sans aucun code » ; un tier sans récolte est bloqué).
    public bool BankDepositAll(uint pid) => StorageOpen(pid) && LicenseService.HasPermission(LicenseService.Perm.Gather) && InjectForge(pid, DepositAllPacket(pid));
    public bool BankDepositExisting(uint pid) => StorageOpen(pid) && LicenseService.HasPermission(LicenseService.Perm.Gather) && InjectForge(pid, DepositExistingPacket(pid));
    public bool BankWithdrawAll(uint pid) => StorageOpen(pid) && LicenseService.HasPermission(LicenseService.Perm.Gather) && InjectForge(pid, MoveForge.BuildBankWithdrawAllFramed());
    public bool BankWithdrawExisting(uint pid) => StorageOpen(pid) && LicenseService.HasPermission(LicenseService.Perm.Gather) && InjectForge(pid, MoveForge.BuildBankWithdrawExistingFramed());
    /// <summary>Dépose une LISTE de piles du sac (ExchangeObjectTransferListFromInventoryRequest, comme « objets visibles » du jeu).</summary>
    public bool BankDepositList(uint pid, IReadOnlyList<int> uids)
        => StorageOpen(pid) && uids.Count > 0 && uids.All(uid => Inventory.Get(pid, uid) is { Position: 63, Qty: > 0 } && ItemCanBank(pid, uid))
           && LicenseService.HasPermission(LicenseService.Perm.Gather) && InjectForge(pid, MoveForge.BuildBankDepositListFramed(uids));
    /// <summary>« Tout déposer » sans les objets liés au personnage : la liste remplace le dépôt global dès qu'il y en a un dans le sac.</summary>
    private byte[] DepositAllPacket(uint pid)
    {
        var uids = Inventory.InventoryUids(pid) ?? Array.Empty<int>();
        var bankable = uids.Where(uid => ItemCanBank(pid, uid)).ToList();
        if (bankable.Count == uids.Count) return MoveForge.BuildBankDepositAllFramed();
        return bankable.Count > 0 ? MoveForge.BuildBankDepositListFramed(bankable) : Array.Empty<byte>();
    }

    private byte[] DepositExistingPacket(uint pid)
    {
        var uids = Inventory.InventoryUids(pid);
        if (uids.All(uid => ItemCanBank(pid, uid))) return MoveForge.BuildBankDepositExistingFramed();
        var existing = Bank.Items(pid).Select(i => i.Gid).ToHashSet();
        var bankable = uids.Where(uid => ItemCanBank(pid, uid)
            && Inventory.Get(pid, uid) is { } item && existing.Contains(item.Gid)).ToList();
        return bankable.Count > 0 ? MoveForge.BuildBankDepositListFramed(bankable) : Array.Empty<byte>();
    }

    private bool InjectForge(uint pid, byte[] pkt) { var relay = RelayForInject(); return relay is not null && pkt.Length > 0 && relay.Inject(pid, pkt); }

    /// <summary>DÉPÔT SÉLECTIF d'un objet par GID dans un coffre DÉJÀ OUVERT (ExchangeObjectMoveRequest{quantity,uid}).
    /// count &lt;= 0 = tout le stack. GID->UID via l'inventaire. Gaté Perm.Gather (prolongement du workflow récolte).</summary>
    public bool BankDepositItem(uint pid, long gid, int count)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null || !StorageOpen(pid)) return false;
        var stacks = BagStacks(pid, gid).Where(i => ItemCanBank(pid, i.Uid)).ToList();   // sac seulement, pile par pile (jamais l'exemplaire équipé), sans lié au perso
        if (stacks.Count == 0) { TransportLog.Line($"[banque] pid {pid} : dépôt gid {gid} -> absent du sac."); return false; }
        int want = count > 0 ? count : (int)Math.Min(int.MaxValue, stacks.Sum(i => (long)i.Qty));
        bool any = false;
        foreach (var st in stacks)
        {
            if (want <= 0) break;
            int qty = Math.Min(want, st.Qty);
            byte[] pkt = MoveForge.BuildExchangeObjectMoveFramed(st.Uid, qty);
            if (pkt.Length == 0) { TransportLog.Line($"[banque] pid {pid} : dépôt gid {gid} NON forgé ({MoveForge.LastForgeError})."); return any; }
            bool ok = relay.Inject(pid, pkt);
            ShowInjected(pid, pkt);
            TransportLog.Line($"[banque] pid {pid} : dépôt sélectif gid {gid} x{qty} (uid {st.Uid}) inject={(ok ? "OK" : "ECHEC")}");
            if (!ok) return any;
            any = true; want -= qty;
        }
        return any;
    }

    /// <summary>PARLE à un PNJ (NpcGenericActionRequest, action=3=dialogue) et attend l'ouverture du dialogue ; mémorise les
    /// réponses visibles pour replyNpc. V1 : action=3 (dialogue standard, comme le banquier) — couvre les PNJ à dialogue
    /// classique ; un PNJ à action spécifique peut demander un affinage. Bloquant (appelé via GetResult côté Lua).</summary>
    public async Task<bool> NpcTalkAsync(uint pid, long npcId, CancellationToken ct = default)
    {
        var relay = RelayForInject(); if (relay is null) return false;
        long map = CurrentMap(pid) ?? 0; if (map == 0) return false;
        var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildNpcTalkFramed(map, npcId)),
            new[] { "NpcDialogQuestionEvent", "NpcDialogCreationEvent" }, 4000, ct).ConfigureAwait(false);
        if (ev is { Name: "NpcDialogQuestionEvent" }) _lastNpcDialog[pid] = ev.Payload;
        if (ev is null) TransportLog.Line($"[pnj] pid {pid} : talkNpc({npcId}) sans dialogue (PNJ absent ou action non standard).");
        return ev is not null;
    }

    /// <summary>Répond au dialogue PNJ courant par l'INDEX (1-based) de la réponse visible. false si aucun dialogue ou index hors limites.</summary>
    public bool NpcReply(uint pid, int index)
    {
        var relay = RelayForInject(); if (relay is null) return false;
        if (!_lastNpcDialog.TryGetValue(pid, out var dlg)) { TransportLog.Line($"[pnj] pid {pid} : replyNpc sans dialogue ouvert."); return false; }
        var replies = AllNpcReplyIds(dlg);
        if (index < 1 || index > replies.Count) { TransportLog.Line($"[pnj] pid {pid} : replyNpc({index}) hors plage — index 1-based, {replies.Count} réponse(s) visible(s) (utilise 1..{replies.Count})."); return false; }
        return relay.Inject(pid, MoveForge.BuildNpcDialogReplyFramed(replies[index - 1]));
    }

    /// <summary>Répond au dialogue PNJ (index 1-based) PUIS attend un changement de carte (téléport PNJ). Bloquant. false si pas de changement dans le délai.</summary>
    public async Task<bool> NpcReplyAndChangeMapAsync(uint pid, int index, CancellationToken ct = default)
    {
        long from = CurrentMap(pid) ?? 0;
        if (!NpcReply(pid, index)) return false;
        long deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            long now = CurrentMap(pid) ?? 0;
            if (now != 0 && now != from) return true;
            try { await Task.Delay(120, ct).ConfigureAwait(false); } catch { return false; }
        }
        long final = CurrentMap(pid) ?? 0; return final != 0 && final != from;
    }


    /// <summary>Depose TOUT le sac a la banque (le perso DOIT deja etre sur la map de la banque). Sequence CAPTUREE en jeu :
    /// iov (ouvrir banquier, npcId -20000) -&gt; [ios reponses] -&gt; ioy(reponse[0]) -&gt; [iwb coffre ouvert] -&gt; kez (deposer
    /// tout) -&gt; kla (fermer). Le transfert n'est confirmé que par la disparition serveur de chacune des piles ciblées.
    /// Un envoi accepté par le relay sans reçu reste une issue inconnue (false), sans réémission automatique.</summary>
    public async Task<bool> BankDepositAllAsync(uint pid, long npcMapId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Inventory.IsSeeded(pid))
        {
            TransportLog.Line($"[banque] pid {pid} : dépôt refusé sans snapshot complet du sac.");
            return false;
        }
        if (!await BankOpenAsync(pid, npcMapId, reuseIfOpen: true, ct).ConfigureAwait(false)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var (mode, depositPkt) = ConfiguredDepositPacket(pid);
        var existing = mode == "existing" ? Bank.Items(pid).Select(i => i.Gid).ToHashSet() : null;
        var candidateUids = Inventory.Items(pid)
            .Where(i => i.Position == 63 && i.Qty > 0 && ItemCanBank(pid, i.Uid) && (existing is null || existing.Contains(i.Gid)))
            .Select(i => i.Uid);
        var receipt = new MZP27BankDepositTracker(candidateUids);
        var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapAtSend = CurrentMap(pid);
        int weightBefore = _lastPods.TryGetValue(pid, out var podsSnap) ? podsSnap.Cur : -1;
        var weightReducedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveWeight(uint p, int curWeight, int _)
        {
            if (p == pid && weightBefore > 0 && curWeight < weightBefore)
                weightReducedTcs.TrySetResult(true);
        }
        void Observe(DecodedPacket p)
        {
            if (p.Pid != pid || p.ClientToServer || p.Name is null ||
                !StorageOpen(pid) || CurrentMap(pid) != mapAtSend) return;
            if (receipt.Observe(p.Name, p.Payload, FieldOf)) complete.TrySetResult(true);
        }
        bool injected = false;
        bool weightConfirmed = weightBefore <= 0;
        bool closed = false;
        PodsUpdated += ObserveWeight;
        PacketDecoded += Observe;
        try
        {
            if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
            if (depositPkt.Length > 0 && receipt.ExpectedCount > 0 && StorageOpen(pid))
            {
                ct.ThrowIfCancellationRequested();
                injected = relay.Inject(pid, depositPkt);
                ShowInjected(pid, depositPkt);
                if (injected)
                {
                    await Task.WhenAny(complete.Task, Task.Delay(3000, ct)).ConfigureAwait(false);
                    if (weightBefore > 0)
                    {
                        var wtDone = await Task.WhenAny(weightReducedTcs.Task, Task.Delay(2500, ct)).ConfigureAwait(false);
                        weightConfirmed = wtDone == weightReducedTcs.Task || (_lastPods.TryGetValue(pid, out var pAfter) && pAfter.Cur < weightBefore);
                        if (!weightConfirmed && !receipt.IsComplete)
                            TransportLog.Line($"[banque] pid {pid} : dépôt NON confirmé (poids inchangé={weightBefore} après 2500 ms)");
                    }
                }
            }
        }
        finally
        {
            PodsUpdated -= ObserveWeight;
            PacketDecoded -= Observe;
            closed = await BankCloseAsync(pid, CancellationToken.None).ConfigureAwait(false);
        }
        bool confirmed = (receipt.ExpectedCount == 0 || injected && (receipt.IsComplete && weightConfirmed)) && closed && !ct.IsCancellationRequested;
        TransportLog.Line($"[banque] pid {pid} : dépôt mode={mode}, piles confirmées={receipt.ConfirmedCount}/{receipt.ExpectedCount}, poids réduit={weightConfirmed}, injection={(injected ? "OK" : "ÉCHEC")}, issue={(confirmed ? "confirmée" : "INCONNUE ou rejetée")}; fermeture confirmée={closed}.");
        return confirmed;
    }

    /// <summary>Type de dépôt choisi dans l'onglet Banque (config globale) : tout (kez) / déjà présents (kgx) / objets visibles
    /// du sac (kdd, liste d'uids non équipés). « visible » sans inventaire connu -> repli sur « tout » (évite un no-op).
    /// Partagé par la banque (BankDepositAllAsync) et les coffres à code (StorageDepositAndCloseAsync, Lot J).</summary>
    private (string Mode, byte[] Pkt) ConfiguredDepositPacket(uint pid)
    {
        string mode = AppConfig.Current.BankDeposit;
        return (mode, mode switch
        {
            "existing" => DepositExistingPacket(pid),
            _ => DepositAllPacket(pid),
        });
    }

    // ===================== Lot D : BANQUE pilotée par script (ouvrir / retirer / déposer sauf / kamas / fermer) =====================
    // État « coffre ouvert » = pid -> carte au moment où le StorageInventoryContentEvent est arrivé. Un changement de carte
    // l'invalide implicitement (StorageOpen compare à la carte courante). Effacé sur ExchangeLeaveEvent / fermeture / Forget.
    // ⚠ PAS effacé sur DialogLeaveEvent : la fermeture du dialogue PNJ du banquier peut arriver APRÈS l'ouverture du coffre.
    private readonly ConcurrentDictionary<uint, long> _storageOpenMap = new();

    // Invalidation définitive sur changement de contexte : revenir sur la même carte ne rouvre pas le coffre.
    private void NoteBankSession(DecodedPacket packet)
    {
        if (!packet.ClientToServer && packet.Name is "MapCurrentEvent" or "ExchangeLeaveEvent")
        {
            _storageOpenMap.TryRemove(packet.Pid, out _);
            _lastNpcDialog.TryRemove(packet.Pid, out _);
        }
    }

    /// <summary>Vrai si un coffre (banque) est ouvert pour ce bot SUR la carte courante. Lecture pure.</summary>
    public bool StorageOpen(uint pid) => _storageOpenMap.TryGetValue(pid, out var m) && m != 0 && m == (CurrentMap(pid) ?? 0);

    /// <summary>OUVRE la banque via le banquier de la carte (NpcGenericActionRequest -&gt; 1re réponse -&gt; coffre). npcMapId&lt;=0 =
    /// carte courante. reuseIfOpen : coffre déjà ouvert sur cette carte -&gt; true sans rien envoyer. Bloquant. Gaté Perm.Gather.</summary>
    public async Task<bool> BankOpenAsync(uint pid, long npcMapId, bool reuseIfOpen = true, CancellationToken ct = default, long npcActorId = -20000)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;   // dépôt banque = prolongement récolte -> POINT D'ACTION
        var relay = RelayForInject();
        if (relay is null) return false;
        if (reuseIfOpen && StorageOpen(pid)) return true;
        if (npcMapId <= 0) npcMapId = CurrentMap(pid) ?? 0;
        if (npcMapId == 0) return false;

        var q = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildNpcBankOpenFramed(npcMapId, npcActorId)), new[] { "NpcDialogQuestionEvent" }, 5000, ct).ConfigureAwait(false);
        if (q is null) { TransportLog.Line($"[banque] pid {pid} : pas de dialogue banquier (NpcDialogQuestionEvent) apres l'ouverture -> abandon (banquier absent sur cette carte ?)."); return false; }
        int? reply = FirstNpcReplyId(q.Payload);
        if (reply is null) { TransportLog.Line($"[banque] pid {pid} : aucune reponse lisible dans le dialogue banquier -> abandon."); return false; }
        TransportLog.Line($"[banque] pid {pid} : dialogue banquier -> reponse {reply} (acces banque).");

        var open = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildNpcDialogReplyFramed(reply.Value)), new[] { "StorageInventoryContentEvent" }, 5000, ct).ConfigureAwait(false);
        if (open is null) { TransportLog.Line($"[banque] pid {pid} : coffre non ouvert (StorageInventoryContentEvent) après réponse {reply} -> abandon."); return false; }
        ApplyStorageContent(open);   // état « coffre ouvert » + contenu écrits AVANT le retour (le waiter précède le décodeur)
        return true;
    }

    // Dernier StorageInventoryContentEvent APPLIQUÉ (idempotence : verbe bloquant au point d'accusé PUIS décodeur, même paquet).
    private readonly ConcurrentDictionary<uint, DecodedPacket> _storageContentPkt = new();

    private void ApplyStorageContent(DecodedPacket p)
    {
        lock (_storageContentPkt)
        {
            if (_storageContentPkt.TryGetValue(p.Pid, out var last) && ReferenceEquals(last, p)) return;
            _storageContentPkt[p.Pid] = p;
            // Objets lus PAR NOM comme les kamas : le n° du champ « objects » a bougé avec les patchs (fields.json fait foi).
            Bank.Seed(p.Pid, InventoryReader.ReadContentAt(p.Payload, FNum("StorageInventoryContentEvent", "objects"))
                .Select(i => new InventoryStore.Item(i.Uid, i.Gid, i.Qty, i.Position)));
            // Kamas du coffre lus par nom. ABSENT ne veut pas dire 0 : le serveur les envoie aussi à part (StorageKamasUpdateEvent, capture
            // 23/10 : 131 707 250 annoncés puis contenu sans champ kamas -> l'ancien « ?? 0 » écrasait le bon total). On garde la valeur connue.
            if (InventoryReader.ReadKamasAt(p.Payload, FNum("StorageInventoryContentEvent", "kamas")) is { } ck) _bankKamas[p.Pid] = ck;
            else _bankKamas.TryAdd(p.Pid, 0);
            _storageOpenMap[p.Pid] = CurrentMap(p.Pid) ?? 0;   // Lot D : coffre ouvert SUR cette carte (bank:isOpen / verbes bank:)
            TransportLog.Line($"[banque] pid {p.Pid} : coffre reçu -> {Bank.StackCount(p.Pid)} stacks, {BankKamas(p.Pid)} kamas");
        }
        BankUpdated?.Invoke(p.Pid);
    }

    /// <summary>FERME la banque (DialogLeaveRequest) et attend la fin de l'échange (best-effort). Non gaté : fermer un
    /// dialogue est toujours permis. Seul ExchangeLeaveEvent confirme la fermeture ; un envoi échoué ou muet conserve l'état.</summary>
    public async Task<bool> BankCloseAsync(uint pid, CancellationToken ct = default)
    {
        var relay = RelayForInject();
        if (relay is null) return false;
        ct.ThrowIfCancellationRequested();
        var response = await SendAndWaitForCodeAsync(pid, () =>
        {
            var packet = MoveForge.BuildDialogLeaveFramed();
            return packet.Length > 0 && relay.Inject(pid, packet);
        }, new[] { "ExchangeLeaveEvent" }, 2500, ct).ConfigureAwait(false);
        bool confirmed = response is not null;
        // NoteBankSession a appliqué ExchangeLeaveEvent avant le waiter. Ne pas effacer une réouverture plus récente.
        TransportLog.Line($"[banque] pid {pid} : fermeture du coffre confirmée={confirmed}");
        return confirmed;
    }

    /// <summary>RETIRE `qty` d'un objet (par GID) du coffre OUVERT, stack par stack, en attendant l'accusé du coffre à chaque
    /// envoi. qty&lt;=0 = tout. Renvoie la quantité effectivement retirée (accusée). Bloquant. Gaté Perm.Gather.</summary>
    public async Task<int> BankWithdrawAsync(uint pid, long gid, int qty, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return 0;
        var relay = RelayForInject();
        if (relay is null) return 0;
        if (!StorageOpen(pid)) { TransportLog.Line($"[banque] pid {pid} : retrait sans coffre ouvert"); return 0; }
        var stacks = Bank.Items(pid).Where(i => i.Gid == gid && i.Qty > 0).ToList();
        if (stacks.Count == 0) return 0;
        long wantL = qty > 0 ? qty : stacks.Sum(s => (long)s.Qty);
        int want = (int)Math.Min(int.MaxValue, wantL);
        int done = 0;
        for (int i = 0; i < stacks.Count && want > 0; i++)
        {
            var st = stacks[i];
            int k = Math.Min(want, st.Qty);
            byte[] pkt = MoveForge.BuildExchangeObjectMoveSignedFramed(st.Uid, -k);   // quantité NÉGATIVE = retrait (.Vi, int64 sign-extend)
            if (pkt.Length == 0) { TransportLog.Line($"[banque] pid {pid} : retrait gid {gid} NON forgé ({MoveForge.LastForgeError})."); break; }
            var mapAtSend = CurrentMap(pid);
            var ev = await SendAndWaitForCodeAsync(pid, () => { bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt); return ok; },
                new[] { "StorageObjectRemovedEvent", "StorageObjectUpdateEvent", "StorageObjectsRemovedEvent", "StorageObjectsUpdateEvent" },
                3000, p => p.Pid == pid && p.Name is not null && StorageOpen(pid) && CurrentMap(pid) == mapAtSend &&
                    MZP25BankReceipt.ConfirmWithdrawal(p.Name, p.Payload, st.Uid, gid, st.Qty, k, FieldOf) > 0, ct).ConfigureAwait(false);
            int confirmed = ev is null ? 0 : MZP25BankReceipt.ConfirmWithdrawal(
                ev.Name, ev.Payload, st.Uid, gid, st.Qty, k, FieldOf);
            if (confirmed <= 0)
            {
                TransportLog.Line($"[banque] pid {pid} : retrait gid {gid} x{k} (uid {st.Uid}) sans accusé corrélé -> arrêt.");
                break;
            }
            done += confirmed; want -= confirmed;
            // Publier l'accusé si le réducteur ne l'a pas encore fait, sans écraser un événement serveur plus récent.
            Bank.TryUpdateQuantity(pid, st.Uid, st.Qty, st.Qty - confirmed);
            TransportLog.Line($"[banque] pid {pid} : retrait gid {gid} x{confirmed}/{k} (uid {st.Uid}) confirmé par {ev!.Name}.");
            if (want > 0 && i + 1 < stacks.Count) await Task.Delay(Random.Shared.Next(250, 450), ct).ConfigureAwait(false);
        }
        return done;
    }

    /// <summary>DÉPOSE tout le SAC (position 63, les objets équipés sont exclus) SAUF les GID listés, en un paquet
    /// (ExchangeObjectTransferListFromInventoryRequest). Coffre OUVERT requis. (true, 0) si rien à déposer (aucun envoi).
    /// Retourne Ok uniquement quand les UID attendus sont confirmés absents du sac
    /// par un événement serveur décodé (ni l'envoi ni un événement coffre quelconque ne suffit).</summary>
    public async Task<(bool Ok, int Stacks)> BankDepositAllExceptAsync(uint pid, IReadOnlyCollection<long> exceptGids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return (false, 0);
        var relay = RelayForInject();
        if (relay is null) return (false, 0);
        if (!StorageOpen(pid)) { TransportLog.Line($"[banque] pid {pid} : dépôt sans coffre ouvert"); return (false, 0); }
        var uids = Inventory.Items(pid).Where(i => i.Position == 63 && !exceptGids.Contains(i.Gid) && ItemCanBank(pid, i.Uid)).Select(i => i.Uid).ToList();
        if (uids.Count == 0) return (true, 0);
        byte[] pkt = MoveForge.BuildBankDepositListFramed(uids);
        if (pkt.Length == 0) { TransportLog.Line($"[banque] pid {pid} : dépôt par liste NON forgé ({MoveForge.LastForgeError})."); return (false, 0); }
        var expected = new HashSet<int>(uids);
        var confirmed = new HashSet<int>();
        var gate = new object();
        long? mapAtSend = CurrentMap(pid);
        var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveDeposit(DecodedPacket p)
        {
            if (p.Pid != pid || p.ClientToServer || p.Name is null ||
                !StorageOpen(pid) || CurrentMap(pid) != mapAtSend) return;
            var seen = MZP25BankReceipt.DepositedBagUids(p.Name, p.Payload, FieldOf);
            if (seen.Count == 0) return;
            lock (gate)
            {
                foreach (int uid in seen) if (expected.Contains(uid)) confirmed.Add(uid);
                if (confirmed.Count == expected.Count) complete.TrySetResult(true);
            }
        }
        PacketDecoded += ObserveDeposit; // arm before send to avoid missing a fast response
        bool injected = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            injected = relay.Inject(pid, pkt);
            ShowInjected(pid, pkt);
            if (injected)
                await Task.WhenAny(complete.Task, Task.Delay(3000, ct)).ConfigureAwait(false);
        }
        finally { PacketDecoded -= ObserveDeposit; }
        int count;
        lock (gate) count = confirmed.Count;
        bool ok = injected && count == expected.Count;
        TransportLog.Line($"[banque] pid {pid} : dépôt UID confirmés {count}/{uids.Count}, inject={(injected ? "OK" : "ECHEC")}, résultat={(ok ? "confirmé" : "non confirmé")}.");
        return (ok, count);
    }

    /// <summary>Déplace des KAMAS entre le sac et le coffre OUVERT : signedQty &gt; 0 = dépôt, &lt; 0 = retrait
    /// (ExchangeMoveKamaRequest). true si le coffre accuse (StorageKamasUpdateEvent). Bloquant. Gaté Perm.Gather.</summary>
    public async Task<bool> BankMoveKamasAsync(uint pid, long signedQty, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null || signedQty == 0) return false;
        if (!StorageOpen(pid)) { TransportLog.Line($"[banque] pid {pid} : kamas sans coffre ouvert"); return false; }
        ct.ThrowIfCancellationRequested();
        long before = BankKamas(pid);
        if (MZP25BankReceipt.ExpectedKamas(before, signedQty) is not { } expected ||
            signedQty > 0 && (Kamas(pid) < 0 || signedQty > Kamas(pid))) return false;
        var mapAtSend = CurrentMap(pid);
        byte[] pkt = MoveForge.BuildExchangeMoveKamaFramed(signedQty);
        if (pkt.Length == 0) { TransportLog.Line($"[banque] pid {pid} : kamas NON forgé ({MoveForge.LastForgeError})."); return false; }
        var ev = await SendAndWaitForCodeAsync(pid, () => { bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt); return ok; },
            new[] { "StorageKamasUpdateEvent" }, 3000,
            p => StorageOpen(pid) && CurrentMap(pid) == mapAtSend && !_chestSession.ContainsKey(pid) &&
                MZP25BankReceipt.ConfirmKamas(p.Payload, expected, FieldOf), ct).ConfigureAwait(false);
        if (ev is null) TransportLog.Line($"[banque] pid {pid} : kamas {signedQty:+#;-#} sans confirmation du coffre (champ kamas à valider en jeu)");
        else
        {
            // Le waiter précède le réducteur, mais une notification plus récente doit toujours faire foi.
            if (_bankKamas.TryUpdate(pid, expected, before)) BankUpdated?.Invoke(pid);
            TransportLog.Line($"[banque] pid {pid} : kamas {signedQty:+#;-#} accusés -> coffre {expected}");
        }
        return ev is not null;
    }
}
