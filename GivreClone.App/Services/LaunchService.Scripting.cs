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

// ===================== Couche awaitable + lecture headless (GivreLuaHost) : activité, attentes de move / de code, dialogues PNJ =====================
public sealed partial class LaunchService
{
    // ================= COUCHE AWAITABLE + LECTURE HEADLESS (scripting Lua : GivreLuaHost) =================
    // Un script Lua doit ATTENDRE la resolution serveur d'un deplacement : arrivee (jsq), refus (jqt) ou timeout.
    // On arme un TaskCompletionSource par pid a l'envoi, complete aux points ou la garde de deplacement se libere.
    public enum MoveOutcome { Arrived, Refused, Timeout, NotSent, Superseded }
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<MoveOutcome>> _moveWait = new();

    /// <summary>Carte courante (mapId) du bot, ou null si inconnue. Lecture headless pour le scripting.</summary>
    public long? CurrentMap(uint pid) => _lastMap.TryGetValue(pid, out var m) && m != 0 ? m : null;

    /// <summary>Le bot est-il en combat ? (FightStartingEvent -> true, FightEndEvent -> false)</summary>
    public bool IsInFight(uint pid) => _inFight.TryGetValue(pid, out var f) && f;

    // Fenêtre « récolte en cours » par pid : armée quand on ENVOIE l'InteractiveUseRequest (iwo) SUR UNE VRAIE RESSOURCE
    // (pas un zaap/porte/PNJ, cf IsGatherResource), fermée à la fin (iwi) ou à l'échec (iwq) de l'interactif. Cap de
    // sécurité (GatherWindowMs) au cas où l'event de fin manquerait. Sert UNIQUEMENT à l'indicateur d'activité.
    private readonly ConcurrentDictionary<uint, long> _gatherUntil = new();
    private const long GatherWindowMs = 12000;
    // Fenêtre « déplacement en cours » par pid, SÉPARÉE de _moveGuardUntil (qui gate « un seul move bot à la fois ») :
    // armée aussi bien sur un move injecté PAR LE BOT (MoveTo) que sur un déplacement MANUEL du joueur (MapMovementRequest
    // sortant observé) — le client n'émet un jrw que pour SON perso. Fermée à l'arrivée (jsq). Cap MoveWindowMs. Activité.
    private readonly ConcurrentDictionary<uint, long> _movingUntil = new();
    private const long MoveWindowMs = 6000;
    // Element_id des interactifs de la carte courante qui sont de VRAIES RESSOURCES (IsGatherResource), par pid — capté
    // au décodage de la carte. Sert à reconnaître une récolte MANUELLE (InteractiveUseRequest sortant observé sur un de
    // ces éléments) et non un zaap/porte/PNJ. Remplacé à chaque nouvelle carte.
    private readonly ConcurrentDictionary<uint, HashSet<int>> _gatherElements = new();

    /// <summary>Levé quand l'activité de HAUT NIVEAU d'un pid a pu changer sur un FRONT que l'UI ne capte pas déjà par un
    /// autre event (fin de déplacement = jsq, début de récolte). L'UI recalcule ActivityOf tout de suite (pas d'attente
    /// du poll 2,5 s) -> transitions « En déplacement » -> « En jeu » et « -> En récolte » immédiates.</summary>
    public event Action<uint>? ActivityChanged;
    private void RaiseActivity(uint pid) => ActivityChanged?.Invoke(pid);

    /// <summary>Activité de HAUT NIVEAU du bot, dérivée de l'état MITM (combat / récolte / déplacement / jeu / connexion).
    /// Priorité descendante. Utilisée par l'UI à la place du compteur de paquets. Pure lecture d'état.</summary>
    public BotActivity ActivityOf(uint pid)
    {
        // Session de jeu perdue (MITM, Dofus.exe ouvert) ou session socket parquée -> « Déconnecté », prioritaire. Ancien pid d'un
        // client MITM en cours de relance (« Reconnecter » en place) -> « Connexion ».
        if (_linkDown.TryGetValue(pid, out var down)) return down.Relaunching ? BotActivity.Connecting : BotActivity.Disconnected;
        if (LifeStatusOf(pid) != 0) return BotActivity.Dead;   // tombe/fantôme -> "Mort" (résurrection auto en cours), prioritaire
        if (IsInFight(pid)) return BotActivity.Combat;
        long now = Environment.TickCount64;
        if (_gatherUntil.TryGetValue(pid, out var gu) && now < gu) return BotActivity.Gathering;
        if (_movingUntil.TryGetValue(pid, out var mu) && now < mu) return BotActivity.Moving;
        return CurrentMap(pid) is not null ? BotActivity.InGame : BotActivity.Connecting;
    }

