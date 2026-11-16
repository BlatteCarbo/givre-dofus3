using System.Collections.Immutable;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

public sealed record ObservedFightMark(int Id, long Author, int Type, ImmutableArray<int> Cells, bool Active);

/// <summary>Server-confirmed marks only. A trigger is not evidence that a mark was removed.</summary>
public sealed class FightMarks
{
    private ImmutableDictionary<int, ObservedFightMark> _marks = ImmutableDictionary<int, ObservedFightMark>.Empty;
    public ImmutableDictionary<int, ObservedFightMark> Snapshot => _marks;

    public void Observe(IReadOnlyList<ProtoField> payload)
    {
        if (MapFightReader.ReadMark(payload) is { } placed)
        {
            var mark = new ObservedFightMark(placed.Id, placed.Author, placed.Type, placed.Cells.ToImmutableArray(), placed.Active);
            ImmutableInterlocked.AddOrUpdate(ref _marks, mark.Id, mark, (_, _) => mark);
        }
        else if (MapFightReader.ReadUnmark(payload) is { } removed)
            ImmutableInterlocked.TryRemove(ref _marks, removed, out _);
        else if (MapFightReader.ReadMarkActivation(payload) is { } activation)
        {
            // Unknown activation cannot invent an owner, cell list or mark type.
            while (true)
            {
                var before = Snapshot;
                if (!before.TryGetValue(activation.Id, out var mark)) return;
                var after = before.SetItem(activation.Id, mark with { Active = activation.Active });
                if (ReferenceEquals(Interlocked.CompareExchange(ref _marks, after, before), before)) return;
            }
        }
    }
}
