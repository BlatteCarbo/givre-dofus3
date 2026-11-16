using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class CharacterCreationTabTests
{
    private const string CapturedHex =
        "120B546573742D436C61756465" + "1801" + "2009" + "288901" + "3816" + "4218E9B69607FBF4D805ACDED503E7F6B705F491E20390AAEA07";

    [Fact]
    public void Blank_name_is_allowed_and_local_rules_reject_the_rest()
    {
        Assert.Null(CharacterCreationTab.NameError(null));
        Assert.Null(CharacterCreationTab.NameError("  "));
        Assert.Null(CharacterCreationTab.NameError("Test-Claude"));
        Assert.NotNull(CharacterCreationTab.NameError("Ab"));
        Assert.NotNull(CharacterCreationTab.NameError("A1b"));
        Assert.NotNull(CharacterCreationTab.NameError("-Claude"));
        Assert.Contains("Nom", CharacterCreationTab.FormError(1, 0, 0, "Ab"));
    }

    [Fact]
    public void Refusal_text_is_french_and_does_not_invent_name_compliance()
    {
        Assert.Null(CharacterCreationTab.RefusalText(0, 4));
        string invalid = CharacterCreationTab.RefusalText(2, 11)!;
        Assert.Contains("nom", invalid, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("11", invalid);
        Assert.DoesNotContain("alphabet", invalid, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("existe", invalid, StringComparison.OrdinalIgnoreCase);
        string none = CharacterCreationTab.RefusalText(1, null)!;
        string many = CharacterCreationTab.RefusalText(3, null)!;
        string forbidden = CharacterCreationTab.RefusalText(4, null)!;
        Assert.NotEqual(none, many);
        Assert.NotEqual(many, forbidden);
        Assert.NotEqual(none, forbidden);
        Assert.Contains("raison", none, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("personnage", many, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interdite", forbidden, StringComparison.OrdinalIgnoreCase);
        string unknown = CharacterCreationTab.RefusalText(5, null)!;
        Assert.Contains("5", unknown);
        Assert.DoesNotContain("nouveau", unknown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refusal_reads_the_mapped_result_and_ignores_other_varints()
    {
        static int? Fields(string message, string field) => (message, field) switch
        {
            ("CharacterCreationResultEvent", "result") => 7,
            ("CharacterCreationResultEvent", "reason") => 9,
            _ => null,
        };
        Assert.Null(CharacterCreationTab.Refusal(Array.Empty<ProtoField>(), Fields));
        var payload = new List<ProtoField>
        {
            new() { Field = 2, Wire = WireType.Varint, Varint = 4 },
            new() { Field = 7, Wire = WireType.Varint, Varint = 2 },
            new() { Field = 9, Wire = WireType.Varint, Varint = 11 },
        };
        string text = CharacterCreationTab.Refusal(payload, Fields)!;
        Assert.Contains("nom", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("11", text);
        Assert.DoesNotContain("4", text);
    }

    [Fact]
    public void Routes_when_the_client_is_on_the_list()
    {
        var ready = new CreationContext(false, false, true, true, true, false, true);
        Assert.Equal(CreationRoute.MitmList, CharacterCreationTab.Route(ready));
        Assert.Equal("", CharacterCreationTab.Notice(ready));

        var suggested = ready with { NameBlank = true, SuggestionReady = true };
        Assert.Equal(CreationRoute.MitmList, CharacterCreationTab.Route(suggested));

        var fields = ready with { FieldsComplete = false };
        Assert.Equal(CreationRoute.StopClientFirst, CharacterCreationTab.Route(fields));
        Assert.Equal(CharacterCreationTab.SocketBecauseFields + " " + CharacterCreationTab.StopClient, CharacterCreationTab.Notice(fields));

        var noSuggestion = ready with { NameBlank = true, SuggestionReady = false };
        Assert.Equal(CreationRoute.StopClientFirst, CharacterCreationTab.Route(noSuggestion));
        Assert.Equal(CharacterCreationTab.SocketBecauseNoSuggestion + " " + CharacterCreationTab.StopClient, CharacterCreationTab.Notice(noSuggestion));
    }

    [Fact]
    public void Routes_when_creation_must_use_socket()
    {
        var rememberedSocket = new CreationContext(false, true, false, false, true, false, true);
        Assert.Equal(CreationRoute.Socket, CharacterCreationTab.Route(rememberedSocket));
        Assert.Equal("", CharacterCreationTab.Notice(rememberedSocket));

        var rememberedMitm = new CreationContext(false, false, false, false, true, true, true);
        Assert.Equal(CreationRoute.Socket, CharacterCreationTab.Route(rememberedMitm));
        Assert.Equal(CharacterCreationTab.SocketBecauseNotOnList, CharacterCreationTab.Notice(rememberedMitm));

        var inGame = new CreationContext(false, false, true, false, true, false, true);
        Assert.Equal(CreationRoute.StopClientFirst, CharacterCreationTab.Route(inGame));
        Assert.Equal(CharacterCreationTab.SocketBecauseNotOnList + " " + CharacterCreationTab.StopClient, CharacterCreationTab.Notice(inGame));

        var socketSession = new CreationContext(true, true, true, false, true, false, true);
        Assert.Equal(CreationRoute.StopClientFirst, CharacterCreationTab.Route(socketSession));
        Assert.Equal(CharacterCreationTab.StopSession, CharacterCreationTab.Notice(socketSession));
    }

    [Fact]
    public void Colors_use_the_class_defaults_and_refuse_a_hole()
    {
        int[] chosen = { 1, 2, 3, 4, 5, 6 };
        Assert.Equal(chosen, CharacterCreationTab.ResolveColors(chosen, null));
        int[] holes = { 9, -1, -1, 4, -1, -1 };
        int[] defaults = { 10, 20, 30, 40, 50, 60 };
        Assert.Equal(new[] { 9, 20, 30, 4, 50, 60 }, CharacterCreationTab.ResolveColors(holes, defaults));
        Assert.Null(CharacterCreationTab.ResolveColors(holes, new[] { 10, 20 }));
        Assert.Null(CharacterCreationTab.ResolveColors(null, null));
    }

    [Fact]
    public void Catalog_fields_follow_the_names_and_the_captured_request()
    {
        Assert.Equal("gccm", CharacterCreationTab.BodyField);
        static int? Fields(string message, string field) => (message, field) switch
        {
            ("CharacterCreationRequest", "modeling_information") => 1,
            ("CharacterRemodelingInformation", "name") => 2,
            ("CharacterRemodelingInformation", "gender") => 3,
            ("CharacterRemodelingInformation", "breed_id") => 4,
            ("CharacterRemodelingInformation", "cosmetic_id") => 5,
            ("CharacterRemodelingInformation", "gccm") => 7,
            ("CharacterRemodelingInformation", "colors") => 8,
            ("CharacterCreationResultEvent", "result") => 1,
            ("CharacterNameSuggestionEvent", "suggestion") => 1,
            _ => null,
        };
        static string? Codes(string name) => name is "CharacterCreationRequest" or "CharacterNameSuggestionRequest" or "CharacterNameSuggestionEvent" ? "x" : null;
        Assert.False(CharacterCreationTab.ReadFields(Codes, (message, field) => field == "gccm" ? null : Fields(message, field)).Complete);
        var full = CharacterCreationTab.ReadFields(Codes, Fields);
        Assert.True(full.Complete);
        Assert.True(full.CanSuggest);

        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (!File.Exists(Path.Combine(dataDir, "fields.json"))) return;
        var cat = ProtocolCatalog.LoadFromDirectory(dataDir);
        var shipped = CharacterCreationTab.ReadFields(cat.CodeForName, cat.FieldNumber);
        Assert.True(shipped.Complete);
        Assert.True(shipped.CanSuggest);
        var nums = new CreationFieldNumbers(
            cat.FieldNumber("CharacterRemodelingInformation", "name") ?? 0,
            cat.FieldNumber("CharacterRemodelingInformation", "gender") ?? 0,
            cat.FieldNumber("CharacterRemodelingInformation", "breed_id") ?? 0,
            cat.FieldNumber("CharacterRemodelingInformation", "cosmetic_id") ?? 0,
            cat.FieldNumber("CharacterRemodelingInformation", CharacterCreationTab.BodyField) ?? 0,
            cat.FieldNumber("CharacterRemodelingInformation", "colors") ?? 0);
        var spec = new CharacterCreationSpec("Test-Claude", 1, 9, 137, 22,
            new[] { 15047528, 11942522, 7696171, 11402086, 7899379, 16422159 }, true);
        byte[] modeling = CharacterCreationEncoder.ModelingInfo(spec, nums);
        Assert.Equal(CapturedHex, Convert.ToHexString(modeling));
        MoveForge.UseCatalog(cat.CodeForName, cat.FieldNumber);
        byte[] frame = MoveForge.BuildCharacterCreationFramed(modeling);
        Assert.NotEmpty(frame);
        Assert.Contains(CapturedHex, Convert.ToHexString(frame));
        Assert.NotEmpty(MoveForge.BuildCharacterNameSuggestionFramed());
    }
}
