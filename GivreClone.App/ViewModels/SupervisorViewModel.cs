using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Supervisor;

namespace GivreClone.App.ViewModels;

/// <summary>Un modèle de script proposé dans l'éditeur du superviseur.</summary>
public sealed class SupervisorTemplateItem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public override string ToString() => Title;
}

/// <summary>Une ligne du tableau « bots supervisés » : un compte, vu depuis le superviseur.</summary>
public sealed partial class SupervisedBotRow : ObservableObject
{
    private static readonly IBrush Ok = new ImmutableSolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush Warn = new ImmutableSolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IBrush Bad = new ImmutableSolidColorBrush(Color.Parse("#F0533B"));
    private static readonly IBrush Off = new ImmutableSolidColorBrush(Color.Parse("#79828F"));

    /// <summary>Identifiant INTERNE du compte (pas l'identifiant de connexion) : sert au changement de mode.</summary>
    public string AccountId { get; init; } = "";
    [ObservableProperty] public partial string Account { get; set; } = "";
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial int Level { get; set; }
    [ObservableProperty] public partial string Map { get; set; } = "—";
    [ObservableProperty] public partial string Mode { get; set; } = "";
    [ObservableProperty] public partial string ModeTip { get; set; } = "";
    [ObservableProperty] public partial string Pods { get; set; } = "—";
    [ObservableProperty] public partial string Script { get; set; } = "—";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial IBrush StatusBrush { get; set; } = Off;

    public void Apply(BotSnapshot b)
    {
        Account = b.Account; Name = b.Name; Level = b.Level; Map = SupervisorFormat.MapText(b);
        Mode = SupervisorFormat.ModeText(b.Mode); ModeTip = SupervisorFormat.ModeTip(b.Mode);
        Pods = SupervisorFormat.PodsText(b); Script = SupervisorFormat.ScriptText(b);
        Status = SupervisorFormat.StatusText(b.Status);
        StatusBrush = SupervisorFormat.StatusTone(b.Status) switch { "ok" => Ok, "warn" => Warn, "bad" => Bad, _ => Off };
    }
}

/// <summary>Une ligne de la console d'un superviseur.</summary>
public sealed record SupervisorLogLine(DateTime Time, string Level, string Text)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>
/// Un superviseur (onglet Bots, section « Superviseurs ») : un script qui ne pilote aucun perso et surveille tous les bots.
/// Lancer / arrêter, console, problème en cours, modèles, tableau des bots supervisés, démarrage avec l'application.
/// </summary>
public sealed partial class SupervisorViewModel : ObservableObject
{
    private static readonly IBrush Green = new ImmutableSolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush Grey = new ImmutableSolidColorBrush(Color.Parse("#79828F"));
    private static readonly IBrush Red = new ImmutableSolidColorBrush(Color.Parse("#F0533B"));
    public const int MaxConsoleLines = 500;

    private readonly SupervisorRuntime _runtime;
    private readonly Action _persist;
    internal SupervisorDef Def { get; }

    public SupervisorViewModel(SupervisorDef def, SupervisorRuntime runtime, Action persist)
    {
        Def = def; _runtime = runtime; _persist = persist;
        _name = def.Name; _code = def.Code; _autoStart = def.AutoStart;
        foreach (var t in SupervisorTemplates.All) TemplateItems.Add(new SupervisorTemplateItem { Id = t.Id, Title = t.Title });
        Refresh();
    }

    public string Id => Def.Id;

    // ---- Édition (persistée) ----
    private string _name, _code;
    private bool _autoStart;

    public string Name
    {
        get => _name;
        set { var v = value?.Trim() ?? ""; if (!Running && v.Length > 0 && v != _name) { _name = v; Def.Name = v; _persist(); OnPropertyChanged(); } }   // pas de renommage en cours d'exécution
    }

    /// <summary>Script du superviseur (champ lié à l'éditeur).</summary>
    public string Code
    {
        get => _code;
        set { if (value != _code) { _code = value ?? ""; Def.Code = _code; _persist(); OnPropertyChanged(); } }
    }

    /// <summary>« Démarrer avec GivreClone » : il se lance dès que la licence est active.</summary>
    public bool AutoStart
    {
        get => _autoStart;
        set { if (value != _autoStart) { _autoStart = value; Def.AutoStart = value; _persist(); OnPropertyChanged(); } }
    }

