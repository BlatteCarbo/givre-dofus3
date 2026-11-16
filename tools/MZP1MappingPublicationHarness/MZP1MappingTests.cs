using GivreClone.App.Services;
using Xunit;

// All tests exercise the actual GivreClone.App/Services/MappingCatalog.cs.
// LicenseService and schema parsers are stubs, NOT live game authentication.
public sealed class MZP1MappingTests
{
    private static TestBundle Bundle(string fields) => new() { Artefacts =
        new Dictionary<string, string> { ["proto"] = "fixture", ["matching"] = "fixture", ["fields"] = fields } };

    private static void Reset(Func<Task<TestBundle?>> fetch)
    {
        MappingCatalog.Clear();
        LicenseService.SigningBlocked = false;
        LicenseService.Fetch = fetch;
        Logger.Entries.Clear();
    }

    [Fact]
    public async Task Partial_bundle_never_becomes_effective_streamed_mapping()
    {
        Reset(() => Task.FromResult<TestBundle?>(Bundle("missing")));
        var callbacks = 0;
        void OnReady() => callbacks++;
        MappingCatalog.Ready += OnReady;
        try
        {
            await MappingCatalog.EnsureFetchedAsync();
            Assert.False(MappingCatalog.IsReady);
            Assert.Null(MappingCatalog.Streamed);
            Assert.Equal(0, callbacks);
            Assert.Single(MappingCatalog.MissingRequiredFields);
            Assert.Contains(Logger.Entries, x => x.Contains("rejeté", StringComparison.Ordinal));
        }
        finally { MappingCatalog.Ready -= OnReady; MappingCatalog.Clear(); }
    }

    [Fact]
    public async Task Complete_bundle_is_published_to_the_actual_consumer_property()
    {
        Reset(() => Task.FromResult<TestBundle?>(Bundle("all")));
        var callbacks = 0;
        void OnReady() => callbacks++;
        MappingCatalog.Ready += OnReady;
        try
        {
            await MappingCatalog.EnsureFetchedAsync();
            Assert.True(MappingCatalog.IsReady);
            Assert.NotNull(MappingCatalog.Streamed);
            Assert.Equal(1, callbacks);
            Assert.Empty(MappingCatalog.MissingRequiredFields);
        }
        finally { MappingCatalog.Ready -= OnReady; MappingCatalog.Clear(); }
    }

    [Fact]
    public async Task Empty_structure_is_rejected_even_if_manifest_would_claim_complete()
    {
        Reset(() => Task.FromResult<TestBundle?>(Bundle("empty")));
        try
        {
            await MappingCatalog.EnsureFetchedAsync();
            Assert.Null(MappingCatalog.Streamed);
        }
        finally { MappingCatalog.Clear(); }
    }

    [Fact]
    public async Task Fetch_finished_after_clear_cannot_reinstall_mapping()
    {
        var pending = new TaskCompletionSource<TestBundle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Reset(() => pending.Task);
        var task = MappingCatalog.EnsureFetchedAsync();
        MappingCatalog.Clear();
        pending.SetResult(Bundle("all"));
        await task;
        Assert.Null(MappingCatalog.Streamed);
        Assert.False(MappingCatalog.IsReady);
    }

    [Fact]
    public void Null_incomplete_and_valid_policy_verdicts_are_deterministic()
    {
        Assert.False(MZP1MappingPublicationPolicy.HasCompleteRequirements(5, 5, null));
        Assert.False(MZP1MappingPublicationPolicy.HasCompleteRequirements(5, 5, ["GameMessage.uid"]));
        Assert.False(MZP1MappingPublicationPolicy.HasCompleteRequirements(0, 5, []));
        Assert.False(MZP1MappingPublicationPolicy.HasCompleteRequirements(5, 0, []));
        Assert.True(MZP1MappingPublicationPolicy.HasCompleteRequirements(5, 5, []));
    }

    [Fact]
    public async Task Revocation_resets_published_mapping_and_missing_diagnostics()
    {
        Reset(() => Task.FromResult<TestBundle?>(Bundle("missing")));
        await MappingCatalog.EnsureFetchedAsync();
        Assert.Single(MappingCatalog.MissingRequiredFields);
        MappingCatalog.Clear();
        Assert.Null(MappingCatalog.Streamed);
        Assert.Empty(MappingCatalog.MissingRequiredFields);
    }
}
