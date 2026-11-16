using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Lot J : coffres et maisons à code =====================
// État des maisons (décodé du MapComplementaryInformationEvent, thread réseau) + séquences BLOQUANTES d'ouverture d'un
// coffre / d'entrée dans une maison (appelées par l'hôte Lua via GetResult). Les séquences s'abonnent à PacketDecoded
// (thread réseau : écriture de champs volatils SEULEMENT) et sondent toutes les 100 ms : aucun _codeWait pendant la
// séquence (les événements résultat / coffre / carte arrivent souvent dans la même rafale). Le CODE n'est JAMAIS journalisé.
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, IReadOnlyList<HouseOnMapInfo>> _mapHouses = new();
    private readonly ConcurrentDictionary<uint, bool> _inHouse = new();
    // pid -> coffre à code ouvert par lockedStorage : 0 = séquence en cours (contenu pas encore reçu), 1 = contenu reçu.
    // Tant qu'il est présent, les événements Storage* ne touchent PAS le store Bank (coffre de maison ≠ banque).
    private readonly ConcurrentDictionary<uint, byte> _chestSession = new();
    // 0 inconnu, 1 code brut accepté, 2 code complété par '_' accepté (même protocole pour tous les bots du processus).
    private static int s_lockPadMode;

    /// <summary>Maisons de la carte courante (dernier MapComplementaryInformationEvent). Lecture pure.</summary>
    public IReadOnlyList<HouseOnMapInfo> MapHouses(uint pid) => _mapHouses.TryGetValue(pid, out var h) ? h : Array.Empty<HouseOnMapInfo>();

    /// <summary>Vrai si le perso est DANS une maison (in_house_information présent sur le dernier changement de carte).</summary>
    public bool InHouse(uint pid) => _inHouse.TryGetValue(pid, out var b) && b;

    /// <summary>Un coffre à code (lockedStorage) est-il en cours d'ouverture / ouvert pour ce bot ?</summary>
    public bool ChestSessionActive(uint pid) => _chestSession.ContainsKey(pid);

    public enum LockOutcome { Opened, Entered, WrongCode, Forbidden, Busy, CodeRequired, Timeout, Denied, ForgeFailed }

    /// <summary>Envoie InteractiveUseRequest MAINTENANT (le perso DOIT déjà être sur la case d'interaction), sans filtre
    /// Harvestable. Pas une récolte : ni fenêtre de récolte ni cap de niveau. Gate Perm.Gather (socle interactifs) AU POINT
    /// D'ACTION. specificInstanceId = instance de maison (0 = omis).</summary>
    public bool SendInteractiveUseRaw(uint pid, int elementId, int skillInstanceUid, int specificInstanceId = 0)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildInteractiveUseFramed(skillInstanceUid, elementId, specificInstanceId);
        if (pkt.Length == 0) { TransportLog.Line($"[interactif] pid {pid} : InteractiveUseRequest non forgé ({MoveForge.LastForgeError})"); return false; }
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[interactif] pid {pid} : InteractiveUseRequest element {elementId} skillUid {skillInstanceUid} inst {specificInstanceId} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ---------------------------------------------------------------- décodage (thread réseau, appelé par OnDecoded)
    private void OnLotJMapInfo(DecodedPacket p)
    {
        _mapHouses[p.Pid] = HouseReader.Extract(p.Payload);
        _inHouse[p.Pid] = HouseReader.IsInHouse(p.Payload);
        _chestSession.TryRemove(p.Pid, out _);   // changement de carte -> aucun coffre ne peut rester ouvert
    }

    private void OnLotJPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null || !_chestSession.ContainsKey(p.Pid)) return;
        switch (p.Name)
        {
            // Coffre ouvert (début d'échange OU contenu). TryUpdate : ne RESSUSCITE pas une session que le thread script vient
            // de retirer (échec/timeout concurrent) -> pas de session fantôme qui couperait les refus auto / les Storage*.
            case "ExchangeStartedWithStorageEvent":
            case "ExchangeStartedWithMultiTabStorageEvent":
            case "StorageInventoryContentEvent":
                _chestSession.TryUpdate(p.Pid, 1, 0);
                break;
            // Fermeture d'un coffre = TOUJOURS ExchangeLeaveEvent (LeaveExchangeAsync l'attend), et seulement une fois le coffre
            // ouvert. DialogLeaveEvent n'est PAS une fin de session : le dialogue de CODE se ferme autour de l'ouverture du
            // coffre (ordre non garanti) et lâcher la session là laisserait les maj du coffre écraser la banque.
            case "ExchangeLeaveEvent":
                _chestSession.TryRemove(new KeyValuePair<uint, byte>(p.Pid, 1));
                break;
        }
    }

    private void ForgetLotJ(uint pid)
    {
        _mapHouses.TryRemove(pid, out _); _inHouse.TryRemove(pid, out _); _chestSession.TryRemove(pid, out _);
    }

    // ---------------------------------------------------------------- séquences de verrou
    private sealed class LockWatch
    {
        public volatile bool Dialog, StorageOpened, UseError, DoorUpdated, Unlocked, DialogClosed;
        public volatile int CodeSize, Action, Result = -1;   // Result -1 = aucun LockableCodeResultEvent
    }

    private void FeedLockWatch(LockWatch w, uint pid, int elementId, DecodedPacket p)
    {
        if (p.Pid != pid || p.ClientToServer || p.Name is null) return;   // ShowInjected ré-émet nos C->S : ignorés
        switch (p.Name)
        {
            case "LockableShowCodeDialogEvent":
                w.CodeSize = (int)PayloadReader.Int(p.Payload, FNum("LockableShowCodeDialogEvent", "code_size"), 0);
                w.Action = (int)PayloadReader.Int(p.Payload, FNum("LockableShowCodeDialogEvent", "action"), 0);   // JOURNAL seulement
                w.DialogClosed = false;
                w.Dialog = true;
                break;
            case "LockableCodeResultEvent":
            {
                int r = (int)PayloadReader.Int(p.Payload, FNum("LockableCodeResultEvent", "result"), 0);   // absent = 0 = UNLOCKED (proto3)
                if (r == 0) w.Unlocked = true; else w.Result = r;
                break;
            }
            case "ExchangeStartedWithStorageEvent":
            case "ExchangeStartedWithMultiTabStorageEvent":
            case "StorageInventoryContentEvent":
                w.StorageOpened = true;
                break;
            case "InteractiveUseErrorEvent":
            {
                int e = (int)PayloadReader.Int(p.Payload, FNum("InteractiveUseErrorEvent", "element_id"), 0);
                if (e == 0 || e == elementId) w.UseError = true;
                break;
            }
            case "LockableStateUpdateHouseDoorEvent":
                w.DoorUpdated = true;
                break;
            case "DialogLeaveEvent":
                w.DialogClosed = true;
                break;
        }
    }

    private static async Task<bool> PollAsync(Func<bool> cond, int timeoutMs, CancellationToken ct)
    {
        long end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            if (cond()) return true;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        return cond();
    }

    /// <summary>Ouvre le coffre à code <paramref name="elementId"/> (le perso est DÉJÀ sur la case d'interaction) : usage de
    /// l'interactif, puis envoi du code si un dialogue de code s'ouvre, jusqu'à l'ouverture du coffre ou l'échec. Le coffre
    /// RESTE OUVERT sur <see cref="LockOutcome.Opened"/> (l'appelant dépose puis ferme). code null = pas de code. Gate Perm.Gather.</summary>
    public async Task<LockOutcome> LockedStorageOpenAsync(uint pid, int elementId, int skillUid, string? code, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return LockOutcome.Denied;
        var relay = RelayForInject(); if (relay is null) return LockOutcome.ForgeFailed;
        var w = new LockWatch();
        void H(DecodedPacket p) => FeedLockWatch(w, pid, elementId, p);
        PacketDecoded += H;
        _chestSession[pid] = 0;
        LockOutcome Fail(LockOutcome o, bool leave)
        {
            if (leave) LeaveDialog(pid);
            _chestSession.TryRemove(pid, out _);
            TransportLog.Line($"[coffre] pid {pid} : element {elementId} skillUid {skillUid} -> {o}");
            return o;
        }
        // Timeout : sous lag le coffre peut s'ouvrir APRÈS l'échéance -> courte grâce (session gardée : la banque reste
        // protégée) ; s'il s'ouvre, on le REFERME (sinon bank:* et la route agiraient sur le coffre de maison).
        async Task<LockOutcome> TimeoutGraceAsync(bool leave)
        {
            if (await PollAsync(() => w.StorageOpened, 2500, ct).ConfigureAwait(false))
            {
                TransportLog.Line($"[coffre] pid {pid} : element {elementId} ouvert après l'échéance -> fermeture");
                await LeaveExchangeAsync(pid, ct).ConfigureAwait(false);
                leave = false;
            }
            return Fail(LockOutcome.Timeout, leave);
        }
        try
        {
            if (!SendInteractiveUseRaw(pid, elementId, skillUid)) return Fail(LockOutcome.ForgeFailed, false);
            await PollAsync(() => w.StorageOpened || w.Dialog || w.UseError, 5000, ct).ConfigureAwait(false);
            if (w.StorageOpened) { TransportLog.Line($"[coffre] pid {pid} : element {elementId} ouvert sans code"); return LockOutcome.Opened; }
            if (w.UseError) return Fail(LockOutcome.Busy, false);
            if (!w.Dialog) return await TimeoutGraceAsync(false).ConfigureAwait(false);

            TransportLog.Line($"[coffre] pid {pid} : dialogue de code (taille {w.CodeSize}, action {w.Action})");
            if (code is null) return Fail(LockOutcome.CodeRequired, true);

            bool sawWrong = false;
            foreach (var v in LockCode.Variants(code, w.CodeSize, Volatile.Read(ref s_lockPadMode) == 2))
            {
                if (sawWrong && w.DialogClosed) break;   // le dialogue s'est fermé sur l'erreur : pas de 2e essai possible
                w.Result = -1;
                byte[] pkt = MoveForge.BuildLockableUseCodeFramed(v);
                if (pkt.Length == 0) { TransportLog.Line($"[coffre] pid {pid} : code non forgé ({MoveForge.LastForgeError})"); return Fail(LockOutcome.ForgeFailed, true); }
                relay.Inject(pid, pkt); ShowInjected(pid, pkt);
                await PollAsync(() => w.StorageOpened || w.Result > 0 || w.UseError, 6000, ct).ConfigureAwait(false);
                if (w.StorageOpened)
                {
                    Volatile.Write(ref s_lockPadMode, v.Length == code.Length ? 1 : 2);
                    TransportLog.Line($"[coffre] pid {pid} : element {elementId} ouvert (variante {(v.Length == code.Length ? "brute" : "complétée")})");
                    return LockOutcome.Opened;
                }
                if (w.Result == 2) return Fail(LockOutcome.Forbidden, true);
                if (w.Result != 1) return sawWrong ? Fail(LockOutcome.WrongCode, true) : await TimeoutGraceAsync(true).ConfigureAwait(false);
                sawWrong = true;                                     // code refusé : variante suivante dans le MÊME dialogue
                await Task.Delay(300, ct).ConfigureAwait(false);     // laisse arriver une éventuelle fermeture du dialogue
            }
            return Fail(LockOutcome.WrongCode, true);
        }
        catch { _chestSession.TryRemove(pid, out _); throw; }
        finally { PacketDecoded -= H; }
    }

    /// <summary>Entre dans la maison dont la porte est <paramref name="elementId"/> (perso DÉJÀ sur la case d'interaction) :
    /// usage de la porte (instance <paramref name="instanceId"/>, 0 = omise), code si demandé, attente du CHANGEMENT DE CARTE.
    /// Gate Perm.Gather.</summary>
    public async Task<LockOutcome> LockedHouseEnterAsync(uint pid, int elementId, int skillUid, int instanceId, string? code, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return LockOutcome.Denied;
        var relay = RelayForInject(); if (relay is null) return LockOutcome.ForgeFailed;
        var w = new LockWatch();
        void H(DecodedPacket p) => FeedLockWatch(w, pid, elementId, p);
        long before = CurrentMap(pid) ?? 0;
        bool Entered() => CurrentMap(pid) is long m && m != before;
        LockOutcome Done(LockOutcome o, bool leave)
        {
            if (leave) LeaveDialog(pid);
            TransportLog.Line($"[maison] pid {pid} : porte {elementId} skillUid {skillUid} instance {instanceId} -> {o}");
            return o;
        }
        PacketDecoded += H;
        try
        {
            if (!SendInteractiveUseRaw(pid, elementId, skillUid, instanceId)) return Done(LockOutcome.ForgeFailed, false);
            await PollAsync(() => Entered() || w.Dialog || w.UseError, 5000, ct).ConfigureAwait(false);
            if (Entered()) return Done(LockOutcome.Entered, false);
            if (w.UseError) return Done(LockOutcome.Busy, false);
            if (!w.Dialog) return Done(LockOutcome.Timeout, false);

            TransportLog.Line($"[maison] pid {pid} : dialogue de code (taille {w.CodeSize}, action {w.Action})");
            if (code is null) return Done(LockOutcome.CodeRequired, true);

            bool sawWrong = false;
            foreach (var v in LockCode.Variants(code, w.CodeSize, Volatile.Read(ref s_lockPadMode) == 2))
            {
                if (sawWrong && w.DialogClosed) break;
                w.Result = -1;
                byte[] pkt = MoveForge.BuildLockableUseCodeFramed(v);
                if (pkt.Length == 0) { TransportLog.Line($"[maison] pid {pid} : code non forgé ({MoveForge.LastForgeError})"); return Done(LockOutcome.ForgeFailed, true); }
                relay.Inject(pid, pkt); ShowInjected(pid, pkt);
                await PollAsync(() => Entered() || w.Result > 0, 6000, ct).ConfigureAwait(false);
                if (Entered())
                {
                    Volatile.Write(ref s_lockPadMode, v.Length == code.Length ? 1 : 2);
                    return Done(LockOutcome.Entered, false);
                }
                if (w.Result == 2) return Done(LockOutcome.Forbidden, true);
                if (w.Result == 1) { sawWrong = true; await Task.Delay(300, ct).ConfigureAwait(false); continue; }
                // Pas de réponse d'échec ni de changement de carte : porte déverrouillée sans entrer (à valider en jeu) ->
                // on ré-utilise la porte UNE fois.
                if (w.DoorUpdated || w.Unlocked)
                {
                    TransportLog.Line($"[maison] pid {pid} : porte {elementId} déverrouillée sans entrée -> nouvel usage");
                    SendInteractiveUseRaw(pid, elementId, skillUid, instanceId);
                    if (await PollAsync(Entered, 5000, ct).ConfigureAwait(false))
                    {
                        Volatile.Write(ref s_lockPadMode, v.Length == code.Length ? 1 : 2);
                        return Done(LockOutcome.Entered, false);
                    }
                }
                return Done(sawWrong ? LockOutcome.WrongCode : LockOutcome.Timeout, true);
            }
            return Done(LockOutcome.WrongCode, true);
        }
        finally { PacketDecoded -= H; }
    }

    /// <summary>Dépose (mode de l'onglet Banque) dans un coffre DÉJÀ OUVERT (lockedStorage), attend la maj du coffre
    /// (best-effort : sac vide = aucun événement), puis ferme. Gate Perm.Gather. Bloquant via GetResult.</summary>
    public async Task<bool> StorageDepositAndCloseAsync(uint pid, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject(); if (relay is null) return false;
        var (mode, pkt) = ConfiguredDepositPacket(pid);
        if (pkt.Length == 0) { await LeaveExchangeAsync(pid, ct).ConfigureAwait(false); _chestSession.TryRemove(pid, out _); return false; }
        var ev = await SendAndWaitForCodeAsync(pid, () => { bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt); return ok; },
            new[] { "StorageObjectsUpdateEvent", "StorageObjectUpdateEvent" }, 3000, ct).ConfigureAwait(false);
        if (ev is null) await Task.Delay(900, ct).ConfigureAwait(false);   // même marge que la banque
        TransportLog.Line($"[coffre] pid {pid} : dépôt (mode={mode}) {(ev is null ? "sans maj reçue" : "confirmé")} -> fermeture.");
        await LeaveExchangeAsync(pid, ct).ConfigureAwait(false);
        _chestSession.TryRemove(pid, out _);
        return true;
    }
}
