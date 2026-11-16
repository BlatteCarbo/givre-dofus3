using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GivreClone.App.Services;

namespace GivreClone.App.Converters;

/// <summary>genericId (int) -> Bitmap de l'icone du monstre (via Stores.MonsterIcons), ou null. Pour un Image.Source.</summary>
public sealed class MonsterIconConverter : IValueConverter
{
    public static readonly MonsterIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int gid ? Stores.MonsterIcons.Get(gid) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
