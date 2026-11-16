using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

// Combat JOUÉ À LA MAIN depuis l'interface du bot (carte de combat cliquable) : on réutilise les verbes d'action du script (placement, prêt, sort, déplacement, fin de tour)
// et on coupe seulement l'automatisme du moteur pendant que le mode manuel est actif.
public sealed partial class LaunchService
{
    public sealed record ManualSpell(int SpellId, int Ap, int MinRange, int Range);
    public sealed record ManualFightSnapshot(bool InFight, string Phase, int TurnNo, int Ap, int Mp, int SelfCell,
        IReadOnlyList<int> Placement, IReadOnlyList<ScriptFighter> Fighters);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, object> _manualOwner = new();

    public bool ManualCombatOn(uint pid) => _manualOwner.ContainsKey(pid);

    /// <summary>Active/désactive le mode « je joue moi-même » : plus de placement, défi ni tour automatiques tant qu'il est actif.</summary>
    public void SetManualCombat(uint pid, bool on)
    {
        if (on)
        {
            _manualOwner.GetOrAdd(pid, static _ => new object());
            // Le mode manuel est une couche séparée : ne jamais remplacer les choix du script.
            CancelAutomaticFightTurn(pid);
        }
        // Les overrides et hooks du script restent propriétaires de leurs réglages.
        else _manualOwner.TryRemove(pid, out _);
    }

    public ManualFightSnapshot ManualSnapshot(uint pid)
    {
        if (!IsInFight(pid)) return new ManualFightSnapshot(false, "", 0, 0, 0, -1, Array.Empty<int>(), Array.Empty<ScriptFighter>());
        var fs = FightersDetail(pid);
        var me = fs.FirstOrDefault(f => f.IsSelf);
        string phase = IsPlacementOpen(pid) ? "placement" : IsMyTurnOpen(pid) ? "myturn" : "wait";
        return new ManualFightSnapshot(true, phase, FightTurnNo(pid), me.Pa, me.Pm, SelfFighterCell(pid), PlacementCells(pid), fs);
    }

    /// <summary>Sorts du personnage avec coût et portée du grade appris (ceux que le jeu connaît ; les autres sont ignorés).</summary>
    public IReadOnlyList<ManualSpell> ManualSpells(uint pid)
    {
        var res = new List<ManualSpell>();
        foreach (int id in LearnedSpells(pid))
            if (_gameNames.SpellCombat(id, LearnedSpellLevel(pid, id)) is { } s) res.Add(new ManualSpell(id, s.Ap, s.MinRange, s.Range));
        return res;
    }

    /// <summary>Cellules où ce sort est lançable depuis ma case (portée, ligne, vue), pour la surbrillance verte.</summary>
    public IReadOnlyList<int> ManualCastCells(uint pid, int spellId)
    {
        int me = SelfFighterCell(pid);
        if (me < 0) return Array.Empty<int>();
        var res = new List<int>();
        for (int c = 0; c < MapPathfinder.CellCount; c++)
            if (c != me && IsFreeOrOccupiedWalkable(pid, c) && ScriptCanCast(pid, spellId, c, me).Ok) res.Add(c);
        return res;
    }

    private bool IsFreeOrOccupiedWalkable(uint pid, int c) => BuildFightGeom(pid) is { } g ? g.Walk(c) || ActorAtCell(pid, c) != 0 : false;

    /// <summary>Cellules atteignables avec mes PM restants (surbrillance bleue).</summary>
    public IReadOnlyList<int> ManualReachCells(uint pid)
    {
        if (BuildFightGeom(pid) is not { } g || g.SelfCell < 0) return Array.Empty<int>();
        int mp = FightersDetail(pid).FirstOrDefault(f => f.IsSelf).Pm;
        if (mp <= 0) return Array.Empty<int>();
        return MapPathfinder.OrthoReachable(g.SelfCell, mp, g.Walk, g.Step).Keys.Where(c => c != g.SelfCell).ToList();
    }

    /// <summary>Bascule sur la variante <paramref name="spellId"/> d'un sort (hors combat) et attend la réponse du serveur. true = variante active.</summary>
    public async Task<bool> SpellVariantSwitchAsync(uint pid, int spellId)
    {
        if (LearnedSpells(pid).Contains(spellId)) return true;
        long seq0 = LastSpellVariantResult(pid).Seq;
        if (!SpellVariantActivate(pid, spellId)) return false;
        for (int w = 0; w < 3000; w += 100)
        {
            var r = LastSpellVariantResult(pid);
            if (r.Seq > seq0 && r.SpellId == spellId) return r.Effective;
            await Task.Delay(100).ConfigureAwait(false);
        }
        return false;
    }

    public Task<(bool Moved, int Cell)> ManualMoveAsync(uint pid, int cell) => ScriptCombatMoveAsync(pid, cell, CancellationToken.None);
}
