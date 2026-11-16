using System.Text.RegularExpressions;

namespace GivreClone.App.Services.ItemCraftMcp;

/// <summary>
/// Descriptive manifest evidence; NOT a substitute for independent rights
/// verification, ownership review or a cryptographic hash check at import time.
/// </summary>
public sealed record ItemCraftMcpEvidence(
    string SourceKind,
    string SourceId,
    string GameVersion,
    string ManifestSha256,
    bool AiDisclosureAuthorized = false,
    string? AuthorizationEvidenceId = null)
{
    private static readonly Regex SafeLabel = new(
        @"\A[A-Za-z0-9_.:-]{1,80}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SafeSha256 = new(
        @"\A[a-fA-F0-9]{64}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SafeVersion = new(
        @"\A(?:0|[1-9][0-9]{0,8})(?:\.(?:0|[1-9][0-9]{0,8})){3}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool CanDisclose()
    {
        if (!AiDisclosureAuthorized ||
            !SafeLabel.IsMatch(SourceKind ?? "") ||
            !SafeLabel.IsMatch(SourceId ?? "") ||
            !SafeLabel.IsMatch(AuthorizationEvidenceId ?? "") ||
            !SafeSha256.IsMatch(ManifestSha256 ?? "") ||
            !SafeVersion.IsMatch(GameVersion ?? ""))
            return false;

        // Public DofusDB scraping permission does not establish an AI/MCP grant.
        // Also reject disguised spelling in source labels.
        static bool IsDofusDb(string text)
        {
            var letters = new string(text.Where(char.IsAsciiLetterOrDigit).ToArray());
            return letters.Contains("dofusdb", StringComparison.OrdinalIgnoreCase);
        }
        return !IsDofusDb(SourceKind) && !IsDofusDb(SourceId);
    }
}

public enum ItemCraftMcpTargetKind { Item, Set }

/// <summary>Identifiers and membership are imported from a reviewed local manifest.</summary>
public sealed record ItemCraftMcpTarget(
    ItemCraftMcpTargetKind Kind,
    long Id,
    string Name,
    IReadOnlyList<long> PieceGids,
    ItemCraftMcpEvidence Evidence,
    bool Verified = false);
