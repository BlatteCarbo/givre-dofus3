using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// SIMULATEUR DE TOUR de combat (hors-ligne, sans réseau). Rejoue les MÊMES règles de décision que
/// <see cref="LaunchService.RunFightTurn"/> (rotation par priorité, conditions, ciblage, placement/portée, kite,
/// filet « attaque obligatoire ») sur un plateau fictif, et émet la séquence d'ACTIONS (déplacements + lancers) au
/// lieu d'injecter des paquets. Réutilise TELS QUELS les helpers géométriques du moteur (<c>CanCast</c>,
/// <c>FindCastCell</c>, <c>FindApproachCell</c>, <c>FindKiteCell</c>, <c>OrderTargets</c>, <c>PlacementRange</c>,
/// <see cref="MapPathfinder"/>) -> pas de divergence de géométrie. Le plateau est plat (toutes cases marchables, pas
/// d'obstacle/LdV statique, secteurs connectés) : c'est un OUTIL DE COMPRÉHENSION du comportement, pas un rejeu
/// bit-exact d'un vrai combat. Les dégâts = MOYENNE déterministe (<c>Avg</c>) du COUP DIRECT mono-cible, sans
/// résistance ni riposte (buffs/AoE non modélisés) ; les limites de lancers <c>MaxPerTarget</c>/<c>MaxPerTurn</c>
/// (que RunFightTurn laisse au filet serveur) sont honorées ici pour éviter le spam. <see cref="SimulateFight"/>
/// enchaîne les tours jusqu'à nettoyer le pack.
/// </summary>
public static class CombatSimulator
{
    /// <summary>Un combattant du plateau simulé. PV absolus (Hp/MaxHp) ; <see cref="HpPercent"/> est CALCULÉ.</summary>
    public sealed class SimFighter
    {
        public long ActorId;
        public int Cell;
        public int MaxHp;
        public int Hp;
        public bool Alive = true;
        /// <summary>PV% (0-100) pour les conditions PV de la cible. MaxHp=0 -> 100 (fail-open, comme avant les PV).</summary>
        public int HpPercent => MaxHp > 0 ? (int)(100L * Math.Max(0, Hp) / MaxHp) : 100;
    }

    /// <summary>État initial du plateau : ma case + budget + ennemis.</summary>
    public sealed class SimState
    {
        public long SelfActorId = 1;
        public int SelfCell;
        public int Ap = 6;
        public int Mp = 3;
        public int SelfRange;         // caractéristique Portée (#19), ajoutée aux sorts « boostables »
        public int SelfHpPercent = 100;
        public int TurnNo = 1;
        public List<SimFighter> Enemies = new();
    }

    /// <summary>Un sort de la rotation, données de combat déjà résolues (via <see cref="LaunchService.ResolveSimSpell"/>).</summary>
    public sealed class SimSpell
    {
        public SpellConfig Cfg = null!;
        public string Name = "";
        public int Ap, MinRange, Range;
        public bool CastInLine, CastInDiagonal, CastTestLos, Rangeable, IsBuff;
        // Dégâts (moyenne déterministe) + élément dominant + limites de lancers PAR CIBLE / PAR TOUR (côté jeu).
        public int AvgDamage;
        public int Element = -1;
        public int MaxPerTarget;
        public int MaxPerTurn;
        // Forme d'AoE (shape ASCII Ankama + rayon + rayon min). Shape 0 / Size 0 = mono-cible. >0 -> le cast touche TOUS
        // les combattants dont la cellule est dans la zone (centrée sur la case ciblée, ou sur MOI pour un sort-sur-soi).
        public int ZoneShape, ZoneSize, ZoneMin;
    }

    public enum SimStepKind { Cast, Move, Pass, EndTurn, TurnStart }

