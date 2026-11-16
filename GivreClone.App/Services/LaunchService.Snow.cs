using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Compatibilité tiers (moteur): actions de compte, instantané des comptes, historique de paquets =====================
// Écrit depuis le thread réseau (OnDecoded -> SnowObserve) ou le thread script (files) : tout est concurrent-safe. Les files sont
// drainées par BotsViewModel.Tick (thread UI), seul autorisé à lancer / arrêter un compte.
public sealed partial class LaunchService
{
    // ---------------------------------------------------------------- actions de compte demandées par un script
    /// <summary>Genre d'action : Disconnect (déconnecte, relance après <c>DelayMs</c> si &gt; 0), Launch / Stop (compte désigné
    /// par <c>Target</c> : alias, perso ou identifiant saisi par le script — jamais renvoyé au script).</summary>
    public enum ScriptAccountActionKind { Disconnect, Launch, Stop }
    public sealed record ScriptAccountAction(uint FromPid, ScriptAccountActionKind Kind, string? Target, long DelayMs, bool IncludeTeam);
    private readonly ConcurrentQueue<ScriptAccountAction> _accountActions = new();

    /// <summary>Enfile une action de compte (global:disconnect / reconnect*, givreController:loadAnAccount / unloadAnAccount).
    /// Résolue par le VM au Tick. false si la file est pleine.</summary>
    public bool EnqueueAccountAction(ScriptAccountAction a)
    {
        if (_accountActions.Count >= 16) return false;
        _accountActions.Enqueue(a);
        return true;
    }
    public bool TryDequeueAccountAction(out ScriptAccountAction a) => _accountActions.TryDequeue(out a!);

    // ---------------------------------------------------------------- instantané des comptes (poussé par le VM)
    /// <summary>Un compte enregistré, vu par les scripts. <c>Alias</c> = libellé du compte (sinon nom du perso) : l'identifiant
    /// de connexion n'est JAMAIS exposé au script, il ne sert qu'à reconnaître un nom saisi par le script.</summary>
    public sealed record ScriptAccountInfo(string Id, string Alias, string Character, uint Pid, bool Loaded, string LoginKey);
    private volatile IReadOnlyList<ScriptAccountInfo> _accountsSnap = Array.Empty<ScriptAccountInfo>();

    public void SetAccountsSnapshot(IReadOnlyList<ScriptAccountInfo> snap) => _accountsSnap = snap;
    public IReadOnlyList<ScriptAccountInfo> ScriptAccounts => _accountsSnap;

