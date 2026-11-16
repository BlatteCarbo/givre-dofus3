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

// ===================== Échange joueur (trade P2P) + Lot D : état d'échange, verbes bloquants, mode mule d'équipe =====================
public sealed partial class LaunchService
{
    // ===================== ÉCHANGE JOUEUR (trade P2P) =====================
    // targetId = id d'ACTEUR ROLEPLAY du joueur cible sur la carte courante (résolu côté GivreLuaHost via le store
    // d'acteurs, PAS un PlayerSearch serveur). Gaté Perm.Gather : c'est une manipulation d'inventaire, au même titre
    // que le dépôt banque / le drop (un script qui peut déjà déposer/jeter peut déjà déplacer des items). count<=0 =
    // tout le stack. ⚠️ MVP émission : l'état « échange conclu » n'est pas encore câblé côté réception (cf. audit YELLOW).

    /// <summary>Le bot `pid` DEMANDE un échange au joueur `targetId` (acteur roleplay sur la carte). false si non forgeable / pas de relais.</summary>
    public bool TradeWithPlayer(uint pid, long targetId)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null || targetId == 0) return false;
        byte[] pkt = MoveForge.BuildExchangePlayerRequestFramed(targetId);
        if (pkt.Length == 0) { TransportLog.Line($"[échange] demande NON forgée pid {pid} -> acteur {targetId} : {MoveForge.LastForgeError}"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[échange] demande envoyée pid {pid} -> acteur {targetId} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Pose (+) `count` d'un objet par GID dans l'échange ouvert (count&lt;=0 = tout le stack).</summary>
    public bool TradePutItem(uint pid, long gid, int count) => TradeMoveItem(pid, gid, count, remove: false);
    /// <summary>Retire (−) `count` d'un objet par GID de l'échange ouvert (count&lt;=0 = tout le stack).</summary>
    public bool TradeRemoveItem(uint pid, long gid, int count) => TradeMoveItem(pid, gid, count, remove: true);

    private bool TradeMoveItem(uint pid, long gid, int count, bool remove)
    {
        var session = _tradeStartedPkt.GetValueOrDefault(pid);
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        // POSE : piles du SAC seulement (jamais l'exemplaire équipé), quantité propre à chaque pile (un équipement = 1 uid par
        // exemplaire). RETRAIT : seulement ce qui est POSÉ dans l'échange (uid + quantité accusés par le serveur) ; repli sur
        // les piles du sac uniquement si ce registre est inconnu (aucun accusé de notre côté décodé pour cet échange).
        List<(int Uid, int Qty)>? stacks = null;
        if (remove)
        {
            if (!SettleTradeStep(pid)) return false;   // no removal from an unconfirmed placement ledger
            stacks = PlacedStacks(pid, gid);
            if (stacks is null) { TransportLog.Line($"[échange] pid {pid} : retrait refusé, contenu local non confirmé."); return false; }
            if (stacks is { Count: 0 }) { TransportLog.Line($"[échange] pid {pid} : objet gid {gid} pas posé dans l'échange -> rien à retirer."); return false; }
        }
        stacks ??= BagStacks(pid, gid).Where(i => remove || ItemCanTrade(pid, i.Uid)).Select(i => (i.Uid, i.Qty)).ToList();
        if (stacks.Count == 0) { TransportLog.Line($"[échange] pid {pid} : objet gid {gid} absent du sac."); return false; }
        int want = count > 0 ? count : (int)Math.Min(int.MaxValue, stacks.Sum(i => (long)i.Qty));
        if (!SettleTradeStep(pid)) return false;
        bool any = false;
        foreach (var st in stacks)
        {
            if (want <= 0) break;
            if (!SettleTradeStep(pid)) return any;
            int qty = Math.Min(want, st.Qty);
            byte[] pkt = MoveForge.BuildExchangeObjectMoveSignedFramed(st.Uid, remove ? -qty : qty);
            if (pkt.Length == 0) { TransportLog.Line($"[échange] pid {pid} : move gid {gid} NON forgé ({MoveForge.LastForgeError})."); return any; }
            bool ok = SendTradeItemMutation(pid, session, st.Uid, remove ? -qty : qty, () => relay.Inject(pid, pkt));
            ShowInjected(pid, pkt);
            TransportLog.Line($"[échange] pid {pid} : {(remove ? "retire" : "pose")} gid {gid} x{qty} (uid {st.Uid}) inject={(ok ? "OK" : "ECHEC")}");
            if (!ok) return any;
            any = true; want -= qty;
        }
        return any;
    }

    /// <summary>Piles d'un GID DANS LE SAC (position 63, quantité &gt; 0), plus grosses d'abord.</summary>
    private List<InventoryStore.Item> BagStacks(uint pid, long gid)
        => Inventory.Items(pid).Where(i => i.Gid == gid && i.Position == 63 && i.Qty > 0).OrderByDescending(i => i.Qty).ToList();

    /// <summary>Piles d'un GID POSÉES par ce bot dans l'échange ouvert (uid, quantité dans l'échange), plus grosses d'abord.
    /// null = registre INCONNU (aucun accusé de notre côté décodé depuis l'ouverture) ; vide = rien de ce GID dans l'échange.</summary>
    private List<(int Uid, int Qty)>? PlacedStacks(uint pid, long gid)
    {
        if (!_tradePlaced.TryGetValue(pid, out var led) || !led.Seen) return null;
        return led.ByUid.Where(kv => kv.Value.Gid == gid && kv.Value.Qty > 0)
                        .Select(kv => (Uid: kv.Key, kv.Value.Qty)).OrderByDescending(x => x.Qty).ToList();
    }

    /// <summary>Ajoute (+) ou retire (−) `amount` kamas de l'échange ouvert.</summary>
    public bool TradePutKamas(uint pid, long amount) => TradePutKamasCore(pid, amount, waitForContent: true);

    private bool TradePutKamasCore(uint pid, long amount, bool waitForContent)
    {
        var session = _tradeStartedPkt.GetValueOrDefault(pid);
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null || amount == 0) return false;
        byte[] pkt = MoveForge.BuildExchangeMoveKamaFramed(amount);
        if (pkt.Length == 0) { TransportLog.Line($"[échange] pid {pid} : kamas NON forgé ({MoveForge.LastForgeError})."); return false; }
        if (!SettleTradeStep(pid, waitForContent ? 1500 : 0) || !_tradeContent.ContainsKey(pid)) return false;
        long target = 0;
        bool ok = SendTradeMutation(pid, session, p => p.Name == "ExchangeKamaModifiedEvent"
            && !PayloadReader.Bool(p.Payload, FNum(p.Name, "remote"))
            && PayloadReader.Int(p.Payload, FNum(p.Name, "quantity"), -1) == target, () =>
            {
                if (!_tradeContent.TryGetValue(pid, out var latest)) return false;
                try { target = checked(latest.MyKamas + amount); } catch (OverflowException) { return false; }
                return target >= 0 && relay.Inject(pid, pkt);
            });
        ShowInjected(pid, pkt);
        TransportLog.Line($"[échange] pid {pid} : kamas {amount:+#;-#;0} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Bascule l'état PRÊT de l'échange côté ce bot (ready=true valide ; les DEUX prêts -> échange conclu).</summary>
    public bool TradeSetReady(uint pid, bool ready)
        => TradeSetReadyForSession(pid, ready, _tradeStartedPkt.GetValueOrDefault(pid), false, waitForContent: false);

    private bool TradeSetReadyForSession(uint pid, bool ready, DecodedPacket? session, bool automatic, bool waitForContent = true)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        // Lot D : step = compteur de changements de contenu de l'échange (sémantique D2, cf. craft « step = ingrédients
        // posés ») au lieu de 0. Non validé en jeu (DEFERRED) : un refus de « prêt » se tranche sur UNE capture.
        // Attend l'accusé serveur des poses/retraits qu'on vient d'injecter (sinon step lu trop tôt = 0 au lieu de 1).
        if (!TradeOpen(pid)) return false;
        int step = _tradeStep.TryGetValue(pid, out var st) ? st : 0;
        byte[] pkt = MoveForge.BuildExchangeReadyToggleFramed(ready, step);
        if (pkt.Length == 0) return false;
        bool Send()
        {
            step = _tradeStep.GetValueOrDefault(pid);
            pkt = MoveForge.BuildExchangeReadyToggleFramed(ready, step);
            return relay.Inject(pid, pkt);
        }
        bool ok = ready && waitForContent
            ? SettleAndSendTradeReady(pid, session, automatic, Send)
            : SendTradeReady(pid, session, automatic, Send, requireSettled: ready);
        if (ok) ShowInjected(pid, pkt);
        TransportLog.Line($"[échange] pid {pid} : prêt={ready} (step {step}) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>ACCEPTE une demande d'échange reçue (ExchangeAcceptRequest vide).</summary>
    public bool TradeAccept(uint pid)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildExchangeAcceptFramed();
        if (pkt.Length == 0) return false;
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[échange] pid {pid} : accepte la demande inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>ANNULE / FERME l'échange en cours (DialogLeaveRequest). Non gaté : fermer un dialogue est toujours permis.</summary>
    public bool TradeCancel(uint pid)
    {
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildDialogLeaveFramed();
        if (pkt.Length == 0) return false;
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[échange] pid {pid} : annule/ferme inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ===================== Lot D : ÉCHANGE — état reçu + verbes BLOQUANTS + mode mule d'équipe =====================
    // Tout l'état est écrit par les décodeurs (thread réseau) dans des ConcurrentDictionary et lu par le thread du script.
    // ⚠ _codeWait ne tient qu'UN waiter par pid : le code AUTOMATIQUE (auto-accept / auto-ready) n'appelle JAMAIS
    // SendAndWaitForCodeAsync (il écraserait en silence un trade:ready() en attente sur le thread du script) : il injecte seulement.
    private readonly ConcurrentDictionary<uint, (long PartnerId, long MapId)> _tradeOpen = new();
    private readonly ConcurrentDictionary<uint, int> _tradeStep = new();
    // Local intentions are separate from the global wire step, which includes remote changes.
    private readonly ConcurrentDictionary<uint, List<Func<DecodedPacket, bool>>> _tradeExpected = new();
    // Dernier ExchangeStartedWithPodsEvent APPLIQUÉ (idempotence : appliqué au point d'accusé par le verbe bloquant PUIS
    // revu par le décodeur dans le même OnDecoded -> une seule application, sinon le reset effacerait une pose déjà faite).
    private readonly ConcurrentDictionary<uint, DecodedPacket> _tradeStartedPkt = new();
    private readonly ConcurrentDictionary<uint, (long SourceId, long AtTick, int Type)> _tradePending = new();   // Type = exchange_type (journal + trade:pending)
    private readonly ConcurrentDictionary<uint, bool> _tradeAutoTeam = new();   // pid -> AutoReady
    private const long TradePendingTtlMs = 60_000;
    // Objets POSÉS par ce bot dans l'échange ouvert (uid -> gid, quantité DANS l'échange), reconstruits depuis les accusés
    // serveur Exchange*Event de NOTRE côté (remote absent = false en proto3). Neuf à l'ouverture, oublié à la fermeture.
    // Seen = au moins un accusé de notre côté décodé : sinon registre inconnu (retrait -> repli sur les piles du sac).
    private sealed class TradePlacedLedger
    {
        public readonly ConcurrentDictionary<int, (long Gid, int Qty)> ByUid = new();
        public volatile bool Seen;
    }
    private readonly ConcurrentDictionary<uint, TradePlacedLedger> _tradePlaced = new();

    // Reserve before Inject: a synchronous transport receipt must see the intention.
    // Session lock also serializes local sends and the final authorization check.
    private bool SendTradeMutation(uint pid, DecodedPacket? session, Func<DecodedPacket, bool> receipt, Func<bool> send)
    {
        lock (_tradeStartedPkt)
        {
            if (!TradeOpen(pid) || session is null || !ReferenceEquals(session, _tradeStartedPkt.GetValueOrDefault(pid))) return false;
            var pending = _tradeExpected.GetOrAdd(pid, _ => new());
            if (pending.Count != 0) return false; // do not register overlapping mutations from stale content
            pending.Add(receipt);
            bool ok = false;
            try { return ok = send(); }
            finally { if (!ok) pending.Remove(receipt); }
        }
    }

    private bool SendTradeItemMutation(uint pid, DecodedPacket? session, int uid, int signedQuantity, Func<bool> send)
    {
        long target = 0;
        return SendTradeMutation(pid, session, p => target > 0
            ? WorkshopPlacementAck(p, uid, (int)target) && p.Name is "ExchangeObjectsAddedEvent" or "ExchangeObjectsModifiedEvent"
            : !PayloadReader.Bool(p.Payload, FNum(p.Name, "remote")) &&
              (p.Name == "ExchangeObjectRemovedEvent" && PayloadReader.Int(p.Payload, FNum(p.Name, "object_uid"), -1) == uid
               || p.Name == "ExchangeObjectsRemovedEvent" && InventoryReader.ReadUids(p.Payload, FNum(p.Name, "objects_uid")).Contains(uid)),
            () =>
            {
                target = (long)(_tradePlaced.TryGetValue(pid, out var ledger) ? ledger.ByUid.GetValueOrDefault(uid).Qty : 0) + signedQuantity;
                return target is >= 0 and <= int.MaxValue && send();
            });
    }

    private void ConfirmTradeMutation(DecodedPacket p)
    {
        if (!_tradeExpected.TryGetValue(p.Pid, out var pending)) return;
        // One event may contain several objects, but each intention is matched exactly.
        pending.RemoveAll(match =>
        {
            try { return match(p); }
            catch { return false; } // malformed receipt remains unconfirmed; keep decoding
        });
    }

    // Never validate partial content after timeout, closure, or replacement of the exchange.
    private bool SettleTradeStep(uint pid, int timeoutMs = 1500)
    {
        if (!TradeOpen(pid) || !_tradeStartedPkt.TryGetValue(pid, out var session)) return false;
        long until = Environment.TickCount64 + Math.Max(0, timeoutMs);
        while (true)
        {
            if (!TradeOpen(pid) || !_tradeStartedPkt.TryGetValue(pid, out var current)
                || !ReferenceEquals(session, current)) return false;
            lock (_tradeStartedPkt)
            {
                if (!ReferenceEquals(session, _tradeStartedPkt.GetValueOrDefault(pid))) return false;
                if (!_tradeExpected.TryGetValue(pid, out var pending) || pending.Count == 0) return true;
            }
            if (Environment.TickCount64 >= until)
            {
                TransportLog.Line($"[échange] pid {pid} : contenu non confirmé -> validation/retrait ignoré.");
                return false;
            }
            Thread.Sleep(40);
        }
    }

    // Applique l'ouverture d'échange (ExchangeStartedWithPodsEvent) UNE fois par paquet : appelé par les verbes bloquants
    // AU POINT D'ACCUSÉ (le waiter est résolu avant le décodeur dans OnDecoded) puis par le décodeur (no-op si déjà fait).
    private void ApplyTradeStarted(DecodedPacket p)
    {
        lock (_tradeStartedPkt)
        {
            if (_tradeStartedPkt.TryGetValue(p.Pid, out var last) && ReferenceEquals(last, p)) return;
            _tradeStartedPkt[p.Pid] = p;
            long a = PayloadReader.Int(p.Payload, FNum("ExchangeStartedWithPodsEvent", "first_character_id"), 0);
            long b = PayloadReader.Int(p.Payload, FNum("ExchangeStartedWithPodsEvent", "second_character_id"), 0);
            long me = CharacterId(p.Pid);
            long partner = a == me ? b : a;
            _tradeOpen[p.Pid] = (partner, CurrentMap(p.Pid) ?? 0);
            _tradeStep[p.Pid] = 0;
            _tradeExpected.TryRemove(p.Pid, out _);
            _tradePending.TryRemove(p.Pid, out _);
            _tradePlaced[p.Pid] = new TradePlacedLedger();   // nouvel échange : rien de posé
            _tradeContent[p.Pid] = new TradeContent();       // fenêtre d'interaction : les deux côtés, vides
            TransportLog.Line($"[échange] pid {p.Pid} : échange ouvert (partenaire {partner})");
        }
    }

    /// <summary>Vrai si un échange joueur est ouvert pour ce bot sur la carte courante. Lecture pure.</summary>
    public bool TradeOpen(uint pid) => _tradeOpen.TryGetValue(pid, out var t) && t.MapId == (CurrentMap(pid) ?? 0);
    /// <summary>characterId du partenaire de l'échange ouvert (0 = aucun échange).</summary>
    public long TradePartner(uint pid) => TradeOpen(pid) && _tradeOpen.TryGetValue(pid, out var t) ? t.PartnerId : 0;
    /// <summary>Demande d'échange REÇUE en attente (id de l'émetteur + âge), ou null. Les demandes de plus de 60 s sont oubliées.</summary>
    public (long SourceId, long AgeMs, int Type)? TradePending(uint pid)
    {
        if (!_tradePending.TryGetValue(pid, out var p)) return null;
        long age = Environment.TickCount64 - p.AtTick;
        if (age > TradePendingTtlMs) { _tradePending.TryRemove(new KeyValuePair<uint, (long, long, int)>(pid, p)); return null; }
        return (p.SourceId, age, p.Type);
    }

    /// <summary>DEMANDE un échange au joueur `targetId` et ATTEND l'ouverture (true), un refus/erreur/échec d'envoi (false)
    /// ou le délai (null : la demande est alors annulée par DialogLeaveRequest). Bloquant. Gaté Perm.Gather.</summary>
    public async Task<bool?> TradeWithPlayerAsync(uint pid, long targetId, int timeoutMs, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null || targetId == 0) return false;
        byte[] pkt = MoveForge.BuildExchangePlayerRequestFramed(targetId);
        if (pkt.Length == 0) { TransportLog.Line($"[échange] demande NON forgée pid {pid} -> acteur {targetId} : {MoveForge.LastForgeError}"); return false; }
        bool injected = false;
        var ev = await SendAndWaitForCodeAsync(pid, () => { injected = relay.Inject(pid, pkt); ShowInjected(pid, pkt); return injected; },
            new[] { "ExchangeStartedWithPodsEvent", "ExchangeLeaveEvent", "ExchangeErrorEvent" }, timeoutMs, ct).ConfigureAwait(false);
        if (!injected) { TransportLog.Line($"[échange] pid {pid} : demande à {targetId} non injectée."); return false; }
        switch (ev?.Name)
        {
            case "ExchangeStartedWithPodsEvent":
                ApplyTradeStarted(ev);   // état écrit AVANT le retour au script (exchange:putItem juste après voit TradeOpen)
                TransportLog.Line($"[échange] pid {pid} : échange ouvert avec {targetId}.");
                return true;
            case "ExchangeErrorEvent":
                TransportLog.Line($"[échange] pid {pid} : demande à {targetId} refusée par le serveur (erreur {PayloadReader.Int(ev.Payload, FNum("ExchangeErrorEvent", "error_type"), 0)}).");
                return false;
            case "ExchangeLeaveEvent":
                TransportLog.Line($"[échange] pid {pid} : demande à {targetId} refusée.");
                return false;
            default:
                if (ct.IsCancellationRequested) return false;
                TransportLog.Line($"[échange] pid {pid} : pas de réponse de {targetId} -> demande annulée.");
                var leave = MoveForge.BuildDialogLeaveFramed();
                if (leave.Length > 0) relay.Inject(pid, leave);
                return null;
        }
    }

    /// <summary>Se déclare PRÊT (step = compteur de contenu) et ATTEND la fin de l'échange : true = conclu, false = annulé/échoué,
    /// null = délai dépassé (l'échange reste ouvert). Bloquant. Gaté Perm.Gather.</summary>
    public async Task<bool?> TradeReadyAndWaitAsync(uint pid, int timeoutMs, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        // Aucun échange joueur ouvert (withPlayer raté, ou seule la banque est ouverte) : rien à valider -> false tout de
        // suite au lieu d'injecter un « prêt » orphelin et de bloquer le script jusqu'au délai.
        if (!TradeOpen(pid)) { TransportLog.Line($"[échange] pid {pid} : prêt demandé sans échange ouvert -> ignoré."); return false; }
        var session = _tradeStartedPkt.GetValueOrDefault(pid);
        if (!SettleTradeStep(pid)) return false;
        int step = _tradeStep.TryGetValue(pid, out var st) ? st : 0;
        byte[] pkt = MoveForge.BuildExchangeReadyToggleFramed(true, step);
        if (pkt.Length == 0) return false;
        bool injected = false;
        var ev = await SendAndWaitForCodeAsync(pid, () => { injected = !ct.IsCancellationRequested && SendTradeReady(pid, session, false, () => relay.Inject(pid, MoveForge.BuildExchangeReadyToggleFramed(true, _tradeStep.GetValueOrDefault(pid)))); ShowInjected(pid, pkt); return injected; },
            new[] { "ExchangeLeaveEvent" }, timeoutMs, ct).ConfigureAwait(false);
        TransportLog.Line($"[échange] pid {pid} : prêt=True (step {step}) inject={(injected ? "OK" : "ECHEC")}");
        if (!injected) return false;
        if (ev is null) return null;
        // proto3 OMET false : champ success absent = échange annulé, pas « inconnu ».
        return PayloadReader.Bool(ev.Payload, FNum("ExchangeLeaveEvent", "success"));
    }

    /// <summary>ACCEPTE la demande d'échange reçue et attend l'ouverture (true) ou un refus/erreur/délai (false). Bloquant. Gaté Perm.Gather.</summary>
    public async Task<bool> TradeAcceptAsync(uint pid, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildExchangeAcceptFramed();
        if (pkt.Length == 0) return false;
        var ev = await SendAndWaitForCodeAsync(pid, () => { bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt); return ok; },
            new[] { "ExchangeStartedWithPodsEvent", "ExchangeLeaveEvent", "ExchangeErrorEvent" }, 5000, ct).ConfigureAwait(false);
        TransportLog.Line($"[échange] pid {pid} : accepte la demande -> {ev?.Name ?? "sans réponse"}");
        if (ev is { Name: "ExchangeStartedWithPodsEvent" }) { ApplyTradeStarted(ev); return true; }
        return false;
    }

    /// <summary>Mode MULE d'équipe : accepte automatiquement les demandes d'échange des AUTRES bots de l'équipe (match par
    /// characterId, jamais par nom) et, si autoReady, se déclare prêt quand le partenaire d'équipe l'est. Activer exige
    /// Perm.Multi + Perm.Gather (false sinon) ; désactiver est toujours permis.</summary>
    public bool SetTradeAutoAcceptTeam(uint pid, bool on, bool autoReady = true)
    {
        lock (_tradeStartedPkt)
        {
        if (!on) { _tradeAutoTeam.TryRemove(pid, out _); return true; }
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi) || !LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        _tradeAutoTeam[pid] = autoReady;
        return true;
        }
    }
    /// <summary>Vrai si le mode mule d'équipe est actif pour ce bot (le watchdog anti-blocage le considère sain).</summary>
    public bool TradeAutoAcceptTeamOn(uint pid) => _tradeAutoTeam.ContainsKey(pid);
    // Un AUTRE bot GivreClone de l'équipe de `pid` porte ce characterId. Match par id (robuste aux accents/apostrophes).
    private bool IsTeamCharacter(uint pid, long charId) => charId != 0 && TeamMembers(pid).Any(m => m.Pid != pid && CharacterId(m.Pid) == charId);

    private bool SettleAndSendTradeReady(uint pid, DecodedPacket? session, bool automatic, Func<bool> send)
    {
        if (!ReferenceEquals(session, _tradeStartedPkt.GetValueOrDefault(pid)) || !SettleTradeStep(pid)) return false;
        return SendTradeReady(pid, session, automatic, send);
    }

    private bool SendTradeReady(uint pid, DecodedPacket? session, bool automatic, Func<bool> send, bool requireSettled = true)
    {
        lock (_tradeStartedPkt)
        {
            if (session is null || !TradeOpen(pid) || !ReferenceEquals(session, _tradeStartedPkt.GetValueOrDefault(pid))
                || automatic && !CanAutoReadyTrade(pid, session)
                || requireSettled && _tradeExpected.TryGetValue(pid, out var pending) && pending.Count != 0) return false;
            return send(); // Injection is not a server Ready confirmation.
        }
    }

    private bool CanAutoReadyTrade(uint pid, DecodedPacket? session)
        => _tradeAutoTeam.TryGetValue(pid, out bool ready) && ready && TradeOpen(pid)
            && session is not null && ReferenceEquals(session, _tradeStartedPkt.GetValueOrDefault(pid));

    // Décodeurs d'ÉCHANGE (thread réseau) : n'écrivent QUE dans les ConcurrentDictionary ci-dessus ; l'automatique mule passe
    // par Task.Run + inject (jamais de Lua, jamais de _codeWait).
    private void OnExchangePacket(DecodedPacket p)
    {
        lock (_tradeStartedPkt)
        {
        switch (p.Name)
        {
            case "ExchangeRequestedTradeEvent":
            {
                long src = PayloadReader.Int(p.Payload, FNum("ExchangeRequestedTradeEvent", "source_id"), 0);
                long tgt = PayloadReader.Int(p.Payload, FNum("ExchangeRequestedTradeEvent", "target_id"), 0);
                long type = PayloadReader.Int(p.Payload, FNum("ExchangeRequestedTradeEvent", "exchange_type"), 0);
                long me = CharacterId(p.Pid);
                if (me == 0) { TransportLog.Line($"[échange] demande pid {p.Pid} ignorée : characterId inconnu"); break; }
                if (tgt != me || src == me || src == 0) break;   // écho de NOTRE propre demande
                _tradePending[p.Pid] = (src, Environment.TickCount64, (int)type);
                TransportLog.Line($"[échange] demande reçue pid {p.Pid} de {src} (type {type})");
                if (_tradeAutoTeam.ContainsKey(p.Pid) && IsTeamCharacter(p.Pid, src))
                {
                    uint pid = p.Pid;
                    TeamLog($"[échange] mule pid {pid} : demande de {src} (équipe) acceptée auto");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(Random.Shared.Next(600, 1400)).ConfigureAwait(false);
                        if (!LicenseService.HasPermission(LicenseService.Perm.Multi) || !_tradeAutoTeam.ContainsKey(pid)) return;
                        if (TradePending(pid) is { SourceId: var s } && s == src) TradeAccept(pid);   // TradeAccept gate Perm.Gather
                    });
                }
                else MaybeAutoRefuseTrade(p.Pid, src);   // Lot E : IGNORE_REQUEST_EXCHANGE (script en cours, émetteur hors app)
                break;
            }
            case "ExchangeStartedWithPodsEvent":
                ApplyTradeStarted(p);   // no-op si le verbe bloquant l'a déjà appliqué (même paquet)
                break;
            case "ExchangeObjectsAddedEvent":
            case "ExchangeObjectRemovedEvent":
            case "ExchangeObjectsRemovedEvent":
            case "ExchangeObjectsModifiedEvent":
            case "ExchangeKamaModifiedEvent":
                if (TradeOpen(p.Pid))
                {
                    ConfirmTradeMutation(p);
                    TrackTradePlaced(p);   // AVANT le step : SettleTradeStep rattrapé => registre des poses à jour
                    TrackTradeContent(p);  // fenêtre d'interaction : contenu des deux côtés (+ « prêt » annulés)
                    _tradeStep.AddOrUpdate(p.Pid, 1, static (_, v) => v + 1);
                }
                break;
            case "ExchangeReadyEvent":
            {
                long cid = PayloadReader.Int(p.Payload, FNum("ExchangeReadyEvent", "character_id"), 0);
                bool rdy = PayloadReader.Bool(p.Payload, FNum("ExchangeReadyEvent", "ready"));   // proto3 : absent = false
                TrackTradeContent(p);   // fenêtre d'interaction : état « prêt » de chaque côté
                if (rdy && _tradeAutoTeam.TryGetValue(p.Pid, out var autoReady) && autoReady
                    && cid != 0 && cid == TradePartner(p.Pid) && IsTeamCharacter(p.Pid, cid))
                {
                    uint pid = p.Pid;
                    var session = _tradeStartedPkt.GetValueOrDefault(pid);
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(Random.Shared.Next(500, 1000)).ConfigureAwait(false);
                        if (!CanAutoReadyTrade(pid, session)) return;
                        TradeSetReadyForSession(pid, true, session, true);
                    });
                }
                break;
            }
            case "ExchangeLeaveEvent":
                _tradeOpen.TryRemove(p.Pid, out _);
                _tradeStep.TryRemove(p.Pid, out _);
                _tradeExpected.TryRemove(p.Pid, out _);
                _tradePending.TryRemove(p.Pid, out _);
                _tradePlaced.TryRemove(p.Pid, out _);
                _tradeContent.TryRemove(p.Pid, out _);
                _storageOpenMap.TryRemove(p.Pid, out _);
                break;
        }
    }

    }

    // Registre des objets posés (thread réseau) : Added/Modified portent la quantité DANS l'échange de chaque uid, Removed
    // l'en retire. Côté partenaire (remote=true) ignoré. Champs résolus PAR NOM. Jamais d'exception sur le thread réseau.
    private void TrackTradePlaced(DecodedPacket p)
    {
        try
        {
            if (!_tradePlaced.TryGetValue(p.Pid, out var led)) return;
            switch (p.Name)
            {
                case "ExchangeObjectsAddedEvent":
                case "ExchangeObjectsModifiedEvent":
                {
                    bool added = p.Name == "ExchangeObjectsAddedEvent";
                    string msg = added ? "ExchangeObjectsAddedEvent" : "ExchangeObjectsModifiedEvent";
                    if (PayloadReader.Bool(p.Payload, FNum(msg, "remote"))) return;
                    foreach (var it in InventoryReader.ReadContentAt(p.Payload, FNum(msg, "objects")))
                    {
                        if (it.Qty > 0) led.ByUid[it.Uid] = (it.Gid, it.Qty); else led.ByUid.TryRemove(it.Uid, out _);
                        led.Seen = true;
                    }
                    break;
                }
                case "ExchangeObjectRemovedEvent":
                    if (PayloadReader.Bool(p.Payload, FNum("ExchangeObjectRemovedEvent", "remote"))) return;
                    led.ByUid.TryRemove((int)PayloadReader.Int(p.Payload, FNum("ExchangeObjectRemovedEvent", "object_uid"), 0), out _);
                    led.Seen = true;
                    break;
                case "ExchangeObjectsRemovedEvent":
                    if (PayloadReader.Bool(p.Payload, FNum("ExchangeObjectsRemovedEvent", "remote"))) return;
                    foreach (int u in InventoryReader.ReadUids(p.Payload, FNum("ExchangeObjectsRemovedEvent", "objects_uid"))) led.ByUid.TryRemove(u, out _);
                    led.Seen = true;
                    break;
            }
        }
        catch { /* registre best-effort : ne casse jamais le décodage */ }
    }
}
