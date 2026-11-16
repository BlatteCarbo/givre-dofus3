extern alias F06App;

using System.Net.Http;
using F06App::GivreClone.App.Services.Treasure;
using Xunit;

namespace GivreClone.Tests;

public sealed class TreasureClueResolutionTests
{
    [Fact]
    public async Task Source_failures_have_distinct_explanations_and_never_lookup_a_map()
    {
        var errors = new HashSet<string>();
        foreach (var failure in new Exception[] { new HttpRequestException(),
            new OperationCanceledException(), new FormatException() })
        {
            var result = await TreasureClueResolution.ResolveAsync("Rocher",
                Task.FromException<IReadOnlyList<TreasureClue>>(failure),
                (_, _) => throw new InvalidOperationException("Must not lookup a failed source"),
                CancellationToken.None);
            Assert.Null(result.Map);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.True(errors.Add(result.Error!));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Caller_cancellation_is_not_reported_as_a_source_timeout(bool sourceCancelled)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var loading = sourceCancelled
            ? Task.FromCanceled<IReadOnlyList<TreasureClue>>(cts.Token)
            : Task.FromResult<IReadOnlyList<TreasureClue>>(Array.Empty<TreasureClue>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TreasureClueResolution.ResolveAsync("Rocher", loading, (_, _) => 123, cts.Token));
    }

    [Fact]
    public async Task Matching_clue_coordinates_resolve_through_the_local_catalog()
    {
        var loading = Task.FromResult<IReadOnlyList<TreasureClue>>(new[] {
            new TreasureClue(1, 2, 3, 1, "Autre"),
            new TreasureClue(2, 4, 5, 2, "Rocher") });
        var result = await TreasureClueResolution.ResolveAsync("Rocher", loading,
            (x, y) => x == 4 && y == 5 ? 123L : null, CancellationToken.None);

        Assert.Equal(123L, result.Map);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Missing_clue_and_missing_local_map_are_distinguished()
    {
        var missingClue = await TreasureClueResolution.ResolveAsync("Rocher",
            Task.FromResult<IReadOnlyList<TreasureClue>>(Array.Empty<TreasureClue>()),
            (_, _) => throw new InvalidOperationException("No clue to resolve"), CancellationToken.None);
        var missingMap = await TreasureClueResolution.ResolveAsync("Rocher",
            Task.FromResult<IReadOnlyList<TreasureClue>>(new[] { new TreasureClue(2, 4, 5, 2, "Rocher") }),
            (_, _) => null, CancellationToken.None);

        Assert.Null(missingClue.Map);
        Assert.Null(missingMap.Map);
        Assert.False(string.IsNullOrWhiteSpace(missingClue.Error));
        Assert.False(string.IsNullOrWhiteSpace(missingMap.Error));
        Assert.NotEqual(missingClue.Error, missingMap.Error);
    }
}
