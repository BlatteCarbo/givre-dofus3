using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Lot F (survie) : PV réels hors combat (fiche perso : 0 + 11 + delta 97), énergie (29/47), paliers d'XP, UpdateLifePoints
/// décodé PAR VALEUR (champs obfusqués), taux de régénération, emote « assis » forgée par nom. Lecteurs routés par le VRAI
/// catalogue (TestSetup).
/// </summary>
public class SurvivalTests
{
    [Theory]
    [InlineData("B001B917A002FFFFFFFFFFFFFFFFFF01BA020D10FFFFFFFFFFFFFFFFFF012041", -1L, 65)]
    [InlineData("B001B917A002E282F4F97BBA020810E282F4F97B2058", 33273217378L, 88)]
    public void Captured_heals_read_resolved_amount_for_players_and_monsters(string hex, long target, int amount)
    {
        Assert.Equal((target, amount), FightStatsReader.ReadLifeGain(RawProtobuf.Parse(Convert.FromHexString(hex))));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(46448378210L)]
    public void Combat_healing_is_decoded_and_restores_life_without_undoing_erosion(long target)
    {
        var packet = Parse(M(F("GameActionFightEvent", "life_points_gain"),
            I(F("LifePointsGain", "target_id"), target), I(F("LifePointsGain", "delta"), 300)));
        var gain = FightStatsReader.ReadLifeGain(packet);
        Assert.NotNull(gain);
        Assert.Equal((target, 300), gain.Value);
        var stats = new FighterStats(target, 10, 5, 500, 1000, 10, 5);
        stats = FightLifePolicy.ApplyLoss(stats, 600, 100);
        stats = FightLifePolicy.ApplyGain(stats, gain.Value.Amount);
        Assert.Equal(300, stats.Life);
        Assert.Equal(900, stats.MaxLife);
        Assert.Equal(900, FightLifePolicy.ApplyGain(stats, int.MaxValue).Life);
        Assert.Equal(stats, FightLifePolicy.ApplyGain(stats, -1));
        Assert.Null(FightStatsReader.ReadLifeGain(Parse()));
    }

