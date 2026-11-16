extern alias F06App;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;

namespace GivreClone.Tests;

public sealed class LevelUpSpellRefreshTests
{
    private const uint Pid = 0x40000111;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Private)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;

    private static Launch Service()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Private))
        {
            if (field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
            {
                field.SetValue(service, Activator.CreateInstance(field.FieldType));
            }
        }
        Field("_autoLevelLock").SetValue(service, new object());
        Field("_combatMapGate").SetValue(service, new object());
        Field("_movementDispatchGate").SetValue(service, new object());
        Field("_autoLevelActive").SetValue(service, new HashSet<uint>());
        Field("_statUpgradeActive").SetValue(service, new HashSet<uint>());
        Field("_autoStatRequests").SetValue(service, new Dictionary<uint, object>());
        Field("_autoStatSuspended").SetValue(service, new HashSet<uint>());

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (Directory.Exists(dataDir))
        {
            var catalog = ProtocolCatalog.LoadFromDirectory(dataDir);
            Field("_catalog").SetValue(service, catalog);
        }

        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        Field("_gameNames").SetValue(service, names);

        return service;
    }

    private static int F(string message, string field)
    {
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        return catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
    }

    private static ProtoField V(string message, string field, int value) => new()
    {
        Field = F(message, field),
        Wire = WireType.Varint,
        Varint = (ulong)value
    };

    private static ProtoField M(string message, string field, params ProtoField[] children) => new()
    {
        Field = F(message, field),
        Wire = WireType.Len,
        Message = children.ToList()
    };

    private static void Dispatch(Launch service, DecodedPacket packet)
    {
        typeof(Launch).GetMethod("OnDecoded", Private)!.Invoke(service, [packet]);
    }

    [Fact]
    public void Level_up_then_spell_list_message_updates_stored_spells_and_levels()
    {
        var service = Service();
        Map(service, "_charLevel")[Pid] = 1;
        Map(service, "_charSpells")[Pid] = new List<int> { 101 };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [101] = 1 };

        // (1) Character levels up from 1 to 20
        var levelUpPacket = new DecodedPacket(Pid, false, "kqp", "CharacterLevelUpEvent", new[]
        {
            V("CharacterLevelUpEvent", "new_level", 20)
        }, Array.Empty<byte>());
        Dispatch(service, levelUpPacket);

        Assert.Equal(20, service.CharacterLevel(Pid));

        // (2) Server sends spell list message (SpellsEvent) carrying updated spells: 101 (upgraded to grade 2) and 102 (newly unlocked at grade 1)
        var spellListPacket = new DecodedPacket(Pid, false, "hms", "SpellsEvent", new[]
        {
            M("SpellsEvent", "human_spells",
                V("SpellItem", "spell_id", 101),
                V("SpellItem", "spell_level", 2)),
            M("SpellsEvent", "human_spells",
                V("SpellItem", "spell_id", 102),
                V("SpellItem", "spell_level", 1))
        }, Array.Empty<byte>());
        Dispatch(service, spellListPacket);

        // Assert: stored spells and levels reflect the new knowledge
        var learned = service.LearnedSpells(Pid);
        Assert.Contains(101, learned);
        Assert.Contains(102, learned);
        Assert.Equal(2, service.LearnedSpellLevel(Pid, 101));
        Assert.Equal(1, service.LearnedSpellLevel(Pid, 102));
    }

    [Fact]
    public void Level_up_alone_keeps_spells_and_does_not_throw()
    {
        var service = Service();
        Map(service, "_charLevel")[Pid] = 10;
        var initialSpells = new List<int> { 101, 102 };
        var initialLevels = new Dictionary<int, int> { [101] = 1, [102] = 2 };
        Map(service, "_charSpells")[Pid] = initialSpells;
        Map(service, "_charSpellLevels")[Pid] = initialLevels;

        var levelUpPacket = new DecodedPacket(Pid, false, "kqp", "CharacterLevelUpEvent", new[]
        {
            V("CharacterLevelUpEvent", "new_level", 25)
        }, Array.Empty<byte>());

        var ex = Record.Exception(() => Dispatch(service, levelUpPacket));
        Assert.Null(ex);
        Assert.Equal(25, service.CharacterLevel(Pid));
        Assert.Equal(initialSpells, service.LearnedSpells(Pid));
        Assert.Equal(1, service.LearnedSpellLevel(Pid, 101));
        Assert.Equal(2, service.LearnedSpellLevel(Pid, 102));
    }

    [Fact]
    public void Level_up_message_missing_its_level_field_does_not_throw()
    {
        var service = Service();
        Map(service, "_charLevel")[Pid] = 15;
        Map(service, "_charSpells")[Pid] = new List<int> { 101 };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [101] = 1 };

        var malformedPacket = new DecodedPacket(Pid, false, "kqp", "CharacterLevelUpEvent",
            Array.Empty<ProtoField>(), Array.Empty<byte>());

        var ex = Record.Exception(() => Dispatch(service, malformedPacket));
        Assert.Null(ex);
        // Level remains 15, existing spells untouched
        Assert.Equal(15, service.CharacterLevel(Pid));
        Assert.Equal(new[] { 101 }, service.LearnedSpells(Pid));
        Assert.Equal(1, service.LearnedSpellLevel(Pid, 101));
    }

    [Fact]
    public void Level_up_then_spell_variant_activation_updates_stored_spells_and_levels()
    {
        var service = Service();
        Map(service, "_charSpells")[Pid] = new List<int> { 101 };
        Map(service, "_charSpellLevels")[Pid] = new Dictionary<int, int> { [101] = 1 };

        var levelUp = new DecodedPacket(Pid, false, "kqp", "CharacterLevelUpEvent", new[]
        {
            V("CharacterLevelUpEvent", "new_level", 100)
        }, Array.Empty<byte>());
        Dispatch(service, levelUp);

        var variant = new DecodedPacket(Pid, false, "hkl", "SpellVariantActivationEvent", new[]
        {
            V("SpellVariantActivationEvent", "spell_id", 102),
            V("SpellVariantActivationEvent", "effective", 1)
        }, Array.Empty<byte>());
        Dispatch(service, variant);

        Assert.Contains(102, service.LearnedSpells(Pid));
        Assert.Equal(1, service.LearnedSpellLevel(Pid, 102));
    }
}
