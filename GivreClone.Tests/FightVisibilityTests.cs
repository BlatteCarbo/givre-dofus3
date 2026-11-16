using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class FightVisibilityTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Catalog.FieldNumber(message, field)
        ?? throw new InvalidOperationException($"Missing mapping: {message}.{field}");
    private static ProtoField V(string message, string field, long value) => new()
        { Field = F(message, field), Wire = WireType.Varint, Varint = unchecked((ulong)value) };
    private static ProtoField M(string message, string field, params ProtoField[] children) => new()
        { Field = F(message, field), Wire = WireType.Len, Message = children.ToList() };

    public FightVisibilityTests() => FightVisibilityReader.UseCatalog(Catalog.FieldNumber);

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(42, 1)]
    [InlineData(-9223372036854775807L, 987)]
    public void Initial_state_is_independent_of_characteristic_entries(long actor, int state)
    {
        var tree = new[] { M("FightFighterShowEvent", "information",
            V("ActorPositionInformation", "actor_id", actor),
            M("ActorPositionInformation", "actor_information", M("ActorInformation", "fighter",
                M("FightFighterInformation", "stats", V("FightCharacteristics", "invisibility_state", state))))) };
        Assert.Equal(new FighterVisibilityObservation(actor, state), FightVisibilityReader.ReadInitial(tree));
        Assert.Null(FightStatsReader.ReadShowFighter(tree));
    }

    [Fact]
    public void Change_uses_target_not_source_and_preserves_raw_default_zero()
    {
        var tree = new[] { M("GameActionFightEvent", "invisibility", V("Invisibility", "target_id", -7)) };
        Assert.Equal(new FighterVisibilityObservation(-7, 0), FightVisibilityReader.ReadChange(tree));
        Assert.Null(FightVisibilityReader.ReadChange(Array.Empty<ProtoField>()));
        Assert.Null(FightVisibilityReader.ReadChange(new[] { M("GameActionFightEvent", "invisibility") }));
    }

    [Theory]
    [InlineData(0, FighterVisibility.Invisible)]
    [InlineData(1, FighterVisibility.Detected)]
    [InlineData(2, FighterVisibility.Visible)]
    [InlineData(987, FighterVisibility.Unknown)]
    public void Detection_preserves_visibility_and_lifecycle_clears(int raw, FighterVisibility expected)
    {
        var reducer = new FightVisibilityReducer();
        reducer.Observe(new(-5, raw));
        reducer.Detect(-5);
        Assert.Equal(raw, reducer.Get(-5).RawState);
        Assert.Equal(expected, reducer.Get(-5).Visibility);
        Assert.Equal(FighterPositionCertainty.LastKnown, reducer.Get(-5).PositionCertainty);
        Assert.False(reducer.Get(-5).CanTarget);
        reducer.Remove(-5);
        Assert.Null(reducer.Get(-5).RawState);
        reducer.Observe(new(-5, raw));
        reducer.Reset();
        Assert.Null(reducer.Get(-5).RawState);
        Assert.False(reducer.Get(-5).CanTarget);
    }

    [Fact]
    public void Detection_alone_and_detection_of_visible_state_do_not_confirm_an_attack_cell()
    {
        var reducer = new FightVisibilityReducer();
        reducer.Detect(-6);
        Assert.False(reducer.Get(-6).CanTarget);
        var visible = new FighterVisibilityState(2, FighterVisibility.Visible, FighterPositionCertainty.Confirmed);
        Assert.True(visible.CanTarget);
        Assert.Equal(FighterVisibility.Visible, visible.Detect().Visibility);
        Assert.False(visible.Detect().CanTarget);
    }

    [Fact]
    public void Relocation_of_a_visible_fighter_confirms_its_reported_cell()
    {
        var reducer = new FightVisibilityReducer();
        reducer.Observe(new(-6, 2));
        Assert.False(reducer.Get(-6).CanTarget);
        reducer.Locate(-6);
        Assert.True(reducer.Get(-6).CanTarget);
    }

    [Theory]
    [InlineData(0, FighterVisibility.Invisible)]
    [InlineData(1, FighterVisibility.Detected)]
    [InlineData(987, FighterVisibility.Unknown)]
    public void Relocation_of_a_non_visible_fighter_never_makes_it_targetable(int raw, FighterVisibility expected)
    {
        var reducer = new FightVisibilityReducer();
        reducer.Observe(new(-6, raw));
        reducer.Locate(-6);
        Assert.Equal(expected, reducer.Get(-6).Visibility);
        Assert.False(reducer.Get(-6).CanTarget);
        reducer.Locate(-7);
        Assert.False(reducer.Get(-7).CanTarget);
    }

    [Fact]
    public void Reveal_requires_a_fresh_position_before_the_actor_is_targetable()
    {
        var reducer = new FightVisibilityReducer();
        reducer.Observe(new(-5, 2), positionConfirmed: true);
        Assert.True(reducer.Get(-5).CanTarget);
        reducer.Observe(new(-5, 0));
        reducer.ConfirmPosition(-5);
        Assert.False(reducer.Get(-5).CanTarget);
        reducer.Observe(new(-5, 2));
        Assert.False(reducer.Get(-5).CanTarget);
        reducer.ConfirmPosition(-5);
        Assert.True(reducer.Get(-5).CanTarget);
        reducer.Observe(new(-5, 987), positionConfirmed: true);
        Assert.False(reducer.Get(-5).CanTarget);
    }

    [Fact]
    public void Detection_packet_locates_signed_actor_without_a_visibility_observation()
    {
        MapFightReader.UseCatalog(Catalog.FieldNumber);
        var tree = new[] { M("GameActionFightEvent", "invisible_detected",
            V("InvisibleDetected", "target_id", -9), V("InvisibleDetected", "cell", 123)) };
        var detection = MapFightReader.ReadRelocation(tree);
        Assert.Equal((-9L, 123, "InvisibleDetected"), detection);
        Assert.Null(FightVisibilityReader.ReadChange(tree));
    }
}
