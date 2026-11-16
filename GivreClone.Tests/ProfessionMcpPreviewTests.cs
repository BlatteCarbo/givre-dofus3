using System.Text.Json;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ProfessionPlanner;
using Xunit;

namespace GivreClone.Tests;

public sealed class ProfessionMcpPreviewTests
{
    private const string Version = "3.7.11.11";
    private static readonly string Sha = new('A', 64);
    private const string Goal = "\"jobId\":28,\"startLevel\":20,\"targetLevel\":30";
    private const string Visual = "{\"op\":\"visual_preview\"," + Goal + "}";
    private const string Routes = "{\"op\":\"farm_routes\"," + Goal + "}";
    private const string Outline = "{\"op\":\"script_outline\"," + Goal + "}";
    private static ProfessionPlanRequest Request() => new(
        new ProfessionGoal(28, "Bijoutier", 20, 30, 100, true), 5, null,
        [new ProfessionRecipe(5, 99, 1, [new RecipeIngredient(20, 2)],
            20, 28, 50, true, Version, true, 5, 0m)],
        [], [new MaterialSource(20, AcquisitionMethod.Gather, 2, 0, Version, true)],
        new XpTable(Version, true, [new XpThreshold(20, 100), new XpThreshold(30, 200)]));
    private static ProfessionMcpProvenance Evidence(string kind = "local-owned") =>
        new(kind, "synthetic-test-source", Version, Sha, true, "approved-synthetic");
    private static ProfessionMcpPreviewSnapshot Snapshot(
        IReadOnlyCollection<ProfessionMcpRouteEvidence>? routes = null,
        ProfessionMcpPartnerGrant? grant = null,
        ProfessionMcpProvenance? provenance = null,
        ProfessionPlanRequest? request = null) =>
        new(new ProfessionMcpSnapshot(request ?? Request(), provenance ?? Evidence()), routes, grant);
    private static ProfessionMcpRouteEvidence Route(long item = 20,
        bool verified = true, string version = Version, AcquisitionMethod method = AcquisitionMethod.Gather) =>
        new("farm-mine-a", item, method, [88081690, 68552196], ["edge-local-1"],
            version, "local-unity", "source-fixture", Sha, verified, true, "approved-route-fixture");
    private static JsonDocument Query(string json, ProfessionMcpPreviewSnapshot? snapshot = null) =>
        JsonDocument.Parse(ProfessionMcpPreviewFacade.QueryJson(json, snapshot ?? Snapshot()));
    private static string Status(JsonDocument doc) => doc.RootElement.GetProperty("status").GetString()!;

