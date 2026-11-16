using System.Collections.Generic;
using System.Globalization;

namespace GivreClone.App.Services.CombatParity;

public static class CastRejectionPolicy
{
    // Server TextInformation 170 carries authoritative available/required AP.
    // Other pre-sequence refusal messages resolve the request without changing AP.
    public static bool TryRead(int messageId, IReadOnlyList<string> parameters, out int availableAp)
    {
        availableAp = 0;
        return messageId == 170 && parameters.Count == 2
            && int.TryParse(parameters[0], NumberStyles.None, CultureInfo.InvariantCulture, out availableAp)
            && int.TryParse(parameters[1], NumberStyles.None, CultureInfo.InvariantCulture, out int requiredAp)
            && availableAp >= 0 && requiredAp > availableAp;
    }
}
