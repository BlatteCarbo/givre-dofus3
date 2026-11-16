namespace GivreClone.Engine.Recovery;

/// <summary>Cash accounting: acquired runes are not charged again when consumed.</summary>
public readonly record struct SmithSessionBudget(long Initial, long Limit)
{
    public long Floor => Math.Max(0, Initial - Math.Max(0, Limit));
    public long Spent(long current) => Math.Max(0, Initial - current);
    public long Remaining(long current) => Math.Max(0, Math.Min(Math.Max(0, Limit), current - Floor));
    public long PurchaseBudget(long current, long returnReserve) => Math.Max(0, Remaining(current) - Math.Max(0, returnReserve));
}
