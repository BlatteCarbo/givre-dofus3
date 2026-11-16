using System;

namespace GivreClone.App.Services;

/// <summary>
/// Explicit boundary for MoonSharp double arguments that will become real
/// HDV order fields. Beyond 2^53-1 an integer may already be rounded in Lua;
/// never silently narrow that value into an ID, quantity or kamas price.
/// </summary>
public static class HdvLuaOrderNumeric
{
    private const double MaxExactInteger = 9_007_199_254_740_991d;

    public static bool TryPositiveLong(double? candidate, out long value)
    {
        value = 0;
        if (candidate is not double number || !double.IsFinite(number) ||
            number < 1 || number > MaxExactInteger || Math.Truncate(number) != number)
            return false;
        value = (long)number;
        return true;
    }

    public static bool TryPositiveInt(double? candidate, out int value)
    {
        value = 0;
        if (!TryPositiveLong(candidate, out long longValue) || longValue > int.MaxValue)
            return false;
        value = (int)longValue;
        return true;
    }

    public static bool TryNonNegativeLong(double? candidate, out long value)
    {
        value = 0;
        if (candidate is not double number || !double.IsFinite(number) ||
            number < 0 || number > MaxExactInteger || Math.Truncate(number) != number)
            return false;
        value = (long)number;
        return true;
    }
}
