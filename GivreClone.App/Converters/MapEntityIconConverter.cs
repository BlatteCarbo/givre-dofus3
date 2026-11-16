using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using GivreClone.App.Services;

namespace GivreClone.App.Converters;

/// <summary>MapEntity -> icône du jeu (monstre du groupe, sinon première compétence de l'interactif), ou null. Lecture du cache des fournisseurs : aucun décodage supplémentaire.</summary>
public sealed class MapEntityIconConverter : IValueConverter
{
    public static readonly MapEntityIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MapEntity { Actor: { Monsters: { Count: > 0 } ms } } => Stores.MonsterIcons.Get(ms[0].GenericId),
        MapEntity { Interactive: { } it } => it.Skills.Select(s => Stores.InteractiveIcons.Get(s.SkillId)).FirstOrDefault(b => b is not null),
        _ => null,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
