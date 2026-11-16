using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class FightControlReaderTests
{
    private static int? Field(string message, string field) => (message, field) switch
    {
        ("FightControlEvent", "fvxb") => 8, ("FightControlEvent", "fvwx") => 9,
        ("FightControlEvent", "spells") => 10,
        ("SpellItem", "spell_id") => 12, ("SpellItem", "spell_level") => 13, _ => null
    };
    private static ProtoField V(int field, long value) => new() { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
    private static ProtoField M(int field, params ProtoField[] children) => new() { Field = field, Wire = WireType.Len, Message = children.ToList() };

    [Fact]
    public void Official_control_grant_reads_observed_master_field_instead_of_metadata()
    {
        int? OfficialField(string message, string field) => (message, field) switch
        {
            ("FightControlEvent", "fvxa") => 1,
            ("FightControlEvent", "fvxb") => 4,
            ("FightControlEvent", "fvwx") => 6,
            _ => null
        };

        var result = FightControlReader.Read([M(1, V(1, 7)), V(4, 33202504034), V(6, 33202504034)], OfficialField);

        Assert.NotNull(result);
        Assert.Equal(33202504034, result.MasterId);
        Assert.Equal(result.MasterId, result.ActorId);
    }

    [Fact]
    public void Signed_companion_and_its_own_spell_grades_follow_the_catalog()
    {
        var result = FightControlReader.Read([V(8, 123456789), V(9, -20000), M(10, V(12, 456), V(13, 3)), M(10, V(12, 457))], Field)!;
        Assert.Equal(123456789, result.MasterId);
        Assert.Equal(-20000, result.ActorId);
        Assert.Equal(3, result.Spells[456]);
        Assert.Equal(1, result.Spells[457]);
    }

    [Fact]
    public void Omitted_slave_restores_master_and_unknown_mapping_does_not_grant_control()
    {
        Assert.Equal(123456789, FightControlReader.Read([V(8, 123456789)], Field)!.ActorId);
        Assert.Null(FightControlReader.Read([V(8, 123456789), V(9, -2)], (_, _) => null));
        Assert.Null(FightControlReader.Read([V(9, -2)], Field));
    }
}