    // --- encodeur protobuf minimal (copié de Patch361112Tests) ---
    private static byte[] Cat(params byte[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var b = new byte[n];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }

    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }

    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] M(int field, params byte[][] parts)
    {
        var b = Cat(parts);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }

    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    private static ProtocolCatalog Catalog() => ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string name) => Catalog().FieldNumber(message, name)
        ?? throw new InvalidOperationException($"Missing field {message}.{name} in bundled fields.json");
    private static byte[] D(string property, long value) =>
        I(F("CharacterCharacteristicDetailedUsable", property), value);
    private static byte[] Entry(int characteristicId, params byte[][] fields)
    {
        var parts = new List<byte[]> { M(F("CharacterCharacteristic", "usable"), fields) };
        if (characteristicId != 0)
            parts.Add(I(F("CharacterCharacteristic", "characteristic_id"), characteristicId));
        return M(F("CharacterCharacteristics", "characteristics"), parts.ToArray());
    }

    // Fiche perso : PV base 710 (id 0 omis), vitalité 100+1539 (id 11), delta -500 (id 97), énergie 8500/10000 (29/47),
    // PA 7+4 (id 1), paliers d'XP 1000 (#4) / 9000 (#8).
    private static IReadOnlyList<ProtoField> Sheet(bool withDelta)
    {
        var entries = new List<byte[]>
        {
            Entry(FightStatsReader.IdLife, D("base", 710)),
            Entry(FightStatsReader.IdVitality, D("base", 100), D("objects_and_mount_bonus", 1539)),
        };
        if (withDelta) entries.Add(Entry(FightStatsReader.IdCurLifeDelta, D("context_modification", -500)));
        entries.Add(Entry(FightStatsReader.IdEnergy, D("base", 8500)));
        entries.Add(Entry(FightStatsReader.IdMaxEnergy, D("base", 10000)));
        entries.Add(Entry(FightStatsReader.IdPa, D("base", 7), D("objects_and_mount_bonus", 4)));
        entries.Add(I(F("CharacterCharacteristics", "experience_level_floor"), 1000));
        entries.Add(I(F("CharacterCharacteristics", "experience_next_level_floor"), 9000));
        return Parse(M(F("CharacterCharacteristicsEvent", "stats"), entries.ToArray()));
    }

    [Fact]
    public void Self_vitals_are_read_from_the_character_sheet()
    {
        var tree = Sheet(withDelta: true);
        var sv = FightStatsReader.ReadSelfVitals(tree);
        Assert.NotNull(sv);
        Assert.Equal(2349, sv!.Value.MaxLife);
        Assert.Equal(-500, sv.Value.LifeDelta);
        Assert.Equal(8500, sv.Value.Energy);
        Assert.Equal(10000, sv.Value.MaxEnergy);
        Assert.Equal(1000, sv.Value.XpLevelFloor);
        Assert.Equal(9000, sv.Value.XpNextLevelFloor);
        // Non-régression : le lecteur historique (PA/PM/PV max) lit toujours la même fiche.
        Assert.Equal(2349, FightStatsReader.ReadSelfCharacteristics(tree)!.Value.MaxLife);
    }

    [Fact]
    public void Self_vitals_without_delta_is_null_delta()
    {
        var sv = FightStatsReader.ReadSelfVitals(Sheet(withDelta: false));
        Assert.NotNull(sv);
        Assert.Equal(2349, sv!.Value.MaxLife);
        Assert.Null(sv.Value.LifeDelta);
    }

    [Fact]
    public void Update_life_points_is_decoded_by_value()
    {
        var lp = F("UpdateLifePointsEvent", "life_points");
        var max = F("UpdateLifePointsEvent", "max_life_points");
        var gained = F("UpdateLifePointsEvent", "life_points_gained");
        Assert.Equal((1849, 2349, true), FightStatsReader.ReadUpdateLifePoints(Parse(I(lp, 1849), I(max, 2349), I(gained, 120))));
        Assert.Equal((1849, 2349, false), FightStatsReader.ReadUpdateLifePoints(Parse(I(max, 2349), I(lp, 1849))));
        Assert.Equal((0, 2349, false), FightStatsReader.ReadUpdateLifePoints(Parse(I(max, 2349))));
        Assert.Null(FightStatsReader.ReadUpdateLifePoints(Parse()));
    }

    [Fact]
    public void Regen_rate_is_read_by_name()
    {
        Assert.Equal(10, FightStatsReader.ReadRegenRate(Parse(I(F("LifePointsRegenBeginEvent", "regen_rate"), 10))));
        Assert.Null(FightStatsReader.ReadRegenRate(Parse(I(F("LifePointsRegenBeginEvent", "fzfx"), 3))));
    }

    [Fact]
    public void Emote_play_event_is_read_by_name()
    {
        Assert.Equal((1, 123456789L), FightStatsReader.ReadEmotePlay(Parse(
            I(F("EmotePlayEvent", "emote_id"), 1),
            I(F("EmotePlayEvent", "account_id"), 99),
            I(F("EmotePlayEvent", "actor_id"), 123456789))));
        Assert.Equal((0, 0L), FightStatsReader.ReadEmotePlay(Parse()));
    }

    [Fact]
    public void Emote_play_request_is_forged_by_name()
    {
        var cat = Catalog();
        byte[] framed = MoveForge.BuildEmotePlayFramed(1);
        Assert.NotEmpty(framed);
        var frames = RawProtobuf.ConsumeFrames(framed, out var rest);
        Assert.Single(frames);
        Assert.Empty(rest);
        var hits = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frames[0]));
        Assert.Single(hits);
        Assert.Equal(cat.CodeForName("EmotePlayRequest"), hits[0].Code);
        var p = hits[0].Payload ?? new List<ProtoField>();
        int emoteF = F("EmotePlayRequest", "emote_id");
        Assert.Equal(1UL, p.Single(f => f.Field == emoteF).Varint);
        Assert.DoesNotContain(p, f => f.Field == F("EmotePlayRequest", "fxra"));
    }
}
