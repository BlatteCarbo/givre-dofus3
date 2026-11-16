using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace GivreClone.App.Converters;

// Profondeur d'un nœud -> marge gauche (indentation) dans la liste plate de l'inspecteur (gros message).
public sealed class DepthToMarginConverter : IValueConverter
{
    public double Step { get; set; } = 16;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new Thickness(value is int d ? d * Step : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
