using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class RawStateMessageReaderTests
{
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly RawStateMessageReader Reader = new("3.7.13.17", Catalog);
    private static byte[] V(int tag, long value) => [.. RawProtobuf.EncodeVarint((ulong)(tag << 3)), .. RawProtobuf.EncodeVarint(unchecked((ulong)value))];
    private static byte[] M(int tag, params byte[][] fields)
    {
        var bytes = fields.SelectMany(f => f).ToArray();
        return [.. RawProtobuf.EncodeVarint((ulong)((tag << 3) | 2)), .. RawProtobuf.EncodeVarint((ulong)bytes.Length), .. bytes];
    }
    private static IReadOnlyList<ProtoField> P(params byte[][] fields) => RawProtobuf.Parse(fields.SelectMany(f => f).ToArray());
    private static IReadOnlyList<ProtoField> Add(int action, params byte[][] effect) => P(M(1, M(1, effect), V(2, action), V(3, -99)));

    [Theory]
    [InlineData(14594, 99, "TE", 37, 2)]
    [InlineData(29098, 96, "TB", 17, 3)]
    [InlineData(29098, 2998, "TB", 17, 3)]
    public void Captured_timed_damage_retains_rolled_value_target_and_turn_boundary(
        int spell, int action, string timing, int value, int grade)
    {
        byte[] trigger = [.. RawProtobuf.EncodeVarint((14UL << 3) | 2), 2, .. System.Text.Encoding.UTF8.GetBytes(timing)];
        var payload = Add(action, V(1, value), M(2, V(1, -1), V(2, -1)), V(3, 1),
            M(4, V(2, 4)), V(5, 20), V(6, 282865), V(8, -2), V(10, 1),
            V(11, 7), V(13, 5), trigger, V(15, grade), V(16, spell));

        var buff = Reader.ReadTimedDamage("juh", payload);

        Assert.NotNull(buff);
        Assert.Equal(value, buff.Value);
        Assert.Equal(-2L, buff.ActorId);
        Assert.Equal(20, buff.NetworkInstanceId);
        Assert.Equal(spell, buff.SourceSpellId);
        Assert.Equal(grade, buff.SourceGrade);
        Assert.Equal(timing, buff.Trigger);
        Assert.Equal(new RawStateTurn(-1, -1), buff.Start);
        Assert.Equal(new RawStateTurn(4, null), buff.End);
        Assert.Null(Reader.ReadTimedDamage("jun", payload));
        Assert.Null(new RawStateMessageReader("3.8.0", Catalog).ReadTimedDamage("juh", payload));
    }

    [Theory]
    [InlineData(32780, 86051)]
    [InlineData(0, 86051)]
    [InlineData(32780, 0)]
    public void Captured_enemy_cast_retains_catalogue_level_identity(int spellId, int levelId)
    {
        // jsq capture21:10:10.860: source=-1, targeted ability, spell32780/row86051.
        var payload = P(M(2, M(2, V(1, levelId), V(2, spellId)), V(5, 370), V(6, -1)),
            V(22, 300), V(36, -1));

        var cast = Reader.ReadSpellCast("jsq", payload);

        Assert.Equal(spellId > 0 && levelId > 0 ? new RawSpellCastObservation(-1, spellId, levelId) : null, cast);
        Assert.Null(Reader.ReadSpellCast("juh", payload));
        Assert.Null(new RawStateMessageReader("3.8.0", Catalog).ReadSpellCast("jsq", payload));
    }

    [Theory]
    [InlineData("D", 23)]
    [InlineData("XD", 24)]
    public void Distribution_capture_preserves_trigger_bytes_and_separate_network_instances(string trigger, int instance)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(trigger);
        byte[] text = [.. RawProtobuf.EncodeVarint((14UL << 3) | 2),
            .. RawProtobuf.EncodeVarint((ulong)bytes.Length), .. bytes];
        var payload = Add(1160, V(1, 999), M(2, V(1, -1), V(2, -1)), M(4, V(2, 4)),
            V(5, instance), V(6, 226051), V(8, -2), V(10, 14578), V(11, 7),
            V(13, 2), text, V(15, 2), V(16, 14577));

        var buff = Reader.ReadTriggeredSubcast("juh", payload);

        Assert.NotNull(buff);
        Assert.Equal(-2L, buff.ActorId);
        Assert.Equal(instance, buff.NetworkInstanceId);
        Assert.Equal(-99L, buff.SourceId);
        Assert.Equal(14577, buff.SourceSpellId);
        Assert.Equal(14578, buff.SubSpellId);
        Assert.Equal(2, buff.SubSpellGrade);
        Assert.Equal(226051, buff.TriggerGroupId);
        Assert.Equal(trigger, buff.Trigger);
        Assert.Equal(new RawStateTurn(-1, -1), buff.Start);
        Assert.Equal(new RawStateTurn(4, null), buff.End);
        Assert.Null(new RawStateMessageReader("3.8.0", Catalog).ReadTriggeredSubcast("juh", payload));
        Assert.Null(Reader.ReadTriggeredSubcast("jun", payload));
    }

    [Theory]
    [InlineData(792)]
    [InlineData(115)]
    public void Sram_passive_capture_only_decodes_the_triggered_subcast_action(int action)
    {
        byte[] trigger = [.. RawProtobuf.EncodeVarint((14UL << 3) | 2), 4, 73, 79, 70, 70];
        var payload = Add(action, V(1, 1), M(2, V(1, -1), V(2, -1)), M(4, V(2, -1)),
            V(5, 4), V(6, 269632), V(8, 39467549025), V(10, 21979), V(11, 7),
            V(13, 1), trigger, V(15, 1), V(16, 21978));

        var buff = Reader.ReadTriggeredSubcast("juh", payload);

        if (action != 792)
        {
            Assert.Null(buff);
            return;
        }
        Assert.NotNull(buff);
        Assert.Equal("IOFF", buff.Trigger);
        Assert.Equal(792, buff.ActionId);
        Assert.Equal(21978, buff.SourceSpellId);
        Assert.Equal(21979, buff.SubSpellId);
        Assert.Equal(1, buff.SubSpellGrade);
        Assert.Equal(39467549025L, buff.ActorId);
        Assert.Equal(4, buff.NetworkInstanceId);
        Assert.Equal(new RawStateTurn(-1, null), buff.End);
    }

    [Theory]
    [InlineData(14617)]
    [InlineData(0)]
    public void Buff_source_spell_is_field16_independent_of_state_action(int spellId)
    {
        Assert.Equal(spellId, Reader.ReadSourceSpellId("juh", Add(138, V(15, 3), V(16, spellId))));
        Assert.Null(Reader.ReadSourceSpellId("juh", Add(138, V(15, spellId))));
        Assert.Null(Reader.ReadSourceSpellId("jun", Add(138, V(16, spellId))));
        Assert.Null(new RawStateMessageReader("3.8.0", Catalog).ReadSourceSpellId("juh", Add(138, V(16, spellId))));
        Assert.Null(Reader.ReadSourceSpellId("juh", Add(138, V(16, spellId), V(16, spellId))));
        Assert.Null(Reader.ReadSourceSpellId("juh", Add(138, M(16, V(1, spellId)))));
    }

    [Theory]
    [InlineData(950)]
    [InlineData(951)]
    [InlineData(952)]
    public void Add_reads_nested_state_identity_and_round_order_without_activity_claim(int action)
    {
        var payload = Add(action, V(1, 76), V(5, 123), V(8, -9000000001),
            M(2, V(1, -2), V(2, 3)), M(4, V(1, 7), V(2, 8)));

        var observation = Reader.Read("juh", payload);

        Assert.Equal(new RawStateAddObservation(-9000000001, 123, action, 76, -99,
            new(3, -2), new(8, 7), null, null), observation);
        Assert.Equal(new RawStateInstance(123, 1), observation!.ScopedInstance);
    }

    [Fact]
    public void Missing_scalars_and_endpoint_differ_from_explicit_zero_and_empty_endpoint()
    {
        var observation = Reader.Read("juh", Add(950, V(1, 0), M(2)));

        Assert.Equal(new RawStateAddObservation(null, null, 950, 0, -99,
            new(null, null), null, null, null), observation);
        Assert.Null(observation!.ScopedInstance);
    }

    [Theory]
    [InlineData(-7, -1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(42, 42, 1)]
    public void Scoped_remove_preserves_signed_id_and_does_not_call_sentinel_bulk(int wireId, int baseId, int scope)
    {
        var observation = Reader.Read("juu", P(V(1, wireId), V(2, long.MinValue), V(3, 99), M(4)));

        Assert.Equal(new RawStateRemoveObservation(long.MinValue, wireId), observation);
        Assert.Equal(new RawStateInstance(baseId, scope), observation!.ScopedInstance);
    }

    [Fact]
    public void Remove_omitted_scalars_remain_missing_not_explicit_zero()
    {
        Assert.Equal(new RawStateRemoveObservation(null, null), Reader.Read("juu", P()));
        Assert.Equal(new RawStateRemoveObservation(0, 0), Reader.Read("juu", P(V(1, 0), V(2, 0))));
    }

    [Theory]
    [InlineData("3.7.0.0", true)]
    [InlineData("3.7.99.123", true)]
    [InlineData("3.8.13.17", false)]
    [InlineData("3.6.13.17", false)]
    [InlineData("3.7", false)]
    [InlineData("3.7.013.17", false)]
    [InlineData(null, false)]
    public void Supplied_family_not_patch_fingerprint_controls_schema_admission(string? version, bool accepted)
    {
        var observation = new RawStateMessageReader(version, Catalog).Read("juu", P(V(1, 1), V(2, 2)));

        Assert.Equal(accepted ? new RawStateRemoveObservation(2, 1) : null, observation);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("FightBoostAddEvent")]
    [InlineData("FightBoostRemoveEvent")]
    public void Unsupported_codes_and_semantic_aliases_produce_no_observation(string code)
    {
        Assert.Null(Reader.Read(code, Add(950, V(1, 76))));
    }

    [Theory]
    [InlineData("", "juh")]
    [InlineData("0801", "juh")]
    [InlineData("0A00", "juh")]
    [InlineData("0A020801", "juh")]
    [InlineData("0A040A001001", "juh")]
    [InlineData("0A060A011010B607", "juh")]
    [InlineData("0A090A0412020A0010B607", "juh")]
    [InlineData("0A070A02120110B607", "juh")]
    [InlineData("0A00", "juu")]
    [InlineData("10011002", "juu")]
    public void Malformed_nested_or_duplicate_consumed_fields_are_rejected(string hex, string code)
    {
        Assert.Null(Reader.Read(code, RawProtobuf.Parse(Convert.FromHexString(hex))));
    }

    [Fact]
    public void Int32_overflow_is_not_truncated_into_supported_action_or_identity()
    {
        Assert.Null(Reader.Read("juu", P(V(1, 4294967296), V(2, 2))));
        Assert.Null(Reader.Read("juh", P(M(1, M(1, V(1, 76)), V(2, 4294968246)))));
    }

    [Fact]
    public void Semantic_only_catalog_is_not_used_as_raw_tag_fallback()
    {
        var semantic = ProtocolCatalog.LoadFromStrings(null, null, null,
            "{\"FightBoostRemoveEvent\":{\"fvuv\":1,\"character_id\":2}}");

        Assert.Null(new RawStateMessageReader("3.7.13.17", semantic).Read("juu", P(V(1, 42), V(2, -7))));
    }

    [Theory]
    [InlineData(-17, -9, -9000000001)]
    [InlineData(0, 0, 0)]
    [InlineData(123, 42, 9000000001)]
    public void Update_reads_parameter0_separately_from_instance_and_actor(int value, int id, long actor)
    {
        var observation = Reader.Read("jth", P(V(1, value), V(2, id), V(3, actor)));

        Assert.Equal(new RawStateParameterUpdateObservation(actor, id, value), observation);
    }

    [Fact]
    public void Update_omissions_remain_missing_not_zero_or_lifetime_refresh()
    {
        Assert.Equal(new RawStateParameterUpdateObservation(null, null, null), Reader.Read("jth", P()));
    }

    [Theory]
    [InlineData(null, 0, 1, RawStateRemovalRequestKind.SingleInstance)]
    [InlineData(0, 0, 1, RawStateRemovalRequestKind.SingleInstance)]
    [InlineData(42, 42, 1, RawStateRemovalRequestKind.SingleInstance)]
    [InlineData(-1, -1, 0, RawStateRemovalRequestKind.ActorFilteredBulk)]
    [InlineData(-99, -1, 0, RawStateRemovalRequestKind.ActorFilteredBulk)]
    public void Envelope31_preserves_oneof_presence_and_classifies_request_only(int? id, int baseId, int scope,
        RawStateRemovalRequestKind kind)
    {
        var fields = id is int value ? new[] { V(1, value), V(2, long.MinValue) } : new[] { V(2, long.MinValue) };

        var observation = Reader.Read("jsq", P(M(31, fields)));

        var request = Assert.IsType<RawStateRemovalRequestObservation>(observation);
        Assert.Equal(new RawStateRemovalRequestObservation(long.MinValue, id, kind), request);
        Assert.Equal(new RawStateInstance(baseId, scope), request.EffectiveScopedInstance);
    }

    [Fact]
    public void Empty_present_request_is_single_default_zero_with_unknown_raw_actor_and_id()
    {
        Assert.Equal(new RawStateRemovalRequestObservation(null, null, RawStateRemovalRequestKind.SingleInstance),
            Reader.Read("jsq", P(M(31))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Unused_boolean_does_not_change_negative_request_classification(int flag)
    {
        Assert.Equal(new RawStateRemovalRequestObservation(0, -3, RawStateRemovalRequestKind.ActorFilteredBulk),
            Reader.Read("jsq", P(M(31, V(1, -3), V(2, 0), V(4, flag)))));
    }

    [Theory]
    [InlineData("jth", "0A00")]
    [InlineData("jth", "10011002")]
    [InlineData("jth", "088080808010")]
    [InlineData("jsq", "")]
    [InlineData("jsq", "FA010110")]
    [InlineData("jsq", "F80100")]
    [InlineData("jsq", "FA01021800")]
    [InlineData("jsq", "FA010408001800")]
    [InlineData("jsq", "FA010418000800")]
    [InlineData("jsq", "FA010408000800")]
    [InlineData("jsq", "FA010410001000")]
    [InlineData("jsq", "FA01020A00")]
    [InlineData("jsq", "FA01022002")]
    [InlineData("jsq", "FA01022800")]
    [InlineData("jsq", "FA0100FA0100")]
    [InlineData("jsq", "F20100FA0100")]
    [InlineData("jsq", "FA0100F20100")]
    [InlineData("jsq", "FA0106088080808010")]
    public void Update_and_removal_reject_malformed_unknown_and_competing_variants(string code, string hex)
    {
        Assert.Null(Reader.Read(code, RawProtobuf.Parse(Convert.FromHexString(hex))));
    }

    [Theory]
    [InlineData("3.7.99.123", true)]
    [InlineData("3.8.0.0", false)]
    [InlineData(null, false)]
    public void Extended_variants_share_family_gate(string? version, bool accepted)
    {
        var reader = new RawStateMessageReader(version, Catalog);

        Assert.Equal(accepted ? new RawStateParameterUpdateObservation(3, 2, 1) : null,
            reader.Read("jth", P(V(1, 1), V(2, 2), V(3, 3))));
        Assert.Equal(accepted ? new RawStateRemovalRequestObservation(3, -1, RawStateRemovalRequestKind.ActorFilteredBulk) : null,
            reader.Read("jsq", P(M(31, V(1, -1), V(2, 3)))));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 3)]
    [InlineData(3, 7)]
    [InlineData(7, 1)]
    [InlineData(-99, -17)]
    public void Add_preserves_raw_classification_evidence_without_derivation(int? gdqa, int? gdqc)
    {
        var fields = new List<byte[]> { V(1, 76) };
        if (gdqa is int a) fields.Add(V(9, a));
        if (gdqc is int c) fields.Add(V(11, c));

        var observation = Reader.Read("juh", Add(950, fields.ToArray()));

        Assert.Equal(new RawStateAddObservation(null, null, 950, 76, -99,
            null, null, gdqa, gdqc), observation);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    public void Add_rejects_wrong_wire_type_for_raw_classification_fields(int field)
    {
        Assert.Null(Reader.Read("juh", Add(950, M(field))));
    }

    [Theory]
    [InlineData(9, 2147483648L)]
    [InlineData(9, -2147483649L)]
    [InlineData(11, 2147483648L)]
    [InlineData(11, -2147483649L)]
    public void Add_rejects_int32_overflow_in_raw_classification_fields(int field, long value)
    {
        Assert.Null(Reader.Read("juh", Add(950, V(field, value))));
    }
}
