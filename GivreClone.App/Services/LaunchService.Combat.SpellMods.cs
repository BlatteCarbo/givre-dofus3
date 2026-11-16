using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.Engine;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// Modificateurs de sort (buffs de portée, de PA, de lancers…) de l'acteur qui joue.
// Cause racine des « sorts ignorés par le serveur » en Turbo : la posture de tir du Crâ (32465) change la fenêtre de lancer de
// 32443 de 1-5 à 4-9 ; le bot continuait de viser à 1-3 cases, le serveur ne répondait pas et le tour perdait 0,6 à 5 s.
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<(uint Pid, long Actor), SpellModifierSet> _spellMods = new();
    // Nombre d'ApplySpellModifierEvent reçus par acteur : seule PREUVE que le serveur a appliqué le buff (un lancer ignoré n'en produit pas).
    private readonly ConcurrentDictionary<(uint Pid, long Actor), int> _spellModEvents = new();
    private readonly ConcurrentDictionary<uint, (int Spell, long Actor, int EventsBefore,
        System.Collections.Generic.IReadOnlyList<SpellModifierEffect> Effects, int Turn, long FightStart)> _lastCastForMods = new();
    // ApplySpellModifierEvent only carries actor_id, not the originating cast/request.
    // Once two modifier casts overlap without confirmation, associating a later event
    // to either one would be guesswork: fail closed for that actor until fight cleanup.
    private readonly ConcurrentDictionary<(uint Pid, long Actor), long> _spellModAmbiguous = new();
    private readonly ConcurrentDictionary<uint, object> _spellModGates = new();
    private object SpellModGate(uint pid) => _spellModGates.GetOrAdd(pid, static _ => new object());

    /// <summary>Un sort de la rotation du tour : sa configuration et ses données de lancer COURANTES (mises à jour par les modificateurs).
    /// Se déconstruit en (Cfg, Data) comme l'ancien tuple, donc les boucles « foreach (var (cfg, data) in rotation) » restent valables.</summary>
    private sealed class RotationSpell
    {
        public RotationSpell(SpellConfig cfg, (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos, bool Rangeable)? data) { Cfg = cfg; Data = data; }
        public SpellConfig Cfg { get; }
        public (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos, bool Rangeable)? Data { get; set; }
        public void Deconstruct(out SpellConfig cfg, out (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos, bool Rangeable)? data) { cfg = Cfg; data = Data; }
    }

    private SpellModifierSet SpellModsOf(uint pid, long actor) => _spellMods.GetOrAdd((pid, actor), static _ => new SpellModifierSet());

    /// <summary>Acteur qui joue en ce moment : le compagnon contrôlé pendant un tour de compagnon, sinon mon personnage.</summary>
    private long ActingActorId(uint pid) => _engineControlled.Value is { } c ? c.Fighter.ActorId : SelfActorId(pid);

    /// <summary>Événement réseau par acteur. Un pending unique du même tour peut être
    /// validé après la barrière Turbo, mais jamais après une superposition ambiguë.</summary>
    internal void OnSpellModifierEvent(uint pid, long actor)
    {
        lock (SpellModGate(pid))
        {
            _spellModEvents.AddOrUpdate((pid, actor), 1, static (_, n) => n + 1);
            if (actor == SelfActorId(pid) && _lastCastForMods.TryGetValue(pid, out var last) && last.Actor == actor)
                CommitCastModifiersLocked(pid, System.Math.Max(1, FightTurnNo(pid)));
        }
    }

    /// <summary>Mémorise le lancer qui vient de partir (appelé par SendCast) pour en déduire, une fois confirmé, ses modificateurs.</summary>
    private void NoteCastForModifiers(uint pid, int spellId)
    {
        long actor = ActingActorId(pid);
        int grade = LearnedSpellLevel(pid, spellId);
        var effects = _gameNames.SpellModifierEffects(spellId, grade);
        var descriptor = _gameNames.GetSpellDescriptor(spellId, grade);
        bool mayModify = effects.Count > 0 || (descriptor?.Effects.Any(e => SpellModifierActions.IsModifier(e.ActionId)) ?? false);
        if (descriptor is not null
            && descriptor.Effects.Any(e => e.ActionId is 281 or 290))
        {
            // Capture the variant before the cast can change its state. A generic
            // modifier confirmation does not identify which conditional row applied.
            var fighter = new Combat.SpellFighter(actor, 0, false, 1, 1,
                States: FighterActiveStates(pid, actor).ToImmutableHashSet(), StatesKnown: false,
                AbsentStates: FighterAbsentStates(pid, actor).ToImmutableHashSet());
            var modifiers = descriptor with { Effects = descriptor.Effects
                .Where(e => e.ActionId is 281 or 290).ToImmutableArray() };
            var resolved = Combat.SpellResolver.Resolve(new Combat.SpellBoard(new[] { fighter }), 0, 0,
                modifiers, (_, _) => null, 1);
            effects = effects.Where(e => e.ActionId is not (281 or 290))
                .Concat((resolved.Board.SpellModifiers ?? System.Array.Empty<Combat.SimulatedSpellModifier>())
                    .Where(m => m.TargetId == actor)
                    .Select(m => new SpellModifierEffect(m.ActionId, m.SpellId, m.Value, m.Duration)))
                .ToArray();
        }
        lock (SpellModGate(pid))
        {
            long fightStart = _fightStartAt.GetValueOrDefault(pid);
            var key = (pid, actor);
            if (_spellModAmbiguous.TryGetValue(key, out long ambiguousFight) && ambiguousFight == fightStart)
                return; // No new actor-only event can restore cast-level provenance this fight.

            if (mayModify && effects.Count == 0)
            {
                // The catalog mentions a conditional modifier but its active
                // branch cannot be identified. Its eventual actor-only packet
                // could otherwise be assigned to a later, different spell.
                _spellModAmbiguous[key] = fightStart;
                _lastCastForMods.TryRemove(pid, out _);
                return;
            }
            if (_lastCastForMods.TryGetValue(pid, out var otherActor)
                && otherActor.Actor != actor && otherActor.FightStart == fightStart)
            {
                // A later cast by this actor must not adopt the previous
                // actor's delayed packet if control switches twice.
                _spellModAmbiguous[(pid, otherActor.Actor)] = fightStart;
                _lastCastForMods.TryRemove(pid, out _);
            }
            if (_lastCastForMods.TryGetValue(pid, out var previous)
                && previous.Actor == actor && previous.FightStart == fightStart)
            {
                // An event can arrive after WaitAfterCastCore returned. A subsequent
                // modifier cast destroys the ability to know which cast it confirms.
                // Ordinary casts do not create modifier events and leave the pending intact.
                if (effects.Count == 0) return;
                _spellModAmbiguous[key] = fightStart;
                _lastCastForMods.TryRemove(pid, out _);
                TransportLog.Line($"[combat] modificateurs non attribuables pour acteur {actor} : deux lancers sans confirmation ; mode prudent jusqu'à fin du combat");
                return;
            }
            if (effects.Count == 0) return;
            _lastCastForMods[pid] = (spellId, actor, _spellModEvents.GetValueOrDefault(key),
                effects.ToArray(), System.Math.Max(1, FightTurnNo(pid)), fightStart);
        }
    }

    /// <summary>
    /// Après l'attente du lancer : si le sort porte des effets de modification (données du jeu) ET que le serveur a bien envoyé un
    /// ApplySpellModifierEvent pour l'acteur depuis le lancer, pose les modificateurs. Renvoie vrai si la rotation doit être recalculée.
    /// </summary>
    private bool CommitCastModifiers(uint pid, int turnNo)
    {
        lock (SpellModGate(pid)) return CommitCastModifiersLocked(pid, turnNo);
    }

    private bool CommitCastModifiersLocked(uint pid, int turnNo)
    {
        if (!_lastCastForMods.TryGetValue(pid, out var last)) return false;
        if (last.FightStart != _fightStartAt.GetValueOrDefault(pid) || last.Turn != turnNo
            || (_serverTurnActor.TryGetValue(pid, out long turnActor) && turnActor != last.Actor)
            || (_spellModAmbiguous.TryGetValue((pid, last.Actor), out long ambiguousFight) && ambiguousFight == last.FightStart))
        {
            // A confirmation in another fight/turn or for another actor cannot
            // identify this cast. Never transfer its effects to a newer turn.
            if (last.FightStart == _fightStartAt.GetValueOrDefault(pid))
                _spellModAmbiguous[(pid, last.Actor)] = last.FightStart;
            _lastCastForMods.TryRemove(pid, out _);
            return false;
        }
        var effects = last.Effects;
        if (effects.Count == 0) { _lastCastForMods.TryRemove(pid, out _); return false; }
        // IMPORTANT: do not consume pending when the event is late. It remains
        // available to OnSpellModifierEvent until replaced, invalidated or cleaned up.
        if (_spellModEvents.GetValueOrDefault((pid, last.Actor)) <= last.EventsBefore) return false;
        _lastCastForMods.TryRemove(pid, out _);
        SpellModsOf(pid, last.Actor).Apply(effects, turnNo);
        TransportLog.Line($"[combat] sort {last.Spell} : {effects.Count} modificateur(s) de sort appliqué(s) (tour {turnNo}) -> fenêtres de lancer recalculées");
        return true;
    }

    /// <summary>Données de lancer d'un sort pour l'acteur qui joue, modificateurs actifs appliqués (portée, PA, ligne de vue).</summary>
    private (int Ap, int MinRange, int Range, bool CastInLine, bool CastInDiagonal, bool CastTestLos, bool Rangeable)? SpellCombatLive(uint pid, int spellId, int grade, long? actor = null)
    {
        if (_gameNames.SpellCombat(spellId, grade) is not { } s) return null;
        if (!_spellMods.TryGetValue((pid, actor ?? ActingActorId(pid)), out var mods) || !mods.Any) return s;
        var a = mods.Adjust(spellId, new SpellCastStats(s.Ap, s.MinRange, s.Range, s.CastInLine, s.CastInDiagonal, s.CastTestLos, s.Rangeable));
        return (a.Ap, a.MinRange, a.Range, a.CastInLine, a.CastInDiagonal, a.CastTestLos, a.Rangeable);
    }

    private void ForgetSpellModifiers(uint pid)
    {
        lock (SpellModGate(pid))
        {
            foreach (var key in _spellMods.Keys) if (key.Pid == pid) _spellMods.TryRemove(key, out _);
            foreach (var key in _spellModEvents.Keys) if (key.Pid == pid) _spellModEvents.TryRemove(key, out _);
            foreach (var key in _spellModAmbiguous.Keys) if (key.Pid == pid) _spellModAmbiguous.TryRemove(key, out _);
            _lastCastForMods.TryRemove(pid, out _);
        }
    }
}