    /// <summary>Dernier poids/pods connu (cur,max), ou null. Lecture headless pour le scripting.</summary>
    public (int Cur, int Max)? Pods(uint pid) => _lastPods.TryGetValue(pid, out var w) ? w : null;

    /// <summary>Expérience TOTALE du personnage (CharacterExperienceGainEvent), ou -1 si inconnue. Pour l'échantillonnage XP/heure.</summary>
    public long CharacterXp(uint pid) => _charXp.TryGetValue(pid, out var x) ? x : -1;
    private readonly ConcurrentDictionary<uint, long> _charXp = new();

    // Complete l'attente de deplacement en cours (si armee) : appele a l'arrivee (jsq) et au refus (jqt).
    private void SettleMove(uint pid, MoveOutcome outcome)
    {
        if (_moveWait.TryRemove(pid, out var tcs)) tcs.TrySetResult(outcome);
    }

    /// <summary>Envoie une action de deplacement (<paramref name="send"/> forge+injecte) et ATTEND sa resolution serveur :
    /// arrivee (jsq), refus (jqt) ou timeout. Arme le TCS AVANT l'envoi (pas de course avec l'ack). Bloquant cote script Lua.</summary>
    public async Task<MoveOutcome> SendAndWaitAsync(uint pid, Func<bool> send, int timeoutMs, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<MoveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_moveWait.TryRemove(pid, out var prev)) prev.TrySetResult(MoveOutcome.Superseded);
        _moveWait[pid] = tcs;
        bool sent;
        try { sent = send(); }
        catch { _moveWait.TryRemove(pid, out _); throw; }
        if (!sent) { _moveWait.TryRemove(pid, out _); return MoveOutcome.NotSent; }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
        if (done != tcs.Task) { _moveWait.TryRemove(pid, out _); return ct.IsCancellationRequested ? MoveOutcome.Superseded : MoveOutcome.Timeout; }
        return await tcs.Task.ConfigureAwait(false);
    }

    // Attente d'un EVENT ENTRANT precis (sequences banque/PNJ) : pid -> (NOMS CLAIRS attendus, TCS). Un seul en vol par
    // pid (sequences strictement sequentielles). Resolu dans OnDecoded des qu'un S->C au bon p.Name arrive (via le mapping,
    // patch-proof : plus de code brut).
    private readonly ConcurrentDictionary<uint, (HashSet<string> Names, Func<DecodedPacket, bool>? Accept, TaskCompletionSource<DecodedPacket> Tcs)> _codeWait = new();

    /// <summary>Forge+injecte <paramref name="send"/> puis ATTEND le 1er paquet ENTRANT (S-&gt;C) dont le NOM CLAIR est
    /// dans <paramref name="names"/> (resolu via le mapping, patch-proof), ou timeout. Renvoie le paquet decode ou null.</summary>
    public Task<DecodedPacket?> SendAndWaitForCodeAsync(uint pid, Func<bool> send, string[] names, int timeoutMs, CancellationToken ct = default)
        => SendAndWaitForCodeAsync(pid, send, names, timeoutMs, accept: null, ct: ct);

    /// <summary>
    /// Predicate must validate the SAME requested object/quantity where a
    /// shared event name is insufficient (e.g. bank operations). The waiter
    /// remains armed on unrelated packets rather than treating them as ACKs.
    /// </summary>
    public async Task<DecodedPacket?> SendAndWaitForCodeAsync(uint pid, Func<bool> send, string[] names,
        int timeoutMs, Func<DecodedPacket, bool>? accept, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<DecodedPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = (Names: new HashSet<string>(names), Accept: accept, Tcs: tcs);
        if (!_codeWait.TryAdd(pid, current)) return null; // never steal another operation's waiter
        bool sent;
        try
        {
            if (ct.IsCancellationRequested) return null;
            sent = send();
            if (!sent) return null;
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            return done == tcs.Task ? await tcs.Task.ConfigureAwait(false) : null;
        }
        finally
        {
            // Do not remove a newer waiter if another path superseded this one.
            _ = ((ICollection<KeyValuePair<uint, (HashSet<string> Names,
                Func<DecodedPacket, bool>? Accept, TaskCompletionSource<DecodedPacket> Tcs)>>)_codeWait)
                .Remove(new KeyValuePair<uint, (HashSet<string> Names,
                    Func<DecodedPacket, bool>? Accept, TaskCompletionSource<DecodedPacket> Tcs)>(pid, current));
        }
    }

    private static bool AcceptCodePacket(
        Func<DecodedPacket, bool>? predicate, DecodedPacket packet)
    {
        try { return predicate?.Invoke(packet) ?? true; }
        catch { return false; } // malformed schema/event can never certify a bank action
    }

    // NpcDialogQuestionEvent (imw) : reply_id de la 1re réponse proposée (= accès banque). ⚠ PATCH : la liste des
    // réponses ET l'id imbriqué ont été renumérotés. v4 : NpcDialogQuestionEvent.visible_replies = #3 (repeated),
    // et chaque réponse porte son reply_id au sous-champ #2 (les params optionnels sont au sous-#1). L'ANCIEN code
    // lisait la liste au #2 (= dialog_params, une chaîne) et l'id au sous-#1 -> il ne trouvait RIEN -> « aucune
    // réponse dans le dialogue » -> dépôt banque KO en silence. Le porteur visible_replies est résolu par le mapping.
    // Numéro de champ d'un message DE JEU : mapping streamé UNIQUEMENT, aucun numéro figé dans le client. Après un patch,
    // mettre à jour le bundle suffit, zéro build. Champ inconnu du mapping -> FieldMiss.None (-1, aucun champ réel) : la
    // lecture rend la valeur par défaut, et l'absence est journalisée une fois (FieldMiss).
    private int FNum(string message, string field) => FieldOf(message, field) ?? FieldMiss.None;

    // Même chose en int? (null = absent) : pour sauter tout un bloc quand un champ manque au lieu de lire des défauts.
    private int? FieldOf(string message, string field)
    {
        int? n = _catalog?.FieldNumber(message, field);
        if (n is null) FieldMiss.Of(message, field);
        return n;
    }

    private int? FirstNpcReplyId(IReadOnlyList<ProtoField> payload)
    {
        // 100% mapping : le mapping v4 porte DÉJÀ les deux numéros -> NpcDialogQuestionEvent.visible_replies (#3) et
        // VisibleReply.reply_id (#2). On les LIT du catalogue (repli = numéro capturé) : après un patch, mettre à jour
        // fields.json suffit, zéro build.
        int repliesF = FNum("NpcDialogQuestionEvent", "visible_replies");
        int replyIdF = FNum("VisibleReply", "reply_id");
        foreach (var f in payload)
            if (f.Field == repliesF && f.Message is { } m)
                foreach (var g in m)
                    if (g.Field == replyIdF && g.Wire == WireType.Varint) return (int)g.Varint;
        return null;
    }

    // Toutes les reply_id visibles d'un NpcDialogQuestionEvent, dans l'ordre (mêmes numéros de champ, résolus par mapping).
    private IReadOnlyList<int> AllNpcReplyIds(IReadOnlyList<ProtoField> payload)
    {
        int repliesF = FNum("NpcDialogQuestionEvent", "visible_replies");
        int replyIdF = FNum("VisibleReply", "reply_id");
        var list = new List<int>();
        foreach (var f in payload)
            if (f.Field == repliesF && f.Message is { } m)
                foreach (var g in m)
                    if (g.Field == replyIdF && g.Wire == WireType.Varint) { list.Add((int)g.Varint); break; }
        return list;
    }

    /// <summary>Lit un champ entier d'un paquet décodé PAR NOM (numéro de champ résolu via le mapping streamé ;
    /// <paramref name="def"/> si absent). Sert à l'enregistreur de trajet pour lire npc_id / reply_id / element_id
    /// sans jamais coder un numéro de champ en dur (règle mapping).</summary>
    public int PacketInt(DecodedPacket p, string field, int def = -1)
        => p.Name is { } n ? (int)PayloadReader.Int(p.Payload, FNum(n, field), def) : def;

    /// <summary>Index 1-based d'une <paramref name="replyId"/> dans les réponses visibles du dialogue PNJ courant du pid
    /// (0 = absente / pas de dialogue mémorisé). Miroir de <see cref="NpcReply"/> : convertit la réponse que le JOUEUR a
    /// choisie (observée dans un NpcDialogReplyRequest sortant) en argument <c>replyNpc(index)</c> pour l'enregistreur.</summary>
    public int NpcReplyIndexOf(uint pid, int replyId)
    {
        if (!_lastNpcDialog.TryGetValue(pid, out var dlg)) return 0;
        var ids = AllNpcReplyIds(dlg);
        for (int i = 0; i < ids.Count; i++) if (ids[i] == replyId) return i + 1;
        return 0;
    }

    // Attend que la carte courante devienne targetMap (polling léger), ou timeout.
    private async Task<bool> WaitMapAsync(uint pid, long targetMap, int timeoutMs, CancellationToken ct)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if ((CurrentMap(pid) ?? 0) == targetMap) return true;
            try { await Task.Delay(120, ct).ConfigureAwait(false); } catch { return false; }
        }
        return (CurrentMap(pid) ?? 0) == targetMap;
    }

    /// <summary>Franchit une transition CONDITIONNELLE (porte/PNJ gardé ; le critère a DÉJÀ été validé par le BFS, sinon
    /// cette arête n'y serait pas). Déclenche l'interactif de la porte puis — si un dialogue PNJ s'ouvre au lieu d'un
    /// changement direct — essaie les réponses visibles jusqu'à l'arrivée sur <paramref name="targetMap"/> (profondeur
    /// bornée). Renvoie true si franchi.
    /// ⚠ V1 : la SÉLECTION de réponse est heuristique (réponses essayées dans l'ordre) — à confirmer/affiner en jeu selon
    /// le PNJ. Échec = false (l'appelant reroute ; le cap arrête proprement, jamais de boucle). Bloquant -> via GetResult.</summary>
    public async Task<bool> CrossGatedDoorAsync(uint pid, MapInteractive door, long targetMap, CancellationToken ct = default)
    {
        var relay = RelayForInject();
        if (relay is null || door.HarvestSkill is not { } skill) return false;

        // 1) Déclenche l'interactif. Attend SOIT le dialogue PNJ, SOIT (passage sans dialogue) directement l'arrivée.
        byte[] use = MoveForge.BuildInteractiveUseFramed(skill.SkillInstanceUid, door.ElementId);
        var dlg = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, use), new[] { "NpcDialogQuestionEvent" }, 4000, ct).ConfigureAwait(false);
        if (await WaitMapAsync(pid, targetMap, 1200, ct).ConfigureAwait(false)) return true;   // franchi sans dialogue
        if (dlg is null) { TransportLog.Line($"[trajet] pid {pid} : passage conditionnel -> ni dialogue PNJ ni changement de carte (mécanisme à confirmer en jeu)."); return false; }

        // 2) Dialogue PNJ ouvert : essaie les réponses visibles jusqu'au changement de carte (profondeur bornée).
        var current = dlg;
        for (int depth = 0; depth < 4; depth++)
        {
            var replies = AllNpcReplyIds(current.Payload);
            if (replies.Count == 0) break;
            bool advanced = false;
            foreach (int r in replies)
            {
                var next = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildNpcDialogReplyFramed(r)), new[] { "NpcDialogQuestionEvent" }, 2500, ct).ConfigureAwait(false);
                if (await WaitMapAsync(pid, targetMap, 2500, ct).ConfigureAwait(false))
                { TransportLog.Line($"[trajet] pid {pid} : passage conditionnel franchi via réponse PNJ {r} -> carte {targetMap}."); return true; }
                if (next is not null) { current = next; advanced = true; break; }   // le dialogue a avancé (nouvelle question)
            }
            if (!advanced) break;   // aucune réponse n'a fait avancer le dialogue ni changé la carte
        }
        relay.Inject(pid, MoveForge.BuildDialogLeaveFramed());   // sortie propre du dialogue
        TransportLog.Line($"[trajet] pid {pid} : passage conditionnel NON franchi (aucune réponse PNJ n'a mené à la carte {targetMap}).");
        return false;
    }
}