    /// <summary>Compte désigné par un nom de script : alias, perso ou identifiant de connexion (comparaison sans casse).</summary>
    public ScriptAccountInfo? FindScriptAccount(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string n = name.Trim();
        var snap = _accountsSnap;
        return snap.FirstOrDefault(a => string.Equals(a.Alias, n, StringComparison.OrdinalIgnoreCase))
            ?? snap.FirstOrDefault(a => string.Equals(a.Character, n, StringComparison.OrdinalIgnoreCase))
            ?? snap.FirstOrDefault(a => a.LoginKey.Length > 0 && string.Equals(a.LoginKey, n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Alias du compte qui pilote <paramref name="pid"/> ("" si inconnu).</summary>
    public string AccountAliasOf(uint pid) => _accountsSnap.FirstOrDefault(a => a.Pid == pid && pid != 0)?.Alias ?? "";

    // ---------------------------------------------------------------- dernier paquet reçu par nom + burst de connexion
    private const int BurstMaxPackets = 2000;
    private const long BurstMaxBytes = 4L * 1024 * 1024;
    private sealed class BurstRecorder
    {
        public readonly object Gate = new();
        public readonly List<DecodedPacket> Packets = new();
        public long Bytes;
        public bool Done;
    }
    private readonly ConcurrentDictionary<(uint Pid, string Name), DecodedPacket> _lastByName = new();
    private readonly ConcurrentDictionary<uint, BurstRecorder> _burst = new();

    /// <summary>Dernière instance REÇUE (serveur -&gt; client) du message <paramref name="name"/> pour ce pid, ou null.
    /// Jamais un message porteur de secret (jetons, tickets, clés d'API…) : ils ne sont pas mémorisés, et refusés ici aussi.</summary>
    public DecodedPacket? LastReceived(uint pid, string name)
        => !LuaSnowCompat.IsSensitiveMessage(name) && _lastByName.TryGetValue((pid, name), out var p) ? p : null;

    /// <summary>Paquets reçus de la connexion au serveur de jeu jusqu'au premier chargement de carte (le « snapshot » de
    /// connexion : caractéristiques, inventaire, métiers, quêtes…), filtrés par nom, du plus ancien au plus récent.
    /// Vide pour un message porteur de secret (jamais enregistré, refusé ici aussi).</summary>
    public IReadOnlyList<DecodedPacket> LoginBurst(uint pid, string name)
    {
        if (LuaSnowCompat.IsSensitiveMessage(name) || !_burst.TryGetValue(pid, out var r)) return Array.Empty<DecodedPacket>();
        lock (r.Gate) return r.Packets.Where(p => p.Name == name).ToList();
    }

    // Thread réseau, EN TÊTE de OnDecoded (avant la résolution des attentes _codeWait : un script réveillé par la question
    // d'un PNJ doit trouver le dialogue déjà mémorisé — sinon un replyNpc enchaîné lisait les réponses de la question d'avant).
    private void SnowObserve(DecodedPacket p)
    {
        try
        {
            if (p.ClientToServer || p.Name is not { } name) return;
            switch (name)
            {
                case "NpcDialogQuestionEvent": _lastNpcDialog[p.Pid] = p.Payload; _npcDialogSeq.AddOrUpdate(p.Pid, 1, (_, v) => v + 1); break;
                case "DialogLeaveEvent": _lastNpcDialog.TryRemove(p.Pid, out _); _npcDialogSeq.AddOrUpdate(p.Pid, 1, (_, v) => v + 1); break;
                case "ObjectAveragePricesEvent": ReadAveragePrices(p.Pid, p.Payload); break;
                case "ExchangeBidSellerStartedEvent": ReadHdvMaxSlots(p.Pid, p.Payload); break;
                case "ExchangeCraftStartedEvent": _craftOpen[p.Pid] = 0; break;   // pas posés = 0
                case "ExchangeLeaveEvent": _craftOpen.TryRemove(p.Pid, out _); break;
                // Sélection de perso RÉUSSIE (nom Dofus 3 ; « CharacterSelectedSuccess » n'existe plus) : oneof result, sous-message
                // success présent = réussite. Autre perso : sa chasse arrivera à part ; burst déjà clos = changement de perso en
                // jeu (pas le 1er login, où le burst court encore) -> snapshot du NOUVEAU perso.
                case "CharacterSelectionEvent" when PayloadReader.Field(p.Payload, FNum("CharacterSelectionEvent", "success")) is not null:
                    ForgetTreasure(p.Pid);
                    if (_burst.TryGetValue(p.Pid, out var old) && old.Done) _burst[p.Pid] = new BurstRecorder();
                    break;
                case "GuildInvitedEvent": MaybeAutoRefuseGuild(p.Pid, p.Payload); break;   // IGNORE_REQUEST_GUILD
                case "ReloginTokenEvent": NoteReloginToken(p.Pid); break;   // jeton non lu : le return sensible ci-dessous le laisse hors mémoire
            }
            if (name.StartsWith("TreasureHunt", StringComparison.Ordinal) || name == "TextInformationEvent") SnowObserveTreasure(p, name);
            // Burst : (re)démarré à l'acceptation du ticket du serveur de jeu, clos au premier chargement de carte. AVANT le filtre
            // ci-dessous : ce nom peut être classé sensible (« Ticket ») — il redémarre le burst sans y être enregistré.
            if (name == "AuthenticationTicketAcceptedEvent")
            {
                _burst[p.Pid] = new BurstRecorder();
                OnGameSessionStarted(p.Pid);   // nouvelle session de jeu (MITM) : lève une coupure de lien / une attente de retour en jeu
            }
            // Messages porteurs de secrets (jetons de session / relog, clés d'API, tickets, identification…) : JAMAIS mémorisés,
            // donc jamais relisibles par un script (lastMessage / historicalMessage).
            if (LuaSnowCompat.IsSensitiveMessage(name)) return;
            _lastByName[(p.Pid, name)] = p;
            var r = _burst.GetOrAdd(p.Pid, static _ => new BurstRecorder());
            if (r.Done) return;
            lock (r.Gate)
            {
                if (r.Packets.Count < BurstMaxPackets && r.Bytes + p.RawFrame.Length <= BurstMaxBytes)
                {
                    r.Packets.Add(p);
                    r.Bytes += p.RawFrame.Length;
                }
                if (name == "MapComplementaryInformationEvent") r.Done = true;
            }
        }
        catch { /* observation best-effort : ne casse jamais le décodage */ }
    }

    // ---------------------------------------------------------------- IGNORE_REQUEST_GUILD (refus auto des invitations de guilde)
    // Même patron que IGNORE_REQUEST_DUEL : délai humain, re-vérification de la politique, jamais d'exception sur le thread réseau.
    // Refuser n'engage rien -> non gaté (comme le refus de défi).
    // Un bot tiers n'ignorait que les inconnus: une invitation venant d'un de MES personnages (compte enregistré) est laissée au joueur.
    private void MaybeAutoRefuseGuild(uint pid, IReadOnlyList<GivreCore.Protobuf.ProtoField> payload)
    {
        if (!_scriptPolicy.TryGetValue(pid, out var pol) || !pol.IgnoreGuild) return;
        string recruiter = (PayloadReader.Str(payload, FNum("GuildInvitedEvent", "recruiter_name")) ?? "").Trim();
        if (recruiter.Length > 0 && _accountsSnap.Any(a => string.Equals(a.Character, recruiter, StringComparison.OrdinalIgnoreCase))) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Random.Shared.Next(900, 1700)).ConfigureAwait(false);
                if (!_scriptPolicy.TryGetValue(pid, out var p2) || !p2.IgnoreGuild) return;   // script arrêté / global repassé à false
                var relay = RelayForInject();
                byte[] pkt = MoveForge.BuildGuildInvitationAnswerFramed(false);
                if (relay is null || pkt.Length == 0) { TransportLog.Line($"[guilde] refus auto NON forgé : {MoveForge.LastForgeError}"); return; }
                bool ok = relay.Inject(pid, pkt);
                ShowInjected(pid, pkt);
                TransportLog.Line($"[guilde] pid {pid} : invitation de guilde refusée (IGNORE_REQUEST_GUILD) inject={(ok ? "OK" : "ECHEC")}");
                if (ok) RequestAutoRefused?.Invoke(pid, "guild", 0);
            }
            catch (Exception ex) { TransportLog.Line($"[guilde] refus auto : {ex.Message}"); }
        });
    }

    // ---------------------------------------------------------------- dialogue PNJ (npc: / global:getReplies)
    private readonly ConcurrentDictionary<uint, long> _npcDialogSeq = new();

    /// <summary>Compteur de questions / fermetures de dialogue PNJ reçues (change à chaque NpcDialogQuestionEvent ou
    /// DialogLeaveEvent) : permet d'attendre la RÉPONSE à une action sans relire l'ancienne question.</summary>
    public long NpcDialogSeq(uint pid) => _npcDialogSeq.TryGetValue(pid, out var v) ? v : 0;

    /// <summary>Question PNJ affichée : id du message (texte via npcmessages), paramètres, réponses visibles (ids). null si aucun
    /// dialogue ouvert.</summary>
    public (int MessageId, IReadOnlyList<string> Params, IReadOnlyList<int> ReplyIds)? NpcDialog(uint pid)
    {
        if (!_lastNpcDialog.TryGetValue(pid, out var d)) return null;
        int msgF = FNum("NpcDialogQuestionEvent", "message_id"), parF = FNum("NpcDialogQuestionEvent", "dialog_params");
        int msg = (int)PayloadReader.Int(d, msgF, 0);
        var pars = new List<string>();
        foreach (var f in d)
            if (f.Field == parF && f.Wire == GivreCore.Protobuf.WireType.Len)
            {
                string? s = f.String;
                if (s is null && f.Bytes is { } b) { try { s = System.Text.Encoding.UTF8.GetString(b); } catch { s = null; } }
                if (s is not null) pars.Add(s);
            }
        return (msg, pars, AllNpcReplyIds(d));
    }

    /// <summary>Action sur un PNJ (NpcGenericActionRequest, <paramref name="actionId"/> : 3 parler, 5 HDV vente, 6 HDV achat…) et
    /// attente de l'ouverture (dialogue, HDV, boutique, coffre). Si le dialogue s'ouvre, la QUESTION est attendue (≤ 1,5 s) avant
    /// de rendre la main : un getReplies() / reply() suivant lit toujours la bonne question.</summary>
    public async Task<bool> NpcActionAsync(uint pid, long actorId, int actionId, CancellationToken ct = default)
    {
        // 5 / 6 = HDV vente / achat : même paquet que HdvOpenSeller/BuyerAsync -> même gate Perm.Hdv AU POINT D'ACTION.
        // Les autres actions (parler, banque, boutique…) restent non gatées, comme talkNpc.
        if ((actionId == 5 || actionId == 6) && !LicenseService.HasPermission(LicenseService.Perm.Hdv))
        { TransportLog.Line($"[pnj] pid {pid} : action {actionId} (HDV) refusée — droit HDV absent."); return false; }
        var relay = RelayForInject(); if (relay is null) return false;
        long map = CurrentMap(pid) ?? 0; if (map == 0) return false;
        long seq0 = NpcDialogSeq(pid);
        var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildNpcTalkFramed(map, actorId, actionId)),
            new[] { "NpcDialogQuestionEvent", "NpcDialogCreationEvent", "ExchangeBidSellerStartedEvent", "ExchangeBidBuyerStartedEvent",
                    "ExchangeNpcShopStartedEvent", "StorageInventoryContentEvent", "ExchangeStartedEvent" }, 4000, ct).ConfigureAwait(false);
        if (ev is null) { TransportLog.Line($"[pnj] pid {pid} : action {actionId} sur PNJ {actorId} sans réponse."); return false; }
        if (ev.Name == "NpcDialogCreationEvent")
        {
            long end = Environment.TickCount64 + 1500;
            while (NpcDialogSeq(pid) == seq0 && Environment.TickCount64 < end)
                try { await Task.Delay(50, ct).ConfigureAwait(false); } catch { break; }
        }
        return true;
    }

    /// <summary>Répond par l'IDENTIFIANT de réponse (pas l'index). Oublie la question courante (la suivante la remplacera).</summary>
    public bool NpcReplyById(uint pid, int replyId)
    {
        var relay = RelayForInject(); if (relay is null || replyId <= 0) return false;
        byte[] pkt = MoveForge.BuildNpcDialogReplyFramed(replyId);
        if (pkt.Length == 0) return false;
        _lastNpcDialog.TryRemove(pid, out _);
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        return ok;
    }

    // ---------------------------------------------------------------- caractéristiques / abonnement (lecture du dernier paquet, par nom)
    /// <summary>Valeur de BASE d'une caractéristique (id de données de jeu : 10 force, 11 vitalité, 12 sagesse, 13 chance,
    /// 14 agilité, 15 intelligence) lue dans le dernier CharacterCharacteristicsEvent ; -1 si inconnue.</summary>
    public int CharacteristicBase(uint pid, int characteristicId)
    {
        if (LastReceived(pid, "CharacterCharacteristicsEvent") is not { } p) return -1;
        int statsF = FNum("CharacterCharacteristicsEvent", "stats"), listF = FNum("CharacterCharacteristics", "characteristics");
        int idF = FNum("CharacterCharacteristic", "characteristic_id"), detF = FNum("CharacterCharacteristic", "detailed");
        int valF = FNum("CharacterCharacteristic", "value"), baseF = FNum("CharacterCharacteristicDetailed", "base");
        int totalF = FNum("CharacterCharacteristicValue", "total");
        foreach (var s in p.Payload)
        {
            if (s.Field != statsF || s.Message is not { } stats) continue;
            foreach (var c in stats)
            {
                if (c.Field != listF || c.Message is not { } ch || (int)PayloadReader.Int(ch, idF, -1) != characteristicId) continue;
                if (ch.FirstOrDefault(x => x.Field == detF)?.Message is { } det) return (int)PayloadReader.Int(det, baseF, 0);
                if (ch.FirstOrDefault(x => x.Field == valF)?.Message is { } val) return (int)PayloadReader.Int(val, totalF, 0);
                return 0;
            }
        }
        return -1;
    }

    /// <summary>Fin d'abonnement (AccountInformationUpdateEvent.subscription_end_date, en ms ou s depuis 1970), null si jamais reçue.
    /// 0 = pas d'abonnement (date dans le passé).</summary>
    public DateTime? SubscriptionEndUtc(uint pid)
    {
        // 1) abonnement annoncé par le serveur de connexion (IdentificationSuccess) : seule source en Socket tant que le paquet de jeu n'est pas reçu
        if (LastReceived(pid, "AccountInformationUpdateEvent") is not { } p) return AccountSubscriptionInfo.EffectiveEnd(AccountSubscriptionInfo.Get(pid));
        long v = PayloadReader.Int(p.Payload, FNum("AccountInformationUpdateEvent", "subscription_end_date"), 0);
        if (v <= 0) return DateTime.UnixEpoch;
        try { return v > 100_000_000_000L ? DateTime.UnixEpoch.AddMilliseconds(v) : DateTime.UnixEpoch.AddSeconds(v); }
        catch { return null; }
    }

    // ---------------------------------------------------------------- prix moyens (ObjectAveragePricesEvent) — par serveur
    // Clé = (portée, gid) : portée = id du serveur de jeu (partagée entre les bots d'un MÊME serveur, utile en mode Socket),
    // sinon portée propre au pid (serveur pas encore identifié) — jamais de mélange entre serveurs.
    private readonly record struct AveragePriceObservation(long Price, long Order);
    private readonly ConcurrentDictionary<(long Scope, int Gid), AveragePriceObservation> _avgPrices = new();
    private long _averagePriceOrder;

    /// <summary>Table de prix serveur actualisée ; émis sur le thread réseau après écriture du cache.</summary>
    public event Action<uint>? AveragePricesChanged;

    private static long PidPriceScope(uint pid) => -(long)pid - 1;   // < 0 : ne croise jamais un id de serveur (> 0)
    private long PriceScope(uint pid) { int sid = ServerIdOf(pid); return sid != 0 ? sid : PidPriceScope(pid); }

    /// <summary>Prix moyen d'un objet sur le SERVEUR de ce bot (ObjectAveragePricesEvent, demandé par le client de jeu à la
    /// connexion), 0 si inconnu (mode Socket : table jamais reçue tant qu'aucun client du serveur ne la demande).</summary>
    public long AveragePrice(uint pid, int gid)
    {
        int sid = ServerIdOf(pid);
        _avgPrices.TryGetValue((PidPriceScope(pid), gid), out var own);
        if (sid != 0 && _avgPrices.TryGetValue(((long)sid, gid), out var shared) && shared.Order >= own.Order)
            return shared.Price;
        return own.Price;
    }

    private void RememberServerIdentity(uint pid, int serverId, string name)
    {
        // HDV quotes belong to one server: a session that moves to another server must not reuse them.
        if (_serverInfo.TryGetValue(pid, out var previous) && previous.Id != 0 && previous.Id != serverId) Hdv.Clear(pid);
        _serverInfo[pid] = (serverId, name);
        PromoteAveragePrices(pid, serverId);
        // Also refresh an already-open detail when this identity reveals another client's shared table.
        AveragePricesChanged?.Invoke(pid);
        if (!string.IsNullOrEmpty(name)) ServerIdentified?.Invoke(pid, name);
    }

    private void PromoteAveragePrices(uint pid, int serverId)
    {
        if (serverId == 0) return;
        long own = PidPriceScope(pid);
        foreach (var entry in _avgPrices.Where(e => e.Key.Scope == own).ToArray())
        {
            var candidate = entry.Value;
            _avgPrices.AddOrUpdate(((long)serverId, entry.Key.Gid), candidate,
                (_, current) => candidate.Order > current.Order ? candidate : current);
            _avgPrices.TryRemove(entry);
        }
    }

    private void ReadAveragePrices(uint pid, IReadOnlyList<GivreCore.Protobuf.ProtoField> payload)
    {
        int listF = FNum("ObjectAveragePricesEvent", "objects_average_prices");
        int gidF = FNum("ObjectAveragePrice", "object_gid"), priceF = FNum("ObjectAveragePrice", "average_price");
        long scope = PriceScope(pid);
        long order = Interlocked.Increment(ref _averagePriceOrder);
        int n = 0;
        foreach (var f in payload)
            if (f.Field == listF && f.Message is { } m)
            {
                int gid = (int)PayloadReader.Int(m, gidF, 0);
                long price = PayloadReader.Int(m, priceF, 0);
                if (gid > 0 && price > 0)
                {
                    var candidate = new AveragePriceObservation(price, order);
                    _avgPrices.AddOrUpdate((scope, gid), candidate,
                        (_, current) => candidate.Order > current.Order ? candidate : current);
                    n++;
                }
            }
        if (n > 0)
        {
            // Identity can arrive while an earlier PID-scoped table is being reduced.
            if (scope < 0) PromoteAveragePrices(pid, ServerIdOf(pid));
            TransportLog.Line($"[hdv] prix moyens serveur : {n} objet(s) ({(scope > 0 ? $"serveur {scope}" : "serveur pas encore identifié")})");
            AveragePricesChanged?.Invoke(pid);
        }
    }

    // ---------------------------------------------------------------- HDV : conditions de vente (SellingConditions)
    private readonly ConcurrentDictionary<uint, int> _hdvMaxSlots = new();
    private readonly ConcurrentDictionary<uint, HashSet<int>> _hdvTypes = new();   // types d'objets ACCEPTÉS par l'HDV vendeur ouvert
    private readonly ConcurrentDictionary<uint, int[]> _hdvLotSizes = new();       // tailles de lot autorisées (1/10/100/1000…)
    private readonly ConcurrentDictionary<uint, float> _hdvTaxPct = new();         // taxe de mise en vente (%)
    private readonly ConcurrentDictionary<uint, int> _hdvHiddenLots = new();       // entrées « lot » sans quantité ni prix (non affichées)
    private static readonly int[] DefaultLotSizes = { 1, 10, 100, 1000 };

    /// <summary>Nombre MAX de lots en vente pour ce compte (SellingConditions.max_item_per_account du dernier HDV vendeur
    /// ouvert), -1 si inconnu.</summary>
    public int HdvMaxSlots(uint pid) => _hdvMaxSlots.TryGetValue(pid, out var v) ? v : -1;

    /// <summary>Emplacements de vente LIBRES : max − mes lots − entrées sans quantité ni prix (elles occupent peut-être une place :
    /// on compte prudemment). -1 si le maximum est inconnu (HDV vendeur pas encore ouvert).</summary>
    public int HdvFreeSlots(uint pid)
    {
        int max = HdvMaxSlots(pid);
        if (max <= 0) return -1;
        return Math.Max(0, max - Hdv.MyListingCount(pid) - _hdvHiddenLots.GetValueOrDefault(pid));
    }

    /// <summary>L'HDV vendeur ouvert accepte-t-il cet objet (son TYPE est dans SellingConditions.types) ? Liste inconnue ou type
    /// inconnu (données d'objets pas chargées) -> vrai : on ne filtre que sur une preuve.</summary>
    public bool HdvAcceptsGid(uint pid, long gid)
    {
        if (!_hdvTypes.TryGetValue(pid, out var types) || types.Count == 0) return true;
        int type = ItemTypeId(gid);
        return type == 0 || types.Contains(type);
    }

    /// <summary>Tailles de lot autorisées par l'HDV vendeur ouvert (croissantes), sinon 1/10/100/1000.</summary>
    public int[] HdvLotSizes(uint pid) => _hdvLotSizes.TryGetValue(pid, out var q) && q.Length > 0 ? q : DefaultLotSizes;

    /// <summary>Taxe de mise en vente de l'HDV vendeur ouvert, en % (0 si inconnue).</summary>
    public float HdvTaxPercent(uint pid) => _hdvTaxPct.TryGetValue(pid, out var t) ? t : 0f;

    private void ReadHdvMaxSlots(uint pid, IReadOnlyList<GivreCore.Protobuf.ProtoField> payload)
    {
        int condF = FNum("ExchangeBidSellerStartedEvent", "selling_conditions");
        int maxF = FNum("SellingConditions", "max_item_per_account");
        // ⚠ le champ s'appelle « types_ » (avec le souligné) dans le schéma extrait.
        int typesF = FNum("SellingConditions", "types_");
        int qtyF = FNum("SellingConditions", "quantities");
        int taxF = FNum("SellingConditions", "tax_percentage");
        foreach (var f in payload)
            if (f.Field == condF && f.Message is { } m)
            {
                long max = PayloadReader.Int(m, maxF, -1);
                if (max > 0) _hdvMaxSlots[pid] = (int)max;
                var types = RepeatedInts(m, typesF);
                if (types.Count > 0) _hdvTypes[pid] = new HashSet<int>(types.Where(t => t > 0).Select(t => (int)t));
                var qty = RepeatedInts(m, qtyF).Where(q => q > 0 && q <= 100_000).Select(q => (int)q).Distinct().OrderBy(q => q).ToArray();
                if (qty.Length > 0) _hdvLotSizes[pid] = qty;
                var tax = m.FirstOrDefault(g => g.Field == taxF);
                if (tax is { Wire: GivreCore.Protobuf.WireType.I32 } && tax.AsFloat is > 0f and < 100f) _hdvTaxPct[pid] = tax.AsFloat;
                return;
            }
    }

    // repeated int32 : PACKED (un seul champ Len) ou non (un champ varint par valeur) — les deux formes sont acceptées.
    private static List<long> RepeatedInts(IReadOnlyList<GivreCore.Protobuf.ProtoField> m, int field)
    {
        var list = new List<long>();
        foreach (var g in m)
        {
            if (g.Field != field) continue;
            if (g.Wire == GivreCore.Protobuf.WireType.Varint) list.Add((long)g.Varint);
            else if (g.Bytes is { Length: > 0 } b) list.AddRange(ReadPackedVarints(b));
        }
        return list;
    }

    // ---------------------------------------------------------------- craft pas à pas (craft:PutItem / ChangeQuantityToCraft / Ready)
    // Établi ouvert (ExchangeCraftStartedEvent) -> quantité d'ingrédients posée depuis (step du ready). Clos par ExchangeLeaveEvent.
    private readonly ConcurrentDictionary<uint, int> _craftOpen = new();

    public bool CraftOpen(uint pid) => _craftOpen.ContainsKey(pid);

    /// <summary>Interaction actuellement OUVERTE côté serveur pour ce pid (« craft », « hdv », « bank », « trade », « shop », « dialogue »), ou null. Tant qu'elle reste ouverte le
    /// personnage ne peut ni bouger ni quitter : l'interface doit toujours pouvoir la fermer (DialogLeaveRequest).</summary>
    public string? OpenInteraction(uint pid) => CraftOpen(pid) ? "craft" : HdvAnyOpen(pid) ? "hdv"
        : CurrentExchange(pid) switch { ExchangeKind.Bank => "bank", ExchangeKind.Trade => "trade", ExchangeKind.Shop => "shop", ExchangeKind.Teleport => "teleport", _ => null }
          ?? (NpcDialog(pid) is not null ? "dialogue" : null);

    /// <summary>Pose <paramref name="qty"/> d'un ingrédient (par GID, piles du SAC) sur l'établi OUVERT et attend l'accusé.
    /// Gaté Perm.Gather (comme craftItem).</summary>
    public async Task<bool> CraftPutItemAsync(uint pid, long gid, int qty, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !CraftOpen(pid)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var stacks = BagStacks(pid, gid);
        if (stacks.Count == 0) { TransportLog.Line($"[craft] pid {pid} : ingrédient gid {gid} absent du sac."); return false; }
        int want = qty > 0 ? qty : (int)Math.Min(int.MaxValue, stacks.Sum(i => (long)i.Qty));
        bool any = false;
        foreach (var st in stacks)
        {
            if (want <= 0 || !CraftOpen(pid)) break;   // établi fermé entre deux piles : ne rien envoyer à un autre échange
            int n = Math.Min(want, st.Qty);
            var ack = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildExchangeObjectMoveFramed(st.Uid, n)),
                new[] { "ExchangeObjectsAddedEvent", "ExchangeObjectsModifiedEvent", "ExchangeErrorEvent" }, 3000, ct).ConfigureAwait(false);
            if (ack is null) break;
            if (ack.Name == "ExchangeErrorEvent") { TransportLog.Line($"[craft] pid {pid} : ingrédient gid {gid} refusé par le serveur."); break; }   // refus : pas une pose
            // Suite de l'accusé sur un thread du pool : ExchangeLeaveEvent a pu retirer l'entrée entre-temps -> mise à jour
            // SEULEMENT si l'établi est encore ouvert (jamais de re-création d'un établi fermé).
            while (_craftOpen.TryGetValue(pid, out var cur) && !_craftOpen.TryUpdate(pid, cur + n, cur)) { }
            any = true; want -= n;
        }
        TransportLog.Line($"[craft] pid {pid} : ingrédient gid {gid} posé={(any ? "OK" : "ECHEC")}");
        return any;
    }

    /// <summary>Nombre de crafts à enchaîner sur l'établi ouvert (ExchangeCraftCountRequest).</summary>
    public async Task<bool> CraftSetCountAsync(uint pid, int count, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !CraftOpen(pid) || count < 1) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var ev = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildCraftCountFramed(count)),
            new[] { "ExchangeCraftCountModifiedEvent" }, 3000, ct).ConfigureAwait(false);
        return ev is not null;
    }

    /// <summary>Valide le craft (ExchangeReadyRequest) et attend le résultat. L'établi RESTE ouvert (le script le ferme,
    ///). true = un objet a été créé: résultat lu PAR NOM (code SUCCESS); un résultat vide = recette
    /// invalide, un craft raté/interdit = false. Une fermeture de l'établi pendant l'attente rend la main tout de suite.</summary>
    public async Task<bool> CraftReadyAsync(uint pid, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !_craftOpen.TryGetValue(pid, out int step)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var result = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildCraftReadyFramed(step <= 0 ? 1 : step)),
            new[] { "ExchangeCraftResultEvent", "ExchangeLeaveEvent" }, 12000, ct).ConfigureAwait(false);
        // Remise à 0 du step SANS recréer l'entrée si l'établi a été fermé pendant l'attente (TryUpdate, pas d'indexeur).
        while (_craftOpen.TryGetValue(pid, out var c) && c != 0 && !_craftOpen.TryUpdate(pid, 0, c)) { }
        InventoryReader.CraftResult? res = result?.Name == "ExchangeCraftResultEvent" ? InventoryReader.ReadCraftResult(result.Payload) : null;
        TransportLog.Line($"[craft] pid {pid} : ready -> {(res is null && result is not null ? "établi refermé avant le résultat" : CraftVerdict(res))}");
        return res is { Made: true };
    }

    private void ForgetSnow(uint pid)
    {
        _burst.TryRemove(pid, out _);
        ForgetTreasure(pid);
        _craftOpen.TryRemove(pid, out _);
        _hdvMaxSlots.TryRemove(pid, out _);
        foreach (var k in _lastByName.Keys.Where(k => k.Pid == pid).ToList()) _lastByName.TryRemove(k, out _);
        long ps = PidPriceScope(pid);   // prix reçus avant l'identification du serveur (portée propre au pid, réutilisable par l'OS)
        foreach (var k in _avgPrices.Keys.Where(k => k.Scope == ps).ToList()) _avgPrices.TryRemove(k, out _);
    }
}
