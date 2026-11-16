namespace GivreClone.Engine;

/// <summary>A visible client's automatic AFK finish must not end a turn owned by automation.
/// Explicit/manual finishes and unknown payloads always pass through.</summary>
public static class ClientAutoPassPolicy
{
    public static bool Suppress(bool socket, bool inFight, bool turnOpen,
        bool automationOwnsTurn, bool fightPermission, bool? isAfk) =>
        !socket && inFight && turnOpen && automationOwnsTurn && fightPermission && isAfk == true;
}