    /// <summary>Une action jouée par le bot simulé (pour l'animation + le journal).</summary>
    public sealed class SimStep
    {
        public SimStepKind Kind;
        public int FromCell = -1;      // Move : départ
        public int ToCell = -1;        // Move : arrivée ; Cast : case ciblée
        public string SpellName = "";  // Cast
        public bool OnSelf;            // Cast sur soi
        public int Distance;           // Cast : distance moi->cible
        public int ApAfter, MpAfter;   // budget restant après l'action
        public string NoteKey = "";    // clé i18n de nuance : "onme-buff","onme-zone","approach","recast","kite","lastchance","no-move","no-cast"
        // Dégâts/mort (Cast sur ennemi) : source de VÉRITÉ pour la reconstruction PV côté VM (pas de recalcul).
        public long TargetActorId;
        public int Damage;
        public int Element = -1;
        public int TargetHpAfter;
        public bool Killed;
        public int TurnNo = 1;         // n° du tour auquel ce pas appartient (stampé en fin de SimulateTurn)
        // Cast à ZONE : TOUTES les cibles touchées (actorId, dégâts, PV après, tué). La cible « principale » ci-dessus
        // (TargetActorId/Damage) pilote l'anim du chiffre ; AoeHits reconstruit les PV de TOUS les combattants de l'AoE.
        public List<(long ActorId, int Damage, int HpAfter, bool Killed)> AoeHits = new();
    }

    /// <summary>Rejoue UN tour. Signature simple : délègue à la surcharge <c>out endState</c>.</summary>
    public static List<SimStep> SimulateTurn(SimState start, IReadOnlyList<SimSpell> rotation, CombatProfile profile)
        => SimulateTurn(start, rotation, profile, out _);

