using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne de la liste « Défis acceptés » : défi générique du jeu, sa règle (texte du jeu), ce que l'IA en fait.</summary>
public sealed partial class ChallengeChoiceRow : ObservableObject
{
    private readonly Action<ChallengeChoiceRow> _changed;
    public ChallengeChoiceRow(int id, string name, string rule, ChallengeSupport support, bool accepted, Action<ChallengeChoiceRow> changed,
                              Avalonia.Media.Imaging.Bitmap? iconOn = null, Avalonia.Media.Imaging.Bitmap? iconOff = null)
    {
        Id = id; Name = name; Rule = rule; Support = support; _isAccepted = accepted; _changed = changed; IconOn = iconOn; IconOff = iconOff;
    }
    public int Id { get; }
    /// <summary>Icône du challenge (silhouette du jeu teintée) : dorée si accepté, éteinte sinon. null : bundle absent.</summary>
    public Avalonia.Media.Imaging.Bitmap? IconOn { get; }
    public Avalonia.Media.Imaging.Bitmap? IconOff { get; }
    public bool HasIcon => IconOn is not null;
    public string Name { get; }
    public string Rule { get; }
    public ChallengeSupport Support { get; }
    public bool IsEnforced => Support == ChallengeSupport.Enforced;
    public bool IsNatural => Support == ChallengeSupport.Natural;
    public bool IsUnsupported => Support == ChallengeSupport.Unsupported;

    private bool _isAccepted;
    public bool IsAccepted
    {
        get => _isAccepted;
        set { if (_isAccepted == value) return; _isAccepted = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRefused)); _changed(this); }
    }
    public bool IsRefused => !_isAccepted;
}

// ===================== Onglet Combat : carte « Challenges » (choix auto, IA adaptée, bonus) + fenêtre « Challenges acceptés » =====================
public partial class BotsViewModel
{
    /// <summary>L'IA tient les défis actifs (Statue, Versatile, Focus, Hardi…), y compris un défi choisi à la main.</summary>
    public bool CombatChallengeRules
    {
        get => _profile.ChallengeRules;
        set { if (_profile.ChallengeRules != value) { _profile.ChallengeRules = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Bonus préféré = XP (segment « XP »), annoncé par GivreClone à chaque combat (socket et MITM).</summary>
    public bool CombatChallengeXp
    {
        get => !_profile.ChallengeLoot;
        set { if (value) SetChallengeLoot(false); }
    }

    /// <summary>Bonus préféré = butin (segment « Butin »).</summary>
    public bool CombatChallengeLoot
    {
        get => _profile.ChallengeLoot;
        set { if (value) SetChallengeLoot(true); }
    }

    private void SetChallengeLoot(bool loot)
    {
        if (_profile.ChallengeLoot == loot) return;
        _profile.ChallengeLoot = loot;
        OnPropertyChanged(nameof(CombatChallengeXp));
        OnPropertyChanged(nameof(CombatChallengeLoot));
        SaveAndPush();
    }

    public ObservableCollection<ChallengeChoiceRow> ChallengeChoices { get; } = new();

    /// <summary>« Défis acceptés (31/43)… » : compte sur le profil affiché (sans construire la liste).</summary>
    public string ChallengeListLabel
    {
        get
        {
            int total = ChallengeCatalog.GenericIds.Count;
            int on = ChallengeCatalog.GenericIds.Count(id => LaunchService.ChallengeAccepted(_profile, ChallengeCatalog.RuleOfGeneric(id)));
            return Loc.Fmt("L.Bots.Combat.ChallengeList", on, total);
        }
    }

    [ObservableProperty] public partial bool ChallengeListVisible { get; set; }

    [RelayCommand]
    private void OpenChallengeList()
    {
        RebuildChallengeChoices();
        ChallengeListVisible = true;
    }

    [RelayCommand]
    private void CloseChallengeList() => ChallengeListVisible = false;

    /// <summary>Retour aux valeurs conseillées : plus aucune surcharge, chaque défi reprend son « accepté par défaut ».</summary>
    [RelayCommand]
    private void ResetChallengeChoices()
    {
        _profile.ChallengeOn.Clear();
        _profile.ChallengeOff.Clear();
        SaveAndPush();
        RebuildChallengeChoices();
        OnPropertyChanged(nameof(ChallengeListLabel));
    }

    private const uint IconTintOn = 0xF2C063, IconTintOff = 0x5E636D;   // doré (accepté) / gris éteint (refusé)

    private void RebuildChallengeChoices()
    {
        ChallengeChoices.Clear();
        var names = Stores.Launch.GameNames;
        foreach (int id in ChallengeCatalog.GenericIds)
        {
            var rule = ChallengeCatalog.RuleOfGeneric(id);
            string name = names.ChallengeName(id) is { Length: > 0 } n ? n : Loc.Fmt("L.Bots.Combat.ChallengeFallback", id);
            // Texte du jeu : paragraphes recollés, paramètre de tours ({1}) rendu lisible.
            string desc = (names.ChallengeDescription(id) ?? "").Replace("\r", "").Replace("\n\n", " ").Replace("\n", " ").Replace("{1}", "N").Trim();
            while (desc.Contains("  ")) desc = desc.Replace("  ", " ");
            int? iconId = names.ChallengeIconId(id);
            ChallengeChoices.Add(new ChallengeChoiceRow(id, name, desc, ChallengeCatalog.Support(rule),
                LaunchService.ChallengeAccepted(_profile, rule), OnChallengeChoiceChanged,
                iconId is int ic ? Stores.ChallengeIcons.Get(ic, IconTintOn) : null,
                iconId is int ic2 ? Stores.ChallengeIcons.Get(ic2, IconTintOff) : null));
        }
    }

    // Case cochée/décochée : on n'enregistre QUE l'écart à la valeur conseillée (une valeur conseillée qui évolue reste suivie).
    private void OnChallengeChoiceChanged(ChallengeChoiceRow row)
    {
        bool def = ChallengeCatalog.DefaultAccepted(ChallengeCatalog.RuleOfGeneric(row.Id));
        _profile.ChallengeOn.Remove(row.Id);
        _profile.ChallengeOff.Remove(row.Id);
        if (row.IsAccepted != def) (row.IsAccepted ? _profile.ChallengeOn : _profile.ChallengeOff).Add(row.Id);
        SaveAndPush();
        OnPropertyChanged(nameof(ChallengeListLabel));
    }

    // Profil affiché rechargé (autre bot / perso identifié) : rafraîchit la carte « Défis ».
    private void RefreshChallengeCard()
    {
        OnPropertyChanged(nameof(CombatChallengeRules));
        OnPropertyChanged(nameof(CombatChallengeXp));
        OnPropertyChanged(nameof(CombatChallengeLoot));
        OnPropertyChanged(nameof(ChallengeListLabel));
        if (ChallengeListVisible) RebuildChallengeChoices();
    }
}
