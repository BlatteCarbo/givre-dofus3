using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GivreClone.App.Services.EquipmentSetData;
using GivreClone.App.Services.ProfessionData;
using Xunit;

namespace GivreClone.Tests;

public sealed class EquipmentSetDataCatalogTests
{
    private sealed class Fixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(),
            "GivreEquipmentSetDataTests", Guid.NewGuid().ToString("N"));
        public string JsonFile => Path.Combine(Folder, "equipment-sets.json");
        public string ManifestFile => Path.Combine(Folder, "manifest.json");

        public Fixture()
        {
            Directory.CreateDirectory(Folder);
            WriteData(BaseJson());
            WriteManifest();
        }

        public static JsonObject BaseJson() => JsonNode.Parse("""
            {
              "schemaVersion": 1,
              "sets": [
                { "id": 700, "name": "Ensemble démo atelier", "memberItemIds": [100,101] },
                { "id": 701, "name": "Ensemble démo recherche", "memberItemIds": [200] }
              ],
              "items": [
                { "id": 100, "name": "Coiffe démo", "setId": 700 },
                { "id": 101, "name": "Ceinture démo", "setId": 700 },
                { "id": 200, "name": "Anneau démo", "setId": 701 },
                { "id": 300, "name": "Plume démo", "setId": null }
              ]
            }
            """)!.AsObject();

        public void WriteData(JsonNode json) =>
            File.WriteAllText(JsonFile, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        public void WriteRaw(string json) => File.WriteAllText(JsonFile, json);

        public void WriteManifest(string version = "3.7.12.12", string source = "local-extraction",
            string permission = "owner-authorized", string format = "json",
            string assetName = "equipment-sets.json", string? hash = null)
        {
            byte[] bytes = File.ReadAllBytes(JsonFile);
            File.WriteAllText(ManifestFile, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, gameVersion = version, source, permission, format,
                asset = new
                {
                    file = assetName, bytes = bytes.LongLength,
                    sha256 = hash ?? Convert.ToHexString(SHA256.HashData(bytes))
                }
            }), Encoding.UTF8);
        }

        public EquipmentSetCatalog Read(string? expected = null) =>
            EquipmentSetCatalogReader.LoadDirectory(Folder, expected);

        public void Dispose()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    private sealed class ProfessionFixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(),
            "GivreEquipmentProfessionTests", Guid.NewGuid().ToString("N"));

        public ProfessionFixture(string version = "3.7.12.12")
        {
            Directory.CreateDirectory(Folder);
            var asset = Path.Combine(Folder, "recipes.json");
            File.WriteAllText(asset, """
                {
                  "schemaVersion": 1,
                  "items": [
                    { "id": 100, "name": "Coiffe démo", "level": 20 },
                    { "id": 101, "name": "Ceinture démo", "level": 20 },
                    { "id": 300, "name": "Plume démo", "level": 1 }
                  ],
                  "jobs": [{ "id": 13, "name": "Bijoutier" }],
                  "recipes": [
                    {
                      "id": "recette-test-100", "jobId": 13, "resultItemId": 100,
                      "requiredLevel": 20, "resultQuantity": 1, "experiencePerCraft": null,
                      "ingredients": [{ "itemId": 300, "quantity": 4 }]
                    }
                  ],
                  "levelThresholds": []
                }
                """);
            byte[] bytes = File.ReadAllBytes(asset);
            File.WriteAllText(Path.Combine(Folder, "manifest.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, gameVersion = version,
                source = "local-extraction", permission = "owner-authorized",
                format = "json", asset = new
                {
                    file = "recipes.json", bytes = bytes.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                }
            }));
        }

        public ProfessionCatalog Read() => ProfessionCatalogReader.LoadDirectory(Folder);

        public void Dispose()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    [Fact]
    public void Approved_synthetic_catalog_preserves_exact_members_and_searches_without_guessing()
    {
        using var fixture = new Fixture();
        byte[] before = File.ReadAllBytes(fixture.JsonFile);
        var catalog = fixture.Read("3.7.12.12");
        Assert.True(catalog.Admitted, string.Join("; ", catalog.Issues));
        Assert.True(catalog.Source.LocalHashVerified);
        Assert.False(catalog.Source.PublisherVerified);
        Assert.False(catalog.Source.AiDisclosureAuthorized);
        Assert.Equal(new long[] { 100, 101 }, catalog.FindSet(700)!.MemberItemIds);
        Assert.Equal(new long[] { 100, 101 }, catalog.ItemsOfSet(700).Select(x => x.Id));
        Assert.Equal(700, catalog.FindItem(100)!.SetId);
        Assert.Null(catalog.FindItem(300)!.SetId);
        Assert.Equal(new long[] { 700, 701 }, catalog.FindSets("ENSEMBLE DÉMO").Select(x => x.Id));
        Assert.Equal(new long[] { 100, 101 }, catalog.FindItems("Démo")
            .Where(x => x.SetId == 700).Select(x => x.Id));
        Assert.Empty(catalog.FindSets("Bouftou")); // no canonical set composed from a search term
        Assert.Empty(catalog.ItemsOfSet(999));
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(fixture.JsonFile)));
    }

    [Fact]
    public void Without_manifest_or_after_digest_tampering_import_fails_closed()
    {
        using var fixture = new Fixture();
        File.Delete(fixture.ManifestFile);
        Assert.False(fixture.Read().Admitted);
        fixture.WriteManifest(hash: new string('0', 64));
        Assert.False(fixture.Read().Admitted);
        fixture.WriteManifest();
        File.AppendAllText(fixture.JsonFile, " ");
        var result = fixture.Read();
        Assert.False(result.Admitted);
        Assert.Empty(result.Sets);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("3.7.12.12", "DofusDB-public-api", "owner-authorized", "json")]
    [InlineData("3.7.12.12", "local-extraction", "unverified", "json")]
    [InlineData("3.7.12.12", "local-extraction", "owner-authorized", "sqlite")]
    [InlineData("3.7.12", "local-extraction", "owner-authorized", "json")]
    [InlineData("4.0.0.70000", "local-extraction", "owner-authorized", "json")]
    public void Unsupported_source_permissions_format_or_version_are_rejected(
        string version, string source, string permission, string format)
    {
        using var fixture = new Fixture();
        fixture.WriteManifest(version, source, permission, format);
        Assert.False(fixture.Read().Admitted);
    }

    [Fact]
    public void Version_mismatch_and_traversal_asset_name_are_rejected()
    {
        using var fixture = new Fixture();
        Assert.False(fixture.Read("3.7.11.11").Admitted);
        fixture.WriteManifest(assetName: "../equipment-sets.json");
        Assert.False(fixture.Read().Admitted);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-in-set")]
    [InlineData("assigned-other-set")]
    [InlineData("listed-in-two-sets")]
    [InlineData("item-tag-not-member")]
    [InlineData("duplicate-set")]
    [InlineData("duplicate-item")]
    public void Bad_membership_or_identity_rejects_entire_catalog(string mutation)
    {
        using var fixture = new Fixture();
        var data = Fixture.BaseJson();
        var sets = data["sets"]!.AsArray();
        var items = data["items"]!.AsArray();
        switch (mutation)
        {
            case "missing":
                sets[0]!["memberItemIds"]!.AsArray().Add(999999);
                break;
            case "duplicate-in-set":
                sets[0]!["memberItemIds"]!.AsArray().Add(100);
                break;
            case "assigned-other-set":
                items[0]!["setId"] = 701;
                break;
            case "listed-in-two-sets":
                sets[1]!["memberItemIds"]!.AsArray().Add(100);
                break;
            case "item-tag-not-member":
                items[3]!["setId"] = 700;
                break;
            case "duplicate-set":
                sets.Add(sets[0]!.DeepClone());
                break;
            case "duplicate-item":
                items.Add(items[0]!.DeepClone());
                break;
        }
        fixture.WriteData(data);
        fixture.WriteManifest();
        var result = fixture.Read();
        Assert.False(result.Admitted);
        Assert.Empty(result.Sets);
    }

    [Fact]
    public void Duplicate_json_fields_and_unsupported_schema_version_are_rejected()
    {
        using var fixture = new Fixture();
        var duplicate = Fixture.BaseJson().ToJsonString()
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1");
        fixture.WriteRaw(duplicate);
        fixture.WriteManifest();
        Assert.False(fixture.Read().Admitted);
        var unsupported = Fixture.BaseJson();
        unsupported["schemaVersion"] = 2;
        fixture.WriteData(unsupported);
        fixture.WriteManifest();
        Assert.False(fixture.Read().Admitted);
    }

    [Fact]
    public void Oversized_manifest_or_member_list_fails_before_any_catalog_is_admitted()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ManifestFile,
            new string('x', EquipmentSetCatalogReader.MaxManifestBytes + 1));
        Assert.False(fixture.Read().Admitted);
        var data = Fixture.BaseJson();
        var members = data["sets"]![0]!["memberItemIds"]!.AsArray();
        for (int i = 0; i < EquipmentSetCatalogReader.MaxMembersPerSet; i++)
            members.Add(10_000 + i);
        fixture.WriteData(data);
        fixture.WriteManifest();
        var rejected = fixture.Read();
        Assert.False(rejected.Admitted);
        Assert.Empty(rejected.Sets);
    }

    [Fact]
    public void Invalid_manifest_property_or_empty_set_names_are_rejected()
    {
        using var fixture = new Fixture();
        string manifest = File.ReadAllText(fixture.ManifestFile);
        File.WriteAllText(fixture.ManifestFile,
            manifest.Replace("\"format\":\"json\"", "\"format\":\"json\",\"extra\":true"));
        Assert.False(fixture.Read().Admitted);
        var data = Fixture.BaseJson();
        data["sets"]![0]!["name"] = "   ";
        fixture.WriteData(data);
        fixture.WriteManifest();
        Assert.False(fixture.Read().Admitted);
    }

    [Fact]
    public void Explicit_unknown_names_retain_identifiers_but_never_match_by_fuzzy_label()
    {
        using var fixture = new Fixture();
        var data = Fixture.BaseJson();
        data["sets"]![0]!["name"] = null;
        data["items"]![0]!["name"] = null;
        fixture.WriteData(data);
        fixture.WriteManifest();
        var result = fixture.Read();
        Assert.True(result.Admitted, string.Join("; ", result.Issues));
        Assert.Null(result.FindSet(700)!.Name);
        Assert.Null(result.FindItem(100)!.Name);
        Assert.Equal(new long[] { 701 }, result.FindSets("Ensemble").Select(x => x.Id));
    }

    [Fact]
    public void Recipe_join_matches_only_same_version_and_exact_item_ids()
    {
        using var fixture = new Fixture();
        using var professionFixture = new ProfessionFixture();
        var sets = fixture.Read();
        var professions = professionFixture.Read();
        Assert.True(professions.Admitted, string.Join("; ", professions.Issues));
        var result = EquipmentSetRecipeBinder.ForSet(sets, professions, 700);
        Assert.True(result.Available, string.Join("; ", result.Issues));
        Assert.False(result.AiDisclosureAuthorized);
        Assert.Equal(2, result.Pieces.Count);
        Assert.Equal("recette-test-100", Assert.Single(result.Pieces[0].Recipes).Id);
        Assert.Equal("LOCAL_RECIPE_UNVERIFIED", result.Pieces[0].RecipeStatus);
        Assert.Empty(result.Pieces[1].Recipes);
        Assert.Equal("NO_RECIPE_IN_SUPPLIED_SNAPSHOT", result.Pieces[1].RecipeStatus);
        Assert.Equal(sets.Source.FileSha256, result.SetSha256);
        Assert.Equal(professions.Source.FileSha256, result.RecipeSha256);
    }

    [Fact]
    public void No_recipe_catalog_is_explicit_unknown_and_version_mismatch_never_joins()
    {
        using var fixture = new Fixture();
        using var wrongVersion = new ProfessionFixture("3.7.11.11");
        var sets = fixture.Read();
        var noRecipes = EquipmentSetRecipeBinder.ForSet(sets, null, 700);
        Assert.True(noRecipes.Available);
        Assert.All(noRecipes.Pieces, piece =>
            Assert.Equal("RECIPE_SOURCE_UNKNOWN", piece.RecipeStatus));
        var mismatch = EquipmentSetRecipeBinder.ForSet(sets, wrongVersion.Read(), 700);
        Assert.False(mismatch.Available);
        Assert.Empty(mismatch.Pieces);
        Assert.False(EquipmentSetRecipeBinder.ForSet(sets, null, 999).Available);
    }
}