    // ---- État ----
    [ObservableProperty] public partial bool Running { get; set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial IBrush StatusBrush { get; set; } = Grey;
    [ObservableProperty] public partial string ProblemText { get; set; } = "";
    [ObservableProperty] public partial string BotsOverview { get; set; } = "Aucun bot";

    public string ToggleText => Running ? "Arrêter" : "Démarrer";
    public bool HasProblem => ProblemText.Length > 0;
    public bool HasBots => Bots.Count > 0;

    public ObservableCollection<SupervisedBotRow> Bots { get; } = new();
    public ObservableCollection<SupervisorLogLine> Console { get; } = new();
    public ObservableCollection<SupervisorTemplateItem> TemplateItems { get; } = new();

    /// <summary>Demande d'afficher la console / la ligne d'un problème (la vue fait défiler).</summary>
    public event Action? JumpToLogRequested;
    public event Action<int>? JumpToProblemRequested;

    private string _endReason = "";
    private int _problemLine = -1;

    partial void OnRunningChanged(bool value) { OnPropertyChanged(nameof(ToggleText)); Refresh(); }
    partial void OnProblemTextChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    private void Refresh()
    {
        if (Running) { StatusText = "En cours"; StatusBrush = Green; }
        else if (_endReason == "error") { StatusText = "Arrêté sur erreur"; StatusBrush = Red; }
        else if (_endReason.Length > 0) { StatusText = $"Arrêté ({_endReason})"; StatusBrush = Grey; }
        else { StatusText = "Arrêté"; StatusBrush = Grey; }
    }

    /// <summary>Journal du script (thread UI). Une ligne d'erreur devient le « problème » courant.</summary>
    public void AddLog(string level, string text)
    {
        Console.Add(new SupervisorLogLine(DateTime.Now, level, text));
        while (Console.Count > MaxConsoleLines) { Console.RemoveAt(0); if (_problemLine >= 0) _problemLine--; }
        if (level == "error") { ProblemText = text; _problemLine = Console.Count - 1; }
    }

    public void OnEnded(string reason)
    {
        _endReason = reason;
        Running = false;
        Refresh();
    }

    /// <summary>Met à jour le tableau des bots supervisés (réconciliation en place par compte, sans vider la liste).</summary>
    public void UpdateBots(IReadOnlyList<BotSnapshot> snapshot, Func<string, string> accountIdOf)
    {
        var keep = snapshot.Select(b => accountIdOf(b.Account)).ToHashSet();
        for (int i = Bots.Count - 1; i >= 0; i--) if (!keep.Contains(Bots[i].AccountId)) Bots.RemoveAt(i);
        foreach (var b in snapshot)
        {
            var id = accountIdOf(b.Account);
            var row = Bots.FirstOrDefault(r => r.AccountId == id);
            if (row is null) { row = new SupervisedBotRow { AccountId = id }; Bots.Add(row); }
            row.Apply(b);
        }
        BotsOverview = SupervisorFormat.Overview(snapshot.ToList());
        OnPropertyChanged(nameof(HasBots));
        // Un bot déconnecté ou mort fait remonter un problème tant que le script n'en a pas signalé lui-même.
        if (!Running || _problemLine < 0) ProblemText = SupervisorFormat.Problem(snapshot.ToList()) ?? "";
    }

    /// <summary>Démarrage automatique (« Démarrer avec GivreClone ») : lance s'il est arrêté.</summary>
    public void StartIfIdle() { if (!Running) Toggle(); }

    [RelayCommand]
    private void Toggle()
    {
        if (Running) { _runtime.Stop(Name, "user"); return; }
        _endReason = "";
        ProblemText = ""; _problemLine = -1;
        var (ok, reason) = _runtime.Start(Name, Code);
        if (!ok) { AddLog("error", reason); ProblemText = reason; _problemLine = Console.Count - 1; Toasts.Show(reason, success: false); return; }
        Running = true;
    }

    [RelayCommand] private void ClearConsole() { Console.Clear(); _problemLine = -1; }
    [RelayCommand] private void JumpToLog() => JumpToLogRequested?.Invoke();
    [RelayCommand] private void JumpToProblem() => JumpToProblemRequested?.Invoke(_problemLine);

    /// <summary>Insère un modèle : remplace un script vide, sinon l'ajoute à la suite.</summary>
    [RelayCommand]
    private void InsertTemplate(SupervisorTemplateItem? item)
    {
        if (item is null || SupervisorTemplates.Get(item.Id) is not { } t) return;
        Code = string.IsNullOrWhiteSpace(Code) ? t.Code : Code.TrimEnd() + "\n\n" + t.Code;
    }
}
