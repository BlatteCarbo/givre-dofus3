using System;

namespace GivreClone.App.Services;

public enum Season { None, Halloween, Christmas }

/// <summary>Décor de fête affiché par-dessus l'interface, sans jamais prendre un clic : Halloween du 15 octobre au 2 novembre, Noël du 15 décembre au 2 janvier.</summary>
public static class SeasonalCalendar
{
    public static Season Of(DateTime date)
    {
        int m = date.Month, d = date.Day;
        if ((m == 10 && d >= 15) || (m == 11 && d <= 2)) return Season.Halloween;
        if ((m == 12 && d >= 15) || (m == 1 && d <= 2)) return Season.Christmas;
        return Season.None;
    }
}
