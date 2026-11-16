using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Statuts d'un bot exposés aux scripts (portail : « table bot »).</summary>
public static class BotStatus
{
    public const string Offline = "offline", Connecting = "connecting", InGame = "ingame", Moving = "moving",
                        Gathering = "gathering", Fight = "fight", Dead = "dead", Disconnected = "disconnected";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Offline, Connecting, InGame, Moving, Gathering, Fight, Dead, Disconnected };

    /// <summary>Statuts où le personnage est dans le monde (map chargée).</summary>
    public static bool IsInWorld(string s) => s is InGame or Moving or Gathering or Fight or Dead;
}

/// <summary>
/// Instantané d'un bot, tel que décrit par la « table bot » du portail : alias du compte (jamais l'identifiant de connexion),
/// perso, pid (0 hors ligne), mode, statut, position, niveau, kamas (-1 inconnu), pods, vie, script, équipe, etc.
/// </summary>
public sealed record BotSnapshot(
    string Account, string Name, int Pid, string Mode, string Status,
    long MapId = 0, string Coords = "", int Level = 0, long Kamas = -1,
    int Pods = 0, int PodsMax = 0, int Life = 0, int MaxLife = 0, bool InFight = false,
    string Script = "", bool ScriptRunning = false, string Team = "", bool Leader = false,
    string LastStop = "", string Disconnect = "", int SinceSec = 0)
{
    public bool InGame => BotStatus.IsInWorld(Status);
    public bool Online => InGame || Status is BotStatus.Connecting or BotStatus.Disconnected;
    public int PodsPercent => PodsMax > 0 ? (int)Math.Clamp(Pods * 100L / PodsMax, 0, 100) : 0;
}

/// <summary>Événement d'un bot (onBotEvent) : nom + informations facultatives (clés texte, valeurs simples).</summary>
public sealed record BotEvent(string Name, IReadOnlyDictionary<string, object?>? Info = null)
{
    public static BotEvent Of(string name, params (string Key, object? Value)[] info)
        => new(name, info.Length == 0 ? null : info.ToDictionary(i => i.Key, i => i.Value));
}

public static class BotEvents
{
    public const string Connected = "connected", Disconnected = "disconnected", Offline = "offline", Crashed = "crashed",
                        LevelUp = "levelUp", Death = "death", PodsFull = "podsFull", ScriptStarted = "scriptStarted",
                        ScriptStopped = "scriptStopped", Moderator = "moderator", CharacterCreated = "characterCreated",
                        CharacterRefused = "characterRefused", LoginWaiting = "loginWaiting";

    /// <summary>Seuil de l'évènement podsFull (passage à 90 %).</summary>
    public const int PodsFullPercent = 90;
}

/// <summary>
/// Déduit des évènements de la différence entre deux instantanés successifs du MÊME bot. Ne couvre que ce qui est déductible :
/// crashed, moderator, characterCreated/Refused et loginWaiting viennent de sources explicites (le client, le moteur) et sont
/// levés par l'exécuteur, pas ici.
/// </summary>
public static class BotEventDiff
{
    public static IReadOnlyList<BotEvent> Diff(BotSnapshot? prev, BotSnapshot cur)
    {
        var ev = new List<BotEvent>();
        bool wasWorld = prev is not null && BotStatus.IsInWorld(prev.Status);
        bool nowWorld = BotStatus.IsInWorld(cur.Status);

        if (!wasWorld && nowWorld) ev.Add(BotEvent.Of(BotEvents.Connected));

        if (prev is not null && prev.Status != cur.Status)
        {
            if (cur.Status == BotStatus.Disconnected && prev.Status != BotStatus.Disconnected)
                ev.Add(BotEvent.Of(BotEvents.Disconnected, ("reason", Reason(cur.Disconnect))));
            else if (cur.Status == BotStatus.Offline && prev.Status != BotStatus.Offline)
                ev.Add(BotEvent.Of(BotEvents.Offline, ("reason", Reason(cur.Disconnect, cur.LastStop))));
            if (cur.Status == BotStatus.Dead && prev.Status != BotStatus.Dead) ev.Add(BotEvent.Of(BotEvents.Death));
        }

        if (prev is not null && wasWorld && nowWorld && cur.Level > prev.Level && prev.Level > 0)
            ev.Add(BotEvent.Of(BotEvents.LevelUp, ("level", cur.Level), ("from", prev.Level)));

        if (prev is not null && nowWorld && cur.PodsMax > 0 && cur.PodsPercent >= BotEvents.PodsFullPercent && prev.PodsPercent < BotEvents.PodsFullPercent)
            ev.Add(BotEvent.Of(BotEvents.PodsFull, ("podsPercent", cur.PodsPercent)));

        if (prev is not null && !prev.ScriptRunning && cur.ScriptRunning)
            ev.Add(BotEvent.Of(BotEvents.ScriptStarted, ("script", cur.Script)));
        if (prev is not null && prev.ScriptRunning && !cur.ScriptRunning)
            ev.Add(BotEvent.Of(BotEvents.ScriptStopped, ("script", prev.Script), ("reason", string.IsNullOrEmpty(cur.LastStop) ? "ended" : cur.LastStop)));
        return ev;
    }

    private static string Reason(params string[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "stopped";
}

/// <summary>Retrouve un bot « par alias du compte, nom du perso ou pid » (portail), casse ignorée.</summary>
public static class BotMatcher
{
    public static BotSnapshot? Find(IEnumerable<BotSnapshot> all, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var list = all as IReadOnlyList<BotSnapshot> ?? all.ToList();
        var k = key.Trim();
        if (int.TryParse(k, out var pid) && pid > 0 && list.FirstOrDefault(b => b.Pid == pid) is { } byPid) return byPid;
        return list.FirstOrDefault(b => b.Account.Equals(k, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(b => b.Name.Equals(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Le compte (alias, perso connu) correspond-il au bot trouvé ? Un nom de perso VIDE (bot hors ligne) ne correspond
    /// jamais : sinon "" == "" désignait le premier compte sans perso connu, et connect_bot lançait un autre compte.</summary>
    public static bool SameAccount(BotSnapshot snap, string alias, string? character)
        => alias.Equals(snap.Account, StringComparison.OrdinalIgnoreCase)
           || snap.Name.Length > 0 && (character ?? "").Equals(snap.Name, StringComparison.OrdinalIgnoreCase);
}
