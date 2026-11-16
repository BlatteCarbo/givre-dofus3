using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Compatibilité tiers de l'API Lua: variantes de noms (MoonSharp userdata), snake_case des tables de paquets,
/// chemin de script -> nom, et résolution des noms de message Dofus 2 contre le VRAI catalogue livré.</summary>
public class LuaSnowCompatTests
{
    private static ProtocolCatalog Cat() => ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));

    [Theory]
    [InlineData("sellItem", "SellItem")]
    [InlineData("toZaap", "ToZaap")]
    [InlineData("SellItem", "sellItem")]
    [InlineData("change_map", "changeMap")]
    [InlineData("get_map_id_from_coords", "GetMapIdFromCoords")]
    public void NameCandidates_cover_moonsharp_fuzzing(string key, string expected)
        => Assert.Contains(expected, LuaSnowCompat.NameCandidates(key));

    [Fact]
    public void NameCandidates_never_return_the_key_itself()
        => Assert.DoesNotContain("changeMap", LuaSnowCompat.NameCandidates("changeMap"));

    [Theory]
    [InlineData("messageId", "message_id")]
    [InlineData("VisibleReplies", "visible_replies")]
    [InlineData("visible_replies", "visible_replies")]
    [InlineData("NPCName", "npc_name")]
    [InlineData("objectGID", "object_gid")]
    public void SnakeCase_converts_camel_and_pascal(string key, string expected)
        => Assert.Equal(expected, LuaSnowCompat.SnakeCase(key));

    [Theory]
    [InlineData(@"C:\Users\x\Scripts\trajets\Ble Astrub.lua", "Ble Astrub")]
    [InlineData("lib/quest.lua", "quest")]
    [InlineData("\"monscript\"", "monscript")]
    [InlineData("", "")]
    public void ScriptNameFromPath_keeps_only_the_name(string raw, string expected)
        => Assert.Equal(expected, LuaSnowCompat.ScriptNameFromPath(raw));

    [Theory]
    [InlineData("NpcDialogQuestionMessage", "NpcDialogQuestionEvent")]
    [InlineData("LeaveDialogMessage", "DialogLeaveEvent")]
    [InlineData("GameFightEndMessage", "FightEndEvent")]
    [InlineData("MapComplementaryInformationsDataMessage", "MapComplementaryInformationEvent")]
    [InlineData("ExchangeLeaveMessage", "ExchangeLeaveEvent")]
    [InlineData("TextInformationMessage", "TextInformationEvent")]
    [InlineData("NpcDialogQuestionEvent", "NpcDialogQuestionEvent")]
    public void Dofus2_message_names_resolve_against_the_shipped_catalog(string d2, string d3)
    {
        var cat = Cat();
        string? code = LuaSnowCompat.MessageNameCandidates(d2).Select(n => cat.CodeForName(n)).FirstOrDefault(c => c is not null);
        Assert.NotNull(code);
        Assert.Equal(cat.CodeForName(d3), code);
    }

    [Fact]
    public void Dofus2_public_ids_translate_to_names()
    {
        Assert.Equal("BasicNoOperationMessage", LuaMessageAliases.D2Ids[176]);
        Assert.Contains("NpcDialogQuestionEvent", LuaSnowCompat.MessageNameCandidates(
            LuaMessageAliases.D2Ids.First(kv => kv.Value == "NpcDialogQuestionMessage").Key.ToString()));
        Assert.True(LuaSnowCompat.IsNoOperation("176"));
        Assert.True(LuaSnowCompat.IsNoOperation(" BasicNoOperationMessage "));
    }

    [Theory]
    [InlineData('S', 0, 1, 9)]
    [InlineData('S', 1, 1, 8)]
    [InlineData('X', 0, 1, 5)]
    [InlineData('X', 1, 2, 8)]
    [InlineData('C', 0, 1, 5)]
    [InlineData('C', 0, 2, 13)]
    [InlineData('D', 0, 1, 5)]
    [InlineData('D', 1, 1, 4)]
    public void SimpleShape_matches_reference_getCells(char kind, int min, int radius, int expected)
    {
        // Cellule centrale loin des bords : toutes les cellules de la forme existent sur la carte.
        int center = MapPathfinder.CellFromMapPoint(18, 3);
        Assert.Equal(expected, CombatZones.SimpleShape(kind, center, min, radius).Count);
    }

    [Fact]
    public void Aligned_and_diagonal_follow_mappoint_axes()
    {
        int c = MapPathfinder.CellFromMapPoint(18, 3);
        Assert.True(CombatZones.Aligned(c, MapPathfinder.CellFromMapPoint(18, 7)));
        Assert.True(CombatZones.Aligned(c, MapPathfinder.CellFromMapPoint(14, 3)));
        Assert.False(CombatZones.Aligned(c, MapPathfinder.CellFromMapPoint(19, 4)));
        Assert.True(CombatZones.InDiagonal(c, MapPathfinder.CellFromMapPoint(20, 5)));
        Assert.False(CombatZones.InDiagonal(c, MapPathfinder.CellFromMapPoint(20, 4)));
    }

    [Fact]
    public void Alias_table_holds_clear_names_only()
    {
        // Jamais de code wire (3 lettres minuscules) dans la table livrée : les scripts ne voient que des noms clairs.
        Assert.DoesNotContain(LuaMessageAliases.D2ToD3.Values, v => v.Length <= 4 && v.All(char.IsLower));
        Assert.True(LuaMessageAliases.D2ToD3.Count > 500);
    }

    // ---- messages porteurs de secrets de session : jamais exposés à Lua (R2) ----

    [Theory]
    [InlineData("ReloginTokenEvent")]
    [InlineData("HaapiTokenEvent")]
    [InlineData("HaapiSessionEvent")]
    [InlineData("BakApiKeyEvent")]
    [InlineData("BakShopTokenEvent")]
    [InlineData("IdentificationRequest")]
    [InlineData("AuthenticationTicketAcceptedEvent")]
    [InlineData("ArenaSwitchToFightServerEvent")]
    [InlineData("ReconnectToGameServerEvent")]
    [InlineData("LockableUseCodeRequest")]
    [InlineData("CharacterDeletionRequest")]
    [InlineData("BakBuyValidationEvent")]
    [InlineData(" reloginTokenEvent ")]
    [InlineData("ClientIdRequest")]
    [InlineData("ClientChallengeInitRequest")]
    [InlineData("ClientChallengeProofRequest")]
    public void Session_secret_messages_are_sensitive(string name)
        => Assert.True(LuaSnowCompat.IsSensitiveMessage(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NpcDialogQuestionEvent")]
    [InlineData("MapComplementaryInformationEvent")]
    [InlineData("GameActionFightEvent")]
    [InlineData("LockableCodeResultEvent")]
    [InlineData("ExchangeNpcShopStartedEvent")]
    public void Gameplay_messages_are_not_sensitive(string? name)
        => Assert.False(LuaSnowCompat.IsSensitiveMessage(name));

    [Fact]
    public void Every_shipped_message_carrying_a_secret_field_is_sensitive()
    {
        // Garde-fou de patch : un message (ou type) du mapping livré qui porte un champ secret doit être refusé à Lua.
        var secretFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "token", "ticket_key", "session_uid", "secret_answer_hash", "secret_question", "email", "code", "login",
            "password", "certificate_hash", "address_token_one", "address_token_two",
        };
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "fields.json")));
        var missed = doc.RootElement.EnumerateObject()
            .Where(m => m.Value.ValueKind == JsonValueKind.Object && m.Value.EnumerateObject().Any(f => secretFields.Contains(f.Name)))
            .Select(m => m.Name)
            .Where(n => !LuaSnowCompat.IsSensitiveMessage(n))
            .ToList();
        Assert.Empty(missed);
    }

    [Theory]
    [InlineData("ReloginTokenStatusMessage")]
    [InlineData("6539")]
    [InlineData("HaapiApiKeyMessage")]
    [InlineData("HaapiTokenMessage")]
    [InlineData("HaapiSessionMessage")]
    [InlineData("AuthenticationTicketMessage")]
    public void Dofus2_names_of_secret_messages_resolve_to_sensitive_names(string d2)
    {
        // Même logique que GivreLuaHost.ResolveCore : nom candidat reconnu OU nom de réception du code -> refus.
        var cat = Cat();
        var hit = LuaSnowCompat.MessageNameCandidates(d2)
            .Select(n => (Name: n, Code: cat.CodeForName(n)))
            .FirstOrDefault(x => x.Code is not null);
        Assert.NotNull(hit.Code);
        Assert.True(LuaSnowCompat.IsSensitiveMessage(hit.Name) || LuaSnowCompat.IsSensitiveMessage(cat.NameForCode(hit.Code!)));
    }

    [Fact]
    public void Every_alias_target_exists_in_the_shipped_catalog()
    {
        // Garde-fou de régénération : une table d'alias construite contre une source périmée réintroduirait des alias morts.
        var cat = Cat();
        var missing = LuaMessageAliases.D2ToD3.Where(kv => cat.CodeForName(kv.Value) is null).Select(kv => kv.Key + " -> " + kv.Value).ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("ChallengeProposalEvent")]
    [InlineData("ShieldPointsVariation")]
    [InlineData("LockableCodeResultEvent")]
    [InlineData("NpcDialogQuestionEvent")]
    public void Ordinary_messages_are_not_sensitive(string name)
        => Assert.False(LuaSnowCompat.IsSensitiveMessage(name));
}
