using System;

namespace GivreClone.App.Services.ProtocolUpdates;

/// <summary>Pilotage complet d'une session Socket « revue inter-patch » : le reçu SHA est déjà valide (appelant) et la dernière version
/// publiée par Cytrus est EXACTEMENT celle installée. Essai opérateur : ne certifie ni codes réseau ni ACK.</summary>
public static class SocketPilotPolicy
{
    public static bool AliasesVerified(string? latestPublished, string? installed) =>
        !string.IsNullOrWhiteSpace(latestPublished) && string.Equals(latestPublished, installed, StringComparison.Ordinal);

    /// <summary>Pilotage complet d'une session inter-patch : exige la revue opérateur ET Cytrus = version installée.
    /// Un nouveau patch ou Cytrus muet referme l'accès.</summary>
    public static bool GrantsReviewedGameplay(bool reviewedCrossPatchPilot, bool aliasesVerified) =>
        reviewedCrossPatchPilot && aliasesVerified;

    /// <summary>Session inter-patch : candidat revu explicite, ou build installé admis (même famille) mais différent
    /// du mapping. Sans cela, la vérification Cytrus n'est jamais atteinte et toute session reste en lecture seule.</summary>
    public static bool IsCrossPatchSession(bool reviewedCandidate, bool admitted, string? mappingBuild, string? installed) =>
        reviewedCandidate || admitted && !string.IsNullOrWhiteSpace(installed)
            && !string.Equals(mappingBuild, installed, StringComparison.Ordinal);
}
