namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    /// <summary>Niveau d'un combattant du combat en cours (relevé à l'entrée en combat) ; null s'il n'est pas connu.</summary>
    public int? FighterLevel(uint pid, long actorId)
    {
        var st = ChallengeState(pid);
        lock (st) return st.Levels.TryGetValue(actorId, out var l) ? l : null;
    }
}
