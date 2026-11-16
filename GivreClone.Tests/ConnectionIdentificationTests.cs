using System.Text;
using GivreClone.App.Services.Transport.Socket;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Reconstruit la réponse d'identification 3.7.11.11 OBSERVÉE (trame réelle du 2026-10-17, structure relevée par le diagnostic du clone) :
/// LoginMessage{ response(2): { uid(1), identification(3): { success(1): { compte(1), pseudo(2), tag(3), serverList(4): { serveurs(1): ServerInformation{ server(1): {id(1), type(3)},
/// accessibilité(2), personnages(3 répété): {nom 1, classe 2, sexe 3, niveau 4, dernière connexion 5} }, slots(2) } } } } }.
/// Les noms de personnages sont ceux du compte jetable de l'essai.
/// </summary>
public class ConnectionIdentificationTests
{
    private static byte[] V(int field, ulong v) { var l = new List<byte> { (byte)(field << 3) }; while (v >= 0x80) { l.Add((byte)(v | 0x80)); v >>= 7; } l.Add((byte)v); return l.ToArray(); }
    private static byte[] L(int field, byte[] body) { var l = new List<byte> { (byte)((field << 3) | 2) }; ulong n = (ulong)body.Length; while (n >= 0x80) { l.Add((byte)(n | 0x80)); n >>= 7; } l.Add((byte)n); l.AddRange(body); return l.ToArray(); }
    private static byte[] S(int field, string s) => L(field, Encoding.UTF8.GetBytes(s));
    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

    private static byte[] Character(string name, int breed, int gender, int level, string last)
        => Cat(S(1, name), V(2, (ulong)breed), gender != 0 ? V(3, (ulong)gender) : Array.Empty<byte>(), V(4, (ulong)level), S(5, last));

    private static byte[] Server(int id, int type, int accessibility, params byte[][] characters)
        => Cat(L(1, Cat(V(1, (ulong)id), V(3, (ulong)type))),
               accessibility != 0 ? V(2, (ulong)accessibility) : Array.Empty<byte>(),
               Cat(characters.Select(c => L(3, c)).ToArray()));

    private static byte[] Frame(params byte[][] servers)
    {
        var serverList = Cat(Cat(servers.Select(s => L(1, s)).ToArray()), L(2, V(2, 5)), L(2, Cat(V(1, 1), V(2, 5))));
        var success = Cat(V(1, 123456789), S(2, "Nick"), S(3, "1234"), L(4, serverList), L(5, Array.Empty<byte>()), L(6, Array.Empty<byte>()));
        return L(2, Cat(L(1, new byte[] { 1 }), L(3, L(1, success))));
    }

    [Fact]
    public void Characters_are_read_from_each_server_of_the_identification_response()
    {
        var frame = Frame(
            Server(354, 2, 1),
            Server(351, 3, 0),
            Server(291, 1, 0, Character("Coeur-Detruct", 18, 1, 1, "2026-09-29T13:58:43.451+02:00"), Character("Chat-jaipeter", 8, 1, 14, "2026-10-09T18:00:42.602+02:00")),
            Server(292, 1, 0, Character("Xyla-Taz", 10, 0, 63, "2026-10-17T00:10:24.943+02:00")));
        var d = ConnectionMessages.Decode(frame);
        Assert.Equal(ConnDecoded.Kind.IdentificationSuccess, d.Type);
        Assert.True(d.CharactersEmbedded);
        Assert.Equal(new[] { 354, 351, 291, 292 }, d.Servers.Select(s => s.Id).ToArray());
        Assert.Equal(1, d.Servers[0].Accessibility);
        Assert.Equal(0, d.Servers[1].Accessibility);
        Assert.Empty(d.Servers[0].Characters);
        var s291 = d.Servers[2];
        Assert.Equal(new[] { "Coeur-Detruct", "Chat-jaipeter" }, s291.Characters.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { 18, 8 }, s291.Characters.Select(c => c.Breed).ToArray());
        Assert.Equal(new[] { 1, 14 }, s291.Characters.Select(c => c.Level).ToArray());
        Assert.All(s291.Characters, c => Assert.Equal(291, c.ServerId));
        var xyla = d.Servers[3].Characters.Single();
        Assert.Equal(("Xyla-Taz", 10, 0, 63, 292), (xyla.Name, xyla.Breed, xyla.Gender, xyla.Level, xyla.ServerId));
        Assert.Equal("2026-10-17T00:10:24.943+02:00", xyla.LastConnectionDate);
    }

    [Fact]
    public void Absent_status_is_online_like_the_original_and_other_values_are_shown_raw()
    {
        var d = ConnectionMessages.Decode(Frame(Server(354, 2, 1)));
        Assert.Equal("en ligne", d.Servers[0].StatusText);
        Assert.True(d.Servers[0].Online);
        Assert.True(new ConnServer { Status = 1 }.Online);
        Assert.False(new ConnServer { Status = 2 }.Online);
        Assert.False(new ConnServer { Status = 7 }.Online);
        Assert.Equal("maintenance", new ConnServer { Status = 2 }.StatusText);
        Assert.Equal("statut 7", new ConnServer { Status = 7 }.StatusText);
    }

    [Fact]
    public void An_account_without_character_reports_none_embedded()
    {
        var d = ConnectionMessages.Decode(Frame(Server(354, 2, 1), Server(351, 3, 0)));
        Assert.False(d.CharactersEmbedded);
        Assert.Equal(0, d.Servers.Sum(s => s.Characters.Count));
    }

    [Fact]
    public void Merge_keeps_embedded_characters_when_the_buckets_are_the_same_list()
    {
        var d = ConnectionMessages.Decode(Frame(Server(292, 1, 0, Character("Xyla-Taz", 10, 0, 63, "2026-10-17T00:10:24.943+02:00"))));
        var merged = ConnectionMessages.MergeServerCharacters(d.Servers.ToArray(), d.Servers.ToArray());
        Assert.Equal("Xyla-Taz", merged.Single().Characters.Single().Name);
    }
}
