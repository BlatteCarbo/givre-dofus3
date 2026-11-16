using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.NoCodeLot2;

namespace GivreClone.App.ViewModels;

/// <summary>Formulaire local de simulation : ne convertit jamais un plan en script exécutable.</summary>
public sealed partial class RoutineDraftField : ObservableObject
{
    public string Key { get; }
    public string Caption { get; }
    public string EvidenceExpected { get; }
    [ObservableProperty] public partial string Value { get; set; } = string.Empty;

    public RoutineDraftField(RoutineField field)
    {
        Key = field.Key;
        Caption = field.Caption;
        EvidenceExpected = field.EvidenceExpected;
    }
}

public partial class BotsViewModel
{
    public IReadOnlyList<RoutineTemplate> NoCodeLot2Templates => RoutineLibrary.All;
    public ObservableCollection<RoutineDraftField> NoCodeLot2Fields { get; } = new();
    [ObservableProperty] public partial string NoCodeLot2SelectedTitle { get; set; } = "Mineur · exploration de grotte";
    [ObservableProperty] public partial string NoCodeLot2PreviewText { get; set; } = "Choisir un modèle et renseigner ses identifiants. Simulation hors jeu.";
    [ObservableProperty] public partial bool NoCodeLot2PreviewAvailable { get; set; }

    private RoutineTemplate? _noCodeLot2Template;
    /// <summary>Preuve source injectée par l'atlas local approuvé. Null : rien n'est vérifié.</summary>
    public Func<RoutineEvidence?>? NoCodeLot2EvidenceProvider { get; set; }

    private void EnsureNoCodeLot2()
    {
        if (_noCodeLot2Template is null) NoCodeLot2ChooseTemplate("mineur-grotte");
    }

    [RelayCommand]
    private void NoCodeLot2ChooseTemplate(string? id)
    {
        var template = RoutineLibrary.Find(id);
        if (template is null) return;
        foreach (var old in NoCodeLot2Fields) old.PropertyChanged -= OnNoCodeLot2FieldChanged;
        NoCodeLot2Fields.Clear();
        _noCodeLot2Template = template;
        NoCodeLot2SelectedTitle = template.Title;
        foreach (var field in template.Fields)
        {
            var row = new RoutineDraftField(field);
            row.PropertyChanged += OnNoCodeLot2FieldChanged;
            NoCodeLot2Fields.Add(row);
        }
        ResetNoCodeLot2Preview();
    }

    private void OnNoCodeLot2FieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RoutineDraftField.Value)) ResetNoCodeLot2Preview();
    }

    private void ResetNoCodeLot2Preview()
    {
        NoCodeLot2PreviewAvailable = false;
        NoCodeLot2PreviewText = "Saisie modifiée : relancer la simulation, sans action en jeu.";
    }

    [RelayCommand]
    private void NoCodeLot2Simulate()
    {
        EnsureNoCodeLot2();
        if (_noCodeLot2Template is null) return;
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var row in NoCodeLot2Fields)
        {
            if (string.IsNullOrWhiteSpace(row.Value)) continue;
            if (row.Value.Length > 18 || !long.TryParse(row.Value.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                NoCodeLot2PreviewText = $"Valeur invalide pour « {row.Caption} ». Entier positif attendu.";
                NoCodeLot2PreviewAvailable = false;
                return;
            }
            values[row.Key] = number;
        }
        // Ne jamais inférer une preuve d'une saisie, d'un site non admis, ou d'un cache non attesté.
        var empty = new RoutineEvidence(new HashSet<long>(), new HashSet<(long, long)>(),
            new HashSet<long>(), new HashSet<long>(), new HashSet<long>(), new Dictionary<long, decimal>());
        RoutineEvidence evidence;
        try { evidence = NoCodeLot2EvidenceProvider?.Invoke() ?? empty; }
        catch { evidence = empty; }
        RoutinePreview report;
        try { report = RoutinePlanner.Preview(_noCodeLot2Template, new RoutineInputs(values), evidence); }
        catch (Exception) { NoCodeLot2PreviewText = "Simulation indisponible : paramètres ou provenance refusés."; return; }
        var sb = new StringBuilder();
        sb.AppendLine($"{_noCodeLot2Template.Title} · SIMULATION UNIQUEMENT");
        foreach (var step in report.Steps)
            sb.AppendLine($"{step.Number}. [{step.Verdict}] {step.Label} — {step.Reason}");
        sb.AppendLine("Aucune exécution, aucun mouvement, achat, combat, ACK ou Lua généré.");
        NoCodeLot2PreviewText = sb.ToString();
        NoCodeLot2PreviewAvailable = true;
    }
}
