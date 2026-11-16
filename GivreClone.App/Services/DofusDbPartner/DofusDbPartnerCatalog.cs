using System.Collections.ObjectModel;

namespace GivreClone.App.Services.DofusDbPartner;

/// <summary>Human assertion; never a publisher licence verification or an in-game receipt.</summary>
public sealed record DofusDbPartnerAdmission(
    bool OperatorApprovedRuntimeUse = false,
    bool OperatorApprovedAiMcpUse = false,
    string? OperatorEvidenceId = null);

public sealed record DofusDbPartnerProvenance(
    string SourceKind,
    string SourceGameVersion,
    bool SourceRightsVerifiedIndependently,
    bool SourceAiMcpAllowed,
    bool SourceRuntimeAllowed,
    bool OperatorApprovedRuntimeUse,
    bool OperatorApprovedAiMcpUse,
    string OperatorEvidenceId,
    string ReviewSummarySha256,
    IReadOnlyDictionary<string, int> SummaryCounts,
    IReadOnlyDictionary<string, int> VerifiedCounts)
{
    public bool GameVersionVerified => false; // capture explicitly records UNKNOWN
    public bool RuntimeCertified => false;
    public bool GameplayCertified => false;
    public bool AiMcpRightsIndependentlyVerified => false;
}

public sealed record DofusDbPartnerItem(
    long Id, string? NameFr, string? NameEn, int? Level, long? ItemSetId);

public sealed record DofusDbPartnerSet(
    long Id, string? NameFr, string? NameEn, IReadOnlyList<long> MemberItemGids);

public sealed record DofusDbPartnerIngredient(long ItemGid, long? Quantity);

/// <summary>ResultQuantity/XP intentionally unknown: not in captured recipe schema.</summary>
public sealed record DofusDbPartnerRecipe(
    long SourceRecipeId, long ResultItemGid, long? ResultQuantity,
    IReadOnlyList<DofusDbPartnerIngredient> Ingredients, long? JobId)
{
    public long? XpPerCraft => null;
    public bool GameVersionVerified => false;
}

public sealed record DofusDbPartnerJob(long Id, string? NameFr, string? NameEn);
public sealed record DofusDbPartnerMonster(
    long Id, string? NameFr, string? NameEn, IReadOnlyList<long> SubareaIds);
public sealed record DofusDbPartnerMapPosition(
    long Id, int X, int Y, long? SubAreaId, long? WorldMapId);

public sealed record DofusDbPartnerLoadResult(
    bool Success, DofusDbPartnerCatalog? Catalog, string Reason)
{
    public static DofusDbPartnerLoadResult Denied(string reason) => new(false, null, reason);
}

/// <summary>
/// Copy-only indexes. No IO, network, runtime client, farming or command surface.
/// </summary>
public sealed class DofusDbPartnerCatalog
{
    private readonly IReadOnlyDictionary<long, DofusDbPartnerItem> _items;
    private readonly IReadOnlyDictionary<long, DofusDbPartnerSet> _sets;
    private readonly IReadOnlyDictionary<long, DofusDbPartnerRecipe> _recipes;
    private readonly IReadOnlyDictionary<long, DofusDbPartnerJob> _jobs;
    private readonly IReadOnlyDictionary<long, DofusDbPartnerMonster> _monsters;
    private readonly IReadOnlyDictionary<long, DofusDbPartnerMapPosition> _positions;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<DofusDbPartnerRecipe>> _recipesByItem;
    private readonly IReadOnlyDictionary<long, IReadOnlyList<DofusDbPartnerSet>> _setsByItem;
    private readonly IReadOnlyDictionary<(int X,int Y), IReadOnlyList<DofusDbPartnerMapPosition>> _positionsByCoordinate;

    public DofusDbPartnerProvenance Provenance { get; }
    public int ItemCount => _items.Count;
    public int SetCount => _sets.Count;
    public int RecipeCount => _recipes.Count;
    public int JobCount => _jobs.Count;
    public int MonsterCount => _monsters.Count;
    public int MapPositionCount => _positions.Count;

