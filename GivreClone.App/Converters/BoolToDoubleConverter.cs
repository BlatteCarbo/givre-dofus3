using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GivreClone.App.Converters;

/// <summary>
/// bool -&gt; double : renvoie la valeur du <c>ConverterParameter</c> (double) si <c>true</c>, sinon 0.
/// Sert à animer un panneau repliable via des transitions (Opacity 0/1, MaxHeight 0/N) pilotées par un ToggleButton.
/// Ex : <c>Opacity="{Binding #tgl.IsChecked, Converter={x:Static conv:BoolToDoubleConverter.Instance}, ConverterParameter=1}"</c>.
/// </summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public static readonly BoolToDoubleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool on = value is true;
        double whenTrue = 1d;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) whenTrue = p;
        else if (parameter is double d) whenTrue = d;
        return on ? whenTrue : 0d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
