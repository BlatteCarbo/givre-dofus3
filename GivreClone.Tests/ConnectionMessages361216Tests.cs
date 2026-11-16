using GivreClone.App.Services.Transport.Socket;
using Xunit;

namespace GivreClone.Tests;

public class ConnectionMessages361216Tests
{
    [Fact]
    public void Identification_server_uses_361216_status_and_mono_account_fields()
    {
        var server = new ProtoWire.Writer();
        server.Int32(1, 291);
        server.Int32(2, 1);       // ONLINE
        server.Int32(3, 1);       // limited=true; must not be read as mono_account
        server.Int32(4, 2);       // server type
        server.Int32(5, 1);       // mono_account=true

        var info = new ProtoWire.Writer();
        info.Message(1, server.ToArray());
        info.Int32(2, 0);         // ACCESSIBLE

        var list = new ProtoWire.Writer();
        list.Message(1, info.ToArray());

        var success = new ProtoWire.Writer();
        success.String(2, "fixture-account");
        success.Message(4, list.ToArray());

        var identification = new ProtoWire.Writer();
        identification.Message(1, success.ToArray());

        var response = new ProtoWire.Writer();
        response.Message(3, identification.ToArray());

        var message = new ProtoWire.Writer();
        message.Message(2, response.ToArray());

        var decoded = ConnectionMessages.Decode(message.ToArray());
        Assert.Equal(ConnDecoded.Kind.IdentificationSuccess, decoded.Type);
        var s = Assert.Single(decoded.Servers);
        Assert.Equal(291, s.Id);
        Assert.Equal(1, s.Status);
        Assert.True(s.MonoAccount);
        Assert.Empty(s.Characters);
    }

    [Fact]
    public void Character_list_response_groups_exact_character_fields_by_server_id()
    {
        var first = Character("Alpha", 8, 0, 73, "2999-01-01T00:00:00Z", 291);
        var second = Character("Beta", 3, 1, 41, "2998-01-01T00:00:00Z", 291);
        var third = Character("Gamma", 10, 0, 22, "2997-01-01T00:00:00Z", 292);

        var chars = new ProtoWire.Writer();
        chars.Message(1, first);
        chars.Message(1, second);
        chars.Message(1, third);

        var response = new ProtoWire.Writer();
        response.Message(6, chars.ToArray());

        var message = new ProtoWire.Writer();
        message.Message(2, response.ToArray());

        var decoded = ConnectionMessages.Decode(message.ToArray());
        Assert.Equal(ConnDecoded.Kind.CharacterList, decoded.Type);
        Assert.Equal(2, decoded.Servers.Count);
        var s291 = Assert.Single(decoded.Servers, s => s.Id == 291);
        Assert.Collection(s291.Characters,
            c => { Assert.Equal("Alpha", c.Name); Assert.Equal(8, c.Breed); Assert.Equal(73, c.Level); Assert.Equal(291, c.ServerId); },
            c => { Assert.Equal("Beta", c.Name); Assert.Equal(3, c.Breed); Assert.Equal(1, c.Gender); Assert.Equal(41, c.Level); });
        var s292 = Assert.Single(decoded.Servers, s => s.Id == 292);
        var c292 = Assert.Single(s292.Characters);
        Assert.Equal("Gamma", c292.Name);
        Assert.Equal("2997-01-01T00:00:00Z", c292.LastConnectionDate);
    }

    [Fact]
    public void Merge_server_characters_preserves_authoritative_metadata_and_drops_unknown_servers()
    {
        var metadata = new[]
        {
            new ConnServer { Id = 291, Status = 1, MonoAccount = true, Accessibility = 2 },
            new ConnServer { Id = 292, Status = 2, MonoAccount = false, Accessibility = 0 },
        };
        var known = new ConnServer { Id = 291 };
        known.Characters.Add(new ConnCharacter { Name = "Alpha", Breed = 8, Level = 73, ServerId = 291 });
        var unknown = new ConnServer { Id = 999 };
        unknown.Characters.Add(new ConnCharacter { Name = "Orphan", Breed = 3, Level = 10, ServerId = 999 });

        var merged = ConnectionMessages.MergeServerCharacters(metadata, new[] { known, unknown });

        Assert.Equal(2, merged.Count);
        var s291 = Assert.Single(merged, s => s.Id == 291);
        Assert.Equal(1, s291.Status);
        Assert.True(s291.MonoAccount);
        Assert.Equal(2, s291.Accessibility);
        Assert.Equal("Alpha", Assert.Single(s291.Characters).Name);
        Assert.Empty(Assert.Single(merged, s => s.Id == 292).Characters);
        Assert.DoesNotContain(merged, s => s.Id == 999);
    }

    private static byte[] Character(string name, int breed, int gender, int level, string date, int serverId)
    {
        var c = new ProtoWire.Writer();
        c.String(1, name);
        c.Int32(2, breed);
        c.Int32(3, gender);
        c.Int32(4, level);
        c.String(5, date);
        c.Int32(6, serverId);
        return c.ToArray();
    }
}
