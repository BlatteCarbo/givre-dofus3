using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class F02CharacterStateTests
{
    private static ProtoField V(int f, long value) => new() { Field=f, Wire=WireType.Varint, Varint=unchecked((ulong)value) };
    private static ProtoField M(int f, params ProtoField[] fields) => new() { Field=f, Wire=WireType.Len, Message=fields.ToList() };

    [Fact]
    public void Detailed_stats_do_not_use_the_overlapping_usable_field_numbers()
    {
        // Catalogue 3.7 : detailed base=7/equip=5/align=4/temp=1 ; usable base=4/equip=1.
        // L'ancienne heuristique prenait align=4 pour base et perdait equip et temporary.
        var sheet = new[] { M(1,
            M(1,V(2,15),M(5,V(7,1),V(5,25),V(4,3),V(1,2),V(2,-4),V(3,5))),
            M(1,V(2,3),M(5,V(7,49))),
            M(1,V(2,1),M(1,V(4,6),V(1,1),V(6,-3),V(7,3)))) };
        var values = FightStatsReader.ReadSelfCharacteristicMap(sheet)!;
        Assert.Equal(32, values[15]);
        Assert.Equal(49, values[3]);
        Assert.Equal(4, values[1]); // compteur used=3 exclu
    }

    [Fact]
    public void Upgrade_target_uses_base_not_equipment_total_and_keeps_zero_base()
    {
        // Capture officielle : base intelligence 1 + équipement 25 = 26 ; requête cible 2 => total 27.
        var sheet = new[] { M(1,
            M(1,V(2,15),M(5,V(7,1),V(5,25))),
            M(1,V(2,10),M(5,V(5,25))),
            M(1,V(2,23),M(1,V(4,3),V(1,1)))) };
        var bases = FightStatsReader.ReadSelfCharacteristicBases(sheet)!;
        Assert.Equal(1,bases[15]);
        Assert.Equal(0,bases[10]);
        Assert.Equal(3,bases[23]);
        Assert.Equal(26,FightStatsReader.ReadSelfCharacteristicMap(sheet)![15]);
        Assert.Null(FightStatsReader.ReadSelfCharacteristicBases(Array.Empty<ProtoField>()));
    }

    [Fact]
    public void Complete_allocation_preserves_other_stats_and_encodes_invested_budget()
    {
        var bases = CharacteristicUpgradePolicy.Stats.ToDictionary(s=>s.Id,_=>0);
        bases[15]=5; bases[12]=1;
        var allocation=CharacteristicUpgradePolicy.Allocation(bases,"wisdom",2)!;
        Assert.Equal(5,allocation["intelligence"]);
        Assert.Equal(6,allocation["wisdom"]);
        Assert.Equal(6,allocation.Count);
        bases.Remove(10);
        Assert.Null(CharacteristicUpgradePolicy.Allocation(bases,"wisdom",2));
    }

    [Fact]
    public void Allocation_wire_preserves_intelligence_when_upgrading_wisdom()
    {
        var cat=ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory,"data"));
        var bases=CharacteristicUpgradePolicy.Stats.ToDictionary(s=>s.Id,_=>0);
        bases[15]=5; bases[12]=1;
        var allocation=CharacteristicUpgradePolicy.Allocation(bases,"wisdom",2)!;
        var frames=RawProtobuf.ConsumeFrames(MoveForge.BuildCharacteristicUpgradeFramed(allocation),out _);
        var payload=RawProtobuf.FindTypeUrls(RawProtobuf.Parse(Assert.Single(frames))).Single().Payload!;
        Assert.Equal(5UL,payload.Single(f=>f.Field==cat.FieldNumber("CharacterCharacteristicUpgradeRequest","intelligence")).Varint);
        Assert.Equal(6UL,payload.Single(f=>f.Field==cat.FieldNumber("CharacterCharacteristicUpgradeRequest","wisdom")).Varint);
        Assert.Empty(MoveForge.BuildCharacteristicUpgradeFramed(new Dictionary<string,int> { ["wisdom"]=6 }));
    }

    [Theory]
    [InlineData("wisdom",1,3)]
    [InlineData("intelligence",101,102)]
    [InlineData("intelligence",201,303)]
    [InlineData("intelligence",301,604)]
    public void Wire_values_are_invested_points(string stat,int basis,int spent)
        => Assert.Equal(spent,CharacteristicUpgradePolicy.Invested(stat,basis));

    [Theory]
    [InlineData("detailed")]
    [InlineData("usable")]
    public void Empty_value_submessage_keeps_zero_base(string kind)
    {
        var cat = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory,"data"));
        int F(string m,string f) => cat.FieldNumber(m,f)!.Value;
        var empty = new ProtoField { Field=F("CharacterCharacteristic",kind), Wire=WireType.Len, Bytes=Array.Empty<byte>() };
        var sheet = new[] { M(F("CharacterCharacteristicsEvent","stats"),
            M(F("CharacterCharacteristics","characteristics"), V(F("CharacterCharacteristic","characteristic_id"),12), empty)) };
        Assert.Null(empty.Message);
        Assert.Equal(0,FightStatsReader.ReadSelfCharacteristicBases(sheet)![12]);
    }

    [Theory]
    [InlineData("intelligence", 1, 1, 2)]
    [InlineData("intelligence", 99, 2, 100)]
    [InlineData("intelligence", 99, 3, 101)]
    [InlineData("intelligence", 199, 4, 200)]
    [InlineData("intelligence", 299, 6, 300)]
    [InlineData("intelligence", 300, 8, 302)]
    [InlineData("wisdom", 0, 2, 0)]
    [InlineData("wisdom", 0, 7, 2)]
    [InlineData("vitality", 300, 7, 307)]
    [InlineData("invalid", 0, 20, 0)]
    public void Upgrade_budget_respects_cost_and_thresholds(string stat, int basis, int budget, int target)
        => Assert.Equal(target, CharacteristicUpgradePolicy.Target(stat,basis,budget));

    [Theory]
    [InlineData(20,2,97,115)]
    [InlineData(0,2,115,115)]
    [InlineData(500,10,0,107)]
    [InlineData(-1,-1,117,117)]
    public void Life_loss_and_erosion_reduce_separate_values(int loss,int erosion,int life,int max)
    {
        var result = FightLifePolicy.ApplyLoss(new FighterStats(42,6,3,117,117,6,3),loss,erosion);
        Assert.Equal(life,result.Life); Assert.Equal(max,result.MaxLife);
        Assert.Equal(6,result.Pa); Assert.Equal(3,result.Pm);
    }

    [Fact]
    public void Live_damage_fields_keep_erosion_separate_from_element_and_loss()
    {
        var cat=ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory,"data"));
        var hit=new[] { M(cat.FieldNumber("GameActionFightEvent","life_points_lost")!.Value,
            V(1,2),V(2,30),V(4,4),V(5,80480567649),V(6,20)) };
        Assert.Equal(new FightStatsReader.LifeLoss(80480567649,20,2,30),FightStatsReader.ReadLifeLossDetails(hit));
    }

    [Theory]
    [InlineData(30, 50)]
    [InlineData(150, 0)]
    [InlineData(-5, 80)]
    public void Confirmed_shield_loss_does_not_count_as_life_loss(int shieldLoss, int remaining)
    {
        var stats = new FighterStats(42, 6, 3, 117, 117, 6, 3)
        {
            Characteristics = System.Collections.Immutable.ImmutableDictionary<int, int>.Empty
                .Add(FightStatsReader.IdShield, 80)
        };

        var result = FightLifePolicy.ApplyLoss(stats, 20, 2, shieldLoss);

        Assert.Equal(remaining, result.Characteristics[FightStatsReader.IdShield]);
        Assert.Equal(97, result.Life);
        Assert.Equal(115, result.MaxLife);
    }

    [Fact]
    public void Set_effects_are_read_even_when_the_piece_list_is_omitted()
    {
        var update=InventoryReader.ReadSetUpdate(new[] { V(1,5),M(3,V(5,10),V(10,118)) })!.Value;
        Assert.Empty(update.Uids);
        Assert.Equal(118,Assert.Single(update.Effects).ActionId);
        Assert.Equal(10,update.Effects[0].Value);
    }
}
