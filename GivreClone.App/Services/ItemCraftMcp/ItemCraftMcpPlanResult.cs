namespace GivreClone.App.Services.ItemCraftMcp;

/// <summary>
/// Source-approved projection of one computation by ItemCraftPlanner.
/// Unknown quantities remain nullable; the MCP facade never recalculates them.
/// </summary>
public sealed record ItemCraftMcpMaterial(
    long ItemGid, long? Required, long? Owned, long? Missing,
    string Method, bool Verified, string? SourceId);

public sealed record ItemCraftMcpStep(
    int StepId, long ItemGid, long? Quantity, string Method,
    long? RecipeId, IReadOnlyList<int> Dependencies,
    bool Verified, string? SourceId, string? LocalRecipeKey = null);

public sealed record ItemCraftMcpPlanResult(
    ItemCraftMcpTargetKind Kind, long TargetId, long Quantity,
    bool IsComplete,
    IReadOnlyList<ItemCraftMcpMaterial> Materials,
    IReadOnlyList<ItemCraftMcpStep> Steps,
    IReadOnlyList<string> Warnings,
    ItemCraftMcpEvidence Evidence,
    bool InventoryVerified = false,
    bool RuntimeCertified = false);
