using System;

namespace GivreClone.App.Services;

/// <summary>
/// Snapshot admission at the moment a socket session is opened. Local Cytrus
/// version alone is not gameplay evidence: it must share the protocol family
/// of an already published catalogue. Binary fingerprint drift does not change
/// that family. This is a necessary condition,
/// not independent proof of server ACK or client identity.
/// </summary>
public static class MZP12SocketVersionBinding
{
    public enum Refusal
    {
        None,
        MappingNotPublished,
        ManifestMissingOrReplaced,
        MappingBuildMismatch,
        ManifestChanged,
        ReviewedCrossPatchCandidate,
        ServerRequiredDifferentBuild
    }

    public sealed record Binding(string? MappingBuild, string? ManifestSha256,
        string? OperatorReviewedTarget = null, string? IdenticalProtoSha256 = null);
    public sealed record Decision(Refusal Reason, string SafeMessage)
    {
        // The explicitly admitted cross-patch candidate can attempt a protocol
        // diagnostic connection. It is never a statement about server ACK.
        public bool StructurallyConsistent => Reason is Refusal.None or Refusal.ReviewedCrossPatchCandidate;
        public bool IsUnverifiedCrossPatchReview => Reason == Refusal.ReviewedCrossPatchCandidate;
        public bool Authenticated => false;
        public bool ServerAck => false;
        public bool GameplayCertified => false;
    }

    public static Decision Evaluate(Binding? bound,
        MZP11CytrusVersionProbe.Result? current)
    {
        if (bound is null || string.IsNullOrWhiteSpace(bound.MappingBuild) ||
            string.IsNullOrWhiteSpace(bound.ManifestSha256))
            return new(Refusal.MappingNotPublished, "Aucun mapping lié au client installé.");

        if (current is not { HasLocalVersionEvidence: true } ||
            string.IsNullOrWhiteSpace(current.ManifestSha256))
            return new(Refusal.ManifestMissingOrReplaced,
                "Version du client installé indisponible : reconnexion bloquée.");

        if (!MZP10MappingVersionAdmission.IsSameProtocolFamily(bound.MappingBuild, current.ProtocolVersion))
        {
            return new(Refusal.MappingBuildMismatch,
                $"Mapping disponible pour {bound.MappingBuild}, jeu installé {current.ProtocolVersion}. " +
                "Correspondance du protocole non vérifiée : connexion Socket différée.");
        }

        return new(Refusal.None, "Même famille que le mapping publié ; identité/ACK non vérifiés.");
    }

    public static Decision CheckServerRequiredBuild(Binding? bound, string? required)
    {
        if (bound is null || string.IsNullOrWhiteSpace(bound.MappingBuild))
            return new(Refusal.MappingNotPublished, "Aucun mapping publié.");
        if (!MZP10MappingVersionAdmission.IsSameProtocolFamily(required, bound.MappingBuild))
            return new(Refusal.ServerRequiredDifferentBuild,
                "Le serveur exige un autre build : ne pas recycler le mapping existant.");
        return new(Refusal.None, "Version requise de la même famille que le mapping publié.");
    }

    /// <summary>
    /// A server required_version retry may change the version STRING sent on
    /// the wire, never the binaries observed by the admission probe. A session
    /// can send unrestricted gameplay commands only with the exact published
    /// mapping build. The same protocol family is diagnostic evidence only;
    /// the separately reviewed movement-only path remains restricted.
    /// This is a structural candidate only, not an ACK certificate.
    /// </summary>
    public static bool IsUnrestrictedSameBuildCandidate(Binding? bound,
        string? sessionClientVersion, bool reviewedCrossPatchSession) =>
        bound is not null &&
        string.Equals(bound.MappingBuild, sessionClientVersion, StringComparison.Ordinal);
}
