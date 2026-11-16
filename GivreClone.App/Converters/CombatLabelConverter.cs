using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GivreClone.App.Services;

namespace GivreClone.App.Converters;

// Libelle LOCALISE d'un CombatStyle / TargetMode / CombatSpeed pour les ComboBox de l'onglet Combat (l'enum est en anglais
// cote code) -> resolu via Loc.T selon la langue courante.
public sealed class CombatLabelConverter : IValueConverter
{
    public static readonly CombatLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CombatStyle.Aggressive => Loc.T("L.Bots.Combat.Style.Aggressive"),
        CombatStyle.Fleeing => Loc.T("L.Bots.Combat.Style.Kite"),
        CombatStyle.Passive => Loc.T("L.Bots.Combat.Style.Passive"),
        TargetMode.Closest => Loc.T("L.Bots.Combat.Target.Closest"),
        TargetMode.LowestHp => Loc.T("L.Bots.Combat.Target.LowestHp"),
        TargetMode.Farthest => Loc.T("L.Bots.Combat.Target.Farthest"),
        CombatSpeed.Instant => Loc.T("L.Bots.Combat.Speed.Instant"),
        CombatSpeed.Fast => Loc.T("L.Bots.Combat.Speed.Fast"),
        CombatSpeed.Slow => Loc.T("L.Bots.Combat.Speed.Slow"),
        CombatSpeed.Turbo => Loc.T("L.Bots.Combat.Speed.Turbo"),
        LevelStatTarget.Strength => Loc.T("L.Bots.Combat.Stat.Strength"),
        LevelStatTarget.Intelligence => Loc.T("L.Bots.Combat.Stat.Intelligence"),
        LevelStatTarget.Chance => Loc.T("L.Bots.Combat.Stat.Chance"),
        LevelStatTarget.Agility => Loc.T("L.Bots.Combat.Stat.Agility"),
        LevelStatTarget.Vitality => Loc.T("L.Bots.Combat.Stat.Vitality"),
        LevelStatTarget.Wisdom => Loc.T("L.Bots.Combat.Stat.Wisdom"),
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
