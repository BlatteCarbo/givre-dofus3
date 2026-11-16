// Minimal only-available-in-tests stand-ins. The REAL MappingCatalog.cs
// implementation is linked by the harness and is not reimplemented here.
namespace GivreClone.App.Services
{
    public sealed class TestBundle
    {
        public bool NotModified { get; init; }
        public string Build { get; init; } = "fixture-build";
        public int MappingVersion { get; init; } = 1;
        public bool Fallback { get; init; }
        public IReadOnlyDictionary<string, string> Artefacts { get; init; } =
            new Dictionary<string, string> { ["proto"] = "yes", ["matching"] = "mapping", ["fields"] = "all" };
    }

    public static class LicenseService
    {
        public static bool SigningBlocked { get; set; }
        public static Func<Task<TestBundle?>> Fetch { get; set; } = () => Task.FromResult<TestBundle?>(new TestBundle());
        public static Task<TestBundle?> FetchMappingAsync(string requestedBuild) => Fetch();
    }

    public static class Logger
    {
        public static List<string> Entries { get; } = new();
        public static void Info(string message) => Entries.Add(message);
        public static void Error(string message) => Entries.Add(message);
    }
}

namespace GivreClone.Protocol
{
    public sealed class ProtocolCatalog
    {
        public string FieldNumber { get; init; } = "all";
        public int MappingCount { get; init; } = 2;
        public int MessageDefCount { get; init; } = 2;
        public ProtocolCatalog? Fallback { get; set; }
        public static ProtocolCatalog LoadFromStrings(string? proto, string? matching, string? confirmed, string? fields)
        {
            if (fields == "empty") return new ProtocolCatalog { FieldNumber = "all", MappingCount = 0, MessageDefCount = 0 };
            return new ProtocolCatalog { FieldNumber = fields ?? "missing" };
        }
        public static ProtocolCatalog LoadFromDirectory(string path) => new();
        public string? CodeForName(string name) => "fixture-code";
    }

    public static class RequiredMapping
    {
        public static readonly string[] Scanned = ["GameMessage.uid"];
        public static readonly string[] Extra = ["GameMessage.request"];
        public static IReadOnlyList<string> MissingIn(string fieldNumber) => fieldNumber == "all"
            ? Array.Empty<string>() : ["GameMessage.request"];
    }
}

namespace GivreLauncher.Core.Transport
{
    public static class TransportLog
    {
        public static void Line(string line) { }
    }
}
