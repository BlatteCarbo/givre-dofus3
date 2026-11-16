extern alias F06App;

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Profile = F06App::GivreClone.App.Services.CombatProfile;
using Speed = F06App::GivreClone.App.Services.CombatSpeed;
using Config = F06App::GivreClone.App.Services.SpellConfig;
using Reader = F06App::GivreClone.App.Services.MapCellReader;
using Names = F06App::GivreClone.App.Services.GameNameResolver;
using Combat = F06App::GivreClone.App.Services.Combat;
using CastLedger = F06App::GivreClone.App.Services.Combat.CastLedger;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;
using TransportLog = GivreLauncher.Core.Transport.TransportLog;

namespace GivreClone.Tests;

public sealed class EliotropePortalDispatchTests
{
    private const uint Pid = 0x40000120;
    private const long MapId = 88085764;
    private const int SpellAttackViaPortal = 14579;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static int Cell(int step) => MapPathfinder.CellFromMapPoint(8 + step, 8);

    [Theory]
    [InlineData(-1L, false, true)]
    [InlineData(123L, false, false)]
    [InlineData(-1L, true, false)]
    public void Captured_enemy_spell_is_retained_only_from_server_and_cleared_between_fights(long source, bool outbound, bool retained)
    {
        var service = new Launch();
        Map(service, "_inFight")[Pid] = true;
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Field("_catalog").SetValue(service, catalog);
        var decoder = new F06App::GivreClone.App.Services.CaptureDecoder(catalog);
        Field("_decoder").SetValue(service, decoder); // same decoder that stamped this packet
        decoder.Packet += packet => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, [packet]);
        ProtoField V(int tag, long value) => new() { Field = tag, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        ProtoField M(int tag, params ProtoField[] fields) => new() { Field = tag, Wire = WireType.Len, Message = fields.ToList() };
        var body = RawProtobuf.Encode([M(2, M(2, V(1, 86051), V(2, 32780)), V(5, 370), V(6, -1)), V(22, 300), V(36, source)]);
        var frame = RawProtobuf.Encode([M(1, new ProtoField { Field = 1, Wire = WireType.Len,
            String = "type.ankama.com/jsq" }, new ProtoField { Field = 2, Wire = WireType.Len, Bytes = body })]);

        decoder.Feed(Pid, outbound, RawProtobuf.EncodeVarint((ulong)frame.Length).Concat(frame).ToArray());

        var books = Map(service, "_observedEnemySpellLevels");
        Assert.Equal(retained, books.Contains(Pid));
        if (retained)
        {
            var enemies = (ConcurrentDictionary<long, ConcurrentDictionary<int, int>>)books[Pid]!;
            Assert.Equal(86051, Assert.Single(enemies[-1]).Value);
            Assert.Equal(32780, Assert.Single(enemies[-1]).Key);
            typeof(Launch).GetMethod("ForgetChallengeFight", Hidden)!.Invoke(service, [Pid]);
            Assert.False(books.Contains(Pid));
        }
    }

