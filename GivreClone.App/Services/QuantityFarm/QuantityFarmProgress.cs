using System.Globalization;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.QuantityFarm;

/// <summary>Ligne locale du script de farm, même contrat que le parseur de trajet : pas un ACK serveur.</summary>
public readonly record struct QuantityFarmProgress(long Gid, long Owned, long Target, long Remaining)
{
    public const int MaxLength = 100;
    private static readonly Regex Pattern = new(@"^\[quantity-farm\] gid=(\d{1,10}) owned=(\d{1,10}) target=(\d{1,10}) remaining=(\d{1,10})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string FormatLine(long gid, long owned, long target)
    {
        long remaining = target > owned ? target - owned : 0;
        return string.Format(CultureInfo.InvariantCulture, "[quantity-farm] gid={0} owned={1} target={2} remaining={3}", gid, owned, target, remaining);
    }

    public static bool TryParse(string? message, out QuantityFarmProgress progress)
    {
        progress = default;
        if (message is not { Length: > 0 and <= MaxLength }) return false;
        var match = Pattern.Match(message);
        if (!match.Success
            || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long gid)
            || !long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long owned)
            || !long.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long target)
            || !long.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long remaining))
            return false;
        if (gid <= 0 || target < 1 || target > 1_000_000_000L || owned < 0) return false;
        long expected = target > owned ? target - owned : 0;
        if (remaining != expected) return false;
        progress = new(gid, owned, target, remaining);
        return true;
    }
}

/// <summary>Dernière ligne de farm par personnage, lue par l'expédition et l'atelier éco.</summary>
public static class QuantityFarmBoard
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, QuantityFarmProgress> ByPid = new();
    public static event Action<int>? Changed;

    public static void Note(int pid, QuantityFarmProgress progress)
    {
        Action<int>? changed;
        lock (Gate)
        {
            ByPid[pid] = progress;
            changed = Changed;
        }
        changed?.Invoke(pid);
    }

    public static bool TryGet(int pid, out QuantityFarmProgress progress)
    {
        lock (Gate) return ByPid.TryGetValue(pid, out progress);
    }

    public static void Clear(int pid)
    {
        Action<int>? changed;
        lock (Gate)
        {
            if (!ByPid.Remove(pid)) return;
            changed = Changed;
        }
        changed?.Invoke(pid);
    }

    public static string Format(QuantityFarmProgress progress)
        => string.Format(CultureInfo.InvariantCulture, "Possédé {0} · reste {1} · objectif {2}", progress.Owned, progress.Remaining, progress.Target);
}
