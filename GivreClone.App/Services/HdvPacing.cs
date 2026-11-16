namespace GivreClone.App.Services;

/// <summary>Cadence des opérations HDV. Le réglage de l'utilisateur fait foi, 0 compris : aucun plancher caché.</summary>
public static class HdvPacing
{
    public const int MaxDelayMs = 3_600_000;

    public static (int Min, int Max) Resolve(int minMs, int maxMs)
    {
        int min = System.Math.Clamp(minMs, 0, MaxDelayMs);
        int max = System.Math.Clamp(maxMs, min, MaxDelayMs);
        return (min, max);
    }
}
