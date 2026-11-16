using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace GivreClone.App.Converters;

// Vrai si toutes les valeurs sont vraies.
public sealed class AllTrueConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture) =>
        values.Count > 0 && values.All(v => v is true);
}
