using System.Collections.Generic;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class CharacterInfoTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void Character_information_code_is_pinned()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        // Le code obfusqué tourne entre versions : le nom clair provient du mapping livré.
        var code = cat.CodeForName("CharacterListEvent");
        Assert.NotNull(code);
        Assert.Equal("CharacterListEvent", cat.NameForCode(code));
    }

    [Fact]
    public void Extracts_name_and_level_from_a_lpe_like_message()
    {
        // Structure reelle du client (lpe) : { repeated bool #1 ; string name #2 ; int32 level #3 }
        var lpe = new List<ProtoField>
        {
            new() { Field = 1, Wire = WireType.Varint, Varint = 1 },            // bool : ne doit PAS etre pris pour le niveau
            new() { Field = 2, Wire = WireType.Len, String = "Persozero" },
            new() { Field = 3, Wire = WireType.Varint, Varint = 176 },
        };
        var (name, level) = CharacterInfoReader.ExtractNameLevel(lpe);
        Assert.Equal("Persozero", name);
        Assert.Equal(176, level);
    }

    [Fact]
    public void Extracts_name_and_level_from_the_nested_kvi_structure()
    {
        // Capture reelle : kvi { repeated lpg { id#2 ; oneof { lpe#1 { bool#1 ; name#2 ; level#3 } } } }
        var lpe = new List<ProtoField>
        {
            new() { Field = 1, Wire = WireType.Varint, Varint = 1 },
            new() { Field = 2, Wire = WireType.Len, String = "Persodeux" },
            new() { Field = 3, Wire = WireType.Varint, Varint = 14 },
        };
        var lpg = new List<ProtoField>
        {
            new() { Field = 2, Wire = WireType.Varint, Varint = 612345678901 },  // id perso (int64, >255 -> exclu)
            new() { Field = 1, Wire = WireType.Len, Message = lpe },
        };
        var kvi = new List<ProtoField>
        {
            new() { Field = 1, Wire = WireType.Len, Message = lpg },
        };

        var (name, level) = CharacterInfoReader.ExtractNameLevel(kvi);
        Assert.Equal("Persodeux", name);
        Assert.Equal(14, level);
    }
}
