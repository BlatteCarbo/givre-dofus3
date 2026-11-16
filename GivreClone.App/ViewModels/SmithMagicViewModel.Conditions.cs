using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.ViewModels;

public sealed record SmithConditionChoice(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Une étape condition dans l'ordre d'exécution : plage de caractéristique et/ou de puits, lien et/ou, puis rune, sauter ou terminer.</summary>
public sealed partial class SmithConditionRow : ObservableObject
{
    public bool UseStat { get; }
    public string StatCode { get; }
    public string StatLabel { get; }
    public int Min { get; }
    public int Max { get; }
    public bool UsePit { get; }
    public int PitMin { get; }
    public int PitMax { get; }
    public ForgeCombine Combine { get; }
    public ForgeActionKind ActionKind { get; }
    public string RuneName { get; }
    public int Order { get; private set; }
    public string Text => $"{Order}. {Describe()}";

    public SmithConditionRow(bool useStat, string statCode, string statLabel, int min, int max, bool usePit, int pitMin, int pitMax, ForgeCombine combine, ForgeActionKind actionKind, string runeName)
    {
        UseStat = useStat;
        StatCode = statCode;
        StatLabel = statLabel;
        Min = min;
        Max = max;
        UsePit = usePit;
        PitMin = pitMin;
        PitMax = pitMax;
        Combine = combine;
        ActionKind = actionKind;
        RuneName = runeName;
        Order = 1;
    }

    public void SetOrder(int order)
    {
        if (Order == order) return;
        Order = order;
        OnPropertyChanged(nameof(Order));
        OnPropertyChanged(nameof(Text));
    }

    public string Describe()
    {
        string stat = UseStat ? $"{StatLabel} entre {Min} et {Max}" : "";
        string pit = UsePit ? $"puits entre {PitMin} et {PitMax}" : "";
        string test = (UseStat, UsePit) switch
        {
            (true, true) => Combine == ForgeCombine.Or ? $"{stat} ou {pit}" : $"{stat} et {pit}",
            (true, false) => stat,
            (false, true) => pit,
            _ => "toujours",
        };
        string action = ActionKind switch
        {
            ForgeActionKind.Skip => "sauter l'étape",
            ForgeActionKind.Finish => "terminer",
            _ => RuneName,
        };
        return $"{test} → {action}";
    }

    public ForgeCondition ToCondition() => new(
        UseStat ? StatCode : null,
        Min,
        Max,
        ActionKind switch
        {
            ForgeActionKind.Skip => ForgeAction.Skip,
            ForgeActionKind.Finish => ForgeAction.Finish,
            _ => ForgeAction.Use(RuneName),
        },
        UsePit ? PitMin : null,
        UsePit ? PitMax : null,
        Combine);
}

/// <summary>Budget d'une rune nommée par une étape condition. Même plafonds que le planificateur d'objectifs ; une rune absente du stock n'est pas comptée ici.</summary>
public static class SmithConditionBudget
{
    public readonly record struct Charge(string? Halt, int AddedRunes, long AddedKamas);

    public static long? RuneGid(string rune, IReadOnlyDictionary<long, (int EffectId, string Tier, string Name)> names)
    {
        foreach (var kv in names)
            if (ForgeState.RuneItemNames(rune).Contains(kv.Value.Name, System.StringComparer.OrdinalIgnoreCase))
                return kv.Key;
        return null;
    }

    public static long? AbsentRuneGid(string stop, IReadOnlyDictionary<long, (int EffectId, string Tier, string Name)>? names)
    {
        const string prefix = "rune absente du sac : ";
        if (names is null || stop is null || !stop.StartsWith(prefix, System.StringComparison.Ordinal)) return null;
        return RuneGid(stop[prefix.Length..].Trim(), names);
    }

    public static Charge Account(ForgeAction action, List<SmithRune> stock, IReadOnlyDictionary<long, (int EffectId, string Tier, string Name)>? names, SmithMagicLimits limits, int usedTotal, long spent, Dictionary<int, int> usedOnLine)
    {
        if (action.Kind != ForgeActionKind.Rune || names is null || RuneGid(action.Rune, names) is not long gid) return new(null, 0, 0);
        int index = stock.FindIndex(r => r.ItemId == gid && r.Quantity > 0);
        if (index < 0) return new(null, 0, 0);
        var rune = stock[index];
        int onLine = rune.EffectId > 0 ? usedOnLine.GetValueOrDefault(rune.EffectId) : 0;
        if (limits.MaxRunes <= 0 || limits.MaxRunesPerLine <= 0 || usedTotal >= limits.MaxRunes || (rune.EffectId > 0 && onLine >= limits.MaxRunesPerLine))
            return new("Limite de runes atteinte.", 0, 0);
        if (rune.UnitPrice < 0)
            return new("Prix de la rune inconnu. Arrêt, aucune rune passée.", 0, 0);
        if (spent < 0 || limits.MaxKamas < 0 || spent > limits.MaxKamas || rune.UnitPrice > limits.MaxKamas - spent)
            return new("Budget de runes atteint.", 0, 0);
        if (rune.EffectId > 0) usedOnLine[rune.EffectId] = onLine + 1;
        stock[index] = rune with { Quantity = rune.Quantity - 1 };
        return new(null, 1, rune.UnitPrice);
    }
}

public sealed class ForgeConditionHaltException : System.Exception
{
    public ForgeConditionHaltException(string? reason)
        : base(string.IsNullOrWhiteSpace(reason) ? "Arrêt, aucune rune passée." : reason) { }
}

public partial class SmithMagicViewModel
{
    private readonly Dictionary<long, List<SmithConditionRow>> _conditionsByItem = new();
    private bool _loadingConditions;
    public ObservableCollection<SmithConditionRow> ConditionSteps { get; } = [];
    public bool HasConditionSteps => ConditionSteps.Count > 0;
    public IReadOnlyList<SmithConditionChoice> ConditionStatChoices { get; } =
        new[] { new SmithConditionChoice("", "Aucune") }.Concat(RuneCatalog.Families.Select(f => new SmithConditionChoice(f.Code, f.Label))).ToArray();
    public IReadOnlyList<SmithConditionChoice> ConditionRuneChoices { get; } =
        RuneCatalog.Families.Select(f => new SmithConditionChoice(f.Code, f.Label)).ToArray();
    public IReadOnlyList<SmithConditionChoice> ConditionTierChoices { get; } =
        new[] { new SmithConditionChoice("Ba", "Normale"), new SmithConditionChoice("Pa", "Pa"), new SmithConditionChoice("Ra", "Ra") };
    public IReadOnlyList<SmithConditionChoice> ConditionActionChoices { get; } =
        new[] { new SmithConditionChoice("rune", "Rune"), new SmithConditionChoice("skip", "Sauter l'étape"), new SmithConditionChoice("finish", "Terminer") };
    public IReadOnlyList<SmithConditionChoice> ConditionLinkChoices { get; } =
        new[] { new SmithConditionChoice("and", "Et"), new SmithConditionChoice("or", "Ou") };

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ConditionUseStat), nameof(ConditionLinkApplies))] public partial SmithConditionChoice? ConditionStat { get; set; }
    [ObservableProperty] public partial int ConditionMin { get; set; }
    [ObservableProperty] public partial int ConditionMax { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ConditionLinkApplies))] public partial bool ConditionUsePit { get; set; }
    [ObservableProperty] public partial int ConditionPitMin { get; set; }
    [ObservableProperty] public partial int ConditionPitMax { get; set; }
    [ObservableProperty] public partial SmithConditionChoice? ConditionLink { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsConditionRuneAction))] public partial SmithConditionChoice? ConditionAction { get; set; }
    [ObservableProperty] public partial SmithConditionChoice? ConditionRuneFamily { get; set; }
    [ObservableProperty] public partial SmithConditionChoice? ConditionRuneTier { get; set; }
    public bool ConditionUseStat => !string.IsNullOrEmpty(ConditionStat?.Code);
    public bool ConditionLinkApplies => ConditionUseStat && ConditionUsePit;
    public bool IsConditionRuneAction => ConditionAction?.Code == "rune";

    private void InitializeConditionSteps()
    {
        ConditionSteps.CollectionChanged += (_, _) => OnConditionStepsChanged();
        ConditionStat = ConditionStatChoices[0];
        ConditionAction = ConditionActionChoices[0];
        ConditionLink = ConditionLinkChoices[0];
        ConditionRuneFamily = ConditionRuneChoices[0];
        ConditionRuneTier = ConditionTierChoices[0];
    }

    private void OnConditionStepsChanged()
    {
        if (_loadingConditions) return;
        RenumberConditionSteps();
        SaveConditionSteps();
        OnPropertyChanged(nameof(HasConditionSteps));
    }

    private void RenumberConditionSteps()
    {
        for (int i = 0; i < ConditionSteps.Count; i++) ConditionSteps[i].SetOrder(i + 1);
    }

    private void SaveConditionSteps()
    {
        if (_loadingConditions || _loadedUid <= 0) return;
        _conditionsByItem[_loadedUid] = ConditionSteps.ToList();
    }

    private void LoadConditionSteps()
    {
        _loadingConditions = true;
        try
        {
            ConditionSteps.Clear();
            if (_loadedUid > 0 && _conditionsByItem.TryGetValue(_loadedUid, out var saved))
                foreach (var row in saved) ConditionSteps.Add(row);
            RenumberConditionSteps();
        }
        finally { _loadingConditions = false; }
        OnPropertyChanged(nameof(HasConditionSteps));
    }

    private void ResetConditionSteps()
    {
        _loadingConditions = true;
        try
        {
            _conditionsByItem.Clear();
            ConditionSteps.Clear();
        }
        finally { _loadingConditions = false; }
        OnPropertyChanged(nameof(HasConditionSteps));
    }

    private void ForgetConditionItem(long uid, bool loaded)
    {
        _conditionsByItem.Remove(uid);
        if (!loaded) return;
        _loadingConditions = true;
        try { ConditionSteps.Clear(); }
        finally { _loadingConditions = false; }
        OnPropertyChanged(nameof(HasConditionSteps));
    }

    private List<ForgeCondition> CopyConditionRules(long uid)
    {
        if (uid <= 0 || !_conditionsByItem.TryGetValue(uid, out var rows) || rows.Count == 0) return [];
        return rows.Select(row => row.ToCondition()).ToList();
    }

    /// <summary>Décision des étapes de l'objet chargé. Null : aucune étape, le planificateur d'objectifs reste utilisé.</summary>
    public ForgeConditionOutcome? PreviewConditionDecision(IReadOnlyDictionary<string, int> stats, int? pit)
    {
        var rules = CopyConditionRules(_loadedUid);
        if (rules.Count == 0) return null;
        return ForgeEngine.DecideConditionSteps(stats, pit, rules);
    }

    private IEnumerable<(long Gid, int Quantity)> MissingNamedRune(string stop)
    {
        if (SmithConditionBudget.AbsentRuneGid(stop, RuneItems()) is long gid and > 0)
            yield return (gid, 1);
    }

    [RelayCommand]
    private void AddConditionStep()
    {
        if (_loadedUid <= 0) { Status = "Charge d'abord un objet de la file."; return; }
        bool useStat = ConditionUseStat;
        bool usePit = ConditionUsePit;
        if (useStat && ConditionMin > ConditionMax) { Status = "L'intervalle de caractéristique est inversé."; return; }
        if (usePit && ConditionPitMin > ConditionPitMax) { Status = "L'intervalle de puits est inversé."; return; }
        var kind = ConditionAction?.Code switch
        {
            "skip" => ForgeActionKind.Skip,
            "finish" => ForgeActionKind.Finish,
            "rune" => ForgeActionKind.Rune,
            _ => (ForgeActionKind?)null,
        };
        if (kind is null) { Status = "Choisis l'action de l'étape."; return; }
        string runeName = "";
        if (kind == ForgeActionKind.Rune)
        {
            var family = RuneCatalog.Families.FirstOrDefault(f => f.Code == ConditionRuneFamily?.Code);
            string tier = ConditionRuneTier?.Code ?? "";
            if (family is null || tier is not ("Ba" or "Pa" or "Ra")) { Status = "Choisis la rune à poser."; return; }
            runeName = RuneCatalog.ItemName(family, tier);
            if (runeName.Length == 0) { Status = "Choisis la rune à poser."; return; }
        }
        string statCode = useStat ? ConditionStat!.Code : "";
        string statLabel = useStat ? ConditionStat!.Label : "";
        var combine = ConditionLink?.Code == "or" ? ForgeCombine.Or : ForgeCombine.And;
        var row = new SmithConditionRow(useStat, statCode, statLabel, ConditionMin, ConditionMax, usePit, ConditionPitMin, ConditionPitMax, combine, kind.Value, runeName);
        ConditionSteps.Add(row);
        Status = useStat || usePit ? $"Étape ajoutée : {row.Describe()}." : "Étape ajoutée : elle s'applique à chaque passage.";
    }

    [RelayCommand]
    private void RemoveConditionStep(SmithConditionRow? row)
    {
        if (row is null) return;
        ConditionSteps.Remove(row);
    }

    [RelayCommand]
    private void MoveConditionStepUp(SmithConditionRow? row)
    {
        if (row is null) return;
        int index = ConditionSteps.IndexOf(row);
        if (index <= 0) return;
        ConditionSteps.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveConditionStepDown(SmithConditionRow? row)
    {
        if (row is null) return;
        int index = ConditionSteps.IndexOf(row);
        if (index < 0 || index >= ConditionSteps.Count - 1) return;
        ConditionSteps.Move(index, index + 1);
    }
}