    /// <summary>Rejoue UN tour complet et expose l'état FINAL (pour enchaîner les tours). <paramref name="rotation"/> =
    /// sorts activés+résolus (re-triés par priorité). Mute une COPIE de l'état ; ne touche pas l'entrée.</summary>
    public static List<SimStep> SimulateTurn(SimState start, IReadOnlyList<SimSpell> rotation, CombatProfile profile, out SimState endState)
    {
        var steps = new List<SimStep>();
        if (start is null || profile is null) { endState = start ?? new SimState(); return steps; }

        // Copie de travail (l'appelant garde son état initial pour rejouer / rembobiner).
        int myCell = start.SelfCell;
        int ap = start.Ap, mp = start.Mp;
        int myRange = start.SelfRange;
        int turnNo = start.TurnNo < 1 ? 1 : start.TurnNo;
        int selfHp = start.SelfHpPercent;
        long selfId = start.SelfActorId;
        var enemies = start.Enemies.Where(e => e.Alive).Select(e => new SimFighter
        { ActorId = e.ActorId, Cell = e.Cell, MaxHp = e.MaxHp, Hp = e.Hp <= 0 ? e.MaxHp : e.Hp, Alive = true }).ToList();
        int maxCasts = profile.MaxCastsPerTurn <= 0 ? 99 : profile.MaxCastsPerTurn;
        int castsThisTurn = 0;

        // Compteurs de limites de lancers (par-tour et par-cible), remis à zéro à chaque tour (= chaque appel).
        var castsPerSpellTurn = new Dictionary<int, int>();
        var castsPerSpellTarget = new Dictionary<(int, long), int>();

        var rot = rotation.Where(r => r?.Cfg is { Enabled: true, SpellId: > 0 }).OrderBy(r => r.Cfg.Priority).ToList();

        // --- callbacks sur le plateau plat ---
        bool EnemyAt(int c) => enemies.Any(e => e.Alive && e.Cell == c);
        bool Walk(int c) => (uint)c < MapPathfinder.CellCount && !EnemyAt(c);   // ma case exclue par KeyMovements/OrthoReachable
        static bool BlocksLos(int c) => false;
        Func<int, bool> OccFor(int targetCell) { var occ = new HashSet<int>(enemies.Where(e => e.Alive && e.Cell != targetCell).Select(e => e.Cell)); return c => occ.Contains(c); }
        bool EnemyAdjacent(int cell) => enemies.Any(e => e.Alive && MapPathfinder.CellDistance(cell, e.Cell) == 1);
        List<(int Cell, long ActorId)> AliveTuples() => enemies.Where(e => e.Alive).Select(e => (e.Cell, e.ActorId)).ToList();
        int? HpOf(long actorId) => enemies.FirstOrDefault(e => e.ActorId == actorId) is { } f ? f.HpPercent : (int?)null;
        SimFighter Fighter(long actorId) => enemies.First(e => e.ActorId == actorId);

        // Ciblage « finish/execute » (profile.FinishKill) — MIROIR de LaunchService.RunFightTurn.KillFirst : dégâts que la
        // rotation abordable (PA courants, priorité, cap par-cible) peut poser sur UNE cible -> flotte les tuables en tête.
        int KillBudget()
        {
            int budget = ap, dmg = 0;
            foreach (var sp in rot)
            {
                if (sp.Cfg.OnMe || sp.Cfg.TargetKind == SpellTargetKind.Ally || sp.Cfg.IsSummon || sp.AvgDamage <= 0 || sp.Ap <= 0) continue;
                int maxc = Math.Max(1, sp.Cfg.PerTurn); if (sp.MaxPerTarget > 0) maxc = Math.Min(maxc, sp.MaxPerTarget);
                int aff = Math.Min(maxc, budget / sp.Ap);
                if (aff <= 0) continue;
                dmg += aff * sp.AvgDamage; budget -= aff * sp.Ap;
            }
            return dmg;
        }
        List<(int Cell, long ActorId)> KillFirst(List<(int Cell, long ActorId)> ordered)
        {
            if (!profile.FinishKill || ordered.Count < 2) return ordered;
            int budget = KillBudget(); if (budget <= 0) return ordered;
            List<(int Cell, long ActorId)> kill = new(), rest = new();
            foreach (var e in ordered) { var f = enemies.FirstOrDefault(x => x.ActorId == e.ActorId); if (f is { Alive: true } && f.Hp > 0 && f.Hp <= budget) kill.Add(e); else rest.Add(e); }
            if (kill.Count == 0 || rest.Count == 0) return ordered;
            kill.AddRange(rest); return kill;
        }

        // Gates (mêmes seuils que SelfSpellGateOk / TargetSpellGateOk ; PV inconnu -> fail-open).
        bool SelfGate(SpellConfig cfg, int enemyCount)
        {
            if (cfg.MinAp > 0 && ap < cfg.MinAp) return false;
            if (cfg.MinTurn > 0 && turnNo < cfg.MinTurn) return false;
            if (cfg.MinEnemies > 0 && enemyCount < cfg.MinEnemies) return false;
            if ((cfg.SelfHpMin > 0 || cfg.SelfHpMax < 100) && (selfHp < cfg.SelfHpMin || selfHp > cfg.SelfHpMax)) return false;
            return true;
        }
        bool TgtGate(SpellConfig cfg, long actorId)
        {
            if (cfg.TgtHpMin <= 0 && cfg.TgtHpMax >= 100) return true;
            return HpOf(actorId) is not int hp || (hp >= cfg.TgtHpMin && hp <= cfg.TgtHpMax);
        }
        // Limites de lancers côté jeu : tgt=0 -> ne teste que le cap PAR TOUR (buff/sur-soi).
        bool CapOk(SimSpell sp, long tgt)
        {
            int sid = sp.Cfg.SpellId;
            if (sp.MaxPerTurn > 0 && castsPerSpellTurn.GetValueOrDefault(sid) >= sp.MaxPerTurn) return false;
            if (sp.MaxPerTarget > 0 && tgt != 0 && castsPerSpellTarget.GetValueOrDefault((sid, tgt)) >= sp.MaxPerTarget) return false;
            return true;
        }

        // Un lancer : consomme PA, incrémente les compteurs de caps, APPLIQUE les dégâts moyens (coup direct mono-cible)
        // et tue la cible si PV<=0. target=null pour un sort sur soi (buff/zone) : aucun dégât modélisé.
        void EmitCast(SimSpell sp, SimFighter? primary, int targetCell, bool onSelf, string note)
        {
            ap -= sp.Ap; castsThisTurn++;
            int sid = sp.Cfg.SpellId;
            castsPerSpellTurn[sid] = castsPerSpellTurn.GetValueOrDefault(sid) + 1;
            if (primary != null) castsPerSpellTarget[(sid, primary.ActorId)] = castsPerSpellTarget.GetValueOrDefault((sid, primary.ActorId)) + 1;

            var hits = new List<(long ActorId, int Damage, int HpAfter, bool Killed)>();
            long tid = 0; int dmg = 0, hpAfter = 0; bool killed = false;
            if (!sp.IsBuff && sp.AvgDamage > 0)   // sort OFFENSIF (mono OU zone) : applique les dégâts au(x) combattant(s) touché(s)
            {
                // Cellules touchées : la ZONE réelle si connue (centrée sur la case ciblée = MOI pour un sort-sur-soi), sinon
                // la seule case ciblée. Direction = de moi vers la cible (pour les formes directionnelles ligne/cône).
                var cells = (sp.ZoneShape != 0 && sp.ZoneSize > 0)
                    ? new HashSet<int>(CombatZones.GetCells(sp.ZoneShape, targetCell, CombatZones.OrientationTo(myCell, targetCell), sp.ZoneMin, sp.ZoneSize))
                    : new HashSet<int> { targetCell };
                foreach (var f in enemies.Where(e => e.Alive && cells.Contains(e.Cell)).ToList())
                {
                    int d = Math.Min(f.Hp, sp.AvgDamage);
                    f.Hp -= d; bool k = false;
                    if (f.Hp <= 0) { f.Hp = 0; f.Alive = false; k = true; }
                    hits.Add((f.ActorId, d, f.Hp, k));
                }
                // cible « principale » pour l'anim (chiffre/beam) : celle explicitement visée si touchée, sinon la 1re de la zone.
                var ph = primary != null ? hits.FirstOrDefault(h => h.ActorId == primary.ActorId) : default;
                if (ph.ActorId != 0) { tid = ph.ActorId; dmg = ph.Damage; hpAfter = ph.HpAfter; killed = ph.Killed; }
                else if (hits.Count > 0) { tid = hits[0].ActorId; dmg = hits[0].Damage; hpAfter = hits[0].HpAfter; killed = hits[0].Killed; }
            }
            else if (primary != null) { tid = primary.ActorId; hpAfter = primary.Hp; }

            steps.Add(new SimStep
            {
                Kind = SimStepKind.Cast, ToCell = targetCell, SpellName = sp.Name, OnSelf = onSelf,
                Distance = onSelf ? 0 : MapPathfinder.CellDistance(myCell, targetCell),
                ApAfter = ap, MpAfter = mp, NoteKey = note,
                TargetActorId = tid, Damage = dmg, Element = sp.Element, TargetHpAfter = hpAfter, Killed = killed,
                AoeHits = hits,
            });
        }
        // Déplacement simulé (jamais refusé sur plateau plat) : consomme les PM du chemin réel.
        bool DoMove(int to, string note)
        {
            if (to < 0 || to == myCell || !Walk(to)) return false;
            var liveEnemies = enemies.Where(e => e.Alive).ToList();
            if (!CombatMovementDecision.CanMove(profile.MovementPolicy,
                liveEnemies.Count == 0 ? 0 : liveEnemies.Min(e => MapPathfinder.CellDistance(myCell, e.Cell)),
                liveEnemies.Count == 0 ? 0 : liveEnemies.Min(e => MapPathfinder.CellDistance(to, e.Cell)))) return false;
            var key = MapPathfinder.KeyMovements(myCell, to, Walk, allowDiag: false, null);
            if (key.Count == 0) return false;
            int len = MapPathfinder.ExpandPath(key.Select(k => k & 0xFFF).ToList()).Count - 1;
            if (len <= 0 || len > mp) return false;
            int from = myCell; myCell = to; mp -= len;
            steps.Add(new SimStep { Kind = SimStepKind.Move, FromCell = from, ToCell = to, ApAfter = ap, MpAfter = mp, NoteKey = note });
            return true;
        }

        int enemyCountTurn = enemies.Count(e => e.Alive);

        // ===== ROTATION (par priorité) =====
        bool budgetHit = false;
        foreach (var sp in rot)
        {
            if (budgetHit) break;
            var cfg = sp.Cfg;
            if (!SelfGate(cfg, enemyCountTurn)) continue;

            for (int i = 0; i < Math.Max(1, cfg.PerTurn); i++)
            {
                if (castsThisTurn >= maxCasts) { budgetHit = true; break; }
                if (ap < sp.Ap) break;
                if (myCell < 0) { budgetHit = true; break; }
                if (!CapOk(sp, 0)) break;   // cap PAR TOUR atteint (vaut aussi pour buffs) -> stop ce sort

                // Sort sur soi : buff (toujours) ou offensif-zone (seulement si un ennemi est dans le rayon ≈ portée, plancher 2).
                if (cfg.OnMe)
                {
                    if (!sp.IsBuff)
                    {
                        // Zone RÉELLE centrée sur moi si connue (sinon repli rayon = max(portée, 2)). Un ennemi dans la zone -> on lance.
                        bool inZone = (sp.ZoneShape != 0 && sp.ZoneSize > 0)
                            ? new HashSet<int>(CombatZones.GetCells(sp.ZoneShape, myCell, 1, sp.ZoneMin, sp.ZoneSize)) is { } zc && enemies.Any(e => e.Alive && zc.Contains(e.Cell))
                            : enemies.Any(e => e.Alive && MapPathfinder.CellDistance(myCell, e.Cell) <= Math.Max(sp.Range, 2));
                        if (!inZone) break;
                        EmitCast(sp, null, myCell, onSelf: true, note: "onme-zone");
                    }
                    else EmitCast(sp, null, myCell, onSelf: true, note: "onme-buff");
                    continue;
                }

                var alive = AliveTuples();
                if (alive.Count == 0) { budgetHit = true; break; }

                int effRange = sp.Range + (sp.Rangeable ? myRange : 0);
                if (effRange < sp.MinRange) effRange = sp.MinRange;
                var (loR, hiR) = LaunchService.PlacementRange(cfg, sp.MinRange, effRange);
                var castS = (sp.Ap, loR, hiR, sp.CastInLine, sp.CastInDiagonal, sp.CastTestLos);

                var ordered = KillFirst(LaunchService.OrderTargets(profile.Target, alive, myCell, HpOf));

                // OPTIMISEUR D'AoE — MIROIR de RunFightTurn (LaunchService) : pour un sort à zone, réordonne les cases-cibles
                // pour MAXIMISER les ennemis touchés + gate MinAoeHits. Le simulateur ne modélise QUE les ennemis -> pas de
                // clause friendly-fire (contrairement au moteur). Fidélité : la prévisualisation vise la même case que le moteur.
                if (sp.ZoneShape != 0 && sp.ZoneSize > 0 && ordered.Count > 0)
                {
                    var orig = ordered;
                    var scored = ordered.Select(e =>
                    {
                        var zc = new HashSet<int>(CombatZones.GetCells(sp.ZoneShape, e.Cell, CombatZones.OrientationTo(myCell, e.Cell), sp.ZoneMin, sp.ZoneSize));
                        return (e, hit: enemies.Count(f => f.Alive && zc.Contains(f.Cell)));
                    }).ToList();
                    int bestHits = scored.Count > 0 ? scored.Max(x => x.hit) : 0;
                    if (cfg.MinAoeHits > 0 && bestHits < cfg.MinAoeHits) break;   // AoE trop peu peuplée -> sort suivant
                    if (ordered.Count > 1)
                        ordered = scored.OrderByDescending(x => x.hit).ThenBy(x => orig.IndexOf(x.e)).Select(x => x.e).ToList();
                }

                // 1) touchable d'ici (portée + cap par-cible OK) ?
                var hitNow = ordered.FirstOrDefault(e => TgtGate(cfg, e.ActorId) && CapOk(sp, e.ActorId) && LaunchService.CanCast(myCell, e.Cell, castS, BlocksLos, OccFor(e.Cell)));
                if (hitNow.ActorId != 0)
                {
                    EmitCast(sp, Fighter(hitNow.ActorId), hitNow.Cell, onSelf: false, note: "");
                    continue;
                }

                if (profile.Style == CombatStyle.Passive) break;
                if (!CombatMovementDecision.CanReposition(profile.MovementPolicy, profile.Style, cfg.AllowApproach)) break;
                if (EnemyAdjacent(myCell)) break;

                // 2) se placer sur une case de tir puis relancer CE sort de là (cible = qui remplit condition PV + cap).
                bool moved = false;
                foreach (var e in ordered.Where(e => TgtGate(cfg, e.ActorId) && CapOk(sp, e.ActorId)))
                {
                    int castCell = LaunchService.FindCastCell(myCell, e.Cell, castS, mp, Walk, BlocksLos, OccFor(e.Cell), out _, null);
                    if (castCell < 0 || castCell == myCell) continue;
                    if (DoMove(castCell, "approach")) { i--; moved = true; break; }   // retente le sort depuis la nouvelle case
                }
                if (moved) continue;
                break;
            }
        }

        // ===== MOUVEMENT DE FIN DE TOUR selon le STYLE =====
        void RecastAfterApproach()
        {
            int here = myCell;
            foreach (var sp in rot)
            {
                if (sp.Cfg.OnMe) continue;
                if (!SelfGate(sp.Cfg, enemies.Count(e => e.Alive))) continue;
                for (int k = 0; k < Math.Max(1, sp.Cfg.PerTurn); k++)
                {
                    if (castsThisTurn >= maxCasts || ap < sp.Ap || !CapOk(sp, 0)) break;
                    int er = sp.Range + (sp.Rangeable ? myRange : 0); if (er < sp.MinRange) er = sp.MinRange;
                    var (lo2, hi2) = LaunchService.PlacementRange(sp.Cfg, sp.MinRange, er);
                    var cS = (sp.Ap, lo2, hi2, sp.CastInLine, sp.CastInDiagonal, sp.CastTestLos);
                    var hit = KillFirst(LaunchService.OrderTargets(profile.Target, AliveTuples(), here, HpOf))
                        .FirstOrDefault(e => TgtGate(sp.Cfg, e.ActorId) && CapOk(sp, e.ActorId) && LaunchService.CanCast(here, e.Cell, cS, BlocksLos, OccFor(e.Cell)));
                    if (hit.ActorId == 0) break;
                    EmitCast(sp, Fighter(hit.ActorId), hit.Cell, onSelf: false, note: "recast");
                }
            }
        }

        if (mp > 0 && enemies.Any(e => e.Alive))
        {
            if (profile.Style == CombatStyle.Aggressive)
            {
                if (profile.AllowsApproach && !EnemyAdjacent(myCell))
                {
                    foreach (var t in KillFirst(LaunchService.OrderTargets(profile.Target, AliveTuples(), myCell, HpOf)))
                    {
                        int approach = LaunchService.FindApproachCell(myCell, t.Cell, mp, Walk, out _, null);
                        if (approach >= 0 && approach != myCell && DoMove(approach, "approach")) { RecastAfterApproach(); break; }
                    }
                }
            }
            else if (profile.Style == CombatStyle.Fleeing)
            {
                // MIROIR de RunFightTurn (bande de kite) : max>0 -> reste dans [min..max] ; max=0 -> ancien kite (fuir au max).
                var enemyCells = enemies.Where(e => e.Alive).Select(e => e.Cell).ToList();
                int nearest = enemyCells.Min(e => MapPathfinder.CellDistance(myCell, e));
                int bandMax = profile.KiteMaxDistance;
                if (bandMax > 0 && bandMax >= profile.KiteMinDistance)
                {
                    if (nearest < profile.KiteMinDistance || (profile.AllowsApproach && nearest > bandMax))
                    {
                        int dest = LaunchService.FindBandCell(myCell, enemyCells, mp, profile.KiteMinDistance, bandMax, Walk, out _, null);
                        if (dest >= 0 && dest != myCell) DoMove(dest, "kite");
                    }
                }
                else if (nearest < profile.KiteMinDistance)
                {
                    int kite = LaunchService.FindKiteCell(myCell, enemyCells, mp, Walk, out _, null);
                    if (kite >= 0 && kite != myCell) DoMove(kite, "kite");
                }
            }
        }

        // ===== FILET « attaque obligatoire » (portée NATIVE, ne bouge pas ; respecte les caps de lancers) =====
        for (int guard = 0; guard < 12; guard++)
        {
            if (castsThisTurn >= maxCasts) break;
            bool didCast = false;
            foreach (var sp in rot)
            {
                if (sp.Cfg.OnMe || ap < sp.Ap) continue;
                int er = sp.Range + (sp.Rangeable ? myRange : 0); if (er < sp.MinRange) er = sp.MinRange;
                var cS = (sp.Ap, sp.MinRange, er, sp.CastInLine, sp.CastInDiagonal, sp.CastTestLos);   // portée NATIVE (ignore les surcharges Placement)
                foreach (var e in KillFirst(LaunchService.OrderTargets(profile.Target, AliveTuples(), myCell, HpOf)))   // finish-kill aussi dans le filet (miroir moteur)
                {
                    if (!CapOk(sp, e.ActorId) || !LaunchService.CanCast(myCell, e.Cell, cS, BlocksLos, OccFor(e.Cell))) continue;
                    EmitCast(sp, Fighter(e.ActorId), e.Cell, onSelf: false, note: "lastchance");
                    didCast = true; break;
                }
                if (didCast) break;
            }
            if (!didCast) break;
        }

        if (castsThisTurn == 0 && !steps.Any(s => s.Kind == SimStepKind.Move))
            steps.Add(new SimStep { Kind = SimStepKind.Pass, ApAfter = ap, MpAfter = mp, NoteKey = enemies.Any(e => e.Alive) ? "no-cast" : "no-enemy" });

        steps.Add(new SimStep { Kind = SimStepKind.EndTurn, ApAfter = ap, MpAfter = mp });

        foreach (var s in steps) s.TurnNo = turnNo;   // tous les pas de ce tour portent son n°
        endState = new SimState
        {
            SelfActorId = selfId, SelfCell = myCell, Ap = ap, Mp = mp, SelfRange = myRange,
            SelfHpPercent = selfHp, TurnNo = turnNo,
            Enemies = enemies.Select(e => new SimFighter { ActorId = e.ActorId, Cell = e.Cell, MaxHp = e.MaxHp, Hp = e.Hp, Alive = e.Alive }).ToList(),
        };
        return steps;
    }

