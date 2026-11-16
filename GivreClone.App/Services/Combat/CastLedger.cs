using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GivreClone.App.Services.Combat;

/// <summary>
/// Registre des lancers : mémoire d'un combattant joué (caractère ou compagnon contrôlé)
/// pour les lancers confirmés, refus (174, 175, etc.), temps de relance (recharge) et plafonds par tour.
/// </summary>
public sealed class CastLedger
{
    private int _turnNo;
    private readonly ConcurrentDictionary<int, int> _spellLastCast = new();
    private readonly ConcurrentDictionary<int, int> _spellRefusedAtTurn = new();
    private readonly ConcurrentDictionary<int, int> _castsPerSpell = new();
    private readonly ConcurrentDictionary<(int SpellId, long Target), int> _castsPerTarget = new();
    private readonly HashSet<int> _refusedSpells = new();
    private readonly HashSet<(int SpellId, int Cell)> _refusedCells = new();
    private readonly object _turnLock = new();

    /// <summary>
    /// Ouvre le scope du tour <paramref name="turnNo"/> SI le tour a changé : le PREMIER enregistrement d'un tour
    /// (lancer ou refus, moteur OU script) ouvre son scope, et un enregistrement du MÊME tour ne l'efface pas.
    /// Sans cela, la reprise d'un tour script par le moteur (qui appelle <see cref="StartTurn"/>) effaçait les
    /// lancers déjà confirmés par le script : le compte par sort revenait à 0 et les plafonds du jeu n'étaient
    /// plus honorés sur la fin du tour.
    /// CONTRAT : le n° de tour est celui du combat (le compteur partagé du bot). Dans le chemin normal, le tour
    /// du moteur et le tour du script portent le MÊME numéro (tous deux dérivés de ce compteur), donc
    /// <see cref="StartTurn"/> puis l'import se.font sur le même scope. Un numéro différent ouvre un nouveau
    /// scope : le Residual « numéro de tour divergent » (enregistrement tardif hors tour réclamé) est documenté
    /// dans task-19-lifecycle.md, il n'est pas gardé ici.
    /// </summary>
    private void EnsureTurn(int turnNo)
    {
        if (turnNo == _turnNo) return;
        _turnNo = turnNo;
        _castsPerSpell.Clear();
        _castsPerTarget.Clear();
        _refusedSpells.Clear();
        _refusedCells.Clear();
    }

    /// <summary>
    /// Début de tour : réinitialise le scope de tour SI le numéro de tour a changé. Une seconde prise en charge
    /// du MÊME tour (reprise du moteur sur le tour du script) ne doit donc NI effacer les compteurs déjà posés
    /// (lancers du script) NI les rejouer.
    /// </summary>
    public void StartTurn(int turnNo)
    {
        lock (_turnLock)
        {
            EnsureTurn(turnNo);
        }
    }

    public void ResetFight()
    {
        lock (_turnLock)
        {
            _turnNo = 0;
            _spellLastCast.Clear();
            _spellRefusedAtTurn.Clear();
            _castsPerSpell.Clear();
            _castsPerTarget.Clear();
            _refusedSpells.Clear();
            _refusedCells.Clear();
        }
    }

    public void RecordCast(int spellId, long target, int turnNo)
    {
        lock (_turnLock)
        {
            EnsureTurn(turnNo);
            _spellLastCast[spellId] = turnNo;
            _castsPerSpell.AddOrUpdate(spellId, 1, (_, c) => c + 1);
            if (target != 0)
            {
                _castsPerTarget.AddOrUpdate((spellId, target), 1, (_, c) => c + 1);
            }
        }
    }

