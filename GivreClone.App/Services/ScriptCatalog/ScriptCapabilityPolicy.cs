namespace GivreClone.App.Services.ScriptCatalog;

/// <summary>
/// Capability declarations make the review explicit; they never grant a capability.
/// Unrecognized capabilities fail closed. Native code loading is outside the API boundary.
/// </summary>
public static class ScriptCapabilityPolicy
{
    private static readonly IReadOnlyDictionary<string, (ScriptCapabilityRisk Risk, string Explanation)> Rules =
        new Dictionary<string, (ScriptCapabilityRisk, string)>(StringComparer.Ordinal)
        {
            ["world.read"] = (ScriptCapabilityRisk.Observation, "Read an immutable world snapshot"),
            ["map.read"] = (ScriptCapabilityRisk.Observation, "Read known map metadata"),
            ["inventory.read"] = (ScriptCapabilityRisk.Observation, "Read an inventory snapshot"),
            ["quest.read"] = (ScriptCapabilityRisk.Observation, "Read known quest progress"),
            ["ui.overlay"] = (ScriptCapabilityRisk.Observation, "Propose overlay data for rendering by Givre"),
            ["movement.request"] = (ScriptCapabilityRisk.RequiresGrant, "Request movement after runtime validation"),
            ["gather.request"] = (ScriptCapabilityRisk.RequiresGrant, "Request gathering after runtime validation"),
            ["combat.request"] = (ScriptCapabilityRisk.RequiresGrant, "Request a combat action after runtime validation"),
            ["economy.market"] = (ScriptCapabilityRisk.RequiresGrant, "Market operations require separate review"),
            ["bank.write"] = (ScriptCapabilityRisk.RequiresGrant, "Bank operations require separate review"),
            ["network.http"] = (ScriptCapabilityRisk.RequiresGrant, "External HTTP access requires separate review"),
            ["filesystem.write"] = (ScriptCapabilityRisk.Denied, "Arbitrary filesystem writes are not a script capability"),
            ["process.launch"] = (ScriptCapabilityRisk.Denied, "Launching host processes is prohibited"),
            ["client.inject"] = (ScriptCapabilityRisk.Denied, "Client injection is outside the Givre script API"),
            ["runtime.native"] = (ScriptCapabilityRisk.Denied, "Native library loading is prohibited")
        };

    public static ScriptCapabilityReview Review(string? name)
    {
        if (string.IsNullOrEmpty(name) || !Rules.TryGetValue(name, out var rule))
            return new ScriptCapabilityReview(name ?? "<null>", ScriptCapabilityRisk.Denied, "Unknown or malformed capability");
        return new ScriptCapabilityReview(name, rule.Risk, rule.Explanation);
    }
}

/// <summary>
/// Future *host-side* adapter contract. Signatures are design metadata, not callable bindings.
/// An implementation must gate each call again against explicit user grants and game evidence.
/// </summary>
public static class GivreScriptApiV1
{
    public const string Version = "1.0";

    public static IReadOnlyList<ScriptApiSignature> Signatures { get; } =
    [
        new("world.snapshot()", "world.read", "Immutable visible world state"),
        new("map.resources()", "map.read", "Known resource markers, with provenance"),
        new("inventory.snapshot()", "inventory.read", "Immutable inventory view"),
        new("quest.progress()", "quest.read", "Available quest progress"),
        new("overlay.publish(markers)", "ui.overlay", "Request Givre-side highlight or mini-map markers"),
        new("movement.request(target)", "movement.request", "Validated movement request (future adapter)"),
        new("gather.request(resource)", "gather.request", "Validated gather request (future adapter)")
    ];
}

public sealed record ScriptApiSignature(string Signature, string Capability, string Description);
