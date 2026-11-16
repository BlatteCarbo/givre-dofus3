using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>
/// Prevents publication of a partial protocol catalogue to real bot consumers.
/// This checks structural completeness only, NOT origin, client identity or wire
/// compatibility. Callers must enforce the latter independently.
/// </summary>
public static class MZP1MappingPublicationPolicy
{
    public static bool HasCompleteRequirements(int mappingCodes, int messageDefinitions,
        IReadOnlyCollection<string>? missingRequiredFields)
        => mappingCodes > 0 && messageDefinitions > 0 &&
           missingRequiredFields is { Count: 0 };
}
