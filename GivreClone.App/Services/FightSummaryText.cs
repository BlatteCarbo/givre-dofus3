using System;
using System.Globalization;

namespace GivreClone.App.Services;

public static class FightSummaryText
{
    public static string Format(int outcome, bool decoded, long seconds, int? turns, Func<string, string> text)
    {
        string key = outcome switch
        {
            2 => "L.GameLog.FightWon",
            0 when decoded => "L.GameLog.FightLost",
            1 => "L.GameLog.FightDraw",
            _ => "L.GameLog.FightOver"
        };
        string result = string.Format(CultureInfo.InvariantCulture, text(key), seconds,
            text(seconds == 1 ? "L.GameLog.Second" : "L.GameLog.Seconds"));
        if (turns is > 0)
            result += string.Format(CultureInfo.InvariantCulture, text("L.GameLog.FightTurns"), turns,
                text(turns == 1 ? "L.GameLog.Turn" : "L.GameLog.Turns"));
        return result;
    }
}
