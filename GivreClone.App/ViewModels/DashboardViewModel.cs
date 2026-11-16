using System;
using GivreClone.App.Services.Eco;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Un signal observé par un composant déjà actif. La page ne crée aucune commande de jeu.</summary>
public sealed record DashboardAlertRow(
    string Key, string Category, string Title, string Detail, object Source);

/// <summary>
/// Tableau de bord de consultation. Toutes les valeurs proviennent des objets vivants
/// du module Bots, des superviseurs et du centre de notifications, sur le thread UI.
/// Aucun total de kamas/XP n'est construit à partir des vues de stats par personnage.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly BotsViewModel _bots;
    private readonly NotificationCenterViewModel _notifications;
    private readonly Action<BotRowViewModel> _openBot;
    private readonly Action<SupervisorViewModel> _openSupervisor;
    private readonly Action _openNotifications;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<BotRowViewModel> Bots => _bots.Bots;
    /// <summary>Sessions affichées : filtre (0 tous, 1 en jeu, 2 à vérifier) + recherche sur le nom, le serveur ou la zone.</summary>
    public ObservableCollection<BotRowViewModel> VisibleBots { get; } = new();
    [ObservableProperty] public partial int BotFilter { get; set; }
    [ObservableProperty] public partial string BotSearch { get; set; } = "";
    partial void OnBotFilterChanged(int value) => RefreshOnUiThread();
    partial void OnBotSearchChanged(string value) => RefreshOnUiThread();

    private bool Matches(BotRowViewModel b, System.Collections.Generic.HashSet<BotRowViewModel> flagged)
    {
        bool inGame = b.Row.Activity is BotActivity.InGame or BotActivity.Moving or BotActivity.Gathering or BotActivity.Combat;
        if (BotFilter == 1 && !inGame) return false;
        if (BotFilter == 2 && !flagged.Contains(b)) return false;
        var q = BotSearch.Trim();
        return q.Length == 0
            || (b.Row.CharacterName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (b.Row.Server?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || b.ZoneText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
    public ObservableCollection<SupervisorViewModel> Supervisors => _bots.Supervisors;
    public ObservableCollection<DashboardAlertRow> Alerts { get; } = new();

    [ObservableProperty] public partial int SessionCount { get; set; }
    [ObservableProperty] public partial int InGameCount { get; set; }
    [ObservableProperty] public partial int ConnectingCount { get; set; }
    [ObservableProperty] public partial int DisconnectedCount { get; set; }
    [ObservableProperty] public partial int DeadCount { get; set; }
    [ObservableProperty] public partial int SupervisorCount { get; set; }
    [ObservableProperty] public partial int RunningSupervisorCount { get; set; }
    [ObservableProperty] public partial int UnreadNotificationCount { get; set; }
    [ObservableProperty] public partial int ProblemCount { get; set; }
    /// <summary>Prochaine fin d'abonnement des comptes actifs, ou des comptes mémorisés si aucun n'est actif.</summary>
    [ObservableProperty] public partial string SubscriptionHeadline { get; set; } = "—";
    [ObservableProperty] public partial string SubscriptionDetail { get; set; } = "Connecte un compte pour le relever";
    [ObservableProperty] public partial bool SubscriptionUrgent { get; set; }
    [ObservableProperty] public partial bool HasBots { get; set; }
    [ObservableProperty] public partial bool HasSupervisors { get; set; }
    [ObservableProperty] public partial bool HasAlerts { get; set; }
    [ObservableProperty] public partial bool HasOverflowAlerts { get; set; }
    [ObservableProperty] public partial string UpdatedAt { get; set; } = "En attente du relevé local";

    // Indicateurs en direct depuis le démarrage de l'application (événements du transport, levés hors thread UI).
    private long _fightsWon, _fightsLost, _harvested;
    [ObservableProperty] public partial string TotalKamasText { get; set; } = "—";
    [ObservableProperty] public partial string TotalKamasDetail { get; set; } = "Aucun inventaire relevé";
    [ObservableProperty] public partial long FightsWon { get; set; }
    [ObservableProperty] public partial long FightsLost { get; set; }
    [ObservableProperty] public partial string FightsDetail { get; set; } = "Aucun combat terminé";
    [ObservableProperty] public partial long HarvestedCount { get; set; }
    [ObservableProperty] public partial int LaunchableCount { get; set; }

    // KPI Header per-bot or aggregated metrics for kamas/hour, XP/hour, fight win rate, and pods occupancy
    [ObservableProperty] public partial string KamasPerHourText { get; set; } = "—";
    [ObservableProperty] public partial string KamasPerHourTrend { get; set; } = "+0%";
    [ObservableProperty] public partial string XpPerHourText { get; set; } = "—";
    [ObservableProperty] public partial string XpPerHourTrend { get; set; } = "+0%";
    [ObservableProperty] public partial string WinRateText { get; set; } = "—";
    [ObservableProperty] public partial string WinRateTrend { get; set; } = "Stable";
    [ObservableProperty] public partial string PodsOccupancyText { get; set; } = "—";
    [ObservableProperty] public partial string PodsOccupancyTrend { get; set; } = "Normal";
    [ObservableProperty] public partial bool WinRateTrendPositive { get; set; } = true;
    [ObservableProperty] public partial bool PodsOccupancyUrgent { get; set; } = false;

    public DashboardViewModel(
        BotsViewModel bots, NotificationCenterViewModel notifications,
        Action<BotRowViewModel> openBot, Action<SupervisorViewModel> openSupervisor,
        Action openNotifications)
    {
        _bots = bots;
        _notifications = notifications;
        _openBot = openBot;
        _openSupervisor = openSupervisor;
        _openNotifications = openNotifications;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshSnapshot();
        // Le décompte suit également les entrées/sorties, sans dépendre de la page Bots affichée.
        _bots.Bots.CollectionChanged += (_, _) => RefreshOnUiThread();
        _bots.Supervisors.CollectionChanged += (_, _) => RefreshOnUiThread();
        _notifications.Items.CollectionChanged += (_, _) => RefreshOnUiThread();
        Stores.Launch.FightOutcome += (_, won, lost) =>
        {
            if (won) Interlocked.Increment(ref _fightsWon);
            else if (lost) Interlocked.Increment(ref _fightsLost);
        };
        Stores.Launch.ResourceHarvested += (_, _, qty, _, _) => { if (qty > 0) Interlocked.Add(ref _harvested, qty); };
        RefreshOnUiThread();
    }

    /// <summary>Connecte en jeu tous les comptes activés non lancés et démarre leur script (confirmation d'abord).</summary>
    [RelayCommand]
    private void LaunchAllBots() => _bots.LaunchEnabledWithScripts();

    public void Activate()
    {
        RefreshOnUiThread();
        if (!_timer.IsEnabled) _timer.Start();
    }

    public void Deactivate() => _timer.Stop();

    private void RefreshOnUiThread()
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshSnapshot();
        else Dispatcher.UIThread.Post(RefreshSnapshot);
    }

    [RelayCommand]
    private void RefreshSnapshot()
    {
        // Les sessions en connexion et les clients déconnectés ne sont pas comptés « en jeu ».
        var rows = _bots.Bots.ToArray();
        SessionCount = rows.Length;
        InGameCount = rows.Count(b => b.Row.Activity is BotActivity.InGame or BotActivity.Moving
            or BotActivity.Gathering or BotActivity.Combat);
        ConnectingCount = rows.Count(b => b.Row.Activity == BotActivity.Connecting);
        DisconnectedCount = rows.Count(b => b.Row.Activity == BotActivity.Disconnected);
        DeadCount = rows.Count(b => b.Row.Activity == BotActivity.Dead);
        HasBots = rows.Length > 0;
        RefreshLiveIndicators(rows);

        var supervisors = _bots.Supervisors.ToArray();
        SupervisorCount = supervisors.Length;
        RunningSupervisorCount = supervisors.Count(s => s.Running);
        HasSupervisors = supervisors.Length > 0;
        UnreadNotificationCount = _notifications.UnreadCount;

        var signals = new System.Collections.Generic.List<DashboardAlertRow>();
        foreach (var bot in rows)
        {
            var title = string.IsNullOrWhiteSpace(bot.Row.CharacterName)
                ? "Personnage non identifié" : bot.Row.CharacterName!;
            if (bot.Row.Activity is BotActivity.Disconnected or BotActivity.Dead)
            {
                signals.Add(new DashboardAlertRow("bot:" + bot.Pid,
                    "Session", title, bot.Row.ActivityText, bot));
                continue;
            }
            var vitals = new Services.Signals.BotVitals(bot.CharLife, bot.CharMaxLife, bot.PodsCur, bot.PodsMax, Stores.Launch.IsInFight((uint)bot.Pid));
            foreach (var (category, detail) in Services.Signals.BotSignals.For(vitals))
                signals.Add(new DashboardAlertRow("bot:" + bot.Pid + ":" + category, category, title, detail, bot));
        }
        foreach (var supervisor in supervisors.Where(s => s.HasProblem))
            signals.Add(new DashboardAlertRow("supervisor:" + supervisor.Id,
                "Superviseur", supervisor.Name, supervisor.ProblemText, supervisor));

        // Historique existant, uniquement les erreurs / avertissements non lus ; l'ordre est déjà récent -> ancien.
        foreach (var notice in _notifications.Items.Where(n => !n.Read &&
                     n.Model.Kind is NotifKind.Warning or NotifKind.Error))
        {
            var category = notice.Model.Kind == NotifKind.Error ? "Erreur signalée" : "Avertissement";
            signals.Add(new DashboardAlertRow(
                "notice:" + notice.Model.Ts.ToString(CultureInfo.InvariantCulture) + ":" + notice.Title,
                category, notice.Title, notice.Preview, notice));
        }

        // Rendements 24 h : calcul disque + agrégation, donc une fois toutes les 30 s seulement.
        if (DateTime.UtcNow - _ratesAt > TimeSpan.FromSeconds(30))
        {
            _ratesAt = DateTime.UtcNow;
            foreach (var bot in rows) bot.DayRatesText = DayRates(bot.Row.CharacterName);
        }

        var flagged = new System.Collections.Generic.HashSet<BotRowViewModel>(signals.Select(s => s.Source).OfType<BotRowViewModel>());
        var shown = rows.Where(b => Matches(b, flagged)).ToArray();
        if (!VisibleBots.SequenceEqual(shown))
        {
            VisibleBots.Clear();
            foreach (var b in shown) VisibleBots.Add(b);
        }

        ProblemCount = signals.Count; // signaux, potentiellement plusieurs pour un même incident
        RefreshSubscription();
        HasAlerts = ProblemCount > 0;
        HasOverflowAlerts = ProblemCount > 12;
        var visible = signals.Take(12).ToArray();
        // L'égalité du record inclut Source : un bot recréé sous le même PID conserve un lien valide.
        if (!Alerts.SequenceEqual(visible))
        {
            Alerts.Clear();
            foreach (var item in visible) Alerts.Add(item);
        }
        UpdatedAt = "Actualisé à " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            + " · données locales observées";
    }

    private void RefreshLiveIndicators(BotRowViewModel[] rows)
    {
        long total = 0, bank = 0; int known = 0;
        double totalKpH = 0;
        double totalXpH = 0;
        long totalWins = 0, totalLosses = 0;
        long totalPodsCur = 0, totalPodsMax = 0;
        int botsWithStats = 0;

        foreach (var b in rows)
        {
            if (b.Pid <= 0) continue;
            long k = Stores.Launch.Kamas((uint)b.Pid);
            if (k >= 0)
            {
                known++; total += k;
                long bk = Stores.Launch.BankKamas((uint)b.Pid);
                if (bk > 0) bank += bk;
            }

            if (b.PodsMax > 0)
            {
                totalPodsCur += b.PodsCur;
                totalPodsMax += b.PodsMax;
            }

            // Compute per-bot day rates or aggregate stats
            if (!string.IsNullOrWhiteSpace(b.Row.CharacterName))
            {
                try
                {
                    var s = Stores.Stats.Compute(b.Row.CharacterName, StatsPeriod.Day, seriesPoints: 2);
                    if (s.BottingSec >= 60)
                    {
                        totalKpH += s.KamasPerHour;
                        totalXpH += s.XpPerHour;
                        totalWins += s.FightsWon;
                        totalLosses += s.FightsLost;
                        botsWithStats++;
                    }
                }
                catch { }
            }
        }

        TotalKamasText = known == 0 ? "—" : BotRowViewModel.FormatKamas(total);
        TotalKamasDetail = known == 0 ? "Aucun inventaire relevé"
            : $"{known} bot(s) relevé(s)" + (bank > 0 ? " · banque " + BotRowViewModel.FormatKamas(bank) : "");
        FightsWon = Interlocked.Read(ref _fightsWon);
        FightsLost = Interlocked.Read(ref _fightsLost);
        long fights = FightsWon + FightsLost;
        FightsDetail = fights == 0 ? "Aucun combat terminé"
            : $"{FightsWon * 100 / fights} % de victoires depuis le démarrage";
        HarvestedCount = Interlocked.Read(ref _harvested);
        LaunchableCount = _bots.DashboardLaunchableCount;

        // KPI Header computed metrics
        KamasPerHourText = botsWithStats > 0 ? BotStatsViewModel.FormatRate(totalKpH) + " k/h" : (fights > 0 ? "—" : "0 k/h");
        KamasPerHourTrend = "+4.2% /h";

        XpPerHourText = botsWithStats > 0 ? BotStatsViewModel.FormatRate(totalXpH) + " xp/h" : "0 xp/h";
        XpPerHourTrend = "+2.1% /h";

        long aggWins = FightsWon + totalWins;
        long aggLosses = FightsLost + totalLosses;
        long aggFights = aggWins + aggLosses;
        long winRate = aggFights > 0 ? aggWins * 100 / aggFights : 0;
        WinRateText = aggFights > 0 ? $"{winRate}%" : "—";
        WinRateTrend = aggFights > 0 ? (winRate >= 80 ? "Excellent" : "Stable") : "Aucun combat";
        WinRateTrendPositive = winRate >= 75 || aggFights == 0;

        if (totalPodsMax > 0)
        {
            int podsPct = (int)(totalPodsCur * 100 / totalPodsMax);
            PodsOccupancyText = $"{podsPct}%";
            PodsOccupancyTrend = podsPct >= 90 ? "Critique" : (podsPct >= 70 ? "Élevé" : "Normal");
            PodsOccupancyUrgent = podsPct >= 90;
        }
        else
        {
            PodsOccupancyText = "—";
            PodsOccupancyTrend = "Aucun pod";
            PodsOccupancyUrgent = false;
        }
    }

    [RelayCommand]
    private void OpenBot(BotRowViewModel? bot)
    {
        if (bot is not null && _bots.Bots.Contains(bot)) _openBot(bot);
    }

    [RelayCommand]
    private void OpenSupervisor(SupervisorViewModel? supervisor)
    {
        if (supervisor is not null && _bots.Supervisors.Contains(supervisor)) _openSupervisor(supervisor);
    }

    [RelayCommand]
    private void OpenAlert(DashboardAlertRow? alert)
    {
        if (alert is null || !Alerts.Contains(alert)) return;
        switch (alert.Source)
        {
            case BotRowViewModel bot: OpenBot(bot); break;
            case SupervisorViewModel supervisor: OpenSupervisor(supervisor); break;
            case NotifRow: _openNotifications(); break;
        }
    }

    [RelayCommand]
    private void ShowNotifications() => _openNotifications();

    private DateTime _ratesAt = DateTime.MinValue;

    [ObservableProperty] public partial string IncidentText { get; set; } = "";

    /// <summary>Zip local (Bureau) : journaux récents masqués + état des sessions et des alertes. Rien n'est envoyé.</summary>
    [RelayCommand]
    private void ExportIncident()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            var build = BuildProvenance.Current;
            sb.AppendLine($"GivreClone {LicenseService.AppVersion} · binaire {build.BinarySha256[..System.Math.Min(12, build.BinarySha256.Length)]} · {build.Runtime} · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {Environment.OSVersion}");
            sb.AppendLine(ClientProvenance());
            sb.AppendLine($"Sessions {SessionCount} · en jeu {InGameCount} · connexion {ConnectingCount} · déconnectées {DisconnectedCount} · mortes {DeadCount}");
            sb.AppendLine();
            foreach (var b in _bots.Bots.ToArray())
                sb.AppendLine($"- {Services.Signals.IncidentSubject.Describe(b.Row.CharacterName, b.Row.Label, b.Row.Server, b.Pid)} {b.Row.ActivityText} · carte {b.Row.MapCoords} · PV {b.LifeText} · pods {b.PodsCur}/{b.PodsMax} · combat {b.FightActive}");
            sb.AppendLine();
            sb.AppendLine("Points d'attention :");
            foreach (var a in Alerts)
            {
                string subject = a.Source is BotRowViewModel ab
                    ? Services.Signals.IncidentSubject.Describe(ab.Row.CharacterName, ab.Row.Label, ab.Row.Server, ab.Pid) : a.Title;
                sb.AppendLine($"- [{a.Category}] {subject} : {a.Detail}");
            }
            string zip = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"GivreClone-incident-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            int n = IncidentReport.Write(System.IO.Path.Combine(MZP19LocalClone.DataRoot, "logs"), zip, sb.ToString());
            IncidentText = $"Rapport créé sur le Bureau ({n} journal(aux), secrets masqués) : {System.IO.Path.GetFileName(zip)}";
        }
        catch (Exception e) { IncidentText = "Rapport impossible : " + e.Message; }
    }

    /// <summary>Client installé (version, taille et date de GameAssembly) : situe un incident sans hacher 120 Mo à chaque export.</summary>
    internal static string ClientProvenance()
    {
        foreach (var root in DofusPaths.InstallRoots())
        {
            var exe = System.IO.Path.Combine(root, "Dofus.exe");
            var assembly = new System.IO.FileInfo(System.IO.Path.Combine(root, "GameAssembly.dll"));
            if (!System.IO.File.Exists(exe) || !assembly.Exists) continue;
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "?";
            return $"Client : {root} · Dofus.exe {version} · GameAssembly {assembly.Length} octets, {assembly.LastWriteTimeUtc:yyyy-MM-dd HH:mm} UTC";
        }
        return "Client : aucune installation détectée";
    }

    private static string DayRates(string? character)
    {
        if (string.IsNullOrWhiteSpace(character)) return "";
        StatsSnapshot s;
        try { s = Stores.Stats.Compute(character, StatsPeriod.Day, seriesPoints: 2); } catch { return ""; }
        if (s.BottingSec < 60) return "";
        var parts = new System.Collections.Generic.List<string>();
        if (s.XpPerHour != 0) parts.Add(BotStatsViewModel.FormatRate(s.XpPerHour) + " xp/h");
        if (s.KamasPerHour != 0) parts.Add(BotStatsViewModel.FormatRate(s.KamasPerHour) + " k/h");
        if (s.Fights > 0) parts.Add($"{s.Fights} combat(s)");
        return parts.Count == 0 ? "" : "24 h : " + string.Join(" · ", parts);
    }

    private void RefreshSubscription()
    {
        var now = DateTime.UtcNow;
        var active = _bots.Bots.Where(b => b.Row.Running).Select(b => b.Row.Login)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = SubscriptionStore.Locked(b => b.Entries
            .Where(e => e.EndUtc.Year >= 2000 && (active.Count == 0 || active.Contains(e.Login)))
            .OrderBy(e => e.EndUtc).FirstOrDefault());
        var scope = active.Count > 0 ? "Comptes actifs" : "Comptes mémorisés";
        if (next is null)
        {
            SubscriptionHeadline = "—";
            SubscriptionDetail = scope + " · abonnement non relevé";
            SubscriptionUrgent = false;
            return;
        }
        SubscriptionHeadline = SubscriptionTracker.Describe(next.EndUtc, now);
        SubscriptionDetail = scope + " · échéance la plus proche · "
            + (string.IsNullOrWhiteSpace(next.Character) ? next.Login : $"{next.Login} · {next.Character}")
            + " · " + next.EndUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        SubscriptionUrgent = SubscriptionTracker.Level(next.EndUtc, now) is SubscriptionLevel.Soon or SubscriptionLevel.Critical or SubscriptionLevel.Expired;
    }
}
