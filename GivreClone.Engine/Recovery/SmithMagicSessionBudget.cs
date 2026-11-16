namespace GivreClone.Engine.Recovery;

/// <summary>Reservations survive workshop retries and apply across all objects in one session.
/// A reservation is conservative: an unconfirmed application is never made available again.</summary>
public sealed class SmithMagicSessionBudget
{
    private readonly Dictionary<(long Item, int Effect), int> _lines = new();
    public int UsedTotal { get; private set; }
    public long Spent { get; private set; }
    public SmithMagicDecision ReserveNext(long item, SmithTarget target, IEnumerable<SmithRune> runes, SmithMagicLimits limits)
    {
        if (item <= 0) return new(false, "Objet invalide.");
        var key = (item, target.EffectId);
        var decision = SmithMagicPlanner.Next(target, runes, limits, UsedTotal, _lines.GetValueOrDefault(key), Spent);
        if (decision.Ready && decision.Rune is { } rune)
        {
            UsedTotal++;
            _lines[key] = _lines.GetValueOrDefault(key) + 1;
            Spent += rune.UnitPrice; // planner checked remaining budget without overflow
        }
        return decision;
    }
}