    /// <summary>Rejoue le COMBAT : enchaîne les tours (budget réinitialisé à chaque tour) jusqu'à ce que tous les ennemis
    /// soient K.O., qu'un tour soit stérile (ni lancer ni déplacement) ou que <paramref name="maxTurns"/> soit atteint.
    /// Insère un pas <see cref="SimStepKind.TurnStart"/> avant chaque tour &gt; 1. « Rejoue MON tour N fois » (pas d'IA adverse).</summary>
    public static List<SimStep> SimulateFight(SimState start, IReadOnlyList<SimSpell> rotation, CombatProfile profile, int maxTurns = 15)
    {
        var all = new List<SimStep>();
        if (start is null || profile is null) return all;

        var cur = new SimState
        {
            SelfActorId = start.SelfActorId, SelfCell = start.SelfCell, Ap = start.Ap, Mp = start.Mp,
            SelfRange = start.SelfRange, SelfHpPercent = start.SelfHpPercent, TurnNo = 1,
            Enemies = start.Enemies.Select(e => new SimFighter
            { ActorId = e.ActorId, Cell = e.Cell, MaxHp = e.MaxHp, Hp = e.MaxHp > 0 ? e.MaxHp : e.Hp, Alive = true }).ToList(),
        };

        for (int t = 1; t <= Math.Max(1, maxTurns); t++)
        {
            if (!cur.Enemies.Any(e => e.Alive)) break;
            cur.TurnNo = t; cur.Ap = start.Ap; cur.Mp = start.Mp;
            if (t > 1) all.Add(new SimStep { Kind = SimStepKind.TurnStart, TurnNo = t, ApAfter = start.Ap, MpAfter = start.Mp });
            var steps = SimulateTurn(cur, rotation, profile, out var end);
            all.AddRange(steps);
            cur = end;
            bool sterile = !steps.Any(s => s.Kind == SimStepKind.Cast || s.Kind == SimStepKind.Move);
            if (!cur.Enemies.Any(e => e.Alive) || sterile) break;
        }
        return all;
    }
}