    [Fact]
    public void Capabilities_work_without_snapshot_and_are_non_executable()
    {
        using var doc = JsonDocument.Parse(ProfessionMcpPreviewFacade.QueryJson(
            "{\"op\":\"capabilities\"}", null));
        Assert.Equal("KNOWN", Status(doc));
        Assert.False(doc.RootElement.GetProperty("luaExecutable").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("executed").GetBoolean());
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"op\":\"run_lua\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30}")]
    [InlineData("{\"op\":\"visual_preview\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"script\":\"buy()\"}")]
    [InlineData("{\"op\":\"visual_preview\",\"op\":\"visual_preview\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30}")]
    [InlineData("{\"op\":\"visual_preview\",\"jobId\":\"28\",\"startLevel\":20,\"targetLevel\":30}")]
    [InlineData("{\"op\":\"visual_preview\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"limit\":31}")]
    [InlineData("{\"op\":\"visual_preview\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"offset\":2049}")]
    [InlineData("{\"op\":\"script_outline\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"limit\":10}")]
    [InlineData("{\"op\":\"visual_preview\",\"jobId\":28,\"startLevel\":30,\"targetLevel\":20}")]
    public void Rejects_bad_or_mutating_json(string input)
    {
        using var result = Query(input);
        Assert.Equal("INVALID_REQUEST", Status(result));
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
    }

    [Fact]
    public void Missing_or_unapproved_snapshot_is_unknown()
    {
        using var absent = JsonDocument.Parse(ProfessionMcpPreviewFacade.QueryJson(Visual, null));
        Assert.Equal("UNKNOWN", Status(absent));
        using var denied = Query(Visual, Snapshot(provenance: Evidence() with
        { AiDisclosureAuthorized = false }));
        Assert.Equal("UNKNOWN", Status(denied));
        Assert.DoesNotContain("synthetic-test-source", denied.RootElement.ToString());
    }

    [Fact]
    public void Preview_shows_recursive_bom_nodes_and_evidence_not_made_up_xp()
    {
        using var result = Query(Visual);
        Assert.Equal("KNOWN", Status(result));
        var steps = result.RootElement.GetProperty("nodes");
        Assert.True(steps.GetArrayLength() >= 2);
        Assert.Equal(28, result.RootElement.GetProperty("target").GetProperty("JobId").GetInt32());
        Assert.Equal(Version, result.RootElement.GetProperty("version").GetString());
        Assert.False(result.RootElement.GetProperty("acknowledged").GetBoolean());
    }

    [Fact]
    public void Verified_source_route_can_only_be_shown_for_planned_method()
    {
        var snapshot = Snapshot([Route()]);
        using var result = Query(Routes, snapshot);
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal("edge-local-1", result.RootElement.GetProperty("routes")[0]
            .GetProperty("edgeIds")[0].GetString());
        Assert.Equal("GATHER", result.RootElement.GetProperty("routes")[0]
            .GetProperty("method").GetString());
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
    }

    [Theory]
    [InlineData(19, true)]
    [InlineData(20, false)]
    public void Unknown_item_or_unverified_edge_is_not_a_route(long item, bool verified)
    {
        using var result = Query(Routes, Snapshot([Route(item, verified)]));
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Empty(result.RootElement.GetProperty("routes").EnumerateArray());
    }

    [Fact]
    public void No_route_evidence_never_infers_farm_coordinates()
    {
        using var result = Query(Routes);
        Assert.Equal("UNKNOWN", Status(result));
        Assert.Equal(0, result.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Version_mismatch_or_different_acquisition_method_never_creates_route()
    {
        using var result = Query(Routes, Snapshot([Route(version: "3.7.12.12"),
            Route(method: AcquisitionMethod.Buy)]));
        Assert.Equal("UNKNOWN", Status(result));
    }

    [Fact]
    public void Outline_never_contains_executable_lua()
    {
        using var result = Query(Outline);
        Assert.Equal("KNOWN", Status(result));
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("executableLua").ValueKind);
        Assert.True(result.RootElement.GetProperty("requiresSeparateGameplayPermission").GetBoolean());
        Assert.False(result.RootElement.GetProperty("executed").GetBoolean());
        Assert.Contains("-- GivreWork profession plan", result.RootElement.GetProperty("luaDraft").GetString());
        Assert.False(result.RootElement.GetProperty("luaDraftIsExecutableGameplay").GetBoolean());
        Assert.DoesNotContain("map:changeMap", result.RootElement.ToString());
    }

    [Fact]
    public void Goal_mismatch_does_not_expose_catalogue()
    {
        using var result = Query("{\"op\":\"visual_preview\",\"jobId\":27,\"startLevel\":20,\"targetLevel\":30}");
        Assert.Equal("UNKNOWN", Status(result));
    }

    [Fact]
    public void Route_and_list_volumes_are_bounded()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(
            Enumerable.Range(0, ProfessionMcpPreviewSnapshot.MaxRoutes + 1)
                .Select(i => Route() with { RouteId = "route" + i }).ToArray()));
        Assert.Throws<ArgumentException>(() => Snapshot([Route() with
        { MapIds = Enumerable.Range(1, 35).Select(x => (long)x).ToArray() }]));
    }

    [Fact]
    public void Dofusdb_requires_a_specific_operator_attestation_and_source_hash()
    {
        var p = Evidence("dofusdb") with { SourceId = "dofusdb-api", AuthorizationEvidenceId =
            "operator-" + new string('a', 32) };
        var grant = new ProfessionMcpPartnerGrant("dofusdb", "api", Version, Sha,
            p.AuthorizationEvidenceId, new DateOnly(2099, 12, 31), true, true, true);
        using var denied = Query(Visual, Snapshot(provenance: p));
        Assert.Equal("UNKNOWN", Status(denied));
        using var permitted = Query(Visual, Snapshot(provenance: p, grant: grant));
        Assert.Equal("KNOWN", Status(permitted));
        Assert.False(permitted.RootElement.GetProperty("provenance")
            .GetProperty("independentlyRightsVerified").GetBoolean());
        using var wrongSha = Query(Visual, Snapshot(provenance: p,
            grant: grant with { SourceSha256 = new string('B', 64) }));
        Assert.Equal("UNKNOWN", Status(wrongSha));
    }

    [Fact]
    public void False_partnership_grant_is_refused()
    {
        var p = Evidence("dofusdb") with { AuthorizationEvidenceId =
            "operator-" + new string('a', 32) };
        var grant = new ProfessionMcpPartnerGrant("dofusdb", "api", Version, Sha,
            p.AuthorizationEvidenceId, new DateOnly(2099, 12, 31), true, false, true);
        using var response = Query(Visual, Snapshot(grant: grant, provenance: p));
        Assert.Equal("UNKNOWN", Status(response));
    }
}
