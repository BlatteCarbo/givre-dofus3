extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using CastLedger = F06App::GivreClone.App.Services.Combat.CastLedger;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Profile = F06App::GivreClone.App.Services.CombatProfile;
using Speed = F06App::GivreClone.App.Services.CombatSpeed;
using Config = F06App::GivreClone.App.Services.SpellConfig;
using Reader = F06App::GivreClone.App.Services.MapCellReader;
using Names = F06App::GivreClone.App.Services.GameNameResolver;
using Damage = F06App::GivreClone.App.Services.SpellDamageInfo;

namespace GivreClone.Tests;

public class CastLedgerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Script_cast_does_not_charge_again_after_a_server_point_update_during_dispatch(bool observed)
    {
        var fight = NewScriptFight(2, turnAp: 7);
        Map(fight.Service, "_socketInjectors")[ScriptPid] = (Func<byte[], bool>)(_ =>
        {
            if (observed)
                typeof(Launch).GetMethod("ApplyPointUpdate", Hidden)!.Invoke(fight.Service,
                    [ScriptPid, ScriptSelfId, true, 7 - ScriptSpellAp]);
            return true;
        });

        Assert.True(fight.Service.CastSpell(ScriptPid, Cell(3), ScriptSpellId, ScriptEpoch));

        Assert.Equal((7 - ScriptSpellAp, 0), fight.Service.ScriptPointsLeft(ScriptPid));
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));
    }

    [Theory]
    [InlineData(true, 8, 8, 0)]
    [InlineData(false, 3, 7, 3)]
    public void Observed_self_points_replace_the_active_script_budget(bool ap, int value, int expectedAp, int expectedMp)
    {
        var fight = NewScriptFight(2, turnAp: 7);

        typeof(Launch).GetMethod("ApplyPointUpdate", Hidden)!.Invoke(fight.Service,
            [ScriptPid, ScriptSelfId, ap, value]);

        Assert.Equal((expectedAp, expectedMp), fight.Service.ScriptPointsLeft(ScriptPid));
    }

    [Fact]
    public void Recharge_combines_game_and_profile_max()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordCast(10, 0, 1);

        // Profile cooldown = 2, game minCastInterval = 3, initialCooldown = 0
        // At turn 3: 3 - 1 = 2 <= 2 (profile) but < 3 (minCastInterval). Max is 3.
        Assert.True(ledger.IsRechargeActive(10, 3, profileCooldown: 2, minCastInterval: 3, initialCooldown: 0));
        // At turn 4: 4 - 1 = 3 >= 3 -> inactive
        Assert.False(ledger.IsRechargeActive(10, 4, profileCooldown: 2, minCastInterval: 3, initialCooldown: 0));

        // Initial cooldown test
        Assert.True(ledger.IsRechargeActive(10, 4, profileCooldown: 0, minCastInterval: 0, initialCooldown: 5));
        Assert.False(ledger.IsRechargeActive(10, 6, profileCooldown: 0, minCastInterval: 0, initialCooldown: 5));
    }

    [Fact]
    public void Refusal_175_skip_window()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(2);
        ledger.RecordRefusal(10, 150, 175, turnNo: 2);

        Assert.True(ledger.IsRecentlyRefused(10, 2));
        Assert.True(ledger.IsRecentlyRefused(10, 3));
        Assert.False(ledger.IsRecentlyRefused(10, 4));
    }

    [Fact]
    public void Refusal_174_excludes_only_cell()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordRefusal(10, 150, 174, turnNo: 1);

        Assert.True(ledger.IsCellRefused(10, 150));
        Assert.False(ledger.IsCellRefused(10, 151));
        Assert.False(ledger.IsRefusedThisTurn(10));
    }

    [Fact]
    public void Other_refusals_exclude_spell_for_turn()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordRefusal(10, 150, 229, turnNo: 1);

        Assert.True(ledger.IsCellRefused(10, 150));
        Assert.True(ledger.IsRefusedThisTurn(10));
    }

    [Fact]
    public void StartTurn_clears_turn_scope_keeps_fight_scope()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordCast(10, 100, 1);
        ledger.RecordRefusal(20, 150, 229, 1);

        Assert.Equal(1, ledger.GetCastsThisTurn(10));
        Assert.True(ledger.IsRefusedThisTurn(20));

        ledger.StartTurn(2);

        Assert.Equal(0, ledger.GetCastsThisTurn(10));
        Assert.False(ledger.IsRefusedThisTurn(20));
        Assert.True(ledger.IsRechargeActive(10, 2, profileCooldown: 2, minCastInterval: 0, initialCooldown: 0));
    }

    [Fact]
    public void ResetFight_clears_all()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordCast(10, 100, 1);
        ledger.RecordRefusal(20, 150, 175, 1);

        ledger.ResetFight();

        Assert.Equal(0, ledger.GetCastsThisTurn(10));
        Assert.False(ledger.IsRechargeActive(10, 2, profileCooldown: 2, minCastInterval: 0, initialCooldown: 0));
        Assert.False(ledger.IsRecentlyRefused(20, 1));
    }

    [Fact]
    public void RecordTargetHit_increments_only_target_count()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);

        ledger.RecordTargetHit(10, 500);

        // Per-target count is incremented
        Assert.Equal(1, ledger.GetTargetCastsThisTurn(10, 500));
        // Per-spell count is NOT incremented
        Assert.Equal(0, ledger.GetCastsThisTurn(10));
        // Last-cast turn is NOT set
        Assert.Null(ledger.GetLastCastTurn(10));
        Assert.False(ledger.IsRechargeActive(10, 2, profileCooldown: 2, minCastInterval: 0, initialCooldown: 0));
    }

    [Fact]
    public void Refused_cast_leaves_caps_and_last_cast_untouched()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);

        // Record a refusal (not a cast)
        ledger.RecordRefusal(10, 150, 175, turnNo: 1);

        Assert.Equal(0, ledger.GetCastsThisTurn(10));
        Assert.Equal(0, ledger.GetTargetCastsThisTurn(10, 500));
        Assert.Null(ledger.GetLastCastTurn(10));
        Assert.True(ledger.SpellTurnCapOk(10, cap: 1, extraCastsPerTurn: 0));
        Assert.True(ledger.TargetCapOk(10, 500, cap: 1, extraCastsPerTarget: 0));
    }

    [Fact]
    public void Snapshots_reflect_current_turn_casts()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordCast(10, 500, 1);
        ledger.RecordTargetHit(10, 600);
        ledger.RecordCast(20, 0, 1);

        var spellSnapshot = ledger.GetSpellCastsSnapshot();
        Assert.Equal(1, spellSnapshot[10]);
        Assert.Equal(1, spellSnapshot[20]);

        var targetSnapshot10 = ledger.GetTargetCastsSnapshot(10);
        Assert.Equal(1, targetSnapshot10[500]);
        Assert.Equal(1, targetSnapshot10[600]);

        var targetSnapshot20 = ledger.GetTargetCastsSnapshot(20);
        Assert.Empty(targetSnapshot20);
    }

    [Fact]
    public void Unacknowledged_silent_refusal_code_0_excludes_spell_and_cell_and_clears_on_next_turn()
    {
        var ledger = new CastLedger();
        ledger.StartTurn(1);
        ledger.RecordRefusal(spellId: 42, cell: 250, messageId: 0, turnNo: 1);

        Assert.True(ledger.IsRefusedThisTurn(42));
        Assert.True(ledger.IsCellRefused(42, 250));
        Assert.False(ledger.IsCellRefused(42, 251));

        ledger.StartTurn(2);

        Assert.False(ledger.IsRefusedThisTurn(42));
        Assert.False(ledger.IsCellRefused(42, 250));
    }

    [Fact]
    public void RecordCast_counts_the_spell_and_its_target_once()
    {
        // BOOKKEEPING DU REGISTRE SEUL : un lancer enregistré UNE fois consomme une fois le plafond par tour
        // et une fois le plafond par cible. Le cycle de vie du handover script -> moteur est couvert par les
        // régressions CONNECTÉES plus bas (`Script_cast_then_engine_takeover_*`).
        var ledger = new CastLedger();
        ledger.StartTurn(1);

        // 1. Script executes combat:castSpell(cell, spellId=10, actor=500)
        // RecordCast(pid, spellId, FightTurnNo(pid)) in Script.cs:381
        ledger.RecordCast(spellId: 10, target: 500, turnNo: 1);

        // Assert single-cast invariants for a spell with cap = 1 per turn / 1 per target
        Assert.Equal(1, ledger.GetCastsThisTurn(10));
        Assert.Equal(1, ledger.GetTargetCastsThisTurn(10, 500));
        Assert.False(ledger.SpellTurnCapOk(10, cap: 1, extraCastsPerTurn: 0));
        Assert.True(ledger.SpellTurnCapOk(10, cap: 2, extraCastsPerTurn: 0));
        Assert.False(ledger.TargetCapOk(10, 500, cap: 1, extraCastsPerTarget: 0));
        Assert.True(ledger.TargetCapOk(10, 500, cap: 2, extraCastsPerTarget: 0));
    }

    // ── Task 19 : cycle de vie du registre des lancers sur un tour PRIS EN CHARGE par le moteur ─────────────────
    // Régression CONNECTÉE : le test passe par les verbes de production utilisés par l'hôte Lua
    //   combat:castSpell -> LaunchService.Combat.Script.cs CastSpell   (Script.cs ENREGISTRE le lancer : AUTEUR)
    //   combat:playTurn  -> LaunchService.Combat.Script.cs PlayTurnForScriptAsync
    //                    -> LaunchService.Combat.Turn.cs RunFightTurn(scriptDriven: true) -> REPRISE du tour
    // Invariant : un lancer de script CONFIRMÉ compte EXACTEMENT UNE FOIS (par sort ET par cible) pendant la
    // reprise du tour par le moteur ; les plafonds par tour / par cible du jeu restent honorés ; une seconde
    // reprise du MÊME tour ne change rien ; un NOUVEAU tour repart de zéro.
    // Le registre est piloté par le NUMÉRO DE TOUR : un enregistrement ouvre le scope de son tour, et un
    // enregistrement du MÊME tour ne l'efface pas (une reprise ne doit jamais perdre un lancer déjà compté).
    private const uint ScriptPid = 0x40000219;
    private const long ScriptMapId = 88085763;
    private const long ScriptSelfId = 123;
    private const long ScriptEnemyId = -100;
    private const int ScriptSpellId = 42;
    private const int ScriptSpellGrade = 1;
    private const int ScriptSpellAp = 3;
    private const int ScriptTurnAp = 6;
    private const int ScriptSpellRange = 3;
    private const int ScriptEpoch = 7;

    private sealed class ScriptFight(Launch service, List<byte[]> frames, Type turnType, object turn)
    {
        public Launch Service { get; } = service;
        public List<byte[]> Frames { get; } = frames;
        public Type TurnType { get; } = turnType;
        public object Turn { get; set; } = turn;
        public CastLedger Ledger =>
            ((ConcurrentDictionary<uint, CastLedger>)Field("_castLedgers").GetValue(Service)!)[ScriptPid];
    }

    private static ScriptFight NewScriptFight(int cap, int profilePerTurn = 1, int profileMaxCasts = 99, int turnAp = ScriptTurnAp)
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden))
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
        foreach (string name in new[] { "_combatMapGate", "_movementDispatchGate", "_fightersLock" })
            Field(name).SetValue(service, new object());
        // Vue d'équipe (champ VOLATILE, pas un ConcurrentDictionary) : sans elle, la reprise du tour plante
        // dans KillFirst -> TeamViewOf.
        var teamViewType = typeof(Launch).GetNestedType("TeamView")!;
        Field("_teamView").SetValue(service,
            Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(uint), teamViewType)));
        foreach (string name in new[] { "_engineControlled", "_teamIntents" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));

        // Profil : un sort offensif mono-cible, plafonds par tour / par cible = `cap`, budget global de tours.
        var profile = new Profile { AutoFight = true, AutoSpells = false, Speed = Speed.Instant, MaxCastsPerTurn = profileMaxCasts };
        profile.Spells.Add(new Config { SpellId = ScriptSpellId, PerTurn = profilePerTurn });
        Map(service, "_combat")[ScriptPid] = profile;
        Map(service, "_charId")[ScriptPid] = ScriptSelfId;
        Map(service, "_fighters")[ScriptPid] = new (int Cell, long ActorId)[] { (Cell(0), ScriptSelfId), (Cell(3), ScriptEnemyId) };
        Map(service, "_inFight")[ScriptPid] = true;
        Map(service, "_myTurnOpen")[ScriptPid] = true;
        Map(service, "_jzcSeen")[ScriptPid] = true;
        Map(service, "_turnEpoch")[ScriptPid] = ScriptEpoch;
        Map(service, "_fightTurnNo")[ScriptPid] = 1;
        Map(service, "_charSpells")[ScriptPid] = new[] { ScriptSpellId };
        Map(service, "_charSpellLevels")[ScriptPid] = new Dictionary<int, int> { [ScriptSpellId] = ScriptSpellGrade };
        Map(service, "_fighterStats")[ScriptPid] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(ScriptSelfId, new(ScriptSelfId, turnAp, 0, 100, 100, turnAp, 0)),
            new KeyValuePair<long, FighterStats>(ScriptEnemyId, new(ScriptEnemyId, 0, 0, 1000, 1000, 0, 0))
        });
        var visibility = new FightVisibilityReducer();
        visibility.Observe(new(ScriptEnemyId, 2), positionConfirmed: true);
        Map(service, "_fighterVisibility")[ScriptPid] = visibility;
        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Hidden)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(ScriptMapId, Enumerable.Range(0, 4)
                    .Select(i => new MapCell(Cell(i), true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[ScriptPid] = ScriptMapId;
        Map(service, "_serverMapId")[ScriptPid] = ScriptMapId;
        var names = new Names();
        typeof(Names).GetField("_combatDone", Hidden)!.SetValue(names, true);
        typeof(Names).GetField("_spellCombat", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>>
            { [ScriptSpellId] = new() { [ScriptSpellGrade] = (ScriptSpellAp, 1, ScriptSpellRange, 0, false) } });
        typeof(Names).GetField("_spellDamage", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, Damage>>
            { [ScriptSpellId] = new() { [ScriptSpellGrade] = new Damage(10, 10, 0, cap, cap) } });
        Field("_gameNames").SetValue(service, names);
        var frames = new List<byte[]>();
        Map(service, "_socketInjectors")[ScriptPid] = (Func<byte[], bool>)(frame => { frames.Add(frame); return true; });

        // Tour SCRIPT réclamé (TryClaimScriptTurn) : état de tour réel, epoch courante, budget PA/PM du tour.
        var turnType = typeof(Launch).GetNestedType("ScriptTurnState", BindingFlags.NonPublic)!;
        var turn = Activator.CreateInstance(turnType, nonPublic: true)!;
        turnType.GetField("Epoch")!.SetValue(turn, ScriptEpoch);
        turnType.GetField("TurnNo")!.SetValue(turn, 1);
        turnType.GetField("ApLeft")!.SetValue(turn, turnAp);
        turnType.GetField("MpLeft")!.SetValue(turn, 0);
        Map(service, "_scriptTurn")[ScriptPid] = turn;
        return new ScriptFight(service, frames, turnType, turn);
    }

    /// <summary>Nouveau tour script (le précédent est terminé) : nouveau n° de tour, nouvelle epoch, nouveau scope.</summary>
    private static void NewScriptTurn(ScriptFight fight, int turnNo, int epoch, int apLeft)
    {
        var turn = Activator.CreateInstance(fight.TurnType, nonPublic: true)!;
        fight.TurnType.GetField("Epoch")!.SetValue(turn, epoch);
        fight.TurnType.GetField("TurnNo")!.SetValue(turn, turnNo);
        fight.TurnType.GetField("ApLeft")!.SetValue(turn, apLeft);
        fight.TurnType.GetField("MpLeft")!.SetValue(turn, 0);
        Map(fight.Service, "_scriptTurn")[ScriptPid] = turn;
        fight.Turn = turn;
        Map(fight.Service, "_turnEpoch")[ScriptPid] = epoch;
        Map(fight.Service, "_fightTurnNo")[ScriptPid] = turnNo;
        Map(fight.Service, "_myTurnOpen")[ScriptPid] = true;
    }

    /// <summary>Reprise du tour par le moteur (RunFightTurn scriptDriven) : le chemin de production de la reprise.</summary>
    private static Task RunEngineTurn(ScriptFight fight)
        => (Task)typeof(Launch).GetMethod("RunFightTurn", Hidden)!.Invoke(fight.Service,
            [ScriptPid, ScriptEpoch, true, CancellationToken.None, null, 1])!;

    private static int ScriptSpellCount(ScriptFight fight)
        => ((Dictionary<int, int>)fight.TurnType.GetField("CastsPerSpell")!.GetValue(fight.Turn)!)[ScriptSpellId];

    private static int ScriptTargetCount(ScriptFight fight)
        => ((Dictionary<(int, long), int>)fight.TurnType.GetField("CastsPerTarget")!.GetValue(fight.Turn)!)[(ScriptSpellId, ScriptEnemyId)];

    private static string[] FrameCodes(IEnumerable<byte[]> frames) => frames.Select(FrameCode).ToArray();

    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static int Cell(int step) => MapPathfinder.CellFromMapPoint(8 + step, 8);

    private static string FrameCode(byte[] frame)
    {
        var payload = Assert.Single(RawProtobuf.ConsumeFrames(frame, out var rest));
        Assert.Empty(rest);
        return Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(payload))).Code;
    }

    private static (string Cast, string Finish) WireCodes()
    {
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        return (catalog.CodeForName("GameActionFightCastRequest"), catalog.CodeForName("FightTurnFinishRequest"));
    }

    [Fact]
    public async Task Script_cast_then_engine_takeover_counts_the_confirmed_cast_exactly_once()
    {
        // Le script dépense TOUT le budget PA du tour : après la reprise, le moteur n'a plus de PA et ne peut
        // rien lancer -> le registre ne peut contenir QUE le lancer du script (pas de lancer du moteur qui
        // masquerait un compteur effacé).
        var fight = NewScriptFight(cap: 1, turnAp: ScriptSpellAp);
        var (castCode, finishCode) = WireCodes();

        // 1. Le script lance le sort (verbe de production combat:castSpell) : Script.cs ENREGISTRE le lancer
        //    (RecordCast -> ActiveLedger(pid).RecordCast) et remplit ses propres compteurs de tour.
        Assert.True(fight.Service.CastSpell(ScriptPid, Cell(3), ScriptSpellId));
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));   // l'enregistrement AUTEUR du script
        Assert.Equal(1, ScriptSpellCount(fight));
        Assert.Equal(1, ScriptTargetCount(fight));
        int framesBeforeTakeover = fight.Frames.Count;

        // 2. Le script confie le reste du tour au moteur (verbe de production combat:playTurn) : le moteur
        //    prend le relais du MÊME tour (RunFightTurn scriptDriven -> bloc de reprise de Turn.cs).
        Assert.True(await fight.Service.PlayTurnForScriptAsync(ScriptPid, ScriptEpoch, CancellationToken.None));

        // 3. Le lancer CONFIRMÉ du script compte EXACTEMENT UNE FOIS après la reprise : par sort ET par cible.
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));
        Assert.Equal(1, fight.Ledger.GetTargetCastsThisTurn(ScriptSpellId, ScriptEnemyId));
        // 4. Le temps de relance (mémoire de combat, hors scope de tour) du lancer du script est conservé.
        Assert.Equal(1, fight.Ledger.GetLastCastTurn(ScriptSpellId)!.Value);
        // 5. Le plafond du sort (1/tour, 1/cible) est atteint : le moteur ne REJOUE pas le sort du script.
        var takeover = FrameCodes(fight.Frames.Skip(framesBeforeTakeover));
        Assert.DoesNotContain(castCode, takeover);
        // ... et le tour est bien fini par le moteur.
        Assert.Contains(finishCode, takeover);
    }

    [Fact]
    public async Task Script_cast_on_an_empty_cell_counts_the_spell_without_inventing_a_target()
    {
        // Case VIDE : aucun acteur connu (ActorAtCell -> 0). Le script n'enregistre alors AUCUNE cible
        // (Script.cs:390) : le registre doit garder 1 lancer par sort et 0 par cible — AUCUNE attribution de
        // cible inventée. La reprise du tour ne peut rien restaurer côté cible (rien à importer) : le compte
        // par cible reste 0, et seul le plafond PAR TOUR lie pour ce lancer.
        var fight = NewScriptFight(cap: 1, turnAp: ScriptSpellAp);
        int emptyCell = Cell(2);   // personne sur cette case (l'ennemi est sur Cell(3))

        Assert.True(fight.Service.CastSpell(ScriptPid, emptyCell, ScriptSpellId));
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));            // 1 par sort
        Assert.Equal(0, fight.Ledger.GetTargetCastsThisTurn(ScriptSpellId, ScriptEnemyId));  // 0 par cible
        Assert.Equal(1, ScriptSpellCount(fight));
        var scriptTargets = (Dictionary<(int, long), int>)fight.TurnType
            .GetField("CastsPerTarget")!.GetValue(fight.Turn)!;
        Assert.Empty(scriptTargets);                                             // le script non plus

        Assert.True(await fight.Service.PlayTurnForScriptAsync(ScriptPid, ScriptEpoch, CancellationToken.None));

        // Après la reprise : le lancer du script est toujours compté UNE fois par sort, et AUCUNE cible n'est
        // attribuée (0 par cible).
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));
        Assert.Equal(0, fight.Ledger.GetTargetCastsThisTurn(ScriptSpellId, ScriptEnemyId));
    }

    [Theory]
    [InlineData(1, 0)]   // plafond 1/tour et 1/cible : le moteur ne peut PAS rejouer le sort du script
    [InlineData(2, 1)]   // plafond 2 : le moteur peut jouer le sort UNE fois de plus
    [InlineData(3, 1)]   // plafond 3 mais PA restants = 3 (le script en a dépensé 3 sur 6) : une seule fois de plus
    public async Task Engine_takeover_honours_the_caps_of_the_script_cast(int cap, int expectedEngineCasts)
    {
        var fight = NewScriptFight(cap: cap, profilePerTurn: cap);
        var (castCode, _) = WireCodes();

        Assert.True(fight.Service.CastSpell(ScriptPid, Cell(3), ScriptSpellId));
        int framesBeforeTakeover = fight.Frames.Count;
        Assert.True(await fight.Service.PlayTurnForScriptAsync(ScriptPid, ScriptEpoch, CancellationToken.None));

        // Le moteur s'est abstenu (ou non) selon les plafonds : preuve au niveau du RÉSEAU, pas du registre.
        var takeover = FrameCodes(fight.Frames.Skip(framesBeforeTakeover));
        Assert.Equal(expectedEngineCasts, takeover.Count(code => code == castCode));
        // Le total au registre : le lancer du script (1) + les lancers du moteur.
        Assert.Equal(1 + expectedEngineCasts, fight.Ledger.GetCastsThisTurn(ScriptSpellId));
        Assert.Equal(1 + expectedEngineCasts, fight.Ledger.GetTargetCastsThisTurn(ScriptSpellId, ScriptEnemyId));
    }

    [Fact]
    public async Task Repeated_takeover_in_the_same_turn_neither_clears_nor_replays_the_script_cast()
    {
        var fight = NewScriptFight(cap: 1, turnAp: ScriptSpellAp);
        Assert.True(fight.Service.CastSpell(ScriptPid, Cell(3), ScriptSpellId));
        Assert.True(await fight.Service.PlayTurnForScriptAsync(ScriptPid, ScriptEpoch, CancellationToken.None));
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));
        Assert.Equal(1, fight.Ledger.GetTargetCastsThisTurn(ScriptSpellId, ScriptEnemyId));

        // Seconde reprise du MÊME tour (le moteur est rappelé sur le tour du script) : la fenêtre de tour est
        // rouverte, mais c'est le MÊME tour -> le registre ne doit NI être effacé NI rejoué.
        Map(fight.Service, "_myTurnOpen")[ScriptPid] = true;
        await RunEngineTurn(fight);
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));
        Assert.Equal(1, fight.Ledger.GetTargetCastsThisTurn(ScriptSpellId, ScriptEnemyId));
    }

    [Fact]
    public async Task A_new_turn_resets_the_ledger_scope()
    {
        var fight = NewScriptFight(cap: 1, turnAp: ScriptSpellAp);
        Assert.True(fight.Service.CastSpell(ScriptPid, Cell(3), ScriptSpellId));
        Assert.True(await fight.Service.PlayTurnForScriptAsync(ScriptPid, ScriptEpoch, CancellationToken.None));
        Assert.Equal(1, fight.Ledger.GetCastsThisTurn(ScriptSpellId));

        // Tour SUIVANT (le script ne lance rien, le moteur prend le relais) : le scope repart de zéro.
        NewScriptTurn(fight, turnNo: 2, epoch: ScriptEpoch + 1, apLeft: 0);
        await RunEngineTurn(fight);
        Assert.Equal(0, fight.Ledger.GetCastsThisTurn(ScriptSpellId));
        Assert.Equal(0, fight.Ledger.GetTargetCastsThisTurn(ScriptSpellId, ScriptEnemyId));
    }
}
