namespace GivreClone.Engine;

/// <summary>Failed groups yield to other candidates until they can reasonably have moved.</summary>
public sealed class EngagementCooldown
{
    private long _map = -1;
    private readonly Dictionary<long, long> _until = new();
    public HashSet<long> Excluded(long map, long now)
    {
        if (map != _map) { _until.Clear(); _map = map; }
        foreach (long id in _until.Where(x => x.Value <= now).Select(x => x.Key).ToArray()) _until.Remove(id);
        return _until.Keys.ToHashSet();
    }
    public void Failed(long map, long actor, long now)
    {
        Excluded(map, now);
        _until[actor] = now + 60_000;
    }
}
