using System;

namespace GivreClone.App.Services.Supervisor;

/// <summary>
/// Point d'accès unique, côté application, au bus de messages bot ↔ superviseur. Les hôtes Lua des bots (module <c>supervisor:</c>) y
/// publient ; les superviseurs y reçoivent. <see cref="SnapshotFor"/> est branché par l'application pour décrire l'expéditeur.
/// </summary>
public static class SupervisorHub
{
    public static SupervisorBus Bus { get; } = new();

    /// <summary>Instantané du bot de ce pid (null si inconnu) : joint aux messages envoyés par <c>supervisor:send</c>.</summary>
    public static Func<int, BotSnapshot?> SnapshotFor { get; set; } = _ => null;
}
