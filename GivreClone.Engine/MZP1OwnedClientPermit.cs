using System.Security.Cryptography;

namespace GivreClone.Engine;

/// <summary>
/// One explicitly owned Dofus process, provided by the operator's independent
/// session authority. The permit is not proof of ownership by itself: the
/// capture host also rechecks PID birth, executable path and SHA before action.
/// Never derive this permit from a generic 'Dofus.exe' process enumeration.
/// </summary>
public sealed record MZP1OwnedClientPermit(int Pid, long ProcessStartUtcTicks,
    string ClientBuild, string ExactExecutablePath, string ExecutableSha256)
{
    public bool IsWellFormed => Pid > 0 && ProcessStartUtcTicks > 0 &&
        string.Equals(ClientBuild, "3.7.10.10", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(ExactExecutablePath) &&
        Path.IsPathFullyQualified(ExactExecutablePath) &&
        ExecutableSha256 is { Length: 64 } &&
        ExecutableSha256.All(Uri.IsHexDigit);

    internal bool MatchesExecutableHash(string path)
    {
        if (!IsWellFormed || !string.Equals(path, ExactExecutablePath,
            StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            byte[] expected = Convert.FromHexString(ExecutableSha256);
            using var input = File.OpenRead(path);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(input), expected);
        }
        catch { return false; }
    }
}
