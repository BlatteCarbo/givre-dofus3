using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// Journal de connexion dans la console Lua du bot : chaque étape (proxy, IP, Ankama, serveurs, bascule sur le proxy de jeu,
// personnage, arrivée en jeu) en une ligne colorée, catégorie « connexion ». Une étape émise avant que le bot existe
// (authentification d'un lancement classique) attend son bot, au plus 3 minutes.
public partial class BotsViewModel
{
    private sealed record PendingStep(string Message, LuaLogLevel Level, DateTime At);
    private readonly Dictionary<string, List<PendingStep>> _journalByAccount = new();
    private readonly Dictionary<int, List<PendingStep>> _journalByPid = new();
    private readonly HashSet<int> _welcomed = new();   // pids déjà annoncés « en jeu » (une fois par session)
    private static readonly TimeSpan JournalKeep = TimeSpan.FromMinutes(3);
    private const string JournalCategory = "connexion";

    /// <summary>Onglet Console : journal lisible (défaut) ou flot technique. Mémorisé.</summary>
    public bool ConsoleTechnical
    {
        get => AppConfig.Current.ConsoleTechnical;
        set
        {
            if (value) ConsoleChat = false;   // un clic sur « Technique » quitte la vue Discussion même si Technique était déjà le mode mémorisé
            if (AppConfig.Current.ConsoleTechnical == value) return;
            AppConfig.Current.ConsoleTechnical = value;
            AppConfig.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConsoleJournal));
            OnPropertyChanged(nameof(ShowJournalList)); OnPropertyChanged(nameof(ShowTechList));
        }
    }

    private bool _consoleChat;
    /// <summary>Troisième vue de la Console : la discussion (MP, canaux) à côté du journal et du flot technique. Non mémorisée.</summary>
    public bool ConsoleChat
    {
        get => _consoleChat;
        set
        {
            if (_consoleChat == value) return;
            _consoleChat = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(ShowJournalList)); OnPropertyChanged(nameof(ShowTechList));
            if (value) ReloadChat();
        }
    }
    public bool ShowJournalList => ConsoleJournal && !ConsoleChat;
    public bool ShowTechList => ConsoleTechnical && !ConsoleChat;
    // Liée en deux sens au bouton « Journal » : seul un clic qui le coche compte (le décochage suit l'autre bouton).
    public bool ConsoleJournal { get => !ConsoleTechnical; set { if (value) { ConsoleChat = false; ConsoleTechnical = false; } } }

    private void OnJournal(string? accountId, int pid, string message, LuaLogLevel level)
    {
        if (pid > 0 && _byPid.ContainsKey(pid)) { AppendLuaLine(JournalCategory, message, level, pid); return; }
        // Compte déjà affiché (relance en place : même entrée Bots) -> directement sur son bot.
        if (pid <= 0 && accountId is not null && _byPid.Values.FirstOrDefault(b => b.Row.Model.Id == accountId) is { } bot)
        {
            AppendLuaLine(JournalCategory, message, level, bot.Pid);
            return;
        }
        var step = new PendingStep(message, level, DateTime.UtcNow);
        if (pid > 0) Add(_journalByPid, pid, step);
        else if (accountId is not null) Add(_journalByAccount, accountId, step);

        static void Add<TKey>(Dictionary<TKey, List<PendingStep>> d, TKey k, PendingStep s) where TKey : notnull
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<PendingStep>();
            if (l.Count < 40) l.Add(s);
        }
    }

    private readonly Dictionary<int, string> _journalCharacter = new();   // dernier perso annoncé par pid (compte à 1 perso : signalé 2 fois)

    private void OnJournalCharacter(int pid, CharacterInfo info)
    {
        if (string.IsNullOrEmpty(info.Name)) return;
        if (_journalCharacter.TryGetValue(pid, out var last) && last == info.Name) return;
        _journalCharacter[pid] = info.Name;
        OnJournal(null, pid, info.Level is int lv and > 0 ? Loc.Fmt("L.Journal.Character", info.Name, lv) : Loc.Fmt("L.Journal.CharacterNoLevel", info.Name),
                  LuaLogLevel.Info);
    }

    private void DriveConnectionJournal()
    {
        var now = DateTime.UtcNow;
        foreach (var bot in _byPid.Values)
        {
            int pid = bot.Pid;
            if (pid <= 0) continue;
            // Étapes en attente de ce bot : celles de son compte (avant le pid) puis celles de son pid, dans l'ordre.
            if (_journalByAccount.Remove(bot.Row.Model.Id, out var byAcc))
                foreach (var s in byAcc) AppendLuaLine(JournalCategory, s.Message, s.Level, pid);
            if (_journalByPid.Remove(pid, out var byPid))
                foreach (var s in byPid) AppendLuaLine(JournalCategory, s.Message, s.Level, pid);
            // Arrivée en jeu (1re carte) : une fois par session.
            if (!_welcomed.Contains(pid) && Stores.Launch.CurrentMap((uint)pid) is not null)
            {
                _welcomed.Add(pid);
                string server = bot.Row.Server is { Length: > 0 } sv && sv != "-" ? sv : "";
                AppendLuaLine(JournalCategory, server.Length > 0 ? Loc.Fmt("L.Journal.WelcomeOn", server) : Loc.T("L.Journal.Welcome"),
                              LuaLogLevel.Success, pid);
            }
        }
        // Ménage : étapes orphelines (lancement abandonné) et sessions terminées.
        foreach (var k in _journalByAccount.Where(kv => now - kv.Value[^1].At > JournalKeep).Select(kv => kv.Key).ToList()) _journalByAccount.Remove(k);
        foreach (var k in _journalByPid.Where(kv => now - kv.Value[^1].At > JournalKeep).Select(kv => kv.Key).ToList()) _journalByPid.Remove(k);
        if (_welcomed.Count > 0) _welcomed.RemoveWhere(p => !_byPid.ContainsKey(p));
        ForgetGameJournal();
        foreach (var k in _journalCharacter.Keys.Where(p => !_byPid.ContainsKey(p) && !_journalByPid.ContainsKey(p)).ToList()) _journalCharacter.Remove(k);
    }
}
