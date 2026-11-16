using System;
using System.Globalization;

namespace GivreClone.App.Services;

/// <summary>Dates LISIBLES dans la langue de l'app. Le build AOT tourne en globalisation INVARIANTE : tout format culturel
/// (« ddd », « MMM », CurrentUICulture...) sort en ANGLAIS. Jours et mois viennent donc des ressources de langue
/// (L.DayShort.D0..D6, lundi = 0 ; L.MonthShort.M1..M12) ; seuls les chiffres passent par la culture invariante.</summary>
public static class LocDate
{
    /// <summary>Jour abrégé : « mer. » / « Wed » / « mié. ».</summary>
    public static string DayShort(DateTime d)
        => Loc.T("L.DayShort.D" + (((int)d.DayOfWeek + 6) % 7).ToString(CultureInfo.InvariantCulture));

    /// <summary>Mois abrégé : « sept. » / « Sep ».</summary>
    public static string MonthShort(DateTime d)
        => Loc.T("L.MonthShort.M" + d.Month.ToString(CultureInfo.InvariantCulture));

    /// <summary>Heure « 22:10 ».</summary>
    public static string Hm(DateTime d) => d.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Jour + date sans l'année : « mer. 30 sept. ».</summary>
    public static string DayMonth(DateTime d)
        => DayShort(d) + " " + d.Day.ToString(CultureInfo.InvariantCulture) + " " + MonthShort(d);

    /// <summary>Date complète : « mer. 23 sept. 2026 ».</summary>
    public static string DayDate(DateTime d) => DayMonth(d) + " " + d.Year.ToString(CultureInfo.InvariantCulture);
}
