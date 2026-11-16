using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GivreClone.App.Services;

/// <summary>
/// Dofus patch/build releases share the protocol of their major/minor family.
/// Family compatibility is not proof of server identity or a server ACK.
/// This module consumes only version metadata and normalized artifact digests.
/// No token, account, process, client file, or remote service is inspected.
/// </summary>
public static class MZP10MappingVersionAdmission
{
    public enum Disposition
    {
        MissingEvidence,
        InvalidVersion,
        DifferentProtocolFamily,
        ServerFallbackUntrusted,
        ChangedArtifacts,
        SameFamilyCandidateNeedsReplay,
        ExactBuildCandidate,
        SameFamilyCandidate,
    }

    public sealed record Evidence(string ClientVersion, string ServedBuild, bool ServerFallback,
        IReadOnlyDictionary<string, string>? Artifacts,
        string? ReferenceVersion = null,
        IReadOnlyDictionary<string, string>? ReferenceArtifacts = null);

    public sealed record Decision(Disposition Status, string SafeReason, string? BundleDigest)
    {
        // Exact metadata is only structural; never authenticates the client or
        // response. The production fetcher independently verifies its license
        // envelope, MAC and completeness independently of protocol compatibility.
        public bool ExactBuildCandidate => Status == Disposition.ExactBuildCandidate;
        public bool ProtocolFamilyCandidate => Status is Disposition.ExactBuildCandidate or Disposition.SameFamilyCandidate;
        public bool MayInject => false;
        public bool AuthenticatedServer => false;
        public bool GameplayCertified => false;
    }

    public static Decision Evaluate(Evidence? evidence)
    {
        if (evidence is null || evidence.Artifacts is null)
            return Fail(Disposition.MissingEvidence, "Mapping ou version absente.");
        if (!TryParse(evidence.ClientVersion, out _, out _, out _, out _) ||
            !TryParse(evidence.ServedBuild, out _, out _, out _, out _))
            return Fail(Disposition.InvalidVersion, "Version Dofus exacte à quatre composantes obligatoire.");
        if (!IsSameProtocolFamily(evidence.ClientVersion, evidence.ServedBuild))
            return Fail(Disposition.DifferentProtocolFamily, "Famille majeure/mineure différente : mapping refusé.");
        var digest = Digest(evidence.Artifacts);
        if (digest is null)
            return Fail(Disposition.MissingEvidence, "Artefacts proto/matching/fields manquants ou invalides.");
        if (string.Equals(evidence.ClientVersion, evidence.ServedBuild, StringComparison.Ordinal))
            return new(Disposition.ExactBuildCandidate,
                "Build exact : forme du bundle candidate uniquement, pas de preuve ACK/jeu.", digest);
        return new(Disposition.SameFamilyCandidate,
            "Même famille de protocole : mapping compatible entre patchs/builds, ACK non vérifié.", digest);
    }

    public static bool IsSameProtocolFamily(string? left, string? right) =>
        TryParse(left, out var leftMajor, out var leftMinor, out _, out _) &&
        TryParse(right, out var rightMajor, out var rightMinor, out _, out _) &&
        leftMajor == rightMajor && leftMinor == rightMinor;

    public static bool TryParse(string? version, out int major, out int minor, out int patch, out int build)
    {
        major = minor = patch = build = 0;
        if (string.IsNullOrWhiteSpace(version) || version.Length > 32 ||
            version.Any(c => !char.IsAsciiDigit(c) && c != '.')) return false;
        var parts = version.Split('.');
        if (parts.Length != 4 || parts.Any(p => p.Length is < 1 or > 9 ||
            (p.Length > 1 && p[0] == '0'))) return false;
        if (!int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor) ||
            !int.TryParse(parts[2], out patch) || !int.TryParse(parts[3], out build)) return false;
        return major > 0 && minor >= 0 && patch >= 0 && build >= 0;
    }

    public static string? Digest(IReadOnlyDictionary<string, string>? artifacts)
    {
        if (artifacts is null) return null;
        // Confirmed overrides and proto also affect business-level reads.
        // Include all four, not just count of code mappings.
        var required = new[] { "proto", "matching", "fields", "confirmed" };
        foreach (string name in required)
        {
            if (!artifacts.TryGetValue(name, out var content) || string.IsNullOrWhiteSpace(content) ||
                content.Length > 20_000_000) return null;
        }
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in required)
        {
            var content = artifacts[name];
            var bytes = Encoding.UTF8.GetBytes(content);
            var label = Encoding.ASCII.GetBytes(name + ":" + bytes.Length + ":");
            sha.AppendData(label);
            sha.AppendData(bytes);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static Decision Fail(Disposition reason, string message) => new(reason, message, null);
}