    [Theory]
    [InlineData(14577, 14578, 2, 226051)]
    [InlineData(14611, 31020, 1, 404765)]
    public void Captured_trigger_pair_survives_dispatch_once_and_server_removal_clears_it(int sourceSpell, int subSpell, int grade, int group)
    {
        var service = new Launch();
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_teamIntentRound")[Pid] = 3;
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Field("_catalog").SetValue(service, catalog);
        var decoder = new F06App::GivreClone.App.Services.CaptureDecoder(catalog);
        Field("_decoder").SetValue(service, decoder); // same decoder that stamped this packet
        decoder.Packet += packet => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, [packet]);
        ProtoField V(int tag, long value) => new() { Field = tag, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        ProtoField M(int tag, params ProtoField[] fields) => new() { Field = tag, Wire = WireType.Len, Message = fields.ToList() };
        void Feed(string code, params ProtoField[] fields)
        {
            var body = RawProtobuf.Encode(fields);
            var frame = RawProtobuf.Encode([M(1, new ProtoField { Field = 1, Wire = WireType.Len,
                String = "type.ankama.com/" + code }, new ProtoField { Field = 2, Wire = WireType.Len, Bytes = body })]);
            decoder.Feed(Pid, false, RawProtobuf.EncodeVarint((ulong)frame.Length).Concat(frame).ToArray());
        }
        foreach (var (instance, trigger) in new[] { (23, "D"), (24, "XD") })
            Feed("juh", M(1, M(1, V(1, 999), M(2, V(1, -1), V(2, -1)), M(4, V(2, 4)),
                V(5, instance), V(6, group), V(8, -2), V(10, subSpell), V(11, 7), V(13, grade),
                new ProtoField { Field = 14, Wire = WireType.Len, Bytes = System.Text.Encoding.UTF8.GetBytes(trigger) },
                V(15, 2), V(16, sourceSpell)), V(2, 1160), V(3, 33273217378)));

        var tracker = (Combat.DistributionTriggerTracker)Map(service, "_distributionTriggers")[Pid]!;
        Assert.Equal(-2L, Assert.Single(tracker.Active(33273217378, 3)).ActorId);
        Feed("juu", V(1, 24), V(2, -2));
        Assert.Single(tracker.Active(33273217378, 3));
        Feed("juu", V(1, 23), V(2, -2));
        Assert.Empty(tracker.Active(33273217378, 3));
        tracker.Observe(new(-2, 23, 33273217378, sourceSpell, subSpell, grade, group,
            "D", new(-1, -1), new(4, null)), 3);
        Assert.Single(tracker.Active(33273217378, 3));
        typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
            [new DecodedPacket(Pid, false, "jun", "FightBoostUpdateStateEvent",
                new[] { V(1, 1) }, Array.Empty<byte>()) { PayloadDecoded = true }]);
        Assert.Empty(tracker.Active(33273217378, 3));
        service.ClearFighterStates(Pid);
        Assert.False(Map(service, "_distributionTriggers").Contains(Pid));
    }

    [Theory]
    [InlineData(14594, 99, "TE", 2, 37)]
    [InlineData(29098, 96, "TB", 3, 17)]
    [InlineData(29098, 2998, "TB", 3, 17)]
    public void Captured_timed_damage_survives_packet_dispatch_and_is_removed_by_server_instance(
        int spell, int action, string timing, int grade, int value)
    {
        var service = new Launch();
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_teamIntentRound")[Pid] = 3;
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Field("_catalog").SetValue(service, catalog);
        var decoder = new F06App::GivreClone.App.Services.CaptureDecoder(catalog);
        Field("_decoder").SetValue(service, decoder); // same decoder that stamped this packet
        decoder.Packet += packet => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, [packet]);
        ProtoField V(int tag, long value) => new() { Field = tag, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        ProtoField M(int tag, params ProtoField[] fields) => new() { Field = tag, Wire = WireType.Len, Message = fields.ToList() };
        void Feed(string code, params ProtoField[] fields)
        {
            var body = RawProtobuf.Encode(fields);
            var frame = RawProtobuf.Encode([M(1, new ProtoField { Field = 1, Wire = WireType.Len,
                String = "type.ankama.com/" + code }, new ProtoField { Field = 2, Wire = WireType.Len, Bytes = body })]);
            decoder.Feed(Pid, false, RawProtobuf.EncodeVarint((ulong)frame.Length).Concat(frame).ToArray());
        }
        Feed("juh", M(1, M(1, V(1, value), M(2, V(1, -1), V(2, -1)), M(4, V(2, 4)),
            V(5, 20), V(6, 282865), V(8, -2), V(10, 1), V(11, 7), V(13, 5),
            new ProtoField { Field = 14, Wire = WireType.Len, Bytes = System.Text.Encoding.UTF8.GetBytes(timing) },
            V(15, grade), V(16, spell)), V(2, action), V(3, 33273217378)));

        var tracker = (Combat.DistributionTriggerTracker)Map(service, "_distributionTriggers")[Pid]!;
        var observed = Assert.Single(tracker.ActiveTimed(33273217378, 3));
        Assert.Equal(-2L, observed.ActorId);
        Assert.Equal(grade, observed.SourceGrade);
        Assert.Equal(spell, observed.SourceSpellId);
        Feed("juu", V(1, 20), V(2, -2));
        Assert.Empty(tracker.ActiveTimed(33273217378, 3));
        service.ClearFighterStates(Pid);
        Assert.False(Map(service, "_distributionTriggers").Contains(Pid));
    }

    [Fact]
    public void Decoded_heal_updates_fighter_life_without_restoring_eroded_maximum()
    {
        var service = new Launch();
        const long actor = 33273217378;
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_charId")[Pid] = actor;
        var stats = new ConcurrentDictionary<long, FighterStats>();
        stats[actor] = new FighterStats(actor, 11, 4, 900, 2000, 11, 4);
        Map(service, "_fighterStats")[Pid] = stats;
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        var decoder = new F06App::GivreClone.App.Services.CaptureDecoder(catalog);
        Field("_decoder").SetValue(service, decoder); // same decoder that stamped this packet
        decoder.Packet += packet => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, new object[] { packet });
        ProtoField V(int tag, ulong value) => new() { Field = tag, Wire = WireType.Varint, Varint = value };
        ProtoField M(int tag, params ProtoField[] fields) => new() { Field = tag, Wire = WireType.Len, Message = fields.ToList() };
        // Captured after Cicatrisation on the live 3.7 client: target field2,
        // resolved heal166 at field4 (field3 is omitted).
        var body = Convert.FromHexString("B001B917A002E282F4F97BBA020910E282F4F97B20A601");
        var frame = RawProtobuf.Encode(new[] { M(1, new ProtoField { Field = 1, Wire = WireType.Len,
            String = "type.ankama.com/jsq" }, new ProtoField { Field = 2, Wire = WireType.Len, Bytes = body }) });
        decoder.Feed(Pid, false, RawProtobuf.EncodeVarint((ulong)frame.Length).Concat(frame).ToArray());
        Assert.Equal(1066, stats[actor].Life);
        Assert.Equal(2000, stats[actor].MaxLife);
    }

    [Fact]
    public void Observed_portal_deactivation_excludes_it_until_server_reactivation()
    {
        var service = new Launch();
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_fighterBreeds")[Pid] = new ConcurrentDictionary<long, int>
        {
            [456L] = Combat.ClassMechanics.EliotropeBreedId
        };
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        ProtoField V(string message, string field, ulong value) => new()
            { Field = F(message, field), Wire = WireType.Varint, Varint = value };
        ProtoField M(string message, string field, params ProtoField[] children) => new()
            { Field = F(message, field), Wire = WireType.Len, Message = children.ToList() };
        void Receive(params ProtoField[] fields) => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
            new object[] { new DecodedPacket(Pid, false, "jwe", "GameActionFightEvent", fields, Array.Empty<byte>()) });
        Receive(M("GameActionFightEvent", "mark_cells", M("MarkCells", "mark",
            V("FightMark", "mark_id", 1), V("FightMark", "author_id", 456),
            V("FightMark", "active", 1), V("FightMark", "mark_type", 4),
            M("FightMark", "cells", V("MarkedCell", "gask", (ulong)Cell(2))))));
        Assert.Single(service.Portals(Pid));
        // Proto3 omits false: the mark id alone confirms deactivation.
        Receive(M("GameActionFightEvent", "activate_glyph_trap", V("ActivateGlyphTrap", "mark_id", 1)));
        Assert.Empty(service.Portals(Pid));
        Assert.False(Assert.Single(service.Portals(Pid, includeInactive: true)).Active);
        Receive(M("GameActionFightEvent", "activate_glyph_trap",
            V("ActivateGlyphTrap", "mark_id", 1), V("ActivateGlyphTrap", "active", 1)));
        Assert.Equal(Cell(2), Assert.Single(service.Portals(Pid)).Cell);
    }

    [Fact]
    public void Huppermage_rune_type_four_is_never_a_portal()
    {
        var service = new Launch();
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_fighterBreeds")[Pid] = new ConcurrentDictionary<long, int>
        {
            [456L] = 17 // Official Huppermage breed ID, not the UI index.
        };
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        ProtoField V(string message, string field, ulong value) => new()
            { Field = F(message, field), Wire = WireType.Varint, Varint = value };
        ProtoField M(string message, string field, params ProtoField[] children) => new()
            { Field = F(message, field), Wire = WireType.Len, Message = children.ToList() };
        typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
            new object[] { new DecodedPacket(Pid, false, "jwe", "GameActionFightEvent", [
                M("GameActionFightEvent", "mark_cells", M("MarkCells", "mark",
                    V("FightMark", "mark_id", 8), V("FightMark", "author_id", 456),
                    V("FightMark", "active", 1), V("FightMark", "mark_type", 4),
                    M("FightMark", "cells", V("MarkedCell", "gask", (ulong)Cell(2)))))
            ], Array.Empty<byte>()) });
        Assert.Empty(service.Portals(Pid));
        Assert.Empty(service.Portals(Pid, includeInactive: true));
    }

    [Fact]
    public void Observed_state_removal_records_only_that_absence_and_fight_reset_clears_it()
    {
        var service = new Launch();
        Assert.Empty(service.FighterAbsentStates(Pid, 456L));
        service.SetFighterState(Pid, 456L, 42, true);
        Assert.DoesNotContain(76, service.FighterAbsentStates(Pid, 456L));
        service.SetFighterState(Pid, 456L, 76, false);
        Assert.Contains(76, service.FighterAbsentStates(Pid, 456L));
        service.SetFighterState(Pid, 456L, 76, true);
        Assert.DoesNotContain(76, service.FighterAbsentStates(Pid, 456L));
        Assert.Contains(76, service.FighterActiveStates(Pid, 456L));
        service.ClearFighterStates(Pid);
        Assert.Empty(service.FighterAbsentStates(Pid, 456L));
        Assert.Empty(service.FighterActiveStates(Pid, 456L));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, false, true, "FightBoostRemoveEvent")]
    [InlineData(true, false, false, true, "FightBoostUpdateEvent")]
    [InlineData(true, false, false, true, "FightBoostUpdateStateEvent")]
    public void Server_state_removal_does_not_become_an_active_state(bool emptySnapshot, bool client = false, bool nonempty = false, bool decoded = true,
        string invalidatingEvent = "FightBoostAddEvent")
    {
        var service = new Launch();
        Map(service, "_inFight")[Pid] = true;
        var names = new Names(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        Field("_gameNames").SetValue(service, names);
        Map(service, "_charSpells")[Pid] = new[] { 14591 };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [14591] = 2 };
        Field("_catalog").SetValue(service, ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));
        var binding = typeof(F06App::GivreClone.App.Services.MappingCatalog)
            .GetField("_sessionVersionBinding", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = binding.GetValue(null);
        try
        {
            binding.SetValue(null, new F06App::GivreClone.App.Services.MZP12SocketVersionBinding.Binding("3.7.13.17", "test"));
            ProtoField V(int tag, ulong value) => new() { Field = tag, Wire = WireType.Varint, Varint = value };
            ProtoField M(int tag, params ProtoField[] children) => new() { Field = tag, Wire = WireType.Len, Message = children.ToList() };
            void Observe(DecodedPacket packet) => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, new object[] { packet });
            void Receive(int action, int instance) => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
                new object[] { new DecodedPacket(Pid, false, "juh", "FightBoostAddEvent",
                    new[] { M(1, M(1, V(1, 76), V(5, (ulong)instance), V(8, 456)), V(2, (ulong)action), V(3, 456)) }, Array.Empty<byte>()) });
            service.SetFighterState(Pid, 456L, 42, true);
            Receive(950, 101);
            Receive(950, 102);
            Assert.Contains(76, service.FighterActiveStates(Pid, 456L));

            if (emptySnapshot)
            {
                Map(service, "_charId")[Pid] = 456L;
                if (!client && !nonempty && decoded)
                {
                    var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
                    var decoder = new F06App::GivreClone.App.Services.CaptureDecoder(catalog);
                    Field("_decoder").SetValue(service, decoder); // same decoder that stamped this packet
                    decoder.Packet += Observe;
                    byte[] frame = EnvelopeBuilder.BuildServerEvent("jun", Array.Empty<ProtoField>());
                    Assert.InRange(frame.Length, 1, 127);
                    decoder.Feed(Pid, false, new byte[] { (byte)frame.Length }.Concat(frame).ToArray());
                }
                else Observe(new DecodedPacket(Pid, client, "jun", "FightBoostUpdateStateEvent",
                    nonempty ? new[] { M(1) } : Array.Empty<ProtoField>(), Array.Empty<byte>()) { PayloadDecoded = decoded });
            }
            else Receive(951, 103);

            if (client || nonempty || !decoded)
            {
                Assert.Contains(76, service.FighterActiveStates(Pid, 456L));
                Assert.DoesNotContain(76, service.FighterAbsentStates(Pid, 456L));
            }
            else
            {
                Assert.DoesNotContain(76, service.FighterActiveStates(Pid, 456L));
                Assert.Contains(76, service.FighterAbsentStates(Pid, 456L));
            }
            if (emptySnapshot && !client && !nonempty && decoded) Assert.Empty(service.FighterActiveStates(Pid, 456L));
            else Assert.Contains(42, service.FighterActiveStates(Pid, 456L));
            Assert.Equal(emptySnapshot && !client && !nonempty && decoded, service.HasEmptyStatBuffInventory(Pid));
            Assert.Equal(emptySnapshot && !client && !nonempty && decoded,
                service.FighterAbsentStates(Pid, 456L).Contains(7));
            foreach (int portalState in new[] { 3737, 3738 })
                Assert.Equal(emptySnapshot && !client && !nonempty && decoded,
                    service.FighterAbsentStates(Pid, 456L).Contains(portalState));
            if (service.HasEmptyStatBuffInventory(Pid))
            {
                service.SetFighterState(Pid, 456L, 3737, true);
                Assert.DoesNotContain(3737, service.FighterAbsentStates(Pid, 456L));
                Assert.Contains(3738, service.FighterAbsentStates(Pid, 456L));
                int origin = MapPathfinder.CellFromMapPoint(10, 0);
                int destination = MapPathfinder.CellFromMapPoint(12, 0);
                var caster = new Combat.SpellFighter(456L, origin, false, 1000, 1000,
                    States: service.FighterActiveStates(Pid, 456L).ToImmutableHashSet(), StatesKnown: false,
                    AbsentStates: service.FighterAbsentStates(Pid, 456L).ToImmutableHashSet());
                var resolved = Combat.SpellResolver.Resolve(new Combat.SpellBoard(new[] { caster }), 0,
                    destination, names.GetSpellDescriptor(14591, 2)!, names.GetSpellDescriptor, 183);
                Assert.Equal(destination, resolved.Board.Fighters[0].Cell);
                Assert.Single(resolved.Marks);
                Assert.Contains(resolved.Marks, mark => mark.Mark.Cells.SequenceEqual(new[] { origin }));
                Assert.Empty(resolved.Unmodeled);
                service.SetFighterState(Pid, 456L, 7, true);
                Assert.DoesNotContain(7, service.FighterAbsentStates(Pid, 456L));
                Assert.Contains(7, service.FighterActiveStates(Pid, 456L));
                typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, new object[] {
                    new DecodedPacket(Pid, true, "juh", "FightBoostAddEvent", Array.Empty<ProtoField>(), Array.Empty<byte>()) });
                Assert.True(service.HasEmptyStatBuffInventory(Pid));
                Assert.True(service.IsStatBuffSourceAbsent(Pid, 14617));
                Observe(new DecodedPacket(Pid, false, "juh", "FightBoostAddEvent",
                    new[] { M(1, M(1, V(16, 999)), V(2, 138), V(3, 456)) }, Array.Empty<byte>()) { PayloadDecoded = true });
                Assert.False(service.HasEmptyStatBuffInventory(Pid));
                Assert.True(service.IsStatBuffSourceAbsent(Pid, 14617));
                Assert.False(service.IsStatBuffSourceAbsent(Pid, 999));
                Observe(new DecodedPacket(Pid, false, "juh", "FightBoostAddEvent",
                    new[] { M(1, M(1, V(16, 14617)), V(2, 138), V(3, 456)) }, Array.Empty<byte>()) { PayloadDecoded = true });
                Assert.False(service.IsStatBuffSourceAbsent(Pid, 14617));
                Assert.True(service.IsStatBuffSourceAbsent(Pid, 888));
                typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, new object[] {
                    new DecodedPacket(Pid, false, "?", invalidatingEvent, Array.Empty<ProtoField>(), Array.Empty<byte>()) });
                Assert.False(service.HasEmptyStatBuffInventory(Pid));
                Assert.False(service.IsStatBuffSourceAbsent(Pid, 888));
            }
            service.ClearFighterStates(Pid);
            Assert.False(service.HasEmptyStatBuffInventory(Pid));
            Assert.False(service.IsStatBuffSourceAbsent(Pid, 14617));
            Assert.Empty(service.FighterAbsentStates(Pid, 456L));
        }
        finally { binding.SetValue(null, previous); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, true, 0)]
    [InlineData(false, false, true, 1)]
    [InlineData(false, false, true, -1, true)]
    [InlineData(false, false, false, -1, false, true)]
    [InlineData(false, false, false, -1, false, true, 1181)]
    [InlineData(true, false, true, -1, false, false, 1183, true)]
    [InlineData(false, false, false, -1, false, false, 1183, false, true)]
    [InlineData(false, false, false, -1, false, false, 1183, false, true, false)]
    public async Task Eliotrope_portal_network_caller_dispatches_attack_on_entry_portal(bool areaSpell, bool placesMark,
        bool selfHeal, int incurable = -1, bool realRay = false, bool classicNeutral = false, int groundAction = 1183,
        bool emptyImpact = false, bool enemyPortalThreat = false, bool observeEnemy = true)
    {
        var service = new Launch();
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_myTurnOpen")[Pid] = true;
        Map(service, "_jzcSeen")[Pid] = true;
        Map(service, "_combat")[Pid] = new Profile { AutoFight = true, AutoSpells = true, Speed = Speed.Turbo };
        if (classicNeutral)
            Map(service, "_combat")[Pid] = new Profile
            {
                AutoFight = true, AutoSpells = false, Speed = Speed.Turbo,
                Spells = new() { new Config { SpellId = SpellAttackViaPortal, Enabled = true, OnMe = true, PerTurn = 1 } }
            };
        Map(service, "_charId")[Pid] = 456L;
        if (selfHeal && incurable != 0) service.SetFighterState(Pid, 456L, 76, incurable == 1);

        int casterCell = Cell(enemyPortalThreat ? 10 : selfHeal ? 2 : 0);
        int entryCell = Cell(emptyImpact ? 5 : selfHeal ? 4 : 2);
        int exitCell = Cell(selfHeal ? 0 : 8);
        int enemyCell = Cell(enemyPortalThreat ? 0 : 10);

        Map(service, "_fighters")[Pid] = new (int Cell, long ActorId)[] { (casterCell, 456), (enemyCell, -200) };
        Map(service, "_charSpells")[Pid] = new[] { SpellAttackViaPortal };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [SpellAttackViaPortal] = 1 };
        Map(service, "_fighterStats")[Pid] = new ConcurrentDictionary<long, FighterStats>(new[]
        {
            new KeyValuePair<long, FighterStats>(456, new(456, enemyPortalThreat ? 1 : 6, enemyPortalThreat ? 0 : 3, selfHeal ? 200 : 1000, 1000, 0, 0)),
            new KeyValuePair<long, FighterStats>(-200, new(-200, enemyPortalThreat ? 3 : 0, 0, 1000, 1000, enemyPortalThreat ? 3 : 0, 0))
        });
        Map(service, "_fighterBreeds")[Pid] = new ConcurrentDictionary<long, int> { [456L] = Combat.ClassMechanics.EliotropeBreedId };

        // Inject portal mark packet (type 4 portal) via OnDecoded to populate Portals and verify portal exclusion from marked cells
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        Field("_catalog").SetValue(service, catalog);
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        ProtoField V(string message, string field, int value) => new()
            { Field = F(message, field), Wire = WireType.Varint, Varint = (ulong)value };
        ProtoField M(string message, string field, params ProtoField[] children) => new()
            { Field = F(message, field), Wire = WireType.Len, Message = children.ToList() };

        typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
            new object[] { new DecodedPacket(Pid, false, "jwe", "GameActionFightEvent", new[] {
                M("GameActionFightEvent", "mark_cells", M("MarkCells", "mark",
                    V("FightMark", "mark_id", 1), V("FightMark", "author_id", 456),
                    V("FightMark", "active", 1), V("FightMark", "mark_type", 4), M("FightMark", "cells", V("MarkedCell", "gask", entryCell))))
            }, Array.Empty<byte>()) });
        typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service,
            new object[] { new DecodedPacket(Pid, false, "jwe", "GameActionFightEvent", new[] {
                M("GameActionFightEvent", "mark_cells", M("MarkCells", "mark",
                    V("FightMark", "mark_id", 2), V("FightMark", "author_id", 456),
                    V("FightMark", "active", 1), V("FightMark", "mark_type", 4), M("FightMark", "cells", V("MarkedCell", "gask", exitCell))))
            }, Array.Empty<byte>()) });

        // Assert service portals count is 2
        var portals = service.Portals(Pid);
        Assert.Equal(2, portals.Count);

        var visibility = new FightVisibilityReducer();
        visibility.Observe(new(-200, 2), positionConfirmed: true);
        Assert.True(visibility.Get(-200).CanTarget);
        Map(service, "_fighterVisibility")[Pid] = visibility;

        var reader = (Reader)RuntimeHelpers.GetUninitializedObject(typeof(Reader));
        typeof(Reader).GetField("_cache", Hidden)!.SetValue(reader,
            new ConcurrentDictionary<long, MapCell[]>(new[]
            {
                new KeyValuePair<long, MapCell[]>(MapId, Enumerable.Range(0, 15)
                    .Select(i => new MapCell(Cell(i), true, true, 0, LinkedZone: 17)).ToArray())
            }));
        Field("_cellReader").SetValue(service, reader);
        Map(service, "_lastMap")[Pid] = MapId;
        Map(service, "_serverMapId")[Pid] = MapId;

        var names = new Names();
        typeof(Names).GetField("_combatDone", Hidden)!.SetValue(names, true);
        typeof(Names).GetField("_spellZone", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int)>>
            { [SpellAttackViaPortal] = new() { [1] = (emptyImpact ? 'C' : areaSpell ? 'L' : 'P', emptyImpact ? 1 : areaSpell ? 2 : 0, 0) } });
        typeof(Names).GetField("_spellCombat", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>>
            {
                [SpellAttackViaPortal] = new() { [1] = (3, classicNeutral ? 0 : 1, 5, 512, false) }
            });

        var attackDesc = new Combat.SpellCastDescriptor(SpellAttackViaPortal, 1, 3, 1, 5, 512, false, 0, 0, 1, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage, 50, 50, 0, 0,
                "A", areaSpell ? 'L' : 'P', areaSpell ? 2 : 0, 0)));
        if (placesMark)
            attackDesc = attackDesc with { Effects = attackDesc.Effects.Add(
                new Combat.SpellEffectDescriptor(400, Combat.SpellEffectKind.Unknown, 0, 0, 0, -1, "A", 'P', 0, 0)) };
        if (classicNeutral)
            attackDesc = attackDesc with { Effects = ImmutableArray.Create(
                new Combat.SpellEffectDescriptor(groundAction, Combat.SpellEffectKind.Unknown, 0, 0, 0, 1, "a,A", 'P', 0, 0),
                new Combat.SpellEffectDescriptor(120, Combat.SpellEffectKind.Unknown, 1, 1, 0, 0, "C", 'P', 0, 0)),
                MinRange = 0, StatesCriterion = "HS!97" };
        if (selfHeal)
            attackDesc = attackDesc with { Effects = ImmutableArray.Create(
                new Combat.SpellEffectDescriptor(108, Combat.SpellEffectKind.Heal, 100, 100, 0, 0,
                    "a,R", emptyImpact ? 'C' : 'P', emptyImpact ? 1 : 0, 0, Triggers: "I")) };
        if (realRay)
        {
            var actualNames = new Names();
            actualNames.EnsureLoaded();
            actualNames.EnsureCombatLoaded();
            attackDesc = actualNames.GetSpellDescriptor(SpellAttackViaPortal, 3)
                ?? throw new InvalidOperationException("Missing actual grade-3 Rayon descriptor");
            typeof(Names).GetField("_spellZone", Hidden)!.SetValue(names,
                new Dictionary<int, Dictionary<int, (int, int, int)>>
                    { [SpellAttackViaPortal] = new() { [1] = ('L', 2, 0) } });
        }

        using var enemyFixture = new FakeGameDb();
        using (var writer = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={enemyFixture.File}"))
        {
            writer.Open();
            using var command = writer.CreateCommand();
            command.CommandText = "create table SpellLevelData(id integer primary key, spellId integer, grade integer); insert into SpellLevelData values(86051,32780,10)";
            command.ExecuteNonQuery();
        }
        using var enemyDb = F06App::GivreClone.App.Services.LotM.GameDb.TryOpen(enemyFixture.File);
        if (enemyPortalThreat)
        {
            Field("_gameDb").SetValue(service, enemyDb);
            // Only the nearby exit is within closure range; closing either endpoint
            // otherwise prevents the same shot and is an equally valid decision.
            attackDesc = attackDesc with { Ap = 1, MinRange = 0, Range = 3,
                Effects = ImmutableArray.Create(new Combat.SpellEffectDescriptor(1183,
                    Combat.SpellEffectKind.Unknown, 0, 0, 0, 1, "a,A", 'P', 0, 0, Triggers: "I")) };
            typeof(Names).GetField("_spellCombat", Hidden)!.SetValue(names,
                new Dictionary<int, Dictionary<int, (int, int, int, int, bool)>>
                    { [SpellAttackViaPortal] = new() { [1] = (1, 0, 3, 512, false) } });
            var decoder = new F06App::GivreClone.App.Services.CaptureDecoder(catalog);
            Field("_decoder").SetValue(service, decoder); // same decoder that stamped this packet
            decoder.Packet += packet => typeof(Launch).GetMethod("OnDecoded", Hidden)!.Invoke(service, [packet]);
            var body = RawProtobuf.Encode([new ProtoField { Field = 2, Wire = WireType.Len, Message = new() {
                new ProtoField { Field = 2, Wire = WireType.Len, Message = new() {
                    new ProtoField { Field = 1, Wire = WireType.Varint, Varint = 86051 },
                    new ProtoField { Field = 2, Wire = WireType.Varint, Varint = 32780 } } } } },
                new ProtoField { Field = 36, Wire = WireType.Varint, Varint = unchecked((ulong)-200L) }]);
            var frame = RawProtobuf.Encode([new ProtoField { Field = 1, Wire = WireType.Len, Message = new() {
                new ProtoField { Field = 1, Wire = WireType.Len, String = "type.ankama.com/jsq" },
                new ProtoField { Field = 2, Wire = WireType.Len, Bytes = body } } }]);
            if (observeEnemy)
                decoder.Feed(Pid, false, RawProtobuf.EncodeVarint((ulong)frame.Length).Concat(frame).ToArray());
        }

        typeof(Names).GetField("_spellDescriptors", Hidden)!.SetValue(names,
            new Dictionary<int, Dictionary<int, Combat.SpellCastDescriptor>>
            {
                [SpellAttackViaPortal] = new() { [1] = attackDesc },
                [32780] = new() { [10] = new Combat.SpellCastDescriptor(32780, 10, 3, 1, 2, 0, false, 0, 0, 0, 0, "",
                    ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage, 50, 50, 0, 0, "A", 'P', 0, 0))) }
            });
        Field("_gameNames").SetValue(service, names);

        var trace = new List<string>();
        void Observe(uint _, string message) => trace.Add(message);
        bool loggingEnabled = TransportLog.Enabled;
        TransportLog.Enabled = true;
        TransportLog.EmittedWithPid += Observe;
        var frames = new List<byte[]>();
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame => { frames.Add(frame); return false; });

        try
        {
            var run = (Task)typeof(Launch).GetMethod("RunFightTurn", Hidden)!.Invoke(service,
                [Pid, 0, false, CancellationToken.None, null, 1])!;
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            TransportLog.EmittedWithPid -= Observe;
            TransportLog.Enabled = loggingEnabled;
        }

        var catalogLoaded = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        var requests = frames.Select(frame =>
        {
            var payload = Assert.Single(RawProtobuf.ConsumeFrames(frame, out var rest));
            Assert.Empty(rest);
            return Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(payload)));
        }).ToArray();

        if (classicNeutral || placesMark || selfHeal && incurable >= 0 || enemyPortalThreat && !observeEnemy)
        {
            Assert.DoesNotContain(requests, r => r.Code == catalogLoaded.CodeForName("GameActionFightCastRequest"));
            return;
        }
        Assert.True(requests.Any(r => r.Code == catalogLoaded.CodeForName("GameActionFightCastRequest")),
            string.Join(Environment.NewLine, trace));
        var castRequest = requests.First(r => r.Code == catalogLoaded.CodeForName("GameActionFightCastRequest")).Payload!;
        int CastField(string field) => catalogLoaded.FieldNumber("GameActionFightCastRequest", field)
            ?? throw new InvalidOperationException($"Missing field {field}");
        
        ulong dispatchedSpell = castRequest.Single(f => f.Field == CastField("spell_id")).Varint;
        Assert.Equal((ulong)SpellAttackViaPortal, dispatchedSpell);

        ulong targetCell = castRequest.Single(f => f.Field == CastField("cell")).Varint;
        Assert.Equal((ulong)(enemyPortalThreat ? exitCell : entryCell), targetCell);
        if (emptyImpact)
            Assert.Contains(trace, line => line.Contains("target=0 via-portail", StringComparison.Ordinal));
    }
}
