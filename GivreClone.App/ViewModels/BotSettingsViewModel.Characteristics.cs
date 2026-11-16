using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class CharacteristicTargetRow : ObservableObject
{
    private readonly Action _changed;
    [ObservableProperty] public partial int StatIndex { get; set; }
    [ObservableProperty] public partial int Cap { get; set; }
    [ObservableProperty] public partial bool Remainder { get; set; }
    public bool HasCap => !Remainder;
    public CharacteristicTargetRow(CharacteristicTarget target, Action changed)
    {
        _changed = () => { };
        StatIndex = Math.Max(0, Array.FindIndex(CharacteristicUpgradePolicy.Stats, s => s.Name == target.Stat));
        Cap = target.Cap ?? 100;
        Remainder = target.Cap is null;
        _changed = changed;
    }
    public CharacteristicTarget Target => new(CharacteristicUpgradePolicy.Stats[Math.Clamp(StatIndex, 0, 5)].Name,
        Remainder ? null : Math.Max(0, Cap));
    partial void OnStatIndexChanged(int value) => _changed();
    partial void OnCapChanged(int value) => _changed();
    partial void OnRemainderChanged(bool value) { OnPropertyChanged(nameof(HasCap)); _changed(); }
}

public partial class BotSettingsViewModel
{
    [ObservableProperty] public partial bool OrderedCharacteristicsEnabled { get; set; }
    [ObservableProperty] public partial CharacteristicTargetRow? SelectedCharacteristicTarget { get; set; }
    public ObservableCollection<CharacteristicTargetRow> CharacteristicTargets { get; } = new();

    private void LoadCharacteristicTargets()
    {
        OrderedCharacteristicsEnabled = _cfg.OrderedCharacteristicsEnabled;
        SelectedCharacteristicTarget = null;
        CharacteristicTargets.Clear();
        foreach (var target in _cfg.OrderedCharacteristicTargets ?? new())
            CharacteristicTargets.Add(new CharacteristicTargetRow(target, SaveCharacteristicTargets));
    }

    partial void OnOrderedCharacteristicsEnabledChanged(bool value)
    {
        if (_loading) return;
        _cfg.OrderedCharacteristicsEnabled = value;
        Save();
    }

    private void SaveCharacteristicTargets()
    {
        if (_loading) return;
        // Remplacement atomique de l'instantané lu par la boucle de dépense.
        _cfg.OrderedCharacteristicTargets = CharacteristicTargets.Select(r => r.Target).ToList();
        Save();
    }

    [RelayCommand]
    private void AddCharacteristicTarget()
    {
        var row = new CharacteristicTargetRow(new CharacteristicTarget(), SaveCharacteristicTargets);
        CharacteristicTargets.Add(row);
        SelectedCharacteristicTarget = row;
        SaveCharacteristicTargets();
    }

    [RelayCommand]
    private void RemoveCharacteristicTarget()
    {
        if (SelectedCharacteristicTarget is not { } row) return;
        CharacteristicTargets.Remove(row);
        SelectedCharacteristicTarget = null;
        SaveCharacteristicTargets();
    }

    [RelayCommand]
    private void MoveCharacteristicTargetUp() => MoveCharacteristicTarget(-1);
    [RelayCommand]
    private void MoveCharacteristicTargetDown() => MoveCharacteristicTarget(1);
    private void MoveCharacteristicTarget(int offset)
    {
        if (SelectedCharacteristicTarget is not { } row) return;
        int from = CharacteristicTargets.IndexOf(row), to = from + offset;
        if (from < 0 || to < 0 || to >= CharacteristicTargets.Count) return;
        CharacteristicTargets.Move(from, to);
        SelectedCharacteristicTarget = row;
        SaveCharacteristicTargets();
    }
}
