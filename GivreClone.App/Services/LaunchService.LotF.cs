using System;
using System.Collections.Concurrent;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Lot F : survie (PV réels hors combat, régénération, énergie, paliers d'XP, emote « assis ») =====================
// Écrit par les décodeurs (thread réseau) dans des ConcurrentDictionary (valeurs immuables) ; lu par le thread du script
// (GivreLuaHost.Regenerate) et par le tick UI. Aucune écriture EN COMBAT (les PV du combat — jxg/jwe — font foi), sauf la
// graine de fin de combat (EndFight) quand aucune fiche n'est arrivée depuis le début du combat.
public sealed partial class LaunchService
{
    private readonly record struct SelfLifeSnap(int Life, int Max, long AtTick);
    private readonly ConcurrentDictionary<uint, SelfLifeSnap> _selfLife = new();
    private readonly ConcurrentDictionary<uint, SelfLifeSnap> _observedSelfLife = new();
    private readonly ConcurrentDictionary<uint, int> _regenMsPerHp = new();     // krf ; absent = DefaultRegenMsPerHp
    private readonly ConcurrentDictionary<uint, long> _lifeSeq = new();         // ++ uniquement sur une observation serveur
    private readonly ConcurrentDictionary<uint, byte> _lifeStaleSinceFight = new();
    private readonly ConcurrentDictionary<uint, (int Life, int Max)> _preFightLife = new();   // PV hors combat GELÉS au début du combat
    private readonly ConcurrentDictionary<uint, (int Cur, int Max)> _selfEnergy = new();
    private readonly ConcurrentDictionary<uint, (long Floor, long Next)> _xpFloors = new();
    private readonly ConcurrentDictionary<uint, long> _emoteAckAt = new();      // tick du dernier EmotePlayEvent me concernant
    private readonly ConcurrentDictionary<uint, long> _emoteErrAt = new();      // tick du dernier EmotePlayErrorEvent
    private const int DefaultRegenMsPerHp = 1000;

    // ---------------------------------------------------------------- estimation / écriture
    // PV estimés : dernière valeur autoritaire + régénération écoulée (hors combat, vivant, pas déjà plein).
    private int EstimateLife(uint pid, SelfLifeSnap s) => EstimateLife(pid, s, IsInFight(pid));
    private int EstimateLife(uint pid, SelfLifeSnap s, bool inFight)
    {
        if (s.Max <= 0) return s.Life;
        if (inFight || LifeStatusOf(pid) != 0 || s.Life >= s.Max) return Math.Min(s.Life, s.Max);
        int ms = _regenMsPerHp.TryGetValue(pid, out var r) && r > 0 ? r : DefaultRegenMsPerHp;
        long gained = (Environment.TickCount64 - s.AtTick) / ms;
        return (int)Math.Min(s.Max, s.Life + gained);
    }

    private void SetSelfLife(uint pid, int life, int max, string src, bool observed = true)
    {
        if (max <= 0) return;
        life = Math.Clamp(life, 0, max);
        bool changed = !_selfLife.TryGetValue(pid, out var old) || old.Life != life || old.Max != max;
        _selfLife[pid] = new SelfLifeSnap(life, max, Environment.TickCount64);
        if (observed)
        {
            _observedSelfLife[pid] = new SelfLifeSnap(life, max, Environment.TickCount64);
            _lifeSeq.AddOrUpdate(pid, 1, static (_, n) => n + 1);
        }
        _lifeStaleSinceFight.TryRemove(pid, out _);
        if (_fighterStats.TryGetValue(pid, out var byId) && byId.TryGetValue(0, out var b0))   // clé 0 = hors combat -> UI Infos / combat:getHp
            byId[0] = b0 with { Life = life, MaxLife = max };
        FighterStatsUpdated?.Invoke(pid);
        if (changed) TransportLog.Line($"[vie] pid {pid} : PV {life}/{max} ({src})");   // dédup : la fiche refire souvent
    }

    // ---------------------------------------------------------------- lectures publiques (pures, sans gate)
    /// <summary>PV (courants, max) du perso. En combat : mon combattant suivi. Hors combat : dernière valeur autoritaire +
    /// extrapolation de la régénération. null si inconnus.</summary>
    public (int Cur, int Max)? SelfLife(uint pid)
    {
        if (IsInFight(pid))
        {
            long me = SelfActorId(pid);
            if (me != 0 && _fighterStats.TryGetValue(pid, out var b) && b.TryGetValue(me, out var st) && st.MaxLife > 0) return (st.Life, st.MaxLife);
        }
        return _selfLife.TryGetValue(pid, out var s) && s.Max > 0 ? (EstimateLife(pid, s), s.Max) : null;
    }
    /// <summary>PV en % (entier, arrondi bas), null si inconnus.</summary>
    public int? SelfLifePercent(uint pid) => SelfLife(pid) is { Max: > 0 } l ? (int)(100L * l.Cur / l.Max) : null;
    /// <summary>ms par PV régénéré (LifePointsRegenBeginEvent), défaut 1000.</summary>
    public int SelfRegenMsPerHp(uint pid) => _regenMsPerHp.TryGetValue(pid, out var r) && r > 0 ? r : DefaultRegenMsPerHp;
    /// <summary>Compteur des observations serveur des PV : sert à attendre l'effet d'un consommable.</summary>
    public long LifeSeq(uint pid) => _lifeSeq.TryGetValue(pid, out var n) ? n : 0;
    /// <summary>PV de la dernière observation serveur, sans graine ni extrapolation ; null si inconnus.</summary>
    public int? SelfLifeAuthoritative(uint pid) => _observedSelfLife.TryGetValue(pid, out var s) && s.Max > 0 ? s.Life : null;
    /// <summary>Énergie (courante, max), null si inconnue.</summary>
    public (int Cur, int Max)? SelfEnergy(uint pid) => _selfEnergy.TryGetValue(pid, out var e) ? e : null;
    /// <summary>XP totale à laquelle commence le niveau suivant, -1 si inconnue.</summary>
    public long CharacterXpNextLevel(uint pid) => _xpFloors.TryGetValue(pid, out var x) && x.Next > 0 ? x.Next : -1;
    /// <summary>XP totale à laquelle a commencé le niveau courant, -1 si inconnue.</summary>
    public long CharacterXpLevelFloor(uint pid) => _xpFloors.TryGetValue(pid, out var x) ? x.Floor : -1;
    /// <summary>Tick (Environment.TickCount64) du dernier EmotePlayEvent me concernant, 0 si aucun.</summary>
    public long EmoteAckAt(uint pid) => _emoteAckAt.TryGetValue(pid, out var t) ? t : 0;
    /// <summary>Tick du dernier EmotePlayErrorEvent, 0 si aucun.</summary>
    public long EmoteErrorAt(uint pid) => _emoteErrAt.TryGetValue(pid, out var t) ? t : 0;

    // Même modèle de combat, avec la réduction du plafond annoncée par LifePointsLost.
    // Plus gros coup observé par ennemi ce combat (pid -> auteur -> dégâts). Vidé au début de chaque combat.
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, int>> _enemyObservedHit = new();
    private void NoteEnemyHit(uint pid, long source, int amount)
    {
        if (amount <= 0) return;
        _enemyObservedHit.GetOrAdd(pid, static _ => new()).AddOrUpdate(source, amount, (_, v) => Math.Max(v, amount));
    }
    public int? ObservedEnemyHit(uint pid, long source) =>
        _enemyObservedHit.TryGetValue(pid, out var byId) && byId.TryGetValue(source, out var v) ? v : null;
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, ConcurrentDictionary<int, int>>> _observedEnemySpellLevels = new();
    private readonly ConcurrentDictionary<(uint Pid, long Actor), byte> _lifeDriftLogged = new();
    private void ForgetEnemyHits(uint pid)
    {
        _enemyObservedHit.TryRemove(pid, out _);
        _observedEnemySpellLevels.TryRemove(pid, out _);
        foreach (var key in _lifeDriftLogged.Keys) if (key.Pid == pid) _lifeDriftLogged.TryRemove(key, out _);
    }
    private readonly Lazy<Combat.MonsterThreatCatalog> _bestiary = new(() =>
    {
        var catalog = Combat.MonsterThreatCatalog.Load(Combat.MonsterThreatCatalog.DefaultPath);
        TransportLog.Line($"[bestiaire] Dofensive : {catalog.Count} grades de monstres chargés");
        return catalog;
    });
    private Combat.MonsterThreat? MonsterThreat(uint pid, long actor) =>
        _fightMonsters.TryGetValue(pid, out var byId) && byId.TryGetValue(actor, out var monster)
            ? _bestiary.Value.Find(monster.GenericId, monster.Grade) : null;
    private void ApplyObservedLifeLoss(uint pid, FightStatsReader.LifeLoss loss)
    {
        if (!_fighterStats.TryGetValue(pid, out var byId) || !byId.TryGetValue(loss.Target, out var stats)) return;
        byId[loss.Target] = FightLifePolicy.ApplyLoss(stats, loss.Amount, loss.PermanentDamage, loss.ShieldLoss);
        if (loss.Target == SelfActorId(pid)) FighterStatsUpdated?.Invoke(pid);
    }

    private void ApplyObservedLifeGain(uint pid, long target, int amount)
    {
        if (!_fighterStats.TryGetValue(pid, out var byId) || !byId.TryGetValue(target, out var stats)) return;
        byId[target] = FightLifePolicy.ApplyGain(stats, amount);
        if (target == SelfActorId(pid)) FighterStatsUpdated?.Invoke(pid);
    }

    // ---------------------------------------------------------------- action (gate au point d'action)
    /// <summary>Joue une emote (EmotePlayRequest). Sert à s'asseoir (emote 1) pour doubler la régénération. Gaté Perm.Gather
    /// (socle hors combat, comme l'usage d'objets). false si non autorisé / pas de relais / forge impossible / en combat.</summary>
    public bool SendEmote(uint pid, int emoteId)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        if (IsInFight(pid)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildEmotePlayFramed(emoteId);
        if (pkt.Length == 0) { TransportLog.Line($"[vie] pid {pid} : EmotePlayRequest NON forgé ({MoveForge.LastForgeError ?? "code absent du mapping"}) -> pas d'injection"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);   // bumps _lastAction (watchdog)
        TransportLog.Line($"[vie] pid {pid} : EmotePlayRequest emote {emoteId} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // ---------------------------------------------------------------- décodage (thread réseau, appelé par OnDecoded)
    // Fiche perso (CharacterCharacteristicsEvent) : appelé APRÈS l'upsert de la clé 0 (PA/PM/PV max) -> y pose les vrais PV.
    private void OnSelfVitals(DecodedPacket p)
    {
        if (FightStatsReader.ReadSelfVitals(p.Payload) is not { } sv) return;
        if (sv.MaxLife > 0 && !IsInFight(p.Pid))   // en combat : les PV du combat (jxg/jwe) font foi, on ne touche pas au snapshot
            SetSelfLife(p.Pid, sv.MaxLife + (sv.LifeDelta ?? 0), sv.MaxLife, sv.LifeDelta is null ? "fiche, delta absent -> plein" : "fiche");
        if (sv.Energy is int en) _selfEnergy[p.Pid] = (en, sv.MaxEnergy ?? en);
        if (sv.XpNextLevelFloor is long nx) _xpFloors[p.Pid] = (sv.XpLevelFloor ?? 0, nx);
    }

    private void OnLotFPacket(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null) return;
        switch (p.Name)
        {
            case "UpdateLifePointsEvent":
                if (!IsInFight(p.Pid) && FightStatsReader.ReadUpdateLifePoints(p.Payload) is { } ul)
                {
                    // Fin de régénération (life_points_gained présent, = LifePointsRegenEnd Dofus 2) : le taux « assis » ne vaut
                    // plus -> retour au taux par défaut jusqu'au prochain RegenBegin (sinon l'estimation monte ~2x trop vite).
                    if (ul.Gained) _regenMsPerHp.TryRemove(p.Pid, out _);
                    SetSelfLife(p.Pid, ul.Life, ul.Max, ul.Gained ? "fin de régénération" : "mise à jour");
                }
                break;
            case "LifePointsRegenBeginEvent":
                if (FightStatsReader.ReadRegenRate(p.Payload) is int rate)
                {
                    if (_selfLife.TryGetValue(p.Pid, out var s0))   // re-base l'estimation au taux PRÉCÉDENT avant de changer de taux
                        _selfLife[p.Pid] = s0 with { Life = EstimateLife(p.Pid, s0), AtTick = Environment.TickCount64 };
                    int ms = Math.Clamp(rate * 100, 100, 10000);   // Dofus 2 : 1 PV toutes les regenRate×100 ms (à vérifier en jeu)
                    bool same = _regenMsPerHp.TryGetValue(p.Pid, out var old) && old == ms;
                    _regenMsPerHp[p.Pid] = ms;
                    if (!same) TransportLog.Line($"[vie] pid {p.Pid} : régénération {rate} -> {ms} ms/PV");   // renvoyé à chaque carte : log au changement seulement
                }
                break;
            case "EmotePlayEvent":
            {
                var (emo, actor) = FightStatsReader.ReadEmotePlay(p.Payload);
                long me = _charId.TryGetValue(p.Pid, out var c) ? c : 0;
                if (me == 0 || actor == me)
                {
                    _emoteAckAt[p.Pid] = Environment.TickCount64;   // me inconnu : n'importe quelle emote vaut accusé (repli, sans log)
                    if (me != 0) TransportLog.Line($"[vie] pid {p.Pid} : emote {emo} jouée");
                }
                break;
            }
            case "EmotePlayErrorEvent":
                _emoteErrAt[p.Pid] = Environment.TickCount64;
                TransportLog.Line($"[vie] pid {p.Pid} : emote refusée par le serveur");
                break;
        }
    }

    // Début de combat : les PV hors combat deviennent « périmés » jusqu'à la prochaine MAJ autoritaire, et on GÈLE les PV
    // estimés d'avant combat (régénération comprise) : le combattant suivi démarre, lui, à PV max (jxg n'a pas le delta 97).
    private void MarkLifeStaleForFight(uint pid)
    {
        _observedSelfLife.TryRemove(pid, out _);
        _lifeStaleSinceFight[pid] = 1;
        if (_selfLife.TryGetValue(pid, out var s) && s.Max > 0) _preFightLife[pid] = (EstimateLife(pid, s, inFight: false), s.Max);
        else _preFightLife.TryRemove(pid, out _);
    }

    // Graine post-combat (appelée par EndFight AVANT la purge de _fighters : SelfActorId en dépend) si AUCUNE MAJ autoritaire
    // (fiche/krk) n'est arrivée depuis le début du combat. Le lecteur du combattant applique déjà le delta de PV 97 ;
    // pertes et soins mettent ensuite ces PV absolus à jour. Ne pas retrancher une seconde fois la blessure d'entrée.
    // Sans graine possible (combattant absent), on garde les PV d'avant combat SANS compter la durée du combat en régénération.
    // La fiche post-combat écrase ensuite.
    private void SeedLifeAfterFight(uint pid)
    {
        bool hadPre = _preFightLife.TryRemove(pid, out var pre);
        if (!_lifeStaleSinceFight.ContainsKey(pid)) return;
        long meId = SelfActorId(pid);
        if (meId != 0 && _fighterStats.TryGetValue(pid, out var fsEnd) && fsEnd.TryGetValue(meId, out var meSt) && meSt.MaxLife > 0)
        {
            int max = hadPre && pre.Max > 0 ? pre.Max : meSt.MaxLife;   // max hors combat (celui du combattant inclut buffs/érosion)
            // Captured end/sheet pairs preserve the life ratio when erosion
            // is removed, rounding up rather than restoring all lost max HP.
            int life = (int)Math.Clamp(((long)meSt.Life * max + meSt.MaxLife - 1) / meSt.MaxLife, 0L, max);
            SetSelfLife(pid, Math.Clamp(life, 0, max), max, "fin de combat (estimation)", observed: false);
            return;
        }
        _lifeStaleSinceFight.TryRemove(pid, out _);
        if (_selfLife.TryGetValue(pid, out var s))   // pas de graine : PV d'avant combat, horloge de régénération remise à maintenant
            _selfLife[pid] = s with { Life = hadPre ? Math.Min(pre.Life, s.Max) : s.Life, AtTick = Environment.TickCount64 };
    }

    // Changement d'état de vie (CharacterLifeStatusEvent) : la régénération s'arrête (mort/fantôme) ou reprend -> l'horloge
    // d'estimation repart de maintenant (sinon le temps passé en fantôme compterait comme régénéré). En ressuscitant, PV bornés
    // à 1 jusqu'à la prochaine fiche (prudent).
    private void OnLifeStatusChangedLotF(uint pid, int prev, int state)
    {
        _observedSelfLife.TryRemove(pid, out _);
        if (!_selfLife.TryGetValue(pid, out var s)) return;
        int life = prev != 0 && state == 0 ? Math.Min(s.Life, 1) : s.Life;
        _selfLife[pid] = s with { Life = life, AtTick = Environment.TickCount64 };
    }

    private void ForgetLotF(uint pid)
    {
        _observedSelfLife.TryRemove(pid, out _);
        _operatorRecovery.TryRemove(pid, out _);
        _selfLife.TryRemove(pid, out _); _regenMsPerHp.TryRemove(pid, out _); _lifeSeq.TryRemove(pid, out _);
        _lifeStaleSinceFight.TryRemove(pid, out _); _preFightLife.TryRemove(pid, out _); _selfEnergy.TryRemove(pid, out _); _xpFloors.TryRemove(pid, out _);
        _emoteAckAt.TryRemove(pid, out _); _emoteErrAt.TryRemove(pid, out _);
    }
}
