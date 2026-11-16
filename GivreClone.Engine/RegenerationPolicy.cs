namespace GivreClone.Engine;

public sealed record RegenerationOptions(bool Enabled = false, int StartPercent = 60,
    int StopPercent = 100, int FoodGid = 0, bool WaitForTeam = false)
{
    public int Start => Math.Clamp(StartPercent, 1, 100);
    public int Stop => Math.Clamp(StopPercent, Start, 100);
}

public enum RegenerationDecision { Ready, Regenerate, WaitForLife }

/// <summary>Only server-observed life is accepted. Unknown online members block;
/// offline or missing roster members are excluded by the caller, never treated as healed.</summary>
public static class RegenerationPolicy
{
    public static bool MemberReady(RegenerationOptions options, bool online, bool sameMap,
        bool inFight, bool alive, int? authoritativePercent, bool recovering = false) =>
        !options.Enabled || !options.WaitForTeam || !online
        || (sameMap && !inFight && alive
            && Decide(options, authoritativePercent, recovering) == RegenerationDecision.Ready);

    public static RegenerationDecision Decide(RegenerationOptions options, int? authoritativePercent,
        bool recovering = false)
    {
        if (!options.Enabled) return RegenerationDecision.Ready;
        if (authoritativePercent is null or < 0 or > 100) return RegenerationDecision.WaitForLife;
        return authoritativePercent < (recovering ? options.Stop : options.Start)
            ? RegenerationDecision.Regenerate : RegenerationDecision.Ready;
    }
}
