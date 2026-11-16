using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.EquipmentSetData;

/// <summary>
/// A local hash authenticates only the inspected bytes. Labels for source and permission
/// are declarations from a caller-supplied manifest; they are not publisher attestations.
/// </summary>
public sealed record EquipmentSetSource(
    string GameVersion, string SourceLabel, string PermissionClaim,
    string FileSha256, bool LocalHashVerified, bool PublisherVerified = false)
{
    /// <summary>AI/MCP distribution is not authorized by this offline reader.</summary>
    public bool AiDisclosureAuthorized => false;
}

/// <param name="Name">Null when source lacks a defensible translation.</param>
/// <param name="SetId">Null when the item has no known equipment set.</param>
public sealed record EquipmentSetItem(long Id, string? Name, long? SetId);

/// <param name="Name">Null when source lacks a defensible translation.</param>
/// <param name="MemberItemIds">Exact source-reported members; never reconstructed by theme or search.</param>
public sealed record EquipmentSet(long Id, string? Name, IReadOnlyList<long> MemberItemIds);

public sealed class EquipmentSetCatalog
{
    private readonly IReadOnlyDictionary<long, EquipmentSet> _setsById;
    private readonly IReadOnlyDictionary<long, EquipmentSetItem> _itemsById;

    public EquipmentSetSource Source { get; }
    public IReadOnlyList<EquipmentSet> Sets { get; }
    public IReadOnlyList<EquipmentSetItem> Items { get; }
    public bool Admitted { get; }
    public IReadOnlyList<string> Issues { get; }

    internal EquipmentSetCatalog(EquipmentSetSource source,
        IReadOnlyList<EquipmentSet> sets, IReadOnlyList<EquipmentSetItem> items,
        bool admitted, IReadOnlyList<string> issues)
    {
        Source = source;
        Sets = Array.AsReadOnly(sets.OrderBy(s => s.Id).ToArray());
        Items = Array.AsReadOnly(items.OrderBy(i => i.Id).ToArray());
        Admitted = admitted;
        Issues = Array.AsReadOnly(issues.ToArray());
        _setsById = Sets.ToDictionary(s => s.Id);
        _itemsById = Items.ToDictionary(i => i.Id);
    }

    public EquipmentSet? FindSet(long id) =>
        Admitted && id > 0 && _setsById.TryGetValue(id, out var set) ? set : null;

    public EquipmentSetItem? FindItem(long id) =>
        Admitted && id > 0 && _itemsById.TryGetValue(id, out var item) ? item : null;

    public IReadOnlyList<EquipmentSetItem> ItemsOfSet(long setId)
    {
        if (FindSet(setId) is not { } set) return Array.Empty<EquipmentSetItem>();
        return set.MemberItemIds.Select(id => _itemsById[id]).ToArray();
    }

    /// <summary>Returns ALL matching named sets in deterministic ID order; never guesses a unique set.</summary>
    public IReadOnlyList<EquipmentSet> FindSets(string? query, int maxResults = 50)
    {
        if (!Admitted || string.IsNullOrWhiteSpace(query) || maxResults is < 1 or > 100)
            return Array.Empty<EquipmentSet>();
        string term = query.Trim();
        if (term.Length > 128) return Array.Empty<EquipmentSet>();
        return Sets.Where(s => s.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
            .Take(maxResults).ToArray();
    }

    /// <summary>Returns ALL matching named items, including pieces belonging to multiple query matches.</summary>
    public IReadOnlyList<EquipmentSetItem> FindItems(string? query, int maxResults = 50)
    {
        if (!Admitted || string.IsNullOrWhiteSpace(query) || maxResults is < 1 or > 100)
            return Array.Empty<EquipmentSetItem>();
        string term = query.Trim();
        if (term.Length > 128) return Array.Empty<EquipmentSetItem>();
        return Items.Where(i => i.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
            .Take(maxResults).ToArray();
    }

    internal static EquipmentSetCatalog Rejected(string reason) =>
        new(new EquipmentSetSource("", "", "", "", false), [], [], false, [reason]);
}
