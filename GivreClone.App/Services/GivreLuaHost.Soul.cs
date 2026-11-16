using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using GivreClone.App.Services.Combat;

namespace GivreClone.App.Services;

// soul: capture d'âmes d'archimonstres, avec les garde-fous de SoulCapture (pierre équipée et assez puissante, sort relancé, fin de combat sûre dans la durée de l'état).
// Le script reste maître : il choisit les groupes à capturer (soul.isCapturable) et appelle soul.tryCapture() à chacun de ses tours, avant combat:playTurn().
public sealed partial class GivreLuaHost
{
    private readonly List<long> _soulHp = new();      // PV totaux des ennemis au début de chacun de MES tours (ce combat)
    private int _soulLastObservedTurn = -1, _soulLastCastTurn, _soulFightKey;

    private void BindSnowSoul(Script s)
    {
        var soul = GetOrCreateTable(s, "soul");
        string Str(CallbackArguments a, int i) => At(a, soul, i).CastToString() ?? "";
        double Dbl(CallbackArguments a, int i, double d) => At(a, soul, i).IsNil() ? d : At(a, soul, i).CastToNumber() ?? d;
        Table Row(params (string k, DynValue v)[] kv) { var t = new Table(s); foreach (var (k, v) in kv) t[k] = v; return t; }

        // un nouveau combat repart de zéro (mesures et dernier lancer oubliés)
        void ResetIfNewFight()
        {
            int key = _launch.IsInFight(_pid) ? HashCode.Combine(_pid, _launch.FightSelfCell(_pid) < 0 ? 0 : 1, _launch.FightersDetail(_pid).Count > 0 ? _launch.FightersDetail(_pid).Min(f => f.ActorId) : 0) : 0;
            if (key != _soulFightKey) { _soulFightKey = key; _soulHp.Clear(); _soulLastObservedTurn = -1; _soulLastCastTurn = 0; }
        }
        (long Hp, int Level, bool Known, int Count) Enemies()
        {
            long hp = 0; int level = 0, n = 0; bool known = true;
            foreach (var f in _launch.FightersDetail(_pid).Where(f => f.IsEnemy && f.Alive && !f.IsSummon))
            {
                n++; hp += Math.Max(0, f.Life);
                if (f.MaxLife <= 0) known = false;
                if (_launch.FighterLevel(_pid, f.ActorId) is { } l) level = Math.Max(level, l); else known = false;
            }
            return (hp, level, known && n > 0, n);
        }
        (int EquippedGid, List<SoulStoneStack> Bag) Stones()
        {
            int equipped = 0; var bag = new List<SoulStoneStack>();
            foreach (var it in _launch.Inventory.Items(_pid))
            {
                if (SoulCapture.StoneById((int)it.Gid) is null) continue;
                if (it.Position != 63 && it.Position >= 0) equipped = (int)it.Gid;
                else bag.Add(new SoulStoneStack((int)it.Gid, (int)it.Uid, it.Qty));
            }
            return (equipped, bag);
        }

        // soul.stones() -> { {gid, power, name}, ... }
        Fill(soul, "stones", DynValue.NewCallback((_, a) =>
        {
            var t = new Table(s); int i = 1;
            foreach (var st in SoulCapture.Stones) t[i++] = DynValue.NewTable(Row(("gid", DynValue.NewNumber(st.GenericId)), ("power", DynValue.NewNumber(st.Power)), ("name", DynValue.NewString(st.Name))));
            return DynValue.NewTable(t);
        }));
        // soul.requiredStone(niveau) -> { gid, power, name } ou nil
        Fill(soul, "requiredStone", DynValue.NewCallback((_, a) =>
            SoulCapture.RequiredStone((int)Dbl(a, 0, 0)) is { } st ? DynValue.NewTable(Row(("gid", DynValue.NewNumber(st.GenericId)), ("power", DynValue.NewNumber(st.Power)), ("name", DynValue.NewString(st.Name)))) : DynValue.Nil));
        // soul.isCapturable(idMonstre) -> bool : le monstre a une âme capturable dans les données du jeu (archimonstres, boss à âme)
        Fill(soul, "isCapturable", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.GameData?.MonsterCapturable((int)Dbl(a, 0, 0)) ?? false)));
        // soul.status() -> { equippedGid, equippedPower, bagCount, enemyHp, highestLevel, levelsKnown, spellId, spellReady }
        Fill(soul, "status", DynValue.NewCallback((_, a) =>
        {
            ResetIfNewFight();
            var (eq, bag) = Stones(); var e = Enemies();
            return DynValue.NewTable(Row(("equippedGid", DynValue.NewNumber(eq)), ("equippedPower", DynValue.NewNumber(SoulCapture.StoneById(eq)?.Power ?? 0)),
                ("bagCount", DynValue.NewNumber(bag.Sum(b => b.Quantity))), ("enemyHp", DynValue.NewNumber(e.Hp)), ("highestLevel", DynValue.NewNumber(e.Level)),
                ("levelsKnown", DynValue.NewBoolean(e.Known)), ("spellId", DynValue.NewNumber(SoulCapture.SpellId))));
        }));

        // soul.observe() : à appeler au DÉBUT de chacun de tes tours (avant d'attaquer) : mesure les dégâts par tour de l'équipe.
        Fill(soul, "observe", DynValue.NewCallback((_, a) =>
        {
            ResetIfNewFight();
            int turn = _launch.FightTurnNo(_pid);
            if (turn == _soulLastObservedTurn) return DynValue.False;
            var e = Enemies();
            if (!e.Known) return DynValue.False;
            _soulLastObservedTurn = turn; _soulHp.Add(e.Hp);
            return DynValue.True;
        }));

        SoulAdvice Advise(CallbackArguments a, out SoulSituation sit)
        {
            ResetIfNewFight();
            var (eq, bag) = Stones(); var e = Enemies();
            int turn = _launch.FightTurnNo(_pid);
            var (ap, _) = _launch.ScriptPointsLeft(_pid);
            int apPerTurn = Math.Max(ap, _launch.SelfStats(_pid)?.Pa ?? 0);
            int spellAp = _launch.GameNames.SpellCombat(SoulCapture.SpellId, _launch.LearnedSpellLevel(_pid, SoulCapture.SpellId))?.Ap ?? 3;
            bool capturable = At(a, soul, 0).Type == DataType.Table ? At(a, soul, 0).Table.Get("capturable").CastToBool() : At(a, soul, 0).CastToBool();
            double window = SoulCapture.StateTurns, margin = 1.25;
            if (At(a, soul, 0).Type == DataType.Table)
            {
                var o = At(a, soul, 0).Table;
                if (o.Get("window").CastToNumber() is { } w) window = w;
                if (o.Get("margin").CastToNumber() is { } m) margin = m;
            }
            bool ready = SoulCapture.CooldownElapsed(turn, _soulLastCastTurn) && ap >= spellAp;
            sit = new SoulSituation(e.Known ? e.Level : 0, capturable, eq, bag, ready, e.Known ? e.Hp : 0, SoulCapture.MeasureDamagePerTurn(_soulHp), apPerTurn, spellAp, (int)window, margin);
            return SoulCapture.Advise(sit);
        }
        DynValue AdviceTable(SoulAdvice adv, SoulSituation sit) => DynValue.NewTable(Row(("action", DynValue.NewString(adv.Action.ToString().ToLowerInvariant())), ("reason", DynValue.NewString(adv.Reason)),
            ("stoneGid", DynValue.NewNumber(adv.StoneGid)), ("stoneUid", DynValue.NewNumber(adv.StoneUid)), ("highestLevel", DynValue.NewNumber(sit.HighestLevel)),
            ("enemyHp", DynValue.NewNumber(sit.EnemyHp)), ("damagePerTurn", DynValue.NewNumber(sit.DamagePerTurn))));

        // soul.advise({ capturable = true, window = 2, margin = 1.25 }) -> { action = "none"|"equip"|"wait"|"cast", reason, stoneGid, stoneUid, ... } : décision sans rien faire
        Fill(soul, "advise", DynValue.NewCallback((_, a) => { var adv = Advise(a, out var sit); return AdviceTable(adv, sit); }));

        // soul.equipStone() -> bool : équipe la pierre conseillée (la plus petite qui convient). ATTENTION : elle prend l'emplacement de l'arme.
        Fill(soul, "equipStone", DynValue.NewCallback((_, a) =>
        {
            var adv = Advise(a, out var unusedSituation);
            return DynValue.NewBoolean(adv.Action == SoulAction.Equip && _launch.EquipItemUid(_pid, adv.StoneUid));
        }));

        // soul.tryCapture({ capturable = true, checkProfitability = true }) -> action, raison : une étape de plus vers la capture, à appeler à chacun de tes tours.
        //   "equipped" : pierre équipée ; "cast" : sort lancé ; "skipped_unprofitable" : archimonstre moins cher que la pierre d'âme.
        Fill(soul, "tryCapture", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(CancellationToken.None);
            if (!_launch.IsInFight(_pid) || !_launch.IsMyTurnOpen(_pid)) return DynValue.NewTuple(DynValue.NewString("none"), DynValue.NewString("pas mon tour de combat"));
            var adv = Advise(a, out var sit);

            // Arbitrage économique : vérifier si le prix de l'archimonstre en HDV couvre le coût de la pierre d'âme requise
            bool checkProfit = At(a, soul, 0).Type == DataType.Table && At(a, soul, 0).Table.Get("checkProfitability").CastToBool();
            if (checkProfit && adv.Action is SoulAction.Equip or SoulAction.Cast)
            {
                int stoneGid = adv.StoneGid > 0 ? adv.StoneGid : sit.EquippedStoneGid;
                long stonePrice = stoneGid > 0 ? _launch.AveragePrice(_pid, stoneGid) : 0;
                
                // Recherche du GID de l'archimonstre présent dans le combat
                var archi = _launch.FightersDetail(_pid)
                    .Where(f => f.IsEnemy && !f.IsSummon && GivreClone.Protocol.ArchMonsters.IsArch((int)f.ActorId))
                    .FirstOrDefault();
                long archiPrice = archi.ActorId != 0 ? _launch.AveragePrice(_pid, (int)archi.ActorId) : 0;

                if (stonePrice > 0 && archiPrice > 0 && archiPrice < stonePrice)
                {
                    Info("combat", $"Capture d'âme ignorée (non rentable) : Archimonstre ({archiPrice:N0} k) < Pierre d'âme ({stonePrice:N0} k)");
                    return DynValue.NewTuple(DynValue.NewString("skipped_unprofitable"), DynValue.NewString($"Archi ({archiPrice:N0} k) moins cher que la pierre ({stonePrice:N0} k)"));
                }
            }

            switch (adv.Action)
            {
                case SoulAction.Equip:
                    return _launch.EquipItemUid(_pid, adv.StoneUid)
                        ? DynValue.NewTuple(DynValue.NewString("equipped"), DynValue.NewString(adv.Reason))
                        : DynValue.NewTuple(DynValue.NewString("wait"), DynValue.NewString("équipement refusé par le jeu"));
                case SoulAction.Cast:
                {
                    // cible : l'ennemi le plus fort du groupe ; le sort doit être réellement lançable d'ici
                    var target = _launch.FightersDetail(_pid).Where(f => f.IsEnemy && f.Alive && !f.IsSummon).OrderByDescending(f => _launch.FighterLevel(_pid, f.ActorId) ?? 0).ThenBy(f => f.Cell).FirstOrDefault();
                    if (target.ActorId == 0) return DynValue.NewTuple(DynValue.NewString("wait"), DynValue.NewString("aucune cible"));
                    var (ok, why) = _launch.ScriptCanCast(_pid, SoulCapture.SpellId, target.Cell, -1);
                    if (!ok) return DynValue.NewTuple(DynValue.NewString("wait"), DynValue.NewString($"sort non lançable d'ici (code {why})"));
                    if (!_launch.CastSpell(_pid, SoulCapture.SpellId, target.Cell, HookEpoch)) return DynValue.NewTuple(DynValue.NewString("wait"), DynValue.NewString("lancer refusé"));
                    _soulLastCastTurn = _launch.FightTurnNo(_pid);
                    Info("combat", $"capture d'âmes lancée : {adv.Reason}");
                    return DynValue.NewTuple(DynValue.NewString("cast"), DynValue.NewString(adv.Reason));
                }
                default:
                    return DynValue.NewTuple(DynValue.NewString(adv.Action.ToString().ToLowerInvariant()), DynValue.NewString(adv.Reason));
            }
        }));
    }
}