    /// <summary>
    /// Compteurs du tour SCRIPT (CastsPerSpell / CastsPerTarget), relevés à leur MAXIMUM : le script reste l'AUTEUR
    /// du compte par sort (Script.cs enregistre son lancer), la reprise du tour par le moteur ne double donc rien —
    /// elle ne fait que compléter ce qui manque (le compte par cible, que le script n'enregistre pas). Une seconde
    /// reprise du MÊME tour ne change donc plus rien.
    /// PRÉMISES du maximum (à maintenir) : (1) DISJOINTION — le registre ne contient PAS déjà la contribution
    /// par cible du script (Script.cs enregistre la cible 0), donc maximum(script, registre) = script + registre ;
    /// le compte par sort, lui, est DÉJÀ dans le registre (Script.cs) et le maximum ne le modifie pas ;
    /// (2) ORDRE — l'import précède toujours les lancers du moteur dans la reprise, donc le registre est vide de
    /// casts moteur au moment du maximum. Un ré-import APRÈS des casts moteur sous-compterait (résidual noted,
    /// non gardé ici) ; (3) CIBLE INCONNUE — un lancer du script sur une case vide n'a pas d'acteur connu : le
    /// script n'enregistre aucune cible, l'import n'a donc rien à restaurer et le compte par cible reste 0
    /// (aucune attribution de cible inventée) : seul le plafond par tour lie pour ce lancer.
    /// </summary>
    public void ImportScriptCasts(int turnNo, IReadOnlyDictionary<int, int> castsPerSpell,
        IReadOnlyDictionary<(int Spell, long Actor), int> castsPerTarget)
    {
        lock (_turnLock)
        {
            EnsureTurn(turnNo);
            foreach (var kv in castsPerSpell)
                if (_castsPerSpell.GetValueOrDefault(kv.Key) < kv.Value) _castsPerSpell[kv.Key] = kv.Value;
            foreach (var kv in castsPerTarget)
            {
                var key = (kv.Key.Spell, kv.Key.Actor);
                if (_castsPerTarget.GetValueOrDefault(key) < kv.Value) _castsPerTarget[key] = kv.Value;
            }
        }
    }

    public void RecordRefusal(int spellId, int cell, int messageId, int turnNo)
    {
        lock (_turnLock)
        {
            EnsureTurn(turnNo);
            if (messageId == 175)
            {
                _spellRefusedAtTurn[spellId] = turnNo;
            }
            if (messageId == 174)
            {
                _refusedCells.Add((spellId, cell));
            }
            else
            {
                _refusedCells.Add((spellId, cell));
                _refusedSpells.Add(spellId);
            }
        }
    }

    public bool IsRefusedThisTurn(int spellId)
    {
        lock (_turnLock)
        {
            return _refusedSpells.Contains(spellId);
        }
    }

    public bool IsRecentlyRefused(int spellId, int turnNo)
    {
        return _spellRefusedAtTurn.TryGetValue(spellId, out var refusedAt) && turnNo - refusedAt <= 1;
    }

    public bool IsCellRefused(int spellId, int cell)
    {
        lock (_turnLock)
        {
            return _refusedCells.Contains((spellId, cell));
        }
    }

    public bool IsRechargeActive(int spellId, int turnNo, int profileCooldown, int minCastInterval, int initialCooldown)
    {
        if (turnNo <= initialCooldown) return true;
        if (_spellLastCast.TryGetValue(spellId, out var last))
        {
            if (profileCooldown > 0 && turnNo - last <= profileCooldown) return true;
            if (minCastInterval > 0 && turnNo - last < minCastInterval) return true;
        }
        return false;
    }

    public int? GetLastCastTurn(int spellId)
    {
        return _spellLastCast.TryGetValue(spellId, out var last) ? last : null;
    }

    public int GetCastsThisTurn(int spellId)
    {
        lock (_turnLock)
        {
            return _castsPerSpell.GetValueOrDefault(spellId);
        }
    }

    public int GetTargetCastsThisTurn(int spellId, long target)
    {
        lock (_turnLock)
        {
            return _castsPerTarget.GetValueOrDefault((spellId, target));
        }
    }

    public void RecordTargetHit(int spellId, long target)
    {
        if (target == 0) return;
        lock (_turnLock)
        {
            _castsPerTarget.AddOrUpdate((spellId, target), 1, (_, c) => c + 1);
        }
    }

    public IReadOnlyDictionary<int, int> GetSpellCastsSnapshot()
    {
        lock (_turnLock)
        {
            return new Dictionary<int, int>(_castsPerSpell);
        }
    }

    public IReadOnlyDictionary<long, int> GetTargetCastsSnapshot(int spellId)
    {
        lock (_turnLock)
        {
            var result = new Dictionary<long, int>();
            foreach (var kv in _castsPerTarget)
            {
                if (kv.Key.SpellId == spellId)
                {
                    result[kv.Key.Target] = kv.Value;
                }
            }
            return result;
        }
    }

    public bool SpellTurnCapOk(int spellId, int cap, int extraCastsPerTurn)
    {
        int used = GetCastsThisTurn(spellId);
        return cap <= 0 || used < cap + extraCastsPerTurn;
    }

    public bool TargetCapOk(int spellId, long target, int cap, int extraCastsPerTarget)
    {
        int used = GetTargetCastsThisTurn(spellId, target);
        return cap <= 0 || used < cap + extraCastsPerTarget;
    }
}
