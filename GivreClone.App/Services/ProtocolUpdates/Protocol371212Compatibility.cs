namespace GivreClone.App.Services.ProtocolUpdates;

/// <summary>
/// A conservative runtime admission policy for the specific 3.7.12.12 update.
/// Metadata-only Cytrus discovery or version-family equality never grants SAFE.
/// </summary>
public enum Protocol371212Status { SAFE, REVIEW, BLOCK }

/// <summary>
/// Evidence supplied by separately verified components. Flags labelled "verified"
/// must only be set after independently checking version, hashes and runtime
/// receipts, not from user text or untrusted JSON alone.
/// </summary>
public sealed record Protocol371212Evidence(
    string? TargetVersion,
    string? InstalledVersion,
    string? BundleVersion,
    string? ReferenceVersion,
    bool OfficialVersionAdvertised,
    bool TargetProtoManifestAndShaVerified,
    bool BaselineProtoManifestAndShaVerified,
    bool TargetClientBinariesMatchManifest,
    bool ExactTrustedTargetBinaryBaselineMatch,
    bool IndependentWireDispatchVerified,
    bool IndependentSerializationRoundTripVerified,
    bool IndependentServerAckVerified,
    bool IndependentEvidenceAdmittedByTrustedGate);

public sealed record Protocol371212Decision(
    Protocol371212Status Status,
    string Message,
    bool CanUseRuntime,
    bool CanInspectOffline,
    bool ProtoChangedProven)
{
    public bool MayUpgradeAutomatically => false;
    public bool MayPromoteCodecAutomatically => false;
}

public static class Protocol371212Compatibility
{
    public const string TargetBuild = "3.7.12.12";
    public const string ReferenceBuild = "3.7.11.11";

    /// <summary>
    /// SAFE only when a trusted independent runtime proof is bound to the exact
    /// installed target AND both candidate extractions have been verified.
    /// An absent/uninstalled target is BLOCK for runtime but still permits a
    /// human offline review of public metadata.
    /// </summary>
    public static Protocol371212Decision Evaluate(Protocol371212Evidence? evidence)
    {
        if (evidence is null || !string.Equals(evidence.TargetVersion, TargetBuild, StringComparison.Ordinal))
            return Block("Version cible absente ou différente de 3.7.12.12.", false);

        if (!evidence.OfficialVersionAdvertised)
            return Block("Version cible officielle non confirmée par une source indépendante.", false);

        if (!string.Equals(evidence.InstalledVersion, TargetBuild, StringComparison.Ordinal))
            return Block("3.7.12.12 non installée : compatibilité runtime indéterminée, activation bloquée.", true);

        if (!string.Equals(evidence.BundleVersion, TargetBuild, StringComparison.Ordinal))
            return Block("Le bundle actif ne cible pas exactement 3.7.12.12.", true);

        if (!string.Equals(evidence.ReferenceVersion, ReferenceBuild, StringComparison.Ordinal) ||
            !evidence.TargetProtoManifestAndShaVerified ||
            !evidence.BaselineProtoManifestAndShaVerified ||
            !evidence.TargetClientBinariesMatchManifest)
            return Block("Manifestes, références ou SHA-256 cible insuffisants pour comparer le protocole.", true);

        if (!evidence.ExactTrustedTargetBinaryBaselineMatch ||
            !evidence.IndependentWireDispatchVerified ||
            !evidence.IndependentSerializationRoundTripVerified ||
            !evidence.IndependentServerAckVerified ||
            !evidence.IndependentEvidenceAdmittedByTrustedGate)
            return new(Protocol371212Status.REVIEW,
                "Sources locales contrôlées mais identité fiable et preuves dispatch/wire/ACK insuffisantes : revue manuelle.",
                false, true, false);

        return new(Protocol371212Status.SAFE,
            "Admissible uniquement pour la cible et les reçus runtime indépendants certifiés. Aucune mise à niveau automatique.",
            true, true, false);
    }

    private static Protocol371212Decision Block(string message, bool offlineReview) =>
        new(Protocol371212Status.BLOCK, message, false, offlineReview, false);
}