    internal DofusDbPartnerCatalog(DofusDbPartnerProvenance provenance,
        IDictionary<long, DofusDbPartnerItem> items,
        IDictionary<long, DofusDbPartnerSet> sets,
        IDictionary<long, DofusDbPartnerRecipe> recipes,
        IDictionary<long, DofusDbPartnerJob> jobs,
        IDictionary<long, DofusDbPartnerMonster> monsters,
        IDictionary<long, DofusDbPartnerMapPosition> positions)
    {
        Provenance = provenance;
        _items = new ReadOnlyDictionary<long, DofusDbPartnerItem>(
            new Dictionary<long, DofusDbPartnerItem>(items));
        _sets = new ReadOnlyDictionary<long, DofusDbPartnerSet>(
            new Dictionary<long, DofusDbPartnerSet>(sets));
        _recipes = new ReadOnlyDictionary<long, DofusDbPartnerRecipe>(
            new Dictionary<long, DofusDbPartnerRecipe>(recipes));
        _jobs = new ReadOnlyDictionary<long, DofusDbPartnerJob>(
            new Dictionary<long, DofusDbPartnerJob>(jobs));
        _monsters = new ReadOnlyDictionary<long, DofusDbPartnerMonster>(
            new Dictionary<long, DofusDbPartnerMonster>(monsters));
        _positions = new ReadOnlyDictionary<long, DofusDbPartnerMapPosition>(
            new Dictionary<long, DofusDbPartnerMapPosition>(positions));
        _recipesByItem = new ReadOnlyDictionary<long, IReadOnlyList<DofusDbPartnerRecipe>>(
            recipes.Values.GroupBy(r => r.ResultItemGid).ToDictionary(
                x => x.Key, x => (IReadOnlyList<DofusDbPartnerRecipe>)Array.AsReadOnly(
                    x.OrderBy(r => r.SourceRecipeId).ToArray())));
        _setsByItem = new ReadOnlyDictionary<long, IReadOnlyList<DofusDbPartnerSet>>(
            sets.Values.SelectMany(s => s.MemberItemGids.Select(i => (ItemId:i,Set:s)))
                .GroupBy(x => x.ItemId).ToDictionary(x => x.Key,
                    x => (IReadOnlyList<DofusDbPartnerSet>)Array.AsReadOnly(
                        x.Select(y => y.Set).OrderBy(s => s.Id).ToArray())));
        _positionsByCoordinate =
            new ReadOnlyDictionary<(int,int),IReadOnlyList<DofusDbPartnerMapPosition>>(
                positions.Values.GroupBy(p => (p.X, p.Y)).ToDictionary(x => x.Key,
                    x => (IReadOnlyList<DofusDbPartnerMapPosition>)Array.AsReadOnly(
                        x.OrderBy(p => p.Id).ToArray())));
    }

    public DofusDbPartnerItem? GetItem(long id) => _items.GetValueOrDefault(id);
    public DofusDbPartnerSet? GetSet(long id) => _sets.GetValueOrDefault(id);
    public DofusDbPartnerJob? GetJob(long id) => _jobs.GetValueOrDefault(id);
    public DofusDbPartnerMonster? GetMonster(long id) => _monsters.GetValueOrDefault(id);
    public DofusDbPartnerMapPosition? GetMapPosition(long id) => _positions.GetValueOrDefault(id);

    public IReadOnlyList<DofusDbPartnerItem> SearchItems(string? query = null, int limit = 30, int offset = 0) =>
        Page(_items.Values, query, limit, offset, r => r.Id, r => r.NameFr, r => r.NameEn);
    public IReadOnlyList<DofusDbPartnerSet> SearchSets(string? query = null, int limit = 30, int offset = 0) =>
        Page(_sets.Values, query, limit, offset, r => r.Id, r => r.NameFr, r => r.NameEn);
    public IReadOnlyList<DofusDbPartnerMonster> SearchMonsters(string? query = null, int limit = 30, int offset = 0) =>
        Page(_monsters.Values, query, limit, offset, r => r.Id, r => r.NameFr, r => r.NameEn);

    public IReadOnlyList<DofusDbPartnerSet> GetSetsContaining(long itemGid) =>
        _setsByItem.TryGetValue(itemGid, out var list) ? list : Array.Empty<DofusDbPartnerSet>();
    public IReadOnlyList<DofusDbPartnerRecipe> GetRecipesForItem(long resultItemGid) =>
        _recipesByItem.TryGetValue(resultItemGid, out var list) ? list : Array.Empty<DofusDbPartnerRecipe>();
    public IReadOnlyList<DofusDbPartnerMapPosition> FindMapPositions(
        int x, int y, long? worldMapId = null, int limit = 50)
    {
        ValidatePage(limit, 0);
        return _positionsByCoordinate.TryGetValue((x,y), out var list)
            ? Array.AsReadOnly(list.Where(p => worldMapId is null || p.WorldMapId == worldMapId)
                .Take(limit).ToArray())
            : Array.Empty<DofusDbPartnerMapPosition>();
    }

    private static IReadOnlyList<T> Page<T>(IEnumerable<T> records, string? query,
        int limit, int offset, Func<T,long> id, Func<T,string?> fr, Func<T,string?> en)
    {
        ValidatePage(limit, offset);
        if (query is { Length: > 100 } || query?.Any(char.IsControl) == true)
            throw new ArgumentException("Unsafe search query.", nameof(query));
        var q = (query ?? "").Trim();
        return Array.AsReadOnly(records.Where(row => q.Length == 0 ||
            (fr(row)?.Contains(q, StringComparison.OrdinalIgnoreCase) == true ||
             en(row)?.Contains(q, StringComparison.OrdinalIgnoreCase) == true ||
             id(row).ToString(System.Globalization.CultureInfo.InvariantCulture) == q))
            .OrderBy(id).Skip(offset).Take(limit).ToArray());
    }

    private static void ValidatePage(int limit, int offset)
    {
        if (limit is < 1 or > 100 || offset is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
