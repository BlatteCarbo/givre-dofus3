using System.Collections.Generic;

namespace GivreClone.App.Services.Social;

/// <summary>Noms locaux (dofus.sqlite). Aucune requête réseau.</summary>
public interface ISocialNames
{
    string? Breed(int id);
    string? Job(int id);
    IReadOnlyList<int> JobIds();
    string? Title(int id);
    string? Ornament(int id);
    string? AlignmentRank(int rankId);
    string? AlignmentSideOfRank(int rankId);
    string? ItemName(long gid);
    int ItemLevel(long gid);
    string? ItemType(long gid);
    string? ItemSet(long gid);
    int ItemWeight(long gid);
    string? ItemDescription(long gid);
    string? Monster(int id);
    long? MonsterXp(int monsterId, int level);
}

public sealed class EmptySocialNames : ISocialNames
{
    public static readonly EmptySocialNames Instance = new();

    public string? Breed(int id) => null;
    public string? Job(int id) => null;
    public IReadOnlyList<int> JobIds() => System.Array.Empty<int>();
    public string? Title(int id) => null;
    public string? Ornament(int id) => null;
    public string? AlignmentRank(int rankId) => null;
    public string? AlignmentSideOfRank(int rankId) => null;
    public string? ItemName(long gid) => null;
    public int ItemLevel(long gid) => -1;
    public string? ItemType(long gid) => null;
    public string? ItemSet(long gid) => null;
    public int ItemWeight(long gid) => -1;
    public string? ItemDescription(long gid) => null;
    public string? Monster(int id) => null;
    public long? MonsterXp(int monsterId, int level) => null;
}
